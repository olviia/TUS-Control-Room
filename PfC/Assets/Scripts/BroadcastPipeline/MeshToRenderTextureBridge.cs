using UnityEngine;
using Klak.Ndi;

/// <summary>
/// Bridge that gets current TVLive textures from BroadcastPipelineManager and blits to output.
/// BroadcastPipelineManager calls SetSourceTexture() or SetSourceReceiver() when TVLive is assigned.
/// </summary>
public class MeshToRenderTextureBridge : MonoBehaviour
{
    [Header("Overlay Source (Optional)")]
    public NdiReceiver ndiReceiverOverlay;

    [Header("Output")]
    public RenderTexture outputRenderTexture;

    // Current source (set by BroadcastPipelineManager)
    private Texture currentTexture;
    private NdiReceiver currentReceiver;

    // Same as WebRTCStreamer
    private RenderTexture compositeRT;
    private Material blendMaterial;

    void Start()
    {
        // Same shader as WebRTCStreamer line 99
        blendMaterial = new Material(Shader.Find("Custom/BlendTwoTextures"));
    }

    void LateUpdate()
    {
        // Get main texture from current source
        Texture mainTexture = GetMainTexture();

        if (mainTexture != null && outputRenderTexture != null)
        {
            // Get NDI overlay (always on top)
            Texture overlayTexture = ndiReceiverOverlay?.GetTexture();

            // Lines 437-448: Try compositing if overlay is available
            if (overlayTexture != null)
            {
                if (compositeRT == null)
                {
                    compositeRT = new RenderTexture(mainTexture.width, mainTexture.height, 0);
                    compositeRT.Create();
                }

                blendMaterial.SetTexture("_MainTex", mainTexture);
                blendMaterial.SetTexture("_OverlayTex", overlayTexture);
                Graphics.Blit(null, compositeRT, blendMaterial);
                Graphics.Blit(compositeRT, outputRenderTexture);
            }
            else
            {
                // Line 453: Direct blit - no overlay
                Graphics.Blit(mainTexture, outputRenderTexture);
            }
        }
    }

    /// <summary>
    /// Called by BroadcastPipelineManager when TVLive gets a Texture source
    /// </summary>
    public void SetSourceTexture(Texture texture)
    {
        currentTexture = texture;
        currentReceiver = null;
        Debug.Log($"[Bridge] Source set to Texture: {texture?.name}");
    }

    /// <summary>
    /// Called by BroadcastPipelineManager when TVLive gets an NDI source
    /// </summary>
    public void SetSourceReceiver(NdiReceiver receiver)
    {
        currentReceiver = receiver;
        currentTexture = null;
        Debug.Log($"[Bridge] Source set to NDI: {receiver?.ndiName}");
    }

    /// <summary>
    /// Get main texture from current source
    /// </summary>
    private Texture GetMainTexture()
    {
        // Priority 1: Direct texture (from TextureSourceObject)
        if (currentTexture != null)
        {
            return currentTexture;
        }

        // Priority 2: NDI receiver
        if (currentReceiver != null)
        {
            return currentReceiver.GetTexture();
        }

        return null;
    }

    void OnDestroy()
    {
        if (compositeRT != null)
        {
            compositeRT.Release();
            Destroy(compositeRT);
        }
        if (blendMaterial != null)
        {
            Destroy(blendMaterial);
        }
    }
}