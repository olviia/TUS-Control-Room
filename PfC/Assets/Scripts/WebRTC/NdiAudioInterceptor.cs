using UnityEngine;
using Unity.WebRTC;
using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;
using BroadcastPipeline;

/// <summary>
/// Intercepts audio from NDI's automatically generated AudioSource child
/// Provides CONTINUOUS audio streaming - always streams (silence when no NDI audio)
/// Attach this to the same GameObject as NdiReceiver
/// </summary>
[RequireComponent(typeof(NdiReceiver))]
public class NdiAudioInterceptor : MonoBehaviour
{
    [Header("Audio Settings")]
    [SerializeField] private int bufferSize = 1024;
    [SerializeField] private int sampleRate = 48000;
    [SerializeField] private int channels = 2;
    [SerializeField] private bool debugMode = false;
    
    [Header("Test Audio Settings")]
    [SerializeField] private float testToneFrequency = 440f; // A4 note
    [SerializeField] private float testToneVolume = 0.3f;
    
    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    private AudioSource ndiAudioSource; // NDI's child AudioSource
    private AudioStreamTrack audioStreamTrack;
    
    // Audio buffering system - ALWAYS active when streaming
    private float[] currentAudioBuffer;
    private float[] silenceBuffer;
    private readonly object audioLock = new object();
    
    // Test audio generation
    private bool isGeneratingTestAudio = false;
    private float testTonePhase = 0f;
    private Coroutine testAudioCoroutine;
    
    // NDI audio detection
    private bool hasNdiAudioThisFrame = false;
    private float[] latestNdiAudio;
    private int latestNdiChannels;
    
    // State management
    private bool isStreamingActive = false;
    private bool isInitialized = false;
    private Coroutine streamingCoroutine;
    
    // Events
    public static event System.Action<PipelineType, bool> OnAudioAvailabilityChanged;
    
    #region Unity Lifecycle
    
    void Awake()
    {
        ndiReceiver = GetComponent<NdiReceiver>();
        ValidateConfiguration();
        InitializeAudioSystem();
    }
    
    void Start()
    {
        if (ndiReceiver != null)
        {
            Debug.Log($"[🎵AudioInterceptor] Connected to NDI receiver: {ndiReceiver.ndiName}");
            FindOrWaitForNdiAudioSource();
        }
    }
    
    void Update()
    {
        // Reset NDI audio detection each frame
        hasNdiAudioThisFrame = false;
        
        // Check if we lost the NDI audio source and need to find it again
        if (ndiAudioSource == null && ndiReceiver != null)
        {
            FindNdiAudioSource();
        }
    }
    
    void OnDestroy()
    {
        StopTestAudio();
        StopAudioStreaming();
        CleanupAudioSystem();
    }
    
    #endregion
    
    #region Initialization
    
