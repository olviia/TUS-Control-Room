using System;
using System.IO;
using UnityEngine;

namespace Klak.Ndi
{
    /// <summary>
    /// Bridge component that captures audio from Vivox AudioSource using OnAudioFilterRead
    /// Attach this to the same GameObject as VivoxParticipantTap
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VivoxAudioBridge : MonoBehaviour
    {
        [Tooltip("Bridge ID for NdiSender to identify this audio source")]
        public int bridgeId = 0;

        [Tooltip("Enable audio recording to file for debugging")]
        public bool enableAudioRecording = false;

        [Tooltip("Bypass ring buffer and send directly to NdiSender")]
        public bool bypassRingBuffer = false;

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

        // Drift compensation (like the FMOD solution)
        private const int TARGET_LATENCY_MS = 50;
        private const int DRIFT_THRESHOLD_MS = 1;
        private uint _totalSamplesWritten = 0;
        private uint _totalSamplesRead = 0;
        private int _targetLatencySamples;
        private int _driftThresholdSamples;
        private float _actualLatency; // Smoothed latency tracking

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

            // Initialize drift compensation
            _targetLatencySamples = (_sampleRate * TARGET_LATENCY_MS) / 1000;
            _driftThresholdSamples = (_sampleRate * DRIFT_THRESHOLD_MS) / 1000;
            _actualLatency = _targetLatencySamples;

            // Register with NdiSender
            NdiSender.RegisterVivoxAudioBridge(this);
            Debug.Log($"[VivoxAudioBridge] Initialized with bridge ID {bridgeId}, target latency: {TARGET_LATENCY_MS}ms ({_targetLatencySamples} samples)");
        }

        private void OnDestroy()
        {
            NdiSender.UnregisterVivoxAudioBridge(this);
        }

        private float _debugTimerForRecording = 0f;

        /// <summary>
        /// OnAudioFilterRead is called by Unity's audio thread with the actual audio being played
        /// This is the CORRECT way to capture audio from AudioSource in real-time
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;

            // Record samples if recording is active
            if (enableAudioRecording)
            {
                if (!_isRecording && _recordingTimer >= _recordingCooldown)
                {
                    _isRecording = true;
                    _recordingDuration = 0f;
                    _recordedSamples = new System.Collections.Generic.List<float>();
                    Debug.Log($"[VivoxAudioBridge] ⏺️ Started recording #{_recordingNumber}");
                }

                if (_isRecording)
                {
                    for (int i = 0; i < data.Length; i++)
                    {
                        _recordedSamples.Add(data[i]);
                    }
                }
            }

            // If bypassing ring buffer, send directly to NdiSender
            if (bypassRingBuffer && ndiSenderDirect != null)
            {
                ndiSenderDirect.SendVivoxAudioData(data, channels, _sampleRate);
            }
            else
            {
                // Write to ring buffer (normal path)
                WriteToRing(data, data.Length);
            }
        }

        private void Update()
        {
            // Recording timer for debugging
            if (enableAudioRecording)
            {
                _recordingTimer += Time.deltaTime;

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

                // Track total samples written for drift compensation
                _totalSamplesWritten += (uint)length;
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

                // Drift compensation: Calculate actual latency and adjust read amount
                int latency = (int)_totalSamplesWritten - (int)_totalSamplesRead;
                _actualLatency = 0.97f * _actualLatency + 0.03f * latency; // Smooth latency tracking

                // Adjust how much we read based on drift
                int samplesToRead = available;
                if (_actualLatency < _targetLatencySamples - _driftThresholdSamples)
                {
                    // Buffer too empty - read less (slow down consumption)
                    samplesToRead = Math.Max(available / 2, 1);
                }
                else if (_actualLatency > _targetLatencySamples + _driftThresholdSamples)
                {
                    // Buffer too full - read more (speed up consumption)
                    samplesToRead = available; // Read everything available
                }
                else
                {
                    // Within threshold - read normal amount (target latency worth)
                    samplesToRead = Math.Min(available, _targetLatencySamples);
                }

                // Create array with adjusted amount
                audioData = new float[samplesToRead];

                // Copy from ring buffer
                for (int i = 0; i < samplesToRead; i++)
                {
                    audioData[i] = _ringBuffer[(_ndiReadIndex + i) % capacity];
                }

                // Update NdiSender read index and tracking
                _ndiReadIndex = (_ndiReadIndex + samplesToRead) % capacity;
                _totalSamplesRead += (uint)samplesToRead;

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