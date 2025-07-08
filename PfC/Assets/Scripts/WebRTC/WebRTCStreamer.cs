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
/// Pipeline-isolated WebRTC streamer with centralized engine management
/// Each instance handles one pipeline independently
/// </summary>
public class WebRTCStreamer : MonoBehaviour
{
    [Header("Pipeline Identity")]
    public PipelineType pipelineType;
    [SerializeField] private string pipelineInstanceId; // Unique identifier per instance
    
    [Header("Configuration")]
    public NdiReceiver ndiReceiver;
    public WebRTCRenderer targetRenderer;
    
    [Header("WebRTC Settings")]
    [SerializeField] private int textureWidth = 1920;
    [SerializeField] private int textureHeight = 1080;
    [SerializeField] private float connectionTimeout = 3f;
    [SerializeField] private int maxRetryAttempts = 3;
    
    private RTCPeerConnection peerConnection;
    private VideoStreamTrack videoTrack;
    private AudioStreamTrack audioTrack;
    private RenderTexture webRtcTexture;
    
    private WebRTCSignaling signaling;
    private StreamerState currentState = StreamerState.Idle;
    private string currentSessionId = string.Empty;
    private ulong connectedClientId;
    private int retryCount = 0;
    
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine textureUpdateCoroutine;
    private bool isOfferer = false;
    private bool isTextureSourceValid = false;
    private bool isCompletelyShutdown = true;
    
    public static event Action<PipelineType, StreamerState, string> OnStateChanged;
    
    #region Initialization

    void Start()
    {
        GeneratePipelineInstanceId();
        RegisterWithEngineManager();
        InitializeWebRTC();
        SetupSignaling();
        SetState(StreamerState.Idle);
    }
    
    /// <summary>
    /// Generate unique instance identifier for this pipeline
    /// </summary>
    private void GeneratePipelineInstanceId()
    {
        pipelineInstanceId = $"{pipelineType}_{System.Guid.NewGuid().ToString("N")[..8]}";
        Debug.Log($"[📡{pipelineInstanceId}] Pipeline instance created");
    }
    
    /// <summary>
    /// Register with centralized WebRTC engine manager
    /// </summary>
    private void RegisterWithEngineManager()
    {
        WebRTCEngineManager.Instance.RegisterStreamer(pipelineType);
    }
    
    /// <summary>
    /// Initialize WebRTC objects for this pipeline only
    /// </summary>
    private void InitializeWebRTC()
    {
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        webRtcTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        webRtcTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);
        
