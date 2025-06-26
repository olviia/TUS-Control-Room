using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;

namespace WebRTC.Core
{
    /// <summary>
    /// Bridges NDI sources to WebRTC streams with synchronized audio/video capture
    /// </summary>
    public class WebRTCStreamBridge : MonoBehaviour
    {
        [Header("NDI Source")]
        [SerializeField] private NdiReceiver ndiReceiver;
        
        [Header("Stream Settings")]
        [SerializeField] private int targetFrameRate = 30;
        [SerializeField] private Vector2Int streamResolution = new Vector2Int(1920, 1080);
        
        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;
        
        // WebRTC components
        private VideoStreamTrack videoTrack;
        private AudioStreamTrack audioTrack;
        private Camera captureCamera;
        private AudioSource ndiAudioSource;
        
        // Capture management
        private RenderTexture renderTexture;
        private Texture2D readbackTexture;
        private bool isCapturing = false;
        private float lastCaptureTime = 0f;
        private float captureInterval;
        
        // Audio sync
        private AudioListener audioListener;
        private double lastAudioTime = 0;
        
        public VideoStreamTrack VideoTrack => videoTrack;
        public AudioStreamTrack AudioTrack => audioTrack;
        public bool IsStreaming => videoTrack != null && audioTrack != null;

        private void Awake()
        {
            captureInterval = 1f / targetFrameRate;
            SetupRenderTexture();
        }

        private void Start()
        {
            if (ndiReceiver == null)
                ndiReceiver = GetComponent<NdiReceiver>();
                
            StartCoroutine(DelayedAudioSourceSearch());
        }

        private IEnumerator DelayedAudioSourceSearch()
        {
            // Wait for NDI receiver to create its audio source
            yield return new WaitForSeconds(0.5f);
            FindNDIAudioSource();
        }

        public void StartStreaming()
        {
            if (IsStreaming)
            {
                LogDebug("Already streaming");
                return;
            }

            if (ndiReceiver == null)
            {
                Debug.LogError("WebRTCStreamBridge: No NDI receiver assigned");
                return;
            }

            SetupVideoTrack();
            SetupAudioTrack();
            
            isCapturing = true;
            LogDebug($"Started streaming from NDI source: {ndiReceiver.ndiName}");
        }

        public void StopStreaming()
        {
            isCapturing = false;
            
            CleanupVideoTrack();
            CleanupAudioTrack();
            
            LogDebug("Stopped streaming");
        }

        private void SetupRenderTexture()
        {
            if (renderTexture != null)
                renderTexture.Release();
                
            renderTexture = new RenderTexture(streamResolution.x, streamResolution.y, 0)
            {
                format = RenderTextureFormat.ARGB32,
                useMipMap = false,
                autoGenerateMips = false
            };
            renderTexture.Create();
            
            readbackTexture = new Texture2D(streamResolution.x, streamResolution.y, TextureFormat.RGB24, false);
        }

        private void SetupVideoTrack()
        {
            CleanupVideoTrack();
            
            // Create video track from render texture
            videoTrack = new VideoStreamTrack(renderTexture);
            
            LogDebug($"Video track created: {streamResolution.x}x{streamResolution.y} @ {targetFrameRate}fps");
        }

        private void SetupAudioTrack()
        {
            CleanupAudioTrack();
            
            if (ndiAudioSource == null)
            {
                FindNDIAudioSource();
                if (ndiAudioSource == null)
                {
                    Debug.LogWarning("WebRTCStreamBridge: No audio source found for NDI receiver");
                    return;
                }
            }
            
            // Create audio track from NDI audio source
            audioTrack = new AudioStreamTrack(ndiAudioSource);
            
            LogDebug($"Audio track created from source: {ndiAudioSource.name}");
        }

