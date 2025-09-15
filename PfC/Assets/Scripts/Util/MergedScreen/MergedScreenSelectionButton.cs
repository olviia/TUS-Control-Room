using System;
using BroadcastPipeline;
using Unity.Android.Gradle.Manifest;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Util.MergedScreen;

public class MergedScreenSelectionButton : MonoBehaviour, IMergedScreenSelectionButton, IPointerClickHandler
{
    public string sceneName;

    public Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }


    void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(0)) // Left click
        {
            HandleLeftClick();
        }
        else if (Input.GetMouseButtonDown(1)) // Right click  
        {
            HandleRightClick();
        }
    }
//add proper trigger

    private void OnLeftTriggerPressed(InputAction.CallbackContext context)
    {
        Debug.LogWarning("clicked left");
        // CheckRayHit(leftHandRay, "left");
    }

    private void OnRightTriggerPressed(InputAction.CallbackContext context)
    {
        Debug.LogWarning("clicked right");
        //  CheckRayHit(rightHandRay, "right");
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            HandleLeftClick();
        else if (eventData.button == PointerEventData.InputButton.Right)
            HandleRightClick();
    }

    // private void OnButtonClicked()
    // {
    //     IMergedScreenSelectionButton.TriggerEvent(sceneName, this);
    // }

    public void HandleLeftClick()
    {
        Debug.Log($"Left clicked source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceLeftClicked(GetComponent<MergedScreenSource>());
    }

    public void HandleRightClick()
    {
        Debug.Log($"Right clicked source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceRightClicked(GetComponent<MergedScreenSource>());
    }

    //public static event Action<string, IMergedScreenSelectionButton> OnMergedScreenButtonClicked;
}