        Debug.Log($"[📡{pipelineInstanceId}] WebRTC initialized {textureWidth}x{textureHeight}");
    }
    
    /// <summary>
    /// Setup pipeline-filtered signaling events
    /// </summary>
    private void SetupSignaling()
    {
        signaling = FindObjectOfType<WebRTCSignaling>();
        if (signaling == null)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] No WebRTCSignaling found");
            return;
        }
        
        // Pipeline-specific event filtering prevents cross-talk
        WebRTCSignaling.OnOfferReceived += OnOfferReceivedFiltered;
        WebRTCSignaling.OnAnswerReceived += OnAnswerReceivedFiltered;
        WebRTCSignaling.OnIceCandidateReceived += OnIceCandidateReceivedFiltered;
    }

    #endregion

    #region State Management

    /// <summary>
    /// Update streamer state with logging and events
    /// </summary>
    private void SetState(StreamerState newState)
    {
        if (currentState != newState)
        {
            Debug.Log($"[📡{pipelineInstanceId}] {currentState} → {newState} session:{currentSessionId}");
            currentState = newState;
            OnStateChanged?.Invoke(pipelineType, newState, currentSessionId);
        }
    }

    #endregion

    #region Public Interface
    
    /// <summary>
    /// Start streaming with complete restart cycle
    /// </summary>
    public void StartStreaming(string sessionId)
    {
        StartCoroutine(CompleteRestartAndStream(sessionId));
    }
    
    /// <summary>
    /// Start receiving with complete restart cycle
    /// </summary>
    public void StartReceiving(string sessionId)
    {    
        StartCoroutine(CompleteRestartAndReceive(sessionId));
    }
    
    /// <summary>
    /// Force stop all streaming operations
    /// </summary>
    public void ForceStop()
    {
        StartCoroutine(CompleteShutdown());
        targetRenderer?.ShowLocalNDI();
    }
    
    /// <summary>
    /// Gracefully stop current streaming session
    /// </summary>
    public void StopStreaming()
    {
        if (currentState == StreamerState.Idle || currentState == StreamerState.Disconnecting)
            return;

        Debug.Log($"[📡{pipelineInstanceId}] StopStreaming session:{currentSessionId}");
        StartCoroutine(GracefulShutdown());
    }

    #endregion

    #region Restart Cycles

    /// <summary>
    /// Complete restart cycle for streaming
    /// </summary>
    private IEnumerator CompleteRestartAndStream(string sessionId)
    {
        yield return StartCoroutine(CompleteShutdown());
        yield return StartCoroutine(CompleteRestart());
        
        currentSessionId = sessionId;
        isOfferer = true;
        SetState(StreamerState.Connecting);
        
        yield return StartCoroutine(StartStreamingWithNdiValidation());
    }

    /// <summary>
    /// Complete restart cycle for receiving
    /// </summary>
    private IEnumerator CompleteRestartAndReceive(string sessionId)
    {
        yield return StartCoroutine(CompleteShutdown());
        yield return StartCoroutine(CompleteRestart());
        
        currentSessionId = sessionId;
        isOfferer = false;
        SetState(StreamerState.Connecting);
        
        CreatePeerConnection();
        StartConnectionTimeout();
        
        Debug.Log($"[📡{pipelineInstanceId}] Ready to receive");
    }

    /// <summary>
    /// Complete shutdown of pipeline resources
    /// </summary>
    private IEnumerator CompleteShutdown()
    {
        if (!isCompletelyShutdown)
        {
            Debug.Log($"[📡{pipelineInstanceId}] COMPLETE SHUTDOWN");
            
            StopAllCoroutines();
            ClearConnectionTimeout();
            isTextureSourceValid = false;
            
            ClosePeerConnection();
            DisposeWebRTCObjects();
            
            // Unregister from engine manager (not stopping engine directly)
            WebRTCEngineManager.Instance.UnregisterStreamer(pipelineType);
            
            ResetState();
            SetState(StreamerState.Idle);

            yield return new WaitForEndOfFrame();
        }
    }

    /// <summary>
    /// Complete restart of pipeline resources
    /// </summary>
    private IEnumerator CompleteRestart()
    {
        Debug.Log($"[📡{pipelineInstanceId}] COMPLETE RESTART");
        
        // Re-register with engine manager
        WebRTCEngineManager.Instance.RegisterStreamer(pipelineType);
        
        // Recreate local WebRTC objects
        RecreateWebRTCObjects();
        
        isCompletelyShutdown = false;
        yield return new WaitForEndOfFrame();
    }

    #endregion

    #region Resource Management

    /// <summary>
    /// Dispose all WebRTC objects safely
    /// </summary>
    private void DisposeWebRTCObjects()
    {
        if (videoTrack != null)
        {
            videoTrack.Dispose();
            videoTrack = null;
        }
        
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
    }
    
    /// <summary>
    /// Recreate all WebRTC objects with fresh state
    /// </summary>
    private void RecreateWebRTCObjects()
    {
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        webRtcTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        webRtcTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);
        
        Debug.Log($"[📡{pipelineInstanceId}] WebRTC objects recreated");
    }
    
    /// <summary>
    /// Reset internal state variables
    /// </summary>
    private void ResetState()
    {
        currentSessionId = string.Empty;
        connectedClientId = 0;
        retryCount = 0;
        isCompletelyShutdown = true;
    }

    #endregion

    #region NDI Validation & Streaming

    /// <summary>
    /// Start streaming with NDI validation
    /// </summary>
    private IEnumerator StartStreamingWithNdiValidation()
    {
        if (!ActivateNdiReceiver())
        {
            SetState(StreamerState.Failed);
            yield break;
        }
        
        yield return StartCoroutine(WaitForValidNdiTexture());
        
        if (!isTextureSourceValid)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] NDI validation failed");
            SetState(StreamerState.Failed);
            yield break;
        }
        
        CreatePeerConnection();
        RecreateVideoStreamTrack();
        StartTextureUpdates();
        
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        
        AddTracksToConnection();
        StartConnectionTimeout();
        StartCoroutine(CreateAndSendOffer());
    }
    
    /// <summary>
    /// Activate NDI receiver for this pipeline
    /// </summary>
    private bool ActivateNdiReceiver()
    {
        if (ndiReceiver == null)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] No NDI receiver assigned");
            return false;
        }
        
        if (!ndiReceiver.gameObject.activeInHierarchy)
        {
            ndiReceiver.gameObject.SetActive(true);
            Debug.Log($"[📡{pipelineInstanceId}] NDI receiver activated");
        }
        
        return true;
    }
    
    /// <summary>
    /// Wait for NDI to produce valid texture
    /// </summary>
    private IEnumerator WaitForValidNdiTexture()
    {
        float timeout = 2f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            var ndiTexture = ndiReceiver.GetTexture();
            
            if (ndiTexture != null && ndiTexture.width > 0 && ndiTexture.height > 0)
            {
                isTextureSourceValid = true;
                Debug.Log($"[📡{pipelineInstanceId}] NDI validated: {ndiTexture.width}x{ndiTexture.height}");
                yield break;
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        isTextureSourceValid = false;
        Debug.LogError($"[📡{pipelineInstanceId}] NDI validation timeout");
    }
    
    /// <summary>
    /// Recreate video track with fresh texture
    /// </summary>
    private void RecreateVideoStreamTrack()
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
        }
        
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        webRtcTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        webRtcTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);
        
        Debug.Log($"[📡{pipelineInstanceId}] VideoStreamTrack recreated");
    }

    #endregion

    #region Connection Management
    
    /// <summary>
    /// Create peer connection without affecting global engine
    /// </summary>
    private void CreatePeerConnection()
    {
        ClosePeerConnection();
        
        var config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } }
            }
        };
        
        peerConnection = new RTCPeerConnection(ref config);
        
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnTrack = OnTrackReceived;
        peerConnection.OnConnectionStateChange = OnConnectionStateChange;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
        
        Debug.Log($"[📡{pipelineInstanceId}] Peer connection created");
    }
    
    /// <summary>
    /// Add video/audio tracks to peer connection
    /// </summary>
    private void AddTracksToConnection()
    {
        if (peerConnection == null || videoTrack == null) 
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Missing peer connection or video track");
            return;
        }
        
        if (!isTextureSourceValid)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] NDI texture invalid for track addition");
            SetState(StreamerState.Failed);
            return;
        }
        
        try
        {
            peerConnection.AddTrack(videoTrack);
            Debug.Log($"[📡{pipelineInstanceId}] Video track added");
            
            var audioSource = ndiReceiver?.GetComponentInChildren<AudioSource>();
            if (audioSource?.clip != null)
            {
                audioTrack = new AudioStreamTrack(audioSource);
                peerConnection.AddTrack(audioTrack);
                Debug.Log($"[📡{pipelineInstanceId}] Audio track added");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Failed to add tracks: {e.Message}");
            SetState(StreamerState.Failed);
        }
    }
    
    /// <summary>
    /// Start continuous texture updates from NDI to WebRTC
    /// </summary>
    private void StartTextureUpdates()
    {
        if (textureUpdateCoroutine != null)
            StopCoroutine(textureUpdateCoroutine);
            
        textureUpdateCoroutine = StartCoroutine(UpdateTextureLoop());
    }
    
    /// <summary>
    /// Continuous texture update loop
    /// </summary>
    private IEnumerator UpdateTextureLoop()
    {
        while (currentState == StreamerState.Connecting || currentState == StreamerState.Streaming)
        {
            if (ndiReceiver != null && webRtcTexture != null && isTextureSourceValid)
            {
                var ndiTexture = ndiReceiver.GetTexture();
                if (ndiTexture?.width > 0)
                {
                    Graphics.Blit(ndiTexture, webRtcTexture);
                }
            }
            yield return new WaitForEndOfFrame();
        }
    }

    #endregion

    #region Pipeline-Filtered Event Handlers
    
    /// <summary>
    /// Handle offer only for this pipeline and session
    /// </summary>
    private void OnOfferReceivedFiltered(PipelineType pipeline, RTCSessionDescription offer, ulong fromClient, string sessionId)
    {
        if (pipeline == this.pipelineType && sessionId == this.currentSessionId && !isOfferer)
            HandleOfferReceived(pipeline, offer, fromClient, sessionId);
    }
    
    /// <summary>
    /// Handle answer only for this pipeline and session
    /// </summary>
    private void OnAnswerReceivedFiltered(PipelineType pipeline, RTCSessionDescription answer, ulong fromClient, string sessionId)
    {
        if (pipeline == this.pipelineType && sessionId == this.currentSessionId && isOfferer)
            HandleAnswerReceived(pipeline, answer, fromClient, sessionId);
    }
    
    /// <summary>
    /// Handle ICE candidate only for this pipeline and session
    /// </summary>
    private void OnIceCandidateReceivedFiltered(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient, string sessionId)
    {
        if (pipeline == this.pipelineType && sessionId == this.currentSessionId)
            HandleIceCandidateReceived(pipeline, candidate, fromClient, sessionId);
    }

    #endregion

    #region WebRTC Event Handlers
    
    /// <summary>
    /// Handle ICE candidate generation
    /// </summary>
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        signaling?.SendIceCandidate(pipelineType, candidate, currentSessionId);
    }
    
    /// <summary>
    /// Handle incoming track reception
    /// </summary>
    private void OnTrackReceived(RTCTrackEvent e)
    {
        Debug.Log($"[📡{pipelineInstanceId}] Track received: {e.Track.Kind}");
        
        if (e.Track is VideoStreamTrack videoStreamTrack)
        {
            videoStreamTrack.OnVideoReceived += OnVideoReceived;
            ClearConnectionTimeout();
        }
    }
    
    /// <summary>
    /// Handle received video texture
    /// </summary>
    private void OnVideoReceived(Texture texture)
    {
        Debug.Log($"[📡{pipelineInstanceId}] Video received: {texture.width}x{texture.height}");
        
        if (targetRenderer != null && texture != null)
        {
            targetRenderer.ShowRemoteStream(texture, currentSessionId);
        }
    }
    
    /// <summary>
    /// Handle peer connection state changes
    /// </summary>
    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        Debug.Log($"[📡{pipelineInstanceId}] Connection state: {state}");
        
        switch (state)
        {
            case RTCPeerConnectionState.Connected:
                SetState(isOfferer ? StreamerState.Streaming : StreamerState.Receiving);
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
    
    /// <summary>
    /// Handle ICE connection state changes
    /// </summary>
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"[📡{pipelineInstanceId}] ICE state: {state}");
        
        if (state == RTCIceConnectionState.Failed && currentState != StreamerState.Disconnecting)
        {
            HandleConnectionFailure();
        }
    }

    #endregion

    #region Signaling Event Handlers
    
    /// <summary>
    /// Process received offer and create answer
    /// </summary>
    private void HandleOfferReceived(PipelineType pipeline, RTCSessionDescription offer, ulong fromClient, string sessionId)
    {
        if (currentState != StreamerState.Connecting) return;
        
        Debug.Log($"[📡{pipelineInstanceId}] Processing offer from client {fromClient}");
        StartCoroutine(CreateAndSendAnswer(offer, fromClient));
    }
    
    /// <summary>
    /// Process received answer
    /// </summary>
    private void HandleAnswerReceived(PipelineType pipeline, RTCSessionDescription answer, ulong fromClient, string sessionId)
    {
        if (currentState != StreamerState.Connecting) return;
        
        Debug.Log($"[📡{pipelineInstanceId}] Processing answer from client {fromClient}");
        StartCoroutine(SetRemoteAnswer(answer));
        connectedClientId = fromClient;
    }
    
    /// <summary>
    /// Process received ICE candidate
    /// </summary>
    private void HandleIceCandidateReceived(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient, string sessionId)
    {
        if (peerConnection == null) return;
        
        try
        {
            peerConnection.AddIceCandidate(candidate);
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] ICE candidate error: {e.Message}");
        }
    }

    #endregion

    #region Offer/Answer Creation
    
    /// <summary>
    /// Create and send WebRTC offer
    /// </summary>
    private IEnumerator CreateAndSendOffer()
    {
        if (peerConnection == null) yield break;
        
        Debug.Log($"[📡{pipelineInstanceId}] Creating offer");
        
        var offerOp = peerConnection.CreateOffer();
        yield return offerOp;
        
        if (offerOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Offer creation failed: {offerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var offer = offerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref offer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Set local description failed: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        signaling.SendOffer(pipelineType, offer, currentSessionId);
        Debug.Log($"[📡{pipelineInstanceId}] Offer sent");
    }
    
    /// <summary>
    /// Create and send WebRTC answer
    /// </summary>
    private IEnumerator CreateAndSendAnswer(RTCSessionDescription offer, ulong toClient)
    {
        if (peerConnection == null) yield break;
        
        Debug.Log($"[📡{pipelineInstanceId}] Creating answer");
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref offer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Set remote description failed: {setRemoteOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        
        if (answerOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Answer creation failed: {answerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answer = answerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Set local answer failed: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        signaling.SendAnswer(pipelineType, answer, toClient, currentSessionId);
        connectedClientId = toClient;
        
        Debug.Log($"[📡{pipelineInstanceId}] Answer sent to client {toClient}");
    }
    
    /// <summary>
    /// Set remote answer for outgoing connection
    /// </summary>
    private IEnumerator SetRemoteAnswer(RTCSessionDescription answer)
    {
        if (peerConnection == null) yield break;
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref answer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Set remote answer failed: {setRemoteOp.Error}");
            HandleConnectionFailure();
        }
    }

    #endregion

    #region Connection Timeout & Error Handling
    
    /// <summary>
    /// Start connection timeout timer
    /// </summary>
    private void StartConnectionTimeout()
    {
        ClearConnectionTimeout();
        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutRoutine());
    }
    
    /// <summary>
    /// Clear active connection timeout
    /// </summary>
    private void ClearConnectionTimeout()
    {
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }
    }
    
    /// <summary>
    /// Connection timeout routine
    /// </summary>
    private IEnumerator ConnectionTimeoutRoutine()
    {
        yield return new WaitForSeconds(connectionTimeout);
        
        if (currentState == StreamerState.Connecting)
        {
            Debug.LogWarning($"[📡{pipelineInstanceId}] Connection timeout");
            HandleConnectionFailure();
        }
    }
    
    /// <summary>
    /// Handle connection failures with retry logic
    /// </summary>
    private void HandleConnectionFailure()
    {
        if (currentState == StreamerState.Disconnecting || currentState == StreamerState.Failed)
            return;

        Debug.LogError($"[📡{pipelineInstanceId}] Connection failed");
        
        ClearConnectionTimeout();
        
        if (retryCount < maxRetryAttempts)
        {
            retryCount++;
            Debug.Log($"[📡{pipelineInstanceId}] Retry {retryCount}/{maxRetryAttempts}");
            StartCoroutine(RetryConnection());
        }
        else
        {
            Debug.LogError($"[📡{pipelineInstanceId}] Max retries reached");
            SetState(StreamerState.Failed);
            targetRenderer?.ShowLocalNDI();
        }
    }
    
    /// <summary>
    /// Retry connection after failure
    /// </summary>
    private IEnumerator RetryConnection()
    {
        ClosePeerConnection();
        yield return new WaitForSeconds(2f);
        
        if (currentState != StreamerState.Failed && !string.IsNullOrEmpty(currentSessionId))
        {
            if (isOfferer)
                StartStreaming(currentSessionId);
            else
                StartReceiving(currentSessionId);
        }
    }

    #endregion

    #region Session Management

    /// <summary>
    /// Graceful shutdown of current session
    /// </summary>
    private IEnumerator GracefulShutdown()
    {
        if (currentState == StreamerState.Disconnecting || currentState == StreamerState.Idle)
            yield break;

        SetState(StreamerState.Disconnecting);
        
        ClearConnectionTimeout();
        
        if (textureUpdateCoroutine != null)
        {
            StopCoroutine(textureUpdateCoroutine);
            textureUpdateCoroutine = null;
        }
        
        ClosePeerConnection();
        
        currentSessionId = string.Empty;
        connectedClientId = 0;
        retryCount = 0;
        
        SetState(StreamerState.Idle);
        
        targetRenderer?.ShowLocalNDI();
        
        Debug.Log($"[📡{pipelineInstanceId}] Graceful shutdown completed");
        
        yield return null;
    }

    #endregion

    #region Cleanup
    
    /// <summary>
    /// Close and dispose peer connection safely
    /// </summary>
    private void ClosePeerConnection()
    {
        if (peerConnection != null)
        {
            try
            {
                var transceivers = peerConnection.GetTransceivers();
                foreach (var transceiver in transceivers)
                {
                    if (transceiver.Receiver?.Track is VideoStreamTrack videoTrack)
                    {
                        videoTrack.OnVideoReceived -= OnVideoReceived;
                    }
                }
                
                peerConnection.Close();
                peerConnection.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"[📡{pipelineInstanceId}] Peer connection cleanup error: {e.Message}");
            }
            finally
            {
                peerConnection = null;
            }
        }
        
        if (textureUpdateCoroutine != null)
        {
            StopCoroutine(textureUpdateCoroutine);
            textureUpdateCoroutine = null;
        }
    }
    
    /// <summary>
    /// Component destruction cleanup
    /// </summary>
    void OnDestroy()
    {
        isTextureSourceValid = false;
        
        // Unsubscribe from signaling events
        if (signaling != null)
        {
            WebRTCSignaling.OnOfferReceived -= OnOfferReceivedFiltered;
            WebRTCSignaling.OnAnswerReceived -= OnAnswerReceivedFiltered;
            WebRTCSignaling.OnIceCandidateReceived -= OnIceCandidateReceivedFiltered;
        }
        
        // Graceful shutdown
        StartCoroutine(GracefulShutdown());
        
        // Dispose WebRTC objects
        DisposeWebRTCObjects();
        
        // Unregister from engine manager
        if (WebRTCEngineManager.Instance != null)
        {
            WebRTCEngineManager.Instance.UnregisterStreamer(pipelineType);
        }
    }

    #endregion

    #region Public Properties
    
    /// <summary>Current streamer state</summary>
    public StreamerState CurrentState => currentState;
    
    /// <summary>Current session identifier</summary>
    public string CurrentSessionId => currentSessionId;
    
    /// <summary>Pipeline instance identifier</summary>
    public string PipelineInstanceId => pipelineInstanceId;
    
    /// <summary>Check if currently connected and streaming/receiving</summary>
    public bool IsConnected => currentState == StreamerState.Streaming || currentState == StreamerState.Receiving;

    #endregion
}