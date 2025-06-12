using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class SourceObject : MonoBehaviour
{
    public NdiReceiver receiver;
    public MeshRenderer screenGameObject;
    
    void Start() {
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
}
