using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Klak.Ndi;
using UnityEngine;

public class ObsGetSceneByName : MonoBehaviour
{
    private NdiReceiver receiver;
    private bool isFound;

    [Header("Source Name is ignored when any of those checkboxes ticked")]
    [SerializeField] private bool isStudioScreen;
    [SerializeField] private bool isPreviewSreen;
    [SerializeField] private bool isProgrammScreen;
    
    
    [SerializeField] private string sourceName = ""; // Changed in 1 asset

    // Start is called before the first frame update
    void Start()
    {
        receiver = GetComponent<NdiReceiver>();
        StartCoroutine(FindAndConnectToSource());
        if (isStudioScreen)
        {
            //IScreensCommunication.OnSendToStudio += ChangeLiveSend;
        }

        if (isPreviewSreen)
        {
            //IScreensCommunication.OnSendToStudioPreview += ChangeLiveSend;
        }
    }

    // Coroutine to continuously search for the NDI source
    private IEnumerator FindAndConnectToSource()
    {
        // Get an NDI source from NdiFinder which contains sourceName
        // If it is not ready from the beginning, continue searching until
        // at least one suitable NDI source is found and assign it as receiver.ndiName

        while (!isFound)
        {
            // Get available NDI sources
            var availableSources = NdiFinder.sourceNames;
            
            if (availableSources != null)
            {
                foreach (var source in availableSources)
                {
                    // Check if the source name contains our target sourceName
                    if (source.Contains(sourceName) && source.Contains(Environment.MachineName))
                    {
                        // Found a matching source, assign it to the receiver
                        receiver.ndiName = source;
                        isFound = true;
                        Debug.Log($"Found and connected to NDI source: {source}");
                        break;
                    }
                }
            }

            // If not found, wait a bit before trying again
            if (!isFound)
            {
                //Debug.Log($"Searching for NDI source containing: {sourceName}");
                yield return new WaitForSeconds(1f); // Check every second
            }
        }
    }

    void OnDestroy()
    {
        // Clean up when the object is destroyed
        StopAllCoroutines();
    }

    private void ChangeLiveSend(string _name)
    {
        Debug.LogError("this what i get on prefab " + _name);
        receiver.ndiName = _name;
    }
}
