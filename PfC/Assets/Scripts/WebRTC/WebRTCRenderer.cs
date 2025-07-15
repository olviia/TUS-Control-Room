using UnityEngine;
using Unity.WebRTC;
using BroadcastPipeline;
using Klak.Ndi;
using System.Collections;

/// <summary>
/// Updated WebRTC Renderer with separated audio handling
/// Now focuses purely on video rendering while audio is handled by WebRTCAudioStreamer
/// </summary>
public class WebRTCRenderer : MonoBehaviour
{
    [Header("Shared Renderer")]
    public MeshRenderer sharedRenderer;
    public PipelineType pipelineType;
    public NdiReceiver localNdiReceiver;
    public NdiReceiver localNdiReceiverCaptions;
    
    [Header("Audio Component")]
    public FilterBasedAudioStreamer audioStreamer; // Reference to the audio streamer
    
    [Header("Display Settings")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool autoFallbackToLocal = true;
    
    // Video rendering components
    private Material originalMaterial;
    public bool isShowingRemoteStream = false;
    private string currentDisplaySession = string.Empty;
    private MaterialPropertyBlock propertyBlock;
    
    // Events
    public static event System.Action<PipelineType, bool, string> OnDisplayModeChanged;
    
    #region Unity Lifecycle
    
    void Start()
    {
        ValidateComponents();
        InitializeRenderer();
        
        Debug.Log($"[🖥️Renderer] Initialized for {pipelineType}");
    }
    
    void OnDestroy()
    {
        Debug.Log($"[🖥️Renderer] Destroyed for {pipelineType}");
    }
    
    #endregion
    
    #region Initialization
    
    private void ValidateComponents()
    {
        if (sharedRenderer == null)
        {
            Debug.LogError($"[🖥️Renderer] No MeshRenderer assigned for {pipelineType}");
            return;
        }
        
        if (localNdiReceiver == null)
        {
            Debug.LogWarning($"[🖥️Renderer] No local NDI receiver assigned for {pipelineType}");
        }
        
        if (audioStreamer == null)
        {
            Debug.LogWarning($"[🖥️Renderer] No audio streamer assigned for {pipelineType}");
        }
    }
    
    private void InitializeRenderer()
    {
        if (sharedRenderer != null)
        {
            originalMaterial = sharedRenderer.material;
            propertyBlock = new MaterialPropertyBlock();
        }
        
        ShowLocalNDI();
    }
    
    #endregion
    
    #region Video Display Management
    
    /// <summary>
    /// Show remote stream - OPTIMIZED for instant switching
    /// </summary>
    public void ShowRemoteStream(Texture remoteTexture, string sessionId = "")
    {
        Debug.Log($"[🖥️Renderer] ShowRemoteStream called for {pipelineType} - INSTANT switch");
        
        if (sharedRenderer == null || remoteTexture == null)
        {
            Debug.LogError($"[🖥️Renderer] Missing components for remote stream");
            return;
        }
        
        // INSTANT texture switch - no material recreation
        SetTextureInstant(remoteTexture);
        
        // Disable local NDI immediately
        SetNdiReceiverActive(false);
        
        // Update state
        isShowingRemoteStream = true;
        currentDisplaySession = sessionId;
        OnDisplayModeChanged?.Invoke(pipelineType, true, sessionId);
        
        Debug.Log($"[🖥️Renderer] Remote texture applied INSTANTLY for {pipelineType}");
    }
    
    /// <summary>
    /// Handle incoming WebRTC audio track - delegate to audio streamer
    /// </summary>
    public void HandleRemoteAudioTrack(AudioStreamTrack audioTrack)
    {
        if (audioStreamer != null)
        {
            audioStreamer.HandleIncomingAudioTrack(audioTrack);
            Debug.Log($"[🖥️Renderer] Audio track delegated to audio streamer");
        }
        else
        {
            Debug.LogError($"[🖥️Renderer] No audio streamer available for audio track");
        }
    }
    
    /// <summary>
    /// Get audio source for WebRTC audio connection
    /// </summary>
    public AudioSource GetRemoteAudioSource()
    {
        if (audioStreamer != null)
        {
            return audioStreamer.ReceivingAudioSource;
        }
        
        Debug.LogError($"[🖥️Renderer] No audio streamer available");
        return null;
    }
    
    /// <summary>
    /// Prepare for receiving remote audio
    /// </summary>
    public void PrepareRemoteAudio(string sessionId = "")
    {
        if (audioStreamer != null)
        {
            audioStreamer.PrepareAudioReceiving(sessionId);
            Debug.Log($"[🖥️Renderer] Remote audio preparation delegated to audio streamer");
        }
        else
        {
            Debug.LogError($"[🖥️Renderer] No audio streamer available for remote audio preparation");
        }
    }
    
    /// <summary>
    /// Show local NDI - INSTANT switch back
    /// </summary>
    public void ShowLocalNDI()
    {
        Debug.Log($"[🖥️Renderer] ShowLocalNDI called for {pipelineType} - INSTANT switch");
        
        // Enable local NDI immediately
        SetNdiReceiverActive(true);
        
        // Clear property block immediately - revert to material defaults
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
            
        propertyBlock.Clear(); // Remove all overrides
        sharedRenderer.SetPropertyBlock(propertyBlock);
        
        // Stop remote audio
        if (audioStreamer != null)
        {
            audioStreamer.StopAudioOperations();
        }
        
        // Enable local audio
        SetLocalAudioActive(true);
        
        // Update state
        isShowingRemoteStream = false;
        currentDisplaySession = string.Empty;
        OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
        
        Debug.Log($"[🖥️Renderer] Local NDI restored INSTANTLY for {pipelineType}");
    }
    
    /// <summary>
    /// Clear display - INSTANT
    /// </summary>
    public void ClearDisplay()
    {
        Debug.Log($"[🖥️Renderer] ClearDisplay called for {pipelineType} - INSTANT");
        
        SetNdiReceiverActive(false);
        
        // Clear all overrides instantly
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
            
        propertyBlock.Clear();
        sharedRenderer.SetPropertyBlock(propertyBlock);
        sharedRenderer.material = originalMaterial;
        
        // Audio clearing
        SetLocalAudioActive(false);
        if (audioStreamer != null)
        {
            audioStreamer.StopAudioOperations();
        }
        
        isShowingRemoteStream = false;
        currentDisplaySession = string.Empty;
        OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
        
        Debug.Log($"[🖥️Renderer] Display cleared INSTANTLY for {pipelineType}");
    }
    
    /// <summary>
    /// Handle stream failure with instant fallback
    /// </summary>
    public void HandleStreamFailure()
    {
        Debug.LogWarning($"[🖥️Renderer] Stream failure detected for {pipelineType} - INSTANT fallback");
        
        if (autoFallbackToLocal)
        {
            ShowLocalNDI();
        }
        else
        {
            ClearDisplay();
        }
    }
    
    #endregion
    
    #region Private Helper Methods
    
    /// <summary>
    /// Instant texture switching using property blocks only
    /// </summary>
    private void SetTextureInstant(Texture texture)
    {
        // Use property block for instant switching - no material changes
        sharedRenderer.GetPropertyBlock(propertyBlock);
        
        // Set texture through property block (fastest method)
        propertyBlock.SetTexture("_BaseMap", texture);
        propertyBlock.SetTexture("_MainTex", texture); // Fallback for different shaders
        
        // Apply immediately
        sharedRenderer.SetPropertyBlock(propertyBlock);
    }
    
    /// <summary>
    /// Control local NDI audio
    /// </summary>
    private void SetLocalAudioActive(bool active)
    {
        if (localNdiReceiver != null)
        {
            var audioSources = localNdiReceiver.GetComponentsInChildren<AudioSource>();
            foreach (var source in audioSources)
            {
                source.enabled = active;
            }
        }
    }
    
    /// <summary>
    /// Set NDI receiver active state
    /// </summary>
    private void SetNdiReceiverActive(bool active)
    {
        if (localNdiReceiver != null)
        {
            localNdiReceiver.gameObject.SetActive(active);
            if (localNdiReceiverCaptions != null)
            {
                localNdiReceiverCaptions.gameObject.SetActive(active);
            }
            
            if (debugMode)
                Debug.Log($"[🖥️Renderer] NDI receiver {(active ? "enabled" : "disabled")} for {pipelineType}");
        }
        else if (debugMode)
        {
            Debug.LogWarning($"[🖥️Renderer] No local NDI receiver assigned for {pipelineType}");
        }
    }
    
    #endregion
    
    #region Public Properties
    
    public bool IsShowingRemoteStream => isShowingRemoteStream;
    public string CurrentDisplaySession => currentDisplaySession;
    
    public string GetCurrentDisplayMode()
    {
        if (isShowingRemoteStream)
            return $"Remote WebRTC ({currentDisplaySession})";
        else if (localNdiReceiver != null && localNdiReceiver.gameObject.activeInHierarchy)
            return "Local NDI";
        else
            return "Blank";
    }
    
    public Texture GetCurrentTexture()
    {
        return sharedRenderer?.material?.mainTexture;
    }
    
    // Audio-related properties (delegated to audio streamer)
    public float AudioVolume
    {
        get => audioStreamer?.AudioVolume ?? 0f;
        set
        {
            if (audioStreamer != null)
                audioStreamer.AudioVolume = value;
        }
    }
    
    public bool IsReceivingAudio => audioStreamer?.IsReceiving ?? false;
    
    #endregion
    
    #region Debug Methods
    
    [ContextMenu("Force Show Local NDI")]
    public void DebugShowLocalNDI()
    {
        ShowLocalNDI();
    }
    
    [ContextMenu("Clear Display")]
    public void DebugClearDisplay()
    {
        ClearDisplay();
    }
    
    [ContextMenu("Debug Audio State")]
    public void DebugAudioState()
    {
        if (audioStreamer != null)
        {
            audioStreamer.DebugAudioState();
        }
        else
        {
            Debug.Log($"[🖥️Renderer] No audio streamer assigned for {pipelineType}");
        }
    }
    
    void OnValidate()
    {
        if (sharedRenderer == null)
        {
            sharedRenderer = GetComponent<MeshRenderer>();
        }
        
        if (localNdiReceiver == null)
        {
            localNdiReceiver = GetComponentInChildren<NdiReceiver>();
        }
        
        if (audioStreamer == null)
        {
            audioStreamer = GetComponent<FilterBasedAudioStreamer>();
        }
    }
    
    #endregion
}