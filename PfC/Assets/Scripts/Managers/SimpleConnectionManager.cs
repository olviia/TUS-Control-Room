using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System;
using UnityEngine.Serialization;

public class SimpleConnectionManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private Button directorButton;
    [SerializeField] private Button journalistButton;
    [SerializeField] private Button audienceButton;
    [SerializeField] private SpawnManager spawnManager;

    //[Serializable]
    //public class LoadBasedOnRole : UnityEvent<Role> { }

    private void Start()
    {
        directorButton.onClick.AddListener(() => LoadBasedOnRole(Role.Director));
        journalistButton.onClick.AddListener(() => LoadBasedOnRole(Role.Presenter));
        audienceButton.onClick.AddListener(() => LoadBasedOnRole(Role.Audience));
    }

    private void LoadBasedOnRole(Role role)
    {
        AssignIP();
        if ( role == Role.Director)
        {
           NetworkManager.Singleton.StartHost();
            
        }
        else
        {
            NetworkManager.Singleton.StartClient();
        }
        
        SetLocation(role);
        CommunicationManager.Instance.SetRole(role);
    }

    private void AssignIP()
    { 
        string ip = String.IsNullOrEmpty(ipInput.text) ? "127.0.0.1" : ipInput.text;
        UnityTransport transport = NetworkManager.Singleton.transform.GetComponent<UnityTransport>();
        transport.SetConnectionData(ip,7777);
    }

    private void SetLocation(Role role)
    {
        GameObject xrGameObject = GameObject.FindGameObjectWithTag("XR");
        spawnManager.PlacePlayer(xrGameObject, role);
    }
}