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

    [Header("WebRTC Audio Settings")]
    [Tooltip("WebRTC packet duration in milliseconds (10, 20, or 40ms). 20ms is standard.")]
    [SerializeField] private int webrtcPacketDurationMs = 20; // WebRTC standard packet size

    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    public AudioStreamTrack audioStreamTrack;
    
    
    // State management
    private bool isStreamingActive = false;
    private bool isInitialized = false;
    private Coroutine ndiPullingCoroutine;

    // Local packet assembly buffer for fixed-size WebRTC packets
    private System.Collections.Generic.List<float> packetAssemblyBuffer = new System.Collections.Generic.List<float>();
    private int lastChannelCount = 2;
    
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
        audioStreamTrack = new AudioStreamTrack();

        StartAudioStreaming();
        StopAudioStreaming();
    }

    void Update()
    {
        // Send fixed-size audio packets to WebRTC at consistent intervals
        if (isStreamingActive && targetAudioSourceBridge != null)
        {
            SendFixedSizeAudioPackets();
        }
    }

    private void SendFixedSizeAudioPackets()
    {
        // Get accumulated audio from ring buffer
        if (targetAudioSourceBridge.GetAccumulatedAudio(out float[] accumulatedData, out int channels))
        {
            // Append to our local assembly buffer
            packetAssemblyBuffer.AddRange(accumulatedData);
            lastChannelCount = channels;
        }

        int sampleRate = AudioSettings.outputSampleRate;

        // Calculate exact samples needed for desired packet duration
        // For 20ms at 48kHz stereo: (48000 * 2 * 20) / 1000 = 1920 samples
        int samplesPerPacket = (sampleRate * lastChannelCount * webrtcPacketDurationMs) / 1000;

        // Send as many complete packets as we can
        while (packetAssemblyBuffer.Count >= samplesPerPacket)
        {
            // Extract exact packet size
            float[] packet = new float[samplesPerPacket];
            packetAssemblyBuffer.CopyTo(0, packet, 0, samplesPerPacket);

            // Remove sent data from buffer
            packetAssemblyBuffer.RemoveRange(0, samplesPerPacket);

            // Send to WebRTC
            try
            {
                audioStreamTrack.SetData(packet, lastChannelCount, sampleRate);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[🎵AudioInterceptor] ❌ SetData FAILED: {e.Message}");
                break;
            }
        }

        // Keep buffer from growing indefinitely if WebRTC can't keep up
        int maxBufferSamples = (sampleRate * lastChannelCount * 100) / 1000; // 100ms max
        if (packetAssemblyBuffer.Count > maxBufferSamples)
        {
            Debug.LogWarning($"[🎵AudioInterceptor] Buffer overflow! Dropping {packetAssemblyBuffer.Count - maxBufferSamples} samples");
            packetAssemblyBuffer.RemoveRange(0, packetAssemblyBuffer.Count - maxBufferSamples);
        }
    }
    
    #endregion
    

    #region Audio Processing Utilities

    // Note: HandleChunk is no longer used - audio is now accumulated in Update via ring buffer
    // This provides better timing and prevents audio crackling issues

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

        if (targetAudioSourceBridge == null)
        {
            Debug.LogError("[🎵AudioInterceptor] No AudioSourceBridge found!");
            return;
        }

        isStreamingActive = true;
        Debug.Log("[🎵AudioInterceptor] Audio streaming started - using ring buffer accumulation");
    }

    public void StopAudioStreaming()
    {
        isStreamingActive = false;
        packetAssemblyBuffer.Clear();
        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }
    
    #endregion
    

}

