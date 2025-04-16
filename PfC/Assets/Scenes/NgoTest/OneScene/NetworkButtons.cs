using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkButtons : MonoBehaviour
{
    private NetworkManager m_NetworkManager;
    public TMP_InputField input;
    private string m_ConnectAddress;
    private void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    private void SetInput()
    {
        m_ConnectAddress = input.text;
        m_NetworkManager.GetComponent<UnityTransport>().SetConnectionData(m_ConnectAddress, 7777);
    }

    public void OnServerClick()
    {
        SetInput();
        m_NetworkManager.StartServer();
    }

    public void OnClientClick()
    {
        SetInput();
        m_NetworkManager.StartClient();
    }

    public void OnShutdownClick()
    {
        m_NetworkManager.Shutdown();
    }
}
