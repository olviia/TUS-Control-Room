using UnityEngine;
using BroadcastPipeline;
using System;
using System.Collections.Generic;

/// <summary>
/// Clean WebRTC audio receiver using only Unity DSP chain (no AudioSource.SetTrack)
/// </summary>
public class WebRTCAudioFilter : MonoBehaviour
{
    private PipelineType pipelineType;
    private float volumeMultiplier = 1.0f;
    private bool isInitialized = false;

    private Queue<float[]> audioChunkQueue = new Queue<float[]>();
    private float[] currentChunk;
    private int bufferPosition = 0;
    private readonly object queueLock = new object();

    private const int MaxQueueSize = 12; // Roughly 0.25s of audio if using 1024 chunks
    private const float SilenceThreshold = 0.0001f;

    public void Initialize(PipelineType pipeline, float volume)
    {
        pipelineType = pipeline;
        volumeMultiplier = Mathf.Clamp01(volume);
        isInitialized = true;

        Debug.Log($"[W][🎵WebRTC-{pipelineType}] Initialized audio filter with volume {volumeMultiplier:F2}");
    }

    public void SetVolume(float volume)
    {
        volumeMultiplier = Mathf.Clamp01(volume);
        Debug.Log($"[W][🎵WebRTC-{pipelineType}] Volume set to {volumeMultiplier:F2}");
    }

    public void ReceiveAudioChunk(float[] audioData, int channels, int sampleRate)
    {
        lock (queueLock)
        {
            float[] chunk = new float[audioData.Length];
            Array.Copy(audioData, chunk, audioData.Length);
            audioChunkQueue.Enqueue(chunk);

            while (audioChunkQueue.Count > MaxQueueSize)
                audioChunkQueue.Dequeue(); // Prevent memory bloat
        }

        if (audioData.Length > 0)
        {
            Debug.Log($"[W][🎵WebRTC-{pipelineType}] Chunk received: {audioData.Length} samples");
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!isInitialized)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        lock (queueLock)
        {
            int outputPos = 0;

            while (outputPos < data.Length)
            {
                if (currentChunk == null || bufferPosition >= currentChunk.Length)
                {
                    if (audioChunkQueue.Count > 0)
                    {
                        currentChunk = audioChunkQueue.Dequeue();
                        bufferPosition = 0;
                    }
                    else
                    {
                        break; // No data: exit early, will fill silence below
                    }
                }

                int samplesToCopy = Mathf.Min(data.Length - outputPos, currentChunk.Length - bufferPosition);
                Array.Copy(currentChunk, bufferPosition, data, outputPos, samplesToCopy);

                for (int i = outputPos; i < outputPos + samplesToCopy; i++)
                    data[i] *= volumeMultiplier;

                outputPos += samplesToCopy;
                bufferPosition += samplesToCopy;
            }

            if (outputPos < data.Length)
                Array.Clear(data, outputPos, data.Length - outputPos);
        }
    }

    void OnDestroy()
    {
        if (isInitialized)
            Debug.Log($"[W][🎵WebRTC-{pipelineType}] Filter destroyed");
    }
}
