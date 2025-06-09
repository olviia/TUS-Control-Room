using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Klak.Ndi;
using UnityEngine.XR.Interaction.Toolkit;

public class LiveQuad : MonoBehaviour, IScreensCommunication
{
    [SerializeField] NdiReceiver reciever;
    
    void Start()
    {
        var grabInteractable = GetComponent<XRGrabInteractable>();
        if (grabInteractable != null)
        {
            grabInteractable.activated.AddListener(OnQuadClicked);
        }
    }
    
    void OnQuadClicked(ActivateEventArgs args)
    {
            Debug.LogError("Quad clicked with ray!" + reciever.ndiName);
            IScreensCommunication.InvokeSendToStudioPreview(reciever.ndiName);
    }
}
