using System.Collections;
using System.Collections.Generic;
using BroadcastPipeline;
using Klak.Ndi;
using UnityEngine;

/// <summary>
/// Source object that provides texture directly instead of NDI stream.
/// NOTE: The inherited 'receiver' field from SourceObject is NOT USED - leave it unassigned in inspector.
/// This class uses the mesh material texture directly via GetTexture().
/// </summary>
public class TextureSourceObject : SourceObject
{
    // NOTE: 'receiver' field is inherited from SourceObject but UNUSED in this class
    // Do not assign it in the inspector - this class gets texture from screenGameObject.material.mainTexture

    /// <summary>
    /// Returns the texture from the mesh material instead of from NDI receiver
    /// </summary>
    public Texture GetTexture()
    {
        if (screenGameObject == null || screenGameObject.material == null)
        {
            Debug.LogWarning($"[TextureSourceObject] screenGameObject or material is null");
            return null;
        }

        // Get texture from _BaseMap shader property
        Texture texture = screenGameObject.material.GetTexture("_BaseMap");
        Debug.Log($"[TextureSourceObject] GetTexture() called - texture from _BaseMap: {texture != null}");
        if (texture != null)
        {
            Debug.Log($"[TextureSourceObject] Texture details - name: {texture.name}, size: {texture.width}x{texture.height}");
        }
        return texture;
    }

    protected override void Start() {
        highlightStrategy = new MaterialHighlightStrategy(screenGameObject, BroadcastPipelineManager.Instance);

        // For texture sources, set a descriptive name instead of using receiver.ndiName
        //ndiName = $"Texture_{gameObject.name}";
        BroadcastPipelineManager.Instance?.RegisterSource(this);
    }

    // Override click methods to avoid accessing receiver (which is null for texture sources)

    public override void OnSourceLeftClicked()
    {
        // Don't update ndiName from receiver since we don't have one
        Debug.Log($"Left clicked texture source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceLeftClicked(this);
    }

    public override void OnSourceRightClicked()
    {
        // Don't update ndiName from receiver since we don't have one
        Debug.Log($"Right clicked texture source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceRightClicked(this);
    }
}