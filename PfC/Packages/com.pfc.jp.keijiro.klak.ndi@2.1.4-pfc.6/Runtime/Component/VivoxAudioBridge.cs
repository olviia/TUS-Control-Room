using System;
using System.IO;
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

        [Tooltip("Enable audio recording to file for debugging")]
        public bool enableAudioRecording = true;

        [Tooltip("Bypass ring buffer and send directly to NdiSender")]
        public bool bypassRingBuffer = true;

        [Tooltip("NdiSender to send audio to when bypassing ring buffer")]
        public NdiSender ndiSenderDirect;

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

        // Audio recording for debugging
        private float _recordingTimer = 0f;
        private float _recordingCooldown = 15f;
        private bool _isRecording = false;
        private float _recordingDuration = 0f;
        private const float MAX_RECORDING_DURATION = 10f;
        private System.Collections.Generic.List<float> _recordedSamples;
        private int _recordingNumber = 0;

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

        private float _debugTimerForRecording = 0f;

        private void Update()
        {
            if (_audioSource == null || _audioSource.clip == null)
            {
                // Debug every 5 seconds if clip is not available
                _debugTimerForRecording += Time.deltaTime;
                if (_debugTimerForRecording >= 5f)
                {
                    Debug.LogWarning($"[VivoxAudioBridge] No AudioClip available yet. AudioSource: {_audioSource != null}, Clip: {(_audioSource?.clip != null)}");
                    _debugTimerForRecording = 0f;
                }
                return;
            }

            // Update AudioClip reference if it changed
            if (_audioClip != _audioSource.clip)
            {
                _audioClip = _audioSource.clip;
                _channels = _audioClip.channels;
                _lastReadPosition = 0;
                Debug.Log($"[VivoxAudioBridge] AudioClip updated: {_audioClip.name}, channels: {_channels}, samples: {_audioClip.samples}");
            }

            // Recording timer for debugging
            if (enableAudioRecording)
            {
                _recordingTimer += Time.deltaTime;

                // Debug every 5 seconds
                _debugTimerForRecording += Time.deltaTime;
                if (_debugTimerForRecording >= 5f)
                {
                    Debug.Log($"[VivoxAudioBridge] Recording status: timer={_recordingTimer:F1}s, isRecording={_isRecording}, cooldown={_recordingCooldown}s");
                    _debugTimerForRecording = 0f;
                }

                if (!_isRecording && _recordingTimer >= _recordingCooldown)
                {
                    // Start new recording
                    _isRecording = true;
                    _recordingDuration = 0f;
                    _recordedSamples = new System.Collections.Generic.List<float>();
                    Debug.Log($"[VivoxAudioBridge] ⏺️ Started recording #{_recordingNumber}");
                }

                if (_isRecording)
                {
                    _recordingDuration += Time.deltaTime;
                    if (_recordingDuration >= MAX_RECORDING_DURATION)
                    {
                        // Stop recording and save
                        Debug.Log($"[VivoxAudioBridge] ⏹️ Stopping recording #{_recordingNumber}, saving...");
                        SaveRecordingToFile();
                        _isRecording = false;
                        _recordingTimer = 0f;
                        _recordingNumber++;
                    }
                }
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

            // Read from AudioClip using GetData
            _audioClip.GetData(_tempBuffer, _lastReadPosition);

            Debug.Log($"[VivoxAudioBridge] GetData read {samplesToRead} samples from position {_lastReadPosition}, total floats: {totalFloats}, channels: {_channels}");

            // Record samples if recording is active
            if (_isRecording && _recordedSamples != null)
            {
                for (int i = 0; i < totalFloats; i++)
                {
                    _recordedSamples.Add(_tempBuffer[i]);
                }
            }

            // If bypassing ring buffer, send directly to NdiSender
            if (bypassRingBuffer && ndiSenderDirect != null)
            {
                // Create a properly sized array with only the data we read
                float[] directData = new float[totalFloats];
                Array.Copy(_tempBuffer, 0, directData, 0, totalFloats);
                ndiSenderDirect.SendVivoxAudioData(directData, _channels, _sampleRate);
                Debug.Log($"[VivoxAudioBridge] Direct send to NDI: {totalFloats} floats, {_channels}ch");
            }
            else
            {
                // Write to ring buffer (normal path)
                WriteToRing(_tempBuffer, totalFloats);
            }

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

        private void SaveRecordingToFile()
        {
            if (_recordedSamples == null || _recordedSamples.Count == 0)
            {
                Debug.LogWarning("[VivoxAudioBridge] No samples recorded");
                return;
            }

            string filename = $"VivoxAudio_Recording_{_recordingNumber}_{System.DateTime.Now:yyyyMMdd_HHmmss}.raw";
            string filepath = Path.Combine(Application.persistentDataPath, filename);

            try
            {
                using (FileStream fs = new FileStream(filepath, FileMode.Create))
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    // Write header info as text
                    string header = $"// Sample Rate: {_sampleRate}, Channels: {_channels}, Total Samples: {_recordedSamples.Count}\n";
                    byte[] headerBytes = System.Text.Encoding.UTF8.GetBytes(header);
                    writer.Write(headerBytes);

                    // Write raw float data
                    foreach (float sample in _recordedSamples)
                    {
                        writer.Write(sample);
                    }
                }

                Debug.Log($"[VivoxAudioBridge] Saved recording to: {filepath}");
                Debug.Log($"[VivoxAudioBridge] Recorded {_recordedSamples.Count} samples ({_recordedSamples.Count / _channels} frames), {_sampleRate}Hz, {_channels}ch");
                Debug.Log($"[VivoxAudioBridge] Duration: {(_recordedSamples.Count / _channels) / (float)_sampleRate:F2} seconds");
            }
            catch (Exception e)
            {
                Debug.LogError($"[VivoxAudioBridge] Failed to save recording: {e.Message}");
            }
        }
    }
}