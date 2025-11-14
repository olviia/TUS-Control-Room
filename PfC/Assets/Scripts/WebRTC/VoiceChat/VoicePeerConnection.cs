using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace TUS.WebRTC.VoiceChat
{
    /// <summary>
    /// Manages a single WebRTC peer connection for voice communication
    /// </summary>
    public class VoicePeerConnection : IDisposable
    {
        public ulong RemoteClientId { get; private set; }
        public string SessionId { get; private set; }
        public RTCPeerConnection PeerConnection { get; private set; }
        public bool IsConnected => PeerConnection != null && PeerConnection.ConnectionState == RTCPeerConnectionState.Connected;

        private AudioStreamTrack _localAudioTrack;
        private GameObject _audioReceiverObject;
        private List<RTCRtpSender> _senders = new List<RTCRtpSender>();
        private Queue<RTCIceCandidate> _pendingIceCandidates = new Queue<RTCIceCandidate>();
        private bool _remoteDescriptionSet = false;

        public event Action<VoicePeerConnection> OnConnectionStateChanged;
        public event Action<AudioStreamTrack> OnRemoteAudioTrackAdded;

        public VoicePeerConnection(ulong remoteClientId, AudioStreamTrack localAudioTrack = null)
        {
            RemoteClientId = remoteClientId;
            SessionId = Guid.NewGuid().ToString();
            _localAudioTrack = localAudioTrack;

            InitializePeerConnection();
        }

        private void InitializePeerConnection()
        {
            var config = new RTCConfiguration
            {
                iceServers = new RTCIceServer[]
                {
                    new RTCIceServer { urls = new string[] { "stun:stun.l.google.com:19302" } },
                    new RTCIceServer { urls = new string[] { "stun:stun1.l.google.com:19302" } }
                }
            };

            PeerConnection = new RTCPeerConnection(ref config);

            // Setup event handlers
            PeerConnection.OnIceCandidate = OnIceCandidate;
            PeerConnection.OnIceConnectionChange = OnIceConnectionChange;
            PeerConnection.OnConnectionStateChange = OnConnectionStateChange;
            PeerConnection.OnTrack = OnTrack;

            // Add local audio track if provided (send audio)
            if (_localAudioTrack != null)
            {
                var sender = PeerConnection.AddTrack(_localAudioTrack);
                _senders.Add(sender);
                Debug.Log($"[VoicePeerConnection] Added local audio track to peer {RemoteClientId}");
            }

            Debug.Log($"[VoicePeerConnection] Initialized peer connection for client {RemoteClientId}, session: {SessionId}");
        }

        #region Offer/Answer Handling

        public IEnumerator CreateOffer()
        {
            var offerOp = PeerConnection.CreateOffer();
            yield return offerOp;

            if (offerOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to create offer: {offerOp.Error.message}");
                yield break;
            }

            var offer = offerOp.Desc;
            var setLocalDescOp = PeerConnection.SetLocalDescription(ref offer);
            yield return setLocalDescOp;

            if (setLocalDescOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to set local description: {setLocalDescOp.Error.message}");
                yield break;
            }

            Debug.Log($"[VoicePeerConnection] Created and set offer for client {RemoteClientId}");

            // Send offer through signaling
            WebRTCVoiceSignaling.Instance?.SendOffer(RemoteClientId, offer.type.ToString(), offer.sdp, SessionId);
        }

        public IEnumerator HandleOffer(string sdpType, string sdp)
        {
            RTCSessionDescription offer = new RTCSessionDescription
            {
                type = RTCSdpType.Offer,
                sdp = sdp
            };

            var setRemoteDescOp = PeerConnection.SetRemoteDescription(ref offer);
            yield return setRemoteDescOp;

            if (setRemoteDescOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to set remote description: {setRemoteDescOp.Error.message}");
                yield break;
            }

            _remoteDescriptionSet = true;
            ProcessPendingIceCandidates();

            Debug.Log($"[VoicePeerConnection] Set remote offer from client {RemoteClientId}");

            // Create answer
            var answerOp = PeerConnection.CreateAnswer();
            yield return answerOp;

            if (answerOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to create answer: {answerOp.Error.message}");
                yield break;
            }

            var answer = answerOp.Desc;
            var setLocalDescOp = PeerConnection.SetLocalDescription(ref answer);
            yield return setLocalDescOp;

            if (setLocalDescOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to set local description: {setLocalDescOp.Error.message}");
                yield break;
            }

            Debug.Log($"[VoicePeerConnection] Created and set answer for client {RemoteClientId}");

            // Send answer through signaling
            WebRTCVoiceSignaling.Instance?.SendAnswer(RemoteClientId, answer.type.ToString(), answer.sdp, SessionId);
        }

        public IEnumerator HandleAnswer(string sdpType, string sdp)
        {
            RTCSessionDescription answer = new RTCSessionDescription
            {
                type = RTCSdpType.Answer,
                sdp = sdp
            };

            var setRemoteDescOp = PeerConnection.SetRemoteDescription(ref answer);
            yield return setRemoteDescOp;

            if (setRemoteDescOp.IsError)
            {
                Debug.LogError($"[VoicePeerConnection] Failed to set remote description: {setRemoteDescOp.Error.message}");
                yield break;
            }

            _remoteDescriptionSet = true;
            ProcessPendingIceCandidates();

            Debug.Log($"[VoicePeerConnection] Set remote answer from client {RemoteClientId}");
        }

        #endregion

        #region ICE Handling

        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            Debug.Log($"[VoicePeerConnection] ICE candidate generated for client {RemoteClientId}");
            WebRTCVoiceSignaling.Instance?.SendIceCandidate(
                RemoteClientId,
                candidate.Candidate,
                candidate.SdpMid,
                candidate.SdpMLineIndex ?? 0,
                SessionId
            );
        }

        public void AddIceCandidate(string candidate, string sdpMid, int sdpMLineIndex)
        {
            RTCIceCandidateInit candidateInit = new RTCIceCandidateInit
            {
                candidate = candidate,
                sdpMid = sdpMid,
                sdpMLineIndex = sdpMLineIndex
            };

            RTCIceCandidate iceCandidate = new RTCIceCandidate(candidateInit);

            // If remote description isn't set yet, queue the candidate
            if (!_remoteDescriptionSet)
            {
                _pendingIceCandidates.Enqueue(iceCandidate);
                Debug.Log($"[VoicePeerConnection] Queued ICE candidate from client {RemoteClientId} (remote description not set)");
                return;
            }

            if (!PeerConnection.AddIceCandidate(iceCandidate))
            {
                Debug.LogWarning($"[VoicePeerConnection] Failed to add ICE candidate from client {RemoteClientId}");
            }
            else
            {
                Debug.Log($"[VoicePeerConnection] Added ICE candidate from client {RemoteClientId}");
            }
        }

        private void ProcessPendingIceCandidates()
        {
            Debug.Log($"[VoicePeerConnection] Processing {_pendingIceCandidates.Count} pending ICE candidates");
            while (_pendingIceCandidates.Count > 0)
            {
                RTCIceCandidate candidate = _pendingIceCandidates.Dequeue();
                if (!PeerConnection.AddIceCandidate(candidate))
                {
                    Debug.LogWarning($"[VoicePeerConnection] Failed to add pending ICE candidate");
                }
            }
        }

        #endregion

        #region Event Handlers

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            Debug.Log($"[VoicePeerConnection] ICE connection state changed to {state} for client {RemoteClientId}");
        }

        private void OnConnectionStateChange(RTCPeerConnectionState state)
        {
            Debug.Log($"[VoicePeerConnection] Connection state changed to {state} for client {RemoteClientId}");
            OnConnectionStateChanged?.Invoke(this);
        }

        private void OnTrack(RTCTrackEvent trackEvent)
        {
            Debug.Log($"[VoicePeerConnection] Received track from client {RemoteClientId}, kind: {trackEvent.Track.Kind}");

            if (trackEvent.Track is AudioStreamTrack audioTrack)
            {
                Debug.Log($"[VoicePeerConnection] Audio track - Enabled: {audioTrack.Enabled}, ReadyState: {audioTrack.ReadyState}");

                // Create audio receiver object
                _audioReceiverObject = new GameObject($"VoiceAudioReceiver_Client{RemoteClientId}");
                var audioSource = _audioReceiverObject.AddComponent<AudioSource>();

                // Configure audio source
                audioSource.playOnAwake = false;
                audioSource.loop = true;
                audioSource.volume = 1.0f;
                audioSource.spatialBlend = 0f; // 2D audio

                // Set the WebRTC audio track
                audioSource.SetTrack(audioTrack);

                // Start playback
                audioSource.Play();

                Debug.Log($"[VoicePeerConnection] AudioSource created - Playing: {audioSource.isPlaying}, Volume: {audioSource.volume}");
                OnRemoteAudioTrackAdded?.Invoke(audioTrack);
            }
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            Debug.Log($"[VoicePeerConnection] Disposing peer connection for client {RemoteClientId}");

            if (_audioReceiverObject != null)
            {
                UnityEngine.Object.Destroy(_audioReceiverObject);
                _audioReceiverObject = null;
            }

            if (PeerConnection != null)
            {
                PeerConnection.Close();
                PeerConnection.Dispose();
                PeerConnection = null;
            }

            _senders.Clear();
            _pendingIceCandidates.Clear();
        }

        #endregion
    }
}
