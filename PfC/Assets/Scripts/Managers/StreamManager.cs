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
    
    [Header("Audio Settings")]
    public bool enableAudio = true;
    public Transform audioPosition; // Where to position 3D audio for this pipeline
}

public class StreamSession
{
    public string sessionId;
    public string sourceIdentifier;
    public bool isMyStream;
    public StreamerState state;
}

/// <summary>
/// Enhanced StreamManager with integrated audio streaming support
/// Creates and controls WebRTC streamers with audio for each configured pipeline
/// </summary>
public class StreamManager : MonoBehaviour
{
    [Header("Stream Configuration")]
    public List<StreamSource> streamSources = new List<StreamSource>();
    
    [Header("Audio Settings")]
    public bool globalAudioEnabled = true;
    
    private Dictionary<PipelineType, WebRTCStreamer> streamers = new Dictionary<PipelineType, WebRTCStreamer>();
    private Dictionary<PipelineType, WebRTCAudioStreamer> audioStreamers = new Dictionary<PipelineType, WebRTCAudioStreamer>();
    private Dictionary<PipelineType, StreamSession> activeSessions = new Dictionary<PipelineType, StreamSession>();

    public bool isStreaming;
    
    #region Unity Lifecycle
    
    private void Start()
    {
        ConnectToEvents();
        StartCoroutine(CreateStreamers());
        
        Debug.Log($"[🎯StreamManager] Managing {GetActiveSources().Length} pipelines with audio support");
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
        
        // Connect to audio events
        WebRTCAudioStreamer.OnAudioStreamStateChanged += HandleAudioStateChange;
    }
    
