using UnityEngine;
using BroadcastPipeline;

public class ReceivingAudioInterceptor : MonoBehaviour
{
    private PipelineType pipelineType;
    private WebRTCAudioFilter targetFilter;
    private bool isInitialized = false;
    private int debugCounter = 0;
    
    public void Initialize(PipelineType pipeline, WebRTCAudioFilter filter)
    {
        pipelineType = pipeline;
        targetFilter = filter;
        isInitialized = true;
        Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Receiving audio interceptor initialized");
    }
    
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized || targetFilter == null) return;
        
        // Check if we have actual audio
        float maxLevel = 0f;
        for (int i = 0; i < Mathf.Min(100, data.Length); i++)
        {
            maxLevel = Mathf.Max(maxLevel, Mathf.Abs(data[i]));
        }
        
        debugCounter++;
        if (debugCounter % 48 == 0) // Log every ~1 second
        {
            Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Intercepted WebRTC audio: {data.Length} samples, {channels} ch, maxLevel: {maxLevel:F4}");
        }
        
        // Send to WebRTC filter if we have audio
        if (maxLevel > 0.001f)
        {
            int sampleRate = AudioSettings.outputSampleRate;
            targetFilter.ReceiveAudioChunk(data, channels, sampleRate);
        }
    }
}