using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Text.RegularExpressions;
using Unity.Services.Matchmaker.Models;
using Klak.Ndi;

public class SimpleVideoStreamer : NetworkBehaviour
{
    public MeshRenderer displayRenderer;
    public float frameInterval = 0.1f; //10 fps
    public NdiReceiver _receiver;

    //quality of compression
    [Range(1, 100)]
    public int quality = 75;

    //ID of stream to be able to stream different videos
    public int streamId = 0;
    // Added resolution control
    [Header("Resolution Settings")]
    [Tooltip("Width to downsample the video to before sending")]
    public int streamingWidth = 960; // Half of 1920
    [Tooltip("Height to downsample the video to before sending")]
    public int streamingHeight = 540; // Half of 1080

    // Max size for RPC messages in bytes (adjust based on my NetworkConfig)
    private const int MAX_MESSAGE_SIZE = 1024 * 1024; // 1MB default

    private Texture2D videoTexture;
    private Texture2D downsampledTexture;

    private RenderTexture captureRT;
    private RenderTexture downsampleRT;
    private float timer;

    private bool isStreaming = false;

    // Dictionary to store chunks for reassembly
    private Dictionary<int, byte[][]> frameChunks = new Dictionary<int, byte[][]>();
    private int frameCounter = 0;
    public void StartStreaming()
    {
        if(IsServer && !isStreaming)
        {
            isStreaming = true;
            Debug.Log($"Streaming started ID {streamId}");
        }
    }
    public void StopStreaming()
    {
        if (IsServer)
        {
            isStreaming = false;
            Debug.Log($"Streaming stopped ID {streamId}");
        }
    }
    // Initialize
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Initialize as server
            videoTexture = new Texture2D(1920, 1080);
            downsampledTexture = new Texture2D(streamingWidth, streamingHeight, TextureFormat.RGB24, false);
            downsampleRT = new RenderTexture(streamingWidth, streamingHeight, 0, RenderTextureFormat.ARGB32);
            isStreaming = true;
        }
        else
        {
            // Initialize as client
            // texture resolution is a placeholder
            videoTexture = new Texture2D(streamingWidth, streamingHeight, TextureFormat.RGB24, false);
            displayRenderer.material.mainTexture = videoTexture;

        }
    }

    // Update is called once per frame
    //Maybe there should be fixed update?
    void Update()
    {
        if (IsServer && isStreaming)
        {
            timer += Time.deltaTime;

            // Send a frame at regular intervals
            if (timer >= frameInterval)
            {
                SendVideoFrame();
                timer = 0;
            }
        }
    }

    void SendVideoFrame()
    {
        //1. Get pixels from texture
        if(_receiver != null && _receiver.GetTexture() != null)
        {
            Texture sourceTexture = _receiver.GetTexture();

            if (sourceTexture == null)
            {
                Debug.LogWarning($"Could not find source texture on material: {displayRenderer.name}, {displayRenderer.material.name}");
                return;
            }
            try
            {
                //2. Capture and downsample texture
                Texture2D frameTexture = CaptureAndDownsampleTexture(sourceTexture);

                // 3. Compress to JPEG with reduced quality if needed
                byte[] frameData = frameTexture.EncodeToJPG(quality);

                // 4. Check if frame is too large for a single network message
                if (frameData.Length > MAX_MESSAGE_SIZE)
                {
                    // Split the frame into chunks and send
                    SendLargeFrame(frameData);
                }
                else
                {
                    // 5. Send to clients if size is acceptable
                    SendFrameClientRpc(frameData, streamId, 0, 1, frameCounter);
                    frameCounter++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing video frame: {e.Message}");
            }

        }
        else
        {
            Debug.LogWarning($"Could not find receiver or its texture: {_receiver.name}, {_receiver.GetTexture().name}");
            return;
        }
    }
    private void SendLargeFrame(byte[] frameData)
    {
        // Calculate how many chunks we need
        int chunkSize = MAX_MESSAGE_SIZE - 100; // Leave some headroom for RPC overhead
        int totalChunks = Mathf.CeilToInt((float)frameData.Length / chunkSize);

        Debug.Log($"Splitting frame {frameCounter} into {totalChunks} chunks");

        // Split the frame into chunks and send each chunk
        for (int i = 0; i < totalChunks; i++)
        {
            int startIndex = i * chunkSize;
            int length = Mathf.Min(chunkSize, frameData.Length - startIndex);

            byte[] chunk = new byte[length];
            System.Array.Copy(frameData, startIndex, chunk, 0, length);

            // Send this chunk to clients
            SendFrameClientRpc(chunk, streamId, i, totalChunks, frameCounter);
        }

        frameCounter++;
    }

    private Texture2D CaptureAndDownsampleTexture(Texture sourceTexture)
    {
        // Create or resize render texture if needed
        if (captureRT == null || captureRT.width != sourceTexture.width || captureRT.height != sourceTexture.height)
        {
            if (captureRT != null)
            {
                captureRT.Release();
                Destroy(captureRT);
            }

            captureRT = new RenderTexture(sourceTexture.width, sourceTexture.height, 0, RenderTextureFormat.ARGB32);
            captureRT.Create();
        }

        // Copy source texture to render texture
        Graphics.Blit(sourceTexture, captureRT);

        // Downsample to the streaming resolution
        Graphics.Blit(captureRT, downsampleRT);

        // Read pixels from downsampled render texture to texture2D
        RenderTexture previousRT = RenderTexture.active;
        RenderTexture.active = downsampleRT;
        downsampledTexture.ReadPixels(new Rect(0, 0, downsampleRT.width, downsampleRT.height), 0, 0);
        downsampledTexture.Apply();

        RenderTexture.active = previousRT;

        return downsampledTexture;
    }

    // Client RPC to send frame data to clients
    [ClientRpc]
    void SendFrameClientRpc(byte[] frameData, int streamId, int chunkIndex, int totalChunks, int frameId)
    {
        if (!IsClient || IsServer) return; // Only process on clients
        try
        {
            // If this is a single-chunk frame
            if (totalChunks == 1)
            {
                // Update the texture with the received frame
                videoTexture.LoadImage(frameData);
                videoTexture.Apply();
                return;
            }

            // Handle multi-chunk frames
            if (!frameChunks.ContainsKey(frameId))
            {
                frameChunks[frameId] = new byte[totalChunks][];
            }

            // Store this chunk
            frameChunks[frameId][chunkIndex] = frameData;

            // Check if we have all chunks for this frame
            bool isFrameComplete = true;
            int totalSize = 0;

            for (int i = 0; i < totalChunks; i++)
            {
                if (frameChunks[frameId][i] == null)
                {
                    isFrameComplete = false;
                    break;
                }
                totalSize += frameChunks[frameId][i].Length;
            }

            // If frame is complete, reassemble and display
            if (isFrameComplete)
            {
                byte[] completeFrame = new byte[totalSize];
                int offset = 0;

                for (int i = 0; i < totalChunks; i++)
                {
                    System.Array.Copy(frameChunks[frameId][i], 0, completeFrame, offset, frameChunks[frameId][i].Length);
                    offset += frameChunks[frameId][i].Length;
                }

                // Update the texture with the reassembled frame
                videoTexture.LoadImage(completeFrame);
                videoTexture.Apply();

                // Remove from dictionary to free memory
                frameChunks.Remove(frameId);

                // Cleanup old frames to prevent memory leaks
                CleanupOldFrames(frameId);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error processing frame chunk: {e.Message}");
        }
    }

    // Cleanup old frame chunks to prevent memory buildup
    private void CleanupOldFrames(int currentFrameId)
    {
        List<int> oldFrames = new List<int>();

        // Find frames that are too old (more than 5 frames behind)
        foreach (var frameId in frameChunks.Keys)
        {
            if (frameId < currentFrameId - 5)
            {
                oldFrames.Add(frameId);
            }
        }

        // Remove old frames
        foreach (var frameId in oldFrames)
        {
            frameChunks.Remove(frameId);
        }

        if (oldFrames.Count > 0)
        {
            Debug.Log($"Cleaned up {oldFrames.Count} incomplete old frames");
        }
    }
}
