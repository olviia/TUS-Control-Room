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
    [SerializeField] private int bufferSizeMs = 200; // Ring buffer size in milliseconds (same as AudioListenerBridge)

    // NDI and WebRTC components
    private NdiReceiver ndiReceiver;
    public AudioStreamTrack audioStreamTrack;

    // Ring buffer for smooth audio streaming (same pattern as AudioListenerBridge)
    private float[] ringBuffer;
    private int writeIndex;
    private int readIndex;
    private int availableSamples;
    private bool readStarted;
    private int channels = 2;
    private int sampleRate = 48000;
    private readonly object bufferLock = new object();

    // State management
    private bool isStreamingActive = false;
    
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

        // Initialize ring buffer
        sampleRate = AudioSettings.outputSampleRate;
        int capacity = (sampleRate * channels * bufferSizeMs) / 1000;
        ringBuffer = new float[System.Math.Max(capacity, 1)];
        writeIndex = 0;
        readIndex = 0;
        availableSamples = 0;
        readStarted = false;
        Debug.Log($"[🎵AudioInterceptor] Ring buffer initialized: {capacity} samples ({bufferSizeMs}ms)");

        StartAudioStreaming();
        StopAudioStreaming();
    }
    
    void Update()
    {
        // Send accumulated audio to WebRTC every frame (same pattern as NdiSender)
        if (isStreamingActive)
        {
            SendAccumulatedAudioToWebRTC();
        }
    }

    #endregion
    

    #region Audio Processing Utilities

    private void HandleChunk(float[] audioData, int incomingChannels, int incomingSampleRate)
    {
        // Update cached values
        this.channels = incomingChannels;
        this.sampleRate = incomingSampleRate;

        // Write incoming audio to ring buffer (same pattern as AudioListenerBridge)
        WriteToRingBuffer(audioData);
    }

    private void WriteToRingBuffer(float[] data)
    {
        if (ringBuffer == null || data == null) return;

        lock (bufferLock)
        {
            int capacity = ringBuffer.Length;
            for (int i = 0; i < data.Length; i++)
            {
                ringBuffer[writeIndex] = data[i];
                writeIndex = (writeIndex + 1) % capacity;
            }
            availableSamples = System.Math.Min(availableSamples + data.Length, capacity);
        }
    }

    private bool ReadFromRingBuffer(out float[] audioData, int samplesToRead)
    {
        audioData = null;

        lock (bufferLock)
        {
            if (ringBuffer == null) return false;

            int capacity = ringBuffer.Length;

            // Wait until buffer is half full before starting (same as AudioListenerBridge)
            if (!readStarted && availableSamples >= capacity / 2)
            {
                int delay = capacity / 2;
                readIndex = (writeIndex - delay + capacity) % capacity;
                readStarted = true;
                Debug.Log($"[🎵AudioInterceptor] Started reading from buffer (half-full at {availableSamples} samples)");
            }

            if (!readStarted) return false;

            int available = (writeIndex - readIndex + capacity) % capacity;
            if (available < samplesToRead) return false;

            // Read requested samples from ring buffer
            audioData = new float[samplesToRead];
            for (int i = 0; i < samplesToRead; i++)
            {
                audioData[i] = ringBuffer[(readIndex + i) % capacity];
            }

            readIndex = (readIndex + samplesToRead) % capacity;
            return true;
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

    #region WebRTC Audio Sending

    private void SendAccumulatedAudioToWebRTC()
    {
        // Get all accumulated audio from ring buffer (same pattern as NdiSender)
        if (GetAccumulatedAudio(out float[] audioData, out int audioChannels))
        {
            try
            {
                audioStreamTrack.SetData(audioData, audioChannels, sampleRate);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[🎵AudioInterceptor] SetData failed: {e.Message}");
            }
        }
    }

    private bool GetAccumulatedAudio(out float[] audioData, out int audioChannels)
    {
        audioData = null;
        audioChannels = channels;

        lock (bufferLock)
        {
            if (ringBuffer == null) return false;

            int capacity = ringBuffer.Length;

            // Wait until buffer is half full before starting (same as AudioListenerBridge)
            if (!readStarted && availableSamples >= capacity / 2)
            {
                int delay = capacity / 2;
                readIndex = (writeIndex - delay + capacity) % capacity;
                readStarted = true;
                Debug.Log($"[🎵AudioInterceptor] Started reading from buffer (half-full at {availableSamples} samples)");
            }

            if (!readStarted) return false;

            int available = (writeIndex - readIndex + capacity) % capacity;
            if (available <= 0) return false;

            // Get all available samples
            audioData = new float[available];
            for (int i = 0; i < available; i++)
            {
                audioData[i] = ringBuffer[(readIndex + i) % capacity];
            }

            readIndex = (readIndex + available) % capacity;
            return true;
        }
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

        // Subscribe to audio chunks
        targetAudioSourceBridge.OnWebRTCAudioReady += HandleChunk;

        // Reset ring buffer state
        lock (bufferLock)
        {
            writeIndex = 0;
            readIndex = 0;
            availableSamples = 0;
            readStarted = false;
        }

        // Start sending audio to WebRTC (via Update)
        isStreamingActive = true;

        Debug.Log("[🎵AudioInterceptor] Audio streaming started with ring buffer (Update-based)");
    }

    public void StopAudioStreaming()
    {
        isStreamingActive = false;

        if (targetAudioSourceBridge != null)
        {
            targetAudioSourceBridge.OnWebRTCAudioReady -= HandleChunk;
        }

        Debug.Log("[🎵AudioInterceptor] Audio streaming stopped");
    }

    #endregion
    

}

