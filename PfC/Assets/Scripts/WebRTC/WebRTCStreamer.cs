
using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using BroadcastPipeline;
using Unity.Netcode;
using System;
using System.Collections.Generic;
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
    private RTCPeerConnection peerConnection;  // For receiving (client side)
    private Dictionary<ulong, RTCPeerConnection> peerConnections = new Dictionary<ulong, RTCPeerConnection>();  // For streaming (server side - multiple clients)
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

    // Per-client state tracking
    private Dictionary<ulong, bool> clientRemoteDescriptionSet = new Dictionary<ulong, bool>();
    private Dictionary<ulong, List<RTCIceCandidate>> clientPendingIceCandidates = new Dictionary<ulong, List<RTCIceCandidate>>();
    
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
        WebRTCSignaling.OnOfferRequested += HandleOfferRequested; // New: Handle late joiner requests
    }
    
    private void DisconnectFromSignaling()
    {
        if (signaling != null)
        {
            WebRTCSignaling.OnOfferReceived -= HandleOfferReceived;
            WebRTCSignaling.OnAnswerReceived -= HandleAnswerReceived;
            WebRTCSignaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
            WebRTCSignaling.OnOfferRequested -= HandleOfferRequested;
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

        // Request a fresh offer from the streamer (for late joiners)
        Debug.Log($"[📡{instanceId}] Requesting offer from streamer for: {sessionId}");
        signaling?.RequestOffer(pipelineType, sessionId);

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
        // Don't create offer immediately - wait for receiver to request it via HandleOfferRequested()
        // This prevents duplicate offers when both streamer and receiver start simultaneously
        // StartCoroutine(CreateOffer());

        Debug.Log($"[📡{instanceId}] Streaming session ready, waiting for offer request");
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
        ProcessPendingOffer();  // Process any offer that arrived before peer connection was ready
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
    
    
    /// <summary>
    /// Create a peer connection for a specific client (multi-client support for streamers)
    /// </summary>
    private RTCPeerConnection CreatePeerConnectionForClient(ulong clientId)
    {
        Debug.Log($"[📡{instanceId}] Creating peer connection for client {clientId}");

        var config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } }
            }
        };

        var pc = new RTCPeerConnection(ref config);

        // Set up callbacks with client ID context
        pc.OnIceCandidate = candidate => OnIceCandidateForClient(candidate, clientId);
        pc.OnConnectionStateChange = state => OnConnectionStateChangeForClient(state, clientId);
        pc.OnIceConnectionChange = state => OnIceConnectionChangeForClient(state, clientId);

        // Add tracks
        pc.AddTrack(videoTrack);
        if (enableAudioStreaming && audioInterceptor != null && audioInterceptor.audioStreamTrack != null)
        {
            pc.AddTrack(audioInterceptor.audioStreamTrack);
            Debug.Log($"[📡{instanceId}] Audio track added for client {clientId}");
        }

        // Initialize per-client state
        clientRemoteDescriptionSet[clientId] = false;
        clientPendingIceCandidates[clientId] = new List<RTCIceCandidate>();

        Debug.Log($"[📡{instanceId}] Peer connection created for client {clientId}");
        return pc;
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
        if (ndiReceiverSource != null)
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
        
        
        Debug.Log($"[📡{instanceId}] UpdateTextureFromNdi coroutine STARTED");
        int frameCount = 0;

        while (IsStreamingOrConnecting())
        {
            var ndiTexture = ndiReceiverSource?.GetTexture();;
            // if (ndiReceiverSource.enabled)
            // {
            //     ndiTexture = ndiReceiverSource?.GetTexture();
            // }
            // else
            // {
            //     //finish this
            //    // ndiTexture = targetRenderer.sharedRenderer.get
            //     
            // }


            if (ndiTexture != null && webRtcTexture != null)
            {
                var ndiTextureCaptions = ndiReceiverCaptions?.GetTexture();

                // Try caption compositing if captions are available
                if (ndiTextureCaptions != null)
                {
                    if (compositeRT == null)
                    {
                        compositeRT = new RenderTexture(ndiTexture.width, ndiTexture.height, 0);
                        compositeRT.Create();
                    }

                    blendMaterial.SetTexture("_MainTex", ndiTexture);
                    blendMaterial.SetTexture("_OverlayTex", ndiTextureCaptions);
                    Graphics.Blit(null, compositeRT, blendMaterial);
                    Graphics.Blit(compositeRT, webRtcTexture);
                }
                else
                {
                    // Direct blit - no captions
                    Graphics.Blit(ndiTexture, webRtcTexture);
                }

                frameCount++;
                if (frameCount % 60 == 0)  // Log every 60 frames
                {
                    Debug.Log($"[📡{instanceId}] Streaming frame {frameCount}, State: {currentState}");
                }
            }
            else
            {
                if (frameCount == 0 || frameCount % 60 == 0)
                {
                    Debug.LogWarning($"[📡{instanceId}] Missing texture! NDI: {ndiTexture != null}, WebRTC: {webRtcTexture != null}");
                }
            }

            yield return new WaitForEndOfFrame();
        }

        Debug.LogWarning($"[📡{instanceId}] UpdateTextureFromNdi coroutine STOPPED after {frameCount} frames. Final state: {currentState}");
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
        signaling.SendOffer(pipelineType, offerOp.Desc, connectedClientId, currentSessionId);

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

    // Per-client signaling methods for multi-client support
    private IEnumerator CreateOfferForClient(ulong clientId)
    {
        if (!peerConnections.ContainsKey(clientId))
        {
            Debug.LogError($"[📡{instanceId}] No peer connection for client {clientId}");
            yield break;
        }

        var pc = peerConnections[clientId];
        yield return null;

        var offerOp = pc.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Offer creation failed for client {clientId}: {offerOp.Error}");
            yield break;
        }

        yield return StartCoroutine(SetLocalDescriptionForClient(clientId, offerOp.Desc));
        signaling.SendOffer(pipelineType, offerOp.Desc, clientId, currentSessionId);

        Debug.Log($"[📡{instanceId}] Offer sent to client {clientId}");
    }

    private IEnumerator SetLocalDescriptionForClient(ulong clientId, RTCSessionDescription desc)
    {
        if (!peerConnections.ContainsKey(clientId))
        {
            Debug.LogError($"[📡{instanceId}] No peer connection for client {clientId}");
            yield break;
        }

        var pc = peerConnections[clientId];
        var setOp = pc.SetLocalDescription(ref desc);
        yield return setOp;

        if (setOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Set local description failed for client {clientId}: {setOp.Error}");
        }
    }

    private IEnumerator SetRemoteDescriptionForClient(ulong clientId, RTCSessionDescription desc)
    {
        if (!peerConnections.ContainsKey(clientId))
        {
            Debug.LogError($"[📡{instanceId}] No peer connection for client {clientId}");
            yield break;
        }

        var pc = peerConnections[clientId];
        var setOp = pc.SetRemoteDescription(ref desc);
        yield return setOp;

        if (setOp.IsError)
        {
            Debug.LogError($"[📡{instanceId}] Set remote description failed for client {clientId}: {setOp.Error}");
        }
        else
        {
            clientRemoteDescriptionSet[clientId] = true;
            ProcessBufferedIceCandidatesForClient(clientId);
        }
    }

    private void ProcessBufferedIceCandidatesForClient(ulong clientId)
    {
        if (!clientPendingIceCandidates.ContainsKey(clientId)) return;

        var candidates = clientPendingIceCandidates[clientId];
        Debug.Log($"[📡{instanceId}] Processing {candidates.Count} buffered ICE candidates for client {clientId}");

        foreach (var candidate in candidates)
        {
            AddIceCandidateForClient(clientId, candidate);
        }

        candidates.Clear();
    }

    private void AddIceCandidateForClient(ulong clientId, RTCIceCandidate candidate)
    {
        if (!peerConnections.ContainsKey(clientId))
        {
            Debug.LogWarning($"[📡{instanceId}] Cannot add ICE candidate - no peer connection for client {clientId}");
            return;
        }

        try
        {
            peerConnections[clientId].AddIceCandidate(candidate);
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡{instanceId}] Failed to add ICE candidate for client {clientId}: {e.Message}");
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

        // Route to per-client peer connection for multi-client support
        if (peerConnections.ContainsKey(fromClient))
        {
            StartCoroutine(SetRemoteDescriptionForClient(fromClient, answer));
            Debug.Log($"[📡{instanceId}] Answer routed to per-client connection for client {fromClient}");
        }
        else
        {
            // Fallback to single peer connection (backwards compatibility)
            StartCoroutine(SetRemoteDescription(answer));
            connectedClientId = fromClient;
        }
    }

    private void HandleIceCandidateReceived(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient, string sessionId)
    {
        if (!IsForThisInstance(pipeline, sessionId)) return;

        // For streamers with multi-client support, route to per-client connection
        if (isOfferer && peerConnections.ContainsKey(fromClient))
        {
            if (!clientRemoteDescriptionSet.ContainsKey(fromClient) || !clientRemoteDescriptionSet[fromClient])
            {
                // Buffer candidate until remote description is set
                if (!clientPendingIceCandidates.ContainsKey(fromClient))
                {
                    clientPendingIceCandidates[fromClient] = new List<RTCIceCandidate>();
                }
                clientPendingIceCandidates[fromClient].Add(candidate);
                Debug.Log($"[📡{instanceId}] ICE candidate buffered for client {fromClient} (total: {clientPendingIceCandidates[fromClient].Count})");
            }
            else
            {
                AddIceCandidateForClient(fromClient, candidate);
            }
        }
        // For receivers, use single peer connection
        else if (!isOfferer && peerConnection != null)
        {
            if (!isRemoteDescriptionSet)
            {
                pendingIceCandidates.Add(candidate);
                Debug.Log($"[📡{instanceId}] ICE candidate buffered (total: {pendingIceCandidates.Count})");
                return;
            }

            AddIceCandidate(candidate);
        }
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

    /// <summary>
    /// Handle offer request from late-joining clients (only for streamers/offerers)
    /// </summary>
    private void HandleOfferRequested(PipelineType pipeline, ulong requestingClient, string sessionId)
    {
        if (!IsForThisInstance(pipeline, sessionId) || !isOfferer) return;

        Debug.Log($"[📡{instanceId}] Offer requested by client {requestingClient} - creating per-client connection");

        // Create a dedicated peer connection for this client
        if (!peerConnections.ContainsKey(requestingClient))
        {
            var pc = CreatePeerConnectionForClient(requestingClient);
            peerConnections[requestingClient] = pc;

            // Start audio streaming when first client connects
            if (peerConnections.Count == 1 && enableAudioStreaming && audioInterceptor != null && !isAudioStreamingActive)
            {
                audioInterceptor.StartAudioStreaming();
                isAudioStreamingActive = true;
                Debug.Log($"[📡{instanceId}] Audio streaming started for first client");
            }
        }

        // Create and send offer for this specific client
        StartCoroutine(CreateOfferForClient(requestingClient));
    }

    #endregion
    
    #region Event Handlers - WebRTC
    
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        signaling?.SendIceCandidate(pipelineType, candidate, connectedClientId, currentSessionId);
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

    // Per-client callbacks for multi-client support
    private void OnIceCandidateForClient(RTCIceCandidate candidate, ulong clientId)
    {
        Debug.Log($"[📡{instanceId}] ICE candidate for client {clientId}");
        signaling?.SendIceCandidate(pipelineType, candidate, clientId, currentSessionId);
    }

    private void OnConnectionStateChangeForClient(RTCPeerConnectionState state, ulong clientId)
    {
        Debug.Log($"[📡{instanceId}] Client {clientId} connection state: {state}");

        switch (state)
        {
            case RTCPeerConnectionState.Connected:
                Debug.Log($"[📡{instanceId}] Client {clientId} connected successfully");
                SetState(StreamerState.Streaming);
                break;

            case RTCPeerConnectionState.Failed:
            case RTCPeerConnectionState.Disconnected:
                Debug.LogWarning($"[📡{instanceId}] Client {clientId} disconnected/failed");
                // Remove the peer connection for this client
                if (peerConnections.ContainsKey(clientId))
                {
                    peerConnections[clientId].Close();
                    peerConnections[clientId].Dispose();
                    peerConnections.Remove(clientId);
                    clientRemoteDescriptionSet.Remove(clientId);
                    clientPendingIceCandidates.Remove(clientId);
                    Debug.Log($"[📡{instanceId}] Client {clientId} peer connection cleaned up");
                }
                break;
        }
    }

    private void OnIceConnectionChangeForClient(RTCIceConnectionState state, ulong clientId)
    {
        Debug.Log($"[📡{instanceId}] Client {clientId} ICE state: {state}");

        if (state == RTCIceConnectionState.Failed)
        {
            Debug.LogError($"[📡{instanceId}] Client {clientId} ICE connection failed");
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
        // Close single peer connection (for receivers)
        if (peerConnection != null)
        {
            UnsubscribeFromVideoEvents();
            peerConnection.Close();
            peerConnection.Dispose();
            peerConnection = null;
        }

        // Close all per-client peer connections (for streamers)
        foreach (var kvp in peerConnections)
        {
            try
            {
                kvp.Value.Close();
                kvp.Value.Dispose();
                Debug.Log($"[📡{instanceId}] Closed peer connection for client {kvp.Key}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[📡{instanceId}] Error closing peer connection for client {kvp.Key}: {e.Message}");
            }
        }
        peerConnections.Clear();
        clientRemoteDescriptionSet.Clear();
        clientPendingIceCandidates.Clear();

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