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
    public string streamSourceName;
    public string sessionId;
    public PipelineType pipelineType;
    public bool isActive;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref directorClientId);
        
        // Handle null strings safely
        if (serializer.IsWriter)
        {
            var safeSourceName = streamSourceName ?? "";
            var safeSessionId = sessionId ?? "";
            serializer.SerializeValue(ref safeSourceName);
            serializer.SerializeValue(ref safeSessionId);
        }
        else
        {
            serializer.SerializeValue(ref streamSourceName);
            serializer.SerializeValue(ref sessionId);
        }
        
        serializer.SerializeValue(ref pipelineType);
        serializer.SerializeValue(ref isActive);
    }
}

public class NetworkStreamCoordinator : NetworkBehaviour
{
    private NetworkVariable<StreamAssignment> studioLiveStream = new NetworkVariable<StreamAssignment>(
        new StreamAssignment { isActive = false },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private NetworkVariable<StreamAssignment> tvLiveStream = new NetworkVariable<StreamAssignment>(
        new StreamAssignment { isActive = false },
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    public static event Action<StreamAssignment, string> OnStreamControlChanged;

    public override void OnNetworkSpawn()
    {
        try
        {
            Debug.Log("[🎬StreamCoordinator] OnNetworkSpawn starting");
            studioLiveStream.OnValueChanged += OnStudioLiveStreamChanged;
            tvLiveStream.OnValueChanged += OnTvLiveStreamChanged;
            Debug.Log("[🎬StreamCoordinator] OnNetworkSpawn completed");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[🎬StreamCoordinator] OnNetworkSpawn failed: {e}");
        }
    }

    #region Public Interface

    public void RequestStreamControl(PipelineType pipeline, NdiReceiver localNdiSource)
    {
        Debug.Log($"[🎬StreamCoordinator] RequestStreamControl pipeline:{pipeline} localNdiSource:{localNdiSource?.ndiName}");

        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)) return;
        if (pipeline != PipelineType.StudioLive && pipeline != PipelineType.TVLive) return;
        if (localNdiSource == null) 
        {
            Debug.LogError("[🎬StreamCoordinator] No NDI source provided");
            return;
        }

        RequestStreamControlServerRpc(pipeline, localNdiSource.ndiName, NetworkManager.Singleton.LocalClientId);
    }

    public void ReleaseStreamControl(PipelineType pipeline)
    {
        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)) return;
        
        Debug.Log($"[🎬StreamCoordinator] ReleaseStreamControl pipeline:{pipeline}");
        ReleaseStreamControlServerRpc(pipeline, NetworkManager.Singleton.LocalClientId);
    }

    #endregion

    #region Server Authority

    [ServerRpc(RequireOwnership = false)]
    private void RequestStreamControlServerRpc(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
        Debug.Log($"[🎬StreamCoordinator] RequestStreamControlServerRpc pipeline:{pipeline} source:{sourceIdentifier}, client:{requestingClientId}");
        
        var currentStream = GetNetworkVariableForPipeline(pipeline);
        // If there's an active stream, first set it to inactive to trigger cleanup on all clients
        if (currentStream?.Value.isActive == true)
        {
            currentStream.Value = new StreamAssignment
            {
                directorClientId = currentStream.Value.directorClientId,
                streamSourceName = currentStream.Value.streamSourceName,
                sessionId = currentStream.Value.sessionId,
                pipelineType = pipeline,
                isActive = false // This will trigger cleanup
            };
        }    StartCoroutine(SetNewAssignmentAfterCleanup(pipeline, sourceIdentifier, requestingClientId));
    }

    private IEnumerator SetNewAssignmentAfterCleanup(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
        yield return null; // Wait one frame for cleanup
    
        string sessionId = GenerateSessionId();
        StreamAssignment newAssignment = new StreamAssignment
        {
            directorClientId = requestingClientId,
            streamSourceName = sourceIdentifier,
            sessionId = sessionId,
            pipelineType = pipeline,
            isActive = true
        };

        var targetStream = GetNetworkVariableForPipeline(pipeline);
        if (targetStream != null)
            targetStream.Value = newAssignment;
    }
    [ServerRpc(RequireOwnership = false)]
    private void ReleaseStreamControlServerRpc(PipelineType pipeline, ulong requestingClientId)
    {
        Debug.Log($"[🎬StreamCoordinator] ReleaseStreamControlServerRpc pipeline:{pipeline} client:{requestingClientId}");
        
        NetworkVariable<StreamAssignment> targetStream = GetNetworkVariableForPipeline(pipeline);
        
        if (targetStream?.Value.directorClientId == requestingClientId)
        {
            targetStream.Value = new StreamAssignment
            {
                directorClientId = 0,
                streamSourceName = "",
                sessionId = "",
                pipelineType = pipeline,
                isActive = false
            };
        }
    }

    #endregion

    #region Network Variable Callbacks

    private void OnStudioLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        Debug.Log($"[🎬StreamCoordinator] OnStudioLiveStreamChanged session:{newValue.sessionId} active:{newValue.isActive} director:{newValue.directorClientId}");
        HandleStreamChange(previousValue, newValue, "Studio Live");
    }

    private void OnTvLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        Debug.Log($"[🎬StreamCoordinator] OnTVLiveStreamChanged session:{newValue.sessionId} active:{newValue.isActive} director:{newValue.directorClientId}");
        HandleStreamChange(previousValue, newValue, "TV Live");
    }

    private void HandleStreamChange(StreamAssignment previousValue, StreamAssignment newValue, string pipelineName)
    {
        string changeDescription = GetStreamChangeDescription(previousValue, newValue);
        Debug.Log($"[🎬StreamCoordinator] HandleStreamChange: {changeDescription}");
        OnStreamControlChanged?.Invoke(newValue, changeDescription);
    }

    #endregion

    #region Utility Methods

    private NetworkVariable<StreamAssignment> GetNetworkVariableForPipeline(PipelineType pipeline)
    {
        return pipeline switch
        {
            PipelineType.StudioLive => studioLiveStream,
            PipelineType.TVLive => tvLiveStream,
            _ => null
        };
    }

    private string GenerateSessionId()
    {
        return $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{UnityEngine.Random.Range(1000, 9999)}";
    }

    private string GetDirectorName(ulong clientId) => $"Client_{clientId}";

    private string GetStreamChangeDescription(StreamAssignment previous, StreamAssignment current)
    {
        if (!current.isActive) return "Stream stopped";
        if (!previous.isActive) return $"Stream started by {GetDirectorName(current.directorClientId)}";
        if (previous.directorClientId != current.directorClientId)
            return $"Stream taken over by {GetDirectorName(current.directorClientId)}";
        return "Stream updated";
    }

    public StreamAssignment GetCurrentAssignment(PipelineType pipeline)
    {
        return pipeline switch
        {
            PipelineType.StudioLive => studioLiveStream.Value,
            PipelineType.TVLive => tvLiveStream.Value,
            _ => new StreamAssignment { isActive = false }
        };
    }

    #endregion

    #region Cleanup

    public override void OnNetworkDespawn()
    {
        Debug.Log("[🎬StreamCoordinator] OnNetworkDespawn");
        
        if (studioLiveStream != null)
            studioLiveStream.OnValueChanged -= OnStudioLiveStreamChanged;
        if (tvLiveStream != null)
            tvLiveStream.OnValueChanged -= OnTvLiveStreamChanged;
    }

    #endregion
}