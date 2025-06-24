using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Net;
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
        
        // Add extra debug callbacks
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnectDebug;
    }

    private void LoadBasedOnRole(Role role)
    {
        string targetIP = GetTargetIP();
        SetLocation(role);
        CommunicationManager.Instance.SetRole(role);
        
        if (role == Role.Director)
        {
            StartCoroutine(DirectorConnectionProcess(targetIP));
        }
        else
        {
            StartCoroutine(ClientConnectionProcess(targetIP));
        }
    }
    
    private string GetTargetIP()
    {
        return string.IsNullOrEmpty(ipInput.text) ? "127.0.0.1" : ipInput.text.Trim();
    }

    #region Connection Logic

    private IEnumerator DirectorConnectionProcess(string targetIP)
    {
        Debug.Log("xx_🔧 Director: Attempting to start as host...");
        
        // Set up transport for hosting with actual network IP
        SetTransportConnection(targetIP, port);
        
        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log($"xx_🔧 Director: Successfully started as HOST on {targetIP}:{port}");
            Debug.Log($"xx_🔧 📢 Other machines should connect to: {targetIP}:{port}");
        }
        else
        {
            Debug.Log($"xx_🔧 Director: Failed to start as host on {targetIP}. Trying to connect as client...");
            yield return new WaitForSeconds(1f);
            // Connect to the target IP (could be another Director's host)
            yield return StartCoroutine(ClientConnectionProcess(targetIP));
        }
    }

    private IEnumerator ClientConnectionProcess(string targetIP)
    {
        Debug.Log($"xx_🔧 Attempting to connect as client to {targetIP}:{port}");
        
        // Set up transport for client connection
        SetTransportConnection(targetIP, port);
        
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("xx_🔧 Client connection initiated, waiting for result...");
            
            // Wait for connection result
            float timeWaited = 0f;
            while (timeWaited < connectionTimeout && !NetworkManager.Singleton.IsConnectedClient)
            {
                yield return new WaitForSeconds(0.1f);
                timeWaited += 0.1f;
            }
            
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("xx_🔧 ✅ Successfully connected as client!");
            }
            else
            {
                Debug.LogError($"xx_🔧 ❌ Failed to connect to host at {targetIP}:{port} within {connectionTimeout} seconds");
                HandleConnectionFailure();
            }
        }
        else
        {
            Debug.LogError("xx_🔧 ❌ Failed to start client");
            HandleConnectionFailure();
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
        ShowInitialSetup();
    }

    #endregion

    #region Connection Callbacks with Enhanced Debugging

    private void OnHostStarted()
    {
        Debug.Log("xx_🔧 ✅ Host started successfully!");
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        Debug.Log($"xx_🔧 HOST: Running on {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
        Debug.Log($"xx_🔧 HOST: LocalClientId = {NetworkManager.Singleton.LocalClientId}");
        Debug.Log($"xx_🔧 HOST: Connected clients count = {NetworkManager.Singleton.ConnectedClients.Count}");
        Debug.Log($"xx_🔧 HOST: IsServer = {NetworkManager.Singleton.IsServer}");
        Debug.Log($"xx_🔧 HOST: IsClient = {NetworkManager.Singleton.IsClient}");
        Debug.Log($"xx_🔧 HOST: IsHost = {NetworkManager.Singleton.IsHost}");
        
        // List all connected clients
        foreach(var client in NetworkManager.Singleton.ConnectedClients)
        {
            Debug.Log($"xx_🔧   Connected Client: {client.Key}");
        }
        
        // Check for NetworkBehaviours in scene
        var allNetworkBehaviours = FindObjectsOfType<NetworkBehaviour>();
        Debug.Log($"xx_🔧 NetworkBehaviours in scene: {allNetworkBehaviours.Length}");
        foreach(var nb in allNetworkBehaviours)
        {
            Debug.Log($"xx_🔧   - {nb.GetType().Name} on {nb.gameObject.name}");
        }
        
        // Start broadcasting our presence
        StartHostBroadcasting();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"xx_🔧 ⭐ Client connected: {clientId}");
        Debug.Log($"xx_🔧   My Local ClientId: {NetworkManager.Singleton.LocalClientId}");
        Debug.Log($"xx_🔧   Total connected clients: {NetworkManager.Singleton.ConnectedClients.Count}");
        Debug.Log($"xx_🔧   Am I the host? {NetworkManager.Singleton.IsHost}");
        Debug.Log($"xx_🔧   Am I a client? {NetworkManager.Singleton.IsClient}");
        Debug.Log($"xx_🔧   Am I connected? {NetworkManager.Singleton.IsConnectedClient}");
        
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("xx_🔧 ✅ This is MY connection event");
            
            // Additional checks for local client
            Debug.Log($"xx_🔧   My connection state - IsConnectedClient: {NetworkManager.Singleton.IsConnectedClient}");
            Debug.Log($"xx_🔧   Network time: {NetworkManager.Singleton.ServerTime}");
            
            // Check NetworkBehaviours after connection
            StartCoroutine(CheckNetworkBehavioursAfterConnection());
        }
        else
        {
            Debug.Log($"xx_🔧 ✅ Another client connected: {clientId}");
        }
    }

    private IEnumerator CheckNetworkBehavioursAfterConnection()
    {
        yield return new WaitForSeconds(1f); // Wait a moment
        
        var allNetworkBehaviours = FindObjectsOfType<NetworkBehaviour>();
        Debug.Log($"xx_🔧 NetworkBehaviours after connection: {allNetworkBehaviours.Length}");
        foreach(var nb in allNetworkBehaviours)
        {
            Debug.Log($"xx_🔧   - {nb.GetType().Name} (Spawned: {nb.IsSpawned}, IsClient: {nb.IsClient}, IsServer: {nb.IsServer})");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("xx_🔧 Local client disconnected from host");
            HandleLocalClientDisconnect();
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"xx_🔧 Client {clientId} disconnected from our host");
        }
    }

    private void OnClientDisconnectDebug(ulong clientId)
    {
        Debug.LogError($"xx_🔧 🚨 CLIENT DISCONNECT DETECTED! ClientId: {clientId}");
        Debug.LogError($"xx_🔧   Local ClientId: {NetworkManager.Singleton?.LocalClientId}");
        Debug.LogError($"xx_🔧   Is this local client? {clientId == NetworkManager.Singleton?.LocalClientId}");
        Debug.LogError($"xx_🔧   Network state - IsHost: {NetworkManager.Singleton?.IsHost}");
        Debug.LogError($"xx_🔧   Network state - IsClient: {NetworkManager.Singleton?.IsClient}");
        Debug.LogError($"xx_🔧   Network state - IsConnectedClient: {NetworkManager.Singleton?.IsConnectedClient}");
        
        // Check if any NetworkBehaviours are causing issues
        var allNetworkBehaviours = FindObjectsOfType<NetworkBehaviour>();
        Debug.LogError($"xx_🔧   Active NetworkBehaviours: {allNetworkBehaviours.Length}");
        foreach(var nb in allNetworkBehaviours)
        {
            Debug.LogError($"xx_🔧     - {nb.GetType().Name} (Spawned: {nb.IsSpawned}, Enabled: {nb.enabled})");
        }
    }

    private void HandleLocalClientDisconnect()
    {
        Debug.Log("xx_🔧 Handling local client disconnect...");
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
        
        Debug.Log("xx_🔧 Returned to initial setup - role selection available");
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
        
        NetworkManager.Singleton.Shutdown();
    }

    #endregion

    #region Auto-Discovery System

    private void AutoDetectIP()
    {
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
        // Disable scan button during scan
        scanForHostsButton.interactable = false;

        bool scanSuccessful = false;
        string foundHostIP = "";
        bool setupSuccessful = false;

        // Setup UDP listener
        try
        {
            udpListener = new System.Net.Sockets.UdpClient(broadcastPort + 1);
            udpListener.Client.ReceiveTimeout = 100; // Short timeout for non-blocking
            setupSuccessful = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("xx_🔧 ❌ Failed to create UDP listener: " + ex.Message);
        }

        if (!setupSuccessful)
        {
            yield return FinalizeScan(scanSuccessful, foundHostIP);
            yield break;
        }

        // Send broadcast request
        bool broadcastSent = false;
        try
        {
            var broadcastClient = new System.Net.Sockets.UdpClient();
            broadcastClient.EnableBroadcast = true;
            
            string request = "FIND_HOST";
            byte[] data = System.Text.Encoding.UTF8.GetBytes(request);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
            
            broadcastClient.Send(data, data.Length, broadcastEndpoint);
            Debug.Log($"xx_🔧 📡 Broadcast sent on port {broadcastPort}");
            
            broadcastClient.Close();
            broadcastSent = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("xx_🔧 ❌ Failed to send broadcast: " + ex.Message);
        }

        if (!broadcastSent)
        {
            yield return FinalizeScan(scanSuccessful, foundHostIP);
            yield break;
        }

        // Wait and listen for responses (outside try-catch)
        float scanTime = 0f;
        const float maxScanTime = 3f;

        while (scanTime < maxScanTime && !scanSuccessful)
        {
            yield return new WaitForSeconds(0.1f);
            scanTime += 0.1f;

            // Try to receive response (non-blocking)
            if (udpListener.Available > 0)
            {
                try
                {
                    var remoteEndpoint = new IPEndPoint(IPAddress.Any, broadcastPort + 1);
                    byte[] receivedData = udpListener.Receive(ref remoteEndpoint);
                    string response = System.Text.Encoding.UTF8.GetString(receivedData);

                    if (response.StartsWith("HOST_IP:"))
                    {
                        foundHostIP = response.Substring(8); // Remove "HOST_IP:" prefix
                        scanSuccessful = true;
                        Debug.Log($"xx_🔧 ✅ Found host at: {foundHostIP}");
                        break;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("xx_🔧 ⚠️ Error receiving UDP data: " + ex.Message);
                }
            }
        }

        if (!scanSuccessful)
        {
            Debug.Log("xx_🔧 ⚠️ No hosts found on network");
        }

        yield return FinalizeScan(scanSuccessful, foundHostIP);
    }

    private IEnumerator FinalizeScan(bool successful, string hostIP)
    {
        // Clean up
        try
        {
            udpListener?.Close();
            udpListener = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("xx_🔧 Warning during UDP cleanup: " + ex.Message);
        }

        // Update UI
        if (successful)
        {
            ipInput.text = hostIP;
        }

        // Re-enable scan button
        scanForHostsButton.interactable = true;
        scanForHostsButton.GetComponentInChildren<TMP_Text>().text = "Scan for Hosts";

        yield return null; // Complete the coroutine
    }

    private void StartHostBroadcasting()
    {
        if (isHostBroadcasting) return;
        
        isHostBroadcasting = true;
        StartCoroutine(HostBroadcastCoroutine());
    }

    private IEnumerator HostBroadcastCoroutine()
    {
        
        // Setup UDP broadcaster
        try
        {
            udpBroadcaster = new System.Net.Sockets.UdpClient(broadcastPort);
            udpBroadcaster.Client.ReceiveTimeout = 100; // Short timeout for non-blocking
            Debug.Log($"xx_🔧 📡 Host broadcasting on port {broadcastPort}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"xx_🔧 ❌ Failed to start host broadcasting: {ex.Message}");
            isHostBroadcasting = false;
            yield break;
        }

        // Main broadcasting loop
        while (isHostBroadcasting && NetworkManager.Singleton.IsHost)
        {
            // Check for incoming requests (non-blocking)
            if (udpBroadcaster.Available > 0)
            {
                try
                {
                    var remoteEndpoint = new IPEndPoint(IPAddress.Any, broadcastPort);
                    byte[] receivedData = udpBroadcaster.Receive(ref remoteEndpoint);
                    string request = System.Text.Encoding.UTF8.GetString(receivedData);

                    if (request == "FIND_HOST")
                    {
                        Debug.Log($"xx_🔧 📡 Received host discovery request from {remoteEndpoint.Address}");
                        
                        // Send our IP back
                        SendHostResponse(GetTargetIP(), remoteEndpoint.Address);
                    }
                }
                catch (System.Exception ex)
                {
                    if (isHostBroadcasting)
                    {
                        Debug.LogWarning($"xx_🔧 ⚠️ Broadcast receive error: {ex.Message}");
                    }
                }
            }
            
            yield return new WaitForSeconds(0.1f); // Small delay to prevent tight loop
        }

        // Cleanup
        try
        {
            udpBroadcaster?.Close();
            udpBroadcaster = null;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"xx_🔧 Warning during broadcast cleanup: {ex.Message}");
        }
        
        isHostBroadcasting = false;
        Debug.Log("xx_🔧 📡 Host broadcasting stopped");
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