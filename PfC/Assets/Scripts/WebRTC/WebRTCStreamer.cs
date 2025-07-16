using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using Unity.Netcode;
using System;
using UnityEngine.Rendering;

public enum StreamerState
{
    Idle, Connecting, Connected, Streaming, Receiving, Disconnecting, Failed
}

/// <summary>
/// Updated WebRTC streamer with separated audio handling
/// Integrates with WebRTCAudioStreamer for proper audio streaming
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
    public FilterBasedAudioStreamer audioStreamer; // New audio streamer reference
    
    [Header("Settings")]
    [SerializeField] private int textureWidth = 1920;
    [SerializeField] private int textureHeight = 1080;
    [SerializeField] private float connectionTimeout = 5f;
    [SerializeField] private bool enableOptimisticStates = true;
    [SerializeField] private int maxRetryAttempts = 3;
    
    // WebRTC objects
    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private AudioStreamTrack audioTrack; // Now managed by audio streamer
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
        ValidateAudioStreamer();
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
        Debug.Log($"[📡{instanceId}] Created with audio streaming support");
    }
    
    private void ValidateAudioStreamer()
    {
        if (audioStreamer == null)
        {
            Debug.LogError($"[📡{instanceId}] No WebRTCAudioStreamer assigned - audio will not work");
        }
        else
        {
            Debug.Log($"[📡{instanceId}] Audio streamer validated and ready");
        }
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
        StartCoroutine(BeginStreamingSession(sessionId));
    }
    
    /// <summary>
    /// Start receiving for the given session  
    /// </summary>
    public void StartReceiving(string sessionId)
    {
        Debug.Log($"[📡{instanceId}] StartReceiving called for: {sessionId}");
        
        PrepareForNewSessionSync(sessionId);
        isOfferer = false;
        SetupReceivingConnection();
        
        // Prepare audio receiving
        if (audioStreamer != null)
        {
            audioStreamer.PrepareAudioReceiving(sessionId);
        }
        
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
        Debug.Log($"[📡{instanceId}] CreatePeerConnection START");
    
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
    
        // Create MediaStream for receiving audio
        if (!isOfferer)
        {
            receiveMediaStream = new MediaStream();
            receiveMediaStream.OnAddTrack = OnMediaStreamTrackAdded;
        }
    
        Debug.Log($"[📡{instanceId}] CreatePeerConnection COMPLETE");
    }
    
    private void OnMediaStreamTrackAdded(MediaStreamTrackEvent e)
    {
        Debug.Log($"[📡{instanceId}] MediaStream track added: {e.Track.Kind}");
    
        if (e.Track is AudioStreamTrack audioStreamTrack)
        {
            Debug.Log($"[📡{instanceId}] Audio track received in MediaStream");
            
            // Delegate audio handling to audio streamer
            if (audioStreamer != null)
            {
                Debug.Log($"[📡{instanceId}] Audio streamer found, calling HandleIncomingAudioTrack");

                audioStreamer.HandleIncomingAudioTrack(audioStreamTrack);
            }
            else
            {
                Debug.LogError($"[📡{instanceId}] No audio streamer available for incoming audio track");
            }
        }
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
        
        // Add audio track via audio streamer
        if (audioStreamer != null)
        {
            audioTrack = audioStreamer.StartAudioStreaming(currentSessionId);
            if (audioTrack != null)
            {
                peerConnection.AddTrack(audioTrack);
                Debug.Log($"[📡{instanceId}] Audio track added via audio streamer");
            }
            else
            {
                Debug.LogError($"[📡{instanceId}] Failed to create audio track via audio streamer");
            }
        }
        else
        {
            Debug.LogWarning($"[📡{instanceId}] No audio streamer - streaming video only");
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
        Debug.Log($"[📡{instanceId}] Track received: {e.Track.Kind}");
    
        if (e.Track is VideoStreamTrack videoStreamTrack)
        {
            videoStreamTrack.OnVideoReceived += OnVideoReceived;
            SetState(StreamerState.Receiving);
            ClearConnectionTimeout();
        }
        else if (e.Track.Kind == TrackKind.Audio)
        {
            Debug.Log($"[📡{instanceId}] Audio track received - adding to MediaStream");
        
            if (receiveMediaStream != null)
            {
                receiveMediaStream.AddTrack(e.Track);
            }
            else
            {
                Debug.LogError($"[📡{instanceId}] No receive MediaStream available for audio track");
            }
        }
    }
    
    private void OnVideoReceived(Texture texture)
    {
        Debug.Log($"[📡{instanceId}] Video received: {texture.width}x{texture.height}");
        targetRenderer?.ShowRemoteStream(texture, currentSessionId);
        SetState(StreamerState.Receiving);
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
                    targetRenderer?.ShowLocalNDI();
                }
                break;
                
            case RTCPeerConnectionState.Connected:
                float connectionDuration = Time.time - lastConnectionTime;
                AdaptToNetworkPerformance(connectionDuration);
                
                SetState(isOfferer ? StreamerState.Streaming : StreamerState.Receiving);
                if (audioStreamer != null)
                {
                    Debug.Log($"[📡{instanceId}] Refreshing NDI audio after connection established");
                    audioStreamer.RefreshNDIConnection();
                }
                ClearConnectionTimeout();
                retryCount = 0;
                break;
                
            case RTCPeerConnectionState.Failed:
            case RTCPeerConnectionState.Disconnected:
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
        
        // Stop audio operations
        if (audioStreamer != null)
        {
            audioStreamer.StopAudioOperations();
        }
    }
    
    private void CleanupCurrentSession()
    {
        StopAllOperations();
        ClosePeerConnection();
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
        
        // Audio track is now managed by audio streamer
        if (audioTrack != null)
        {
            audioTrack.Dispose();
            audioTrack = null;
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
    public bool HasAudioStreamer => audioStreamer != null;
    
    #endregion
    
    #region Debug Methods
    
    [ContextMenu("Debug Audio State")]
    public void DebugAudioState()
    {
        if (audioStreamer != null)
        {
            audioStreamer.DebugAudioState();
        }
        else
        {
            Debug.Log($"[📡{instanceId}] No audio streamer assigned");
        }
    }
    
    void OnValidate()
    {
        if (audioStreamer == null)
        {
            audioStreamer = GetComponent<FilterBasedAudioStreamer>();
        }
        
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<WebRTCRenderer>();
        }
    }
    
    #endregion
}