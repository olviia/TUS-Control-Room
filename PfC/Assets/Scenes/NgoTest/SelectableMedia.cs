using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine.XR.Interaction.Toolkit;

// This component is attached to each selectable media object
public class SelectableMedia : XRSimpleInteractable
{
    [SerializeField] private Renderer mediaRenderer;


    // Reference to the texture sync component
    [SerializeField] private RuntimeTextureSync textureSync;

    private void Awake()
    {
        // If no renderer is assigned, use this object's renderer
        if (mediaRenderer == null)
        {
            mediaRenderer = GetComponent<Renderer>();
        }

        // Set up the interaction event
        selectEntered.AddListener(OnSelected);

        // Find the texture sync component if not assigned
        if (textureSync == null)
        {
            textureSync = FindObjectOfType<RuntimeTextureSync>();
        }
    }


    // Called when this object is selected by an interactor
    private void OnSelected(SelectEnterEventArgs args)
    {
        // Send the selected renderer's texture to the network sync
        if (textureSync != null && mediaRenderer != null)
        {
            textureSync.CopyTextureFromRenderer(mediaRenderer);
        }
    }
}



