using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;

/// <summary>
/// NDI Audio Interceptor that directly feeds WebRTC using SetData with proper buffering
/// Back to the working approach but with smooth buffer management
/// </summary>
[RequireComponent(typeof(NdiReceiver))]
public class NdiAudioInterceptor : MonoBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private bool debugMode = false;
    
    [Header("Component References")]
    [SerializeField] private AudioSourceBridge targetAudioSourceBridge;
    
    [Header("Buffering and Timing")]
    [SerializeField] private float audioPollingRate = 1000f; // Hz - high frequency NDI data pulling
    [SerializeField] private float webrtcSendRate = 100f; // Hz - WebRTC streaming rate
    [SerializeField] private int bufferSizeMs = 100; // Buffer size in milliseconds
    
    [Header("Test Audio Settings")]
    [SerializeField] private float testToneFrequency = 440f;
    [SerializeField] private float testToneVolume = 0.3f;

    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    private AudioStreamTrack audioStreamTrack;
    
    // Smooth buffering system
    private readonly object audioLock = new object();
    private float[] smoothBuffer;
    private int smoothBufferSize;
    private int smoothBufferWritePos = 0;
    private int smoothBufferReadPos = 0;
    private bool bufferHasData = false;
    
    // WebRTC streaming
    private float[] webrtcBuffer;
    private int webrtcBufferSize;
    
    // Timing calculations
    private float audioPollingInterval;
    private float webrtcSendInterval;
    
    // Test audio generation
    private bool isGeneratingTestAudio = false;
    private float testTonePhase = 0f;
    
    // Audio level monitoring
    private float currentAudioLevel = 0f;
    private float peakAudioLevel = 0f;
    private int ndiFramesProcessedCount = 0;
    private int webrtcFramesSentCount = 0;
    private float totalAudioTime = 0f;
    
    // Chunk size tracking
    private int lastChunkSize = 0;
    private int minChunkSize = int.MaxValue;
    private int maxChunkSize = 0;
    
    // Audio data tracking
    private bool hasNdiAudioThisFrame = false;
    private int sampleRate;
    private int systemChannels;
    
    // State management
    private bool isStreamingActive = false;
    private bool isInitialized = false;
    private Coroutine ndiPullingCoroutine;
    private Coroutine webrtcStreamingCoroutine;
    
    #region Unity Lifecycle
    
    void Awake()
    {
        ndiReceiver = GetComponent<NdiReceiver>();
        InitializeAudioSystem();
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
        if (targetAudioSourceBridge == null)
        {
            targetAudioSourceBridge = gameObject.GetComponentInChildren<AudioSourceBridge>();
        }
    }
    
    void OnDestroy()
    {
        StopTestAudio();
        StopAudioStreaming();
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeAudioSystem()
    {
        sampleRate = AudioSettings.outputSampleRate;
        systemChannels = GetChannelCountFromSpeakerMode(AudioSettings.speakerMode);
        
        // Calculate intervals
        audioPollingInterval = 1f / audioPollingRate;
        webrtcSendInterval = 1f / webrtcSendRate;
        
        // Calculate buffer sizes
        smoothBufferSize = (int)(sampleRate * systemChannels * bufferSizeMs / 1000f);
        webrtcBufferSize = (int)(sampleRate * systemChannels * webrtcSendInterval);
        
        // Initialize buffers
        smoothBuffer = new float[smoothBufferSize];
        webrtcBuffer = new float[webrtcBufferSize];
        
        isInitialized = true;
        Debug.Log($"[🎵AudioInterceptor] Initialized - Sample Rate: {sampleRate}Hz, Channels: {systemChannels}");
        Debug.Log($"[🎵AudioInterceptor] Smooth buffer: {smoothBufferSize} samples ({bufferSizeMs}ms)");
        Debug.Log($"[🎵AudioInterceptor] WebRTC buffer: {webrtcBufferSize} samples ({webrtcSendInterval*1000:F1}ms)");
        Debug.Log($"[🎵AudioInterceptor] NDI pulling: {audioPollingRate}Hz, WebRTC sending: {webrtcSendRate}Hz");
    }
    
    private int GetChannelCountFromSpeakerMode(AudioSpeakerMode speakerMode)
    {
        switch (speakerMode)
        {
            case AudioSpeakerMode.Mono: return 1;
            case AudioSpeakerMode.Stereo: return 2;
            case AudioSpeakerMode.Quad: return 4;
            case AudioSpeakerMode.Surround: return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            default: return 2;
        }
    }
    
    #endregion
    
    #region NDI Data Pulling
    
    private IEnumerator NdiDataPulling()
    {
        Debug.Log($"[🎵AudioInterceptor] Starting NDI data pulling at {audioPollingRate}Hz");
        
        while (isStreamingActive)
        {
            PullNdiAudioData();
            yield return new WaitForSeconds(audioPollingInterval);
        }
        
        Debug.Log("[🎵AudioInterceptor] NDI data pulling stopped");
    }
    
    private void PullNdiAudioData()
    {
        hasNdiAudioThisFrame = false;
        
        if (targetAudioSourceBridge == null)
            return;
            
        // Try to get latest processed audio from AudioSourceBridge
        if (targetAudioSourceBridge.TryGetLatestAudio(out float[] audioData, out int sourceChannels))
        {
            lock (audioLock)
            {
                hasNdiAudioThisFrame = true;
                ndiFramesProcessedCount++;
                
                // Write to smooth buffer
                WriteToSmoothBuffer(audioData, sourceChannels);
                
                // Track chunk sizes for analysis
                lastChunkSize = audioData.Length;
                if (lastChunkSize < minChunkSize) minChunkSize = lastChunkSize;
                if (lastChunkSize > maxChunkSize) maxChunkSize = lastChunkSize;
                
                // Calculate audio levels
                currentAudioLevel = CalculateRMS(audioData);
                if (currentAudioLevel > peakAudioLevel)
                {
                    peakAudioLevel = currentAudioLevel;
                }
                
                totalAudioTime += (float)audioData.Length / sourceChannels / sampleRate;
                
                if (debugMode || currentAudioLevel > 0.001f)
                {
                    Debug.Log($"[🎵AudioInterceptor] NDI Audio pulled - RMS: {currentAudioLevel:F3}, Size: {audioData.Length}, Channels: {sourceChannels}");
                }
            }
        }
        else if (isGeneratingTestAudio)
        {
            hasNdiAudioThisFrame = true;
        }
    }
    
    #endregion
    
    #region WebRTC Streaming
    
    private IEnumerator WebRtcStreaming()
    {
        Debug.Log($"[🎵AudioInterceptor] Starting WebRTC streaming at {webrtcSendRate}Hz");
        
        while (isStreamingActive)
        {
            SendToWebRtc();
            yield return new WaitForSeconds(webrtcSendInterval);
        }
        
        Debug.Log("[🎵AudioInterceptor] WebRTC streaming stopped");
    }
    
    private void SendToWebRtc()
    {
        if (audioStreamTrack == null) return;
        
        lock (audioLock)
        {
            // Clear WebRTC buffer
            System.Array.Clear(webrtcBuffer, 0, webrtcBuffer.Length);
            
            if (isGeneratingTestAudio)
            {
                // Generate test audio directly
                GenerateTestAudio(webrtcBuffer, systemChannels);
            }
            else if (bufferHasData)
            {
                // Read from smooth buffer
                ReadFromSmoothBuffer(webrtcBuffer, systemChannels);
            }
            // else: buffer stays silent
            
            // Send to WebRTC
            try
            {
                audioStreamTrack.SetData(webrtcBuffer, systemChannels, sampleRate);
                webrtcFramesSentCount++;
                
                if (debugMode)
                {
                    float rms = CalculateRMS(webrtcBuffer);
                    Debug.Log($"[🎵AudioInterceptor] WebRTC sent - Frame: {webrtcFramesSentCount}, RMS: {rms:F3}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[🎵AudioInterceptor] Error sending to WebRTC: {e.Message}");
            }
        }
    }
    
    #endregion
    
    #region Buffer Management
    
    private void WriteToSmoothBuffer(float[] ndiData, int ndiChannels)
    {
        if (ndiData == null || ndiData.Length == 0) return;
        
        int ndiSamples = ndiData.Length / ndiChannels;
        
        // Write NDI data to circular buffer with channel conversion
        for (int sample = 0; sample < ndiSamples; sample++)
        {
            for (int ch = 0; ch < systemChannels; ch++)
            {
                float sampleValue = 0f;
                
                if (ndiChannels == systemChannels)
                {
                    sampleValue = ndiData[sample * ndiChannels + ch];
                }
                else if (ndiChannels == 1 && systemChannels == 2)
                {
                    // Mono to stereo
                    sampleValue = ndiData[sample];
                }
                else if (ndiChannels == 2 && systemChannels == 1)
                {
                    // Stereo to mono
                    if (ch == 0)
                    {
                        float left = ndiData[sample * 2];
                        float right = ndiData[sample * 2 + 1];
                        sampleValue = (left + right) * 0.5f;
                    }
                }
                else if (ch < ndiChannels)
                {
                    sampleValue = ndiData[sample * ndiChannels + ch];
                }
                
                int bufferIndex = (smoothBufferWritePos + sample * systemChannels + ch) % smoothBufferSize;
                smoothBuffer[bufferIndex] = sampleValue;
            }
        }
        
        // Advance write position
        smoothBufferWritePos = (smoothBufferWritePos + ndiSamples * systemChannels) % smoothBufferSize;
        bufferHasData = true;
        
        if (debugMode)
        {
            Debug.Log($"[🎵AudioInterceptor] Wrote to buffer - Samples: {ndiSamples}, Available: {GetAvailableSamples()}");
        }
    }
    
    private void ReadFromSmoothBuffer(float[] outputBuffer, int outputChannels)
    {
        int samplesNeeded = outputBuffer.Length / outputChannels;
        int samplesAvailable = GetAvailableSamples();
        
        if (samplesAvailable < samplesNeeded)
        {
            // Not enough data, buffer underrun
            if (debugMode)
            {
                Debug.Log($"[🎵AudioInterceptor] Buffer underrun - Need: {samplesNeeded}, Available: {samplesAvailable}");
            }
            return; // Keep buffer silent
        }
        
        // Read samples from circular buffer
        for (int sample = 0; sample < samplesNeeded; sample++)
        {
            for (int ch = 0; ch < outputChannels; ch++)
            {
                int bufferIndex = (smoothBufferReadPos + sample * systemChannels + ch) % smoothBufferSize;
                outputBuffer[sample * outputChannels + ch] = smoothBuffer[bufferIndex];
            }
        }
        
        // Advance read position
        smoothBufferReadPos = (smoothBufferReadPos + samplesNeeded * systemChannels) % smoothBufferSize;
        
        if (debugMode)
        {
            float rms = CalculateRMS(outputBuffer);
            Debug.Log($"[🎵AudioInterceptor] Read from buffer - RMS: {rms:F3}, Available after read: {GetAvailableSamples()}");
        }
    }
    
    private int GetAvailableSamples()
    {
        if (!bufferHasData) return 0;
        
        int available = smoothBufferWritePos - smoothBufferReadPos;
        if (available < 0) available += smoothBufferSize;
        return available / systemChannels;
    }
    
    #endregion
    
    #region Audio Processing Utilities
    
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
        if (audioStreamTrack != null)
        {
            audioStreamTrack.Dispose();
        }
        
        // Create AudioStreamTrack without AudioSource (direct feeding)
        audioStreamTrack = new AudioStreamTrack();
        audioStreamTrack.Loopback = false;
        
        isStreamingActive = true;
        
        // Reset buffers
        lock (audioLock)
        {
            bufferHasData = false;
            smoothBufferWritePos = 0;
            smoothBufferReadPos = 0;
            System.Array.Clear(smoothBuffer, 0, smoothBuffer.Length);
        }
        
        // Start coroutines
        if (ndiPullingCoroutine != null)
        {
            StopCoroutine(ndiPullingCoroutine);
        }
        if (webrtcStreamingCoroutine != null)
        {
            StopCoroutine(webrtcStreamingCoroutine);
        }
        
        ndiPullingCoroutine = StartCoroutine(NdiDataPulling());
        webrtcStreamingCoroutine = StartCoroutine(WebRtcStreaming());
        
        Debug.Log($"[🎵AudioInterceptor] Audio streaming started - Direct WebRTC feeding with smooth buffering");
    }
    
    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        
        // Stop coroutines
        if (ndiPullingCoroutine != null)
        {
            StopCoroutine(ndiPullingCoroutine);
            ndiPullingCoroutine = null;
        }
        
        if (webrtcStreamingCoroutine != null)
        {
            StopCoroutine(webrtcStreamingCoroutine);
            webrtcStreamingCoroutine = null;
        }
        
        if (audioStreamTrack != null)
        {
            audioStreamTrack.Dispose();
            audioStreamTrack = null;
        }
        
        // Clear buffers
        lock (audioLock)
        {
            bufferHasData = false;
            smoothBufferWritePos = 0;
            smoothBufferReadPos = 0;
            System.Array.Clear(smoothBuffer, 0, smoothBuffer.Length);
        }
        
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    public AudioStreamTrack GetAudioTrack()
    {
        return audioStreamTrack;
    }
    
    public bool IsReceivingAudio => hasNdiAudioThisFrame || isGeneratingTestAudio;
    public bool IsStreamingActive => isStreamingActive;
    
    public void SetTargetAudioSourceBridge(AudioSourceBridge bridge)
    {
        targetAudioSourceBridge = bridge;
        Debug.Log($"[🎵AudioInterceptor] Target AudioSourceBridge set: {(bridge != null ? bridge.name : "null")}");
    }
    
    #endregion
    
    #region Test Audio Generation
    
    private void GenerateTestAudio(float[] data, int audioChannels)
    {
        int samples = data.Length / audioChannels;
        float sampleRateFloat = (float)sampleRate;
        
        for (int sample = 0; sample < samples; sample++)
        {
            float sineWave = Mathf.Sin(testTonePhase) * testToneVolume;
            testTonePhase += 2f * Mathf.PI * testToneFrequency / sampleRateFloat;
            
            if (testTonePhase > 2f * Mathf.PI)
                testTonePhase -= 2f * Mathf.PI;
            
            for (int ch = 0; ch < audioChannels; ch++)
            {
                data[sample * audioChannels + ch] = sineWave;
            }
        }
        
        currentAudioLevel = testToneVolume;
    }
    
    private void StopTestAudio()
    {
        isGeneratingTestAudio = false;
    }
    
    #endregion
    
    #region Test Methods
    
    [ContextMenu("Test Audio - Start")]
    public void TestAudioStart()
    {
        if (!isStreamingActive)
        {
            Debug.LogWarning("[🎵AudioInterceptor] Start audio streaming first!");
            return;
        }
        
        isGeneratingTestAudio = true;
        Debug.Log("[🎵AudioInterceptor] Test audio started");
        StartCoroutine(StopTestAfterDelay(3f));
    }
    
    private IEnumerator StopTestAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        StopTestAudio();
        Debug.Log("[🎵AudioInterceptor] Test audio stopped");
    }
    
    [ContextMenu("Print Audio Status")]
    public void PrintAudioStatus()
    {
        string bridgeStatus = "Not assigned";
        if (targetAudioSourceBridge != null)
        {
            bridgeStatus = $"Connected to '{targetAudioSourceBridge.name}'";
        }
        
        string ndiCoroutineStatus = ndiPullingCoroutine != null ? "Running" : "Stopped";
        string webrtcCoroutineStatus = webrtcStreamingCoroutine != null ? "Running" : "Stopped";
        
        int availableSamples = GetAvailableSamples();
        
        string status = $"[🎵AudioInterceptor] Status:\n" +
                        $"  NDI Source: {(ndiReceiver != null ? $"Connected to '{ndiReceiver.ndiName}'" : "No NDI receiver")}\n" +
                        $"  AudioSourceBridge: {bridgeStatus}\n" +
                        $"  Streaming: {isStreamingActive}\n" +
                        $"  NDI Pulling Coroutine: {ndiCoroutineStatus} ({audioPollingRate}Hz)\n" +
                        $"  WebRTC Streaming Coroutine: {webrtcCoroutineStatus} ({webrtcSendRate}Hz)\n" +
                        $"  Smooth Buffer: {availableSamples} samples available ({availableSamples / (float)sampleRate:F3}s)\n" +
                        $"  Receiving Audio: {hasNdiAudioThisFrame}\n" +
                        $"  Audio Level: {currentAudioLevel:F3} (Peak: {peakAudioLevel:F3})\n" +
                        $"  NDI Frames Processed: {ndiFramesProcessedCount}\n" +
                        $"  WebRTC Frames Sent: {webrtcFramesSentCount}\n" +
                        $"  Chunk Sizes - Last: {lastChunkSize}, Min: {(minChunkSize == int.MaxValue ? 0 : minChunkSize)}, Max: {maxChunkSize}\n" +
                        $"  Buffer Size: {smoothBufferSize} samples ({bufferSizeMs}ms)\n" +
                        $"  WebRTC Buffer: {webrtcBufferSize} samples ({webrtcSendInterval*1000:F1}ms)\n" +
                        $"  Total Audio Time: {totalAudioTime:F1}s\n" +
                        $"  Test Audio: {isGeneratingTestAudio}\n" +
                        $"  Sample Rate: {sampleRate}Hz, Channels: {systemChannels}";
        
        Debug.Log(status);
    }
    
    #endregion
}