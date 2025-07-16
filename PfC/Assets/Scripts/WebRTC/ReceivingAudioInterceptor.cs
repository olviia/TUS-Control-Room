
using UnityEngine;
using BroadcastPipeline;

public class ReceivingAudioInterceptor : MonoBehaviour
{
    private PipelineType pipelineType;
    private WebRTCAudioFilter targetFilter;
    private bool isInitialized = false;
    private int debugCounter = 0;
    private bool passThrough = true;
    
    // Cache these on the main thread
    private AudioSource cachedAudioSource;
    private bool wasPlaying = false;
    private float lastVolume = 0f;
    private string clipName = "none";
    
    void Awake()
    {
        // Cache the AudioSource reference on the main thread
        cachedAudioSource = GetComponent<AudioSource>();
    }
    
    public void Initialize(PipelineType pipeline, WebRTCAudioFilter filter)
    {
        pipelineType = pipeline;
        targetFilter = filter;
        isInitialized = true;
        // Cache AudioSource if not already done
        if (cachedAudioSource == null)
            cachedAudioSource = GetComponent<AudioSource>();
        Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Receiving audio interceptor initialized");
    }
    
    void Update()
    {
        // Update cached values on the main thread
        if (cachedAudioSource != null)
        {
            wasPlaying = cachedAudioSource.isPlaying;
            lastVolume = cachedAudioSource.volume;
            clipName = cachedAudioSource.clip?.name ?? "none";
        }
    }
    
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized || targetFilter == null) return;
        
        // Create a copy of the data BEFORE it gets processed
        float[] dataCopy = new float[data.Length];
        System.Array.Copy(data, dataCopy, data.Length);
        
        // Check if we have actual audio - with MORE samples
        float maxLevel = 0f;
        float avgLevel = 0f;
        int nonZeroSamples = 0;
        
        for (int i = 0; i < data.Length; i++)
        {
            float sample = Mathf.Abs(data[i]);
            maxLevel = Mathf.Max(maxLevel, sample);
            avgLevel += sample;
            if (sample > 0.0001f) nonZeroSamples++;
        }
        avgLevel /= data.Length;
        
        debugCounter++;  
        if (debugCounter % 96 == 0)
        {
            Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Audio stats - Max: {maxLevel:F6}, Avg: {avgLevel:F6}, NonZero: {nonZeroSamples}/{data.Length}");
            Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] AudioSource - Playing: {wasPlaying}, Volume: {lastVolume}, Clip: {clipName}");
            
            // Log first few samples to see actual values
            string sampleValues = "First 10 samples: ";
            for (int i = 0; i < Mathf.Min(10, data.Length); i++)
            {
                sampleValues += $"{data[i]:F6} ";
            }
            Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] {sampleValues}");
        }
        
        // AMPLIFY the audio for testing - multiply by 10
        for (int i = 0; i < dataCopy.Length; i++)
        {
            dataCopy[i] *= 10f;
        }
        
        // Send to WebRTC filter if we have audio
        if (targetFilter != null && maxLevel > 0.0001f)
        {
            int sampleRate = AudioSettings.outputSampleRate;
            targetFilter.ReceiveAudioChunk(dataCopy, channels, sampleRate);
            if (debugCounter % 96 == 0)
            {
                Debug.Log($"bbb_[🎵Interceptor-{pipelineType}] Sent amplified audio to filter");
            }
        }
        if (!passThrough)
        {
            System.Array.Clear(data, 0, data.Length);
        }
        if (passThrough)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= 10f;
            }
        }
    }
}