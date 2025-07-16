using UnityEngine;
using Unity.WebRTC;
using BroadcastPipeline;
using System;
using System.Collections.Generic;

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
    
    //chuncked audio handling
    private Queue<float[]> audioChunkQueue = new Queue<float[]>();
    private float[] currentAudioBuffer;
    private int bufferPosition = 0;
    private int expectedChannels = 2;
    private int expectedSampleRate = 48000;
    private readonly object queueLock = new object();
    
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
    
    public void ReceiveAudioChunk(float[] audioData, int channels, int sampleRate)
    {
        Debug.Log($"aaa_[🎵WebRTC-{pipelineType}] Received chunk: {audioData.Length} samples, channels: {channels}, sampleRate: {sampleRate}");
        
        expectedChannels = channels;
        expectedSampleRate = sampleRate;
        
        lock (queueLock)
        {
            // Create a copy of the audio data and queue it
            float[] chunk = new float[audioData.Length];
            Array.Copy(audioData, chunk, audioData.Length);
            audioChunkQueue.Enqueue(chunk);
            
            // Keep queue size manageable
            while (audioChunkQueue.Count > 10)
            {
                audioChunkQueue.Dequeue();
            }
        }
    }
    
    /// <summary>
    /// This is where we inject WebRTC audio into Unity's audio pipeline
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }
        
        lock (queueLock)
        {
            // Fill the output buffer from queued chunks
            int outputIndex = 0;
            
            while (outputIndex < data.Length && audioChunkQueue.Count > 0)
            {
                if (currentAudioBuffer == null || bufferPosition >= currentAudioBuffer.Length)
                {
                    // Get next chunk from queue
                    if (audioChunkQueue.Count > 0)
                    {
                        currentAudioBuffer = audioChunkQueue.Dequeue();
                        bufferPosition = 0;
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (currentAudioBuffer != null)
                {
                    // Copy data from current chunk to output
                    int samplesToCopy = Mathf.Min(data.Length - outputIndex, currentAudioBuffer.Length - bufferPosition);
                    Array.Copy(currentAudioBuffer, bufferPosition, data, outputIndex, samplesToCopy);
                    
                    outputIndex += samplesToCopy;
                    bufferPosition += samplesToCopy;
                    
                    // Apply volume
                    for (int i = outputIndex - samplesToCopy; i < outputIndex; i++)
                    {
                        data[i] *= volumeMultiplier;
                    }
                }
            }
            
            // Fill remaining with silence
            if (outputIndex < data.Length)
            {
                Array.Clear(data, outputIndex, data.Length - outputIndex);
            }
        }
    }

    
    void OnDestroy()
    {
        if (isInitialized)
        {
            Debug.Log($"[🎵WebRTC-{pipelineType}] Audio filter destroyed");
        }
    }
}