using System;
using Klak.Ndi;
using UnityEngine;

public class AddBridgeToAudioChild : MonoBehaviour
{
    public NdiReceiver ndiReceiver;

    [Header("Audio Bridge Settings")]
    [Tooltip("ID to be assigned to AudioListenerIndividualBridge components")]
    public int audioBridgeId = 0;

    private void Update()
    {
        // Check all AudioSource children for missing AudioListenerIndividualBridge
        AudioSource[] audioSources = ndiReceiver.GetComponentsInChildren<AudioSource>();
        foreach (AudioSource audioSource in audioSources)
        {
            AudioListenerIndividualBridge bridge = audioSource.GetComponent<AudioListenerIndividualBridge>();
            if (bridge == null)
            {
                bridge = audioSource.gameObject.AddComponent<AudioListenerIndividualBridge>();
                bridge.SetBridgeId(audioBridgeId);
                Debug.Log($"[AddBridgeToAudioChild] Added AudioListenerIndividualBridge with ID {audioBridgeId} to {audioSource.gameObject.name}");
            }
        }
    }
}
