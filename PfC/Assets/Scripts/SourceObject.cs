using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class SourceObject : MonoBehaviour
{
    [SerializeField] NdiReceiver receiver;
    [SerializeField] MeshRenderer screenGameObject;
    
    void Start() {
        BroadcastPipelineManager.Instance?.RegisterSource(this);
    }
    
    void OnDestroy() {
        BroadcastPipelineManager.Instance?.UnregisterSource(this);
    }

    public void OnSourceClicked()
    {
        BroadcastPipelineManager.Instance?.OnSourceClicked(this);
    }
}
