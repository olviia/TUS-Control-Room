using System;
using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class LiveButton : MonoBehaviour, IScreensCommunication
{
    [SerializeField] NdiReceiver reciever;
    public void SendToStudio()
    {
        IScreensCommunication.InvokeSendToStudio(reciever.ndiName);
    }
    public void SendToStudioPreview()
    {
        Debug.Log("successfully poked name ndi is " + reciever.ndiName);
        IScreensCommunication.InvokeSendToStudioPreview(reciever.ndiName);
    }
}
