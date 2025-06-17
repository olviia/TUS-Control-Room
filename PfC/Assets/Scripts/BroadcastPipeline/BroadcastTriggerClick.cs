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
    
    void Awake()
    {
        xriActions = new XRIDefaultInputActions();
        leftTriggerAction = xriActions.XRILeftHandInteraction.Activate;
        rightTriggerAction = xriActions.XRIRightHandInteraction.Activate;
    }
    void Start()
    {
        // Find ALL XRRayInteractor components, including inactive ones
        XRRayInteractor[] allRays = FindObjectsOfType<XRRayInteractor>(true);

        foreach (var ray in allRays)
        {
            Debug.Log($"Ray: {ray.name} on parent: {ray.transform.parent?.name}");

            // Check by name patterns (case insensitive)
            string rayName = ray.name.ToLower();
            string parentName = ray.transform.parent?.name.ToLower() ?? "";

            if (rayName.Contains("left") || parentName.Contains("left"))
            {
                leftHandRay = ray;
                Debug.Log($"Assigned LEFT ray: {ray.name}");
            }
            else if (rayName.Contains("right") || parentName.Contains("right"))
            {
                rightHandRay = ray;
                Debug.Log($"Assigned RIGHT ray: {ray.name}");
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
        GetComponentInParent<SourceObject>()?.OnSourceRightClicked();
        GetComponentInParent<PipelineDestination>()?.OnDestinationRightClicked();
    }
    private void HandleLeftClick()
    {
        GetComponentInParent<SourceObject>()?.OnSourceLeftClicked();
        GetComponentInParent<PipelineDestination>()?.OnDestinationLeftClicked();
    }
}
