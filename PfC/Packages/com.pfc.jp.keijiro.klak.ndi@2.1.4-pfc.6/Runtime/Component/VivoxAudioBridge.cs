using System;
using UnityEngine;

namespace Klak.Ndi
{
    /// <summary>
    /// Bridge component that reads audio from Vivox's AudioClip and provides it to NdiSender
    /// Attach this to the same GameObject as VivoxParticipantTap
    /// </summary>
    public class VivoxAudioBridge : MonoBehaviour
    {
        [Tooltip("Bridge ID for NdiSender to identify this audio source")]
        public int bridgeId = 0;

        // Ring buffer for audio accumulation
        private const int BufferLengthMS = 200;
        private float[] _ringBuffer;
        private int _writeIndex;
        private int _availableSamples;
        private int _channels = 2;
        private int _sampleRate = 48000;
        private readonly object _bufferAccessLock = new object();

        // Separate read position for NdiSender
        private int _ndiReadIndex;
        private bool _ndiReadStarted;

        // AudioSource and AudioClip references
        private AudioSource _audioSource;
        private AudioClip _audioClip;
        private int _lastReadPosition;
        private float[] _tempBuffer;

        public int BridgeId => bridgeId;

        private void Start()
        {
            if (!Application.isPlaying) return;

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                Debug.LogError("[VivoxAudioBridge] No AudioSource found! This component must be on the same GameObject as VivoxParticipantTap.");
                enabled = false;
                return;
            }

            _sampleRate = AudioSettings.outputSampleRate;

            // Allocate ring buffer
            int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
            _ringBuffer = new float[Math.Max(capacity, 1)];
            _writeIndex = 0;
            _availableSamples = 0;
            _lastReadPosition = 0;

            // Register with NdiSender
            NdiSender.RegisterVivoxAudioBridge(this);
            Debug.Log($"[VivoxAudioBridge] Initialized with bridge ID {bridgeId}");
        }

        private void OnDestroy()
        {
            NdiSender.UnregisterVivoxAudioBridge(this);
        }

        private void Update()
        {
            if (_audioSource == null || _audioSource.clip == null) return;

            // Update AudioClip reference if it changed
            if (_audioClip != _audioSource.clip)
            {
                _audioClip = _audioSource.clip;
                _channels = _audioClip.channels;
                _lastReadPosition = 0;
                Debug.Log($"[VivoxAudioBridge] AudioClip updated: {_audioClip.name}, channels: {_channels}");
            }

            // Read new audio data from the AudioClip
            ReadAudioFromClip();
        }

        private void ReadAudioFromClip()
        {
            if (_audioClip == null || !_audioSource.isPlaying) return;

            // Get current playback position
            int currentPosition = _audioSource.timeSamples;

            // Calculate how many samples to read
            int totalSamples = _audioClip.samples;
            int samplesToRead = 0;

            if (currentPosition >= _lastReadPosition)
            {
                samplesToRead = currentPosition - _lastReadPosition;
            }
            else
            {
                // Wrapped around (looping AudioClip)
                samplesToRead = (totalSamples - _lastReadPosition) + currentPosition;
            }

            if (samplesToRead <= 0) return;

            // Limit to reasonable chunk size
            samplesToRead = Math.Min(samplesToRead, _sampleRate / 10); // Max 100ms at a time

            // Allocate temp buffer if needed
            int totalFloats = samplesToRead * _channels;
            if (_tempBuffer == null || _tempBuffer.Length < totalFloats)
            {
                _tempBuffer = new float[totalFloats];
            }

            // Read from AudioClip
            _audioClip.GetData(_tempBuffer, _lastReadPosition);

            // Write to ring buffer
            WriteToRing(_tempBuffer, totalFloats);

            // Update last read position
            _lastReadPosition = currentPosition;
        }

        private void WriteToRing(float[] data, int length)
        {
            if (_ringBuffer == null) return;

            lock (_bufferAccessLock)
            {
                int capacity = _ringBuffer.Length;
                for (int i = 0; i < length; i++)
                {
                    _ringBuffer[_writeIndex] = data[i];
                    _writeIndex = (_writeIndex + 1) % capacity;
                }
                _availableSamples = Math.Min(_availableSamples + length, capacity);
            }
        }

        /// <summary>
        /// Get accumulated audio data for NdiSender
        /// </summary>
        public bool GetAccumulatedAudio(out float[] audioData, out int channels)
        {
            audioData = null;
            channels = _channels;

            lock (_bufferAccessLock)
            {
                if (_ringBuffer == null)
                {
                    return false;
                }

                int capacity = _ringBuffer.Length;

                // Initialize NdiSender read position on first call (when buffer is half full)
                if (!_ndiReadStarted && _availableSamples >= capacity / 2)
                {
                    int delay = capacity / 2;
                    _ndiReadIndex = (_writeIndex - delay + capacity) % capacity;
                    _ndiReadStarted = true;
                    Debug.Log($"[VivoxAudioBridge] Started reading with {delay} samples delay");
                }

                if (!_ndiReadStarted)
                {
                    return false;
                }

                int available = (_writeIndex - _ndiReadIndex + capacity) % capacity;

                if (available <= 0)
                {
                    return false;
                }

                // Create array with accumulated data
                audioData = new float[available];

                // Copy from ring buffer
                for (int i = 0; i < available; i++)
                {
                    audioData[i] = _ringBuffer[(_ndiReadIndex + i) % capacity];
                }

                // Update NdiSender read index
                _ndiReadIndex = (_ndiReadIndex + available) % capacity;

                return true;
            }
        }

        public void SetBridgeId(int id)
        {
            bridgeId = id;
        }
    }
}