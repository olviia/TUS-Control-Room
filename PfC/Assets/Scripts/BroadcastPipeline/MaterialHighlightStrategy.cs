using System.Collections.Generic;
using UnityEngine;

namespace BroadcastPipeline
{
    public class MaterialHighlightStrategy : IHighlightStrategy
{
    private MeshRenderer targetRenderer;
    private BroadcastPipelineManager pipelineManager;
    private int originalMaterialCount;
    private Color originalColor;

    public MaterialHighlightStrategy(MeshRenderer renderer, BroadcastPipelineManager manager)
    {
        targetRenderer = renderer;
        pipelineManager = manager;
        originalMaterialCount = renderer.materials.Length;
        originalColor = renderer.material.color;  // Store original color
    }
    
    public void ApplyHighlight(PipelineType pipelineType)
    {
        Color outlineMaterial = GetOutlineMaterialForPipelineType(pipelineType);
        if (outlineMaterial != null)
        {
            AddOutlineMaterial(outlineMaterial);
        }
    }

    public void RemoveHighlight()
    {
        // Reset to original material count
        Material[] materials = targetRenderer.materials;
        if (materials.Length > originalMaterialCount)
        {
            Material[] newMaterials = new Material[originalMaterialCount];
            for (int i = 0; i < originalMaterialCount; i++)
            {
                newMaterials[i] = materials[i];
            }
            targetRenderer.materials = newMaterials;
        }

        // Reset color to original
        targetRenderer.material.color = originalColor;
    }

    public void ApplyConflictHighlight()
    {

        if (pipelineManager.conflictOutline != null)
        {
            
            AddOutlineMaterial(pipelineManager.conflictOutline);
        }
    }

    public bool HasConflictingAssignments(List<PipelineType> assignments)
    {
        bool hasStudioPreview = assignments.Contains(PipelineType.StudioPreview);
        bool hasStudioLive = assignments.Contains(PipelineType.StudioLive);
        bool hasTVPreview = assignments.Contains(PipelineType.TVPreview);
        bool hasTVLive = assignments.Contains(PipelineType.TVLive);
    
        // Conflict scenarios 
        return (hasStudioPreview && hasTVPreview) ||    // Same source in both previews
               (hasStudioLive && hasTVLive) ||          // Same source in both live
               (hasStudioPreview && hasTVLive) ||       // Studio preview + TV live
               (hasStudioLive && hasTVPreview); 
    }
    private void AddOutlineMaterial(Color outlineMaterial)
    {

        targetRenderer.material.color = outlineMaterial;
    }
    private Color GetOutlineMaterialForPipelineType(PipelineType pipelineType)
    {
        switch (pipelineType)
        {
            case PipelineType.StudioPreview: return pipelineManager.studioPreviewOutline;
            case PipelineType.StudioLive: return pipelineManager.studioLiveOutline;
            case PipelineType.TVPreview: return pipelineManager.tvPreviewOutline;
            case PipelineType.TVLive: return pipelineManager.tvLiveOutline;
            default: return Color.white;
        }
    }
}
}