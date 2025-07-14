using System;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

public class SimpleConnectionManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private Button directorButton;
    [SerializeField] private Button journalistButton;
    [SerializeField] private Button audienceButton;
    [SerializeField] private Button autoDetectIPButton;
    [SerializeField] private Button scanForHostsButton;
    [SerializeField] private SpawnManager spawnManager;
    
    // Connection settings
    [SerializeField] private ushort port = 7777;
    [SerializeField] private ushort broadcastPort = 7778; // Different port for discovery
    [SerializeField] private float connectionTimeout = 5f;
    
    private Coroutine connectionAttemptCoroutine;
    private System.Net.Sockets.UdpClient udpBroadcaster;
    private System.Net.Sockets.UdpClient udpListener;
    private bool isHostBroadcasting = false;
    
    // Connection state tracking
    private enum ConnectionState { Idle, ConnectingAsClient, BecomingHost }
    private ConnectionState currentState = ConnectionState.Idle;
    private Role pendingRole;
    
    private bool hostAlreadyRunning = false;

    private void Start()
    {
        directorButton.onClick.AddListener(() => LoadBasedOnRole(Role.Director));
        journalistButton.onClick.AddListener(() => LoadBasedOnRole(Role.Presenter));
        audienceButton.onClick.AddListener(() => LoadBasedOnRole(Role.Audience));
        autoDetectIPButton.onClick.AddListener(AutoDetectIP);
        scanForHostsButton.onClick.AddListener(ScanForHosts);
        
        // Add connection callbacks with enhanced debugging
        NetworkManager.Singleton.OnServerStarted += OnHostStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void LoadBasedOnRole(Role role)
    {
        if (currentState != ConnectionState.Idle)
        {
            Debug.LogWarning("xx_🔧 Connection already in progress");
            return;
        }

        string targetIP = GetTargetIP();
        SetLocation(role);
        CommunicationManager.Instance.SetRole(role);
        pendingRole = role;
        
        if (role == Role.Director)
        {
            WebsocketManager websocketManager = FindAnyObjectByType<WebsocketManager>();
            websocketManager.SetDefaultWsAdress(GetLocalIPAddress());
            websocketManager.AutoConnectToServer();
            DirectorConnectionProcess(targetIP);
            
        }
        else
        {
            ClientConnectionProcess(targetIP);
        }
    }
    
    private string GetTargetIP()
    {
        return string.IsNullOrEmpty(ipInput.text) ? GetLocalIPAddress() : ipInput.text.Trim();
    }
    
    private string GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"xx_🔧 ⚠️ Failed to get local IP: {ex.Message}");
        }
    
        return "127.0.0.1"; // Fallback to localhost
    }

    #region Connection Logic

    private void DirectorConnectionProcess(string targetIP)
    {
        Debug.Log($"xx_🔧 Director attempting to connect to {targetIP}:{port}");
        currentState = ConnectionState.ConnectingAsClient;
        
        SetTransportConnection(targetIP, port);
        if (!hostAlreadyRunning)
        {
            NetworkManager.Singleton.StartHost();
        }
        else NetworkManager.Singleton.StartClient();
    }


    private void ClientConnectionProcess(string targetIP)
    {
        Debug.Log($"xx_🔧 Attempting to connect as client to {targetIP}:{port}");
        currentState = ConnectionState.ConnectingAsClient;
        
        SetTransportConnection(targetIP, port);
        
        // Start timeout for regular clients
        if (connectionAttemptCoroutine != null)
            StopCoroutine(connectionAttemptCoroutine);
        connectionAttemptCoroutine = StartCoroutine(ClientConnectionTimeout());
        
        NetworkManager.Singleton.StartClient();
    }

    private IEnumerator ClientConnectionTimeout()
    {
        yield return new WaitForSeconds(connectionTimeout);
        
        if (currentState == ConnectionState.ConnectingAsClient && !NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.Log("xx_🔧 Client connection timeout - retrying once");
            NetworkManager.Singleton.Shutdown();
            
            yield return new WaitForSeconds(1f); // Wait for potential host
            
            // Retry once
            NetworkManager.Singleton.StartClient();
            yield return new WaitForSeconds(connectionTimeout);
            
            if (!NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("xx_🔧 Client retry failed");
                NetworkManager.Singleton.Shutdown();
                HandleConnectionFailure();
            }
        }
    }

    private void SetTransportConnection(string ip, ushort port)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(ip, port);
        Debug.Log($"xx_🔧 Transport configured for {ip}:{port}");
    }

    private void HandleConnectionFailure()
    {
        Debug.Log("xx_🔧 Connection failed - returning to role selection");
        currentState = ConnectionState.Idle;
        ShowInitialSetup();
    }

    #endregion

    #region Connection Callbacks with Enhanced Debugging

    private void OnHostStarted()
    {
        Debug.Log("xx_🔧 ✅ Host started successfully");
        currentState = ConnectionState.Idle;
        
        if (connectionAttemptCoroutine != null)
        {
            StopCoroutine(connectionAttemptCoroutine);
            connectionAttemptCoroutine = null;
        }
        
        StartHostBroadcasting();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("xx_🔧 ✅ Connected as client successfully");
            currentState = ConnectionState.Idle;
            
            if (connectionAttemptCoroutine != null)
            {
                StopCoroutine(connectionAttemptCoroutine);
                connectionAttemptCoroutine = null;
            }
        }
    }
    

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            HandleDisconnect();
        }
    }

    private void HandleDisconnect()
    {
        Debug.Log("xx_🔧 Client disconnected");
        currentState = ConnectionState.Idle;
        
        if (connectionAttemptCoroutine != null)
        {
            StopCoroutine(connectionAttemptCoroutine);
            connectionAttemptCoroutine = null;
        }
        
        ShowInitialSetup();
    }

    #endregion

    #region UI Management

    private void ShowInitialSetup()
    {
        // Re-enable role selection buttons
        directorButton.interactable = true;
        journalistButton.interactable = true;
        audienceButton.interactable = true;
        
    }

    #endregion

    #region Utility Methods

    private void SetLocation(Role role)
    {
        GameObject xrGameObject = GameObject.FindGameObjectWithTag("XR");
        spawnManager.PlacePlayer(xrGameObject, role);
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        if (connectionAttemptCoroutine != null)
        {
            StopCoroutine(connectionAttemptCoroutine);
        }
        
        // Clean up UDP resources
        StopHostBroadcasting();
        udpListener?.Close();
    }
    
    public void ForceDisconnect()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            Debug.Log("xx_🔧 Host shutdown");
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            Debug.Log("xx_🔧 Client disconnected");
        }
        
        currentState = ConnectionState.Idle;
        NetworkManager.Singleton.Shutdown();
    }

    #endregion

    #region Auto-Discovery System

    private void AutoDetectIP()
    {
        //change the autodetecting
        string detectedIP = GetTargetIP();
        ipInput.text = detectedIP;
        Debug.Log($"xx_🔧 🔍 Auto-detected IP: {detectedIP}");
    }

    private void ScanForHosts()
    {
        Debug.Log("xx_🔧 🔍 Scanning for hosts on local network...");
        StartCoroutine(ScanForHostsCoroutine());
    }
    private IEnumerator ScanForHostsCoroutine()
    {
        scanForHostsButton.interactable = false;
    
        if (!SetupUDPListener() || !SendBroadcastRequest())
        {
            FinalizeScan(false, "");
            yield break;
        }
    
// Simple scan loop
        float scanTime = 0f;
        while (scanTime < 3f)
        {
            yield return new WaitForSeconds(0.1f);
            scanTime += 0.1f;
        
            if (udpListener.Available > 0)
            {
                var result = TryReceiveHostResponse();
                if (result.found)
                {
                    Debug.Log($"xx_🔧 ✅ Found host at: {result.ip}");
                    hostAlreadyRunning = true;
                    FinalizeScan(true, result.ip);
                    yield break;
                }
            }
        }
    
        Debug.Log("xx_🔧 ⚠️ No hosts found");
        hostAlreadyRunning = false;
        FinalizeScan(false, "");
    }
    private bool SetupUDPListener()
    {
        try
        {
            udpListener = new UdpClient(broadcastPort + 1);
            udpListener.Client.ReceiveTimeout = 100;
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log($"xx_Failed to create UDP listener: {ex.Message}");
            CleanupUDP();
            return false;
        }
    }
    private void CleanupUDP()
    {
        try
        {
            udpListener?.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"xx_🔧 Warning during UDP cleanup: {ex.Message}");
        }
        finally
        {
            udpListener = null;
        }
    }
    private bool SendBroadcastRequest()
    {
        try
        {
            using var broadcastClient = new UdpClient();
            broadcastClient.EnableBroadcast = true;
        
            byte[] data = System.Text.Encoding.UTF8.GetBytes("FIND_HOST");
            broadcastClient.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, broadcastPort));
        
            Debug.Log($"xx_🔧 📡 Broadcast sent on port {broadcastPort}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"xx_🔧 ❌ Failed to send broadcast: {ex.Message}");
            return false;
        }
    }
    private (bool found, string ip) TryReceiveHostResponse()
    {
        try
        {
            var remoteEndpoint = new IPEndPoint(IPAddress.Any, broadcastPort + 1);
            byte[] data = udpListener.Receive(ref remoteEndpoint);
            string response = System.Text.Encoding.UTF8.GetString(data);
        
            if (response.StartsWith("HOST_IP:"))
                return (true, response.Substring(8));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"xx_🔧 ⚠️ Error receiving UDP data: {ex.Message}");
        }
    
        return (false, "");
    }
    private void FinalizeScan(bool successful, string hostIP)
    {
        CleanupUDP();

        // Update UI
        if (successful)
        {
            ipInput.text = hostIP;
        }

        // Re-enable scan button
        scanForHostsButton.interactable = true;
        //scanForHostsButton.GetComponentInChildren<TMP_Text>().text = "Scan for Hosts";
    }

    private void StartHostBroadcasting()
    {
        if (isHostBroadcasting) return;
        
        isHostBroadcasting = true;
        StartCoroutine(HostBroadcastCoroutine());
    }

    private IEnumerator HostBroadcastCoroutine()
    {
        if (!SetupUDPBroadcaster()) yield break;
    
        Debug.Log($"xx_🔧 📡 Host broadcasting on port {broadcastPort}");
    
        while (isHostBroadcasting && NetworkManager.Singleton.IsHost)
        {
            if (udpBroadcaster.Available > 0)
                HandleDiscoveryRequest();
            
            yield return new WaitForSeconds(0.1f);
        }
    
        CleanupBroadcaster();
    }
    private bool SetupUDPBroadcaster()
    {
        try
        {
            udpBroadcaster = new UdpClient(broadcastPort);
            udpBroadcaster.Client.ReceiveTimeout = 100;
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"xx_🔧 ❌ Failed to start broadcasting: {ex.Message}");
            isHostBroadcasting = false;
            return false;
        }
    }
    private void HandleDiscoveryRequest()
    {
        try
        {
            var remoteEndpoint = new IPEndPoint(IPAddress.Any, broadcastPort);
            byte[] data = udpBroadcaster.Receive(ref remoteEndpoint);
            string request = System.Text.Encoding.UTF8.GetString(data);

            if (request == "FIND_HOST")
            {
                Debug.Log($"xx_🔧 📡 Discovery request from {remoteEndpoint.Address}");
                SendHostResponse(GetTargetIP(), remoteEndpoint.Address);
            }
        }
        catch (Exception ex)
        {
            if (isHostBroadcasting)
                Debug.LogWarning($"xx_🔧 ⚠️ Broadcast receive error: {ex.Message}");
        }
    }
    private void CleanupBroadcaster()
    {
        try
        {
            udpBroadcaster?.Close();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"xx_🔧 Warning during broadcast cleanup: {ex.Message}");
        }
        finally
        {
            udpBroadcaster = null;
            isHostBroadcasting = false;
        }
    }

    private void SendHostResponse(string hostIP, IPAddress clientAddress)
    {
        try
        {
            string response = $"HOST_IP:{hostIP}";
            byte[] responseData = System.Text.Encoding.UTF8.GetBytes(response);
            
            var responseClient = new System.Net.Sockets.UdpClient();
            var responseEndpoint = new IPEndPoint(clientAddress, broadcastPort + 1);
            responseClient.Send(responseData, responseData.Length, responseEndpoint);
            responseClient.Close();
            
            Debug.Log($"xx_🔧 📡 Sent host IP ({hostIP}) to {clientAddress}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"xx_🔧 ⚠️ Failed to send host response: {ex.Message}");
        }
    }

    private void StopHostBroadcasting()
    {
        isHostBroadcasting = false;
        udpBroadcaster?.Close();
        udpBroadcaster = null;
    }

    #endregion
    
}