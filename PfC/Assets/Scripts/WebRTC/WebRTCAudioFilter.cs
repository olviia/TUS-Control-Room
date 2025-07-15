using UnityEngine;
using Unity.WebRTC;
using BroadcastPipeline;
using System;

/// <summary>
/// Component that uses OnAudioFilterRead to inject WebRTC audio into Unity's audio pipeline
/// This bypasses AudioSource volume and gives us direct control
/// </summary>
public class WebRTCAudioFilter : MonoBehaviour
{
    private PipelineType pipelineType;
    private AudioStreamTrack incomingAudioTrack;
    private float volumeMultiplier = 1.0f;
    private bool isInitialized = false;
    private bool hasIncomingAudio = false;
    
    // Audio buffer for WebRTC data
    private float[] webrtcAudioBuffer;
    private int bufferSize = 1024;
    private int channels = 2;
    
    public void Initialize(PipelineType pipeline, float volume)
    {
        pipelineType = pipeline;
        volumeMultiplier = Mathf.Clamp01(volume);
        isInitialized = true;
        
        // Initialize audio buffer
        webrtcAudioBuffer = new float[bufferSize * channels];
        
        Debug.Log($"[🎵WebRTC-{pipelineType}] Audio filter initialized with volume {volumeMultiplier:F2}");
    }
    
    public void SetIncomingAudioTrack(AudioStreamTrack audioTrack)
    {
        incomingAudioTrack = audioTrack;
        hasIncomingAudio = true;
        
        Debug.Log($"[🎵WebRTC-{pipelineType}] Incoming audio track set");
    }
    
    public void SetVolume(float volume)
    {
        volumeMultiplier = Mathf.Clamp01(volume);
        Debug.Log($"[🎵WebRTC-{pipelineType}] Volume multiplier set to {volumeMultiplier:F2}");
    }
    
    /// <summary>
    /// This is where we inject WebRTC audio into Unity's audio pipeline
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized || !hasIncomingAudio)
        {
            // Fill with silence if no audio
            Array.Clear(data, 0, data.Length);
            return;
        }
        
        // Here we would normally get audio data from the WebRTC track
        // Since Unity WebRTC doesn't expose raw audio data directly,
        // we need to use a different approach
        
        // For now, let's create a working version that demonstrates the concept
        // In a full implementation, you'd need to:
        // 1. Extract audio data from the AudioStreamTrack
        // 2. Convert it to the right format
        // 3. Apply volume control
        // 4. Fill the data array
        
        // Temporary: Fill with silence but apply volume control to demonstrate
        Array.Clear(data, 0, data.Length);
        
        // If we had audio data, we would do:
        // for (int i = 0; i < data.Length; i++)
        // {
        //     data[i] = webrtcAudioData[i] * volumeMultiplier;
        // }
        

    }
    
    void OnDestroy()
    {
        if (isInitialized)
        {
            Debug.Log($"[🎵WebRTC-{pipelineType}] Audio filter destroyed");
        }
    }
}