using UnityEngine;
using BroadcastPipeline;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;
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

public class StreamManager : MonoBehaviour
{
    [Header("Stream Sources")]
    public List<StreamSource> streamSources = new List<StreamSource>();
    
    private Dictionary<PipelineType, WebRTCStreamer> streamers = new Dictionary<PipelineType, WebRTCStreamer>();
    private Dictionary<PipelineType, StreamingSession> activeSessions = new Dictionary<PipelineType, StreamingSession>();

    // [Header("Stream Configuration")]
    // [SerializeField] private float streamStartDelay = 0.5f;
    // [SerializeField] private float sourceChangeGracePeriod = 2f;
    //

    private void Start()
    {
        SubscribeToEvents();
        StartCoroutine(InitializeStreamers());
        
        Debug.Log($"[🎯StreamManager] Initialized with {streamSources.Count} stream sources");
    }
    
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
        CleanupAllStreamers();
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
    
    private IEnumerator InitializeStreamers()
    {
        yield return new WaitForEndOfFrame();
        
        foreach (var source in streamSources)
        {
            if (!source.isActive || source.renderer == null) continue;
            CreateStreamerForSource(source);
        }
        
        Debug.Log("[🎯StreamManager] All streamers initialized");
    }
    
    private void CreateStreamerForSource(StreamSource source)
    {
        var streamerGO = new GameObject($"WebRTC_{source.pipelineType}_{source.sourceName}");
        streamerGO.transform.SetParent(transform);
        
        var streamer = streamerGO.AddComponent<WebRTCStreamer>();
        streamer.pipelineType = source.pipelineType;
        streamer.targetRenderer = source.renderer;
        
        if (source.renderer != null && source.ndiReceiver != null)
        {
            source.renderer.localNdiReceiver = source.ndiReceiver;
        }
        
        streamers[source.pipelineType] = streamer;
        activeSessions[source.pipelineType] = null;
        
        Debug.Log($"[🎯StreamManager] Created streamer for {source.pipelineType} ({source.sourceName})");
    }
    
    private void HandleStreamControlChange(StreamAssignment assignment, string description)
    {
        Debug.Log($"[🎯StreamManager] Stream control change: {assignment.pipelineType}, session:{assignment.sessionId}, active:{assignment.isActive}, director:{assignment.directorClientId}");
        
        var pipeline = assignment.pipelineType;
        var source = GetSourceForPipeline(pipeline);
        
        if (source?.renderer == null)
        {
            Debug.LogError($"[🎯StreamManager] No source/renderer found for {pipeline}");
            return;
        }
        
        bool isMyStream = NetworkManager.Singleton?.LocalClientId == assignment.directorClientId;
        // Stop any existing session
        if (activeSessions[pipeline] != null)
        {
            Debug.Log($"[🎯StreamManager] Stopping existing session for {pipeline}");
            StopStreamingForPipeline(pipeline);
        }
        
        if (!assignment.isActive)
        {
            HandleStreamStopped(pipeline, source.renderer);
            return;
        }
        // Create session tracking
        activeSessions[pipeline] = new StreamingSession
        {
            sessionId = assignment.sessionId,
            sourceIdentifier = assignment.streamSourceName,
            isMyStream = isMyStream,
            state = StreamerState.Idle
        };
        
        if (isMyStream)
        {
            HandleMyStreamStarted(pipeline, assignment.streamSourceName, source, assignment.sessionId);
        }
        else
        {
            HandleRemoteStreamStarted(pipeline, source, assignment.sessionId);
        }
    }
    
    private void HandleStreamStopped(PipelineType pipeline, WebRTCRenderer renderer)
    {
        Debug.Log($"[🎯StreamManager] Stream stopped for {pipeline}");
        activeSessions[pipeline] = null;
        renderer.ShowLocalNDI();
    }
    
