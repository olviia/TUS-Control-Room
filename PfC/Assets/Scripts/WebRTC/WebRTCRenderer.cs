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
    
    private Material originalMaterial;
    private Material remoteMaterial;
    private bool isShowingRemoteStream = false;
    private Coroutine transitionCoroutine;
    
    // Events for debugging
    public static event System.Action<PipelineType, bool> OnDisplayModeChanged;
    
    void Start()
    {
        if (sharedRenderer == null)
        {
            Debug.LogError($"[WebRTCRenderer] No MeshRenderer assigned for {pipelineType}");
            return;
        }
        
        originalMaterial = sharedRenderer.material;
        
        // Start by showing local NDI
        ShowLocalNDI();
        
        Debug.Log($"[WebRTCRenderer] Initialized for {pipelineType}");
    }
    
    public void ShowRemoteStream(VideoStreamTrack remoteTrack)
    {
        if (remoteTrack?.Texture == null)
        {
            Debug.LogError($"[WebRTCRenderer] Invalid remote track for {pipelineType}");
            return;
        }
        
        Debug.Log($"[WebRTCRenderer] 🔴 ShowRemoteStream called for {pipelineType}");
        
        // Create material for remote stream
        if (remoteMaterial == null)
        {
            remoteMaterial = new Material(originalMaterial.shader);
        }
        remoteMaterial.mainTexture = remoteTrack.Texture;
        
        // Disable local NDI GameObject
        SetNdiReceiverActive(false);
        
        // Transition to remote material
        StartCoroutine(TransitionToMaterial(remoteMaterial, () => {
            isShowingRemoteStream = true;
            OnDisplayModeChanged?.Invoke(pipelineType, true);
            Debug.Log($"[WebRTCRenderer] 📺 {pipelineType} now showing remote stream");
        }));
    }
    
    public void ShowLocalNDI()
    {
        Debug.Log($"[WebRTCRenderer] 🔴 ShowLocalNDI called for {pipelineType}");
        
        // Enable local NDI GameObject - it will handle the material
        SetNdiReceiverActive(true);
        
        // Clean up remote material
        if (remoteMaterial != null && isShowingRemoteStream)
        {
            StartCoroutine(TransitionToLocalNDI(() => {
                isShowingRemoteStream = false;
                OnDisplayModeChanged?.Invoke(pipelineType, false);
                Debug.Log($"[WebRTCRenderer] 📺 {pipelineType} now showing local NDI");
            }));
        }
        else
        {
            isShowingRemoteStream = false;
            OnDisplayModeChanged?.Invoke(pipelineType, false);
        }
    }
    
    public void ClearDisplay()
    {
        Debug.Log($"[WebRTCRenderer] 🔴 ClearDisplay called for {pipelineType}");
        
        SetNdiReceiverActive(false);
        
        StartCoroutine(TransitionToMaterial(originalMaterial, () => {
            isShowingRemoteStream = false;
            OnDisplayModeChanged?.Invoke(pipelineType, false);
            Debug.Log($"[WebRTCRenderer] 📺 {pipelineType} display cleared");
        }));
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
        
        // Immediate switch for now - you can add fade effects here if desired
        if (sharedRenderer != null)
        {
            sharedRenderer.material = targetMaterial;
        }
        
        yield return null; // Wait one frame
        
        onComplete?.Invoke();
    }
    
    private IEnumerator TransitionToLocalNDI(System.Action onComplete = null)
    {
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);
        
        // The NDI receiver will handle setting its own material when activated
        // We just need to make sure we're not overriding it
        
        yield return null; // Wait one frame for NDI to set its material
        
        onComplete?.Invoke();
    }
    
    // Public properties for external inspection
    public bool IsShowingRemoteStream => isShowingRemoteStream;
    
    public string GetCurrentDisplayMode()
    {
        if (isShowingRemoteStream)
            return "Remote WebRTC";
        else if (localNdiReceiver != null && localNdiReceiver.gameObject.activeInHierarchy)
            return "Local NDI";
        else
            return "Blank";
    }
    
    public Texture GetCurrentTexture()
    {
        if (sharedRenderer?.material?.mainTexture != null)
            return sharedRenderer.material.mainTexture;
        return null;
    }
    
    void OnDestroy()
    {
        // Clean up created materials
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
    
    // Debug methods
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
        // Auto-assign renderer if not set
        if (sharedRenderer == null)
        {
            sharedRenderer = GetComponent<MeshRenderer>();
        }
        
        // Auto-find NDI receiver if not set
        if (localNdiReceiver == null)
        {
            localNdiReceiver = GetComponentInChildren<NdiReceiver>();
        }
    }
}