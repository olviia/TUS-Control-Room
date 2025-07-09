using UnityEngine;
using Unity.WebRTC;
using BroadcastPipeline;
using Klak.Ndi;
using System.Collections;

public class WebRTCRenderer : MonoBehaviour
{
    [Header("Shared Renderer")]
    public MeshRenderer sharedRenderer;
    public PipelineType pipelineType;
    public NdiReceiver localNdiReceiver;
    
    [Header("Display Settings")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private bool autoFallbackToLocal = true;
    
    private Material originalMaterial;
    private bool isShowingRemoteStream = false;
    private string currentDisplaySession = string.Empty;
    private MaterialPropertyBlock propertyBlock;
    
    // Events
    public static event System.Action<PipelineType, bool, string> OnDisplayModeChanged;
    
    void Start()
    {
        ValidateComponents();
        InitializeRenderer();
        
        Debug.Log($"[🖥️Renderer] Initialized for {pipelineType}");
    }
    
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
    
    /// <summary>
    /// Set NDI receiver active state
    /// </summary>
    private void SetNdiReceiverActive(bool active)
    {
        if (localNdiReceiver != null)
        {
            localNdiReceiver.gameObject.SetActive(active);
            
            if (debugMode)
                Debug.Log($"[🖥️Renderer] NDI receiver {(active ? "enabled" : "disabled")} for {pipelineType}");
        }
        else if (debugMode)
        {
            Debug.LogWarning($"[🖥️Renderer] No local NDI receiver assigned for {pipelineType}");
        }
    }
    
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

    #endregion
    
    #region Cleanup
    
    void OnDestroy()
    {
        // No materials to cleanup since we're not creating them anymore
        Debug.Log($"[🖥️Renderer] Destroyed for {pipelineType}");
    }

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
    }

    #endregion
}