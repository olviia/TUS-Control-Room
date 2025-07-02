using Unity.Netcode;
using Unity.WebRTC;
using UnityEngine;
using BroadcastPipeline;
using System;

public class WebRTCSignaling : NetworkBehaviour
{
    // Simple message relay events - no session management
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnOfferReceived;
    public static event Action<PipelineType, RTCSessionDescription, ulong, string> OnAnswerReceived;
    public static event Action<PipelineType, RTCIceCandidate, ulong, string> OnIceCandidateReceived;

    #region Public Interface

    public void SendOffer(PipelineType pipeline, RTCSessionDescription offer, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[🔗WebRTCSignaling] SendOffer pipeline:{pipeline} session:{sessionId}");
        SendOfferServerRpc(pipeline, offer.type.ToString(), offer.sdp, 
                          NetworkManager.Singleton.LocalClientId, sessionId);
    }

    public void SendAnswer(PipelineType pipeline, RTCSessionDescription answer, ulong toClient, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[🔗WebRTCSignaling] SendAnswer pipeline:{pipeline} session:{sessionId} to:{toClient}");
        SendAnswerServerRpc(pipeline, answer.type.ToString(), answer.sdp,
                           NetworkManager.Singleton.LocalClientId, toClient, sessionId);
    }

    public void SendIceCandidate(PipelineType pipeline, RTCIceCandidate candidate, string sessionId)
    {
        if (!IsNetworkReady()) return;
        
        SendIceCandidateServerRpc(pipeline, candidate.Candidate, candidate.SdpMid,
                                 candidate.SdpMLineIndex ?? 0, NetworkManager.Singleton.LocalClientId, sessionId);
    }

    #endregion

    #region Server RPCs - Simple Message Relay

    [ServerRpc(RequireOwnership = false)]
    private void SendOfferServerRpc(PipelineType pipeline, string sdpType, string sdp, 
                                   ulong fromClient, string sessionId)
    {
        Debug.Log($"[🔗WebRTCSignaling] Server relaying offer pipeline:{pipeline} session:{sessionId} from:{fromClient}");
        ReceiveOfferClientRpc(pipeline, sdpType, sdp, fromClient, sessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendAnswerServerRpc(PipelineType pipeline, string sdpType, string sdp,
                                    ulong fromClient, ulong toClient, string sessionId)
    {
        Debug.Log($"[🔗WebRTCSignaling] Server relaying answer pipeline:{pipeline} session:{sessionId} from:{fromClient} to:{toClient}");
        ReceiveAnswerClientRpc(pipeline, sdpType, sdp, fromClient, toClient, sessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SendIceCandidateServerRpc(PipelineType pipeline, string candidate, string sdpMid,
                                          int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        ReceiveIceCandidateClientRpc(pipeline, candidate, sdpMid, sdpMLineIndex, fromClient, sessionId);
    }

    #endregion

    #region Client RPCs

    [ClientRpc]
    private void ReceiveOfferClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                      ulong fromClient, string sessionId)
    {
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        Debug.Log($"[🔗WebRTCSignaling] Client {NetworkManager.Singleton.LocalClientId} received offer pipeline:{pipeline} session:{sessionId} from:{fromClient}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            OnOfferReceived?.Invoke(pipeline, sessionDesc, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing offer: {e.Message}");
        }
    }

    [ClientRpc]
    private void ReceiveAnswerClientRpc(PipelineType pipeline, string sdpType, string sdp,
                                       ulong fromClient, ulong toClient, string sessionId)
    {
        if (NetworkManager.Singleton.LocalClientId != toClient) return;
        
        Debug.Log($"[🔗WebRTCSignaling] Client {toClient} received answer pipeline:{pipeline} session:{sessionId} from:{fromClient}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            OnAnswerReceived?.Invoke(pipeline, sessionDesc, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing answer: {e.Message}");
        }
    }

    [ClientRpc]
    private void ReceiveIceCandidateClientRpc(PipelineType pipeline, string candidate, string sdpMid,
                                             int sdpMLineIndex, ulong fromClient, string sessionId)
    {
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        try
        {
            var iceCandidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            });
            OnIceCandidateReceived?.Invoke(pipeline, iceCandidate, fromClient, sessionId);
        }
        catch (Exception e)
        {
            Debug.LogError($"[🔗WebRTCSignaling] Error processing ICE candidate: {e.Message}");
        }
    }

    #endregion

    #region Utility Methods

    private bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && 
               (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost);
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

    #region Network Events

    public override void OnNetworkSpawn()
    {
        Debug.Log("[🔗WebRTCSignaling] Network spawned");
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log("[🔗WebRTCSignaling] Network despawned");
    }

    #endregion
}