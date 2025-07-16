using UnityEngine;
using BroadcastPipeline;

/// <summary>
/// Component that intercepts NDI's OnAudioFilterRead to capture audio data
/// This gets the audio BEFORE it reaches Unity's audio system
/// </summary>
public class NDIAudioInterceptor : MonoBehaviour
{
    private PipelineType pipelineType;
    private FilterBasedAudioStreamer audioStreamer;
    private bool isInitialized = false;
    private float volumeMultiplier = 1.0f;
    
    private int debugCounter = 0;
    
    public void Initialize(PipelineType pipeline, FilterBasedAudioStreamer streamer)
    {
        pipelineType = pipeline;
        audioStreamer = streamer;
        isInitialized = true;
        
        Debug.Log($"[🎵NDI-{pipelineType}] Audio interceptor initialized");
    }
    
    /// <summary>
    /// This intercepts NDI's audio before it reaches the speakers
    /// </summary>
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized || audioStreamer == null) return;
    
        // DEBUG: Check if we're getting NDI audio data
        bool hasAudio = false;
        float maxSample = 0f;
        for (int i = 0; i < Mathf.Min(10, data.Length); i++)
        {
            float sample = Mathf.Abs(data[i]);
            if (sample > 0.001f)
            {
                hasAudio = true;
            }
            maxSample = Mathf.Max(maxSample, sample);
        }

        // Log every ~2 seconds (assuming 48kHz, 1024 samples per call = ~96 calls per 2 seconds)
        debugCounter++;
        if (debugCounter % 1096 == 0)
        {
            Debug.Log($"aaa_[🎵NDI-{pipelineType}] Audio data: {data.Length} samples, {channels} channels, hasAudio: {hasAudio}, maxLevel: {maxSample:F4}");
        }
    
        // Apply volume control to NDI audio (this controls what you hear locally)
        if (volumeMultiplier < 1.0f)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= volumeMultiplier;
            }
        }
    
        // CRITICAL FIX: Only send audio data if we actually have meaningful audio
        if (hasAudio && maxSample > 0.001f)
        {
            // Send audio data to WebRTC streamer
            audioStreamer.OnNDIAudioData(data, channels);
        }
    }
    
    /// <summary>
    /// Set volume multiplier for NDI audio (controls local playback)
    /// </summary>
    public void SetVolumeMultiplier(float volume)
    {
        volumeMultiplier = Mathf.Clamp01(volume);
        Debug.Log($"[🎵NDI-{pipelineType}] NDI volume multiplier set to {volumeMultiplier:F2}");
    }
    
    void OnDestroy()
    {
        if (isInitialized)
        {
            Debug.Log($"[🎵NDI-{pipelineType}] Audio interceptor destroyed");
        }
    }
}