using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace TUS.WebRTC.VoiceChat
{
    /// <summary>
    /// Manages WebRTC voice chat with role-based communication rules
    ///
    /// Communication Rules:
    /// - Directors can communicate with other Directors and Presenters (bidirectional)
    /// - Presenters can communicate with Directors (bidirectional) and send to Audience (one-way)
    /// - Audience can hear Presenters and communicate with other Audience members
    /// - Audience cannot hear Directors, Directors cannot hear Audience
    /// </summary>
    public class WebRTCVoiceChatManager : NetworkBehaviour
    {
        public static WebRTCVoiceChatManager Instance { get; private set; }

        [Header("Components")]
        [SerializeField] private MicrophoneAudioCapture microphoneCapture;

        [Header("Audio Settings")]
        [SerializeField] private bool autoJoinOnStart = true;
        [SerializeField] private float reconnectDelay = 2f;

        public Role MyRole { get; private set; }
        public bool IsInVoiceChat { get; private set; }

        // Peer connections: key is remote client ID
        private Dictionary<ulong, VoicePeerConnection> _peerConnections = new Dictionary<ulong, VoicePeerConnection>();
        private HashSet<ulong> _pendingConnections = new HashSet<ulong>();

        private WebRTCVoiceSignaling _signaling;
        private Coroutine _webRTCUpdateCoroutine;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // Create microphone capture if not assigned
            if (microphoneCapture == null)
            {
                GameObject micObj = new GameObject("MicrophoneCapture");
                micObj.transform.SetParent(transform);
                microphoneCapture = micObj.AddComponent<MicrophoneAudioCapture>();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Get role from RoleManager
            if (RoleManager.Instance != null)
            {
                MyRole = RoleManager.Instance.currentRole;
                Debug.Log($"[WebRTCVoiceChatManager] My role: {MyRole}");
            }
            else
            {
                Debug.LogError("[WebRTCVoiceChatManager] RoleManager not found");
                return;
            }

            // Find or create signaling component
            _signaling = WebRTCVoiceSignaling.Instance;
            if (_signaling == null)
            {
                GameObject signalingObj = new GameObject("WebRTCVoiceSignaling");
                var networkObj = signalingObj.AddComponent<NetworkObject>();
                _signaling = signalingObj.AddComponent<WebRTCVoiceSignaling>();
                networkObj.Spawn();
            }

            // Subscribe to signaling events
            _signaling.OnOfferReceived += HandleOfferReceived;
            _signaling.OnAnswerReceived += HandleAnswerReceived;
            _signaling.OnIceCandidateReceived += HandleIceCandidateReceived;

            // Subscribe to role registry changes
            if (NetworkRoleRegistry.Instance != null)
            {
                NetworkRoleRegistry.Instance.UserRoles.OnListChanged += OnUserRolesChanged;
            }

            // Start WebRTC update coroutine
            if (WebRTCEngineManager.Instance != null)
            {
                WebRTCEngineManager.Instance.RegisterStreamer();
            }
            else
            {
                // Fallback if WebRTCEngineManager not present
                _webRTCUpdateCoroutine = StartCoroutine(WebRTCUpdateCoroutine());
            }

            if (autoJoinOnStart)
            {
                JoinVoiceChat();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            LeaveVoiceChat();

            if (_signaling != null)
            {
                _signaling.OnOfferReceived -= HandleOfferReceived;
                _signaling.OnAnswerReceived -= HandleAnswerReceived;
                _signaling.OnIceCandidateReceived -= HandleIceCandidateReceived;
            }

            if (NetworkRoleRegistry.Instance != null)
            {
                NetworkRoleRegistry.Instance.UserRoles.OnListChanged -= OnUserRolesChanged;
            }

            if (WebRTCEngineManager.Instance != null)
            {
                WebRTCEngineManager.Instance.UnregisterStreamer();
            }

            if (_webRTCUpdateCoroutine != null)
            {
                StopCoroutine(_webRTCUpdateCoroutine);
                _webRTCUpdateCoroutine = null;
            }
        }

        #region Public Methods

        /// <summary>
        /// Join voice chat and establish connections based on role
        /// </summary>
        public void JoinVoiceChat()
        {
            if (IsInVoiceChat)
            {
                Debug.LogWarning("[WebRTCVoiceChatManager] Already in voice chat");
                return;
            }

            Debug.Log($"[WebRTCVoiceChatManager] Joining voice chat as {MyRole}");

            // Start microphone capture
            if (!microphoneCapture.IsCapturing)
            {
                microphoneCapture.StartCapture();
            }

            IsInVoiceChat = true;

            // Establish connections with appropriate peers based on role
            EstablishPeerConnections();
        }

        /// <summary>
        /// Leave voice chat and close all connections
        /// </summary>
        public void LeaveVoiceChat()
        {
            if (!IsInVoiceChat)
            {
                return;
            }

            Debug.Log("[WebRTCVoiceChatManager] Leaving voice chat");

            // Stop microphone
            if (microphoneCapture != null && microphoneCapture.IsCapturing)
            {
                microphoneCapture.StopCapture();
            }

            // Close all peer connections
            foreach (var kvp in _peerConnections)
            {
                kvp.Value.Dispose();
            }
            _peerConnections.Clear();
            _pendingConnections.Clear();

            IsInVoiceChat = false;
        }

        /// <summary>
        /// Mute/unmute local microphone
        /// </summary>
        public void SetMicrophoneMuted(bool muted)
        {
            if (muted)
            {
                microphoneCapture?.StopCapture();
            }
            else if (IsInVoiceChat && !microphoneCapture.IsCapturing)
            {
                microphoneCapture?.StartCapture();
            }
        }

        #endregion

        #region Connection Management

        private void EstablishPeerConnections()
        {
            if (NetworkRoleRegistry.Instance == null)
            {
                Debug.LogError("[WebRTCVoiceChatManager] NetworkRoleRegistry is null");
                return;
            }

            var myClientId = NetworkManager.Singleton.LocalClientId;
            var allUsers = NetworkRoleRegistry.Instance.UserRoles;

            foreach (var userRole in allUsers)
            {
                ulong clientId = ulong.Parse(userRole.playerId.ToString());

                // Skip self
                if (clientId == myClientId)
                    continue;

                // Check if we should connect based on roles
                if (ShouldConnectToPeer(MyRole, userRole.role, out bool sendAudio, out bool receiveAudio))
                {
                    CreatePeerConnection(clientId, sendAudio, receiveAudio);
                }
            }
        }

        /// <summary>
        /// Determine if we should connect to a peer based on role rules
        /// </summary>
        private bool ShouldConnectToPeer(Role myRole, Role theirRole, out bool sendAudio, out bool receiveAudio)
        {
            sendAudio = false;
            receiveAudio = false;

            switch (myRole)
            {
                case Role.Director:
                    if (theirRole == Role.Director || theirRole == Role.Presenter)
                    {
                        sendAudio = true;
                        receiveAudio = true;
                        return true;
                    }
                    // Directors don't connect to Audience
                    return false;

                case Role.Presenter:
                    if (theirRole == Role.Director)
                    {
                        // Bidirectional with Directors
                        sendAudio = true;
                        receiveAudio = true;
                        return true;
                    }
                    else if (theirRole == Role.Audience)
                    {
                        // Send-only to Audience
                        sendAudio = true;
                        receiveAudio = false;
                        return true;
                    }
                    else if (theirRole == Role.Presenter)
                    {
                        // Presenters can communicate with each other
                        sendAudio = true;
                        receiveAudio = true;
                        return true;
                    }
                    return false;

                case Role.Audience:
                    if (theirRole == Role.Presenter)
                    {
                        // Receive-only from Presenters
                        sendAudio = false;
                        receiveAudio = true;
                        return true;
                    }
                    else if (theirRole == Role.Audience)
                    {
                        // Bidirectional with other Audience
                        sendAudio = true;
                        receiveAudio = true;
                        return true;
                    }
                    // Audience don't connect to Directors
                    return false;

                default:
                    return false;
            }
        }

        private void CreatePeerConnection(ulong remoteClientId, bool sendAudio, bool receiveAudio)
        {
            // Don't create duplicate connections
            if (_peerConnections.ContainsKey(remoteClientId) || _pendingConnections.Contains(remoteClientId))
            {
                return;
            }

            _pendingConnections.Add(remoteClientId);

            // Only include our audio track if we should send audio
            var audioTrack = sendAudio ? microphoneCapture.AudioTrack : null;
            var peerConnection = new VoicePeerConnection(remoteClientId, audioTrack);

            peerConnection.OnConnectionStateChanged += HandleConnectionStateChanged;
            _peerConnections[remoteClientId] = peerConnection;
            _pendingConnections.Remove(remoteClientId);

            // Lower client ID initiates the connection (to avoid both sides creating offers)
            if (NetworkManager.Singleton.LocalClientId < remoteClientId)
            {
                Debug.Log($"[WebRTCVoiceChatManager] Initiating connection to client {remoteClientId} (send: {sendAudio}, receive: {receiveAudio})");
                StartCoroutine(peerConnection.CreateOffer());
            }
            else
            {
                Debug.Log($"[WebRTCVoiceChatManager] Waiting for offer from client {remoteClientId} (send: {sendAudio}, receive: {receiveAudio})");
            }
        }

        private void RemovePeerConnection(ulong remoteClientId)
        {
            if (_peerConnections.TryGetValue(remoteClientId, out var peerConnection))
            {
                peerConnection.Dispose();
                _peerConnections.Remove(remoteClientId);
                Debug.Log($"[WebRTCVoiceChatManager] Removed peer connection for client {remoteClientId}");
            }
        }

        #endregion

        #region Signaling Event Handlers

        private void HandleOfferReceived(ulong fromClientId, string sdpType, string sdp, string sessionId)
        {
            Debug.Log($"[WebRTCVoiceChatManager] Received offer from client {fromClientId}");

            // Check if we should accept this connection
            if (NetworkRoleRegistry.Instance == null)
                return;

            var theirRole = NetworkRoleRegistry.Instance.GetUserRole(fromClientId.ToString());
            if (!theirRole.HasValue)
            {
                Debug.LogWarning($"[WebRTCVoiceChatManager] Could not find role for client {fromClientId}");
                return;
            }

            if (!ShouldConnectToPeer(MyRole, theirRole.Value.role, out bool sendAudio, out bool receiveAudio))
            {
                Debug.Log($"[WebRTCVoiceChatManager] Rejecting connection from client {fromClientId} based on role rules");
                return;
            }

            // Create peer connection if it doesn't exist
            if (!_peerConnections.ContainsKey(fromClientId))
            {
                var audioTrack = sendAudio ? microphoneCapture.AudioTrack : null;
                var peerConnection = new VoicePeerConnection(fromClientId, audioTrack);
                peerConnection.OnConnectionStateChanged += HandleConnectionStateChanged;
                _peerConnections[fromClientId] = peerConnection;
            }

            // Handle the offer
            StartCoroutine(_peerConnections[fromClientId].HandleOffer(sdpType, sdp));
        }

        private void HandleAnswerReceived(ulong fromClientId, string sdpType, string sdp, string sessionId)
        {
            Debug.Log($"[WebRTCVoiceChatManager] Received answer from client {fromClientId}");

            if (_peerConnections.TryGetValue(fromClientId, out var peerConnection))
            {
                StartCoroutine(peerConnection.HandleAnswer(sdpType, sdp));
            }
            else
            {
                Debug.LogWarning($"[WebRTCVoiceChatManager] Received answer from unknown client {fromClientId}");
            }
        }

        private void HandleIceCandidateReceived(ulong fromClientId, string candidate, string sdpMid, int sdpMLineIndex, string sessionId)
        {
            if (_peerConnections.TryGetValue(fromClientId, out var peerConnection))
            {
                peerConnection.AddIceCandidate(candidate, sdpMid, sdpMLineIndex);
            }
            else
            {
                Debug.LogWarning($"[WebRTCVoiceChatManager] Received ICE candidate from unknown client {fromClientId}");
            }
        }

        private void HandleConnectionStateChanged(VoicePeerConnection peerConnection)
        {
            Debug.Log($"[WebRTCVoiceChatManager] Connection state changed for client {peerConnection.RemoteClientId}: {peerConnection.PeerConnection?.ConnectionState}");

            // Handle reconnection if needed
            if (peerConnection.PeerConnection?.ConnectionState == Unity.WebRTC.RTCPeerConnectionState.Failed ||
                peerConnection.PeerConnection?.ConnectionState == Unity.WebRTC.RTCPeerConnectionState.Disconnected)
            {
                StartCoroutine(ReconnectToPeer(peerConnection.RemoteClientId));
            }
        }

        #endregion

        #region User Role Changes

        private void OnUserRolesChanged(NetworkListEvent<VivoxUserRole> changeEvent)
        {
            if (!IsInVoiceChat)
                return;

            // Re-establish connections when user roles change
            Debug.Log("[WebRTCVoiceChatManager] User roles changed, re-evaluating connections");

            // Get current client IDs we should be connected to
            var myClientId = NetworkManager.Singleton.LocalClientId;
            var allUsers = NetworkRoleRegistry.Instance.UserRoles;
            HashSet<ulong> shouldBeConnectedTo = new HashSet<ulong>();

            foreach (var userRole in allUsers)
            {
                ulong clientId = ulong.Parse(userRole.playerId.ToString());
                if (clientId == myClientId)
                    continue;

                if (ShouldConnectToPeer(MyRole, userRole.role, out _, out _))
                {
                    shouldBeConnectedTo.Add(clientId);
                }
            }

            // Remove connections that shouldn't exist
            var toRemove = new List<ulong>();
            foreach (var clientId in _peerConnections.Keys)
            {
                if (!shouldBeConnectedTo.Contains(clientId))
                {
                    toRemove.Add(clientId);
                }
            }

            foreach (var clientId in toRemove)
            {
                RemovePeerConnection(clientId);
            }

            // Add new connections
            foreach (var clientId in shouldBeConnectedTo)
            {
                if (!_peerConnections.ContainsKey(clientId))
                {
                    var userRole = NetworkRoleRegistry.Instance.GetUserRole(clientId.ToString());
                    if (userRole.HasValue)
                    {
                        ShouldConnectToPeer(MyRole, userRole.Value.role, out bool sendAudio, out bool receiveAudio);
                        CreatePeerConnection(clientId, sendAudio, receiveAudio);
                    }
                }
            }
        }

        #endregion

        #region Reconnection

        private IEnumerator ReconnectToPeer(ulong remoteClientId)
        {
            Debug.Log($"[WebRTCVoiceChatManager] Reconnecting to client {remoteClientId} in {reconnectDelay} seconds");

            yield return new WaitForSeconds(reconnectDelay);

            // Remove old connection
            RemovePeerConnection(remoteClientId);

            // Check if we should still be connected
            var theirRole = NetworkRoleRegistry.Instance?.GetUserRole(remoteClientId.ToString());
            if (theirRole.HasValue && ShouldConnectToPeer(MyRole, theirRole.Value.role, out bool sendAudio, out bool receiveAudio))
            {
                CreatePeerConnection(remoteClientId, sendAudio, receiveAudio);
            }
        }

        #endregion

        #region WebRTC Update

        private IEnumerator WebRTCUpdateCoroutine()
        {
            while (true)
            {
                Unity.WebRTC.WebRTC.Update();
                yield return null;
            }
        }

        #endregion
    }
}
