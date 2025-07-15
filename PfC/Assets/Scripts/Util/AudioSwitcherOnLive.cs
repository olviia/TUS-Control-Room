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

    private Coroutine ndiTurnOnCoroutine;

    
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
            float newVolume = ndiReceiver.GetComponentInChildren<AudioSource>().volume > 0 ? 0f : 1f;
            ndiReceiver.GetComponentInChildren<AudioSource>().volume = newVolume;
        }
    }

    #region NDI Control

    private void Start()
    {
        WebRTCStreamer.OnStateChanged += CheckAndTurnOnNdi;
    }

    private void CheckAndTurnOnNdi(PipelineType pipeline, StreamerState state, string sessionId)
    {
        ndiTurnOnCoroutine= StartCoroutine(NdiTurnOn(pipeline));

    }

    private IEnumerator NdiTurnOn(PipelineType pipeline)
    {
        if (ndiReceiver == null) yield return new WaitForEndOfFrame();
        
        if (pipeline == PipelineType.StudioLive || PipelineType.TVLive == pipeline)
        {
            SetReceiveAudioField(true);
            SetCreateVirtualSpeakersField(true);
            ndiReceiver.GetComponentInChildren<AudioSource>().volume = 0f;
            
            WebRTCStreamer.OnStateChanged -= CheckAndTurnOnNdi;
        }
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