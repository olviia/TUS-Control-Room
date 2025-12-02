using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;

/// <summary>
/// NDI Audio Interceptor that creates a mirror AudioSource for smooth WebRTC streaming
/// Mirrors AudioSourceBridge's audio without modifying the original component
/// </summary>
[RequireComponent(typeof(NdiReceiver))]
public class NdiAudioInterceptor : MonoBehaviour
{
    
    [Header("Component References")]
    [SerializeField] private AudioSourceBridge targetAudioSourceBridge;
    
    [Header("Buffering Settings")]
    [SerializeField] private float audioPollingRate = 1000f; // Hz - high frequency NDI data pulling
    [SerializeField] private int bufferSizeMs = 100; // Buffer size in milliseconds

    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    public AudioStreamTrack audioStreamTrack;
    
    
    // State management
    private bool isStreamingActive = false;
    private bool isInitialized = false;
    private Coroutine ndiPullingCoroutine;
    
    #region Unity Lifecycle
    
    void Awake()
    {
        ndiReceiver = GetComponent<NdiReceiver>();
    }
    
    void Start()
    {
        if (ndiReceiver != null)
        {
            Debug.Log($"[🎵AudioInterceptor] Connected to NDI receiver: {ndiReceiver.ndiName}");
        }
        audioStreamTrack = new AudioStreamTrack();

        StartAudioStreaming();
        StopAudioStreaming();
    }

    void Update()
    {
        // Get accumulated audio from ring buffer and send to WebRTC (similar to NdiSender)
        if (isStreamingActive && targetAudioSourceBridge != null)
        {
            if (targetAudioSourceBridge.GetAccumulatedAudio(out float[] audioData, out int channels))
            {
                // Send to WebRTC audioStreamTrack
                try
                {
                    audioStreamTrack.SetData(audioData, channels, AudioSettings.outputSampleRate);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[🎵AudioInterceptor] ❌ SetData FAILED: {e.Message}");
                }
            }
        }
    }
    
    #endregion
    

    #region Audio Processing Utilities

    // Note: HandleChunk is no longer used - audio is now accumulated in Update via ring buffer
    // This provides better timing and prevents audio crackling issues

    private float CalculateRMS(float[] audioData)
    {
        float sum = 0f;
        for (int i = 0; i < audioData.Length; i++)
        {
            sum += audioData[i] * audioData[i];
        }
        return Mathf.Sqrt(sum / audioData.Length);
    }

    #endregion
    
    #region Public Interface
    
    public void StartAudioStreaming()
    {
        targetAudioSourceBridge = gameObject.GetComponentInChildren<AudioSourceBridge>();

        if (targetAudioSourceBridge == null)
        {
            Debug.LogError("[🎵AudioInterceptor] No AudioSourceBridge found!");
            return;
        }

        isStreamingActive = true;
        Debug.Log("[🎵AudioInterceptor] Audio streaming started - using ring buffer accumulation");
    }

    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    #endregion
    

}