    private void ValidateConfiguration()
    {
        if (ndiReceiver == null)
        {
            Debug.LogError("[🎵AudioInterceptor] No NdiReceiver found!");
            return;
        }
        
        // Get actual Unity audio settings
        sampleRate = AudioSettings.outputSampleRate;
        channels = GetChannelCountFromSpeakerMode(AudioSettings.speakerMode);
        
        Debug.Log($"[🎵AudioInterceptor] Unity Audio - Sample Rate: {sampleRate}Hz, Channels: {channels}");
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
    
    private void InitializeAudioSystem()
    {
        // Create audio buffers
        currentAudioBuffer = new float[bufferSize * channels];
        silenceBuffer = new float[bufferSize * channels]; // Already filled with zeros
        
        isInitialized = true;
        Debug.Log($"[🎵AudioInterceptor] Initialized - Buffer: {bufferSize}, Sample Rate: {sampleRate}Hz, Channels: {channels}");
    }
    
    #endregion
    
    #region NDI AudioSource Detection
    
    private void FindOrWaitForNdiAudioSource()
    {
        if (FindNdiAudioSource())
        {
            SetupNdiAudioInterception();
        }
        else
        {
            // NDI audio source not ready yet, start checking periodically
            StartCoroutine(WaitForNdiAudioSource());
        }
    }
    
    private bool FindNdiAudioSource()
    {
        if (ndiReceiver == null) return false;
        
        // Look for AudioSource in children (NDI creates it as child)
        AudioSource[] childAudioSources = ndiReceiver.GetComponentsInChildren<AudioSource>();
        
        foreach (var audioSource in childAudioSources)
        {
            // NDI audio sources are typically on child GameObjects
            if (audioSource.gameObject != this.gameObject)
            {
                ndiAudioSource = audioSource;
                Debug.Log($"[🎵AudioInterceptor] Found NDI AudioSource: {audioSource.gameObject.name}");
                return true;
            }
        }
        
        return false;
    }
    
    private IEnumerator WaitForNdiAudioSource()
    {
        Debug.Log("[🎵AudioInterceptor] Waiting for NDI AudioSource to be created...");
        
        float timeout = 10f; // Wait up to 10 seconds
        float elapsed = 0f;
        
        while (elapsed < timeout && ndiAudioSource == null)
        {
            if (FindNdiAudioSource())
            {
                SetupNdiAudioInterception();
                yield break;
            }
            
            elapsed += 0.5f;
            yield return new WaitForSeconds(0.5f);
        }
        
        if (ndiAudioSource == null)
        {
            Debug.LogWarning("[🎵AudioInterceptor] NDI AudioSource not found after timeout. Audio will stream silence until NDI audio is available.");
        }
    }
    
    private void SetupNdiAudioInterception()
    {
        if (ndiAudioSource == null) return;
        var existingBridge = ndiAudioSource.GetComponent<AudioSourceBridge>();

        // Add our audio filter to the NDI AudioSource to intercept its audio
        var audioFilter = ndiAudioSource.gameObject.GetComponent<NdiAudioFilter>();
        if (audioFilter == null)
        {
            audioFilter = ndiAudioSource.gameObject.AddComponent<NdiAudioFilter>();
        }
        
        // Connect the filter to this interceptor
        audioFilter.Initialize(this);
        
        Debug.Log($"[🎵AudioInterceptor] Audio interception setup complete on {ndiAudioSource.gameObject.name}");
    }
    
    #endregion
    
    #region Public Interface
    
    /// <summary>
    /// Start streaming audio through WebRTC AudioStreamTrack
    /// IMMEDIATELY starts continuous streaming (silence until audio available)
    /// Called from WebRTCStreamer when video streaming starts
    /// </summary>
    public void StartAudioStreaming()
    {
        if (!isInitialized)
        {
            Debug.LogError("[🎵AudioInterceptor] Not initialized!");
            return;
        }
        
        if (isStreamingActive)
        {
            Debug.LogWarning("[🎵AudioInterceptor] Audio streaming already active");
            return;
        }
        
        // Create WebRTC audio track (no AudioSource - we use SetData)
        audioStreamTrack = new AudioStreamTrack();
        audioStreamTrack.Loopback = false; // Don't play locally
        
        isStreamingActive = true;
        
        // START CONTINUOUS STREAMING IMMEDIATELY
        streamingCoroutine = StartCoroutine(ContinuousAudioStreamingCoroutine());
        
        Debug.Log("[🎵AudioInterceptor] CONTINUOUS audio streaming started - will stream silence until audio available");
    }
    
    /// <summary>
    /// Stop audio streaming
    /// Called from WebRTCStreamer when video streaming stops
    /// </summary>
    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        
        // Stop streaming coroutine
        if (streamingCoroutine != null)
        {
            StopCoroutine(streamingCoroutine);
            streamingCoroutine = null;
        }
        
        // Dispose WebRTC track
        if (audioStreamTrack != null)
        {
            audioStreamTrack.Dispose();
            audioStreamTrack = null;
        }
        
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    /// <summary>
    /// Get the current audio track for adding to peer connection
    /// </summary>
    public AudioStreamTrack GetAudioTrack()
    {
        return audioStreamTrack;
    }
    
    /// <summary>
    /// Called by NdiAudioFilter when NDI audio data is available
    /// This updates our audio buffer with real NDI data
    /// </summary>
    public void OnNdiAudioData(float[] audioData, int audioChannels)
    {
        if (!isStreamingActive) return;
        
        lock (audioLock)
        {
            hasNdiAudioThisFrame = true;
            
            // Store latest NDI audio data
            latestNdiAudio = audioData;
            latestNdiChannels = audioChannels;
            
            // Convert and copy to current buffer
            ConvertAndCopyAudioData(audioData, audioChannels, currentAudioBuffer, channels);
        }
        
        if (debugMode && Time.frameCount % 60 == 0) // Log once per second at 60fps
        {
            float rms = CalculateRMS(audioData);
            Debug.Log($"[🎵AudioInterceptor] NDI audio received - Samples: {audioData.Length}, Channels: {audioChannels}, RMS: {rms:F4}");
        }
    }
    
    /// <summary>
    /// Check if audio is currently being received from NDI or generated
    /// </summary>
    public bool IsReceivingAudio => hasNdiAudioThisFrame || isGeneratingTestAudio;
    
    /// <summary>
    /// Check if streaming is active
    /// </summary>
    public bool IsStreamingActive => isStreamingActive;
    
    #endregion
    
    #region Continuous Audio Streaming - CORE FUNCTIONALITY
    
    /// <summary>
    /// CONTINUOUS audio streaming coroutine - ALWAYS streams something
    /// Streams real audio when available, silence when not
    /// </summary>
    private IEnumerator ContinuousAudioStreamingCoroutine()
    {
        Debug.Log("[🎵AudioInterceptor] Continuous audio streaming coroutine started");
        
        while (isStreamingActive)
        {
            if (audioStreamTrack != null)
            {
                // Prepare next audio buffer
                PrepareNextAudioBuffer();
                
                // ALWAYS send data to WebRTC (silence or real audio)
                try
                {
                    audioStreamTrack.SetData(currentAudioBuffer, channels, sampleRate);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[🎵AudioInterceptor] Error sending audio data: {e.Message}");
                }
            }
            
            // Wait for next audio frame (audio update rate)
            yield return new WaitForSeconds((float)bufferSize / sampleRate);
        }
        
        Debug.Log("[🎵AudioInterceptor] Continuous audio streaming coroutine ended");
    }
    
    /// <summary>
    /// Prepares the next audio buffer - real audio, test audio, or silence
    /// ALWAYS provides data - never leaves WebRTC hanging
    /// </summary>
    private void PrepareNextAudioBuffer()
    {
        lock (audioLock)
        {
            if (isGeneratingTestAudio)
            {
                // Generate test audio
                GenerateTestAudio(currentAudioBuffer, channels);
            }
            else if (hasNdiAudioThisFrame && latestNdiAudio != null)
            {
                // Use real NDI audio (already copied in OnNdiAudioData)
                // currentAudioBuffer already contains converted NDI data
            }
            else
            {
                // No audio available - use silence
                System.Array.Copy(silenceBuffer, 0, currentAudioBuffer, 0, currentAudioBuffer.Length);
                
                if (debugMode && Time.frameCount % 300 == 0) // Log every 5 seconds
                {
                    Debug.Log("[🎵AudioInterceptor] Streaming silence - no audio source available");
                }
            }
        }
    }
    
    #endregion
    
    #region Audio Processing
    
    private void ConvertAndCopyAudioData(float[] input, int inputChannels, float[] output, int outputChannels)
    {
        int inputSamples = input.Length / inputChannels;
        int outputSamples = output.Length / outputChannels;
        int samplesToCopy = Mathf.Min(inputSamples, outputSamples);
        
        for (int sample = 0; sample < samplesToCopy; sample++)
        {
            if (inputChannels == outputChannels)
            {
                // Direct copy
                for (int ch = 0; ch < outputChannels; ch++)
                {
                    output[sample * outputChannels + ch] = input[sample * inputChannels + ch];
                }
            }
            else if (inputChannels == 1 && outputChannels == 2)
            {
                // Mono to stereo
                float monoSample = input[sample];
                output[sample * 2] = monoSample;
                output[sample * 2 + 1] = monoSample;
            }
            else if (inputChannels == 2 && outputChannels == 1)
            {
                // Stereo to mono
                float leftSample = input[sample * 2];
                float rightSample = input[sample * 2 + 1];
                output[sample] = (leftSample + rightSample) * 0.5f;
            }
            else
            {
                // Default: copy available channels, pad with zeros
                for (int ch = 0; ch < outputChannels; ch++)
                {
                    if (ch < inputChannels)
                    {
                        output[sample * outputChannels + ch] = input[sample * inputChannels + ch];
                    }
                    else
                    {
                        output[sample * outputChannels + ch] = 0f;
                    }
                }
            }
        }
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
    
    #region Test Audio Generation
    
    private void GenerateTestAudio(float[] data, int audioChannels)
    {
        int samples = data.Length / audioChannels;
        float sampleRateFloat = (float)sampleRate;
        
        for (int sample = 0; sample < samples; sample++)
        {
            float sineWave = Mathf.Sin(testTonePhase) * testToneVolume;
            testTonePhase += 2f * Mathf.PI * testToneFrequency / sampleRateFloat;
            
            // Reset phase to prevent overflow
            if (testTonePhase > 2f * Mathf.PI)
                testTonePhase -= 2f * Mathf.PI;
            
            // Apply to all channels
            for (int ch = 0; ch < audioChannels; ch++)
            {
                data[sample * audioChannels + ch] = sineWave;
            }
        }
    }
    
    private void StopTestAudio()
    {
        isGeneratingTestAudio = false;
        
        if (testAudioCoroutine != null)
        {
            StopCoroutine(testAudioCoroutine);
            testAudioCoroutine = null;
        }
    }
    
    #endregion
    
    #region Utility
    
    private PipelineType GetPipelineType()
    {
        // Try to determine pipeline type from parent objects
        var streamManager = GetComponentInParent<WebRTCStreamer>();
        if (streamManager != null)
        {
            return streamManager.pipelineType;
        }
        
        // Fallback - you might need to adjust this based on your naming convention
        if (name.ToLower().Contains("studio"))
            return PipelineType.StudioLive;
        else if (name.ToLower().Contains("tv"))
            return PipelineType.TVLive;
        
        return PipelineType.StudioLive; // Default
    }
    
    private void CleanupAudioSystem()
    {
        // Cleanup resources
        currentAudioBuffer = null;
        silenceBuffer = null;
        latestNdiAudio = null;
    }
    
    #endregion
    
    #region Test Methods
    
    [ContextMenu("Test Audio - Sender + Receiver (with local playback)")]
    public void TestAudioSenderAndReceiver()
    {
        if (!isStreamingActive)
        {
            Debug.LogWarning("[🎵AudioInterceptor] Start audio streaming first!");
            return;
        }
        
        // This test plays audio locally AND streams it
        if (ndiAudioSource != null)
        {
            // Enable local playback on NDI audio source
            ndiAudioSource.volume = testToneVolume;
            ndiAudioSource.enabled = true;
        }
        
        // Generate test audio
        isGeneratingTestAudio = true;
        
        Debug.Log("[🎵AudioInterceptor] Test audio started - you should hear it locally AND on receiver");
        
        // Auto-stop after 3 seconds
        StartCoroutine(StopTestAfterDelay(3f));
    }
    
    [ContextMenu("Test Audio - Receiver Only (no local playback)")]
    public void TestAudioReceiverOnly()
    {
        if (!isStreamingActive)
        {
            Debug.LogWarning("[🎵AudioInterceptor] Start audio streaming first!");
            return;
        }
        
        // This test streams audio but doesn't play locally
        if (ndiAudioSource != null)
        {
            // Disable local playback
            ndiAudioSource.volume = 0f;
        }
        
        // Generate test audio
        isGeneratingTestAudio = true;
        
        Debug.Log("[🎵AudioInterceptor] Test audio started - you should ONLY hear it on receiver, not locally");
        
        // Auto-stop after 3 seconds
        StartCoroutine(StopTestAfterDelay(3f));
    }
    
    private IEnumerator StopTestAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        StopTestAudio();
        
        // Restore NDI audio source to normal state
        if (ndiAudioSource != null)
        {
            ndiAudioSource.volume = 1f; // Restore normal volume
        }
        
        Debug.Log("[🎵AudioInterceptor] Test audio stopped");
    }
    
    [ContextMenu("Print Audio Status")]
    public void PrintAudioStatus()
    {
        string ndiSourceStatus = ndiAudioSource != null ? 
            $"Found ({ndiAudioSource.gameObject.name})" : 
            "Not found - will stream silence until available";
            
        string status = $"[🎵AudioInterceptor] Status:\n" +
                       $"  Streaming: {isStreamingActive}\n" +
                       $"  Receiving NDI Audio: {hasNdiAudioThisFrame}\n" +
                       $"  Generating Test Audio: {isGeneratingTestAudio}\n" +
                       $"  Sample Rate: {sampleRate}Hz\n" +
                       $"  Channels: {channels}\n" +
                       $"  NDI AudioSource: {ndiSourceStatus}\n" +
                       $"  Test Frequency: {testToneFrequency}Hz\n" +
                       $"  Current Mode: {GetCurrentAudioMode()}";
        
        Debug.Log(status);
    }
    
    private string GetCurrentAudioMode()
    {
        if (!isStreamingActive) return "Not streaming";
        if (isGeneratingTestAudio) return "Test audio";
        if (hasNdiAudioThisFrame) return "NDI audio";
        return "Silence";
    }
    
    #endregion
}

/// <summary>
/// Helper component that gets added to NDI's AudioSource to intercept audio data
/// </summary>
public class NdiAudioFilter : MonoBehaviour
{
    private NdiAudioInterceptor interceptor;
    
    public void Initialize(NdiAudioInterceptor interceptor)
    {
        this.interceptor = interceptor;
    }
    
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (interceptor != null)
        {
            interceptor.OnNdiAudioData(data, channels);
        }
    }
}