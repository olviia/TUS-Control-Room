using UnityEngine;
using Unity.WebRTC;
using BroadcastPipeline;
using UnityEngine.Serialization;

/// <summary>
/// Receives WebRTC audio and plays it through AudioSource
/// Controlled by WebRTCRenderer - follows same lifecycle as video
/// Attach this to the AudioPosition GameObject referenced in WebRTCRenderer
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class WebRTCAudioReceiver : MonoBehaviour
{
    [Header("Audio Configuration")]
    [SerializeField] private float volume = 1.0f;
    [SerializeField] private bool spatialAudio = true;
    [SerializeField] private float spatialBlend = 1.0f;
    [SerializeField] private bool debugMode = false;
    [SerializeField] private AudioSource[] additionalAudioSources;
    
    
    // Audio components
    private AudioSource audioSource;
    private AudioStreamTrack receivedAudioTrack;
    
    // State management
    private bool isReceivingAudio = false;
    private bool isInitialized = false;
    private string currentSessionId = string.Empty;
    private PipelineType pipelineType;
    
    // Events
    public static event System.Action<PipelineType, bool, string> OnAudioStateChanged;
    
    #region Unity Lifecycle
    
    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        InitializeAudioReceiver();
    }
    
    void Start()
    {
        ConfigureAudioSource();
        isInitialized = true;
        
        Debug.Log($"[🔊AudioReceiver] Initialized for pipeline: {pipelineType}");
    }
    
    void OnDestroy()
    {
        StopReceivingAudio();
        Debug.Log($"[🔊AudioReceiver] Destroyed for pipeline: {pipelineType}");
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeAudioReceiver()
    {
        // Try to determine pipeline type from hierarchy
        pipelineType = DeterminePipelineType();
        
        if (audioSource == null)
        {
            Debug.LogError("[🔊AudioReceiver] No AudioSource component found!");
            return;
        }
    }
    
    private void ConfigureAudioSource()
    {
        if (audioSource == null) return;
        
        // Configure AudioSource for WebRTC audio
        audioSource.clip = null; // We'll use SetTrack instead
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = volume;
        
        // Configure spatial audio
        if (spatialAudio)
        {
            audioSource.spatialBlend = spatialBlend;
            audioSource.dopplerLevel = 0.1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 1f;
            audioSource.maxDistance = 1000f;
        }
        else
        {
            audioSource.spatialBlend = 0f; // 2D audio
        }

        foreach (AudioSource source in additionalAudioSources)
        {
            source.clip = null;
            source.spatialBlend = spatialBlend;
            source.dopplerLevel = 0.1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 10f;
        }
        
        // Disable initially
        audioSource.enabled = false;
        
        Debug.Log($"[🔊AudioReceiver] AudioSource configured - Spatial: {spatialAudio}, Volume: {volume}");
    }
    
    private PipelineType DeterminePipelineType()
    {
        // Look for WebRTCRenderer in parent hierarchy
        var renderer = GetComponentInParent<WebRTCRenderer>();
        if (renderer != null)
        {
            return renderer.pipelineType;
        }
        
        // Fallback to name-based detection
        Transform current = transform;
        while (current != null)
        {
            string name = current.name.ToLower();
            if (name.Contains("studio"))
                return PipelineType.StudioLive;
            else if (name.Contains("tv"))
                return PipelineType.TVLive;
            
            current = current.parent;
        }
        
        Debug.LogWarning("[🔊AudioReceiver] Could not determine pipeline type, defaulting to StudioLive");
        return PipelineType.StudioLive;
    }
    
    #endregion
    
    #region Public Interface
    
    /// <summary>
    /// Start receiving audio from WebRTC track
    /// Called from WebRTCRenderer when remote stream starts
    /// </summary>
    public void StartReceivingAudio(AudioStreamTrack audioTrack, string sessionId = "")
    {
        if (!isInitialized)
        {
            Debug.LogError("[🔊AudioReceiver] Not initialized!");
            return;
        }
        
        if (audioTrack == null)
        {
            Debug.LogError("[🔊AudioReceiver] No audio track provided!");
            return;
        }
        
        if (isReceivingAudio)
        {
            Debug.LogWarning("[🔊AudioReceiver] Already receiving audio, stopping previous...");
            StopReceivingAudio();
        }
        
        // Use Unity WebRTC extension method to set track
        try
        {
            receivedAudioTrack = audioTrack;
            currentSessionId = sessionId;
            
            audioSource.SetTrack(receivedAudioTrack);
            audioSource.enabled = true;
            audioSource.Play();

            foreach (var additionalAudio in additionalAudioSources)
            {
                Debug.Log($"[🔊AudioReceiver] received additional audio");
   
                additionalAudio.SetTrack(receivedAudioTrack);
                additionalAudio.enabled = true;
                additionalAudio.Play();
            }
            
            isReceivingAudio = true;
            OnAudioStateChanged?.Invoke(pipelineType, true, sessionId);
            
            Debug.Log($"[🔊AudioReceiver] Started receiving audio for session: {sessionId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[🔊AudioReceiver] Failed to start audio: {e.Message}");
            StopReceivingAudio();
        }
    }
    
    /// <summary>
    /// Stop receiving audio
    /// Called from WebRTCRenderer when remote stream stops
    /// </summary>
    public void StopReceivingAudio()
    {
        if (!isReceivingAudio) return;
        
        try
        {
            if (audioSource != null)
            {
                audioSource.Stop();
                audioSource.enabled = false;
                
                // Clear the WebRTC track
                if (receivedAudioTrack != null)
                {
                    // Note: SetTrack with null clears the track
                    audioSource.SetTrack(null);
                }
            }
            
            isReceivingAudio = false;
            string sessionId = currentSessionId;
            currentSessionId = string.Empty;
            receivedAudioTrack = null;

            foreach (var additionalAudio in additionalAudioSources)
            {
                additionalAudio.Stop();
                additionalAudio.SetTrack(null);
            }
            
            OnAudioStateChanged?.Invoke(pipelineType, false, sessionId);
            
            Debug.Log($"[🔊AudioReceiver] Stopped receiving audio for session: {sessionId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[🔊AudioReceiver] Error stopping audio: {e.Message}");
        }
    }
    
    /// <summary>
    /// Set volume for received audio
    /// </summary>
    public void SetVolume(float newVolume)
    {
        volume = Mathf.Clamp01(newVolume);
        if (audioSource != null)
        {
            audioSource.volume = volume;
        }
    }
    
    /// <summary>
    /// Toggle spatial audio
    /// </summary>
    public void SetSpatialAudio(bool enabled)
    {
        spatialAudio = enabled;
        
        if (audioSource != null)
        {
            audioSource.spatialBlend = spatialAudio ? spatialBlend : 0f;
        }
    }
    
    /// <summary>
    /// Handle audio failure - called when track is lost
    /// </summary>
    public void HandleAudioFailure()
    {
        Debug.LogWarning($"[🔊AudioReceiver] Audio failure detected for pipeline: {pipelineType}");
        StopReceivingAudio();
    }
    
    #endregion
    
    #region Properties
    
    /// <summary>
    /// Check if currently receiving audio
    /// </summary>
    public bool IsReceivingAudio => isReceivingAudio;
    
    /// <summary>
    /// Get current session ID
    /// </summary>
    public string CurrentSessionId => currentSessionId;
    
    /// <summary>
    /// Get pipeline type
    /// </summary>
    public PipelineType PipelineType => pipelineType;
    
    /// <summary>
    /// Get current volume
    /// </summary>
    public float Volume => volume;
    
    /// <summary>
    /// Check if spatial audio is enabled
    /// </summary>
    public bool IsSpatialAudio => spatialAudio;
    
    /// <summary>
    /// Get audio track info for debugging
    /// </summary>
    public string GetAudioTrackInfo()
    {
        if (receivedAudioTrack == null)
            return "No audio track";
        
        return $"Track ID: {receivedAudioTrack.Id}, Enabled: {receivedAudioTrack.Enabled}";
    }
    
    #endregion
    
    #region Debug Methods
    
    [ContextMenu("Test Audio Start")]
    public void DebugStartAudio()
    {
        Debug.Log("[🔊AudioReceiver] Debug start called - this requires a real AudioStreamTrack");
    }
    
    [ContextMenu("Test Audio Stop")]
    public void DebugStopAudio()
    {
        StopReceivingAudio();
    }
    
    [ContextMenu("Print Audio Status")]
    public void PrintAudioStatus()
    {
        string status = $"[🔊AudioReceiver] Status - " +
                       $"Receiving: {isReceivingAudio}, " +
                       $"Session: {currentSessionId}, " +
                       $"Volume: {volume}, " +
                       $"Spatial: {spatialAudio}, " +
                       $"AudioSource Enabled: {(audioSource != null ? audioSource.enabled.ToString() : "null")}, " +
                       $"Track: {GetAudioTrackInfo()}";
        
        Debug.Log(status);
    }
    
    void OnValidate()
    {
        // Update settings in real-time during development
        if (audioSource != null && Application.isPlaying)
        {
            audioSource.volume = volume;
            audioSource.spatialBlend = spatialAudio ? spatialBlend : 0f;
        }
    }
    
    #endregion
}