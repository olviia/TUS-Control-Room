using UnityEngine;
using Unity.WebRTC;
using System.Collections;
using BroadcastPipeline;

/// <summary>
/// Centralized WebRTC engine manager with reference counting
/// Prevents pipeline interference by managing single WebRTC.Update() coroutine
/// </summary>
public class WebRTCEngineManager : MonoBehaviour
{
    private static WebRTCEngineManager _instance;
    private Coroutine _updateCoroutine;
    private int _activeStreamers = 0;
    private readonly object _lock = new object();
    
    public static WebRTCEngineManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("WebRTC Engine Manager");
                _instance = go.AddComponent<WebRTCEngineManager>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }
    
    /// <summary>
    /// Register a new streamer - starts engine if first streamer
    /// </summary>
    public void RegisterStreamer(PipelineType pipeline)
    {
        lock (_lock)
        {
            if (_activeStreamers == 0)
            {
                _updateCoroutine = StartCoroutine(WebRTC.Update());
                Debug.Log("[⚙️WebRTCEngine] Started WebRTC engine");
            }
            _activeStreamers++;
            Debug.Log($"[⚙️WebRTCEngine] Registered {pipeline}, active: {_activeStreamers}");
        }
    }
    
    /// <summary>
    /// Unregister a streamer - stops engine if last streamer
    /// </summary>
    public void UnregisterStreamer(PipelineType pipeline)
    {
        lock (_lock)
        {
            _activeStreamers = Mathf.Max(0, _activeStreamers - 1);
            Debug.Log($"[⚙️WebRTCEngine] Unregistered {pipeline}, active: {_activeStreamers}");
            
            if (_activeStreamers == 0 && _updateCoroutine != null)
            {
                StopCoroutine(_updateCoroutine);
                _updateCoroutine = null;
                Debug.Log("[⚙️WebRTCEngine] Stopped WebRTC engine");
            }
        }
    }
    
    /// <summary>
    /// Check if WebRTC engine is running
    /// </summary>
    public bool IsEngineRunning => _updateCoroutine != null;
    
    /// <summary>
    /// Get active streamer count
    /// </summary>
    public int ActiveStreamerCount => _activeStreamers;
    
    void OnDestroy()
    {
        if (_updateCoroutine != null)
        {
            StopCoroutine(_updateCoroutine);
            _updateCoroutine = null;
        }
    }
}