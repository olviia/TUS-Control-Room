using System.Collections;
using System.Collections.Generic;
using DefaultNamespace;
using UnityEngine;

public class ClickForwarder : MonoBehaviour
{
    void OnMouseOver()
    {
        if(Input.GetMouseButtonDown(0)) // Left click
        {
            GetComponentInParent<SourceObject>()?.OnSourceLeftClicked();
            GetComponentInParent<PipelineDestination>()?.OnDestinationLeftClicked();
        }
        else if(Input.GetMouseButtonDown(1)) // Right click  
        {
            GetComponentInParent<SourceObject>()?.OnSourceRightClicked();
            GetComponentInParent<PipelineDestination>()?.OnDestinationRightClicked();
        }
    }
}
