using System.Collections.Generic;
using Klak.Ndi;

namespace BroadcastPipeline
{
        // <summary>
        /// Unified interface for any source that can be assigned to broadcast pipelines
        /// </summary>
        public interface IPipelineSource
        {
            /// <summary>
            /// Handle left click/trigger interaction
            /// </summary>
            void OnSourceLeftClicked();
        
            /// <summary>
            /// Handle right click/trigger interaction
            /// </summary>
            void OnSourceRightClicked();

            /// <summary>
            /// Unregister this source
            /// </summary>
            void Cleanup();
            
            /// <summary>
            /// Name of ndi source that will be populated into broadcast pipeline
            /// </summary>
            string ndiName { get; }

            /// <summary>
            /// Apply visual highlight for the given pipeline type
            /// </summary>
            void ApplyHighlight(PipelineType pipelineType);

            /// <summary>
            /// Remove all visual highlights
            /// </summary>
            void RemoveHighlight();

            /// <summary>
            /// Apply conflict highlight when source has conflicting assignments
            /// </summary>
            void ApplyConflictHighlight();

            /// <summary>
            /// Check if the given assignments would create a conflict for this source type
            /// </summary>
            bool HasConflictingAssignments(List<PipelineType> assignments);
        }
    
}