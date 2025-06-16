using System;
using System.Collections;
using System.Collections.Generic;
using BroadcastPipeline;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

public class ClickForwarder : MonoBehaviour
{
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
