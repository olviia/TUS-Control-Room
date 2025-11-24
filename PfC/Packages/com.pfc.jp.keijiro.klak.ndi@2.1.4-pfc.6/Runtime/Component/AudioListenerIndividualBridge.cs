using UnityEngine;

namespace Klak.Ndi
{
    /// <summary>
    /// Individual audio bridge for each AudioSource in ObjectBased mode.
    /// Inherits all ring buffer functionality from AudioListenerBridge.
    /// Registers itself with NdiSender for simplified object-based mixing.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioListenerIndividualBridge : AudioListenerBridge
    {
        [SerializeField] private int _bridgeId = 0;
        public int BridgeId => _bridgeId;

        private AudioSource _audioSource;
        private bool _isRegistered = false;

        public void SetBridgeId(int id)
        {
            _bridgeId = id;
        }

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (!_isRegistered)
            {
                NdiSender.RegisterIndividualAudioBridge(this);
                _isRegistered = true;
            }
        }

        private void OnDisable()
        {
            if (_isRegistered)
            {
                NdiSender.UnregisterIndividualAudioBridge(this);
                _isRegistered = false;
            }
        }

        private void OnDestroy()
        {
            if (_isRegistered)
            {
                NdiSender.UnregisterIndividualAudioBridge(this);
                _isRegistered = false;
            }
        }
    }
}
