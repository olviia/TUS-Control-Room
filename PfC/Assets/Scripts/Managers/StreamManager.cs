using UnityEngine;
using BroadcastPipeline;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Klak.Ndi;

[System.Serializable]
public class StreamSource
{
    public string sourceName;
    public PipelineType pipelineType;
    public WebRTCRenderer renderer;
    public NdiReceiver ndiReceiver;
    public bool isActive = true;
}

public class StreamingSession
{
    public string sessionId;
    public string sourceIdentifier;
    public bool isMyStream;
    public StreamerState state;
}

/// <summary>
/// Single configuration point for all streaming pipelines
/// Auto-configures entire system from streamSources list
/// </summary>
public class StreamManager : MonoBehaviour
{
    [Header("Stream Configuration - SINGLE SOURCE OF TRUTH")]
    public List<StreamSource> streamSources = new List<StreamSource>();
    
    private Dictionary<PipelineType, WebRTCStreamer> streamers = new Dictionary<PipelineType, WebRTCStreamer>();
    private Dictionary<PipelineType, StreamingSession> activeSessions = new Dictionary<PipelineType, StreamingSession>();
    
    /// <summary>
    /// Get supported pipelines from active stream sources
    /// </summary>
    public PipelineType[] GetSupportedPipelines()
    {
        return streamSources.Where(s => s.isActive).Select(s => s.pipelineType).Distinct().ToArray();
    }

    #region Initialization

    private void Start()
    {
        SubscribeToEvents();
        StartCoroutine(InitializeStreamers());
        
        var supportedPipelines = GetSupportedPipelines();
        Debug.Log($"[🎯StreamManager] Auto-configured {supportedPipelines.Length} pipelines: {string.Join(", ", supportedPipelines)}");
    }
    
    private void SubscribeToEvents()
    {
        NetworkStreamCoordinator.OnStreamControlChanged += HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged += HandleStreamerStateChange;
    }
    
