using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using System;
using Unity.Collections;

/// <summary>
/// WebRTC Audio Streamer that uses existing NDI AudioSource
/// This approach leverages the existing NDI audio pipeline instead of raw data manipulation
/// </summary>
public class WebRTCAudioStreamer : MonoBehaviour
{
    [Header("Pipeline Identity")]
    public PipelineType pipelineType;
    
    [Header("Audio Sources")]
    public NdiReceiver ndiAudioSource;
    public Transform audioSourcePosition;
    
    [Header("Audio Settings")]
    [SerializeField] private float spatialBlend = 1.0f;
    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private float _audioVolume = 1.0f;
    
    // WebRTC Audio Components
    private AudioStreamTrack sendingAudioTrack;
    private AudioSource ndiAudioSourceComponent;
    private AudioSource receivingAudioSource;
    private GameObject receivingAudioGameObject;
    
    // State Management
    private bool isStreaming = false;
    private bool isReceiving = false;
    private string currentSessionId = string.Empty;
    
    // Events
    public static event Action<PipelineType, bool, string> OnAudioStreamStateChanged;
    
    public float AudioVolume
    {
        get => _audioVolume;
        set
        {
            _audioVolume = Mathf.Clamp01(value);
            if (receivingAudioSource != null)
            {
                receivingAudioSource.volume = _audioVolume;
            }
        }
    }
    
    #region Unity Lifecycle
    
    void Start()
    {
        InitializeAudioSystems();
        Debug.Log($"[🎵Audio-{pipelineType}] Audio streamer initialized");
    }
    
    void OnDestroy()
    {
        StopAllAudioOperations();
        DisposeAudioComponents();
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeAudioSystems()
    {
        if (audioSourcePosition == null)
            audioSourcePosition = transform;
            
        // Find the NDI AudioSource component
        if (ndiAudioSource != null)
        {
            ndiAudioSourceComponent = ndiAudioSource.GetComponentInChildren<AudioSource>();
            if (ndiAudioSourceComponent == null)
            {
                Debug.LogError($"[🎵Simple-{pipelineType}] No AudioSource found in NDI receiver");
            }
            else
            {
                Debug.Log($"[🎵Simple-{pipelineType}] Found NDI AudioSource: {ndiAudioSourceComponent.name}");
            }
        }
    }
    
    #endregion
    
    #region Public Interface
    
    /// <summary>
    /// Start streaming audio for WebRTC transmission
    /// </summary>
    public AudioStreamTrack StartAudioStreaming(string sessionId)
    {
        if (isStreaming)
        {
            Debug.LogWarning($"[🎵Audio-{pipelineType}] Already streaming audio");
            return sendingAudioTrack;
        }
        
        if (ndiAudioSourceComponent == null)
        {
            Debug.LogError($"[🎵Audio-{pipelineType}] No NDI AudioSource available for streaming");
            return null;
        }
        
        currentSessionId = sessionId;
        isStreaming = true;
        
        CreateSendingAudioTrack();
        
        OnAudioStreamStateChanged?.Invoke(pipelineType, true, sessionId);
        Debug.Log($"[🎵Audio-{pipelineType}] Started audio streaming for session: {sessionId}");
        
        return sendingAudioTrack;
    }
    
    /// <summary>
    /// Prepare to receive remote audio
    /// </summary>
    public AudioSource PrepareAudioReceiving(string sessionId)
    {
        if (isReceiving)
        {
            Debug.LogWarning($"[🎵Audio-{pipelineType}] Already receiving audio");
            return receivingAudioSource;
        }
        
        currentSessionId = sessionId;
        isReceiving = true;
        
        CreateReceivingAudioSource();
        
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, sessionId);
        Debug.Log($"[🎵Audio-{pipelineType}] Prepared for audio receiving: {sessionId}");
        
        return receivingAudioSource;
    }
    
    /// <summary>
    /// Handle incoming WebRTC audio track
    /// </summary>
    public void HandleIncomingAudioTrack(AudioStreamTrack audioTrack)
    {
        if (receivingAudioSource == null)
        {
            Debug.LogError($"[🎵Audio-{pipelineType}] No receiving AudioSource prepared");
            return;
        }
        
        // Use Unity WebRTC's SetTrack extension method
        receivingAudioSource.SetTrack(audioTrack);
        receivingAudioSource.loop = true;
        receivingAudioSource.Play();
        
        Debug.Log($"[🎵Audio-{pipelineType}] Incoming audio track connected");
        
        // Verify audio setup after a short delay
        StartCoroutine(VerifyAudioSetup());
    }
    
