using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BroadcastPipeline
{
    public class UIColorHighlightStrategy : IHighlightStrategy
    {
        private Button targetButton;
        private Color originalColor;
        
        private BroadcastPipelineManager pipelineManager;
    
        public UIColorHighlightStrategy(Button button, BroadcastPipelineManager manager)
        {
            targetButton = button;
            pipelineManager = manager;
            originalColor = targetButton.image.color;
        }

        public void ApplyHighlight(PipelineType pipelineType)
        {
            Color highlightColor = GetColorForPipelineType(pipelineType);
            targetButton.image.color = highlightColor;
        }

        public void RemoveHighlight()
        {
            targetButton.image.color = originalColor;
        }

        public void ApplyConflictHighlight()
        {
            RemoveHighlight();
            if (pipelineManager.conflictOutline != null)
            {
                targetButton.image.color = pipelineManager.conflictOutline;
            }
        }

        public bool HasConflictingAssignments(List<PipelineType> assignments)
        {
            // Same conflict logic as material strategy
            bool hasStudioPreview = assignments.Contains(PipelineType.StudioPreview);
            bool hasStudioLive = assignments.Contains(PipelineType.StudioLive);
            bool hasTVPreview = assignments.Contains(PipelineType.TVPreview);
            bool hasTVLive = assignments.Contains(PipelineType.TVLive);
    
            return (hasStudioPreview && hasTVPreview) ||
                   (hasStudioLive && hasTVLive) ||
                   (hasStudioPreview && hasTVLive) ||
                   (hasStudioLive && hasTVPreview);
        }
        
        private Color GetColorForPipelineType(PipelineType pipelineType)
        {
            switch (pipelineType)
            {
                case PipelineType.StudioPreview: return pipelineManager.studioPreviewOutline;
                case PipelineType.StudioLive: return pipelineManager.studioLiveOutline;
                case PipelineType.TVPreview: return pipelineManager.tvPreviewOutline;
                case PipelineType.TVLive: return pipelineManager.tvLiveOutline;
                default: return originalColor;
            }
        }
    }
}