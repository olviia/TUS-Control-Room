using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BroadcastPipeline;
using Klak.Ndi;

/// <summary>
/// Auto-configuring network coordinator - detects pipelines from StreamManager
/// No manual pipeline configuration needed
/// </summary>
public class NetworkStreamCoordinator : NetworkBehaviour
{
    // Auto-populated from StreamManager
    private PipelineType[] supportedPipelines = new PipelineType[0];
    
    // NetworkVariables - created dynamically
    private NetworkVariable<StreamAssignment> studioLiveStream;
    private NetworkVariable<StreamAssignment> tvLiveStream;
    private Dictionary<PipelineType, NetworkVariable<StreamAssignment>> pipelineStreams;
    
    public static event Action<StreamAssignment, string> OnStreamControlChanged;

    #region Auto-Configuration

    void Awake()
    {
        InitializeCommonPipelines();
    }

    /// <summary>
    /// Auto-configure from StreamManager - called by StreamManager
    /// </summary>
    public void AutoConfigureFromManager(StreamManager manager)
    {
        supportedPipelines = manager.GetSupportedPipelines();
        Debug.Log($"[🎬StreamCoordinator] Auto-configured pipelines: {string.Join(", ", supportedPipelines)}");
    }

    /// <summary>
    /// Initialize common pipeline NetworkVariables
    /// </summary>
    private void InitializeCommonPipelines()
    {
        studioLiveStream = new NetworkVariable<StreamAssignment>(
            new StreamAssignment { isActive = false, pipelineType = PipelineType.StudioLive },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
        
        tvLiveStream = new NetworkVariable<StreamAssignment>(
            new StreamAssignment { isActive = false, pipelineType = PipelineType.TVLive },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
        
        pipelineStreams = new Dictionary<PipelineType, NetworkVariable<StreamAssignment>>
        {
            { PipelineType.StudioLive, studioLiveStream },
            { PipelineType.TVLive, tvLiveStream }
        };
    }

    public override void OnNetworkSpawn()
    {
        studioLiveStream.OnValueChanged += (prev, curr) => OnStreamChanged(prev, curr, "Studio Live");
        tvLiveStream.OnValueChanged += (prev, curr) => OnStreamChanged(prev, curr, "TV Live");

        Debug.Log($"[🎬StreamCoordinator] Network spawned with {supportedPipelines.Length} pipelines");

        // Check for active streams when client joins (late joiner support)
        if (IsClient && !IsServer)
        {
            StartCoroutine(CheckExistingStreamsOnJoin());
        }
    }

    /// <summary>
    /// When a client joins, check if streams are already active and trigger receiving
    /// </summary>
    private IEnumerator CheckExistingStreamsOnJoin()
    {
        // Wait a frame to ensure NetworkVariables are fully synchronized
        yield return new WaitForEndOfFrame();

        Debug.Log("[🎬StreamCoordinator] Checking for existing active streams...");

        foreach (var kvp in pipelineStreams)
        {
            var pipeline = kvp.Key;
            var assignment = kvp.Value.Value;

            if (assignment.isActive)
            {
                Debug.Log($"[🎬StreamCoordinator] Found active stream for {pipeline} - auto-starting receive");
                // Trigger the stream change event manually for late joiners
                OnStreamChanged(new StreamAssignment { isActive = false, pipelineType = pipeline },
                               assignment,
                               $"{pipeline} (Late Join)");
            }
        }
    }

    #endregion

    #region Public Interface

    public void RequestStreamControl(PipelineType pipeline, string localNdiName)
    {
        Debug.Log($"REQUEST STREAM CONTROL CALLED: {pipeline}, NDI: {localNdiName}");
        Debug.Log($"CALL STACK:\n{System.Environment.StackTrace}");
        if (!ValidateRequest(pipeline, localNdiName)) return;

        RequestStreamControlServerRpc(pipeline, localNdiName, NetworkManager.Singleton.LocalClientId);
    }

    public void ReleaseStreamControl(PipelineType pipeline)
    {
        if (!IsNetworkReady()) return;
        
        ReleaseStreamControlServerRpc(pipeline, NetworkManager.Singleton.LocalClientId);
    }

    public StreamAssignment GetCurrentAssignment(PipelineType pipeline)
    {
        return pipelineStreams.TryGetValue(pipeline, out var networkVar) 
            ? networkVar.Value 
            : new StreamAssignment { isActive = false, pipelineType = pipeline };
    }

    public bool IsPipelineActive(PipelineType pipeline)
    {
        return GetCurrentAssignment(pipeline).isActive;
    }

    public int GetActiveStreamCount()
    {
        return pipelineStreams.Values.Count(stream => stream.Value.isActive);
    }

    #endregion

    #region Validation

    private bool ValidateRequest(PipelineType pipeline, string localNdiName)
    {
        if (!IsNetworkReady()) return false;

        if (!supportedPipelines.Contains(pipeline))
        {
            Debug.LogError($"[🎬StreamCoordinator] Unsupported pipeline: {pipeline}");
            return false;
        }

        // Allow null/empty ndiName for non-NDI sources like TextureSourceObject
        // The actual source validation happens in BroadcastPipelineManager
        if (string.IsNullOrEmpty(localNdiName))
        {
            Debug.Log("[🎬StreamCoordinator] Non-NDI source (TextureSourceObject) - skipping ndiName validation");
        }

        return true;
    }

    private bool IsNetworkReady()
    {
        return NetworkManager.Singleton != null && 
               (NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost);
    }

    #endregion

    #region Server Authority

    [ServerRpc(RequireOwnership = false)]
    private void RequestStreamControlServerRpc(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
        var targetStream = GetNetworkVariableForPipeline(pipeline);
        if (targetStream == null) return;
        
        // Deactivate existing stream
        if (targetStream.Value.isActive)
        {
            DeactivateStream(targetStream, pipeline);
        }
        
        SetNewAssignmentAfterCleanup(pipeline, sourceIdentifier, requestingClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseStreamControlServerRpc(PipelineType pipeline, ulong requestingClientId)
    {
        var targetStream = GetNetworkVariableForPipeline(pipeline);
        
        if (targetStream?.Value.directorClientId == requestingClientId && targetStream.Value.isActive)
        {
            DeactivateStream(targetStream, pipeline);
        }
    }

    private void DeactivateStream(NetworkVariable<StreamAssignment> targetStream, PipelineType pipeline)
    {
        var current = targetStream.Value;
        targetStream.Value = new StreamAssignment
        {
            directorClientId = current.directorClientId,
            streamSourceName = current.streamSourceName,
            sessionId = current.sessionId,
            pipelineType = pipeline,
            isActive = false
        };
    }

    private void SetNewAssignmentAfterCleanup(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
    
        var targetStream = GetNetworkVariableForPipeline(pipeline);
        if (targetStream != null)
        {
            targetStream.Value = new StreamAssignment
            {
                directorClientId = requestingClientId,
                streamSourceName = sourceIdentifier,
                sessionId = GenerateSessionId(pipeline),
                pipelineType = pipeline,
                isActive = true
            };
        }
    }

    #endregion

    #region Network Variable Management

    private NetworkVariable<StreamAssignment> GetNetworkVariableForPipeline(PipelineType pipeline)
    {
        return pipelineStreams.TryGetValue(pipeline, out var networkVar) ? networkVar : null;
    }

    private void OnStreamChanged(StreamAssignment previousValue, StreamAssignment newValue, string pipelineName)
    {
        string changeDescription = GetStreamChangeDescription(previousValue, newValue);
        OnStreamControlChanged?.Invoke(newValue, changeDescription);
    }

    #endregion

    #region Utility Methods

    private string GenerateSessionId(PipelineType pipeline)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var random = UnityEngine.Random.Range(1000, 9999);
        return $"{pipeline}_{timestamp}_{random}";
    }

    private string GetStreamChangeDescription(StreamAssignment previous, StreamAssignment current)
    {
        if (!current.isActive) return "Stream stopped";
        if (!previous.isActive) return $"Stream started by Client_{current.directorClientId}";
        if (previous.directorClientId != current.directorClientId)
            return $"Stream taken over by Client_{current.directorClientId}";
        return "Stream updated";
    }

    #endregion

    #region Cleanup

    public override void OnNetworkDespawn()
    {
        if (studioLiveStream != null)
            studioLiveStream.OnValueChanged -= (prev, curr) => OnStreamChanged(prev, curr, "Studio Live");
        
        if (tvLiveStream != null)
            tvLiveStream.OnValueChanged -= (prev, curr) => OnStreamChanged(prev, curr, "TV Live");
        
        pipelineStreams?.Clear();
    }

    #endregion
}