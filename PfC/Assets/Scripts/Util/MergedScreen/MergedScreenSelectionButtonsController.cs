using System;
using System.Collections.Generic;
using BroadcastPipeline;
using Klak.Ndi;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;
using Util.MergedScreen;

public class MergedScreenSelectionButtonsController : MonoBehaviour
{
    [SerializeField] private NdiReceiver ndiReceiver;
    [SerializeField] private List<MergedScreenSelectionButton> buttons;

    private bool firstTimeClicked = true;
    private void Start()
    {
        GameObject.FindFirstObjectByType<WebsocketManager>().WsConnected += AssignScenesToButtons;
    }

    
    private void AssignScenesToButtons(bool isConnected)
    {
        if (isConnected)
        {
            //firstTimeClicked = false;

            var sceneName = ObsUtilities.FindSceneBySceneFilter(ObsOperationBase.SharedObsWebSocket,
                "Dedicated NDI® output",
                "ndi_filter_ndiname",
                ndiReceiver.ndiName);
            
            Debug.LogError($"merged scene: {sceneName}");

            List<string> sourceNames = ObsUtilities.GetSourceNamesInScene(ObsOperationBase.SharedObsWebSocket,
                sceneName);

            if (sourceNames.Count == buttons.Count)
            {
                for (int i = 0; i < sourceNames.Count; i++)
                {
                    buttons[i].sceneName = sourceNames[i];
                    //here is the name of ndi source that will be created
                    //ndi name is the name of the scene, but the filter in obs is applied
                    //to the media source inside that scene
                    
                    var mergedScreenSource = buttons[i].gameObject.AddComponent<MergedScreenSource>();
                    mergedScreenSource.Initialize(sourceNames[i], buttons[i].button, BroadcastPipelineManager.Instance);

                }
            }
            else
            {
                Debug.LogError("Number of buttons on merged screen does not correspond to the number of scenes");
            }
        }
    }
}
