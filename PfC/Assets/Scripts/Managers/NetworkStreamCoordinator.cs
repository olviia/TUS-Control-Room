using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections;
using BroadcastPipeline;
using Klak.Ndi;

[System.Serializable]
public struct StreamAssignment : INetworkSerializable
{
    public ulong directorClientId;
    public string streamSourceName;  // NDI source name (includes computer name)
    public PipelineType pipelineType;
    public bool isActive;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref directorClientId);
        serializer.SerializeValue(ref streamSourceName);
        serializer.SerializeValue(ref pipelineType);
        serializer.SerializeValue(ref isActive);
    }
}

public class NetworkStreamCoordinator : NetworkBehaviour
{
    [Header("Event References")]
    public BroadcastPipelineManager localPipelineManager;
    
    // Network state for live streams only
    private NetworkVariable<StreamAssignment> studioLiveStream;
    private NetworkVariable<StreamAssignment> tvLiveStream;
    
    // Events for UI feedback
    public static event Action<StreamAssignment, string> OnStreamControlChanged;
    
    private bool isNetworkReady = false;

    private void Awake()
    {
        Debug.Log("xx_🔧 NetworkStreamCoordinator Awake()");
        
        // Initialize NetworkVariables with proper permissions
        studioLiveStream = new NetworkVariable<StreamAssignment>(
            new StreamAssignment { isActive = false },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
        
        tvLiveStream = new NetworkVariable<StreamAssignment>(
            new StreamAssignment { isActive = false },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        Debug.Log($"xx_🔧 NetworkStreamCoordinator.OnNetworkSpawn()");
        Debug.Log($"xx_🔧   My ClientId: {NetworkManager.Singleton?.LocalClientId}");
        Debug.Log($"xx_🔧   IsServer: {IsServer}");
        Debug.Log($"xx_🔧   IsClient: {IsClient}");
        Debug.Log($"xx_🔧   IsHost: {IsHost}");
        Debug.Log($"xx_🔧   IsSpawned: {IsSpawned}");
        
        // Verify client ID is valid before doing anything
        if (NetworkManager.Singleton?.LocalClientId == null)
        {
            Debug.LogError("xx_🔧 ❌ LocalClientId is null during spawn!");
            return;
        }
        
        // Subscribe to network variable changes only after spawn
        studioLiveStream.OnValueChanged += OnStudioLiveStreamChanged;
        tvLiveStream.OnValueChanged += OnTvLiveStreamChanged;
        
        // Wait a moment for network to stabilize before being ready
        StartCoroutine(DelayedNetworkReady());
    }

    private IEnumerator DelayedNetworkReady()
    {
        yield return new WaitForSeconds(0.5f); // Wait for network to stabilize
        
        isNetworkReady = true;
        Debug.Log("xx_🔧 ✅ NetworkStreamCoordinator ready for RPCs");
        
        if (localPipelineManager != null)
        {
            SubscribeToLocalPipelineEvents();
        }
    }

    #region Public Interface for Directors

    /// <summary>
    /// Called when a director wants to stream their local source to a live pipeline
    /// </summary>
    public void RequestStreamControl(PipelineType pipeline, NdiReceiver localNdiSource)
    {
        if (!isNetworkReady)
        {
            Debug.LogWarning("xx_🔧 NetworkStreamCoordinator not ready yet - delaying request");
            StartCoroutine(DelayedRequestStreamControl(pipeline, localNdiSource));
            return;
        }
        
        // Check if we're properly networked
        if (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("xx_Not connected to network - operating in local mode only");
            return;
        }
        
        if (pipeline != PipelineType.StudioLive && pipeline != PipelineType.TVLive)
        {
            Debug.LogWarning($"xx_Can only stream to Live pipelines, not {pipeline}");
            return;
        }

        string sourceIdentifier = GetSourceIdentifier(localNdiSource);
        Debug.Log($"xx_Director requesting control of {pipeline} with source: {sourceIdentifier}");
        
        // Get local client ID more safely
        ulong localClientId = GetLocalClientId();
        if (localClientId != 0)
        {
            RequestStreamControlServerRpc(pipeline, sourceIdentifier, localClientId);
        }
        else
        {
            Debug.LogError("xx_Failed to get local client ID");
        }
    }

    private IEnumerator DelayedRequestStreamControl(PipelineType pipeline, NdiReceiver localNdiSource)
    {
        // Wait until properly ready
        while (!isNetworkReady)
        {
            yield return new WaitForSeconds(0.1f);
        }
        
        // Try again
        RequestStreamControl(pipeline, localNdiSource);
    }

    /// <summary>
    /// Called when a director wants to stop their stream
    /// </summary>
    public void ReleaseStreamControl(PipelineType pipeline)
    {
        if (!isNetworkReady)
        {
            Debug.LogWarning("xx_NetworkStreamCoordinator not ready yet");
            return;
        }
        
        // Check if we're properly networked
        if (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)
        {
            Debug.LogWarning("xx_Not connected to network - operating in local mode only");
            return;
        }
        
        ulong localClientId = GetLocalClientId();
        if (localClientId != 0)
        {
            ReleaseStreamControlServerRpc(pipeline, localClientId);
        }
        else
        {
            Debug.LogError("xx_Failed to get local client ID for release");
        }
    }

    #endregion

    #region Server Authority

    [ServerRpc(RequireOwnership = false)]
    private void RequestStreamControlServerRpc(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
        Debug.Log($"xx_[SERVER] Client {requestingClientId} requesting {pipeline} control with {sourceIdentifier}");
        
        StreamAssignment newAssignment = new StreamAssignment
        {
            directorClientId = requestingClientId,
            streamSourceName = sourceIdentifier,
            pipelineType = pipeline,
            isActive = true
        };

        // Update the appropriate network variable (last director wins)
        switch (pipeline)
        {
            case PipelineType.StudioLive:
                studioLiveStream.Value = newAssignment;
                break;
            case PipelineType.TVLive:
                tvLiveStream.Value = newAssignment;
                break;
        }
        
        Debug.Log($"xx_[SERVER] Assigned {pipeline} to client {requestingClientId}");
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseStreamControlServerRpc(PipelineType pipeline, ulong requestingClientId)
    {
        Debug.Log($"xx_[SERVER] Client {requestingClientId} requesting to release {pipeline}");
        
        // Only allow the current controller to release
        NetworkVariable<StreamAssignment> targetStream = GetNetworkVariableForPipeline(pipeline);
        
        if (targetStream != null && targetStream.Value.directorClientId == requestingClientId)
        {
            StreamAssignment emptyAssignment = new StreamAssignment
            {
                directorClientId = 0,
                streamSourceName = "",
                pipelineType = pipeline,
                isActive = false
            };
            
            targetStream.Value = emptyAssignment;
            Debug.Log($"xx_[SERVER] Released {pipeline} from client {requestingClientId}");
        }
        else
        {
            Debug.LogWarning($"xx_[SERVER] Client {requestingClientId} tried to release {pipeline} but doesn't own it");
        }
    }

    #endregion

    #region Network Variable Callbacks

    private void OnStudioLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        HandleStreamChange(previousValue, newValue, "Studio Live");
    }

    private void OnTvLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        HandleStreamChange(previousValue, newValue, "TV Live");
    }

    private void HandleStreamChange(StreamAssignment previousValue, StreamAssignment newValue, string pipelineName)
    {
        Debug.Log($"xx_[CLIENT] {pipelineName} stream changed:");
        Debug.Log($"xx_  Previous: {GetStreamDescription(previousValue)}");
        Debug.Log($"xx_  New: {GetStreamDescription(newValue)}");
        
        // Get local client ID safely
        ulong localClientId = GetLocalClientId();
        
        // Determine what this client should do
        bool wasStreaming = previousValue.isActive && previousValue.directorClientId == localClientId;
        bool shouldStream = newValue.isActive && newValue.directorClientId == localClientId;
        bool shouldReceive = newValue.isActive && newValue.directorClientId != localClientId;
        
        if (wasStreaming && !shouldStream)
        {
            Debug.Log($"xx_[CLIENT] Stop streaming {pipelineName} - someone else took control");
            StopStreaming(newValue.pipelineType);
        }
        
        if (shouldStream)
        {
            Debug.Log($"xx_[CLIENT] Start streaming {pipelineName}");
            StartStreaming(newValue.pipelineType, newValue.streamSourceName);
        }
        else if (shouldReceive)
        {
            Debug.Log($"xx_[CLIENT] Start receiving {pipelineName} from {GetDirectorName(newValue.directorClientId)}");
            StartReceiving(newValue.pipelineType, newValue.streamSourceName);
        }
        else if (!newValue.isActive)
        {
            Debug.Log($"xx_[CLIENT] {pipelineName} stream stopped - fallback to local");
            FallbackToLocal(newValue.pipelineType);
        }
        
        // Fire event for UI updates
        string changeDescription = GetStreamChangeDescription(previousValue, newValue);
        OnStreamControlChanged?.Invoke(newValue, changeDescription);
    }

    #endregion

    #region Stream Management (To be implemented with WebRTC)

    private void StartStreaming(PipelineType pipeline, string sourceIdentifier)
    {
        // TODO: Implement WebRTC streaming start
        Debug.Log($"xx_[STREAM] 🚀 START streaming {sourceIdentifier} to {pipeline}");
        
        // Keep local assignment as-is (director sees their own source)
        // Start streaming this source to other clients
        // Other clients will receive this stream and override their local display
    }

    private void StopStreaming(PipelineType pipeline)
    {
        // TODO: Implement WebRTC streaming stop
        Debug.Log($"xx_[STREAM] ⏹️ STOP streaming {pipeline}");
        
        // Stop WebRTC stream transmission
        // Local assignment stays (director still sees their local source)
    }

    private void StartReceiving(PipelineType pipeline, string sourceIdentifier)
    {
        // TODO: Implement WebRTC receiving start
        Debug.Log($"xx_[STREAM] 📡 START receiving {sourceIdentifier} for {pipeline}");
        
        // Override local pipeline destination to show incoming stream instead of local NDI
        OverrideLocalPipelineWithNetworkStream(pipeline, sourceIdentifier);
    }

    private void FallbackToLocal(PipelineType pipeline)
    {
        // TODO: Implement fallback to local NDI
        Debug.Log($"xx_[STREAM] 🔄 FALLBACK to local for {pipeline}");
        
        // Stop WebRTC reception, return to whatever is locally assigned
        RestoreLocalPipelineAssignment(pipeline);
    }

    #endregion

    #region Integration with Local Pipeline

    private void SubscribeToLocalPipelineEvents()
    {
        // TODO: Subscribe to your BroadcastPipelineManager events
        // When director clicks "forward to live", call RequestStreamControl
    }

    private void OverrideLocalPipelineWithNetworkStream(PipelineType pipeline, string sourceIdentifier)
    {
        // TODO: Override the pipeline destination to show incoming network stream
        Debug.Log($"xx_[PIPELINE] 🔀 {pipeline} - Override with network stream from {sourceIdentifier}");
        
        // Find the pipeline destinations for this type
        // Replace their NDI input with WebRTC stream input
        // This is where you'll connect the incoming WebRTC stream to the renderer
    }

    private void RestoreLocalPipelineAssignment(PipelineType pipeline)
    {
        // TODO: Restore the pipeline to use whatever is currently assigned locally
        Debug.Log($"xx_[PIPELINE] 🔄 {pipeline} - Restore local assignment");
        
        // Stop showing network stream, return to local NDI source
        // Use whatever is currently in BroadcastPipelineManager.activeAssignments[pipeline]
    }

    #endregion

    #region Utility Methods

    private ulong GetLocalClientId()
    {
        if (NetworkManager.Singleton != null)
        {
            return NetworkManager.Singleton.LocalClientId;
        }
        
        Debug.LogError("xx_NetworkManager.Singleton is null!");
        return 0;
    }

    private string GetSourceIdentifier(NdiReceiver ndiSource)
    {
        // Use NDI source name which includes computer name
        return ndiSource.ndiName;
    }

    private NetworkVariable<StreamAssignment> GetNetworkVariableForPipeline(PipelineType pipeline)
    {
        switch (pipeline)
        {
            case PipelineType.StudioLive: return studioLiveStream;
            case PipelineType.TVLive: return tvLiveStream;
            default: return null;
        }
    }

    private string GetStreamDescription(StreamAssignment assignment)
    {
        if (!assignment.isActive) return "No stream";
        return $"Client {assignment.directorClientId} streaming {assignment.streamSourceName}";
    }

    private string GetDirectorName(ulong clientId)
    {
        // TODO: Get actual director name from user management system
        return $"Director_{clientId}";
    }

    private string GetStreamChangeDescription(StreamAssignment previous, StreamAssignment current)
    {
        if (!current.isActive) return "Stream stopped";
        if (!previous.isActive) return $"Stream started by {GetDirectorName(current.directorClientId)}";
        if (previous.directorClientId != current.directorClientId)
            return $"Stream taken over by {GetDirectorName(current.directorClientId)}";
        return "Stream updated";
    }

    #endregion

    #region Cleanup

    public override void OnNetworkDespawn()
    {
        if (studioLiveStream != null)
            studioLiveStream.OnValueChanged -= OnStudioLiveStreamChanged;
        if (tvLiveStream != null)
            tvLiveStream.OnValueChanged -= OnTvLiveStreamChanged;
    }

    #endregion
}