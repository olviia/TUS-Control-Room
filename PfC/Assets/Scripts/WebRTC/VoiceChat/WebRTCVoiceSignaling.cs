using System;
using Unity.Netcode;
using UnityEngine;

namespace TUS.WebRTC.VoiceChat
{
    /// <summary>
    /// Handles WebRTC signaling for voice communication using Unity Netcode
    /// </summary>
    public class WebRTCVoiceSignaling : NetworkBehaviour
    {
        public static WebRTCVoiceSignaling Instance { get; private set; }

        // Events for signaling messages
        public event Action<ulong, string, string, string> OnOfferReceived;
        public event Action<ulong, string, string, string> OnAnswerReceived;
        public event Action<ulong, string, string, int, string> OnIceCandidateReceived;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        #region Sending Methods

        /// <summary>
        /// Send WebRTC offer to a specific client
        /// </summary>
        public void SendOffer(ulong targetClientId, string sdpType, string sdp, string sessionId)
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                // Server sends directly to target client
                SendOfferClientRpc(NetworkManager.Singleton.LocalClientId, sdpType, sdp, sessionId,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
            }
            else
            {
                // Client sends through server
                SendOfferServerRpc(targetClientId, sdpType, sdp, sessionId);
            }
        }

        /// <summary>
        /// Send WebRTC answer to a specific client
        /// </summary>
        public void SendAnswer(ulong targetClientId, string sdpType, string sdp, string sessionId)
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                SendAnswerClientRpc(NetworkManager.Singleton.LocalClientId, sdpType, sdp, sessionId,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
            }
            else
            {
                SendAnswerServerRpc(targetClientId, sdpType, sdp, sessionId);
            }
        }

        /// <summary>
        /// Send ICE candidate to a specific client
        /// </summary>
        public void SendIceCandidate(ulong targetClientId, string candidate, string sdpMid, int sdpMLineIndex, string sessionId)
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                SendIceCandidateClientRpc(NetworkManager.Singleton.LocalClientId, candidate, sdpMid, sdpMLineIndex, sessionId,
                    new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
            }
            else
            {
                SendIceCandidateServerRpc(targetClientId, candidate, sdpMid, sdpMLineIndex, sessionId);
            }
        }

        #endregion

        #region Server RPCs

        [ServerRpc(RequireOwnership = false)]
        private void SendOfferServerRpc(ulong targetClientId, string sdpType, string sdp, string sessionId, ServerRpcParams serverRpcParams = default)
        {
            ulong senderClientId = serverRpcParams.Receive.SenderClientId;
            SendOfferClientRpc(senderClientId, sdpType, sdp, sessionId,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendAnswerServerRpc(ulong targetClientId, string sdpType, string sdp, string sessionId, ServerRpcParams serverRpcParams = default)
        {
            ulong senderClientId = serverRpcParams.Receive.SenderClientId;
            SendAnswerClientRpc(senderClientId, sdpType, sdp, sessionId,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendIceCandidateServerRpc(ulong targetClientId, string candidate, string sdpMid, int sdpMLineIndex,
            string sessionId, ServerRpcParams serverRpcParams = default)
        {
            ulong senderClientId = serverRpcParams.Receive.SenderClientId;
            SendIceCandidateClientRpc(senderClientId, candidate, sdpMid, sdpMLineIndex, sessionId,
                new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { targetClientId } } });
        }

        #endregion

        #region Client RPCs

        [ClientRpc]
        private void SendOfferClientRpc(ulong fromClientId, string sdpType, string sdp, string sessionId, ClientRpcParams clientRpcParams = default)
        {
            // Don't process our own messages
            if (fromClientId == NetworkManager.Singleton.LocalClientId) return;

            Debug.Log($"[WebRTCVoiceSignaling] Received offer from client {fromClientId}, session: {sessionId}");
            OnOfferReceived?.Invoke(fromClientId, sdpType, sdp, sessionId);
        }

        [ClientRpc]
        private void SendAnswerClientRpc(ulong fromClientId, string sdpType, string sdp, string sessionId, ClientRpcParams clientRpcParams = default)
        {
            if (fromClientId == NetworkManager.Singleton.LocalClientId) return;

            Debug.Log($"[WebRTCVoiceSignaling] Received answer from client {fromClientId}, session: {sessionId}");
            OnAnswerReceived?.Invoke(fromClientId, sdpType, sdp, sessionId);
        }

        [ClientRpc]
        private void SendIceCandidateClientRpc(ulong fromClientId, string candidate, string sdpMid, int sdpMLineIndex,
            string sessionId, ClientRpcParams clientRpcParams = default)
        {
            if (fromClientId == NetworkManager.Singleton.LocalClientId) return;

            Debug.Log($"[WebRTCVoiceSignaling] Received ICE candidate from client {fromClientId}, session: {sessionId}");
            OnIceCandidateReceived?.Invoke(fromClientId, candidate, sdpMid, sdpMLineIndex, sessionId);
        }

        #endregion
    }
}