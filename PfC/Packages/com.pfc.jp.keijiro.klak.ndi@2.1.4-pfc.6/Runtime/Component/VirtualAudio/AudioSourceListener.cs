using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Klak.Ndi.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AudioSourceListener : MonoBehaviour
    {
        // TODO: support multiple channel audio files

        [Serializable]
        public struct AdditionalSettings
        {
            public bool forceToChannel;
            public int channel;
        }
        
        public AdditionalSettings additionalSettings;
        
        [SerializeField] private bool _showDebugGizmos = false;
        
        private AudioSource _audioSource;
        private VirtualAudio.AudioSourceData _audioSourceData;
        
        private VirtualAudio.AudioSourceSettings _TmpSettings;

        private object _lockObj = new object();

        private float[] listenerWeights;

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

        private void Start()
        {
            if (!Application.isPlaying) return;
            _sampleRate = AudioSettings.outputSampleRate;
            // Allocate ring buffer: samples = sampleRate * channels * (ms/1000)
            int capacity = (_sampleRate * _channels * BufferLengthMS) / 1000;
            _ringBuffer = new float[Math.Max(capacity, 1)];
            _writeIndex = 0;
            _availableSamples = 0;
            Debug.Log($"[AudioSourceListener-{gameObject.name}] Init sampleRate={_sampleRate}Hz channels={_channels} ringCapacity={_ringBuffer.Length}");
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            _channels = channels;

            // Write incoming frame into ring buffer
            WriteToRing(data);

            // Create buffer for reading from ring
            float[] bufferedData = new float[data.Length];
            bool hasBufferedData = ReadFromRing(bufferedData);

            lock (_lockObj)
            {
                if (_audioSourceData == null)
                {
                    return;
                }

                listenerWeights = _audioSourceData.currentWeights;
                _audioSourceData.settings = _TmpSettings;

                // Send buffered data to VirtualAudio (not original data!)
                if (hasBufferedData)
                {
                    VirtualAudio.SetAudioDataFromSource(_audioSourceData.id, bufferedData, channels);
                }
                else
                {
                    // Still filling buffer, send original data temporarily
                    VirtualAudio.SetAudioDataFromSource(_audioSourceData.id, data, channels);
                }
            }
        }

        private void Update()
        {
            _TmpSettings.position = transform.position;
            _TmpSettings.spatialBlend = _audioSource.spatialBlend;
            _TmpSettings.volume = _audioSource.volume;
            _TmpSettings.forceToChannel = additionalSettings.forceToChannel ? additionalSettings.channel : -1;
            _TmpSettings.minDistance = _audioSource.minDistance;
            _TmpSettings.maxDistance = _audioSource.maxDistance;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_showDebugGizmos || !Application.isPlaying)
                return;

            float[] tmpListenerWeights = null;
            lock (_lockObj)
            {
                tmpListenerWeights = listenerWeights;
            }

            if (tmpListenerWeights == null)
                return;

            if (!Camera.main)
                return;
            
            var listenersPositions = VirtualAudio.GetListenersPositions();
            if (listenersPositions.Length != tmpListenerWeights.Length)
                return;

            for (int i = 0; i < listenersPositions.Length; i++)
            {
                Gizmos.color = tmpListenerWeights[i] > 0 ? new Color(0, 1, 0, 0.5f) : new Color(1, 0, 0, 0.5f);
                listenersPositions[i] = listenersPositions[i];
                Gizmos.DrawWireSphere( listenersPositions[i], 1f);
            }
            
            for (int i = 0; i < tmpListenerWeights.Length; i++)
            {
                if (tmpListenerWeights[i] > 0)
                {
                    Gizmos.color = Color.black;
                    Gizmos.DrawLine( listenersPositions[i], transform.position);
                    Gizmos.color = Color.green;
                    var dir =  listenersPositions[i] - transform.position;
                    Gizmos.DrawLine( transform.position, transform.position + dir * listenerWeights[i]);
                    
                }
            }
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (!_audioSource)
            {
                Debug.LogError("AudioSourceListener requires an AudioSource component.");
                enabled = false;
            }
        }

        private void VirtualAudioStateChanged(bool active)
        {
            _audioSource.spatialize = VirtualAudio.UseVirtualAudio;
            _audioSource.spatializePostEffects = VirtualAudio.UseVirtualAudio;

            if (active)
            {
                lock (_lockObj)
                {
                    _audioSourceData = VirtualAudio.RegisterAudioSourceChannel(_TmpSettings);
                }
            }
            else
            {
                lock (_lockObj)
                {
                    if (_audioSourceData != null)
                    {
                        VirtualAudio.UnregisterAudioSource(_audioSourceData);
                        _audioSourceData = null;
                    }
                }
            }
        }
        
        private void OnEnable()
        {
            // We need raw audio data without any spatialization
            _audioSource.spatialize = VirtualAudio.UseVirtualAudio;
            _audioSource.spatializePostEffects = VirtualAudio.UseVirtualAudio;
            
            _TmpSettings.position = transform.position;
            _TmpSettings.spatialBlend = _audioSource.spatialBlend;
            _TmpSettings.volume = _audioSource.volume;
            _TmpSettings.forceToChannel = additionalSettings.forceToChannel ? additionalSettings.channel : -1;
            
            _audioSource.bypassListenerEffects = false;
            _TmpSettings.minDistance = _audioSource.minDistance;
            _TmpSettings.maxDistance = _audioSource.maxDistance;
            _TmpSettings.rolloffMode = _audioSource.rolloffMode;
            _TmpSettings.customRolloffCurve = _audioSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            _TmpSettings.spatialBlendCurve =  _audioSource.GetCustomCurve(AudioSourceCurveType.SpatialBlend);
            
            VirtualAudio.OnVirtualAudioStateChanged.AddListener(VirtualAudioStateChanged);

            if (!VirtualAudio.UseVirtualAudio)
                return;
            
            lock (_lockObj)
            {
                _audioSourceData = VirtualAudio.RegisterAudioSourceChannel(_TmpSettings);
            }
        }
        
        private void OnDisable()
        {
            VirtualAudio.OnVirtualAudioStateChanged.RemoveListener(VirtualAudioStateChanged);
            lock (_lockObj)
            {
                if (_audioSourceData == null)
                    return;

                VirtualAudio.UnregisterAudioSource(_audioSourceData);
                _audioSourceData = null;
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
                Debug.Log($"[AudioSourceListener-{gameObject.name}] Reading started at index {_readIndex}, write at {_writeIndex} (delay={delay} samples, {delay/(float)(_sampleRate*_channels)*1000:F1}ms)");
            }

            if (!_readStarted) return false;  // Still filling initial buffer

            // Check if enough samples available between read and write positions
            int available = (_writeIndex - _readIndex + capacity) % capacity;
            if (available < outputBuffer.Length)
            {
                Debug.LogWarning($"[AudioSourceListener-{gameObject.name}] Buffer underrun! Available={available}, Need={outputBuffer.Length}");
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

    }
}