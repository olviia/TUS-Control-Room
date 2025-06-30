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

public class StreamManager : MonoBehaviour
{
    [Header("Stream Sources")]
    public List<StreamSource> streamSources = new List<StreamSource>();
    
    [Header("Stream Configuration")]
    [SerializeField] private float streamStartDelay =0.02f;
    
    private Dictionary<PipelineType, WebRTCStreamer> streamers = new Dictionary<PipelineType, WebRTCStreamer>();
    private Dictionary<PipelineType, bool> isStreamActive = new Dictionary<PipelineType, bool>();
    
    private void Start()
    {
        // Subscribe to events
        NetworkStreamCoordinator.OnStreamControlChanged += HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged += HandleStreamerStateChange;
        
        // Initialize all stream sources
        StartCoroutine(InitializeStreamers());
        
        Debug.Log($"[StreamManager] Initialized with {streamSources.Count} stream sources");
    }
    
    private void OnDestroy()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged -= HandleStreamerStateChange;
        
        // Clean up all streamers
        foreach (var streamer in streamers.Values)
        {
            if (streamer != null)
                streamer.StopStreaming();
        }
    }
    
    private IEnumerator InitializeStreamers()
    {
        yield return new WaitForEndOfFrame();
        
        foreach (var source in streamSources)
        {
            if (!source.isActive || source.renderer == null) continue;
            
            CreateStreamerForSource(source);
            isStreamActive[source.pipelineType] = false;
        }
        
        Debug.Log("[StreamManager] All streamers initialized");
    }
    
    private void CreateStreamerForSource(StreamSource source)
    {
        var streamerGO = new GameObject($"WebRTC_{source.pipelineType}_{source.sourceName}");
        streamerGO.transform.SetParent(transform);
        
        var streamer = streamerGO.AddComponent<WebRTCStreamer>();
        streamer.pipelineType = source.pipelineType;
        streamer.targetRenderer = source.renderer;
        
        // Ensure renderer has proper localNdiReceiver assignment
        if (source.renderer != null && source.ndiReceiver != null)
        {
            source.renderer.localNdiReceiver = source.ndiReceiver;
        }
        
        // Pre-assign NDI receiver if available
        if (source.ndiReceiver != null)
        {
            streamer.ndiReceiver = source.ndiReceiver;
        }
        
        streamers[source.pipelineType] = streamer;
        
        Debug.Log($"[StreamManager] Created streamer for {source.pipelineType} ({source.sourceName})");
    }
    
    private void HandleStreamControlChange(StreamAssignment assignment, string description)
    {
        Debug.Log($"[StreamManager] Stream control change: {assignment.pipelineType}, active: {assignment.isActive}");
        
        var pipeline = assignment.pipelineType;
        var source = GetSourceForPipeline(pipeline);
        
        if (source?.renderer == null)
        {
            Debug.LogError($"[StreamManager] No source/renderer found for {pipeline}");
            return;
        }
        
        // ALWAYS stop existing stream first
        StopStreamingForPipeline(pipeline);
        
        // Wait a moment for cleanup
        StartCoroutine(HandleStreamChangeDelayed(assignment, source));
    }
    
    private IEnumerator HandleStreamChangeDelayed(StreamAssignment assignment, StreamSource source)
    {
        yield return new WaitForSeconds(0.5f); // Allow cleanup time
        
        if (!assignment.isActive)
        {
            HandleStreamStopped(assignment.pipelineType, source.renderer);
            yield break;
        }
        
        bool isMyStream = NetworkManager.Singleton?.LocalClientId == assignment.directorClientId;
        
        if (isMyStream)
        {
            HandleMyStreamStarted(assignment.pipelineType, assignment.streamSourceName, source);
        }
        else
        {
            HandleRemoteStreamStarted(assignment.pipelineType, source);
        }
    }
    
    private void HandleStreamStopped(PipelineType pipeline, WebRTCRenderer renderer)
    {
        Debug.Log($"[StreamManager] ⏹️ Stream stopped for {pipeline}");
        isStreamActive[pipeline] = false;
        renderer.ShowLocalNDI();
    }
    
    private void HandleMyStreamStarted(PipelineType pipeline, string sourceIdentifier, StreamSource source)
    {
        Debug.Log($"[StreamManager] 🚀 Starting MY stream for {pipeline} with source: {sourceIdentifier}");
        StartCoroutine(StartMyStreamDelayed(pipeline, sourceIdentifier, source));
    }
    
    private void HandleRemoteStreamStarted(PipelineType pipeline, StreamSource source)
    {
        Debug.Log($"[StreamManager] 📡 Starting to receive REMOTE stream for {pipeline}");
        StartCoroutine(StartReceivingDelayed(pipeline));
    }
    
    private IEnumerator StartMyStreamDelayed(PipelineType pipeline, string sourceIdentifier, StreamSource source)
    {
        yield return new WaitForSeconds(streamStartDelay);
        
        var sourceObject = FindSourceByName(sourceIdentifier);
        if (sourceObject == null)
        {
            Debug.LogError($"[StreamManager] Source not found: {sourceIdentifier} for {pipeline}");
            source.renderer.ShowLocalNDI();
            yield break;
        }
        
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer == null)
        {
            Debug.LogError($"[StreamManager] No streamer found for {pipeline}");
            source.renderer.ShowLocalNDI();
            yield break;
        }
        
        // Update streamer with the active NDI source
        streamer.ndiReceiver = sourceObject.receiver;
        
        streamer.StartStreaming();
        isStreamActive[pipeline] = true;
        
        // Show local content while streaming to others
        source.renderer.ShowLocalNDI();
        
        Debug.Log($"[StreamManager] ✅ Successfully started streaming {sourceIdentifier} for {pipeline}");
    }
    
    private IEnumerator StartReceivingDelayed(PipelineType pipeline)
    {
        yield return new WaitForSeconds(streamStartDelay);
        
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer == null)
        {
            Debug.LogError($"[StreamManager] No streamer found for {pipeline}");
            yield break;
        }
        
        streamer.StartReceiving();
        isStreamActive[pipeline] = true;
        
        Debug.Log($"[StreamManager] ✅ Successfully started receiving for {pipeline}");
    }
    
    private void StopStreamingForPipeline(PipelineType pipeline)
    {
        var streamer = GetStreamerForPipeline(pipeline);
        if (streamer != null && isStreamActive.ContainsKey(pipeline) && isStreamActive[pipeline])
        {
            Debug.Log($"[StreamManager] Stopping existing stream for {pipeline}");
            streamer.StopStreaming();
            isStreamActive[pipeline] = false;
        }
    }
    
    private void HandleStreamerStateChange(PipelineType pipeline, StreamerState state)
    {
        Debug.Log($"[StreamManager] Streamer state change: {pipeline} → {state}");
        
        var source = GetSourceForPipeline(pipeline);
        if (source?.renderer == null) return;
        
        switch (state)
        {
            case StreamerState.Failed:
                Debug.LogWarning($"[StreamManager] Stream failed for {pipeline}, showing local content");
                source.renderer.ShowLocalNDI();
                isStreamActive[pipeline] = false;
                break;
                
            case StreamerState.Receiving:
                Debug.Log($"[StreamManager] Successfully receiving {pipeline}");
                break;
                
            case StreamerState.Idle:
                if (isStreamActive.ContainsKey(pipeline) && isStreamActive[pipeline])
                {
                    Debug.Log($"[StreamManager] Stream ended for {pipeline}, showing local content");
                    source.renderer.ShowLocalNDI();
                    isStreamActive[pipeline] = false;
                }
                break;
        }
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
        
        Debug.LogWarning($"[StreamManager] Source not found: {sourceName}");
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
    
    // Public methods
    public bool IsStreamActive(PipelineType pipeline)
    {
        return isStreamActive.ContainsKey(pipeline) && isStreamActive[pipeline];
    }
    
    public void ForceStopStream(PipelineType pipeline)
    {
        Debug.Log($"[StreamManager] Force stopping stream for {pipeline}");
        StopStreamingForPipeline(pipeline);
        
        var source = GetSourceForPipeline(pipeline);
        source?.renderer?.ShowLocalNDI();
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
            isStreamActive[pipeline] = false;
        }
    }
}