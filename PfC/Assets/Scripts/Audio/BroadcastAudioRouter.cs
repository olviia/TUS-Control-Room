using UnityEngine;
using UnityEngine.Audio;
using BroadcastPipeline;
using System.Collections.Generic;

/// <summary>
/// Routes audio from StudioLive and TVLive destinations to a dedicated mixer for NDI/OBS output.
/// All other audio is routed to the Master mixer for local monitoring only.
/// </summary>
public class BroadcastAudioRouter : MonoBehaviour
{
    [Header("Audio Mixer Configuration")]
    [Tooltip("Master mixer for local monitoring (all audio)")]
    public AudioMixerGroup masterMixerGroup;

    [Tooltip("Broadcast mixer for NDI/OBS output (StudioLive + TVLive only)")]
    public AudioMixerGroup broadcastMixerGroup;

    [Header("Destination Tracking")]
    private Dictionary<PipelineType, PipelineDestination> destinations = new Dictionary<PipelineType, PipelineDestination>();

    void Start()
    {
        // Wait for destinations to register, then setup routing
        Invoke(nameof(SetupAudioRouting), 0.5f);
    }

    void OnEnable()
    {
        // Subscribe to pipeline changes
        if (BroadcastPipelineManager.Instance != null)
        {
            // Listen for when destinations get assigned
            BroadcastPipelineManager.OnActiveSourcesChanged += OnPipelineChanged;
        }
    }

    void OnDisable()
    {
        if (BroadcastPipelineManager.Instance != null)
        {
            BroadcastPipelineManager.OnActiveSourcesChanged -= OnPipelineChanged;
        }
    }

    private void SetupAudioRouting()
    {
        // Find all pipeline destinations
        var allDestinations = FindObjectsOfType<PipelineDestination>();

        foreach (var dest in allDestinations)
        {
            destinations[dest.pipelineType] = dest;

            // Route based on pipeline type
            if (dest.pipelineType == PipelineType.StudioLive || dest.pipelineType == PipelineType.TVLive)
            {
                RouteDestinationToBroadcast(dest);
            }
            else
            {
                RouteDestinationToMaster(dest);
            }
        }

        Debug.Log($"[AudioRouter] Setup complete. Broadcasting: StudioLive + TVLive");
    }

    private void OnPipelineChanged(HashSet<string> activeNdiNames)
    {
        // Re-route audio when pipeline assignments change
        // This ensures correct audio routing after source changes
        foreach (var dest in destinations.Values)
        {
            if (dest.pipelineType == PipelineType.StudioLive || dest.pipelineType == PipelineType.TVLive)
            {
                RouteDestinationToBroadcast(dest);
            }
        }
    }

    /// <summary>
    /// Routes a destination's audio to the broadcast mixer (for NDI/OBS output)
    /// </summary>
    private void RouteDestinationToBroadcast(PipelineDestination destination)
    {
        if (destination == null || destination.receiver == null) return;

        // Find all AudioSources in the NDI receiver's children
        var audioSources = destination.receiver.GetComponentsInChildren<AudioSource>(true);

        foreach (var audioSource in audioSources)
        {
            audioSource.outputAudioMixerGroup = broadcastMixerGroup;
            Debug.Log($"[AudioRouter] Routed {destination.pipelineType} to BROADCAST mixer");
        }
    }

    /// <summary>
    /// Routes a destination's audio to the master mixer (local monitoring only)
    /// </summary>
    private void RouteDestinationToMaster(PipelineDestination destination)
    {
        if (destination == null || destination.receiver == null) return;

        var audioSources = destination.receiver.GetComponentsInChildren<AudioSource>(true);

        foreach (var audioSource in audioSources)
        {
            audioSource.outputAudioMixerGroup = masterMixerGroup;
        }

        Debug.Log($"[AudioRouter] Routed {destination.pipelineType} to MASTER mixer (local only)");
    }

    #region Public API

    /// <summary>
    /// Manually route a specific pipeline to broadcast output
    /// </summary>
    public void AddToBroadcast(PipelineType pipelineType)
    {
        if (destinations.TryGetValue(pipelineType, out var dest))
        {
            RouteDestinationToBroadcast(dest);
        }
    }

    /// <summary>
    /// Remove a specific pipeline from broadcast output
    /// </summary>
    public void RemoveFromBroadcast(PipelineType pipelineType)
    {
        if (destinations.TryGetValue(pipelineType, out var dest))
        {
            RouteDestinationToMaster(dest);
        }
    }

    #endregion
}