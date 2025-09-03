using BroadcastPipeline;

/// <summary>
/// Interface for controlling source click blocking during network synchronization
/// </summary>
public interface ISourceClickController
{
    /// <summary>
    /// Check if preview-to-live clicking is currently allowed
    /// </summary>
    bool IsPreviewToLiveClickAllowed();
    
    /// <summary>
    /// Block preview-to-live clicks during network synchronization
    /// </summary>
    void BlockPreviewToLiveClicks(PipelineType pipelineType);
    
    /// <summary>
    /// Unblock clicks when network sync is complete
    /// </summary>
    void UnblockPreviewToLiveClicks(PipelineType pipelineType);
}