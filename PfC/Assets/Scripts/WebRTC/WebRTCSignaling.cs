using Unity.Netcode;
using Unity.WebRTC;
using UnityEngine;
using BroadcastPipeline;
using System;
using System.Collections.Generic;

public class WebRTCSignaling : NetworkBehaviour
{
    public static event Action<PipelineType, RTCSessionDescription, ulong> OnOfferReceived;
    public static event Action<PipelineType, RTCSessionDescription, ulong> OnAnswerReceived;
    public static event Action<PipelineType, RTCIceCandidate, ulong> OnIceCandidateReceived;

    // Track active sessions to prevent duplicate offers
    private Dictionary<PipelineType, HashSet<ulong>> activeSessions = new Dictionary<PipelineType, HashSet<ulong>>();
    
    private void Awake()
    {
        activeSessions[PipelineType.StudioLive] = new HashSet<ulong>();
        activeSessions[PipelineType.TVLive] = new HashSet<ulong>();
    }

    #region Server RPCs

    [ServerRpc(RequireOwnership = false)]
    public void SendOfferServerRpc(PipelineType pipeline, string sdpType, string sdp, ulong fromClient, ulong timestamp)
    {
        Debug.Log($"[WebRTCSignaling] Server received offer from {fromClient} for {pipeline}");
        
        // Track this session
        if (!activeSessions.ContainsKey(pipeline))
            activeSessions[pipeline] = new HashSet<ulong>();
        activeSessions[pipeline].Add(fromClient);
        
        ReceiveOfferClientRpc(pipeline, sdpType, sdp, fromClient, timestamp);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendAnswerServerRpc(PipelineType pipeline, string sdpType, string sdp, ulong fromClient, ulong toClient, ulong timestamp)
    {
        Debug.Log($"[WebRTCSignaling] Server received answer from {fromClient} to {toClient} for {pipeline}");
        ReceiveAnswerClientRpc(pipeline, sdpType, sdp, fromClient, toClient, timestamp);
    }

    [ServerRpc(RequireOwnership = false)]
    public void SendIceCandidateServerRpc(PipelineType pipeline, string candidate, string sdpMid, int sdpMLineIndex, ulong fromClient, ulong timestamp)
    {
        ReceiveIceCandidateClientRpc(pipeline, candidate, sdpMid, sdpMLineIndex, fromClient, timestamp);
    }

    [ServerRpc(RequireOwnership = false)]
    public void CloseConnectionServerRpc(PipelineType pipeline, ulong fromClient)
    {
        Debug.Log($"[WebRTCSignaling] Server received connection close from {fromClient} for {pipeline}");
        
        // Remove from active sessions
        if (activeSessions.ContainsKey(pipeline))
            activeSessions[pipeline].Remove(fromClient);
        
        NotifyConnectionClosedClientRpc(pipeline, fromClient);
    }

    #endregion

    #region Client RPCs

    [ClientRpc]
    private void ReceiveOfferClientRpc(PipelineType pipeline, string sdpType, string sdp, ulong fromClient, ulong timestamp)
    {
        // Don't receive our own offers
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        Debug.Log($"[WebRTCSignaling] Client {NetworkManager.Singleton.LocalClientId} received offer from {fromClient} for {pipeline}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            OnOfferReceived?.Invoke(pipeline, sessionDesc, fromClient);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCSignaling] Error processing offer: {e.Message}");
        }
    }

    [ClientRpc]
    private void ReceiveAnswerClientRpc(PipelineType pipeline, string sdpType, string sdp, ulong fromClient, ulong toClient, ulong timestamp)
    {
        // Only process answers directed to us
        if (NetworkManager.Singleton.LocalClientId != toClient) return;
        
        Debug.Log($"[WebRTCSignaling] Client {toClient} received answer from {fromClient} for {pipeline}");
        
        try
        {
            var sessionDesc = new RTCSessionDescription
            {
                type = ParseSdpType(sdpType),
                sdp = sdp
            };
            OnAnswerReceived?.Invoke(pipeline, sessionDesc, fromClient);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCSignaling] Error processing answer: {e.Message}");
        }
    }

    [ClientRpc]
    private void ReceiveIceCandidateClientRpc(PipelineType pipeline, string candidate, string sdpMid, int sdpMLineIndex, ulong fromClient, ulong timestamp)
    {
        // Don't receive our own candidates
        if (NetworkManager.Singleton.LocalClientId == fromClient) return;
        
        try
        {
            var iceCandidate = new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            });
            OnIceCandidateReceived?.Invoke(pipeline, iceCandidate, fromClient);
        }
        catch (Exception e)
        {
            Debug.LogError($"[WebRTCSignaling] Error processing ICE candidate: {e.Message}");
        }
    }

    [ClientRpc]
    private void NotifyConnectionClosedClientRpc(PipelineType pipeline, ulong fromClient)
    {
        Debug.Log($"[WebRTCSignaling] Connection closed notification for {pipeline} from {fromClient}");
        // Streamers can listen to this if needed for cleanup
    }

    #endregion

    #region Public Interface

    public void SendOffer(PipelineType pipeline, RTCSessionDescription offer)
    {
        if (!IsNetworkReady()) return;
        
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Debug.Log($"[WebRTCSignaling] Sending offer for {pipeline} with timestamp {timestamp}");
        
        SendOfferServerRpc(pipeline, offer.type.ToString(), offer.sdp, NetworkManager.Singleton.LocalClientId, timestamp);
    }

    public void SendAnswer(PipelineType pipeline, RTCSessionDescription answer, ulong toClient)
    {
        if (!IsNetworkReady()) return;
        
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Debug.Log($"[WebRTCSignaling] Sending answer for {pipeline} to {toClient} with timestamp {timestamp}");
        
        SendAnswerServerRpc(pipeline, answer.type.ToString(), answer.sdp, NetworkManager.Singleton.LocalClientId, toClient, timestamp);
    }

    public void SendIceCandidate(PipelineType pipeline, RTCIceCandidate candidate)
    {
        if (!IsNetworkReady()) return;
        
        var timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        
        SendIceCandidateServerRpc(
            pipeline, 
            candidate.Candidate, 
            candidate.SdpMid, 
            candidate.SdpMLineIndex ?? 0, 
            NetworkManager.Singleton.LocalClientId,
            timestamp
        );
    }

    public void CloseConnection(PipelineType pipeline)
    {
        if (!IsNetworkReady()) return;
        
        Debug.Log($"[WebRTCSignaling] Closing connection for {pipeline}");
        CloseConnectionServerRpc(pipeline, NetworkManager.Singleton.LocalClientId);
    }

    #endregion

    #region Utility

    private bool IsNetworkReady()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[WebRTCSignaling] NetworkManager is null");
            return false;
        }
        
        if (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("[WebRTCSignaling] Not connected to network");
            return false;
        }
        
        return true;
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

    public bool HasActiveSession(PipelineType pipeline, ulong clientId)
    {
        return activeSessions.ContainsKey(pipeline) && activeSessions[pipeline].Contains(clientId);
    }

    public int GetActiveSessionCount(PipelineType pipeline)
    {
        return activeSessions.ContainsKey(pipeline) ? activeSessions[pipeline].Count : 0;
    }

    #endregion

    #region Network Events

    public override void OnNetworkSpawn()
    {
        Debug.Log("[WebRTCSignaling] Network spawned");
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log("[WebRTCSignaling] Network despawned");
        
        // Clear all active sessions
        foreach (var pipeline in activeSessions.Keys)
        {
            activeSessions[pipeline].Clear();
        }
    }

    #endregion
}