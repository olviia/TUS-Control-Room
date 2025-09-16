using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BroadcastPipeline;
using Klak.Ndi;
using NUnit.Framework.Internal;
using OBSWebsocketDotNet;
using Unity.Netcode;
using Unity.Services.Vivox.AudioTaps;
using UnityEngine;
using Util.MergedScreen;

public class BroadcastPipelineManager : MonoBehaviour 
{
    [Header("Outline Colors")]
    public Color studioPreviewOutline;  // Blue
    public Color studioLiveOutline;     // Orange  
    public Color tvPreviewOutline;      // Green
    public Color tvLiveOutline;         // Magenta
    public Color conflictOutline;       // Bright Red
    
    public static BroadcastPipelineManager Instance { get; private set; }
    private List<IPipelineSource> registeredSources = new List<IPipelineSource>();
    
    private Dictionary<PipelineType, IPipelineSource> activeAssignments 
                = new Dictionary<PipelineType, IPipelineSource>();
    
    private Dictionary<PipelineType, List<PipelineDestination>> registeredDestinations 
        = new Dictionary<PipelineType, List<PipelineDestination>>();
    
    // Track which live pipelines are controlled by network (other directors)
    private Dictionary<PipelineType, bool> networkControlledPipelines = new Dictionary<PipelineType, bool>();
    
    private NetworkStreamCoordinator networkStreamCoordinator;
    
    public static event Action<HashSet<string>> OnActiveSourcesChanged;

    
    //ugly workaround
    private bool isStudioStreamedForTheFirstTime = true;
    private bool isTVStreamedForTheFirstTime = true;
    private Coroutine streamedForTheFirstTimeCoroutine;
    

    private void Awake()
    {
        Instance = this;
        // Initialize network control tracking
        networkControlledPipelines[PipelineType.StudioLive] = false;
        networkControlledPipelines[PipelineType.TVLive] = false;
    }

    private void Start()
    {
        networkStreamCoordinator = FindObjectOfType<NetworkStreamCoordinator>();
        NetworkStreamCoordinator.OnStreamControlChanged += OnNetworkStreamChanged;
    }

    private void OnDestroy()
    {
        NetworkStreamCoordinator.OnStreamControlChanged -= OnNetworkStreamChanged;
    }

    public void RegisterSource(IPipelineSource source) 
    {
        registeredSources.Add(source);
    }
    
    public void UnregisterSource(IPipelineSource source) 
    {
        registeredSources.Remove(source);
    }

    public void OnSourceLeftClicked(IPipelineSource source)
    {
        // Always assign to TV Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.TVPreview);
    }

    public void OnSourceRightClicked(IPipelineSource source)
    {
        // Always assign to Studio Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.StudioPreview);


    }

    private IEnumerator Reassign(PipelineType preview, PipelineType destination)
    {
        yield return new WaitForSeconds(1f);

        ForwardContentToNextStage(preview, destination);
    }
    
    public void OnDestinationLeftClicked(PipelineDestination destination)
    {
        Debug.Log($"xx_Pipeline Manager: Left click on {destination.pipelineType}");
        // Left clicks only work on TV pipeline
        if(destination.pipelineType == PipelineType.TVPreview)
        {
            ForwardContentToNextStage(PipelineType.TVPreview, PipelineType.TVLive);
            
            if (isTVStreamedForTheFirstTime)
            {
                StartCoroutine(Reassign(PipelineType.TVPreview, PipelineType.TVLive));
                isTVStreamedForTheFirstTime =  false;
            }
        }
    }

    public void OnDestinationRightClicked(PipelineDestination destination)
    {
        Debug.Log($"xx_Pipeline Manager: Right click on {destination.pipelineType}");
        // Right clicks only work on Studio pipeline  
        if(destination.pipelineType == PipelineType.StudioPreview)
        {
            ForwardContentToNextStage(PipelineType.StudioPreview, PipelineType.StudioLive);
            
            if (isStudioStreamedForTheFirstTime)
            {
                StartCoroutine(Reassign(PipelineType.StudioPreview, PipelineType.StudioLive));
                isStudioStreamedForTheFirstTime =  false;
            }
            
        }
    }

    public void RegisterDestination(PipelineDestination destination)
    {
        if (!registeredDestinations.ContainsKey(destination.pipelineType))
        {
            registeredDestinations[destination.pipelineType] = new List<PipelineDestination>();
        }
        registeredDestinations[destination.pipelineType].Add(destination);
    }

    public void UnregisterDestination(PipelineDestination destination)
    {
        if (registeredDestinations.ContainsKey(destination.pipelineType))
        {
            registeredDestinations[destination.pipelineType].Remove(destination);
            
            // Clean up empty lists
            if (registeredDestinations[destination.pipelineType].Count == 0)
            {
                registeredDestinations.Remove(destination.pipelineType);
            }
        }
    }

    public IPipelineSource GetActiveAssignment(PipelineType pipelineType)
    {
        return activeAssignments[pipelineType];
    }
    private void ForwardContentToNextStage(PipelineType fromStage, PipelineType toStage)
    {
        if (activeAssignments.ContainsKey(fromStage))
        {
            IPipelineSource sourceToForward = activeAssignments[fromStage];
            
            // ALWAYS assign locally first for immediate visual feedback
            AssignSourceToPipeline(sourceToForward, toStage);
            Debug.Log($"xx_📺 Local assignment: Forwarded content from {fromStage} to {toStage}");
            
            // Additionally, if going to a Live stage, coordinate with network
            if (toStage == PipelineType.StudioLive || toStage == PipelineType.TVLive)
            {
                Debug.Log($"xx_🌐 Additionally requesting network control for {toStage}");
                
                networkStreamCoordinator?.RequestStreamControl(toStage, sourceToForward.ndiName);
            }
        }
        else
        {
            Debug.Log($"xx_❌ No content in {fromStage} to forward");
        }
    }

