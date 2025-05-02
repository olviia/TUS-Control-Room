using System;
using System.Collections.Generic;
using System.Linq;
using Klak.Ndi;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NDISourceUI : MonoBehaviour
{
    public NdiReceiver receiver;
    public Button refresh;
    public GameObject sources;
    public Button buttonPrefab;
    public Button show;

    public bool immediatelyChooseAvailableStream = true;

    public static event Action<string> onNdiChanged;

    private void OnEnable()
    {
        RefreshSources();
        if (immediatelyChooseAvailableStream && string.IsNullOrEmpty(receiver.ndiName))
            receiver.ndiName = NdiFinder.EnumerateSourceNames().FirstOrDefault();
    }

    private void Start()
    {
        refresh.onClick.AddListener(RefreshSources);
        show.onClick.AddListener(ShowSources);
    }

    private void ChangeSource(string sourceName)
    {

        Debug.Log("Changing source to " + sourceName);
        receiver.ndiName = sourceName;
        onNdiChanged?.Invoke(sourceName);
    }

    private void ShowSources()
    {
        sources.SetActive(!sources.activeInHierarchy);
    }

    private void RefreshSources()
    {
        //destroy existing buttons
        List<Transform> allChildren = new List<Transform>();
        foreach (Transform child in sources.GetComponentsInChildren<Transform>(true))
        {
            if (child != sources.transform)
            {
                allChildren.Add(child);
            }
        }

        foreach (var child in allChildren)
        {
            Destroy(child.gameObject);
        }

        //create new buttons
        foreach (var source in NdiFinder.EnumerateSourceNames())
        {
            var newButton = Instantiate(buttonPrefab, sources.transform);
            newButton.name = source;
            newButton.GetComponentInChildren<Text>().text = source;
            newButton.onClick.AddListener(delegate 
            { 
                ChangeSource(source); 
            }
            );
        }
    }

}