    private void DisconnectFromEvents()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= HandleStreamControlChange;
        WebRTCStreamer.OnStateChanged -= HandleStreamerStateChange;
        WebRTCAudioStreamer.OnAudioStreamStateChanged -= HandleAudioStateChange;
    }
    
    private IEnumerator CreateStreamers()
    {
        yield return new WaitForEndOfFrame();
        
        foreach (var source in GetActiveSources())
        {
            CreateStreamerForSource(source);
        }
        
        ConfigureCoordinator();
        
        Debug.Log($"[🎯StreamManager] Created {streamers.Count} streamers with {audioStreamers.Count} audio streamers");
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
        streamer.ndiReceiverCaptions = source.ndiReceiverCaptions;
        
        // Create Audio Streamer if audio is enabled
        WebRTCAudioStreamer audioStreamer = null;
        if (globalAudioEnabled && source.enableAudio)
        {
            audioStreamer = CreateAudioStreamerForSource(streamerObject, source);
            
            // Link streamer and audio streamer
            streamer.audioStreamer = audioStreamer;
        }
        
        // Configure renderer with audio streamer reference
        ConfigureRenderer(source, audioStreamer);
        
        // Store references
        streamers[source.pipelineType] = streamer;
        if (audioStreamer != null)
        {
            audioStreamers[source.pipelineType] = audioStreamer;
        }
        activeSessions[source.pipelineType] = null;
        
        Debug.Log($"[🎯StreamManager] Created streamer for {source.pipelineType} {(audioStreamer != null ? "with audio" : "video only")}");
    }
    
    private WebRTCAudioStreamer CreateAudioStreamerForSource(GameObject parent, StreamSource source)
    {
        var audioStreamer = parent.AddComponent<WebRTCAudioStreamer>();
        
        // Configure audio streamer
        audioStreamer.pipelineType = source.pipelineType;
        audioStreamer.ndiAudioSource = source.ndiReceiverSource; // Use same NDI receiver for audio
        
        // Set audio position - use source's audioPosition or renderer's transform as fallback
        if (source.audioPosition != null)
        {
            audioStreamer.audioSourcePosition = source.audioPosition;
        }
        else if (source.renderer != null)
        {
            audioStreamer.audioSourcePosition = source.renderer.transform;
        }
        else
        {
            audioStreamer.audioSourcePosition = parent.transform;
        }
        
        Debug.Log($"[🎯StreamManager] Created audio streamer for {source.pipelineType} at position {audioStreamer.audioSourcePosition.name}");
        
        return audioStreamer;
    }
    
    private void ConfigureRenderer(StreamSource source, WebRTCAudioStreamer audioStreamer)
    {
        if (source.renderer != null && source.ndiReceiverSource != null && source.ndiReceiverCaptions != null)
        {
            source.renderer.localNdiReceiver = source.ndiReceiverSource;
            source.renderer.localNdiReceiverCaptions = source.ndiReceiverCaptions;
            source.renderer.pipelineType = source.pipelineType;
            
            // Link audio streamer to renderer
            source.renderer.audioStreamer = audioStreamer;
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
            GetAudioStreamerForPipeline(pipeline)?.StopAudioOperations();
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
        var sourceObject = FindSourceByName(assignment.streamSourceName);
        var streamer = GetStreamerForPipeline(pipeline);
        var audioStreamer = GetAudioStreamerForPipeline(pipeline);
        isStreaming = true;
        
        if (sourceObject?.receiver == null || streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] Cannot start streaming {pipeline} - missing components");
            ShowLocalContent(source);
            return;
        }
        
        // Update NDI source for both video and audio
        streamer.ndiReceiverSource = sourceObject.receiver;
        if (audioStreamer != null)
        {
            audioStreamer.ndiAudioSource = sourceObject.receiver;
        }
        
        streamer.StartStreaming(assignment.sessionId);
        source.renderer.ShowLocalNDI();
        
        Debug.Log($"[🎯StreamManager] Started streaming {assignment.streamSourceName} for {pipeline} {(audioStreamer != null ? "with audio" : "video only")}");
    }
    
    private void StartReceiving(PipelineType pipeline, string sessionId)
    {
        isStreaming = false;
        var streamer = GetStreamerForPipeline(pipeline);
        var audioStreamer = GetAudioStreamerForPipeline(pipeline);
        
        if (streamer == null)
        {
            Debug.LogError($"[🎯StreamManager] No streamer for {pipeline}");
            return;
        }
        
        // Prepare audio receiving if audio streamer exists
        if (audioStreamer != null)
        {
            audioStreamer.PrepareAudioReceiving(sessionId);
        }
        
        streamer.StartReceiving(sessionId);
        Debug.Log($"[🎯StreamManager] Started receiving {pipeline} {(audioStreamer != null ? "with audio" : "video only")}");
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
    
    private void HandleAudioStateChange(PipelineType pipeline, bool isStreaming, string sessionId)
    {
        Debug.Log($"[🎯StreamManager] Audio state changed for {pipeline}: streaming={isStreaming}, session={sessionId}");
        
        // You can add additional audio state handling here if needed
        // For example, UI updates, audio visualization, etc.
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
    
    private WebRTCAudioStreamer GetAudioStreamerForPipeline(PipelineType pipeline)
    {
        return audioStreamers.GetValueOrDefault(pipeline);
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
    /// Check if pipeline has audio enabled
    /// </summary>
    public bool HasAudioEnabled(PipelineType pipeline)
    {
        return audioStreamers.ContainsKey(pipeline);
    }
    
    /// <summary>
    /// Get audio streamer for external volume control
    /// </summary>
    public WebRTCAudioStreamer GetAudioStreamer(PipelineType pipeline)
    {
        return GetAudioStreamerForPipeline(pipeline);
    }
    
    /// <summary>
    /// Force stop stream and return to local content
    /// </summary>
    public void ForceStopStream(PipelineType pipeline)
    {
        GetStreamerForPipeline(pipeline)?.StopSession();
        GetAudioStreamerForPipeline(pipeline)?.StopAudioOperations();
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
    
    /// <summary>
    /// Debug audio state for specific pipeline
    /// </summary>
    public void DebugAudioState(PipelineType pipeline)
    {
        var audioStreamer = GetAudioStreamerForPipeline(pipeline);
        if (audioStreamer != null)
        {
            audioStreamer.DebugAudioState();
        }
        else
        {
            Debug.Log($"[🎯StreamManager] No audio streamer for {pipeline}");
        }
    }
    
    /// <summary>
    /// Debug all audio states
    /// </summary>
    [ContextMenu("Debug All Audio States")]
    public void DebugAllAudioStates()
    {
        Debug.Log($"[🎯StreamManager] === AUDIO DEBUG INFO ===");
        Debug.Log($"Global Audio Enabled: {globalAudioEnabled}");
        Debug.Log($"Audio Streamers: {audioStreamers.Count}");
        
        foreach (var kvp in audioStreamers)
        {
            Debug.Log($"--- {kvp.Key} ---");
            kvp.Value.DebugAudioState();
        }
    }
    
    #endregion
    
    #region Cleanup
    
    private void StopAllStreamers()
    {
        foreach (var streamer in streamers.Values)
        {
            streamer?.StopSession();
        }
        
        foreach (var audioStreamer in audioStreamers.Values)
        {
            audioStreamer?.StopAudioOperations();
        }
        
        streamers.Clear();
        audioStreamers.Clear();
        activeSessions.Clear();
    }
    
    #endregion
}