    private void HandleMyStreamStarted(PipelineType pipeline, string sourceIdentifier, StreamSource source, string sessionId)
    {
        Debug.Log($"[🎯StreamManager] Starting MY stream for {pipeline} with source: {sourceIdentifier} session: {sessionId}");
        
        var sourceObject = FindSourceByName(sourceIdentifier);
        if (sourceObject == null)
        {
            Debug.LogError($"[🎯StreamManager] Source not found: {sourceIdentifier} for {pipeline}");
            source.renderer.ShowLocalNDI();
            return;
        }
        
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] No streamer found for {pipeline}");
            source.renderer.ShowLocalNDI();
            return;
        }
        
        streamer.ndiReceiver = sourceObject.receiver;
        streamer.StartStreaming(sessionId);
        source.renderer.ShowLocalNDI();
        
        Debug.Log($"[🎯StreamManager] Successfully started streaming {sourceIdentifier} for {pipeline} session {sessionId}");
    }
    
    private void HandleRemoteStreamStarted(PipelineType pipeline, StreamSource source, string sessionId)
    {
        Debug.Log($"[🎯StreamManager] Starting to receive REMOTE stream for {pipeline} session {sessionId}");
        
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] No streamer found for {pipeline}");
            return;
        }
        
        streamer.StartReceiving(sessionId);
        
        Debug.Log($"[🎯StreamManager] Successfully started receiving for {pipeline} session {sessionId}");
    }
    
    private void StopStreamingForPipeline(PipelineType pipeline)
    {
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer != null)
        {
            Debug.Log($"[🎯StreamManager] Stopping streaming for {pipeline}");
            streamer.StopStreaming();
        }
    }
    
    private void HandleStreamerStateChange(PipelineType pipeline, StreamerState state, string sessionId)
    {
        Debug.Log($"[🎯StreamManager] Streamer state change: {pipeline} → {state} session {sessionId}");
        
        if (activeSessions[pipeline] != null)
        {
            activeSessions[pipeline].state = state;
        }
        
        var source = GetSourceForPipeline(pipeline);
        if (source?.renderer == null) return;
        
        switch (state)
        {
            case StreamerState.Failed:
                Debug.LogWarning($"[🎯StreamManager] Stream failed for {pipeline} session {sessionId}");
                source.renderer.HandleStreamFailure();
                activeSessions[pipeline] = null;
                break;
                
            case StreamerState.Receiving:
                Debug.Log($"[🎯StreamManager] Successfully receiving {pipeline} session {sessionId}");
                break;
                
            case StreamerState.Idle:
                if (activeSessions[pipeline] != null && activeSessions[pipeline].sessionId == sessionId)
                {
                    Debug.Log($"[🎯StreamManager] Stream ended for {pipeline} session {sessionId}");
                    source.renderer.ShowLocalNDI();
                    activeSessions[pipeline] = null;
                }
                break;
        }
    }
    
    private void HandleDisplayModeChange(PipelineType pipeline, bool isRemote, string sessionId)
    {
        Debug.Log($"[🎯StreamManager] Display mode changed for {pipeline}: {(isRemote ? "Remote" : "Local")} session {sessionId}");
    }
    
    private SourceObject FindSourceByName(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        
        var sources = FindObjectsOfType<SourceObject>();
        foreach (var source in sources)
        {
            if (source.receiver != null && source.receiver.ndiName == sourceName)
                return source;
        }
        
        Debug.LogWarning($"[🎯StreamManager] Source not found: {sourceName}");
        return null;
    }
    
    private StreamSource GetSourceForPipeline(PipelineType pipeline)
    {
        return streamSources.Find(s => s.pipelineType == pipeline);
    }
    
    private WebRTCStreamer GetStreamerForPipeline(PipelineType pipeline)
    {
        return streamers.ContainsKey(pipeline) ? streamers[pipeline] : null;
    }
    
    private void CleanupAllStreamers()
    {
        foreach (var streamer in streamers.Values)
        {
            if (streamer != null)
                streamer.StopStreaming();
        }
        streamers.Clear();
        activeSessions.Clear();
    }
    
    #region Public Interface
    
    public bool IsStreamActive(PipelineType pipeline)
    {
        return activeSessions.ContainsKey(pipeline) && 
               activeSessions[pipeline] != null &&
               (activeSessions[pipeline].state == StreamerState.Streaming || 
                activeSessions[pipeline].state == StreamerState.Receiving);
    }
    
    public void ForceStopStream(PipelineType pipeline)
    {
        Debug.Log($"[🎯StreamManager] Force stopping stream for {pipeline}");
        StopStreamingForPipeline(pipeline);
        
        var source = GetSourceForPipeline(pipeline);
        source?.renderer?.ShowLocalNDI();
        
        activeSessions[pipeline] = null;
    }
    
    public StreamingSession GetActiveSession(PipelineType pipeline)
    {
        return activeSessions.GetValueOrDefault(pipeline);
    }
    
    public void AddStreamSource(string name, PipelineType pipeline, WebRTCRenderer renderer, NdiReceiver ndiReceiver = null)
    {
        var newSource = new StreamSource
        {
            sourceName = name,
            pipelineType = pipeline,
            renderer = renderer,
            ndiReceiver = ndiReceiver,
            isActive = true
        };
        
        streamSources.Add(newSource);
        
        if (Application.isPlaying)
        {
            CreateStreamerForSource(newSource);
        }
    }
    
    [ContextMenu("Force Stop All Streams")]
    public void ForceStopAllStreams()
    {
        foreach (var pipeline in streamers.Keys)
        {
            ForceStopStream(pipeline);
        }
    }
    
    [ContextMenu("Debug All Session States")]
    public void DebugAllSessionStates()
    {
        foreach (var kvp in activeSessions)
        {
            var session = kvp.Value;
            var streamer = GetStreamerForPipeline(kvp.Key);
            Debug.Log($"[🎯StreamManager] Pipeline {kvp.Key}: Session={session?.sessionId}, State={session?.state}, StreamerState={streamer?.CurrentState}");
        }
    }

    #endregion
}