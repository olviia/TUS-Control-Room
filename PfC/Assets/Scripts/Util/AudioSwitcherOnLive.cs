using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BroadcastPipeline;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class AudioSwitcherOnLive : MonoBehaviour
{
    [SerializeField] private NdiReceiver ndiReceiver;
    [SerializeField] private WebRTCRenderer incomingAudioWebRtcRenderer;
    [SerializeField] private bool audioIsPlaying;

    private Coroutine ndiTurnOnCoroutine;

    
    // Call this method from your UI button
    public void ToggleAudioMode()
    {
        if (incomingAudioWebRtcRenderer != null && incomingAudioWebRtcRenderer.isShowingRemoteStream)
        {
           // float newVolume = incomingAudioWebRtcRenderer.AudioVolume > 0 ? 0f : 1f;
           // incomingAudioWebRtcRenderer.AudioVolume = newVolume;

        }
        else if (ndiReceiver != null)
        {        
           // NDIAudioInterceptor ndiAudioInterceptor = ndiReceiver.GetComponent<NDIAudioInterceptor>();

            // change ndi volume
            if (!audioIsPlaying)
            {
                TurnOnNdi(true);
            }
            else
            {
                TurnOnNdi(false);
            }
            audioIsPlaying = !audioIsPlaying;
        }
    }

    #region NDI Control

    private void Start()
    {
        
    }



    private  void TurnOnNdi(bool activate)
    {
            SetReceiveAudioField(activate);
            SetCreateVirtualSpeakersField(activate);
    }

    // Helper method to set the _createVirtualSpeakers field via reflection
    // (since it's not exposed as a public property)
    private void SetCreateVirtualSpeakersField(bool value)
    {
        var field = typeof(NdiReceiver).GetField("_createVirtualSpeakers",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
            field.SetValue(ndiReceiver, value);
    }

    private void SetReceiveAudioField(bool value)
    {
        var field = typeof(NdiReceiver).GetField("_receiveAudio",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
            field.SetValue(ndiReceiver, value);
        else
            Debug.LogError("Could not find _receiveAudio field in NdiReceiver");
    }
#endregion
}