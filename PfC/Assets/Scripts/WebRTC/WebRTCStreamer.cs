
using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using Unity.Netcode;
using System;
using System.Linq;
using UnityEngine.Rendering;

public enum StreamerState
{
    Idle, Connecting, Connected, Streaming, Receiving, Disconnecting, Failed
}

/// <summary>
/// Updated WebRTC streamer with separated 
/// Integrates with 
/// </summary>
public class WebRTCStreamer : MonoBehaviour
{
    [Header("Pipeline Identity")]
    public PipelineType pipelineType;
    [SerializeField] private string instanceId;
    
    [Header("Configuration")]
    public NdiReceiver ndiReceiverSource;
    public NdiReceiver ndiReceiverCaptions;
    public WebRTCRenderer targetRenderer;
    
    [Header("Settings")]
    [SerializeField] private int textureWidth = 1280;
    [SerializeField] private int textureHeight = 720;
    [SerializeField] private float connectionTimeout = 5f;
    [SerializeField] private bool enableOptimisticStates = true;
    [SerializeField] private int maxRetryAttempts = 3;
    
    [Header("Audio Integration")]
    public NdiAudioInterceptor audioInterceptor; // Assign in inspector or find automatically
    public bool enableAudioStreaming = true;

// Audio streaming components
    public AudioStreamTrack audioTrack;
    private RTCRtpSender audioSender;
    private bool isAudioStreamingActive = false;

// Audio receiving components  
    private AudioStreamTrack receivedAudioTrack;
    
    // WebRTC objects
    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private RenderTexture webRtcTexture;
    private MediaStream receiveMediaStream;
    
    // Blending captions and media source
    private RenderTexture compositeRT;
    private Material blendMaterial;
    
    // State
    private WebRTCSignaling signaling;
    private StreamerState currentState = StreamerState.Idle;
    private string currentSessionId = string.Empty;
    private ulong connectedClientId;
    private int retryCount = 0;
    private bool isOfferer = false;
    private bool isRemoteDescriptionSet = false;
    
    // Coroutines
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine textureUpdateCoroutine;
    
    // ICE candidate buffering
    private System.Collections.Generic.List<RTCIceCandidate> pendingIceCandidates = new System.Collections.Generic.List<RTCIceCandidate>();
    
    // Offer buffering for race condition handling
    private RTCSessionDescription? pendingOffer = null;
    private ulong pendingOfferClient = 0;
    
    public static event Action<PipelineType, StreamerState, string> OnStateChanged;
    
    #region Unity Lifecycle
    
    void Start()
    {
        CreateInstanceId();
        RegisterWithEngine();
        CreateWebRtcObjects();
        ConnectToSignaling();
        SetState(StreamerState.Idle);
        blendMaterial = new Material(Shader.Find("Custom/BlendTwoTextures"));
    }
    
    void OnDestroy()
    {
        DisconnectFromSignaling();
        StopAllOperations();
        DisposeWebRtcObjects();
        UnregisterFromEngine();
    }
    
    #endregion
    
    #region Initialization
    
    private void CreateInstanceId()
    {
        instanceId = $"{pipelineType}_{System.Guid.NewGuid().ToString("N")[..8]}";
    }
    
    private void RegisterWithEngine()
    {
        WebRTCEngineManager.Instance.RegisterStreamer(pipelineType);
    }
    
    private void UnregisterFromEngine()
    {
        if (WebRTCEngineManager.Instance != null)
        {
            WebRTCEngineManager.Instance.UnregisterStreamer(pipelineType);
        }
    }
    
    private void CreateWebRtcObjects()
    {
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        webRtcTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        webRtcTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);

        audioTrack = audioInterceptor.audioStreamTrack;
        audioInterceptor.StartAudioStreaming();
        audioInterceptor.StopAudioStreaming();
        
