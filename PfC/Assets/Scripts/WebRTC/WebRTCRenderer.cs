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
        }
        
        ShowLocalNDI();
    }
    
    public void ShowRemoteStream(VideoStreamTrack remoteTrack, string sessionId = "")
    {
        if (remoteTrack?.Texture == null)
        {
            Debug.LogError($"[WebRTCRenderer] Invalid remote track for {pipelineType}");
            return;
        }
        
        Debug.Log($"[WebRTCRenderer] ShowRemoteStream called for {pipelineType} session {sessionId}");
        
        // Create or update remote material
        if (remoteMaterial == null)
        {
            remoteMaterial = new Material(originalMaterial.shader);
        }
        remoteMaterial.mainTexture = remoteTrack.Texture;
        
        // Disable local NDI
        SetNdiReceiverActive(false);
        
        // Transition to remote material
        StartCoroutine(TransitionToMaterial(remoteMaterial, () => {
            isShowingRemoteStream = true;
            currentDisplaySession = sessionId;
            OnDisplayModeChanged?.Invoke(pipelineType, true, sessionId);
            Debug.Log($"[WebRTCRenderer] Now showing remote stream for {pipelineType}");
        }));
    }
    
    public void ShowLocalNDI()
    {
        Debug.Log($"[WebRTCRenderer] ShowLocalNDI called for {pipelineType}");
        
        // Enable local NDI
        SetNdiReceiverActive(true);
        
        // Clean up remote session
        if (isShowingRemoteStream)
        {
            StartCoroutine(TransitionToLocalNDI(() => {
                isShowingRemoteStream = false;
                currentDisplaySession = string.Empty;
                OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
                Debug.Log($"[WebRTCRenderer] Now showing local NDI for {pipelineType}");
            }));
        }
        else
        {
            isShowingRemoteStream = false;
            currentDisplaySession = string.Empty;
            OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
        }
    }
    
    public void ClearDisplay()
    {
        Debug.Log($"[WebRTCRenderer] ClearDisplay called for {pipelineType}");
        
        SetNdiReceiverActive(false);
        
        StartCoroutine(TransitionToMaterial(originalMaterial, () => {
            isShowingRemoteStream = false;
            currentDisplaySession = string.Empty;
            OnDisplayModeChanged?.Invoke(pipelineType, false, string.Empty);
            Debug.Log($"[WebRTCRenderer] Display cleared for {pipelineType}");
        }));
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