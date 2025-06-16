using Klak.Ndi;
using Unity.VisualScripting;
using UnityEngine;

namespace BroadcastPipeline
{
    public class PipelineDestination: MonoBehaviour
    {
        public NdiReceiver receiver;
        public PipelineType pipelineType;

        void Start()
        {
            BroadcastPipelineManager.Instance?.RegisterDestination(this);
        }
        void OnDestroy()
        {
            BroadcastPipelineManager.Instance?.UnregisterDestination(this);
        }
        public void OnDestinationLeftClicked()
        {
            Debug.Log($"Left clicked destination: {pipelineType}");
            BroadcastPipelineManager.Instance?.OnDestinationLeftClicked(this);
        }

        public void OnDestinationRightClicked()
        {
            Debug.Log($"Right clicked destination: {pipelineType}");
            BroadcastPipelineManager.Instance?.OnDestinationRightClicked(this);
        }
    }
}