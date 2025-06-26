using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BroadcastPipeline;
using Klak.Ndi;
using OBSWebsocketDotNet;
using Unity.Netcode;
using UnityEngine;

public class BroadcastPipelineManager : MonoBehaviour 
{
    [Header("Outline Colors")]
    public Material studioPreviewOutline;  // Blue
    public Material studioLiveOutline;     // Orange  
    public Material tvPreviewOutline;      // Green
    public Material tvLiveOutline;         // Magenta
    public Material conflictOutline;       // Bright Red
    
    public static BroadcastPipelineManager Instance { get; private set; }
    private List<SourceObject> registeredSources = new List<SourceObject>();
    
    private Dictionary<PipelineType, SourceObject> activeAssignments 
                = new Dictionary<PipelineType, SourceObject>();
    
    private Dictionary<PipelineType, List<PipelineDestination>> registeredDestinations 
        = new Dictionary<PipelineType, List<PipelineDestination>>();
    
    // Track which live pipelines are controlled by network (other directors)
    private Dictionary<PipelineType, bool> networkControlledPipelines = new Dictionary<PipelineType, bool>();
    
    private NetworkStreamCoordinator networkStreamCoordinator;
    

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

    public void RegisterSource(SourceObject source) 
    {
        registeredSources.Add(source);
    }
    
    public void UnregisterSource(SourceObject source) 
    {
        registeredSources.Remove(source);
    }

    public void OnSourceLeftClicked(SourceObject source)
    {
        Debug.Log($"xx_Pipeline Manager: Left click on {source.gameObject.name}");
        // Always assign to TV Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.TVPreview);
    }

    public void OnSourceRightClicked(SourceObject source)
    {
        Debug.Log($"xx_Pipeline Manager: Right click on {source.gameObject.name}");
        // Always assign to Studio Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.StudioPreview);
    }
    
    public void OnDestinationLeftClicked(PipelineDestination destination)
    {
        Debug.Log($"xx_Pipeline Manager: Left click on {destination.pipelineType}");
        // Left clicks only work on TV pipeline
        if(destination.pipelineType == PipelineType.TVPreview)
        {
            ForwardContentToNextStage(PipelineType.TVPreview, PipelineType.TVLive);
        }
    }

