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
    public NdiReceiver ndiReceiverSource;
    public NdiReceiver ndiReceiverCaptions;
    public bool isActive = true;
    
   }

public class StreamSession
{
    public string sessionId;
    public string sourceIdentifier;
    public bool isMyStream;
    public StreamerState state;
}

/// <summary>
/// Creates and controls WebRTC streamers  for each configured pipeline
/// </summary>
public class StreamManager : MonoBehaviour
{
    [Header("Stream Configuration")]
    public List<StreamSource> streamSources = new List<StreamSource>();
    
    private Dictionary<PipelineType, WebRTCStreamer> streamers = new Dictionary<PipelineType, WebRTCStreamer>();
    private Dictionary<PipelineType, StreamSession> activeSessions = new Dictionary<PipelineType, StreamSession>();

    public bool isStreaming;
    
    #region Unity Lifecycle
    
    private void Start()
    {
        ConnectToEvents();
        StartCoroutine(CreateStreamers());
        
        Debug.Log($"[🎯StreamManager] Managing {GetActiveSources().Length} pipelines ");
    }
    
    private void OnDestroy()
    {
        DisconnectFromEvents();
        StopAllStreamers();
    }
    
    #endregion
    
    #region Initialization
    
    private void ConnectToEvents()
    {
        NetworkStreamCoordinator.OnStreamControlChanged += HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged += HandleStreamerStateChange;
        
     }
    
