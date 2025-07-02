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
    Idle,
    Connecting,
    Connected,
    Streaming,
    Receiving,
    Disconnecting,
    Failed
}

public class WebRTCStreamer : MonoBehaviour
{
    [Header("Configuration")]
    public PipelineType pipelineType;
    public NdiReceiver ndiReceiver;
    public WebRTCRenderer targetRenderer;
    
    [Header("WebRTC Settings")]
    [SerializeField] private int textureWidth = 1920;
    [SerializeField] private int textureHeight = 1080;
    [SerializeField] private float connectionTimeout = 10f;
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
    private bool isOfferer = false; //  Track who creates the offer
    public static event Action<PipelineType, StreamerState, string> OnStateChanged;
    
    #region Initialization

    void Start()
    {
        InitializeWebRTC();
        SetupSignaling();
        SetState(StreamerState.Idle);
    }
    
    private void InitializeWebRTC()
    {
        StartCoroutine(WebRTC.Update());
        
        var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
        webRtcTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        webRtcTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);
        
        Debug.Log($"[📡WebRTCStreamer] Initialized {pipelineType} with {textureWidth}x{textureHeight}");
    }
    
    private void SetupSignaling()
    {
        signaling = FindObjectOfType<WebRTCSignaling>();
        if (signaling == null)
        {
            Debug.LogError($"[📡WebRTCStreamer] No WebRTCSignaling found for {pipelineType}");
            return;
        }
        
        WebRTCSignaling.OnOfferReceived += HandleOfferReceived;
        WebRTCSignaling.OnAnswerReceived += HandleAnswerReceived;
        WebRTCSignaling.OnIceCandidateReceived += HandleIceCandidateReceived;
    }

    #endregion

    #region State Management

    private void SetState(StreamerState newState)
    {
        if (currentState != newState)
        {
            Debug.Log($"[📡WebRTCStreamer] {pipelineType} state: {currentState} → {newState} session:{currentSessionId}");
            currentState = newState;
            OnStateChanged?.Invoke(pipelineType, newState, currentSessionId);
        }
    }

    #endregion

    #region Public Interface
    
    public void StartStreaming(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError($"[📡WebRTCStreamer] Invalid session ID for {pipelineType}");
            return;
        }
        if (currentState != StreamerState.Idle)
        {
            ForceStop();
        }

        currentSessionId = sessionId;
        isOfferer = true; // This peer will create the offer
        SetState(StreamerState.Connecting);
        
        if (!ValidateNdiSource())
        {
            SetState(StreamerState.Failed);
            return;
        }
        
        CreatePeerConnection();
        AddTracksToConnection();
        StartTextureUpdates();
        StartConnectionTimeout();
        
        // Create offer immediately
        StartCoroutine(CreateAndSendOffer());
        
        Debug.Log($"[📡WebRTCStreamer] Started streaming session {sessionId} for {pipelineType} - WILL CREATE OFFER");
    }
    
    public void StartReceiving(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            Debug.LogError($"[📡WebRTCStreamer] Invalid session ID for receiving {pipelineType}");
            return;
        }

        Debug.Log($"[📡WebRTCStreamer] StartReceiving {pipelineType} session:{sessionId}");
        
        // Stop any existing session immediately
        if (currentState != StreamerState.Idle)
        {
            ForceStop();
        }
        
        currentSessionId = sessionId;
        isOfferer = false; // This peer will wait for offer and create answer
        SetState(StreamerState.Connecting);
        
        CreatePeerConnection();
        StartConnectionTimeout();
        
        Debug.Log($"[📡WebRTCStreamer] Ready to receive session {sessionId} for {pipelineType} - WILL WAIT FOR OFFER");
    }
    private void ForceStop()
    {
        Debug.Log($"[📡WebRTCStreamer] Force stopping {pipelineType}");
        
        StopAllCoroutines();
        ClearConnectionTimeout();
        ClosePeerConnection();
        
        currentSessionId = string.Empty;
        isOfferer = false;
        connectedClientId = 0;
        retryCount = 0;
        
        SetState(StreamerState.Idle);
        targetRenderer?.ShowLocalNDI();
    }
    public void StopStreaming()
    {
        if (currentState == StreamerState.Idle || currentState == StreamerState.Disconnecting)
        {
            return;
        }

        Debug.Log($"[📡WebRTCStreamer] StopStreaming {pipelineType} session:{currentSessionId}");
        StartCoroutine(GracefulShutdown());
    }

    #endregion

    #region Session Management

    private IEnumerator StartStreamingCoroutine(string sessionId)
    {
        // Stop any existing session first
        if (currentState != StreamerState.Idle)
        {
            yield return StartCoroutine(GracefulShutdown());
            yield return new WaitForSeconds(0.5f);
        }
        
        currentSessionId = sessionId;
        SetState(StreamerState.Connecting);
        
        if (!ValidateNdiSource())
        {
            SetState(StreamerState.Failed);
            yield break;
        }
        
        CreatePeerConnection();
        AddTracksToConnection();
        StartTextureUpdates();
        StartConnectionTimeout();
        
        yield return StartCoroutine(CreateAndSendOffer());
        
        Debug.Log($"[📡WebRTCStreamer] Started streaming session {sessionId} for {pipelineType}");
    }

    private IEnumerator StartReceivingCoroutine(string sessionId)
    {
        // Stop any existing session first
        if (currentState != StreamerState.Idle)
        {
            yield return StartCoroutine(GracefulShutdown());
            yield return new WaitForSeconds(0.5f);
        }
        
        currentSessionId = sessionId;
        SetState(StreamerState.Connecting);
        
        CreatePeerConnection();
        StartConnectionTimeout();
        
        Debug.Log($"[📡WebRTCStreamer] Ready to receive session {sessionId} for {pipelineType}");
    }

    private IEnumerator GracefulShutdown()
    {
        if (currentState == StreamerState.Disconnecting || currentState == StreamerState.Idle)
        {
            yield break;
        }

        SetState(StreamerState.Disconnecting);
        
        // Stop all ongoing operations
        ClearConnectionTimeout();
        
        if (textureUpdateCoroutine != null)
        {
            StopCoroutine(textureUpdateCoroutine);
            textureUpdateCoroutine = null;
        }
        
        // Close peer connection
        ClosePeerConnection();
        
        // Reset state
        currentSessionId = string.Empty;
        connectedClientId = 0;
        retryCount = 0;
        
        SetState(StreamerState.Idle);
        
        // Show local content
        targetRenderer?.ShowLocalNDI();
        
        Debug.Log($"[📡WebRTCStreamer] Graceful shutdown completed for {pipelineType}");
        
        yield return null;
    }

    #endregion

    #region Connection Management
    
    private bool ValidateNdiSource()
    {
        if (ndiReceiver == null)
        {
            Debug.LogError($"[📡WebRTCStreamer] No NDI receiver assigned for {pipelineType}");
            return false;
        }
        
        var ndiTexture = ndiReceiver.GetTexture();
        if (ndiTexture == null || ndiTexture.width <= 0)
        {
            Debug.LogError($"[📡WebRTCStreamer] NDI texture invalid for {pipelineType}");
            return false;
        }
        
        Debug.Log($"[📡WebRTCStreamer] NDI texture validated: {ndiTexture.width}x{ndiTexture.height}");
        return true;
    }
    
    private void CreatePeerConnection()
    {
        ClosePeerConnection(); // Ensure clean state
        
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
        
        Debug.Log($"[📡WebRTCStreamer] Created peer connection for {pipelineType} session {currentSessionId}");
    }
    
    private void AddTracksToConnection()
    {
        if (peerConnection == null || videoTrack == null) return;
        
        try
        {
            peerConnection.AddTrack(videoTrack);
            Debug.Log($"[📡WebRTCStreamer] Added video track for {pipelineType}");
            
            var audioSource = ndiReceiver?.GetComponentInChildren<AudioSource>();
            if (audioSource != null)
            {
                audioTrack = new AudioStreamTrack(audioSource);
                peerConnection.AddTrack(audioTrack);
                Debug.Log($"[📡WebRTCStreamer] Added audio track for {pipelineType}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to add tracks: {e.Message}");
            SetState(StreamerState.Failed);
        }
    }
    
    private void StartTextureUpdates()
    {
        if (textureUpdateCoroutine != null)
            StopCoroutine(textureUpdateCoroutine);
            
        textureUpdateCoroutine = StartCoroutine(UpdateTextureLoop());
    }
    
    private IEnumerator UpdateTextureLoop()
    {
        while (currentState == StreamerState.Connecting || currentState == StreamerState.Streaming)
        {
            if (ndiReceiver != null && webRtcTexture != null)
            {
                var ndiTexture = ndiReceiver.GetTexture();
                if (ndiTexture != null && ndiTexture.width > 0)
                {
                    try
                    {
                        Graphics.Blit(ndiTexture, webRtcTexture);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[📡WebRTCStreamer] Texture blit failed: {e.Message}");
                    }
                }
            }
            
            yield return new WaitForEndOfFrame();
        }
    }

    #endregion

    #region WebRTC Event Handlers
    
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        Debug.Log($"[📡WebRTCStreamer] Generated ICE candidate for {pipelineType}");
        signaling?.SendIceCandidate(pipelineType, candidate, currentSessionId);
    }
    
    private void OnTrackReceived(RTCTrackEvent e)
    {
        Debug.Log($"[📡WebRTCStreamer] Track received for {pipelineType}: {e.Track.Kind}");
        
        if (e.Track is VideoStreamTrack videoStreamTrack)
        {
            videoStreamTrack.OnVideoReceived += OnVideoReceived;
            ClearConnectionTimeout();
        }
    }
    
    private void OnVideoReceived(Texture texture)
    {
        Debug.Log($"[📡WebRTCStreamer] OnVideoReceived for {pipelineType}: {texture.width}x{texture.height}");
        
        if (targetRenderer != null && texture != null)
        {
            var tempTrack = new VideoStreamTrack(texture);
            targetRenderer.ShowRemoteStream(tempTrack, currentSessionId);
            Debug.Log($"[📡WebRTCStreamer] Applied video texture to renderer for {pipelineType}");
        }
    }
    
    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        Debug.Log($"[📡WebRTCStreamer] Connection state: {state} for {pipelineType} session:{currentSessionId}");
        
        switch (state)
        {
            case RTCPeerConnectionState.Connected:
                if (isOfferer)
                    SetState(StreamerState.Streaming);
                else
                    SetState(StreamerState.Receiving);
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
    
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"[📡WebRTCStreamer] ICE connection state: {state} for {pipelineType}");
        
        if (state == RTCIceConnectionState.Failed && currentState != StreamerState.Disconnecting)
        {
            HandleConnectionFailure();
        }
    }

    #endregion

    #region Signaling Event Handlers
    
    private void HandleOfferReceived(PipelineType pipeline, RTCSessionDescription offer, ulong fromClient, string sessionId)
    {
        if (pipeline != pipelineType || sessionId != currentSessionId || currentState != StreamerState.Connecting || isOfferer)
        {
            Debug.LogWarning($"[📡WebRTCStreamer] Ignoring offer for {pipelineType}: pipeline match={pipeline == pipelineType}, session match={sessionId == currentSessionId}, state={currentState}");
            return;
        }
        
        Debug.Log($"[📡WebRTCStreamer] 📥 PROCESSING offer for {pipelineType} session {sessionId} from client {fromClient}");
        StartCoroutine(CreateAndSendAnswer(offer, fromClient));
    }
    
    private void HandleAnswerReceived(PipelineType pipeline, RTCSessionDescription answer, ulong fromClient, string sessionId)
    {
        if (pipeline != pipelineType || sessionId != currentSessionId || currentState != StreamerState.Connecting || !isOfferer)
        {
            Debug.LogWarning($"[📡WebRTCStreamer] Ignoring answer for {pipelineType}: pipeline match={pipeline == pipelineType}, session match={sessionId == currentSessionId}, state={currentState}");
            return;
        }
        
        Debug.Log($"[📡WebRTCStreamer] 📥 PROCESSING answer for {pipelineType} session {sessionId} from client {fromClient}");
        StartCoroutine(SetRemoteAnswer(answer));
        connectedClientId = fromClient;
    }
    
    private void HandleIceCandidateReceived(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient, string sessionId)
    {
        if (pipeline != pipelineType || sessionId != currentSessionId || peerConnection == null)
            return;
        
        try
        {
            peerConnection.AddIceCandidate(candidate);
        }
        catch (Exception e)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to add ICE candidate: {e.Message}");
        }
    }

    #endregion

    #region Offer/Answer Creation
    
    private IEnumerator CreateAndSendOffer()
    {
        if (peerConnection == null) 
        {
            Debug.LogError($"[📡WebRTCStreamer] No peer connection for offer creation");
            yield break;
        }
        
        Debug.Log($"[📡WebRTCStreamer] 🔄 Creating offer for {pipelineType} session {currentSessionId}");
        
        var offerOp = peerConnection.CreateOffer();
        yield return offerOp;
        
        if (offerOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to create offer: {offerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var offer = offerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref offer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to set local description: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        signaling.SendOffer(pipelineType, offer, currentSessionId);
        Debug.Log($"[📡WebRTCStreamer] 📤 SENT offer for {pipelineType} session {currentSessionId}");
    }
    
    private IEnumerator CreateAndSendAnswer(RTCSessionDescription offer, ulong toClient)
    {
        if (peerConnection == null) yield break;
        
        Debug.Log($"[📡WebRTCStreamer] 🔄 Creating answer for {pipelineType} session {currentSessionId}");
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref offer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to set remote description: {setRemoteOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        
        if (answerOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to create answer: {answerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answer = answerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to set local answer: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        signaling.SendAnswer(pipelineType, answer, toClient, currentSessionId);
        connectedClientId = toClient;
        
        Debug.Log($"[📡WebRTCStreamer] 📤 SENT answer for {pipelineType} session {currentSessionId} to client {toClient}");
    }
    
    private IEnumerator SetRemoteAnswer(RTCSessionDescription answer)
    {
        if (peerConnection == null) yield break;
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref answer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[📡WebRTCStreamer] Failed to set remote answer: {setRemoteOp.Error}");
            HandleConnectionFailure();
        }
    }

    #endregion

    #region Connection Timeout & Error Handling
    
    private void StartConnectionTimeout()
    {
        ClearConnectionTimeout();
        connectionTimeoutCoroutine = StartCoroutine(ConnectionTimeoutRoutine());
    }
    
    private void ClearConnectionTimeout()
    {
        if (connectionTimeoutCoroutine != null)
        {
            StopCoroutine(connectionTimeoutCoroutine);
            connectionTimeoutCoroutine = null;
        }
    }
    
    private IEnumerator ConnectionTimeoutRoutine()
    {
        yield return new WaitForSeconds(connectionTimeout);
        
        if (currentState == StreamerState.Connecting)
        {
            Debug.LogWarning($"[📡WebRTCStreamer] Connection timeout for {pipelineType} session {currentSessionId}");
            HandleConnectionFailure();
        }
    }
    
    private void HandleConnectionFailure()
    {
        if (currentState == StreamerState.Disconnecting || currentState == StreamerState.Failed)
            return;

        Debug.LogError($"[📡WebRTCStreamer] Connection failed for {pipelineType} session {currentSessionId}");
        
        ClearConnectionTimeout();
        
        if (retryCount < maxRetryAttempts)
        {
            retryCount++;
            Debug.Log($"[📡WebRTCStreamer] Retry attempt {retryCount}/{maxRetryAttempts} for {pipelineType}");
            StartCoroutine(RetryConnection());
        }
        else
        {
            Debug.LogError($"[📡WebRTCStreamer] Max retry attempts reached for {pipelineType}");
            SetState(StreamerState.Failed);
            targetRenderer?.ShowLocalNDI();
        }
    }
    
    private IEnumerator RetryConnection()
    {
        ClosePeerConnection();
        yield return new WaitForSeconds(2f);
        
        if (currentState != StreamerState.Failed && !string.IsNullOrEmpty(currentSessionId))
        {
            if (ndiReceiver != null)
                yield return StartCoroutine(StartStreamingCoroutine(currentSessionId));
            else
                yield return StartCoroutine(StartReceivingCoroutine(currentSessionId));
        }
    }

    #endregion

    #region Cleanup
    
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
                Debug.LogError($"[📡WebRTCStreamer] Error closing peer connection: {e.Message}");
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
    
    void OnDestroy()
    {
        if (signaling != null)
        {
            WebRTCSignaling.OnOfferReceived -= HandleOfferReceived;
            WebRTCSignaling.OnAnswerReceived -= HandleAnswerReceived;
            WebRTCSignaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
        }
        
        StartCoroutine(GracefulShutdown());
        
        videoTrack?.Dispose();
        audioTrack?.Dispose();
        
        if (webRtcTexture != null)
        {
            webRtcTexture.Release();
            DestroyImmediate(webRtcTexture);
        }
    }

    #endregion

    #region Public Properties
    
    public StreamerState CurrentState => currentState;
    public string CurrentSessionId => currentSessionId;
    public bool IsConnected => currentState == StreamerState.Streaming || currentState == StreamerState.Receiving;

    #endregion
}
