using System;
using System.Collections;
using System.Collections.Generic;
using DefaultNamespace;
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
    
    private Dictionary<PipelineType, PipelineDestination> registeredDestinations 
                = new Dictionary<PipelineType, PipelineDestination>();

    private void Awake()
    {
        Instance = this;
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
        Debug.Log($"Pipeline Manager: Left click on {source.gameObject.name}");
        // Always assign to TV Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.TVPreview);
    }

    public void OnSourceRightClicked(SourceObject source)
    {
        Debug.Log($"Pipeline Manager: Right click on {source.gameObject.name}");
        // Always assign to Studio Preview (overwrite if already there)
        AssignSourceToPipeline(source, PipelineType.StudioPreview);
    }
    
    public void OnDestinationLeftClicked(PipelineDestination destination)
    {
        Debug.Log($"Pipeline Manager: Left click on {destination.pipelineType}");
        // Left clicks only work on TV pipeline
        if(destination.pipelineType == PipelineType.TVPreview)
        {
            ForwardContentToNextStage(PipelineType.TVPreview, PipelineType.TVLive);
        }
    }

    public void OnDestinationRightClicked(PipelineDestination destination)
    {
        Debug.Log($"Pipeline Manager: Right click on {destination.pipelineType}");
        // Right clicks only work on Studio pipeline  
        if(destination.pipelineType == PipelineType.StudioPreview)
        {
            ForwardContentToNextStage(PipelineType.StudioPreview, PipelineType.StudioLive);
        }
    }

    public void RegisterDestination(PipelineDestination destination)
    {
        registeredDestinations[destination.pipelineType] = destination;
    }

    public void UnregisterDestination(PipelineDestination destination)
    {
        registeredDestinations.Remove(destination.pipelineType);
    }
    
    private void ForwardContentToNextStage(PipelineType fromStage, PipelineType toStage)
    {
        if (activeAssignments.ContainsKey(fromStage))
        {
            SourceObject sourceToForward = activeAssignments[fromStage];
            AssignSourceToPipeline(sourceToForward, toStage);
            Debug.Log($"Forwarded content from {fromStage} to {toStage}");
        }
        else
        {
            Debug.Log($"No content in {fromStage} to forward");
        }
    }

    private void AssignSourceToPipeline(SourceObject source, PipelineType targetType)
    {
        activeAssignments[targetType] = source;
        Debug.Log($"Assigned {source.gameObject.name} to {targetType}");
        UpdateActiveSourceHighlight ();
        UpdateDestinationNDI(targetType);
    }

    private void UpdateActiveSourceHighlight()
    {
        // Remove all outlines first
        foreach(SourceObject source in registeredSources)
        {
            RemoveOutline(source);
        }
        // Apply outlines based on assignments
        foreach(var assignment in activeAssignments)
        {
            Material outlineMaterial = GetOutlineMaterialForPipelineType(assignment.Key);
            SetOutline(source: assignment.Value, outlineMaterial: outlineMaterial);
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
                Debug.Log($"CONFLICT: {source.gameObject.name} assigned to conflicting pipelines");
                RemoveOutline(source);
                SetOutline(source, conflictOutline);
                // TODO: Make outline twice as thick for conflicts
            }
        }
    }

    private bool HasConflict(List<PipelineType> assignments)
    {
        bool hasStudioPreview = assignments.Contains(PipelineType.StudioPreview);
        bool hasStudioLive = assignments.Contains(PipelineType.StudioLive);
        bool hasTVPreview = assignments.Contains(PipelineType.TVPreview);
        bool hasTVLive = assignments.Contains(PipelineType.TVLive);
    
        // Conflict scenarios we discussed
        return (hasStudioPreview && hasTVPreview) ||    // Same source in both previews
               (hasStudioLive && hasTVLive) ||          // Same source in both live
               (hasStudioPreview && hasTVLive) ||       // Studio preview + TV live
               (hasStudioLive && hasTVPreview);         // Studio live + TV preview
    }
    private void UpdateDestinationNDI(PipelineType pipelineType)
    {
        if (!registeredDestinations.ContainsKey(pipelineType))
        {
            Debug.LogWarning($"No destination registered for {pipelineType}");
            return;
        }
    
        PipelineDestination destination = registeredDestinations[pipelineType];
    
        if (activeAssignments.ContainsKey(pipelineType))
        {
            // Assign NDI source name to destination
            SourceObject source = activeAssignments[pipelineType];
            if (source.receiver != null && destination.receiver != null)
            {
                destination.receiver.ndiName = source.receiver.ndiName;
                Debug.Log($"Assigned NDI source '{source.receiver.ndiName}' to {pipelineType}");
            }
        }
        else
        {
            // Clear NDI source name  
            if (destination.receiver != null)
            {
                destination.receiver.ndiName = "";
                Debug.Log($"Cleared NDI source from {pipelineType}");
            }
        }
    }
}