    private void DisconnectFromEvents()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged -= HandleStreamerStateChange;
    }
    
    private IEnumerator CreateStreamers()
    {
        yield return new WaitForEndOfFrame();
        
        foreach (var source in GetActiveSources())
        {
            CreateStreamerForSource(source);
        }
        
        ConfigureCoordinator();
        
    }
    
    private void CreateStreamerForSource(StreamSource source)
    {
        // Create the main streamer GameObject
        var streamerObject = new GameObject($"Streamer_{source.pipelineType}");
        streamerObject.transform.SetParent(transform);
        
        // Create WebRTC Streamer
        var streamer = streamerObject.AddComponent<WebRTCStreamer>();
        streamer.pipelineType = source.pipelineType;
        streamer.targetRenderer = source.renderer;
        streamer.ndiReceiverSource = source.ndiReceiverSource;
        streamer.audioInterceptor = source.ndiReceiverSource.GetComponent<NdiAudioInterceptor>();
        
        if(source.ndiReceiverSource != null)
            streamer.ndiReceiverCaptions = source.ndiReceiverCaptions;
        
        // Store references
        streamers[source.pipelineType] = streamer;

        activeSessions[source.pipelineType] = null;
    }
    
    
    
    private void ConfigureRenderer(StreamSource source)
    {
        if (source.renderer != null && source.ndiReceiverSource != null)
        {
            source.renderer.localNdiReceiver = source.ndiReceiverSource;
            if(source.ndiReceiverCaptions != null)
                source.renderer.localNdiReceiverCaptions = source.ndiReceiverCaptions;
            source.renderer.pipelineType = source.pipelineType;
            
        }
    }
    
    private void ConfigureCoordinator()
    {
        var coordinator = FindObjectOfType<NetworkStreamCoordinator>();
        coordinator?.AutoConfigureFromManager(this);
    }
    
    #endregion
    
    #region Stream Control Events
    
    private void HandleStreamControlChange(StreamAssignment assignment, string description)
    {
        var pipeline = assignment.pipelineType;
        var source = GetSourceForPipeline(pipeline);
        
        if (source?.renderer == null)
        {
            Debug.LogError($"[🎯StreamManager] No configuration for {pipeline}");
            return;
        }
        
        StopCurrentSession(pipeline);
        
        if (!assignment.isActive)
        {
            ShowLocalContent(source);
            return;
        }
        
        StartNewSession(pipeline, assignment, source);
    }
    
    private void StopCurrentSession(PipelineType pipeline)
    {
        if (activeSessions[pipeline] != null)
        {
            GetStreamerForPipeline(pipeline)?.StopSession();
            activeSessions[pipeline] = null;
        }
    }
    
    private void ShowLocalContent(StreamSource source)
    {
        source.renderer.ShowLocalNDI();
    }
    
    private void StartNewSession(PipelineType pipeline, StreamAssignment assignment, StreamSource source)
    {
        bool isMyStream = NetworkManager.Singleton?.LocalClientId == assignment.directorClientId;
        
        activeSessions[pipeline] = new StreamSession
        {
            sessionId = assignment.sessionId,
            sourceIdentifier = assignment.streamSourceName,
            isMyStream = isMyStream,
            state = StreamerState.Idle
        };
        
        if (isMyStream)
        {
            StartStreaming(pipeline, assignment, source);
        }
        else
        {
            StartReceiving(pipeline, assignment.sessionId);
        }
    }
    
    private void StartStreaming(PipelineType pipeline, StreamAssignment assignment, StreamSource source)
    {
        var pipelineSource = FindPipelineSourceByName(assignment.streamSourceName);
        var streamer = GetStreamerForPipeline(pipeline);
        isStreaming = true;

        if (pipelineSource == null || streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] Cannot start streaming {pipeline} - missing components");
            ShowLocalContent(source);
            return;
        }

        // Check if source is a TextureSourceObject (non-NDI source)
        if (pipelineSource is TextureSourceObject textureSourceObj)
        {
            Debug.Log($"[🎯StreamManager] Source is TextureSourceObject - using direct texture streaming");
            streamer.textureSource = textureSourceObj;
            streamer.ndiReceiverSource = null; // Clear NDI receiver
        }
        else
        {
            // Update NDI source for both video
            streamer.textureSource = null; // Clear texture source
            var liveDestinationReceiver = FindLiveDestinationReceiver(pipeline);
            if (liveDestinationReceiver != null)
            {
                streamer.ndiReceiverSource = liveDestinationReceiver;

                // CRITICAL: Also update the audio interceptor
                var liveAudioInterceptor = liveDestinationReceiver.GetComponent<NdiAudioInterceptor>();
                if (liveAudioInterceptor != null)
                {
                    streamer.audioInterceptor = liveAudioInterceptor;
                    Debug.Log($"[Audio] Updated audio interceptor to Live destination");
                }
                else
                {
                    Debug.LogError($"[Audio] No NdiAudioInterceptor found on Live destination receiver!");
                }
            }
        }

        streamer.StartStreaming(assignment.sessionId);
        source.renderer.ShowLocalNDI();
    }
    private NdiReceiver FindLiveDestinationReceiver(PipelineType pipeline)
    {
        // Find the Live destination for this pipeline
        var liveDestinations = FindObjectsOfType<PipelineDestination>()
            .Where(dest => dest.pipelineType == pipeline);
    
        return liveDestinations.FirstOrDefault()?.receiver;
    }
    
    private void StartReceiving(PipelineType pipeline, string sessionId)
    {
        isStreaming = false;
        var streamer = GetStreamerForPipeline(pipeline);
        
        if (streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] No streamer for {pipeline}");
            return;
        }
        
        streamer.StartReceiving(sessionId);
    }
    
    #endregion
    
    #region Event Handlers
    
    private void HandleStreamerStateChange(PipelineType pipeline, StreamerState state, string sessionId)
    {
        UpdateSessionState(pipeline, state);
        
        var source = GetSourceForPipeline(pipeline);
        if (source?.renderer == null) return;
        
        switch (state)
        {
            case StreamerState.Failed:
                source.renderer.HandleStreamFailure();
                ClearSession(pipeline);
                break;
                
            case StreamerState.Idle:
                if (IsSessionEnded(pipeline, sessionId))
                {
                    source.renderer.ShowLocalNDI();
                    ClearSession(pipeline);
                }
                break;
        }
    }
    
    
    private void UpdateSessionState(PipelineType pipeline, StreamerState state)
    {
        if (activeSessions[pipeline] != null)
        {
            activeSessions[pipeline].state = state;
        }
    }
    
    private bool IsSessionEnded(PipelineType pipeline, string sessionId)
    {
        return activeSessions[pipeline]?.sessionId == sessionId;
    }
    
    private void ClearSession(PipelineType pipeline)
    {
        activeSessions[pipeline] = null;
    }
    
    #endregion
    
    #region Helper Methods
    
    private StreamSource[] GetActiveSources()
    {
        return streamSources.Where(s => s.isActive && s.renderer != null).ToArray();
    }
    
    private IPipelineSource FindPipelineSourceByName(string sourceName)
    {
        if (string.IsNullOrEmpty(sourceName)) return null;
        
        var allSources = FindObjectsOfType<MonoBehaviour>().OfType<IPipelineSource>();
        return allSources.FirstOrDefault(s => s.ndiName == sourceName);
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
    
    /// <summary>
    /// Get all supported pipeline types
    /// </summary>
    public PipelineType[] GetSupportedPipelines()
    {
        return GetActiveSources().Select(s => s.pipelineType).Distinct().ToArray();
    }
    
    /// <summary>
    /// Check if pipeline is currently streaming or receiving
    /// </summary>
    public bool IsStreamActive(PipelineType pipeline)
    {
        var session = activeSessions.GetValueOrDefault(pipeline);
        return session?.state == StreamerState.Streaming || session?.state == StreamerState.Receiving;
    }


    
    /// <summary>
    /// Force stop stream and return to local content
    /// </summary>
    public void ForceStopStream(PipelineType pipeline)
    {
        GetStreamerForPipeline(pipeline)?.StopSession();
        GetSourceForPipeline(pipeline)?.renderer?.ShowLocalNDI();
        ClearSession(pipeline);
    }
    
    /// <summary>
    /// Get current session information
    /// </summary>
    public StreamSession GetActiveSession(PipelineType pipeline)
    {
        return activeSessions.GetValueOrDefault(pipeline);
    }
    
   
    
    
    
    #endregion
    
    #region Cleanup
    
    private void StopAllStreamers()
    {
        foreach (var streamer in streamers.Values)
        {
            streamer?.StopSession();
        }
        
        streamers.Clear();
        activeSessions.Clear();
    }
    
    #endregion
}