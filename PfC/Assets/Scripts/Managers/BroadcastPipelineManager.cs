using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BroadcastPipelineManager : MonoBehaviour 
{
    [Header("Studio Pipeline")]
    public MeshRenderer studioPreview;
    public MeshRenderer studioOutput;

    [Header("TV Pipeline")]  
    public MeshRenderer tvPreview;
    public MeshRenderer tvOutput;
    public static BroadcastPipelineManager Instance { get; private set; }
    private List<SourceObject> registeredSources = new List<SourceObject>();

    private void Awake()
    {
        Instance = this;
    }
    
    public void RegisterSource(SourceObject source) 
    {
        registeredSources.Add(source);
    }
    public void UnregisterSource(SourceObject source) 
    {
        registeredSources.Remove(source);
    }

    public void OnSourceClicked(SourceObject source)
    {
        Debug.Log($"Pipeline Manager received click from: {source.gameObject.name}");
    }
}
