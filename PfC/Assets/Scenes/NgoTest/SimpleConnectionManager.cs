using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class SimpleConnectionManager : MonoBehaviour
{
    //[SerializeField] private TMP_InputField ipInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    private void Start()
    {
        hostButton.onClick.AddListener(StartHost);
        clientButton.onClick.AddListener(StartClient);
    }

    private void StartHost()
    {
        NetworkManager.Singleton.StartHost();
    }

    private void StartClient()
    {   //ignore it and chose ip in unity transport

        //string ip = string.IsNullOrEmpty(ipInput.text) ? "127.0.0.1" : ipInput.text;

        //var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        //transport.ConnectionData.Address = ip;

        NetworkManager.Singleton.StartClient();
    }
}