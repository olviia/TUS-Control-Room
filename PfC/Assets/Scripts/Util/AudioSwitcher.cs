using Klak.Ndi;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using UnityEngine.UI;

public class AudioSwitcher : MonoBehaviour
{
    [SerializeField] private NdiReceiver ndiReceiver;
    private AudioMode currentMode;

    // Call this method from your UI button
    public void ToggleAudioMode()
    {
         if (ndiReceiver != null)
        {
            // Cycle through modes: Automatic -> Virtual Speakers -> None -> Automatic
            currentMode = (AudioMode)(((int)currentMode + 1) % 2);

            ApplyAudioMode();
            
            
            GetReceiveAudioField();
        }
    }

    #region NDI Control

    

    // Enum to represent the different audio modes
    public enum AudioMode
    {        
        Automatic = 0,
        //VirtualSpeakers = 0,
        None = 1
    }

    

    private void Start()
    {
        currentMode = GetReceiveAudioField() ? AudioMode.Automatic : AudioMode.None;
    }



    // Set a specific mode directly
    public void SetAudioMode(AudioMode mode)
    {
        currentMode = mode;
        ApplyAudioMode();
    }

    private void ApplyAudioMode()
    {
        // Access the serialized fields directly
        // Note: In builds, you won't have the editor GUI functionality,
        // so we need to modify the component properties directly

        switch (currentMode)
        {
            case AudioMode.Automatic:
                 //Automatic mode
                 SetReceiveAudioField(true);
                 SetCreateVirtualSpeakersField(false);
                 break;

            // case AudioMode.VirtualSpeakers:
            //     // Always create Virtual Speakers
            //     SetReceiveAudioField(true); (maybe this one should be false)
            //     SetCreateVirtualSpeakersField(true);
            //     break;

            case AudioMode.None:
                // No audio
                SetReceiveAudioField(false);
                break;
        }

        // Trigger any necessary updates in the NdiReceiver
        //ndiReceiver.Restart();

    }

    // Helper method to set the _createVirtualSpeakers field via reflection
    // (since it's not exposed as a public property)
    private void SetCreateVirtualSpeakersField(bool value)
    {
        var field = typeof(NdiReceiver).GetField("_createVirtualSpeakers",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
            field.SetValue(ndiReceiver, value);
    }

    private void SetReceiveAudioField(bool value)
    {
        var field = typeof(NdiReceiver).GetField("_receiveAudio",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field != null)
            field.SetValue(ndiReceiver, value);
        else
            Debug.LogError("Could not find _receiveAudio field in NdiReceiver");
    }    
    private bool GetReceiveAudioField()
    {
        var field = typeof(NdiReceiver).GetField("_receiveAudio",
            BindingFlags.NonPublic | BindingFlags.Instance);

        return (bool)(field?.GetValue(ndiReceiver));
    }
#endregion


}