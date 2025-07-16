using UnityEngine;
using BroadcastPipeline;

public class ReceivingAudioInterceptor : MonoBehaviour
{
    private PipelineType pipelineType;
    private WebRTCAudioFilter targetFilter;
    private bool isInitialized = false;
    private int debugCounter = 0;
    private bool passThrough = true;
    
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
        
        // Create a copy of the data BEFORE it gets processed
        float[] dataCopy = new float[data.Length];
        System.Array.Copy(data, dataCopy, data.Length);
        
        // Check if we have actual audio
        float maxLevel = 0f;
        for (int i = 0; i < Mathf.Min(100, data.Length); i++)
        {
            maxLevel = Mathf.Max(maxLevel, Mathf.Abs(data[i]));
        }
        
        debugCounter++;  
        if (debugCounter % 96 == 0)
        {
            Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Intercepted WebRTC audio: {data.Length} samples, {channels} ch, maxLevel: {maxLevel:F4}");
            
            // Additional debug info
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] AudioSource - Playing: {audioSource.isPlaying}, Volume: {audioSource.volume}, Mute: {audioSource.mute}");
            }
        }
        
        // Send to WebRTC filter if we have audio
        if (targetFilter != null && maxLevel > 0.0001f)
        {
            int sampleRate = AudioSettings.outputSampleRate;
            targetFilter.ReceiveAudioChunk(dataCopy, channels, sampleRate);
        }
        if (!passThrough)
        {
            System.Array.Clear(data, 0, data.Length);
        }
    }
}