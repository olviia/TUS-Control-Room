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

        // Public method for NdiSender to get accumulated buffered audio (uses separate read index)
        private readonly object _bufferAccessLock = new object();
        public bool GetAccumulatedAudio(out float[] audioData, out int channels)
        {
            audioData = null;
            channels = _channels;

            lock (_bufferAccessLock)
            {
                if (_ringBuffer == null)
                    return false;

                int capacity = _ringBuffer.Length;

                // Initialize NdiSender read position on first call (when buffer is half full)
                if (!_ndiReadStarted && _availableSamples >= capacity / 2)
                {
                    int delay = capacity / 2;
                    _ndiReadIndex = (_writeIndex - delay + capacity) % capacity;
                    _ndiReadStarted = true;
                    Debug.Log($"[AudioListenerBridge] NdiSender read started at index {_ndiReadIndex}, write at {_writeIndex}");
                }

                if (!_ndiReadStarted)
                    return false;

                int available = (_writeIndex - _ndiReadIndex + capacity) % capacity;

                if (available <= 0)
                    return false;

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

        // Ring buffer (intermediate audio storage)
        private const int BufferLengthMS = 200;
        private float[] _ringBuffer;
        private int _writeIndex;
        private int _availableSamples;
        private int _channels = 2;
        private int _sampleRate = 48000;

        // Tracked read position for continuous consumption (diagnostics)
        private int _readIndex;
        private bool _readStarted;

        // Separate read position for NdiSender
        private int _ndiReadIndex;
        private bool _ndiReadStarted;

        // Diagnostic timed capture (kept, now reads from ring buffer)
        private static bool _isCapturing = false;
        private static float _captureTimer = 0f;
        private const float CAPTURE_INTERVAL = 20f;
        private const float CAPTURE_DURATION = 5f;

        // ReadFromRing output capture (to test if tracking works correctly)
        private System.Collections.Generic.List<float> _readFromRingCapture;
        private readonly object _capturelock = new object();
        private bool _isCapturingReadOutput = false;
        private float _readOutputCaptureTimer = 0f;

        // Buffered callback data (to send from Update instead of audio thread)
        private System.Collections.Generic.Queue<float[]> _callbackQueue = new System.Collections.Generic.Queue<float[]>();
        private readonly object _callbackLock = new object();
        private int _callbackChannels = 2;

        // Per-frame analysis control
        private const float JumpThreshold = 0.1f;
        private const bool LogPerFrameDiscontinuities = false;

        // Buffer diagnostics
        private int _underrunCount = 0;
        private int _totalReadCalls = 0;
        private float _diagnosticLogTimer = 0f;
        private const float DIAGNOSTIC_LOG_INTERVAL = 5f;

        // File logging for diagnostics
        private StreamWriter _logWriter;
        private readonly object _logLock = new object();
        private string _logFilePath;
        private int _unbufferedSendCount = 0;

        private void Start()
        {
            if (!Application.isPlaying) return;
            _sampleRate = AudioSettings.outputSampleRate;
            // Allocate ring buffer: samples = sampleRate * channels * (ms/1000)
            int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
            _ringBuffer = new float[Math.Max(capacity, 1)];
            _writeIndex = 0;
            _availableSamples = 0;
            _readFromRingCapture = new System.Collections.Generic.List<float>();

            // Initialize file logging with unique filename
            string baseFilename = $"AudioBridge_Log_{DateTime.Now:yyyyMMdd_HHmmss}";
            _logFilePath = $"{baseFilename}.txt";
            int counter = 1;

            // If file is locked, try with counter suffix
            while (File.Exists(_logFilePath) && counter < 100)
            {
                _logFilePath = $"{baseFilename}_{counter}.txt";
                counter++;
            }

            try
            {
                _logWriter = new StreamWriter(_logFilePath, false);
                _logWriter.AutoFlush = true;  }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioListenerBridge] Failed to create log file: {ex.Message}");
            }

            Debug.Log($"[AudioListenerBridge] Init sampleRate={_sampleRate}Hz channels={_channels} ringCapacity={_ringBuffer.Length}");
            Debug.Log($"[AudioListenerBridge] Logging to file: {_logFilePath}");
        }

        private void Update()
        {
            if (!Application.isPlaying) return;

            // Handle ring buffer snapshot capture
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
                // WriteCapturedAudio(); // Commented out - not needed for debugging NDI path
            }

            // Handle ReadFromRing output capture (separate from snapshot)
            _readOutputCaptureTimer += Time.deltaTime;
            if (_readOutputCaptureTimer >= CAPTURE_INTERVAL && !_isCapturingReadOutput)
            {
                _readOutputCaptureTimer = 0f;
                _isCapturingReadOutput = true;
                lock (_capturelock)
                {
                    _readFromRingCapture.Clear();
                }
                Debug.Log("[AudioListenerBridge] Starting ReadFromRing output capture...");
            }

            if (_isCapturingReadOutput && _readOutputCaptureTimer >= CAPTURE_DURATION)
            {
                _isCapturingReadOutput = false;
                _readOutputCaptureTimer = 0f;
                WriteReadFromRingCapture();
            }
            
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;

            // Write incoming frame into ring buffer (for diagnostics)
            WriteToRing(data);

            // Create buffer for reading from ring (for diagnostics)
            float[] bufferedData = new float[data.Length];


            // Send ORIGINAL unbuffered data to NdiSender
            // NdiSender will do its own buffering!
            lock (_lock)
            {
                float[] copy = new float[data.Length];
                System.Array.Copy(data, copy, data.Length);
                _onAudioFilterReadEvent?.Invoke(copy, channels);
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
            // NOTE: _availableSamples is never decremented in ReadFromRing!
            // This will saturate at capacity and may not reflect actual available data
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
                string msg = $"Reading started at index {_readIndex}, write at {_writeIndex} (delay={delay} samples, {delay/(float)(_sampleRate*_channels)*1000:F1}ms)";
               // LogToFile(msg);
                Debug.Log($"[AudioListenerBridge] {msg}");
            }
        
            if (!_readStarted) return false;  // Still filling initial buffer
        
            _totalReadCalls++;
        
            // Check if enough samples available between read and write positions
            // NOTE: This calculation has a potential issue - when _writeIndex == _readIndex,
            // it can't distinguish between "buffer empty" and "buffer full"
            int available = (_writeIndex - _readIndex + capacity) % capacity;
            if (available < outputBuffer.Length)
            {
                _underrunCount++;
                string msg = $"UNDERRUN #{_underrunCount}! Available={available}, Need={outputBuffer.Length}, ReadIdx={_readIndex}, WriteIdx={_writeIndex}";
                //LogToFile(msg);
                Debug.LogWarning($"[AudioListenerBridge] Buffer underrun #{_underrunCount}! Available={available}, Need={outputBuffer.Length}, ReadIdx={_readIndex}, WriteIdx={_writeIndex}");
                return false;
            }
        
            // Read from tracked position (continuous consumption!)
            for (int i = 0; i < outputBuffer.Length; i++)
            {
                outputBuffer[i] = _ringBuffer[_readIndex];
                _readIndex = (_readIndex + 1) % capacity;
            }
        
            // Capture the read output if capturing is enabled
            if (_isCapturingReadOutput)
            {
                lock (_capturelock)
                {
                    _readFromRingCapture.AddRange(outputBuffer);
                }
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

                  
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AudioListenerBridge] Write error: {ex.Message}");
                }
            });

            writeThread.IsBackground = true;
            writeThread.Start();
        }

        private void WriteReadFromRingCapture()
        {
            float[] capturedData;
            lock (_capturelock)
            {
                if (_readFromRingCapture.Count == 0)
                {
                    Debug.LogWarning("[AudioListenerBridge] No ReadFromRing output captured");
                    return;
                }
                capturedData = _readFromRingCapture.ToArray();
            }

            Thread writeThread = new Thread(() =>
            {
                try
                {
                    // Generate unique filename to avoid sharing violations
                    string baseFilename = $"AudioBridge_ReadOutput_{DateTime.Now:HHmmss}";
                    string filename = $"{baseFilename}.raw";
                    int counter = 1;
                    while (File.Exists(filename) && counter < 100)
                    {
                        filename = $"{baseFilename}_{counter}.raw";
                        counter++;
                    }

                    using (var fs = new FileStream(filename, FileMode.Create))
                    using (var bw = new BinaryWriter(fs))
                    {
                        for (int i = 0; i < capturedData.Length; i++)
                            bw.Write(capturedData[i]);
                    }

                    Debug.Log($"[AudioListenerBridge] Wrote ReadFromRing output: {capturedData.Length} samples -> {filename}");
                    float durationSec = capturedData.Length / (float)(_channels * _sampleRate);
                    Debug.Log($"[AudioListenerBridge] Duration: {durationSec:F2}s");


                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AudioListenerBridge] Write error: {ex.Message}");
                }
            });

            writeThread.IsBackground = true;
            writeThread.Start();
        }
        




        private void OnDestroy()
        {
            if (_logWriter != null)
            {
                try
                {    lock (_logLock)
                    {
                        _logWriter.Close();
                        _logWriter = null;
                    }
                    Debug.Log($"[AudioListenerBridge] Log file closed: {_logFilePath}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AudioListenerBridge] Error closing log: {ex.Message}");
                }
            }
        }
    }
}
