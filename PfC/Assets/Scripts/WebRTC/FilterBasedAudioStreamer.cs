using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using System;
using System.Linq;
using Unity.Collections;

/// <summary>
/// Enhanced WebRTC Audio Streamer with proper reconnection handling
/// Fixes audio track lifecycle management during reconnections
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
    private int cachedSampleRate = 48000; // Cache the sample rate

    
    // Audio Filter Management
    private NDIAudioInterceptor ndiInterceptor;
    private WebRTCAudioFilter webrtcFilter;
    private float[] interceptedAudioBuffer;
    private bool isCapturingAudio = false;
    
    // State Management
    private bool isStreaming = false;
    private bool isReceiving = false;
    private string currentSessionId = string.Empty;
    private int connectionAttemptCount = 0; // Track reconnection attempts
    
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
        WebRTCStreamer.OnStateChanged += HandleStreamerStateChange;
    }
    
    void OnDestroy()
    {
        WebRTCStreamer.OnStateChanged -= HandleStreamerStateChange;
        StopAllAudioOperations();
        DisposeAudioComponents();
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeAudioSystems()
    {
        if (audioSourcePosition == null)
            audioSourcePosition = transform;
            
        cachedSampleRate = AudioSettings.outputSampleRate;
        connectionAttemptCount = 0;
    }
    
    #endregion
    
    #region Public Interface
    
    /// <summary>
    /// Start streaming audio for WebRTC transmission with reconnection support
    /// </summary>
    public AudioStreamTrack StartAudioStreaming(string sessionId)
    {
        Debug.Log($"[🎵Filter-{pipelineType}] StartAudioStreaming called for session: {sessionId}");
        RefreshNDIConnection();

        // CRITICAL: Clean up previous audio track if reconnecting
        if (isStreaming && currentSessionId != sessionId)
        {
            Debug.Log($"[🎵Filter-{pipelineType}] Reconnection detected - cleaning up previous audio");
            ForceCleanupAudioStreaming();
        }
        
        if (isStreaming && currentSessionId == sessionId)
        {
            Debug.LogWarning($"[🎵Filter-{pipelineType}] Already streaming audio for this session");
            return sendingAudioTrack;
        }
        
        currentSessionId = sessionId;
        connectionAttemptCount++;
        isStreaming = true;
        
        RefreshNDIConnection();

        CreateSendingAudioTrack();

        DebugAudioFlow();
        
        OnAudioStreamStateChanged?.Invoke(pipelineType, true, sessionId);
        Debug.Log($"[🎵Filter-{pipelineType}] Started audio streaming for session: {sessionId} (attempt: {connectionAttemptCount})");
        
        return sendingAudioTrack;
    }
    
    /// <summary>
    /// Prepare to receive remote audio with proper cleanup
    /// </summary>
    public AudioSource PrepareAudioReceiving(string sessionId)
    {
        Debug.Log($"[🎵Filter-{pipelineType}] PrepareAudioReceiving called for session: {sessionId}");
        
        // CRITICAL: Clean up previous receiving setup if reconnecting
        if (isReceiving && currentSessionId != sessionId)
        {
            Debug.Log($"[🎵Filter-{pipelineType}] Reconnection detected - cleaning up previous receiving");
            ForceCleanupAudioReceiving();
        }
        
        if (isReceiving && currentSessionId == sessionId)
        {
            Debug.LogWarning($"[🎵Filter-{pipelineType}] Already receiving audio for this session");
            return receivingAudioSource;
        }
        
        currentSessionId = sessionId;
        connectionAttemptCount++;
        isReceiving = true;
        
        CreateReceivingAudioSource();
        DebugAudioFlow(); // Debug when preparing to receive

        
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, sessionId);
        Debug.Log($"[🎵Filter-{pipelineType}] Prepared for audio receiving: {sessionId} (attempt: {connectionAttemptCount})");
        
        return receivingAudioSource;
    }
    
    /// <summary>
    /// Handle incoming WebRTC audio track with improved reconnection logic
    /// </summary>
    public void HandleIncomingAudioTrack(AudioStreamTrack audioTrack)
    {
        Debug.Log($"[🎵Filter-{pipelineType}] HandleIncomingAudioTrack called! Session: {currentSessionId}, Attempt: {connectionAttemptCount}");
    
        if (receivingAudioSource == null)
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] No receiving AudioSource prepared - recreating");
            CreateReceivingAudioSource();
            
            if (receivingAudioSource == null)
            {
                Debug.LogError($"[🎵Filter-{pipelineType}] Failed to create receiving AudioSource");
                return;
            }
        }
        
        // CRITICAL: Stop previous audio before setting new track
        if (receivingAudioSource.isPlaying)
        {
            receivingAudioSource.Stop();
            Debug.Log($"[🎵Filter-{pipelineType}] Stopped previous audio before reconnection");
        }
    
        // Use the simple SetTrack approach for receiving
        audioTrack.onReceived += OnWebRTCAudioReceived;
        receivingAudioSource.loop = true;
        receivingAudioSource.Play();
    
        Debug.Log($"[🎵Filter-{pipelineType}] Audio track connected to AudioSource, playing: {receivingAudioSource.isPlaying}");
    
        StartCoroutine(VerifyAudioSetup());
    }
    // Add this new method to handle chunked audio data
    private void OnWebRTCAudioReceived(float[] audioData, int channels, int sampleRate)
    {
        Debug.Log($"aaa_[🎵Filter-{pipelineType}] Received audio chunk: {audioData.Length} samples, {channels} channels, {sampleRate}Hz");
    
        // Check audio levels
        float maxLevel = audioData.Max(sample => Mathf.Abs(sample));
        Debug.Log($"aaa_[🎵Filter-{pipelineType}] Received audio maxLevel: {maxLevel:F4}");
    
        // Now you need to feed this chunked data to your WebRTCAudioFilter
        // or directly to Unity's audio system
        if (webrtcFilter != null)
        {
            webrtcFilter.ReceiveAudioChunk(audioData, channels, sampleRate);
        }
    }
    /// <summary>
    /// Stop current audio operations with improved cleanup
    /// </summary>
    public void StopAudioOperations()
    {
        Debug.Log($"[🎵Filter-{pipelineType}] StopAudioOperations called for session: {currentSessionId}");
        
        StopAllAudioOperations();
        OnAudioStreamStateChanged?.Invoke(pipelineType, false, string.Empty);
        Debug.Log($"[🎵Filter-{pipelineType}] Audio operations stopped");
    }
    
    /// <summary>
    /// Force cleanup for reconnection scenarios
    /// </summary>
    public void ForceCleanupForReconnection()
    {
        Debug.Log($"[🎵Filter-{pipelineType}] Force cleanup for reconnection");
        
        ForceCleanupAudioStreaming();
        ForceCleanupAudioReceiving();
        
        connectionAttemptCount = 0;
        currentSessionId = string.Empty;
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
    
        // Clean up existing interceptor if reconnecting
        CleanupNDIInterceptor();
    
        // Start coroutine to wait for NDI AudioSource to be created
        StartCoroutine(ContinuousNDIMonitoring());
    }
    
    private void CleanupNDIInterceptor()
    {
        if (ndiInterceptor != null)
        {
            Debug.Log($"[🎵Filter-{pipelineType}] Cleaning up existing NDI interceptor");
            DestroyImmediate(ndiInterceptor);
            ndiInterceptor = null;
        }
        isCapturingAudio = false;
    }
    
    private IEnumerator WaitForNDIAudioSource()
    {
        Debug.Log($"[🎵Filter-{pipelineType}] Waiting for NDI to create AudioSource...");
    
        AudioSource ndiAudioSourceComponent = null;
        int attempts = 0;
    
        while (ndiAudioSourceComponent == null && attempts < 50) // Wait max 5 seconds
        {
            ndiAudioSourceComponent = ndiAudioSource.GetComponentInChildren<AudioSource>();
        
            if (ndiAudioSourceComponent == null)
            {
                attempts++;
                yield return new WaitForSeconds(0.1f); // Check every 100ms
            }
        }
    
        if (ndiAudioSourceComponent != null)
        {
            Debug.Log($"[🎵Filter-{pipelineType}] Found NDI AudioSource: {ndiAudioSourceComponent.name} after {attempts * 0.1f}s");
        
            // Add interceptor to the found AudioSource
            ndiInterceptor = ndiAudioSourceComponent.gameObject.GetComponent<NDIAudioInterceptor>();
            if (ndiInterceptor == null)
            {
                ndiInterceptor = ndiAudioSourceComponent.gameObject.AddComponent<NDIAudioInterceptor>();
            }
        
            ndiInterceptor.Initialize(pipelineType, this);
            isCapturingAudio = true;
            
            TestNDIAudioSource(); // Test NDI audio immediately after setup

        
            Debug.Log($"[🎵Filter-{pipelineType}] NDI audio interception setup complete on: {ndiAudioSourceComponent.name}");
        }
        else
        {
            Debug.LogError($"[🎵Filter-{pipelineType}] Failed to find NDI AudioSource after 5 seconds");
        }
    }
    
    /// <summary>
    /// Called by NDIAudioInterceptor when audio data is available
    /// </summary>
    public void OnNDIAudioData(float[] audioData, int channels)
    {
        if (!isStreaming || sendingAudioTrack == null) return;
    
        // DEBUG: Check audio data quality
        bool hasAudio = audioData.Any(sample => Mathf.Abs(sample) > 0.001f);
        float maxLevel = audioData.Max(sample => Mathf.Abs(sample));
        // Only log when we have significant audio changes
        if (hasAudio && maxLevel > 0.01f)
        {
            Debug.Log($"aaa_[🎵Filter-{pipelineType}] Sending to WebRTC: {audioData.Length} samples, hasAudio: {hasAudio}, maxLevel: {maxLevel:F4}");
        }
        int actualSampleRate = cachedSampleRate;

        // Feed audio data to WebRTC 
        try
        {
            sendingAudioTrack.SetData(audioData, channels, actualSampleRate);
        }
        catch (Exception e)
        {
            Debug.LogError($"aaa_[🎵Filter-{pipelineType}] Data length: {audioData.Length}, Channels: {channels}, SampleRate: {actualSampleRate}");
            Debug.LogError($"aaa_[🎵Filter-{pipelineType}] Error feeding audio to WebRTC: {e.Message}");
        }
    }
    #endregion
    
    #region WebRTC Audio Management
    
    public void RefreshNDIConnection()
    {
        Debug.Log($"aaa_[🎵Filter-{pipelineType}] Refreshing NDI audio connection");
    
        if (ndiAudioSource == null) return;
    
        // Clean up any existing interceptor
        CleanupNDIInterceptor();
    
        // Immediately check for AudioSource
        var currentAudioSource = ndiAudioSource.GetComponentInChildren<AudioSource>();
    
        if (currentAudioSource != null)
        {
            Debug.Log($"aaa_[🎵Filter-{pipelineType}] Found NDI AudioSource during refresh: {currentAudioSource.name}");
        
            // Add interceptor immediately
            ndiInterceptor = currentAudioSource.gameObject.AddComponent<NDIAudioInterceptor>();
            ndiInterceptor.Initialize(pipelineType, this);
            isCapturingAudio = true;
        
            TestNDIAudioSource(); // Debug what we found
        }
        else
        {
            Debug.Log($"aaa_[🎵Filter-{pipelineType}] No NDI AudioSource found during refresh - will wait");
            // Fall back to the coroutine approach
            StartCoroutine(WaitForNDIAudioSource());
        }
    }

    
    private IEnumerator ContinuousNDIMonitoring()
{
    while (isStreaming) // Keep monitoring as long as we're supposed to be streaming
    {
        if (ndiAudioSource != null)
        {
            var currentAudioSource = ndiAudioSource.GetComponentInChildren<AudioSource>();
            
            if (currentAudioSource == null)
            {
                if (ndiInterceptor != null)
                {
                    Debug.Log($"aaa_[🎵Filter-{pipelineType}] NDI AudioSource removed - cleaning up interceptor");
                    CleanupNDIInterceptor();
                }
            }
            else
            {
                // AudioSource exists, check if we have interceptor on it
                var interceptorOnThisSource = currentAudioSource.GetComponent<NDIAudioInterceptor>();
                
                if (interceptorOnThisSource == null)
                {
                    Debug.Log($"aaa_[🎵Filter-{pipelineType}] New NDI AudioSource detected - adding interceptor");
                    
                    // Add interceptor to new AudioSource
                    ndiInterceptor = currentAudioSource.gameObject.AddComponent<NDIAudioInterceptor>();
                    ndiInterceptor.Initialize(pipelineType, this);
                    isCapturingAudio = true;
                }
                else if (interceptorOnThisSource != ndiInterceptor)
                {
                    Debug.Log($"aaa_[🎵Filter-{pipelineType}] NDI AudioSource changed - updating reference");
                    ndiInterceptor = interceptorOnThisSource;
                }
            }
        }
        
        yield return new WaitForSeconds(0.5f); // Check twice per second
    }
}
    
    private void HandleStreamerStateChange(PipelineType pipeline, StreamerState state, string sessionId)
    {
        // Only handle events for our pipeline
        if (pipeline != this.pipelineType) return;
    
        Debug.Log($"[🎵Filter-{pipelineType}] Streamer state changed: {state}, session: {sessionId}");
    
        switch (state)
        {
            case StreamerState.Connecting:
                // Prepare for potential reconnection
                Debug.Log($"[🎵Filter-{pipelineType}] Connection starting - preparing audio");
                break;
                
            case StreamerState.Streaming:
                // Restart audio if we're not already streaming for this session
                if (!isStreaming && !string.IsNullOrEmpty(sessionId))
                {
                    Debug.Log($"[🎵Filter-{pipelineType}] Auto-restarting audio for reconnection");
                    StartAudioStreaming(sessionId);
                }
                break;
            
            case StreamerState.Failed:
            case StreamerState.Idle:
                // Stop audio when connection fails or goes idle
                if (isStreaming || isReceiving)
                {
                    Debug.Log($"[🎵Filter-{pipelineType}] Auto-stopping audio due to connection failure/idle");
                    StopAudioOperations();
                }
                break;
                
            case StreamerState.Disconnecting:
                // Prepare for cleanup
                Debug.Log($"[🎵Filter-{pipelineType}] Connection disconnecting - preparing cleanup");
                break;
        }
    }
    
    private void CreateSendingAudioTrack()
    {
        // Clean up existing sending audio first
        CleanupSendingAudio();
        
        // Create a dummy AudioSource for WebRTC AudioStreamTrack
        var audioGO = new GameObject($"WebRTC_Audio_Sender_{pipelineType}_{connectionAttemptCount}");
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
        
        // Create WebRTC AudioStreamTrack
        sendingAudioTrack = new AudioStreamTrack(sendingAudioSource);
        sendingAudioSource.Play();
        
        Debug.Log($"[🎵Filter-{pipelineType}] Sending audio track created (attempt: {connectionAttemptCount})");
    }
    
    private void CreateReceivingAudioSource()
    {
        // Clean up existing receiving audio first
        CleanupReceivingAudio();
        
        receivingAudioGameObject = new GameObject($"WebRTC_Audio_Receiver_{pipelineType}_{connectionAttemptCount}");
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
        
        receivingAudioGameObject.SetActive(true);
        receivingAudioSource.Play(); // Start playing to trigger OnAudioFilterRead
        
        Debug.Log($"[🎵Filter-{pipelineType}] Receiving audio source created at {receivingAudioGameObject.transform.position} (attempt: {connectionAttemptCount})");
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
    
    #region Cleanup Methods
    
    private void ForceCleanupAudioStreaming()
    {
        isStreaming = false;
        isCapturingAudio = false;
        CleanupNDIInterceptor();
        CleanupSendingAudio();
        Debug.Log($"[🎵Filter-{pipelineType}] Force cleanup audio streaming completed");
    }
    
    private void ForceCleanupAudioReceiving()
    {
        isReceiving = false;
        CleanupReceivingAudio();
        Debug.Log($"[🎵Filter-{pipelineType}] Force cleanup audio receiving completed");
    }
    
    private void CleanupSendingAudio()
    {
        if (sendingAudioTrack != null)
        {
            sendingAudioTrack.Dispose();
            sendingAudioTrack = null;
        }
        
        if (sendingAudioSource != null && sendingAudioSource.gameObject != null)
        {
            DestroyImmediate(sendingAudioSource.gameObject);
            sendingAudioSource = null;
        }
    }
    
    private void CleanupReceivingAudio()
    {
        if (receivingAudioSource != null)
        {
            receivingAudioSource.Stop();
        }
        
        if (webrtcFilter != null)
        {
            DestroyImmediate(webrtcFilter);
            webrtcFilter = null;
        }
        
        if (receivingAudioGameObject != null)
        {
            DestroyImmediate(receivingAudioGameObject);
            receivingAudioGameObject = null;
            receivingAudioSource = null;
        }
    }
    
    #endregion
    
    #region Audio Verification and Debugging
    
    private IEnumerator VerifyAudioSetup()
    {
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[🎵Filter-{pipelineType}] Audio Verification (Attempt: {connectionAttemptCount}):");
        
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
        Debug.Log($"  - Connection Attempts: {connectionAttemptCount}");
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
        ForceCleanupAudioStreaming();
        ForceCleanupAudioReceiving();
        currentSessionId = string.Empty;
        connectionAttemptCount = 0;
    }
    
    private void DisposeAudioComponents()
    {
        CleanupSendingAudio();
        CleanupReceivingAudio();
        CleanupNDIInterceptor();
    }
    
    #endregion
    
    #region Public Properties
    
    public bool IsStreaming => isStreaming;
    public bool IsReceiving => isReceiving;
    public string CurrentSessionId => currentSessionId;
    public AudioSource ReceivingAudioSource => receivingAudioSource;
    public int ConnectionAttemptCount => connectionAttemptCount;
    
    #endregion
    
    
    // <summary>
/// Enhanced debug method to check audio flow
/// </summary>
public void DebugAudioFlow()
{
    Debug.Log($"aaa_[🎵Filter-{pipelineType}] === AUDIO FLOW DEBUG ===");
    Debug.Log($"aaa_Unity Audio Settings:");
    Debug.Log($"aaa_  - Output Sample Rate: {AudioSettings.outputSampleRate}");
    Debug.Log($"aaa_  - DSP Buffer Size: {AudioSettings.GetConfiguration().dspBufferSize}");
    Debug.Log($"aaa_  - Speaker Mode: {AudioSettings.GetConfiguration().speakerMode}");
    
    Debug.Log($"aaa_Streaming State:");
    Debug.Log($"aaa_  - Is Streaming: {isStreaming}");
    Debug.Log($"aaa_  - Is Receiving: {isReceiving}");
    Debug.Log($"aaa_  - Session ID: {currentSessionId}");
    
    if (sendingAudioTrack != null)
    {
        Debug.Log($"aaa_Sending Audio Track:");
        Debug.Log($"aaa_  - Track ID: {sendingAudioTrack.Id}");
        Debug.Log($"aaa_  - Track Kind: {sendingAudioTrack.Kind}");
        Debug.Log($"aaa_  - Track Enabled: {sendingAudioTrack.Enabled}");
    }
    
    if (receivingAudioSource != null)
    {
        Debug.Log($"aaa_Receiving Audio Source:");
        Debug.Log($"aaa_  - Is Playing: {receivingAudioSource.isPlaying}");
        Debug.Log($"aaa_  - Volume: {receivingAudioSource.volume}");
        Debug.Log($"aaa_  - Mute: {receivingAudioSource.mute}");
        Debug.Log($"aaa_  - Clip: {(receivingAudioSource.clip != null ? receivingAudioSource.clip.name : "null")}");
    }
    
    if (ndiAudioSource != null)
    {
        var ndiAudioComponent = ndiAudioSource.GetComponentInChildren<AudioSource>();
        if (ndiAudioComponent != null)
        {
            Debug.Log($"aaa_NDI Audio Source:");
            Debug.Log($"aaa_  - Is Playing: {ndiAudioComponent.isPlaying}");
            Debug.Log($"aaa_  - Volume: {ndiAudioComponent.volume}");
            Debug.Log($"aaa_  - Has Clip: {ndiAudioComponent.clip != null}");
        }
    }
    
    // Check AudioListener
    var audioListener = FindObjectOfType<AudioListener>();
    if (audioListener != null)
    {
        Debug.Log($"aaa_Audio Listener:");
        Debug.Log($"aaa_  - Enabled: {audioListener.enabled}");
        Debug.Log($"aaa_  - Volume: {AudioListener.volume}");
        Debug.Log($"aaa_  - Pause: {AudioListener.pause}");
    }
}
// <summary>
    /// Test if NDI is actually producing audio
    /// </summary>
    [ContextMenu("Test NDI Audio Source")]
    public void TestNDIAudioSource()
    {
        if (ndiAudioSource == null)
        {
            Debug.LogError("aaa_No NDI audio source assigned!");
            return;
        }

        var ndiAudioComponent = ndiAudioSource.GetComponentInChildren<AudioSource>();
        if (ndiAudioComponent == null)
        {
            Debug.LogError("aaa_NDI hasn't created an AudioSource yet!");
            return;
        }

        Debug.Log($"aaa_[🎵Test] NDI AudioSource found: {ndiAudioComponent.name}");
        Debug.Log($"aaa_[🎵Test] Is Playing: {ndiAudioComponent.isPlaying}");
        Debug.Log($"aaa_[🎵Test] Volume: {ndiAudioComponent.volume}");
        Debug.Log($"aaa_[🎵Test] Has Clip: {ndiAudioComponent.clip != null}");

        if (ndiAudioComponent.clip != null)
        {
            Debug.Log($"aaa_[🎵Test] Clip Sample Rate: {ndiAudioComponent.clip.frequency}");
            Debug.Log($"aaa_[🎵Test] Clip Channels: {ndiAudioComponent.clip.channels}");
            Debug.Log($"aaa_[🎵Test] Clip Length: {ndiAudioComponent.clip.length}s");
        }

        // Check if interceptor exists
        var interceptor = ndiAudioComponent.GetComponent<NDIAudioInterceptor>();
        Debug.Log($"aaa_[🎵Test] Has Interceptor: {interceptor != null}");
        if (interceptor != null)
        {
            Debug.Log($"aaa_[🎵Test] Interceptor Enabled: {interceptor.enabled}");
        }
    }
}