using Unity.Netcode;
using Unity.WebRTC;
using UnityEngine;
using BroadcastPipeline;
using System;

/// <summary>
/// Enhanced WebRTC signaling with pipeline isolation
/// Handles message routing for multiple simultaneous streams
/// </summary>
public class WebRTCSignaling : NetworkBehaviour
{
    // Pipeline-aware message relay events with enhanced filtering
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnOfferReceived;
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnAnswerReceived;
    public static event Action<PipelineType, RTCIceCandidate, ulong, string> OnIceCandidateReceived;

    #region Public Interface

    /// <summary>
    /// Send WebRTC offer for specific pipeline
    /// </summary>
    public void SendOffer(PipelineType pipeline, RTCSessionDescription offer, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[🔗WebRTCSignaling] SendOffer pipeline:{pipeline} session:{sessionId}");
        SendOfferServerRpc(pipeline, offer.type.ToString(), offer.sdp, 
                          NetworkManager.Singleton.LocalClientId, sessionId);
    }

    /// <summary>
    /// Send WebRTC answer for specific pipeline to specific client
    /// </summary>
    public void SendAnswer(PipelineType pipeline, RTCSessionDescription answer, ulong toClient, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[🔗WebRTCSignaling] SendAnswer pipeline:{pipeline} session:{sessionId} to:{toClient}");
        SendAnswerServerRpc(pipeline, answer.type.ToString(), answer.sdp,
                           NetworkManager.Singleton.LocalClientId, toClient, sessionId);
    }

    /// <summary>
    /// Send ICE candidate for specific pipeline
    /// </summary>
    public void SendIceCandidate(PipelineType pipeline, RTCIceCandidate candidate, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        SendIceCandidateServerRpc(pipeline, candidate.Candidate, candidate.SdpMid,
                                 candidate.SdpMLineIndex ?? 0, NetworkManager.Singleton.LocalClientId, sessionId);
    }

    #endregion

    #region Server RPCs - Pipeline-Aware Message Relay

    /// <summary>
    /// Server relay for WebRTC offers with pipeline filtering
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SendOfferServerRpc(PipelineType pipeline, string sdpType, string sdp, 
                                   ulong fromClient, string sessionId)
    {
            ReceiveOfferClientRpc(pipeline, sdpType, sdp, fromClient, sessionId);
        
    }

    /// <summary>
    /// Server relay for WebRTC answers with pipeline filtering
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SendAnswerServerRpc(PipelineType pipeline, string sdpType, string sdp,
                                    ulong fromClient, ulong toClient, string sessionId)
    {

            ReceiveAnswerClientRpc(pipeline, sdpType, sdp, fromClient, toClient, sessionId);
        
    }

    /// <summary>
    /// Server relay for ICE candidates with pipeline filtering
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void SendIceCandidateServerRpc(PipelineType pipeline, string candidate, string sdpMid,
                                          int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        // ICE candidates are less critical, relay with basic validation
        if (IsValidSessionFormat(sessionId))
        {
            ReceiveIceCandidateClientRpc(pipeline, candidate, sdpMid, sdpMLineIndex, fromClient, sessionId);
        }
    }

    #endregion

    #region Client RPCs - Enhanced Pipeline Filtering