        Debug.Log($"[📡{instanceId}] WebRTC objects created {textureWidth}x{textureHeight}");
    }
    
    private void ConnectToSignaling()
    {
        signaling = FindObjectOfType<WebRTCSignaling>();
        if (signaling == null)
        {
            Debug.LogError($"[📡{instanceId}] No WebRTCSignaling found");
            return;
        }
        
        WebRTCSignaling.OnOfferReceived += HandleOfferReceived;
        WebRTCSignaling.OnAnswerReceived += HandleAnswerReceived;
        WebRTCSignaling.OnIceCandidateReceived += HandleIceCandidateReceived;
    }
    
    private void DisconnectFromSignaling()
    {
        if (signaling != null)
        {
            WebRTCSignaling.OnOfferReceived -= HandleOfferReceived;
            WebRTCSignaling.OnAnswerReceived -= HandleAnswerReceived;
            WebRTCSignaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
        }
    }
    
    #endregion
    
    #region Public Interface
    
    /// <summary>
    /// Start streaming for the given session
    /// </summary>
    public void StartStreaming(string sessionId)
    {
        streamingStartTime = Time.time;
        Debug.Log($"aabb_[🔍Timing] Streaming started at: {streamingStartTime}");
        StartCoroutine(BeginStreamingSession(sessionId));
    }
    
    /// <summary>
    /// Start receiving for the given session  
    /// </summary>
    public void StartReceiving(string sessionId)
    {
        Debug.Log($"aabb_[🔍Timing] Receiving started at: {Time.time}");
        
        PrepareForNewSessionSync(sessionId);
        isOfferer = false;
        SetupReceivingConnection();
        
        Debug.Log($"[📡{instanceId}] Receiver ready immediately for: {sessionId}");
        StartConnectionTimeout();
    }
    
    private void PrepareForNewSessionSync(string sessionId)
    {
        Debug.Log($"[📡{instanceId}] PrepareForNewSessionSync START");
        
        if (currentState != StreamerState.Idle)
        {
            CleanupCurrentSession();
        }
        
        currentSessionId = sessionId;
        SetState(StreamerState.Connecting);
        retryCount = 0;
        
        Debug.Log($"[📡{instanceId}] PrepareForNewSessionSync COMPLETE");
    }
    
    /// <summary>
    /// Stop current session and return to local display
    /// </summary>
    public void StopSession()
    {
        Debug.Log($"[📡{instanceId}] StopSession() called! Stack trace:");
        Debug.Log(System.Environment.StackTrace);
        StartCoroutine(EndCurrentSession());
    }
    
    /// <summary>
    /// Force complete system restart
    /// </summary>
    public void ForceRestart()
    {
        StartCoroutine(ForceCompleteRestart());
    }
    
    #endregion
    
    #region Session Management
    
    private IEnumerator BeginStreamingSession(string sessionId)
    {
        PrepareForNewSessionSync(sessionId);
        isOfferer = true;
        
        if (!ValidateNdiSource())
        {
            SetState(StreamerState.Failed);
            yield break;
        }
        
        SetupStreamingConnection();
        StartConnectionTimeout();
        StartCoroutine(CreateOffer());
        
        Debug.Log($"[📡{instanceId}] Streaming session initiated immediately");
    }
    
    private IEnumerator EndCurrentSession()
    {
        if (currentState == StreamerState.Idle) yield break;
        
        SetState(StreamerState.Disconnecting);
        
        StopAllOperations();
        CleanupCurrentSession();
        ResetSessionState();
        
        SetState(StreamerState.Idle);
        targetRenderer?.ShowLocalNDI();
        
        yield return null;
    }
    
    private IEnumerator ForceCompleteRestart()
    {
        Debug.LogWarning($"[📡{instanceId}] Force restart");
        
        StopAllOperations();
        DisposeWebRtcObjects();
        yield return null;
        
        CreateWebRtcObjects();
        ResetSessionState();
        SetState(StreamerState.Idle);
    }
    
    #endregion
    
    #region Connection Setup
    
    private void SetupStreamingConnection()
    {
        CreatePeerConnection();
        AddTracksToConnection();
        StartTextureUpdates();
    }
    
    private void SetupReceivingConnection()
    {
        CreatePeerConnection();
    }
    
    private void CreatePeerConnection()
    {
        Debug.Log($"aaa_[📡{instanceId}] CreatePeerConnection START");
    
        ClosePeerConnection();
    
        var config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } }
            }
        };
    
        peerConnection = new RTCPeerConnection(ref config);
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnTrack = OnTrackReceived;
        peerConnection.OnConnectionStateChange = OnConnectionStateChange;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
    
        Debug.Log($"aaa_[📡{instanceId}] CreatePeerConnection COMPLETE");
    }
    
    
    private void AddTracksToConnection()
    {
        if (peerConnection == null || videoTrack == null) 
        {
            Debug.LogError($"[📡{instanceId}] Cannot add tracks - missing components");
            SetState(StreamerState.Failed);
            return;
        }
        
        // Add video track
        peerConnection.AddTrack(videoTrack);
        
        //add audio track
        if (enableAudioStreaming && audioInterceptor != null)
        {
            // Start audio streaming
            audioInterceptor.StartAudioStreaming();
            
                // Add to peer connection 
                audioSender = peerConnection.AddTrack(audioInterceptor.audioStreamTrack);
                isAudioStreamingActive = true;
                Debug.Log($"[📡{instanceId}] Audio track added to peer connection");
        }
        
        Debug.Log($"[📡{instanceId}] Tracks added to connection");
    }
    
    
    #endregion
    
    #region NDI Management
    
    private bool ValidateNdiSource()
    {
        if (ndiReceiverSource == null)
        {
            Debug.LogError($"[📡{instanceId}] No NDI receiver assigned");
            return false;
        }
        
        ActivateNdiReceiver(ndiReceiverSource);
        ActivateNdiReceiver(ndiReceiverCaptions);
        return HasValidNdiTexture();
    }
    
    private void ActivateNdiReceiver(NdiReceiver ndiReceiver)
    {
        if (ndiReceiver != null && !ndiReceiver.gameObject.activeInHierarchy)
        {
            ndiReceiver.gameObject.SetActive(true);
            Debug.Log($"[📡{instanceId}] NDI receiver activated");
        }
    }
    
    private bool HasValidNdiTexture()
    {
        var texture = ndiReceiverSource.GetTexture();
        bool isValid = texture != null && texture.width > 0 && texture.height > 0;
        
        if (isValid)
        {
            Debug.Log($"[📡{instanceId}] NDI validated: {texture.width}x{texture.height}");
        }
        else
        {
            Debug.LogError($"[📡{instanceId}] Invalid NDI texture");
        }
        
        return isValid;
    }
    
    #endregion
    
    #region Texture Updates
    
    private void StartTextureUpdates()
    {
        StopTextureUpdates();
        textureUpdateCoroutine = StartCoroutine(UpdateTextureFromNdi());
    }
    
    private void StopTextureUpdates()
    {
        if (textureUpdateCoroutine != null)
        {
            StopCoroutine(textureUpdateCoroutine);
            textureUpdateCoroutine = null;
        }
    }
    
    private IEnumerator UpdateTextureFromNdi()
    {
        while (IsStreamingOrConnecting())
        {
            var ndiTexture = ndiReceiverSource?.GetTexture();
            var ndiTextureCaptions = ndiReceiverCaptions?.GetTexture();
            
            if (compositeRT == null && ndiTexture != null)
            {
                compositeRT = new RenderTexture(ndiTexture.width, ndiTexture.height, depth: 0);
                compositeRT.Create();
            }
            
            if (ndiTexture != null && webRtcTexture != null && ndiTextureCaptions != null)
            {
                blendMaterial.SetTexture("_MainTex", ndiTexture);
                blendMaterial.SetTexture("_OverlayTex", ndiTextureCaptions);

                Graphics.Blit(null, compositeRT, blendMaterial);
                Graphics.Blit(compositeRT, webRtcTexture);
            }
            
            yield return new WaitForEndOfFrame();
        }
    }

    private bool IsStreamingOrConnecting()
    {
        return currentState == StreamerState.Connecting || currentState == StreamerState.Streaming;
    }
    
    #endregion
    
    #region WebRTC Signaling
    
    private IEnumerator CreateOffer()
    {
        yield return null;
        
        var offerOp = peerConnection.CreateOffer();
        yield return offerOp;
        
        if (offerOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Offer creation failed: {offerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        yield return StartCoroutine(SetLocalDescription(offerOp.Desc));
        signaling.SendOffer(pipelineType, offerOp.Desc, currentSessionId);
        
        Debug.Log($"[📡{instanceId}] Offer sent");
    }
    
    private IEnumerator SetLocalDescription(RTCSessionDescription desc)
    {
        var setOp = peerConnection.SetLocalDescription(ref desc);
        yield return setOp;
        
        if (setOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Set local description failed: {setOp.Error}");
            HandleConnectionFailure();
        }
    }
    
    private IEnumerator SetRemoteDescription(RTCSessionDescription desc)
    {
        var setOp = peerConnection.SetRemoteDescription(ref desc);
        yield return setOp;
        
        if (setOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Set remote description failed: {setOp.Error}");
            HandleConnectionFailure();
        }
        else
        {
            isRemoteDescriptionSet = true;
            ProcessBufferedIceCandidates();
        }
    }
    
    #endregion
    
    #region Event Handlers - Signaling
    
    private void HandleOfferReceived(PipelineType pipeline, RTCSessionDescription offer, ulong fromClient, string sessionId)
    {
        if (!IsForThisInstance(pipeline, sessionId) || isOfferer) return;
        
        Debug.Log($"[📡{instanceId}] Processing offer from client {fromClient}");
        
        if (peerConnection == null)
        {
            Debug.LogWarning($"[📡{instanceId}] Offer arrived before peer connection ready - buffering");
            pendingOffer = offer;
            pendingOfferClient = fromClient;
            return;
        }
        
        StartCoroutine(ProcessOfferImmediately(offer, fromClient));
    }
    
    private void ProcessPendingOffer()
    {
        if (pendingOffer.HasValue && peerConnection != null)
        {
            Debug.Log($"[📡{instanceId}] Processing buffered offer from client {pendingOfferClient}");
            StartCoroutine(ProcessOfferImmediately(pendingOffer.Value, pendingOfferClient));
            
            pendingOffer = null;
            pendingOfferClient = 0;
        }
    }
    
    private IEnumerator ProcessOfferImmediately(RTCSessionDescription offer, ulong fromClient)
    {
        if (peerConnection == null)
        {
            Debug.LogError($"[📡{instanceId}] No peer connection for offer");
            yield break;
        }
        
        Debug.Log($"[📡{instanceId}] Setting remote description...");
        yield return StartCoroutine(SetRemoteDescription(offer));
        
        if (!isRemoteDescriptionSet)
        {
            Debug.LogError($"[📡{instanceId}] Failed to set remote description");
            yield break;
        }
        
        Debug.Log($"[📡{instanceId}] Creating answer...");
        yield return StartCoroutine(CreateAnswerImmediate(fromClient));
    }
    
    private IEnumerator CreateAnswerImmediate(ulong toClient)
    {
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        
        if (answerOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Answer creation failed: {answerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        yield return StartCoroutine(SetLocalDescription(answerOp.Desc));
        
        signaling.SendAnswer(pipelineType, answerOp.Desc, toClient, currentSessionId);
        connectedClientId = toClient;
        
        Debug.Log($"[📡{instanceId}] Answer completed for client {toClient}");
    }
    
    private void HandleAnswerReceived(PipelineType pipeline, RTCSessionDescription answer, ulong fromClient, string sessionId)
    {
        if (!IsForThisInstance(pipeline, sessionId) || !isOfferer) return;
        
        Debug.Log($"[📡{instanceId}] Processing answer from client {fromClient}");
        
        if (enableOptimisticStates)
        {
            SetState(StreamerState.Streaming);
        }
        
        StartCoroutine(SetRemoteDescription(answer));
        connectedClientId = fromClient;
    }
    
    private void HandleIceCandidateReceived(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient, string sessionId)
    {
        if (!IsForThisInstance(pipeline, sessionId)) return;
        
        if (peerConnection == null) return;
        
        if (!isRemoteDescriptionSet)
        {
            pendingIceCandidates.Add(candidate);
            Debug.Log($"[📡{instanceId}] ICE candidate buffered (total: {pendingIceCandidates.Count})");
            return;
        }
        
        AddIceCandidate(candidate);
    }
    
    private void AddIceCandidate(RTCIceCandidate candidate)
    {
        try
        {
            peerConnection.AddIceCandidate(candidate);
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡{instanceId}] Failed to add ICE candidate: {e.Message}");
        }
    }
    
    private void ProcessBufferedIceCandidates()
    {
        Debug.Log($"[📡{instanceId}] Processing {pendingIceCandidates.Count} buffered ICE candidates");
        
        foreach (var candidate in pendingIceCandidates)
        {
            AddIceCandidate(candidate);
        }
        
        pendingIceCandidates.Clear();
    }
    
    private bool IsForThisInstance(PipelineType pipeline, string sessionId)
    {
        return pipeline == this.pipelineType && sessionId == this.currentSessionId;
    }
    
    #endregion
    
    #region Event Handlers - WebRTC
    
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        signaling?.SendIceCandidate(pipelineType, candidate, currentSessionId);
    }
    
    private void OnTrackReceived(RTCTrackEvent e)
    {
        Debug.Log($"aaa_[📡{instanceId}] *** TRACK RECEIVED *** Kind: {e.Track.Kind}, ID: {e.Track.Id}");
    
        if (e.Track is VideoStreamTrack videoStreamTrack)
        {
            Debug.Log($"aaa_[📡{instanceId}] Video track received and processed");
            videoStreamTrack.OnVideoReceived += OnVideoReceived;
        } 
        else if (e.Track is AudioStreamTrack audioStreamTrack)
        {
            Debug.Log($"[📡{instanceId}] Audio track received and processed");
            receivedAudioTrack = audioStreamTrack;
            NotifyRendererStartAudio(audioStreamTrack);
        }
        else
        {
            Debug.LogWarning($"aaa_[📡{instanceId}] Unknown track type received: {e.Track.GetType()}");
        }
        
        SetState(StreamerState.Receiving);
        ClearConnectionTimeout();
    }
    // private void OnAudioReceived(float[] data, int channels, int samplerate)
    // {
    //     Debug.Log($"[📡{instanceId}] Audio received");
    //     receivedAudioTrack = audioStreamTrack;
    //     
    //     // Notify renderer to start audio
    //     NotifyRendererStartAudio(audioStreamTrack);
    //     SetState(StreamerState.Receiving);
    // }
    
    private void OnVideoReceived(Texture texture)
    {
        Debug.Log($"[📡{instanceId}] Video received: {texture.width}x{texture.height}");
        targetRenderer?.ShowRemoteStream(texture, currentSessionId);
        SetState(StreamerState.Receiving);
    }    

    private void NotifyRendererStartAudio(AudioStreamTrack audioStreamTrack)
    {
        audioInitTime = Time.time;
        float timeSinceStreamStart = audioInitTime - streamingStartTime;
        Debug.Log($"aabb_[🔍Timing] Audio initialization at: {audioInitTime} (+ {timeSinceStreamStart:F2}s after stream start)");

        Debug.Log($"aabb_[🔍InitState] NotifyRendererStartAudio called - this will initialize audio subsystem");
    
        if (targetRenderer == null) return;

        var audioReceiver = targetRenderer.GetComponentInChildren<WebRTCAudioReceiver>();

        if (audioReceiver != null)
        {
            audioReceiver.StartReceivingAudio(audioStreamTrack, currentSessionId);
            audioSubsystemInitialized = true; // Mark as initialized
            Debug.Log($"aabb_[🔍InitState] ✅ Audio subsystem initialized via SetTrack");
            Debug.Log($"aabb_[📡{instanceId}] Audio receiver started for session: {currentSessionId}");
        }
        else
        {
            Debug.LogWarning($"aabb_[📡{instanceId}] No WebRTCAudioReceiver found - audio will not be heard");
        }
    }
    
    private float lastConnectionTime = 0f;
    
    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        Debug.Log($"[📡{instanceId}] Connection state: {state}");
        
        switch (state)
        {
            case RTCPeerConnectionState.Connecting:
                lastConnectionTime = Time.time;
                
                if (enableOptimisticStates && isOfferer)
                {
                    SetState(StreamerState.Streaming);
                    //targetRenderer?.ShowLocalNDI();
                }
                break;
                
            case RTCPeerConnectionState.Connected:
                float connectionDuration = Time.time - lastConnectionTime;
                AdaptToNetworkPerformance(connectionDuration);
                
                SetState(isOfferer ? StreamerState.Streaming : StreamerState.Receiving);

                ClearConnectionTimeout();
                retryCount = 0;
                break;
                
            case RTCPeerConnectionState.Failed:
                
            case RTCPeerConnectionState.Disconnected:
                targetRenderer?.ShowLocalNDI();
                if (currentState != StreamerState.Disconnecting)
                    HandleConnectionFailure();
                break;
        }
    }
    
    private void AdaptToNetworkPerformance(float connectionTime)
    {
        Debug.Log($"[📡{instanceId}] Connection took {connectionTime:F1}s");
        
        if (connectionTime > 3f)
        {
            connectionTimeout = 8f;
            enableOptimisticStates = false;
            Debug.Log($"[📡{instanceId}] Slow network detected - using conservative settings");
        }
        else if (connectionTime < 1f)
        {
            connectionTimeout = 3f;
            enableOptimisticStates = true;
            Debug.Log($"[📡{instanceId}] Fast network detected - using optimized settings");
        }
    }
    
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"[📡{instanceId}] ICE state: {state}");
        
        if (state == RTCIceConnectionState.Failed && currentState != StreamerState.Disconnecting)
        {
            HandleConnectionFailure();
        }
    }
    
    #endregion
    
    #region Error Handling
    
    private void HandleConnectionFailure()
    {
        if (currentState == StreamerState.Disconnecting || currentState == StreamerState.Failed)
            return;
        
        Debug.LogError($"[📡{instanceId}] Connection failed (attempt {retryCount + 1})");
        
        ClearConnectionTimeout();
        
        int maxRetries = GetAdaptiveMaxRetries();
        
        if (retryCount < maxRetries)
        {
            retryCount++;
            connectionTimeout = Mathf.Min(connectionTimeout * 1.5f, 10f);
            Debug.Log($"[📡{instanceId}] Adapted timeout to {connectionTimeout}s for retry {retryCount}");
            
            StartCoroutine(RetryConnection());
        }
        else
        {
            SetState(StreamerState.Failed);
            targetRenderer?.ShowLocalNDI();
            Debug.LogError($"[📡{instanceId}] Max retries reached");
        }
    }
    
    private int GetAdaptiveMaxRetries()
    {
        if (connectionTimeout > 7f) return 5;
        if (connectionTimeout > 4f) return 3;
        return 2;
    }
    
    private IEnumerator RetryConnection()
    {
        ClosePeerConnection();
        yield return null;
        
        if (string.IsNullOrEmpty(currentSessionId)) yield break;
        
        if (isOfferer)
        {
            SetupStreamingConnection();
            StartCoroutine(CreateOffer());
        }
        else
        {
            SetupReceivingConnection();
        }
        
        StartConnectionTimeout();
    }
    
    #endregion
    
    #region Connection Timeout
    
    private void StartConnectionTimeout()
    {
        ClearConnectionTimeout();
        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutTimer());
    }
    
    private void ClearConnectionTimeout()
    {
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }
    }
    
    private IEnumerator ConnectionTimeoutTimer()
    {
        yield return new WaitForSeconds(connectionTimeout);
        
        if (currentState == StreamerState.Connecting)
        {
            Debug.LogWarning($"[📡{instanceId}] Connection timeout");
            HandleConnectionFailure();
        }
    }
    
    #endregion
    
    #region Cleanup Operations
    
    private void StopAllOperations()
    {
        ClearConnectionTimeout();
        StopTextureUpdates();

    }
    
    private void CleanupCurrentSession()
    {
        StopAllOperations();
        
        if (isAudioStreamingActive)
        {
            StopAudioStreaming();
        }
        if (receivedAudioTrack != null)
        {
            NotifyRendererStopAudio();
            receivedAudioTrack = null;
        }
        
        ClosePeerConnection();
    }
    
    private void StopAudioStreaming()
    {
        if (!isAudioStreamingActive) return;
        
        try
        {
            // Remove audio track from peer connection
            if (audioSender != null && peerConnection != null)
            {
                peerConnection.RemoveTrack(audioSender);
                audioSender = null;
            }
        
            // Stop audio interceptor
            if (audioInterceptor != null)
            {
                audioInterceptor.StopAudioStreaming();
            }
        
            audioTrack = null;
            isAudioStreamingActive = false;
            Debug.Log($"[📡{instanceId}] Audio streaming stopped");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[📡{instanceId}] Error stopping audio streaming: {e.Message}");
        }
    }
    private void NotifyRendererStopAudio()
    {
        if (targetRenderer == null) return;
    
        // Try to find WebRTCAudioReceiver
        var audioReceiver = targetRenderer.GetComponentInChildren<WebRTCAudioReceiver>();
        if (audioReceiver != null)
        {
            audioReceiver.StopReceivingAudio();
            Debug.Log($"[📡{instanceId}] Audio receiver stopped");
        }
    }
    
    private void ResetSessionState()
    {
        currentSessionId = string.Empty;
        connectedClientId = 0;
        retryCount = 0;
        isOfferer = false;
    }
    
    private void ClosePeerConnection()
    {
        if (peerConnection != null)
        {
            UnsubscribeFromVideoEvents();
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }
        
        if (receiveMediaStream != null)
        {
            receiveMediaStream.Dispose();
            receiveMediaStream = null;
        }
        
        isRemoteDescriptionSet = false;
        pendingIceCandidates.Clear();
        pendingOffer = null;
        pendingOfferClient = 0;
    }
    
    private void UnsubscribeFromVideoEvents()
    {
        var transceivers = peerConnection.GetTransceivers();
        foreach (var transceiver in transceivers)
        {
            if (transceiver.Receiver?.Track is VideoStreamTrack videoTrack)
            {
                videoTrack.OnVideoReceived -= OnVideoReceived;
            }
        }
    }
    
    private void DisposeWebRtcObjects()
    {
        if (videoTrack != null)
        {
            videoTrack.Dispose();
            videoTrack = null;
        }
        
        if (webRtcTexture != null)
        {
            webRtcTexture.Release();
            DestroyImmediate(webRtcTexture);
            webRtcTexture = null;
        }
        
        if (compositeRT != null)
        {
            compositeRT.Release();
            DestroyImmediate(compositeRT);
            compositeRT = null;
        }
    }
    
    #endregion
    
    #region State Management
    
    private void SetState(StreamerState newState)
    {
        if (currentState != newState)
        {
            Debug.Log($"[📡{instanceId}] {currentState} → {newState}");
            currentState = newState;
            OnStateChanged?.Invoke(pipelineType, newState, currentSessionId);
        }
    }
    
    #endregion
    
    #region Public Properties
    
    public StreamerState CurrentState => currentState;
    public string CurrentSessionId => currentSessionId;
    public string InstanceId => instanceId;
    public bool IsConnected => currentState == StreamerState.Streaming || currentState == StreamerState.Receiving;
    public bool IsAudioStreamingActive => isAudioStreamingActive;
    public bool HasReceivedAudioTrack => receivedAudioTrack != null;
    public bool HasAudioInterceptor => audioInterceptor != null;
    public string GetAudioStatus()
    {
        if (!enableAudioStreaming) return "Audio disabled";
        if (audioInterceptor == null) return "No audio interceptor";
        if (isOfferer && isAudioStreamingActive) return "Streaming audio";
        if (!isOfferer && receivedAudioTrack != null) return "Receiving audio";
        if (isOfferer) return "Ready to stream";
        return "Ready to receive";
    }
    
    #endregion

    #region Debug

    [ContextMenu("Print Audio Status")]
    public void DebugPrintAudioStatus()
    {
        Debug.Log($"[📡{instanceId}] Audio Status: {GetAudioStatus()}");
    }
    
    [ContextMenu("Debug Audio Subsystem State")]
    public void DebugAudioSubsystemState()
    {
        Debug.Log($"aabb_[🔍AudioSubsystem] === UNITY AUDIO STATE ===");
        Debug.Log($"aabb_[🔍AudioSubsystem] Speaker Mode: {AudioSettings.GetConfiguration().speakerMode}");
        Debug.Log($"aabb_[🔍AudioSubsystem] Sample Rate: {AudioSettings.GetConfiguration().sampleRate}");
        Debug.Log($"aabb_[🔍AudioSubsystem] Buffer Size: {AudioSettings.GetConfiguration().dspBufferSize}");
        Debug.Log($"aabb_[🔍AudioSubsystem] Audio Active: {AudioListener.volume > 0}");
    
        // Check if any AudioSource has been used with WebRTC
        var allAudioSources = FindObjectsOfType<AudioSource>();
        var webrtcSources = allAudioSources.Where(a => a.clip == null && a.enabled).ToList();
    
        Debug.Log($"aabb_[🔍AudioSubsystem] Total AudioSources: {allAudioSources.Length}");
        Debug.Log($"aabb_[🔍AudioSubsystem] WebRTC AudioSources (clip=null, enabled): {webrtcSources.Count}");
    
        foreach (var source in webrtcSources)
        {
            Debug.Log($"aabb_[🔍AudioSubsystem]   - {source.gameObject.name}: playing={source.isPlaying}, volume={source.volume}");
        }
    }
    
    private static bool audioSubsystemInitialized = false; // Static to track globally

    [ContextMenu("Check WebRTC Audio Initialization State")]
    public void CheckAudioInitState()
    {
        Debug.Log($"aabb_[🔍InitState] WebRTC Audio Subsystem Initialized: {audioSubsystemInitialized}");
        Debug.Log($"aabb_[🔍InitState] WebRTC Engine Running: {WebRTCEngineManager.Instance.IsEngineRunning}");
        Debug.Log($"aabb_[🔍InitState] Audio Track Created: {(audioTrack != null)}");
        Debug.Log($"aabb_[🔍InitState] Is Streaming Active: {isAudioStreamingActive}");
        Debug.Log($"aabb_[🔍InitState] Current State: {currentState}");
    }

    private float audioInitTime = 0f;
    private float streamingStartTime = 0f;
    
    #endregion
}