    /// <summary>
    /// Stop current audio operations
    /// </summary>
    public void StopAudioOperations()
    {
        StopAllAudioOperations();
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, string.Empty);
        Debug.Log($"[🎵Audio-{pipelineType}] Audio operations stopped");
    }
    
    #endregion
    
    #region WebRTC Audio Management
    
    private void CreateSendingAudioTrack()
    {
        if (ndiAudioSourceComponent == null)
        {
            Debug.LogError($"[🎵Audio-{pipelineType}] No NDI AudioSource for streaming");
            return;
        }
        
        // Create WebRTC AudioStreamTrack directly from NDI's AudioSource
        sendingAudioTrack = new AudioStreamTrack(ndiAudioSourceComponent);
        
        // Ensure the NDI AudioSource is playing
        if (!ndiAudioSourceComponent.isPlaying)
        {
            ndiAudioSourceComponent.Play();
        }
        
        Debug.Log($"[🎵Audio-{pipelineType}] Sending audio track created from NDI AudioSource");
    }
    
    private void CreateReceivingAudioSource()
    {
        if (receivingAudioGameObject == null)
        {
            receivingAudioGameObject = new GameObject($"WebRTC_Audio_Receiver_{pipelineType}");
            
            // IMPORTANT: Set position BEFORE setting parent to avoid transform issues
            if (audioSourcePosition != null)
            {
                receivingAudioGameObject.transform.position = audioSourcePosition.position;
                receivingAudioGameObject.transform.rotation = audioSourcePosition.rotation;
                // Set parent with worldPositionStays = true to maintain position
                receivingAudioGameObject.transform.SetParent(audioSourcePosition, true);
            }
            else
            {
                receivingAudioGameObject.transform.SetParent(transform, false);
                Debug.LogWarning($"[🎵Audio-{pipelineType}] No audioSourcePosition set - using default position");
            }
            
            receivingAudioGameObject.hideFlags = HideFlags.DontSave;
            
            receivingAudioSource = receivingAudioGameObject.AddComponent<AudioSource>();
            
            // Configure for 3D spatial audio
            receivingAudioSource.spatialBlend = spatialBlend;
            receivingAudioSource.volume = _audioVolume;
            receivingAudioSource.minDistance = minDistance;
            receivingAudioSource.maxDistance = maxDistance;
            receivingAudioSource.rolloffMode = AudioRolloffMode.Linear;
            receivingAudioSource.playOnAwake = false;
            receivingAudioSource.loop = true;
            
            // Ensure proper audio output routing
            receivingAudioSource.outputAudioMixerGroup = null; // Use default output
            receivingAudioSource.priority = 128;
            receivingAudioSource.bypassEffects = false;
            receivingAudioSource.bypassListenerEffects = false;
            receivingAudioSource.bypassReverbZones = false;
        }
        
        receivingAudioGameObject.SetActive(true);
        Debug.Log($"[🎵Audio-{pipelineType}] Receiving audio source created at {receivingAudioGameObject.transform.position} (target: {audioSourcePosition?.position})");
    }
    
    #endregion
    
    #region Audio Verification and Debugging
    
    private IEnumerator VerifyAudioSetup()
    {
        yield return new WaitForSeconds(1f);
        
        if (receivingAudioSource != null)
        {
            Debug.Log($"[🎵Audio-{pipelineType}] Audio Verification:");
            Debug.Log($"  - Playing: {receivingAudioSource.isPlaying}");
            Debug.Log($"  - Volume: {receivingAudioSource.volume}");
            Debug.Log($"  - Clip: {receivingAudioSource.clip}");
            Debug.Log($"  - Mute: {receivingAudioSource.mute}");
            Debug.Log($"  - Spatial Blend: {receivingAudioSource.spatialBlend}");
            
            // Check AudioListener
            var audioListener = FindObjectOfType<AudioListener>();
            Debug.Log($"  - AudioListener found: {audioListener != null}");
            if (audioListener != null)
            {
                Debug.Log($"  - AudioListener enabled: {audioListener.enabled}");
                Debug.Log($"  - AudioListener volume: {AudioListener.volume}");
            }
        }
        
        // Also verify sending audio source
        if (ndiAudioSourceComponent != null)
        {
            Debug.Log($"[🎵Audio-{pipelineType}] NDI Audio Source Verification:");
            Debug.Log($"  - Playing: {ndiAudioSourceComponent.isPlaying}");
            Debug.Log($"  - Volume: {ndiAudioSourceComponent.volume}");
            Debug.Log($"  - Clip: {ndiAudioSourceComponent.clip}");
            Debug.Log($"  - Enabled: {ndiAudioSourceComponent.enabled}");
        }
    }
    
    public void DebugAudioState()
    {
        Debug.Log($"[🎵Audio-{pipelineType}] Current State:");
        Debug.Log($"  - Streaming: {isStreaming}");
        Debug.Log($"  - Receiving: {isReceiving}");
        Debug.Log($"  - Session: {currentSessionId}");
        
        if (ndiAudioSourceComponent != null)
            Debug.Log($"  - NDI AudioSource Playing: {ndiAudioSourceComponent.isPlaying}");
        if (sendingAudioTrack != null)
            Debug.Log($"  - Sending Track Valid: {sendingAudioTrack != null}");
        if (receivingAudioSource != null)
            Debug.Log($"  - Receiving AudioSource Playing: {receivingAudioSource.isPlaying}");
    }
    
    #endregion
    
    #region Cleanup
    
    private void StopAllAudioOperations()
    {
        isStreaming = false;
        isReceiving = false;
        
        if (receivingAudioSource != null)
        {
            receivingAudioSource.Stop();
        }
        
        currentSessionId = string.Empty;
    }
    
    private void DisposeAudioComponents()
    {
        // Dispose WebRTC audio track
        if (sendingAudioTrack != null)
        {
            sendingAudioTrack.Dispose();
            sendingAudioTrack = null;
        }
        
        // Cleanup GameObjects
        if (receivingAudioGameObject != null)
        {
            DestroyImmediate(receivingAudioGameObject);
        }
    }
    
    #endregion
    
    #region Public Properties
    
    public bool IsStreaming => isStreaming;
    public bool IsReceiving => isReceiving;
    public string CurrentSessionId => currentSessionId;
    public AudioSource ReceivingAudioSource => receivingAudioSource;
    public AudioSource NdiAudioSource => ndiAudioSourceComponent;
    
    #endregion
}