    /// <summary>
    /// Client RPC for receiving WebRTC offers
    /// </summary>
    [ClientRpc]
    private void ReceiveOfferClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                      ulong fromClient, string sessionId)
    {
        // Prevent self-reception
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        Debug.Log($"[🔗WebRTCSignaling] Client {NetworkManager.Singleton.LocalClientId} received offer pipeline:{pipeline} session:{sessionId} from:{fromClient}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            
            // Pipeline-filtered event dispatch
            OnOfferReceived?.Invoke(pipeline, sessionDesc, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing offer for {pipeline}: {e.Message}");
        }
    }

    /// <summary>
    /// Client RPC for receiving WebRTC answers
    /// </summary>
    [ClientRpc]
    private void ReceiveAnswerClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                       ulong fromClient, ulong toClient, string sessionId)
    {
        // Only process if this client is the intended recipient
        if (NetworkManager.Singleton.LocalClientId != toClient) return;
        
        Debug.Log($"[🔗WebRTCSignaling] Client {toClient} received answer pipeline:{pipeline} session:{sessionId} from:{fromClient}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            
            // Pipeline-filtered event dispatch
            OnAnswerReceived?.Invoke(pipeline, sessionDesc, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing answer for {pipeline}: {e.Message}");
        }
    }

    /// <summary>
    /// Client RPC for receiving ICE candidates
    /// </summary>
    [ClientRpc]
    private void ReceiveIceCandidateClientRpc(PipelineType pipeline, string candidate, string sdpMid,
                                             int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        // Prevent self-reception
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        try
        {
            var iceCandidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            });
            
            // Pipeline-filtered event dispatch
            OnIceCandidateReceived?.Invoke(pipeline, iceCandidate, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing ICE candidate for {pipeline}: {e.Message}");
        }
    }

    #endregion

    #region Session Validation

    /// <summary>
    /// Validate session against current coordinator state
    /// </summary>
    private bool ValidateSession(PipelineType pipeline, string sessionId, ulong fromClient)
    {
        // Basic validation
        if (!IsValidSessionFormat(sessionId)) return false;
        
        // Get coordinator to validate session
        var coordinator = FindObjectOfType<NetworkStreamCoordinator>();
        if (coordinator == null) return true; // Allow if no coordinator (fallback)
        
        var currentAssignment = coordinator.GetCurrentAssignment(pipeline);
        
        // Validate session ID and client authorization
        bool isValidSession = currentAssignment.isActive && 
                             currentAssignment.sessionId == sessionId &&
                             currentAssignment.directorClientId == fromClient;
        
        if (!isValidSession)
        {
            Debug.LogWarning($"[🔗WebRTCSignaling] Session validation failed for {pipeline}:{sessionId} from client {fromClient}");
            Debug.LogWarning($"[🔗WebRTCSignaling] Expected session:{currentAssignment.sessionId} from client:{currentAssignment.directorClientId}");
        }
        
        return isValidSession;
    }

    /// <summary>
    /// Validate basic session ID format
    /// </summary>
    private bool IsValidSessionFormat(string sessionId)
    {
        return !string.IsNullOrEmpty(sessionId) && sessionId.Length > 5;
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Check if network is ready for signaling operations
    /// </summary>
    private bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && 
               (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost);
    }

    /// <summary>
    /// Parse SDP type string to WebRTC enum
    /// </summary>
    private RTCSdpType ParseSdpType(string sdpType)
    {
        return sdpType.ToLower() switch
        {
            "offer" => RTCSdpType.Offer,
            "answer" => RTCSdpType.Answer,
            "pranswer" => RTCSdpType.Pranswer,
            "rollback" => RTCSdpType.Rollback,
            _ => RTCSdpType.Offer
        };
    }

    #endregion

    #region Network Events

    public override void OnNetworkSpawn()
    {
        Debug.Log("[🔗WebRTCSignaling] Network spawned - signaling active");
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log("[🔗WebRTCSignaling] Network despawned - signaling inactive");
    }

    #endregion

    #region Debug & Monitoring

    /// <summary>
    /// Get signaling statistics for monitoring
    /// </summary>
    public SignalingStats GetSignalingStats()
    {
        var coordinator = FindObjectOfType<NetworkStreamCoordinator>();
        return new SignalingStats
        {
            IsNetworkReady = IsNetworkReady(),
            ActiveStreamCount = coordinator?.GetActiveStreamCount() ?? 0,
            LocalClientId = NetworkManager.Singleton?.LocalClientId ?? 0,
            IsHost = NetworkManager.Singleton?.IsHost ?? false
        };
    }

    #endregion
}

/// <summary>
/// Signaling statistics for monitoring and debugging
/// </summary>
[System.Serializable]
public struct SignalingStats
{
    public bool IsNetworkReady;
    public int ActiveStreamCount;
    public ulong LocalClientId;
    public bool IsHost;
}