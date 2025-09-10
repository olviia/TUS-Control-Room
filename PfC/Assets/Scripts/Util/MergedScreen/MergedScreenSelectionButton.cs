using System;
using Unity.Android.Gradle.Manifest;
using UnityEngine;
using UnityEngine.UI;
using Util.MergedScreen;

public class MergedScreenSelectionButton : MonoBehaviour, IMergedScreenSelectionButton
{
    public string sceneName;
    
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(OnButtonClicked);
        }
    }
    
    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(OnButtonClicked);
        }
    }

    private void OnButtonClicked()
    {
        IMergedScreenSelectionButton.TriggerEvent(sceneName, this);
    }

    //public static event Action<string, IMergedScreenSelectionButton> OnMergedScreenButtonClicked;
}
