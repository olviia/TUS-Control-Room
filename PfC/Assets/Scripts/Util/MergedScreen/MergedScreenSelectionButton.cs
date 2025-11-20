using System;
using BroadcastPipeline;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Util.MergedScreen;

public class MergedScreenSelectionButton : MonoBehaviour, IMergedScreenSelectionButton, IPointerClickHandler
{
    public string sceneName;

    public Button button;
    

    private void Awake()
    {
        button = GetComponent<Button>();
        
    }

    public void OnPointerClick(PointerEventData eventData)
    {

        // for mouse input
        if (eventData.pointerId == 0) // Mouse
        {

            if (eventData.button == PointerEventData.InputButton.Left)
            {
                HandleLeftClick();
            } else if (eventData.button == PointerEventData.InputButton.Right)
            {
                HandleRightClick();
            }
        }
        // XR Controllers - check which ray interactor is hitting
        else if (eventData.pointerId > 0)
        {
            var rayInteractors = FindObjectsOfType<XRRayInteractor>();
            foreach (var ray in rayInteractors)
            {
                if (ray.TryGetCurrentUIRaycastResult(out var hit) && 
                    hit.gameObject == this.gameObject)
                {
                    // This is the key - Unity names them predictably!
                    string name = ray.transform.name.ToLower();
                    
                    if (name.Contains("left"))
                        HandleLeftClick();
                    else if (name.Contains("right"))
                        HandleRightClick();
                    
                    break;
                }
            }
        }
    }


    public void HandleLeftClick()
    {
        BroadcastPipelineManager.Instance?.OnSourceLeftClicked(GetComponent<MergedScreenSource>());
    }

    public void HandleRightClick()
    {
        BroadcastPipelineManager.Instance?.OnSourceRightClicked(GetComponent<MergedScreenSource>());
    }

    //public static event Action<string, IMergedScreenSelectionButton> OnMergedScreenButtonClicked;
}