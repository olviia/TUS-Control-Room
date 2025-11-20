// csharp
using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Klak.Ndi
{
    public class AudioListenerBridge : MonoBehaviour
    {
        // External callback (unchanged)
        private static object _lock = new object();
        private static Action<float[], int> _onAudioFilterReadEvent;
        public static Action<float[], int> OnAudioFilterReadEvent
        {
            get { lock (_lock) { return _onAudioFilterReadEvent; } }
            set { lock (_lock) { _onAudioFilterReadEvent = value; } }
        }

        // Ring buffer (intermediate audio storage)
        private const int BufferLengthMS = 200;
        private float[] _ringBuffer;
        private int _writeIndex;
        private int _availableSamples;
        private int _channels = 2;
        private int _sampleRate = 48000;

        // Tracked read position for continuous consumption
        private int _readIndex;
        private bool _readStarted;

        // Diagnostic timed capture (kept, now reads from ring buffer)
        private static bool _isCapturing = false;
        private static float _captureTimer = 0f;
        private const float CAPTURE_INTERVAL = 20f;
        private const float CAPTURE_DURATION = 5f;

        // Per-frame analysis control
        private const float JumpThreshold = 0.1f;
        private const bool LogPerFrameDiscontinuities = false;

        private void Start()
        {
            if (!Application.isPlaying) return;
            _sampleRate = AudioSettings.outputSampleRate;
            // Allocate ring buffer: samples = sampleRate * channels * (ms/1000)
            int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
            _ringBuffer = new float[Math.Max(capacity, 1)];
            _writeIndex = 0;
            _availableSamples = 0;
            Debug.Log($"[AudioListenerBridge] Init sampleRate={_sampleRate}Hz channels={_channels} ringCapacity={_ringBuffer.Length}");
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            _captureTimer += Time.deltaTime;
            if (_captureTimer >= CAPTURE_INTERVAL && !_isCapturing)
            {
                _captureTimer = 0f;
                _isCapturing = true;
                Debug.Log("[AudioListenerBridge] Starting diagnostic capture (ring snapshot)...");
            }

            if (_isCapturing && _captureTimer >= CAPTURE_DURATION)
            {
                _isCapturing = false;
                _captureTimer = 0f;
                WriteCapturedAudio();
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;

            // Write incoming frame into ring buffer
            WriteToRing(data);

            // Create buffer for reading from ring
            float[] bufferedData = new float[data.Length];
            bool hasBufferedData = ReadFromRing(bufferedData);

            // Optional per-frame discontinuity analysis on buffered data
            if (LogPerFrameDiscontinuities && hasBufferedData)
                AnalyzeFrame(bufferedData);

            // Forward buffered data to external listener (NDI callback)
            lock (_lock)
            {
                if (hasBufferedData)
                {
                    // Send buffered data to NDI (not original data!)
                    _onAudioFilterReadEvent?.Invoke(bufferedData, channels);
                }
                else
                {
                    // Still filling buffer, send original data temporarily
                    _onAudioFilterReadEvent?.Invoke(data, channels);
                }
            }
        }

        private void WriteToRing(float[] data)
        {
            if (_ringBuffer == null) return;
            int capacity = _ringBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                _ringBuffer[_writeIndex] = data[i];
                _writeIndex = (_writeIndex + 1) % capacity;
            }
            _availableSamples = Math.Min(_availableSamples + data.Length, capacity);
        }

        private bool ReadFromRing(float[] outputBuffer)
        {
            if (_ringBuffer == null) return false;

            int capacity = _ringBuffer.Length;

            // Initial setup: establish 100ms delay once buffer is half-full
            if (!_readStarted && _availableSamples >= capacity / 2)
            {
                int delay = capacity / 2;
                _readIndex = (_writeIndex - delay + capacity) % capacity;
                _readStarted = true;
                Debug.Log($"[AudioListenerBridge] Reading started at index {_readIndex}, write at {_writeIndex} (delay={delay} samples, {delay/(float)(_sampleRate*_channels)*1000:F1}ms)");
            }

            if (!_readStarted) return false;  // Still filling initial buffer

            // Check if enough samples available between read and write positions
            int available = (_writeIndex - _readIndex + capacity) % capacity;
            if (available < outputBuffer.Length)
            {
                Debug.LogWarning($"[AudioListenerBridge] Buffer underrun! Available={available}, Need={outputBuffer.Length}");
                return false;
            }

            // Read from tracked position (continuous consumption!)
            for (int i = 0; i < outputBuffer.Length; i++)
            {
                outputBuffer[i] = _ringBuffer[_readIndex];
                _readIndex = (_readIndex + 1) % capacity;
            }

            return true;
        }

        private void WriteCapturedAudio()
        {
            if (_availableSamples == 0)
            {
                Debug.LogWarning("[AudioListenerBridge] No audio captured (ring empty)");
                return;
            }

            // Snapshot oldest->newest
            float[] snapshot = new float[_availableSamples];
            int capacity = _ringBuffer.Length;
            int readStart = (_writeIndex - _availableSamples + capacity) % capacity;
            for (int i = 0; i < _availableSamples; i++)
                snapshot[i] = _ringBuffer[(readStart + i) % capacity];

            Thread writeThread = new Thread(() =>
            {
                try
                {
                    string filename = $"AudioBridge_Simple_{DateTime.Now:HHmmss}.raw";
                    using (var fs = new FileStream(filename, FileMode.Create))
                    using (var bw = new BinaryWriter(fs))
                    {
                        for (int i = 0; i < snapshot.Length; i++)
                            bw.Write(snapshot[i]);
                    }

                    Debug.Log($"[AudioListenerBridge] Wrote {snapshot.Length} samples -> {filename}");
                    float durationSec = snapshot.Length / (float)(_channels * _sampleRate);
                    Debug.Log($"[AudioListenerBridge] Duration: {durationSec:F2}s");

                    AnalyzeCorruption(snapshot);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AudioListenerBridge] Write error: {ex.Message}");
                }
            });

            writeThread.IsBackground = true;
            writeThread.Start();
        }

        // Frame-level quick discontinuity check (not cumulative)
        private void AnalyzeFrame(float[] data)
        {
            if (data == null || data.Length < 2) return;
            int jumps = 0;
            float maxJump = 0f;
            float prev = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                float diff = Mathf.Abs(data[i] - prev);
                if (diff > JumpThreshold)
                {
                    jumps++;
                    if (diff > maxJump) maxJump = diff;
                }
                prev = data[i];
            }
            if (jumps > 0)
                Debug.Log($"[AudioListenerBridge] Frame jumps={jumps} max={maxJump:F4}");
        }

        // Post-capture aggregate analysis
        private void AnalyzeCorruption(float[] data)
        {
            if (data.Length < 2) return;
            int largeJumps = 0;
            float maxJump = 0f;

            for (int i = 1; i < data.Length; i++)
            {
                float diff = Mathf.Abs(data[i] - data[i - 1]);
                if (diff > JumpThreshold)
                {
                    largeJumps++;
                    if (diff > maxJump) maxJump = diff;
                }
            }

            float percent = largeJumps / (float)data.Length * 100f;
            Debug.Log("[AudioListenerBridge] Corruption analysis:");
            Debug.Log($"  Large jumps (>{JumpThreshold}): {largeJumps} / {data.Length} ({percent:F2}%)");
            Debug.Log($"  Max jump: {maxJump:F4}");

            if (percent > 0.1f)
                Debug.LogWarning($"  WARNING: {percent:F2}% discontinuities");
            else
                Debug.Log("  Audio appears clean");
        }
    }
}
