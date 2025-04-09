using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Text.RegularExpressions;

public class SimpleVideoStreamer : NetworkBehaviour
{
    public MeshRenderer displayRenderer;
    public float frameInterval = 0.1f; //10 fps

    //quality of compression
    public int quality = 75;

    //ID of stream to be able to stream different videos
    public int streamId = 0;

    private Texture2D videoTexture;

    private RenderTexture captureRT;
    private float timer;

    private bool isStreaming = false;

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
            // Resolution here can be changed
            videoTexture = new Texture2D(1920, 1080);
            isStreaming = true;
        }
        else
        {
            // Initialize as client
            // texture resolution is a placeholder
            videoTexture = new Texture2D(2, 2);
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
        Texture sourceTexture = displayRenderer.material.mainTexture;

        if (sourceTexture == null)
        {
            Debug.LogWarning("Could not find source texture on material");
            return;
        }
        //2. cupture texture
        Texture2D frameTexture = CaptureTextureContent(sourceTexture);
        // 2. Compress to JPEG (maybe there should be PNG? but it might be more difficult)
        byte[] frameData = frameTexture.EncodeToJPG(quality);

        // 3. Send to clients
        SendFrameClientRpc(frameData, streamId);
    }
    private Texture2D CaptureTextureContent(Texture sourceTexture)
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

            if (videoTexture != null)
            {
                Destroy(videoTexture);
            }

            videoTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGB24, false);
        }

        // Copy source texture to render texture
        Graphics.Blit(sourceTexture, captureRT);

        // Read pixels from render texture to texture2D
        RenderTexture previousRT = RenderTexture.active;
        RenderTexture.active = captureRT;
        videoTexture.ReadPixels(new Rect(0, 0, captureRT.width, captureRT.height), 0, 0);
        videoTexture.Apply();

        RenderTexture.active = previousRT;

        return videoTexture;
    }

    // Client RPC to send frame data to clients
    [ClientRpc]
    void SendFrameClientRpc(byte[] frameData, int streamId)
    {
        if (IsClient && !IsServer) // Only process on clients
        {
            // Update the texture with the received frame
            // Load image data
            videoTexture.LoadImage(frameData);
            videoTexture.Apply();
        }
    }

}
