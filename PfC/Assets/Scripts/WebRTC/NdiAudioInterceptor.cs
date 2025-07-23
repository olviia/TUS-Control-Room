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
    }
    
    void Update()
    {
        // Dynamic AudioSourceBridge detection for runtime on/off capability
        // if (targetAudioSourceBridge == null)
        // {
        //     targetAudioSourceBridge.OnWebRTCAudioReady += HandleChunk;
        // }
    }
    
    #endregion
    

    #region Audio Processing Utilities
    
    private void HandleChunk(float[] audioData, int channels, int sampleRate)
    {
        // Direct chunk forwarding - WebRTC handles the smoothness!
        audioStreamTrack.SetData(audioData, channels, sampleRate);
        
        float rms = CalculateRMS(audioData);

            Debug.Log($"[AudioSourceBridge] NDI Audio: RMS={rms:F3}, Channels={channels}");
         
    }
    
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

        audioStreamTrack = new AudioStreamTrack();
        
        targetAudioSourceBridge.OnWebRTCAudioReady += HandleChunk;
        
        isStreamingActive = true;

    }
    
    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        
        targetAudioSourceBridge.OnWebRTCAudioReady -= HandleChunk;
        
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    #endregion
    

}

