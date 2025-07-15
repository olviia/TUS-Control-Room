using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using System;
using Unity.Collections;

/// <summary>
/// WebRTC Audio Streamer using OnAudioFilterRead approach
/// Properly intercepts NDI audio filters and controls WebRTC audio at filter level
/// </summary>
public class FilterBasedAudioStreamer : MonoBehaviour
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
    private AudioSource sendingAudioSource;
    private AudioSource receivingAudioSource;
    private GameObject receivingAudioGameObject;
    
    // Audio Filter Management
    private NDIAudioInterceptor ndiInterceptor;
    private WebRTCAudioFilter webrtcFilter;
    private float[] interceptedAudioBuffer;
    private bool isCapturingAudio = false;
    
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
            
            // Apply volume to WebRTC filter
            if (webrtcFilter != null)
            {
                webrtcFilter.SetVolume(_audioVolume);
            }
            
            Debug.Log($"[🎵Filter-{pipelineType}] Volume set to {_audioVolume:F2}");
        }
    }
    
    #region Unity Lifecycle
    
    void Start()
    {
        InitializeAudioSystems();
        Debug.Log($"[🎵Filter-{pipelineType}] Filter-based audio streamer initialized");
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
            Debug.LogWarning($"[🎵Filter-{pipelineType}] Already streaming audio");
            return sendingAudioTrack;
        }
        
        currentSessionId = sessionId;
        isStreaming = true;
        
        SetupNDIAudioInterception();
        CreateSendingAudioTrack();
        
        OnAudioStreamStateChanged?.Invoke(pipelineType, true, sessionId);
        Debug.Log($"[🎵Filter-{pipelineType}] Started audio streaming for session: {sessionId}");
        
        return sendingAudioTrack;
    }
    
    /// <summary>
    /// Prepare to receive remote audio
    /// </summary>
    public AudioSource PrepareAudioReceiving(string sessionId)
    {
        if (isReceiving)
        {
            Debug.LogWarning($"[🎵Filter-{pipelineType}] Already receiving audio");
            return receivingAudioSource;
        }
        
        currentSessionId = sessionId;
        isReceiving = true;
        
        CreateReceivingAudioSource();
        
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, sessionId);
        Debug.Log($"[🎵Filter-{pipelineType}] Prepared for audio receiving: {sessionId}");
        
        return receivingAudioSource;
    }
    
    /// <summary>
    /// Handle incoming WebRTC audio track
    /// </summary>
    public void HandleIncomingAudioTrack(AudioStreamTrack audioTrack)
    {
        if (receivingAudioSource == null)
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] No receiving AudioSource prepared");
            return;
        }
        
        // Set up WebRTC filter to handle incoming audio
        if (webrtcFilter != null)
        {
            webrtcFilter.SetIncomingAudioTrack(audioTrack);
        }
        
        Debug.Log($"[🎵Filter-{pipelineType}] Incoming audio track connected to filter");
        
        // Verify audio setup
        StartCoroutine(VerifyAudioSetup());
    }
    
    /// <summary>
    /// Stop current audio operations
    /// </summary>
    public void StopAudioOperations()
    {
        StopAllAudioOperations();
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, string.Empty);
        Debug.Log($"[🎵Filter-{pipelineType}] Audio operations stopped");
    }
    
    #endregion
    
    #region NDI Audio Interception
    
    private void SetupNDIAudioInterception()
    {
        if (ndiAudioSource == null)
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] No NDI audio source for interception");
            return;
        }
        
        // Find NDI's AudioSource
        var ndiAudioSourceComponent = ndiAudioSource.GetComponentInChildren<AudioSource>();
        if (ndiAudioSourceComponent == null)
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] No AudioSource found in NDI receiver");
            return;
        }
        
        // Add interceptor to NDI's AudioSource GameObject
        ndiInterceptor = ndiAudioSourceComponent.gameObject.GetComponent<NDIAudioInterceptor>();
        if (ndiInterceptor == null)
        {
            ndiInterceptor = ndiAudioSourceComponent.gameObject.AddComponent<NDIAudioInterceptor>();
        }
        
        ndiInterceptor.Initialize(pipelineType, this);
        isCapturingAudio = true;
        
        Debug.Log($"[🎵Filter-{pipelineType}] NDI audio interception setup complete");
    }
    
    /// <summary>
    /// Called by NDIAudioInterceptor when audio data is available
    /// </summary>
    public void OnNDIAudioData(float[] audioData, int channels)
    {
        if (!isStreaming || sendingAudioTrack == null) return;
    
        // Feed audio data to WebRTC 
        try
        {
            sendingAudioTrack.SetData(audioData, channels, 48000);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] Error feeding audio to WebRTC: {e.Message}");
        }
    }
    #endregion
    
    #region WebRTC Audio Management
    
    private void CreateSendingAudioTrack()
    {
        // Create a dummy AudioSource for WebRTC AudioStreamTrack
        if (sendingAudioSource == null)
        {
            var audioGO = new GameObject($"WebRTC_Audio_Sender_{pipelineType}");
            audioGO.transform.SetParent(transform, false);
            audioGO.hideFlags = HideFlags.DontSave;
            
            sendingAudioSource = audioGO.AddComponent<AudioSource>();
            sendingAudioSource.volume = 0f; // We don't want local playback
            sendingAudioSource.spatialBlend = 0f;
            sendingAudioSource.loop = true;
            sendingAudioSource.playOnAwake = false;
            
            // Create dummy clip
            var dummyClip = AudioClip.Create("WebRTC_Dummy", AudioSettings.outputSampleRate, 2, AudioSettings.outputSampleRate, false);
            sendingAudioSource.clip = dummyClip;
        }
        
        // Create WebRTC AudioStreamTrack
        sendingAudioTrack = new AudioStreamTrack(sendingAudioSource);
        sendingAudioSource.Play();
        
        Debug.Log($"[🎵Filter-{pipelineType}] Sending audio track created");
    }
    
    private void CreateReceivingAudioSource()
    {
        if (receivingAudioGameObject == null)
        {
            receivingAudioGameObject = new GameObject($"WebRTC_Audio_Receiver_{pipelineType}");
            receivingAudioGameObject.hideFlags = HideFlags.DontSave;
            
            // Position the audio source correctly
            if (audioSourcePosition != null)
            {
                receivingAudioGameObject.transform.position = audioSourcePosition.position;
                receivingAudioGameObject.transform.rotation = audioSourcePosition.rotation;

            }
            else
            {
                receivingAudioGameObject.transform.position = transform.position;
            }
            
            receivingAudioSource = receivingAudioGameObject.AddComponent<AudioSource>();
            
            // Configure for 3D spatial audio
            receivingAudioSource.spatialBlend = spatialBlend;
            receivingAudioSource.volume = 1.0f; // Volume controlled by filter
            receivingAudioSource.minDistance = minDistance;
            receivingAudioSource.maxDistance = maxDistance;
            receivingAudioSource.rolloffMode = AudioRolloffMode.Linear;
            receivingAudioSource.playOnAwake = false;
            receivingAudioSource.loop = true;
            
            // Add WebRTC audio filter
            webrtcFilter = receivingAudioGameObject.AddComponent<WebRTCAudioFilter>();
            webrtcFilter.Initialize(pipelineType, _audioVolume);
            
            // Create dummy clip to trigger OnAudioFilterRead
            var dummyClip = AudioClip.Create("WebRTC_Receiver_Dummy", AudioSettings.outputSampleRate, 2, AudioSettings.outputSampleRate, true, OnDummyAudioRead);
            receivingAudioSource.clip = dummyClip;
        }
        
        receivingAudioGameObject.SetActive(true);
        receivingAudioSource.Play(); // Start playing to trigger OnAudioFilterRead
        
        Debug.Log($"[🎵Filter-{pipelineType}] Receiving audio source created at {receivingAudioGameObject.transform.position}");
    }
    
    /// <summary>
    /// Dummy audio callback to keep AudioSource active
    /// </summary>
    private void OnDummyAudioRead(float[] data)
    {
        // Fill with silence - actual audio comes from WebRTC filter
        Array.Clear(data, 0, data.Length);
    }
    
    #endregion
    
    #region Audio Verification and Debugging
    
    private IEnumerator VerifyAudioSetup()
    {
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[🎵Filter-{pipelineType}] Audio Verification:");
        
        if (receivingAudioSource != null)
        {
            Debug.Log($"  - Receiving AudioSource Playing: {receivingAudioSource.isPlaying}");
            Debug.Log($"  - WebRTC Filter Active: {webrtcFilter != null && webrtcFilter.enabled}");
        }
        
        if (ndiInterceptor != null)
        {
            Debug.Log($"  - NDI Interceptor Active: {ndiInterceptor.enabled}");
            Debug.Log($"  - Audio Capturing: {isCapturingAudio}");
        }
        
        // Check AudioListener
        var audioListener = FindObjectOfType<AudioListener>();
        Debug.Log($"  - AudioListener found: {audioListener != null}");
        if (audioListener != null)
        {
            Debug.Log($"  - AudioListener enabled: {audioListener.enabled}");
            Debug.Log($"  - AudioListener volume: {AudioListener.volume}");
        }
    }
    
    public void DebugAudioState()
    {
        Debug.Log($"[🎵Filter-{pipelineType}] Current State:");
        Debug.Log($"  - Streaming: {isStreaming}");
        Debug.Log($"  - Receiving: {isReceiving}");
        Debug.Log($"  - Capturing: {isCapturingAudio}");
        Debug.Log($"  - Session: {currentSessionId}");
        Debug.Log($"  - Volume: {_audioVolume}");
        
        if (ndiInterceptor != null)
            Debug.Log($"  - NDI Interceptor: {ndiInterceptor.enabled}");
        if (webrtcFilter != null)
            Debug.Log($"  - WebRTC Filter: {webrtcFilter.enabled}");
    }
    
    #endregion
    
    #region Cleanup
    
    private void StopAllAudioOperations()
    {
        isStreaming = false;
        isReceiving = false;
        isCapturingAudio = false;
        
        if (receivingAudioSource != null)
        {
            receivingAudioSource.Stop();
        }
        
        if (sendingAudioSource != null)
        {
            sendingAudioSource.Stop();
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
        if (sendingAudioSource != null && sendingAudioSource.gameObject != null)
        {
            DestroyImmediate(sendingAudioSource.gameObject);
        }
        
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
    
    #endregion
}