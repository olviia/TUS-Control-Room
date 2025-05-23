using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class LiveButton : MonoBehaviour
{
    [SerializeField] NdiReceiver reciever;
    public void SendToLive()
    {
        FindAnyObjectByType<LiveScreenSettings>().studioScreenNdiName = reciever.ndiName;
    }
}
