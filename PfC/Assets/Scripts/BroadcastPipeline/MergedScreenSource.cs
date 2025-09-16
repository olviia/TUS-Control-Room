using System.Collections.Generic;
using System.Linq;
using OBSWebsocketDotNet;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace BroadcastPipeline
{
    public class MergedScreenSource: MonoBehaviour, IPipelineSource
    {
        //obs scene name is set as an ndi filter name for its media source
        private string obsSceneName;
        private IHighlightStrategy highlightStrategy;
        private Button button;

        private OBSWebsocket websocket;
        private string singleSource;
        
        public string ndiName { get; private set; }
        
        public void Initialize(string sceneName, Button button, BroadcastPipelineManager manager)
        {
            obsSceneName = sceneName;
                ndiName = $"{System.Environment.MachineName} ({sceneName})";
            Debug.Log("scene name: "+ ndiName);
            highlightStrategy = new UIColorHighlightStrategy(button, manager);
            this.button = button;
            
            
            websocket = ObsOperationBase.SharedObsWebSocket;
            //keep only one media source in subscenes, as only the first in the list will be processed
            List<string> sourcesInScene = ObsUtilities.GetSourceNamesInScene(websocket,  obsSceneName);
            singleSource = sourcesInScene.First();
            
            // Register with pipeline manager
            manager?.RegisterSource(this);
            BroadcastPipelineManager.OnActiveSourcesChanged += CheckAndChangeFilterInObs;

            //subscribe to event from broadcast
        }
        
        
        public void OnSourceLeftClicked()
        {
            Debug.Log($"Left clicked merged screen: {obsSceneName}");
            BroadcastPipelineManager.Instance?.OnSourceLeftClicked(this);
        }

        public void OnSourceRightClicked()
        {
            Debug.Log($"Right clicked merged screen: {obsSceneName}");
            BroadcastPipelineManager.Instance?.OnSourceRightClicked(this);
        }

        public void ApplyHighlight(PipelineType pipelineType)
        {
            highlightStrategy?.ApplyHighlight(pipelineType);        }

        public void RemoveHighlight()
        {
            highlightStrategy?.RemoveHighlight();
        }

        public void ApplyConflictHighlight()
        {
            highlightStrategy?.ApplyConflictHighlight();
        }

        public bool HasConflictingAssignments(List<PipelineType> assignments)
        {
            return highlightStrategy?.HasConflictingAssignments(assignments) ?? false;
        }
        public void Cleanup()
        {
            BroadcastPipelineManager.Instance?.UnregisterSource(this);
        }


        private void CheckAndChangeFilterInObs(HashSet<string> activeNdiNames)
        {
            bool isActive = false;
            foreach (var ndi in activeNdiNames)
            {
               isActive = ndi.Contains(obsSceneName);
                if (isActive)
                    break;

            }
            Debug.Log($"single sours: {singleSource}, name: {obsSceneName}");
            if (isActive && !ObsUtilities.FilterExists(websocket, singleSource, obsSceneName))
            {
                ObsUtilities.CreateNdiOutputFilter(websocket, singleSource, Constants.DEDICATED_NDI_OUTPUT, obsSceneName, "ndi_filter_ndiname");
            }
            else if (!isActive && ObsUtilities.FilterExists(websocket, singleSource, obsSceneName))
            {
                Debug.Log($"removed filter from {obsSceneName}");

                ObsUtilities.RemoveNdiOutputFilter(websocket, singleSource, Constants.DEDICATED_NDI_OUTPUT);
            }
        }

    }
}