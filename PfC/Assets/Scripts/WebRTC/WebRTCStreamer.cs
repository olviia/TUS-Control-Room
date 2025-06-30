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
    private RenderTexture copyTexture;
    
    private WebRTCSignaling signaling;
    private StreamerState currentState = StreamerState.Idle;
    private ulong connectedClientId;
    private int retryCount = 0;
    private Coroutine connectionTimeoutCoroutine;
    private Coroutine textureUpdateCoroutine;
    
    // Events for debugging
    public static event Action<PipelineType, StreamerState> OnStateChanged;
    
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
        copyTexture = new RenderTexture(textureWidth, textureHeight, 0, format);
        
        webRtcTexture.Create();
        copyTexture.Create();
        
        videoTrack = new VideoStreamTrack(webRtcTexture);
        
        Debug.Log($"[WebRTCStreamer] Initialized for {pipelineType} with {textureWidth}x{textureHeight}");
    }
    
    private void SetupSignaling()
    {
        signaling = FindObjectOfType<WebRTCSignaling>();
        if (signaling == null)
        {
            Debug.LogError($"[WebRTCStreamer] No WebRTCSignaling found for {pipelineType}");
            return;
        }
        
        WebRTCSignaling.OnOfferReceived += HandleOfferReceived;
        WebRTCSignaling.OnAnswerReceived += HandleAnswerReceived;
        WebRTCSignaling.OnIceCandidateReceived += HandleIceCandidateReceived;
    }
    
    private void SetState(StreamerState newState)
    {
        if (currentState != newState)
        {
            Debug.Log($"[WebRTCStreamer] {pipelineType} state: {currentState} → {newState}");
            currentState = newState;
            OnStateChanged?.Invoke(pipelineType, newState);
        }
    }
    
    #region Public Interface
    
    public void StartStreaming()
    {
        if (currentState == StreamerState.Streaming || currentState == StreamerState.Connecting)
        {
            Debug.Log($"[WebRTCStreamer] {pipelineType} already streaming or connecting");
            return;
        }
        
        if (ndiReceiver == null)
        {
            Debug.LogError($"[WebRTCStreamer] No NDI receiver assigned for {pipelineType}");
            SetState(StreamerState.Failed);
            return;
        }
        
        // Check if NDI has valid texture
        var ndiTexture = ndiReceiver.GetTexture();
        if (ndiTexture == null || ndiTexture.width <= 0)
        {
            Debug.LogError($"[WebRTCStreamer] NDI texture invalid: {ndiTexture?.width}x{ndiTexture?.height} for {pipelineType}");
            SetState(StreamerState.Failed);
            return;
        }
        
        Debug.Log($"[WebRTCStreamer] NDI texture valid: {ndiTexture.width}x{ndiTexture.height}");
        
        StopAllConnections(); // Clean slate
        SetState(StreamerState.Connecting);
        
        StartCoroutine(InitiateStreaming());
        StartConnectionTimeout();
        
        Debug.Log($"[WebRTCStreamer] 📡 Starting to stream {pipelineType}");
    }
    
    public void StartReceiving()
    {
        if (currentState == StreamerState.Receiving || currentState == StreamerState.Connecting)
        {
            Debug.Log($"[WebRTCStreamer] {pipelineType} already receiving or connecting");
            return;
        }
        
        StopAllConnections(); // Clean slate
        SetState(StreamerState.Connecting);
        
        CreatePeerConnection();
        StartConnectionTimeout();
        
        Debug.Log($"[WebRTCStreamer] 📺 Ready to receive {pipelineType}");
    }
    
    public void StopStreaming()
    {
        Debug.Log($"[WebRTCStreamer] 🛑 FORCE STOP streaming {pipelineType}");
        
        // Clear any pending operations
        StopAllCoroutines();
        
        StopAllConnections();
        SetState(StreamerState.Idle);
        retryCount = 0;
        
        // Notify signaling to close connection
        if (signaling != null)
        {
            signaling.CloseConnection(pipelineType);
        }
        
        Debug.Log($"[WebRTCStreamer] ⏹️ Stopped streaming {pipelineType}");
    }
    
    // Add this method for debugging
    [ContextMenu("Debug NDI Texture")]
    public void DebugNDITexture()
    {
        if (ndiReceiver != null)
        {
            var texture = ndiReceiver.GetTexture();
            Debug.Log($"[WebRTCStreamer] NDI Receiver: {ndiReceiver.ndiName}");
            Debug.Log($"[WebRTCStreamer] WebRTC Texture: {webRtcTexture?.width}x{webRtcTexture?.height}");
        }
        else
        {
            Debug.LogError("[WebRTCStreamer] No NDI receiver assigned");
        }
    }
    
    #endregion
    
    #region Connection Management
    
    private IEnumerator InitiateStreaming()
    {
        yield return new WaitForSeconds(0.1f); // Brief delay for stability
        
        CreatePeerConnection();
        AddTracksToConnection();
        StartTextureUpdates();
        
        yield return StartCoroutine(CreateAndSendOffer());
    }
    
    private void CreatePeerConnection()
    {
        if (peerConnection != null)
        {
            Debug.LogWarning($"[WebRTCStreamer] Disposing existing peer connection for {pipelineType}");
            ClosePeerConnection();
        }
        
        var config = new RTCConfiguration
        {
            iceServers = new RTCIceServer[]
            {
                new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
                new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } },
                new RTCIceServer { urls = new string[] { "stun:stun2.l.google.com:19302" } }
            }
        };
        
        peerConnection = new RTCPeerConnection(ref config);
        
        peerConnection.OnIceCandidate = OnIceCandidate;
        peerConnection.OnTrack = OnTrackReceived;
        peerConnection.OnConnectionStateChange = OnConnectionStateChange;
        peerConnection.OnIceConnectionChange = OnIceConnectionChange;
        
        Debug.Log($"[WebRTCStreamer] Created peer connection for {pipelineType}");
    }
    
    private void AddTracksToConnection()
    {
        if (peerConnection == null || videoTrack == null) return;
        
        try
        {
            peerConnection.AddTrack(videoTrack);
            Debug.Log($"[WebRTCStreamer] Added video track for {pipelineType}");
            
            // Add audio if available
            var audioSource = ndiReceiver?.GetComponentInChildren<AudioSource>();
            if (audioSource != null)
            {
                audioTrack = new AudioStreamTrack(audioSource);
                peerConnection.AddTrack(audioTrack);
                Debug.Log($"[WebRTCStreamer] Added audio track for {pipelineType}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to add tracks: {e.Message}");
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
                if (ndiTexture != null && ndiTexture.width > 0 && ndiTexture.height > 0)
                {
                    try
                    {
                        // Copy NDI texture to our WebRTC texture
                        Graphics.Blit(ndiTexture, webRtcTexture);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[WebRTCStreamer] Texture blit failed: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[WebRTCStreamer] NDI texture invalid: {ndiTexture?.width}x{ndiTexture?.height}");
                }
            }
            
            yield return new WaitForEndOfFrame();
        }
    }
    
    private IEnumerator CreateAndSendOffer()
    {
        if (peerConnection == null)
        {
            Debug.LogError($"[WebRTCStreamer] No peer connection for offer creation");
            yield break;
        }
        
        var offerOp = peerConnection.CreateOffer();
        yield return offerOp;
        
        if (offerOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to create offer: {offerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var offer = offerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref offer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to set local description: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        // Send offer via signaling
        signaling.SendOffer(pipelineType, offer);
        Debug.Log($"[WebRTCStreamer] 📤 Sent offer for {pipelineType}");
    }
    
    private IEnumerator CreateAndSendAnswer(RTCSessionDescription offer, ulong toClient)
    {
        if (peerConnection == null)
        {
            Debug.LogError($"[WebRTCStreamer] No peer connection for answer creation");
            yield break;
        }
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref offer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to set remote description: {setRemoteOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answerOp = peerConnection.CreateAnswer();
        yield return answerOp;
        
        if (answerOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to create answer: {answerOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        var answer = answerOp.Desc;
        var setLocalOp = peerConnection.SetLocalDescription(ref answer);
        yield return setLocalOp;
        
        if (setLocalOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to set local answer: {setLocalOp.Error}");
            HandleConnectionFailure();
            yield break;
        }
        
        // Send answer via signaling
        signaling.SendAnswer(pipelineType, answer, toClient);
        connectedClientId = toClient;
        
        Debug.Log($"[WebRTCStreamer] 📤 Sent answer for {pipelineType} to client {toClient}");
    }
    
    #endregion
    
    #region Event Handlers
    
    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        if (signaling != null)
        {
            signaling.SendIceCandidate(pipelineType, candidate);
        }
    }
    
    private void OnTrackReceived(RTCTrackEvent e)
    {
        Debug.Log($"[WebRTCStreamer] 📺 Received track for {pipelineType}: {e.Track.Kind}");
        
        if (e.Track is VideoStreamTrack videoTrack)
        {
            Debug.Log($"[WebRTCStreamer] Setting up OnVideoReceived for {pipelineType}");
            
            // Use OnVideoReceived event instead of direct texture access
            videoTrack.OnVideoReceived += OnVideoReceived;
            
            SetState(StreamerState.Receiving);
            ClearConnectionTimeout();
        }
        else if (e.Track is AudioStreamTrack audioTrack)
        {
            Debug.Log($"[WebRTCStreamer] 🔊 Received audio track for {pipelineType}");
        }
    }
    
    private void OnVideoReceived(Texture texture)
    {
        Debug.Log($"[WebRTCStreamer] 📺 OnVideoReceived called for {pipelineType}: {texture.width}x{texture.height}");
        
        if (targetRenderer != null && texture != null)
        {
            // Create a temporary VideoStreamTrack for the renderer
            var tempTrack = new VideoStreamTrack(texture);
            targetRenderer.ShowRemoteStream(tempTrack);
            
            Debug.Log($"[WebRTCStreamer] ✅ Applied video texture to renderer for {pipelineType}");
        }
    }
    
    private void OnConnectionStateChange(RTCPeerConnectionState state)
    {
        Debug.Log($"[WebRTCStreamer] 🔗 {pipelineType} connection state: {state}");
        
        switch (state)
        {
            case RTCPeerConnectionState.Connected:
                if (currentState != StreamerState.Receiving)
                    SetState(StreamerState.Streaming);
                ClearConnectionTimeout();
                retryCount = 0;
                break;
                
            case RTCPeerConnectionState.Failed:
            case RTCPeerConnectionState.Disconnected:
                HandleConnectionFailure();
                break;
        }
    }
    
    private void OnIceConnectionChange(RTCIceConnectionState state)
    {
        Debug.Log($"[WebRTCStreamer] 🧊 {pipelineType} ICE connection state: {state}");
        
        if (state == RTCIceConnectionState.Failed)
        {
            HandleConnectionFailure();
        }
    }
    
    #endregion
    
    #region Signaling Handlers
    
    private void HandleOfferReceived(PipelineType pipeline, RTCSessionDescription offer, ulong fromClient)
    {
        if (pipeline != pipelineType || currentState != StreamerState.Connecting) return;
        
        Debug.Log($"[WebRTCStreamer] 📥 Received offer for {pipelineType} from client {fromClient}");
        StartCoroutine(CreateAndSendAnswer(offer, fromClient));
    }
    
    private void HandleAnswerReceived(PipelineType pipeline, RTCSessionDescription answer, ulong fromClient)
    {
        if (pipeline != pipelineType || currentState != StreamerState.Connecting) return;
        
        Debug.Log($"[WebRTCStreamer] 📥 Received answer for {pipelineType} from client {fromClient}");
        StartCoroutine(SetRemoteAnswer(answer));
        connectedClientId = fromClient;
    }
    
    private void HandleIceCandidateReceived(PipelineType pipeline, RTCIceCandidate candidate, ulong fromClient)
    {
        if (pipeline != pipelineType || peerConnection == null) return;
        
        try
        {
            peerConnection.AddIceCandidate(candidate);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to add ICE candidate: {e.Message}");
        }
    }
    
    private IEnumerator SetRemoteAnswer(RTCSessionDescription answer)
    {
        if (peerConnection == null) yield break;
        
        var setRemoteOp = peerConnection.SetRemoteDescription(ref answer);
        yield return setRemoteOp;
        
        if (setRemoteOp.IsError)
        {
            Debug.LogError($"[WebRTCStreamer] Failed to set remote answer: {setRemoteOp.Error}");
            HandleConnectionFailure();
        }
    }
    
    #endregion
    
    #region Connection Timeout & Retry
    
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
            Debug.LogWarning($"[WebRTCStreamer] Connection timeout for {pipelineType}");
            HandleConnectionFailure();
        }
    }
    
    private void HandleConnectionFailure()
    {
        Debug.LogError($"[WebRTCStreamer] Connection failed for {pipelineType}");
        
        ClearConnectionTimeout();
        ClosePeerConnection();
        
        if (retryCount < maxRetryAttempts)
        {
            retryCount++;
            Debug.Log($"[WebRTCStreamer] Retry attempt {retryCount}/{maxRetryAttempts} for {pipelineType}");
            
            StartCoroutine(RetryConnection());
        }
        else
        {
            Debug.LogError($"[WebRTCStreamer] Max retry attempts reached for {pipelineType}");
            SetState(StreamerState.Failed);
            
            // Show local content on failure
            if (targetRenderer != null)
                targetRenderer.ShowLocalNDI();
        }
    }
    
    private IEnumerator RetryConnection()
    {
        yield return new WaitForSeconds(2f); // Wait before retry
        
        if (currentState != StreamerState.Failed)
        {
            // Retry the last operation
            if (ndiReceiver != null) // Was streaming
                StartStreaming();
            else // Was receiving
                StartReceiving();
        }
    }
    
    #endregion
    
    #region Cleanup
    
    private void StopAllConnections()
    {
        ClearConnectionTimeout();
        
        if (textureUpdateCoroutine != null)
        {
            StopCoroutine(textureUpdateCoroutine);
            textureUpdateCoroutine = null;
        }
        
        ClosePeerConnection();
        
        if (targetRenderer != null && targetRenderer.IsShowingRemoteStream)
            targetRenderer.ShowLocalNDI();
    }
    
    private void ClosePeerConnection()
    {
        if (peerConnection != null)
        {
            try
            {
                peerConnection.Close();
                peerConnection.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebRTCStreamer] Error closing peer connection: {e.Message}");
            }
            finally
            {
                peerConnection = null;
            }
        }
    }
    
    void OnDestroy()
    {
        StopStreaming();
        
        // Cleanup signaling events
        if (signaling != null)
        {
            WebRTCSignaling.OnOfferReceived -= HandleOfferReceived;
            WebRTCSignaling.OnAnswerReceived -= HandleAnswerReceived;
            WebRTCSignaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
        }
        
        // Cleanup video track events
        if (peerConnection != null)
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
        
        // Cleanup tracks
        videoTrack?.Dispose();
        audioTrack?.Dispose();
        
        // Cleanup textures
        if (webRtcTexture != null)
        {
            webRtcTexture.Release();
            DestroyImmediate(webRtcTexture);
        }
        
        if (copyTexture != null)
        {
            copyTexture.Release();
            DestroyImmediate(copyTexture);
        }
    }
    
    #endregion
}