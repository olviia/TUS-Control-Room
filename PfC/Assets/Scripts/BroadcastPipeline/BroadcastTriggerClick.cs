using System;
using System.Collections;
using System.Collections.Generic;
using BroadcastPipeline;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class BroadcastTriggerClick : MonoBehaviour
{
    private XRRayInteractor leftHandRay;
    private XRRayInteractor rightHandRay;
    private XRIDefaultInputActions xriActions;
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
    
    // NEW: Reference to click controller
    private ISourceClickController clickController;
    
    void Awake()
    {
        xriActions = new XRIDefaultInputActions();
        leftTriggerAction = xriActions.XRILeftHandInteraction.Activate;
        rightTriggerAction = xriActions.XRIRightHandInteraction.Activate;
    }
    
    void Start()
    {
        // NEW: Get reference to click controller
        clickController = BroadcastPipelineManager.Instance as ISourceClickController;
        
        // Find ALL XRRayInteractor components, including inactive ones
        XRRayInteractor[] allRays = FindObjectsOfType<XRRayInteractor>(true);

        foreach (var ray in allRays)
        {
            // Check by name patterns (case insensitive)
            string rayName = ray.name.ToLower();
            string parentName = ray.transform.parent?.name.ToLower() ?? "";

            if (rayName.Contains("left") || parentName.Contains("left"))
            {
                leftHandRay = ray;
            }
            else if (rayName.Contains("right") || parentName.Contains("right"))
            {
                rightHandRay = ray;
            }
        }

        if (leftHandRay == null) Debug.LogError("Left hand ray not found!");
        if (rightHandRay == null) Debug.LogError("Right hand ray not found!");
    }
    
    void OnEnable()
    {
        xriActions.Enable();
        leftTriggerAction.performed += OnLeftTriggerPressed;
        rightTriggerAction.performed += OnRightTriggerPressed;
    }
    
    void OnDisable()
    {
        leftTriggerAction.performed -= OnLeftTriggerPressed;
        rightTriggerAction.performed -= OnRightTriggerPressed;
        xriActions.Disable();
    }
    
    void OnMouseOver()
    {
        if(Input.GetMouseButtonDown(0)) // Left click
        {
            HandleLeftClick();
        }
        else if(Input.GetMouseButtonDown(1)) // Right click  
        {
            HandleRightClick();
        }
    }
        
    private void OnLeftTriggerPressed(InputAction.CallbackContext context)
    {
        Debug.LogWarning("clicked left");
        CheckRayHit(leftHandRay, "left");
    }
    
    private void OnRightTriggerPressed(InputAction.CallbackContext context)
    {
        Debug.LogWarning("clicked right");
        CheckRayHit(rightHandRay, "right");
    }
    
    private void CheckRayHit(XRRayInteractor rayInteractor, String hand)
    {
        if (rayInteractor == null) return;
        
        // Get the current raycast hit
        if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            // Check if we hit an object with our clickable tag
            if (hit.collider == this.GetComponent<Collider>())
            {
                Debug.Log($"XR Trigger pressed on clickable object: {hit.collider.name}");
                
                // Handle the click
                if (hand.Equals("left"))
                {
                    HandleLeftClick();
                }
                else if (hand.Equals("right"))
                {
                    HandleRightClick();
                }
            }
        }
    }
    
    private void HandleRightClick()
    {
        // Source right clicks always allowed (go to Studio Preview)
        GetComponentInParent<SourceObject>()?.OnSourceRightClicked();
        
        // NEW: Check if preview-to-live clicking is blocked for destinations
        var destination = GetComponentInParent<PipelineDestination>();
        if (destination != null)
        {
            // Right clicks on Studio Preview go to Studio Live - check if allowed
            if (destination.pipelineType == PipelineType.StudioPreview)
            {
                if (clickController != null && !clickController.IsPreviewToLiveClickAllowed())
                {
                    Debug.LogWarning($"xx_🔒 Preview-to-Live click blocked - Studio pipeline syncing");
                    return; // Block the click
                }
            }
            
            destination.OnDestinationRightClicked();
        }
    }
    
    private void HandleLeftClick()
    {
        // Source left clicks always allowed (go to TV Preview)
        GetComponentInParent<SourceObject>()?.OnSourceLeftClicked();
        
        // NEW: Check if preview-to-live clicking is blocked for destinations
        var destination = GetComponentInParent<PipelineDestination>();
        if (destination != null)
        {
            // Left clicks on TV Preview go to TV Live - check if allowed
            if (destination.pipelineType == PipelineType.TVPreview)
            {
                if (clickController != null && !clickController.IsPreviewToLiveClickAllowed())
                {
                    Debug.LogWarning($"xx_🔒 Preview-to-Live click blocked - TV pipeline syncing");
                    return; // Block the click
                }
            }
            
            destination.OnDestinationLeftClicked();
        }
    }
}