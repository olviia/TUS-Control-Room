using System.Collections;
using System.Collections.Generic;
using BroadcastPipeline;
using Klak.Ndi;
using UnityEngine;

public class SourceObject : MonoBehaviour, IPipelineSource
{
    //string name
    public NdiReceiver receiver;
    public MeshRenderer screenGameObject;
    private IHighlightStrategy highlightStrategy;
    public string ndiName { get; private set; }

    
    
    
    void Start() {
        highlightStrategy = new MaterialHighlightStrategy(screenGameObject, BroadcastPipelineManager.Instance);

        ndiName = receiver.ndiName;
        BroadcastPipelineManager.Instance?.RegisterSource(this);
    }
    
    void OnDestroy() {
        BroadcastPipelineManager.Instance?.UnregisterSource(this);
    }

    public void OnSourceLeftClicked()
    {
        Debug.Log($"Left clicked source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceLeftClicked(this);
    }

    public void OnSourceRightClicked()
    {
        Debug.Log($"Right clicked source: {gameObject.name}");
        BroadcastPipelineManager.Instance?.OnSourceRightClicked(this);
    }


    public void ApplyHighlight(PipelineType pipelineType)
    {
        highlightStrategy?.ApplyHighlight(pipelineType);        
    }

    public void RemoveHighlight()
    {
        highlightStrategy?.RemoveHighlight();
    }

    public void ApplyConflictHighlight()
    {
        highlightStrategy?.ApplyConflictHighlight();
    }

    public bool HasConflictingAssignments(List<PipelineType> assignments)
    {
        return highlightStrategy?.HasConflictingAssignments(assignments) ?? false;
    }
    public void Cleanup()
    {
        BroadcastPipelineManager.Instance?.UnregisterSource(this);
    }
}
