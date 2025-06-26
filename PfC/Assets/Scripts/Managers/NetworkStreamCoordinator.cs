using Unity.Netcode;
using UnityEngine;
using System;
using BroadcastPipeline;
using Klak.Ndi;

[System.Serializable]
public struct StreamAssignment : INetworkSerializable
{
    public ulong directorClientId;
    public string streamSourceName;
    public PipelineType pipelineType;
    public bool isActive;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref directorClientId);
    
        // Handle null strings safely
        if (serializer.IsWriter)
        {
            var safeString = streamSourceName ?? "";
            serializer.SerializeValue(ref safeString);
        }
        else
        {
            serializer.SerializeValue(ref streamSourceName);
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

    private void Awake()
    {

    }

    public override void OnNetworkSpawn()
    {
       
        try
        {
            Debug.Log("[StreamCoordinator] OnNetworkSpawn starting");
            studioLiveStream.OnValueChanged += OnStudioLiveStreamChanged;
            tvLiveStream.OnValueChanged += OnTvLiveStreamChanged;
            Debug.Log("[StreamCoordinator] OnNetworkSpawn completed");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[StreamCoordinator] OnNetworkSpawn failed: {e}");
        }
    }

    #region Public Interface

    public void RequestStreamControl(PipelineType pipeline, NdiReceiver localNdiSource)
    {
        Debug.Log($"[StreamCoordinator] RequestStreamControl pipeline:{pipeline} localNdiSource:{localNdiSource}");

        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)) return;
        if (pipeline != PipelineType.StudioLive && pipeline != PipelineType.TVLive) return;

        RequestStreamControlServerRpc(pipeline, localNdiSource.ndiName, NetworkManager.Singleton.LocalClientId);
    }

    public void ReleaseStreamControl(PipelineType pipeline)
    {
        if (NetworkManager.Singleton == null || 
            (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)) return;
        
        ReleaseStreamControlServerRpc(pipeline, NetworkManager.Singleton.LocalClientId);
    }

    #endregion

    #region Server Authority

    [ServerRpc(RequireOwnership = false)]
    private void RequestStreamControlServerRpc(PipelineType pipeline, string sourceIdentifier, ulong requestingClientId)
    {
        Debug.Log($"[StreamCoordinator] RequestStreamControlServerRpc pipeline:{pipeline} localNdiSource:{sourceIdentifier}, requestingClientId:{requestingClientId}");

        StreamAssignment newAssignment = new StreamAssignment
        {
            directorClientId = requestingClientId,
            streamSourceName = sourceIdentifier,
            pipelineType = pipeline,
            isActive = true
        };

        // TODO: Consider WebRTC vs NDI name for network streaming
        switch (pipeline)
        {
            case PipelineType.StudioLive:
                studioLiveStream.Value = newAssignment;
                break;
            case PipelineType.TVLive:
                tvLiveStream.Value = newAssignment;
                break;
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ReleaseStreamControlServerRpc(PipelineType pipeline, ulong requestingClientId)
    {
        NetworkVariable<StreamAssignment> targetStream = GetNetworkVariableForPipeline(pipeline);
        
        if (targetStream?.Value.directorClientId == requestingClientId)
        {
            targetStream.Value = new StreamAssignment
            {
                directorClientId = 0,
                streamSourceName = "",
                pipelineType = pipeline,
                isActive = false
            };
        }
    }

    #endregion

    #region Network Variable Callbacks

    private void OnStudioLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        Debug.Log($"[StreamCoordinator] OnStudioLiveStreamChanged from {previousValue} to {newValue}");
        HandleStreamChange(previousValue, newValue, "Studio Live");
    }

    private void OnTvLiveStreamChanged(StreamAssignment previousValue, StreamAssignment newValue)
    {
        Debug.Log($"[StreamCoordinator] OnTVLiveStreamChanged from {previousValue} to {newValue}");

        HandleStreamChange(previousValue, newValue, "TV Live");
    }

    private void HandleStreamChange(StreamAssignment previousValue, StreamAssignment newValue, string pipelineName)
    {
        Debug.Log($"[StreamCoordinator] HandleStreamChange {previousValue} to {newValue}");

        string changeDescription = GetStreamChangeDescription(previousValue, newValue);
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

    private string GetDirectorName(ulong clientId) => $"Director_{clientId}";

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