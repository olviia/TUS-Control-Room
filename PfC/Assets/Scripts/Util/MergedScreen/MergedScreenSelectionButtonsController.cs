using System;
using System.Collections.Generic;
using Klak.Ndi;
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
        IMergedScreenSelectionButton.OnMergedScreenButtonClicked += AssignScenesToButtons;
    }

    
    private void AssignScenesToButtons(string arg1, IMergedScreenSelectionButton button)
    {
        if (firstTimeClicked)
        {
            firstTimeClicked = false;

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
                }
            }
            else
            {
                Debug.LogError("Number of buttons on merged screen does not correspond to the number of scenes");
            }
        }
    }
}
