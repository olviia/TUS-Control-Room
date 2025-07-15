using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BroadcastPipeline;
using UnityEngine;

using UnityEngine.UI;

public class AudioSwitcherOnLive : MonoBehaviour
{
    [SerializeField] private NdiReceiver ndiReceiver;
    [SerializeField] private WebRTCRenderer incomingAudioWebRtcRenderer;

    
    // Call this method from your UI button
    public void ToggleAudioMode()
    {
        if (incomingAudioWebRtcRenderer != null && incomingAudioWebRtcRenderer.isShowingRemoteStream)
        {
            float newVolume = incomingAudioWebRtcRenderer.AudioVolume > 0 ? 0f : 1f;
            incomingAudioWebRtcRenderer.AudioVolume = newVolume;

        }
        else if (ndiReceiver != null)
        {
            // change ndi volume
            float newVolume = GetNdiAudioVolume() > 0 ? 0f : 1f;
            ControlNdiAudioVolume(newVolume);
        }
    }

    #region NDI Control

    private void Start()
    {
        WebRTCStreamer.OnStateChanged += CheckAndTurnOnNdi;
    }

    private void CheckAndTurnOnNdi(PipelineType pipeline, StreamerState state, string sessionId)
    {
        if (pipeline == PipelineType.StudioLive || PipelineType.TVLive == pipeline)
        {
            SetReceiveAudioField(true);
            SetCreateVirtualSpeakersField(true);
            ControlNdiAudioVolume(0f);
        }
    }
    
    private float ControlNdiAudioVolume(float volume)
    {
        if (ndiReceiver != null)
        {
            var audioSource = ndiReceiver.GetComponentInChildren<AudioSource>();
            if (audioSource != null)
            {
                audioSource.volume = volume;
            }
        }
        return volume;
    }
    private float GetNdiAudioVolume()
    {
        if (ndiReceiver != null)
        {
            var audioSource = ndiReceiver.GetComponentInChildren<AudioSource>();
            if (audioSource != null)
            {
                
                return audioSource.volume;
            }
        }

        return -100f;
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