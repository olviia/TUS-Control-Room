using System;
using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// NDI Audio Interceptor that creates a mirror AudioSource for smooth WebRTC streaming
/// Mirrors AudioSourceBridge's audio without modifying the original component
/// </summary>
[RequireComponent(typeof(NdiReceiver))]
public class NdiAudioInterceptor : MonoBehaviour
{
    
    [Header("Component References")]
    [SerializeField] private AudioSourceBridge targetAudioSourceBridge;
    
    private readonly Queue<float[]> audioBuffer = new Queue<float[]>();
    private readonly object bufferLock = new object();
    
    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    public AudioStreamTrack audioStreamTrack;
    
    
    // State management
    private bool isStreamingActive = false;
    private bool isInitialized = false;
    
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
        if (!isStreamingActive) return;

        lock (bufferLock)
        {
            audioBuffer.Enqueue(audioData);

            // Prevent buffer overflow
            while (audioBuffer.Count > 5)
            {
                audioBuffer.Dequeue();
            }
        }
    }
    // Called by Unity audio thread at consistent intervals
    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isStreamingActive || audioStreamTrack == null) return;
        
        lock (bufferLock)
        {
            if (audioBuffer.Count > 0)
            {
                var bufferedChunk = audioBuffer.Dequeue();
                
                audioStreamTrack.SetData(bufferedChunk, channels, 48000);
            }
        }
        
        // Clear the filter data 
        Array.Clear(data, 0, data.Length);
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
        audioStreamTrack = new AudioStreamTrack();
        
        targetAudioSourceBridge = gameObject.GetComponentInChildren<AudioSourceBridge>();

        targetAudioSourceBridge.OnWebRTCAudioReady += HandleChunk;
        
        isStreamingActive = true;

    }
    
    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        
        targetAudioSourceBridge.OnWebRTCAudioReady -= HandleChunk;
        lock (bufferLock)
        {
            audioBuffer.Clear();
        }
        
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    #endregion
    

}

