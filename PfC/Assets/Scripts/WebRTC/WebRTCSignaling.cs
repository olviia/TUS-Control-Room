using Unity.Netcode;
using Unity.WebRTC;
using UnityEngine;
using BroadcastPipeline;
using System;

/// <summary>
/// Simple WebRTC message relay for multiple pipelines
/// Routes signaling messages between clients with minimal validation
/// </summary>
public class WebRTCSignaling : NetworkBehaviour
{
    // Events for pipeline-specific message delivery
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnOfferReceived;
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnAnswerReceived;
    public static event Action<PipelineType, RTCIceCandidate, ulong, string> OnIceCandidateReceived;
    public static event Action<PipelineType, ulong, string> OnOfferRequested; // New: Late joiner requests offer

    #region Public Interface

    /// <summary>
    /// Send WebRTC offer for specific pipeline to specific client
    /// </summary>
    public void SendOffer(PipelineType pipeline, RTCSessionDescription offer, ulong toClient, string sessionId)
    {
        if (!IsNetworkReady()) return;

        Debug.Log($"[🔗Signaling] SendOffer {pipeline}:{sessionId} to:{toClient}");
        SendOfferServerRpc(pipeline, offer.type.ToString(), offer.sdp,
                          NetworkManager.Singleton.LocalClientId, toClient, sessionId);
    }

    /// <summary>
    /// Send WebRTC answer for specific pipeline to specific client
    /// </summary>
    public void SendAnswer(PipelineType pipeline, RTCSessionDescription answer, ulong toClient, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[🔗Signaling] SendAnswer {pipeline}:{sessionId} to:{toClient}");
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

    /// <summary>
    /// Request a fresh offer from the streamer (for late joiners)
    /// </summary>
    public void RequestOffer(PipelineType pipeline, string sessionId)
    {
        if (!IsNetworkReady()) return;

        Debug.Log($"[🔗Signaling] RequestOffer {pipeline}:{sessionId}");
        RequestOfferServerRpc(pipeline, NetworkManager.Singleton.LocalClientId, sessionId);
    }

    #endregion

    #region Server RPCs - Simple Message Relay

    [ServerRpc(RequireOwnership = false)]
    private void SendOfferServerRpc(PipelineType pipeline, string sdpType, string sdp,
                                   ulong fromClient, ulong toClient, string sessionId)
    {
        BroadcastOfferClientRpc(pipeline, sdpType, sdp, fromClient, toClient, sessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendAnswerServerRpc(PipelineType pipeline, string sdpType, string sdp,
                                    ulong fromClient, ulong toClient, string sessionId)
    {
        BroadcastAnswerClientRpc(pipeline, sdpType, sdp, fromClient, toClient, sessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendIceCandidateServerRpc(PipelineType pipeline, string candidate, string sdpMid,
                                          int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        BroadcastIceCandidateClientRpc(pipeline, candidate, sdpMid, sdpMLineIndex, fromClient, sessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOfferServerRpc(PipelineType pipeline, ulong requestingClient, string sessionId)
    {
        NotifyOfferRequestClientRpc(pipeline, requestingClient, sessionId);
    }

    #endregion

    #region Client RPCs - Message Broadcasting

    [ClientRpc]
    private void BroadcastOfferClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                        ulong fromClient, ulong toClient, string sessionId)
    {
        if (ShouldIgnoreMessage(fromClient) || !IsMessageForMe(toClient)) return;

        Debug.Log($"[🔗Signaling] Offer received {pipeline}:{sessionId} from:{fromClient}");

        var sessionDesc = CreateSessionDescription(sdpType, sdp);
        if (sessionDesc.HasValue)
        {
            OnOfferReceived?.Invoke(pipeline, sessionDesc.Value, fromClient, sessionId);
        }
    }

    [ClientRpc]
    private void BroadcastAnswerClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                         ulong fromClient, ulong toClient, string sessionId)
    {
        if (ShouldIgnoreMessage(fromClient) || !IsMessageForMe(toClient)) return;
        
        Debug.Log($"[🔗Signaling] Answer received {pipeline}:{sessionId} from:{fromClient}");
        
        var sessionDesc = CreateSessionDescription(sdpType, sdp);
        if (sessionDesc.HasValue)
        {
            OnAnswerReceived?.Invoke(pipeline, sessionDesc.Value, fromClient, sessionId);
        }
    }

    [ClientRpc]
    private void BroadcastIceCandidateClientRpc(PipelineType pipeline, string candidate, string sdpMid,
                                               int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        if (ShouldIgnoreMessage(fromClient)) return;

        try
        {
            var iceCandidate = CreateIceCandidate(candidate, sdpMid, sdpMLineIndex);
            OnIceCandidateReceived?.Invoke(pipeline, iceCandidate, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗Signaling] Failed to process ICE candidate: {e.Message}");
        }
    }

    [ClientRpc]
    private void NotifyOfferRequestClientRpc(PipelineType pipeline, ulong requestingClient, string sessionId)
    {
        Debug.Log($"[🔗Signaling] Offer requested by {requestingClient} for {pipeline}:{sessionId}");
        OnOfferRequested?.Invoke(pipeline, requestingClient, sessionId);
    }

    #endregion

    #region Message Processing

    private bool ShouldIgnoreMessage(ulong fromClient)
    {
        return NetworkManager.Singleton.LocalClientId == fromClient;
    }

    private bool IsMessageForMe(ulong toClient)
    {
        return NetworkManager.Singleton.LocalClientId == toClient;
    }

    private RTCSessionDescription? CreateSessionDescription(string sdpType, string sdp)
    {
        try
        {
            return new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗Signaling] Failed to create session description: {e.Message}");
            return null;
        }
    }

    private RTCIceCandidate CreateIceCandidate(string candidate, string sdpMid, int sdpMLineIndex)
    {
        return new RTCIceCandidate(new RTCIceCandidateInit
        {
            candidate = candidate,
            sdpMid = sdpMid,
            sdpMLineIndex = sdpMLineIndex
        });
    }

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

    #region Network Status

    private bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && 
               (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost);
    }

    #endregion

    #region Unity Netcode Events

    public override void OnNetworkSpawn()
    {
        Debug.Log("[🔗Signaling] Network spawned - signaling active");
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log("[🔗Signaling] Network despawned - signaling inactive");
    }

    #endregion

    #region Debug Information

    /// <summary>
    /// Get current signaling status for debugging
    /// </summary>
    public SignalingStats GetStats()
    {
        return new SignalingStats
        {
            IsNetworkReady = IsNetworkReady(),
            LocalClientId = NetworkManager.Singleton?.LocalClientId ?? 0,
            IsHost = NetworkManager.Singleton?.IsHost ?? false
        };
    }

    #endregion
}

/// <summary>
/// Simple signaling statistics
/// </summary>
[System.Serializable]
public struct SignalingStats
{
    public bool IsNetworkReady;
    public ulong LocalClientId;
    public bool IsHost;
}