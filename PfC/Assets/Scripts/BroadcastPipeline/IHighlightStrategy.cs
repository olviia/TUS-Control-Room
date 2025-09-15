using System.Collections.Generic;

namespace BroadcastPipeline
{
    /// <summary>
    /// Strategy pattern for different highlight implementations
    /// </summary>
    public interface IHighlightStrategy
    {
        /// <summary>
        /// Apply highlight for specific pipeline type
        /// </summary>
        void ApplyHighlight(PipelineType pipelineType);
        
        /// <summary>
        /// Remove all highlights
        /// </summary>
        void RemoveHighlight();
        
        /// <summary>
        /// Apply conflict warning highlight
        /// </summary>
        void ApplyConflictHighlight();
        /// <summary>
        /// Check if the given assignments would create a conflict for this source type
        /// </summary>
        bool HasConflictingAssignments(List<PipelineType> assignments);
    }
}