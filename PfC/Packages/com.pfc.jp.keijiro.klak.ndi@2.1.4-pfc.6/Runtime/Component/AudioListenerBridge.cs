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

        // Ring buffer (intermediate audio storage)
        private const int BufferLengthMS = 200;
        private float[] _ringBuffer;
        private int _writeIndex;
        private int _availableSamples;
        private int _channels = 2;
        private int _sampleRate = 48000;

        // Separate read position for NdiSender
        private int _ndiReadIndex;
        private bool _ndiReadStarted;
        

        // Buffered callback data (to send from Update instead of audio thread)
        private System.Collections.Generic.Queue<float[]> _callbackQueue = new System.Collections.Generic.Queue<float[]>();
        private readonly object _callbackLock = new object();
        private int _callbackChannels = 2;
        

        private void Start()
        {
            if (!Application.isPlaying) return;
            _sampleRate = AudioSettings.outputSampleRate;
            // Allocate ring buffer: samples = sampleRate * channels * (ms/1000)
            int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
            _ringBuffer = new float[Math.Max(capacity, 1)];
            _writeIndex = 0;
            _availableSamples = 0;


        }


        protected virtual void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;

            // Write incoming frame into ring buffer (for diagnostics)
            WriteToRing(data);

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
        
    }
}
