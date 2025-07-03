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
    [SerializeField] private float transitionDuration = 0.5f;
    [SerializeField] private bool autoFallbackToLocal = true;
    
    private Material originalMaterial;
    private Material remoteMaterial;
    private bool isShowingRemoteStream = false;
    private Coroutine transitionCoroutine;
    private string currentDisplaySession = string.Empty;
    private MaterialPropertyBlock propertyBlock;
    
    // Events
    public static event System.Action<PipelineType, bool, string> OnDisplayModeChanged;
    
    void Start()
    {
        ValidateComponents();
        InitializeRenderer();
        
        Debug.Log($"[WebRTCRenderer] Initialized for {pipelineType}");
    }
    
    private void ValidateComponents()
    {
        if (sharedRenderer == null)
        {
            Debug.LogError($"[WebRTCRenderer] No MeshRenderer assigned for {pipelineType}");
            return;
        }
        
        if (localNdiReceiver == null)
        {
            Debug.LogWarning($"[WebRTCRenderer] No local NDI receiver assigned for {pipelineType}");
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
    
    public void ShowRemoteStream(Texture remoteTexture, string sessionId = "")
    {
        // Always recreate material for fresh connections
        if (remoteMaterial != null)
        {
            DestroyImmediate(remoteMaterial);
        }
    
        remoteMaterial = new Material(originalMaterial.shader);
        remoteMaterial.mainTexture = remoteTexture;
        sharedRenderer.material = remoteMaterial;
        
        // Disable local NDI
        SetNdiReceiverActive(false);
        
 
        // Get current property block values to preserve other properties
        sharedRenderer.GetPropertyBlock(propertyBlock);
        
        // Set the texture through property block
        propertyBlock.SetTexture("_BaseMap", remoteTexture);
        propertyBlock.SetTexture("_MainTex", remoteTexture); // For URP materials
        
        // Apply the property block
        sharedRenderer.SetPropertyBlock(propertyBlock);
        
        isShowingRemoteStream = true;
        currentDisplaySession = sessionId;
        OnDisplayModeChanged?.Invoke(pipelineType, true, sessionId);
        
        Debug.Log($"[WebRTCRenderer] Applied remote texture via MaterialPropertyBlock for {pipelineType}");

    }
    
    public void ShowLocalNDI()
    {
        Debug.Log($"[WebRTCRenderer] ShowLocalNDI called for {pipelineType}");
        
        // Enable local NDI
        SetNdiReceiverActive(true);
        
        // Clear the MaterialPropertyBlock to revert to material defaults
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
            
        propertyBlock.Clear(); // This removes all property block overrides
        sharedRenderer.SetPropertyBlock(propertyBlock);
        
        // Clean up remote session
        isShowingRemoteStream = false;
        currentDisplaySession = string.Empty;
        OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
        
        Debug.Log($"[WebRTCRenderer] Cleared MaterialPropertyBlock, showing local NDI for {pipelineType}");

    }
    
    public void ClearDisplay()
    {Debug.Log($"[WebRTCRenderer] ClearDisplay called for {pipelineType}");
        
        SetNdiReceiverActive(false);
        
        // Clear property block and set to original material
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();
            
        propertyBlock.Clear();
        sharedRenderer.SetPropertyBlock(propertyBlock);
        sharedRenderer.material = originalMaterial;
        
        isShowingRemoteStream = false;
        currentDisplaySession = string.Empty;
        OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
        
        Debug.Log($"[WebRTCRenderer] Display cleared for {pipelineType}");
    }
    
    public void HandleStreamFailure()
    {
        Debug.LogWarning($"[WebRTCRenderer] Stream failure detected for {pipelineType}");
        
        if (autoFallbackToLocal)
        {
            ShowLocalNDI();
        }
        else
        {
            ClearDisplay();
        }
    }
    
    private void SetNdiReceiverActive(bool active)
    {
        if (localNdiReceiver != null)
        {
            localNdiReceiver.gameObject.SetActive(active);
            
            if (debugMode)
                Debug.Log($"[WebRTCRenderer] NDI receiver {(active ? "enabled" : "disabled")} for {pipelineType}");
        }
        else if (debugMode)
        {
            Debug.LogWarning($"[WebRTCRenderer] No local NDI receiver assigned for {pipelineType}");
        }
    }
    
    private IEnumerator TransitionToMaterial(Material targetMaterial, System.Action onComplete = null)
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);
        
        if (sharedRenderer != null && targetMaterial != null)
        {
            // Could add fade effects here in the future
            sharedRenderer.material = targetMaterial;
        }
        
        yield return null;
        onComplete?.Invoke();
    }
    
    private IEnumerator TransitionToLocalNDI(System.Action onComplete = null)
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);
        
        yield return null;
        onComplete?.Invoke();
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
        if (remoteMaterial != null)
        {
            DestroyImmediate(remoteMaterial);
            remoteMaterial = null;
        }
        
        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }
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