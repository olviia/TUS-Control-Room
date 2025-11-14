using System;
using Unity.WebRTC;
using UnityEngine;

namespace TUS.WebRTC.VoiceChat
{
    /// <summary>
    /// Captures microphone audio and creates a WebRTC AudioStreamTrack
    /// </summary>
    public class MicrophoneAudioCapture : MonoBehaviour
    {
        [Header("Microphone Settings")]
        [SerializeField] private int sampleRate = 48000;
        [SerializeField] private int lengthSeconds = 1;
        [SerializeField] private bool autoStart = true;

        [Header("Audio Settings")]
        [SerializeField] private float volume = 1.0f;
        [SerializeField] private bool muteLocalPlayback = true;

        public AudioStreamTrack AudioTrack { get; private set; }
        public bool IsCapturing { get; private set; }
        public string CurrentMicrophone { get; private set; }

        private AudioSource _audioSource;
        private AudioClip _microphoneClip;

        public event Action OnCaptureStarted;
        public event Action OnCaptureStopped;

        private void Awake()
        {
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.loop = true;
            _audioSource.volume = muteLocalPlayback ? 0f : volume;
        }

        private void Start()
        {
            if (autoStart)
            {
                StartCapture();
            }
        }

        /// <summary>
        /// Start capturing microphone audio
        /// </summary>
        public void StartCapture(string deviceName = null)
        {
            if (IsCapturing)
            {
                Debug.LogWarning("[MicrophoneAudioCapture] Already capturing audio");
                return;
            }

            // Use default microphone if none specified
            if (string.IsNullOrEmpty(deviceName))
            {
                if (Microphone.devices.Length > 0)
                {
                    deviceName = Microphone.devices[0];
                }
                else
                {
                    Debug.LogError("[MicrophoneAudioCapture] No microphone devices found");
                    return;
                }
            }

            CurrentMicrophone = deviceName;

            // Start microphone capture
            _microphoneClip = Microphone.Start(deviceName, true, lengthSeconds, sampleRate);

            if (_microphoneClip == null)
            {
                Debug.LogError($"[MicrophoneAudioCapture] Failed to start microphone: {deviceName}");
                return;
            }

            // Wait for microphone to start
            int timeout = 0;
            while (Microphone.GetPosition(deviceName) <= 0 && timeout < 100)
            {
                timeout++;
                System.Threading.Thread.Sleep(10);
            }

            if (timeout >= 100)
            {
                Debug.LogError("[MicrophoneAudioCapture] Microphone initialization timeout");
                StopCapture();
                return;
            }

            // Setup audio source
            _audioSource.clip = _microphoneClip;
            _audioSource.Play();

            // Create WebRTC audio track from audio source
            AudioTrack = new AudioStreamTrack(_audioSource);

            IsCapturing = true;
            Debug.Log($"[MicrophoneAudioCapture] Started capturing from microphone: {deviceName}");
            Debug.Log($"[MicrophoneAudioCapture] AudioTrack created - Enabled: {AudioTrack.Enabled}, ReadyState: {AudioTrack.ReadyState}");
            OnCaptureStarted?.Invoke();
        }

        /// <summary>
        /// Stop capturing microphone audio
        /// </summary>
        public void StopCapture()
        {
            if (!IsCapturing)
            {
                return;
            }

            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (!string.IsNullOrEmpty(CurrentMicrophone))
            {
                Microphone.End(CurrentMicrophone);
            }

            if (AudioTrack != null)
            {
                AudioTrack.Dispose();
                AudioTrack = null;
            }

            if (_microphoneClip != null)
            {
                Destroy(_microphoneClip);
                _microphoneClip = null;
            }

            IsCapturing = false;
            CurrentMicrophone = null;

            Debug.Log("[MicrophoneAudioCapture] Stopped capturing microphone audio");
            OnCaptureStopped?.Invoke();
        }

        /// <summary>
        /// Set microphone volume (does not affect WebRTC stream, only local playback)
        /// </summary>
        public void SetVolume(float newVolume)
        {
            volume = Mathf.Clamp01(newVolume);
            if (_audioSource != null && !muteLocalPlayback)
            {
                _audioSource.volume = volume;
            }
        }

        /// <summary>
        /// Mute/unmute local playback of microphone
        /// </summary>
        public void SetMuteLocalPlayback(bool mute)
        {
            muteLocalPlayback = mute;
            if (_audioSource != null)
            {
                _audioSource.volume = mute ? 0f : volume;
            }
        }

        /// <summary>
        /// Get list of available microphone devices
        /// </summary>
        public static string[] GetAvailableMicrophones()
        {
            return Microphone.devices;
        }

        /// <summary>
        /// Switch to a different microphone device
        /// </summary>
        public void SwitchMicrophone(string deviceName)
        {
            if (IsCapturing)
            {
                StopCapture();
                StartCapture(deviceName);
            }
            else
            {
                CurrentMicrophone = deviceName;
            }
        }

        private void OnDestroy()
        {
            StopCapture();
        }

        private void OnApplicationQuit()
        {
            StopCapture();
        }
    }
}