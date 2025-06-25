using UnityEngine;
using BroadcastPipeline;
using Unity.Netcode;

public class StreamManager : MonoBehaviour
{
    [Header("References")]
    public BroadcastPipelineManager broadcastPipelineManager;
    
    private void Start()
    {
        NetworkStreamCoordinator.OnStreamControlChanged += HandleStreamControlChange;
    }
    
    private void OnDestroy()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= HandleStreamControlChange;
    }
    
    private void HandleStreamControlChange(StreamAssignment assignment, string description)
    {
        if (!assignment.isActive)
        {
            FallbackToLocal(assignment.pipelineType);
            return;
        }
        
        bool isMyStream = NetworkManager.Singleton?.LocalClientId == assignment.directorClientId;
        
        if (isMyStream)
            StartStreaming(assignment.pipelineType, assignment.streamSourceName);
        else
            StartReceiving(assignment.pipelineType, assignment.streamSourceName);
    }
    
    #region Stream Implementation
    
    private void StartStreaming(PipelineType pipeline, string sourceIdentifier)
    {
        Debug.Log($"xx_[STREAM] 🚀 START streaming {sourceIdentifier} to {pipeline}");
        // TODO: Implement WebRTC streaming start
        // Keep local assignment as-is (director sees their own source)
    }
    
    private void StartReceiving(PipelineType pipeline, string sourceIdentifier)
    {
        Debug.Log($"xx_[STREAM] 📡 START receiving {sourceIdentifier} for {pipeline}");
        // TODO: Implement WebRTC receiving start
        OverrideLocalPipelineWithNetworkStream(pipeline, sourceIdentifier);
    }
    
    private void FallbackToLocal(PipelineType pipeline)
    {
        Debug.Log($"xx_[STREAM] 🔄 FALLBACK to local for {pipeline}");
        // TODO: Stop WebRTC reception
        RestoreLocalPipelineAssignment(pipeline);
    }
    
    #endregion
    
    #region Pipeline Integration
    
    private void OverrideLocalPipelineWithNetworkStream(PipelineType pipeline, string sourceIdentifier)
    {
        Debug.Log($"xx_[PIPELINE] 🔀 {pipeline} - Override with network stream from {sourceIdentifier}");
        // TODO: Replace NDI input with WebRTC stream input
        // Connect incoming WebRTC stream to the renderer
    }
    
    private void RestoreLocalPipelineAssignment(PipelineType pipeline)
    {
        Debug.Log($"xx_[PIPELINE] 🔄 {pipeline} - Restore local assignment");
        // TODO: Return to local NDI source
        // Use whatever is in BroadcastPipelineManager.activeAssignments[pipeline]
    }
    
    #endregion
}