    private void UnsubscribeFromEvents()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged -= HandleStreamerStateChange;
    }
    
    /// <summary>
    /// Auto-create streamers from configured sources
    /// </summary>
    private IEnumerator InitializeStreamers()
    {
        yield return new WaitForEndOfFrame();
        
        foreach (var source in streamSources.Where(s => s.isActive && s.renderer != null))
        {
            CreateStreamerForSource(source);
        }
        
        // Auto-configure coordinator with detected pipelines
        var coordinator = FindObjectOfType<NetworkStreamCoordinator>();
        coordinator?.AutoConfigureFromManager(this);
        
        Debug.Log($"[🎯StreamManager] System auto-configured from {streamSources.Count} sources");
    }
    
    /// <summary>
    /// Create isolated streamer with full configuration
    /// </summary>
    private void CreateStreamerForSource(StreamSource source)
    {
        var streamerGO = new GameObject($"WebRTC_{source.pipelineType}_{source.sourceName}");
        streamerGO.transform.SetParent(transform);
        
        var streamer = streamerGO.AddComponent<WebRTCStreamer>();
        streamer.pipelineType = source.pipelineType;
        streamer.targetRenderer = source.renderer;
        streamer.ndiReceiver = source.ndiReceiver;
        
        // Configure renderer
        if (source.renderer != null && source.ndiReceiver != null)
        {
            source.renderer.localNdiReceiver = source.ndiReceiver;
            source.renderer.pipelineType = source.pipelineType;
        }
        
        streamers[source.pipelineType] = streamer;
        activeSessions[source.pipelineType] = null;
        
        Debug.Log($"[🎯StreamManager] Auto-created {source.pipelineType} streamer");
    }

    #endregion

    #region Stream Control Event Handling

    private void HandleStreamControlChange(StreamAssignment assignment, string description)
    {
        var pipeline = assignment.pipelineType;
        var source = GetSourceForPipeline(pipeline);
        
        if (source?.renderer == null)
        {
            Debug.LogError($"[🎯StreamManager] No configuration found for {pipeline}");
            return;
        }
        
        bool isMyStream = NetworkManager.Singleton?.LocalClientId == assignment.directorClientId;
        
        // Clean shutdown existing session
        if (activeSessions[pipeline] != null)
        {
            GetStreamerForPipeline(pipeline)?.ForceStop();
            activeSessions[pipeline] = null;
        }
        
        if (!assignment.isActive)
        {
            source.renderer.ShowLocalNDI();
            return;
        }
        
        activeSessions[pipeline] = new StreamingSession
        {
            sessionId = assignment.sessionId,
            sourceIdentifier = assignment.streamSourceName,
            isMyStream = isMyStream,
            state = StreamerState.Idle
        };
        
        if (isMyStream)
            StartMyStream(pipeline, assignment.streamSourceName, source, assignment.sessionId);
        else
            StartReceivingStream(pipeline, source, assignment.sessionId);
    }
    
    private void StartMyStream(PipelineType pipeline, string sourceIdentifier, StreamSource source, string sessionId)
    {
        var sourceObject = FindSourceByName(sourceIdentifier);
        var streamer = GetStreamerForPipeline(pipeline);
        
        if (sourceObject?.receiver == null || streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] Missing source/streamer for {pipeline}");
            source.renderer.ShowLocalNDI();
            return;
        }
        
        streamer.ndiReceiver = sourceObject.receiver;
        streamer.StartStreaming(sessionId);
        source.renderer.ShowLocalNDI();
        
        Debug.Log($"[🎯StreamManager] Started streaming {sourceIdentifier} for {pipeline}");
    }
    
    private void StartReceivingStream(PipelineType pipeline, StreamSource source, string sessionId)
    {
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] No streamer for {pipeline}");
            return;
        }
        
        streamer.StartReceiving(sessionId);
        Debug.Log($"[🎯StreamManager] Started receiving {pipeline}");
    }

    #endregion

    #region Streamer State Handling

    private void HandleStreamerStateChange(PipelineType pipeline, StreamerState state, string sessionId)
    {
        if (activeSessions[pipeline] != null)
            activeSessions[pipeline].state = state;
        
        var source = GetSourceForPipeline(pipeline);
        if (source?.renderer == null) return;
        
        switch (state)
        {
            case StreamerState.Failed:
                source.renderer.HandleStreamFailure();
                activeSessions[pipeline] = null;
                break;
                
            case StreamerState.Idle:
                if (activeSessions[pipeline]?.sessionId == sessionId)
                {
                    source.renderer.ShowLocalNDI();
                    activeSessions[pipeline] = null;
                }
                break;
        }
    }

    #endregion

    #region Helper Methods

    private SourceObject FindSourceByName(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        
        return FindObjectsOfType<SourceObject>()
            .FirstOrDefault(s => s.receiver?.ndiName == sourceName);
    }
    
    private StreamSource GetSourceForPipeline(PipelineType pipeline)
    {
        return streamSources.FirstOrDefault(s => s.pipelineType == pipeline);
    }
    
    private WebRTCStreamer GetStreamerForPipeline(PipelineType pipeline)
    {
        return streamers.GetValueOrDefault(pipeline);
    }

    #endregion

    #region Public Interface
    
    public bool IsStreamActive(PipelineType pipeline)
    {
        var session = activeSessions.GetValueOrDefault(pipeline);
        return session?.state == StreamerState.Streaming || session?.state == StreamerState.Receiving;
    }
    
    public void ForceStopStream(PipelineType pipeline)
    {
        GetStreamerForPipeline(pipeline)?.ForceStop();
        GetSourceForPipeline(pipeline)?.renderer?.ShowLocalNDI();
        activeSessions[pipeline] = null;
    }
    
    public StreamingSession GetActiveSession(PipelineType pipeline)
    {
        return activeSessions.GetValueOrDefault(pipeline);
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        foreach (var streamer in streamers.Values)
            streamer?.ForceStop();
        streamers.Clear();
        activeSessions.Clear();
    }

    #endregion
}