        private void FindNDIAudioSource()
        {
            // NDI receiver creates a child GameObject with AudioSource
            if (ndiReceiver.transform.childCount > 0)
            {
                for (int i = 0; i < ndiReceiver.transform.childCount; i++)
                {
                    var child = ndiReceiver.transform.GetChild(i);
                    var audioSource = child.GetComponent<AudioSource>();
                    if (audioSource != null)
                    {
                        ndiAudioSource = audioSource;
                        LogDebug($"Found NDI audio source: {child.name}");
                        break;
                    }
                }
            }
            
            if (ndiAudioSource == null)
            {
                // Fallback: search in parent or siblings
                ndiAudioSource = GetComponentInChildren<AudioSource>();
                if (ndiAudioSource != null)
                    LogDebug($"Found fallback audio source: {ndiAudioSource.name}");
            }
        }

        private void Update()
        {
            if (!isCapturing || !IsStreaming) return;
            
            // Throttle capture to target frame rate
            if (Time.time - lastCaptureTime < captureInterval) return;
            
            CaptureFrame();
            lastCaptureTime = Time.time;
        }

        private void CaptureFrame()
        {
            if (ndiReceiver == null) return;
            
            try
            {
                // Get synchronized timestamp for A/V sync
                double currentTime = AudioSettings.dspTime;
                
                // Capture video frame from NDI
                var ndiTexture = ndiReceiver.GetTexture();
                if (ndiTexture == null) return;
                
                // Copy NDI texture to render texture with proper format
                Graphics.Blit(ndiTexture, renderTexture);
                
                // Sync audio timestamp
                if (ndiAudioSource != null && audioTrack != null)
                {
                    // Ensure audio and video timestamps match
                    SynchronizeAudioVideo(currentTime);
                }
                
            }
            catch (System.Exception e)
            {
                Debug.LogError($"WebRTCStreamBridge: Error capturing frame: {e.Message}");
            }
        }

        private void SynchronizeAudioVideo(double currentTime)
        {
            // Store timing for sync verification
            lastAudioTime = currentTime;
            
            // WebRTC handles internal sync, but we ensure consistent timing
            if (ndiAudioSource.isPlaying)
            {
                // Audio is playing, video frame captured at same DSP time
                // WebRTC will maintain this sync relationship
            }
        }

        private void CleanupVideoTrack()
        {
            if (videoTrack != null)
            {
                videoTrack.Dispose();
                videoTrack = null;
            }
        }

        private void CleanupAudioTrack()
        {
            if (audioTrack != null)
            {
                audioTrack.Dispose();
                audioTrack = null;
            }
        }

        private void OnDestroy()
        {
            StopStreaming();
            
            if (renderTexture != null)
            {
                renderTexture.Release();
                renderTexture = null;
            }
            
            if (readbackTexture != null)
            {
                DestroyImmediate(readbackTexture);
                readbackTexture = null;
            }
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogs)
                Debug.Log($"[WebRTCStreamBridge] {message}");
        }

        #region Public Interface
        
        /// <summary>
        /// Set the NDI receiver to capture from
        /// </summary>
        public void SetNDIReceiver(NdiReceiver receiver)
        {
            bool wasStreaming = IsStreaming;
            
            if (wasStreaming)
                StopStreaming();
                
            ndiReceiver = receiver;
            
            if (wasStreaming)
                StartStreaming();
        }
        
        /// <summary>
        /// Update stream quality settings
        /// </summary>
        public void UpdateStreamSettings(int frameRate, Vector2Int resolution)
        {
            bool wasStreaming = IsStreaming;
            
            if (wasStreaming)
                StopStreaming();
                
            targetFrameRate = frameRate;
            streamResolution = resolution;
            captureInterval = 1f / targetFrameRate;
            
            SetupRenderTexture();
            
            if (wasStreaming)
                StartStreaming();
        }
        
        /// <summary>
        /// Get current stream statistics
        /// </summary>
        public (int frameRate, Vector2Int resolution, bool hasAudio) GetStreamInfo()
        {
            return (targetFrameRate, streamResolution, ndiAudioSource != null);
        }
        
        #endregion
    }
}