    private void AssignSourceToPipeline(IPipelineSource source, PipelineType targetType)
    {
        Debug.Log($"Before assignment - activeAssignments count: {activeAssignments.Count}");
        activeAssignments[targetType] = source;
       // Debug.Log($"After assignment - assigned {source.GetType().Name} with ndiName '{source.ndiName}' to {targetType}");
        
        //to add or remove ndi filter in MergedScreenSource
        var activeNdiNames = new HashSet<string>(activeAssignments.Values.Select(s => s.ndiName));
        OnActiveSourcesChanged?.Invoke(activeNdiNames);
        
        UpdateActiveSourceHighlight();
        UpdateDestinationNDI(targetType);
    }
    

    private void UpdateActiveSourceHighlight()
    {
        // Remove all outlines first
        foreach(IPipelineSource source in registeredSources)
        {
            source.RemoveHighlight();
        }
        
        // Apply outlines based on assignments, but skip network-controlled live pipelines
        foreach(var assignment in activeAssignments)
        {
            PipelineType pipelineType = assignment.Key;
            IPipelineSource source = assignment.Value;
            
            // Skip outline for live pipelines that are controlled by other directors
            bool isLivePipeline = (pipelineType == PipelineType.StudioLive || pipelineType == PipelineType.TVLive);
            bool isNetworkControlled = networkControlledPipelines.ContainsKey(pipelineType) && networkControlledPipelines[pipelineType];
            
            if (isLivePipeline && isNetworkControlled)
            {
                continue;
            }
            
            if (HasConflicts(source))
            {
                source.ApplyConflictHighlight();
            }
            else
            {
                source.ApplyHighlight(pipelineType);
            }
        }
    
    }
    private bool HasConflicts(IPipelineSource source)
    {
        // Get all pipeline types this source is assigned to
        var sourcePipelineTypes = activeAssignments
            .Where(assignment => assignment.Value == source)
            .Select(assignment => assignment.Key)
            .ToList();
        
        // Use the source's own conflict logic
        return source.HasConflictingAssignments(sourcePipelineTypes);
    }
    
    
    private void UpdateDestinationNDI(PipelineType pipelineType)
    {
        if (!registeredDestinations.ContainsKey(pipelineType))
        {
            return;
        }
    
        List<PipelineDestination> destinations = registeredDestinations[pipelineType];
        foreach(var dest in destinations)
        {
            if (activeAssignments.ContainsKey(pipelineType))
            {
                IPipelineSource source = activeAssignments[pipelineType];
                dest.receiver.ndiName = source.ndiName;

                if (pipelineType == PipelineType.TVLive)
                {
                    OBSWebsocket obsWebSocket = ObsSceneSourceOperation.SharedObsWebSocket;
                    
                    
                    string name = ObsUtilities.FindSceneBySourceFilter(obsWebSocket, Constants.DEDICATED_NDI_OUTPUT,
                        "ndi_filter_ndiname",
                        source.ndiName);
                    
                    ObsSceneSourceOperation obsScene = GetComponent<ObsSceneSourceOperation>();
                    
                    //clean obs stream live scene
                    
                    ObsUtilities.ClearScene(obsWebSocket, "StreamLive");
                    //add subtitles
                    obsScene.ConfigureAndExecute("StreamLive", "TVSuper", true, "TVSuper");

                    obsScene.ConfigureAndExecute("StreamLive", name, true, name);
                    
                    // add audio tap for presenters
                    obsScene.ConfigureAndExecute("StreamLive", "PresenterAudio", true, "PresenterAudio");


                }
            }
            else
            {
                dest.receiver.ndiName = "";
            }
        }
    }
    
    private void OnNetworkStreamChanged(StreamAssignment assignment, string description)
    {
        var pipelineType = assignment.pipelineType;
        bool isMyStream = false;
        
        // Check if this is my stream or someone else's
        if (NetworkManager.Singleton != null)
        {
            var localClientId = NetworkManager.Singleton.LocalClientId;
            isMyStream = assignment.isActive && assignment.directorClientId == localClientId;
        }
        
        // Update network control tracking
        if (assignment.isActive && !isMyStream)
        {
            // Another director is controlling this pipeline
            networkControlledPipelines[pipelineType] = true;
            
            // Remove from active assignments to clear the outline
            if (activeAssignments.ContainsKey(pipelineType))
            {
                activeAssignments.Remove(pipelineType);
            }
        }
        else if (assignment.isActive && isMyStream)
        {
            // I am controlling this pipeline
            networkControlledPipelines[pipelineType] = false;
            Debug.Log($"xx_📡 I am now controlling {pipelineType}");
        }
        else if (!assignment.isActive)
        {
            // No one is controlling this pipeline
            networkControlledPipelines[pipelineType] = false;
            Debug.Log($"xx_📺 {pipelineType} returned to local control");
        }
        
        // Update the visual highlighting based on new network state
        UpdateActiveSourceHighlight();
        
        // Log the details
        if (assignment.isActive)
        {
            Debug.Log($"xx_📡 Source: {assignment.streamSourceName}");
        }
    }
}