    public void OnDestinationRightClicked(PipelineDestination destination)
    {
        Debug.Log($"xx_Pipeline Manager: Right click on {destination.pipelineType}");
        // Right clicks only work on Studio pipeline  
        if(destination.pipelineType == PipelineType.StudioPreview)
        {
            ForwardContentToNextStage(PipelineType.StudioPreview, PipelineType.StudioLive);
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
    
    private void ForwardContentToNextStage(PipelineType fromStage, PipelineType toStage)
    {
        if (activeAssignments.ContainsKey(fromStage))
        {
            SourceObject sourceToForward = activeAssignments[fromStage];
            
            // ALWAYS assign locally first for immediate visual feedback
            AssignSourceToPipeline(sourceToForward, toStage);
            Debug.Log($"xx_📺 Local assignment: Forwarded content from {fromStage} to {toStage}");
            
            // Additionally, if going to a Live stage, coordinate with network
            if (toStage == PipelineType.StudioLive || toStage == PipelineType.TVLive)
            {
                Debug.Log($"xx_🌐 Additionally requesting network control for {toStage}");
                
                networkStreamCoordinator?.RequestStreamControl(toStage, sourceToForward.receiver);
            }
        }
        else
        {
            Debug.Log($"xx_❌ No content in {fromStage} to forward");
        }
    }

    private void AssignSourceToPipeline(SourceObject source, PipelineType targetType)
    {
        activeAssignments[targetType] = source;
        Debug.Log($"xx_Assigned {source.gameObject.name} to {targetType}");
        UpdateActiveSourceHighlight();
        UpdateDestinationNDI(targetType);
    }

    private void UpdateActiveSourceHighlight()
    {
        // Remove all outlines first
        foreach(SourceObject source in registeredSources)
        {
            RemoveOutline(source);
        }
        
        // Apply outlines based on assignments, but skip network-controlled live pipelines
        foreach(var assignment in activeAssignments)
        {
            PipelineType pipelineType = assignment.Key;
            SourceObject source = assignment.Value;
            
            // Skip outline for live pipelines that are controlled by other directors
            bool isLivePipeline = (pipelineType == PipelineType.StudioLive || pipelineType == PipelineType.TVLive);
            bool isNetworkControlled = networkControlledPipelines.ContainsKey(pipelineType) && networkControlledPipelines[pipelineType];
            
            if (isLivePipeline && isNetworkControlled)
            {
                Debug.Log($"xx_Skipping outline for {pipelineType} - controlled by another director");
                continue;
            }
            
            Material outlineMaterial = GetOutlineMaterialForPipelineType(pipelineType);
            if (outlineMaterial != null)
            {
                SetOutline(source, outlineMaterial);
            }
        }
    
        // Check for conflicts and apply warning outlines
        CheckAndApplyConflictHighlights();
    }
    
    private Material GetOutlineMaterialForPipelineType(PipelineType pipelineType)
    {
        switch(pipelineType)
        {
            case PipelineType.StudioPreview: return studioPreviewOutline;
            case PipelineType.StudioLive: return studioLiveOutline;
            case PipelineType.TVPreview: return tvPreviewOutline;
            case PipelineType.TVLive: return tvLiveOutline;
            default: return null;
        }
    }
    
    private void SetOutline(SourceObject source, Material outlineMaterial)
    {
        MeshRenderer renderer = source.screenGameObject;
    
        // Remove any existing outline first
        RemoveOutline(source);
    
        // Add outline material to the array
        Material[] materials = renderer.materials;
        Material[] newMaterials = new Material[materials.Length + 1];
    
        // Copy existing materials
        for(int i = 0; i < materials.Length; i++)
        {
            newMaterials[i] = materials[i];
        }
    
        // Add outline material at the end
        newMaterials[materials.Length] = outlineMaterial;
        renderer.materials = newMaterials;
    }
    
    private void RemoveOutline(SourceObject source)
    {
        MeshRenderer renderer = source.screenGameObject;
        Material[] materials = renderer.materials;
    
        // If only one material, no outline to remove
        if(materials.Length <= 1) return;
    
        // Remove last material (outline)
        Material[] newMaterials = new Material[materials.Length - 1];
        for(int i = 0; i < newMaterials.Length; i++)
        {
            newMaterials[i] = materials[i];
        }
    
        renderer.materials = newMaterials;
    }
    
    private void CheckAndApplyConflictHighlights()
    {
        // Group sources by how many assignments they have
        Dictionary<SourceObject, List<PipelineType>> sourceAssignments = new Dictionary<SourceObject, List<PipelineType>>();
    
        foreach(var assignment in activeAssignments)
        {
            SourceObject source = assignment.Value;
            PipelineType pipelineType = assignment.Key;
            
            // Skip network-controlled live pipelines for conflict detection
            bool isLivePipeline = (pipelineType == PipelineType.StudioLive || pipelineType == PipelineType.TVLive);
            bool isNetworkControlled = networkControlledPipelines.ContainsKey(pipelineType) && networkControlledPipelines[pipelineType];
            
            if (isLivePipeline && isNetworkControlled)
            {
                continue; // Don't count network-controlled pipelines in conflicts
            }
        
            if(!sourceAssignments.ContainsKey(source))
                sourceAssignments[source] = new List<PipelineType>();
            
            sourceAssignments[source].Add(pipelineType);
        }
    
        // Check for conflicts and apply red warning outline
        foreach(var sourceGroup in sourceAssignments)
        {
            SourceObject source = sourceGroup.Key;
            List<PipelineType> assignments = sourceGroup.Value;
        
            if(HasConflict(assignments))
            {
                Debug.Log($"xx_CONFLICT: {source.gameObject.name} assigned to conflicting pipelines");
                RemoveOutline(source);
                SetOutline(source, conflictOutline);
            }
        }
    }

    private bool HasConflict(List<PipelineType> assignments)
    {
        bool hasStudioPreview = assignments.Contains(PipelineType.StudioPreview);
        bool hasStudioLive = assignments.Contains(PipelineType.StudioLive);
        bool hasTVPreview = assignments.Contains(PipelineType.TVPreview);
        bool hasTVLive = assignments.Contains(PipelineType.TVLive);
    
        // Conflict scenarios 
        return (hasStudioPreview && hasTVPreview) ||    // Same source in both previews
               (hasStudioLive && hasTVLive) ||          // Same source in both live
               (hasStudioPreview && hasTVLive) ||       // Studio preview + TV live
               (hasStudioLive && hasTVPreview);         // Studio live + TV preview
    }
    
    private void UpdateDestinationNDI(PipelineType pipelineType)
    {
        if (!registeredDestinations.ContainsKey(pipelineType))
        {
            Debug.Log($"xx_No destination registered for {pipelineType}");
            return;
        }
    
        List<PipelineDestination> destinations = registeredDestinations[pipelineType];
        foreach(var dest in destinations)
        {
            if (activeAssignments.ContainsKey(pipelineType))
            {
                SourceObject source = activeAssignments[pipelineType];
                dest.receiver.ndiName = source.receiver.ndiName;

                if (pipelineType == PipelineType.TVLive)
                {
                    //move it to ObsSceneSourceOperation
                    //add code to remove the item
                    //to do it, use RemoveSceneItem()
                    OBSWebsocket obsWebSocket = ObsSceneSourceOperation.SharedObsWebSocket;
                    
                    string name = ObsUtilities.FindSceneBySourceFilter(obsWebSocket, "Dedicated NDI® output",
                        "ndi_filter_ndiname",
                        source.receiver.ndiName);
                    
                    ObsSceneSourceOperation obsScene = GetComponent<ObsSceneSourceOperation>();
                    obsScene.ConfigureAndExecute("StreamLive", name, true, name);
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
        Debug.Log($"xx_🔄 Network Stream Update: {description}");
        
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
                Debug.Log($"xx_Removing {pipelineType} from active assignments - controlled by another director");
                activeAssignments.Remove(pipelineType);
            }
            Debug.Log($"xx_📡 {pipelineType} now controlled by Director {assignment.directorClientId}");
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