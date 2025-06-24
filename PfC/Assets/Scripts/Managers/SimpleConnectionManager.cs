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
        
        //testing
        EnsureNoConnectionApproval();
        LogDeepConnectionDiagnostics();
        LogNetcodePackageInfo();
    
        // Add transport failure callback
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;

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
            //testing
            EnsureNoConnectionApproval();
            LogDeepConnectionDiagnostics();
            LogNetcodePackageInfo();
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
    
    #region Log different network settings
    //some tests
    // Add these diagnostic methods to identify network adapter issues



private void LogAllNetworkInterfaces()
{
    Debug.Log("yy_ === ALL NETWORK INTERFACES ===");
    
    foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
    {
        Debug.Log($"yy_ Interface: {ni.Name}");
        Debug.Log($"yy_   Type: {ni.NetworkInterfaceType}");
        Debug.Log($"yy_   Status: {ni.OperationalStatus}");
        Debug.Log($"yy_   Description: {ni.Description}");
        
        if (ni.OperationalStatus == OperationalStatus.Up)
        {
            var properties = ni.GetIPProperties();
            foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    Debug.Log($"yy_   IPv4: {ip.Address}");
                    Debug.Log($"yy_   Subnet: {ip.IPv4Mask}");
                }
            }
            
            // Log gateway info
            foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses)
            {
                Debug.Log($"yy_   Gateway: {gateway.Address}");
            }
        }
        Debug.Log("yy_   ---");
    }
    Debug.Log("yy_ =============================");
}

private void LogCurrentNetworkSelection()
{
    Debug.Log("yy_ === CURRENT NETWORK SELECTION ===");
    
    // Show which IP we're currently using
    string currentIP = GetTargetIP();
    Debug.Log($"yy_ Current Target IP: {currentIP}");
    
    // Show all available local IPs
    string[] localIPs = GetAllLocalIPs();
    Debug.Log($"yy_ Available Local IPs: {string.Join(", ", localIPs)}");
    
    // Try to determine which interface we should be using
    var activeInterface = GetActiveNetworkInterface();
    if (activeInterface != null)
    {
        Debug.Log($"yy_ Recommended Interface: {activeInterface.Name}");
        Debug.Log($"yy_ Recommended Type: {activeInterface.NetworkInterfaceType}");
        
        var properties = activeInterface.GetIPProperties();
        foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
        {
            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
            {
                Debug.Log($"yy_ Recommended IP: {ip.Address}");
            }
        }
    }
    
    Debug.Log("yy_ ===============================");
}

private NetworkInterface GetActiveNetworkInterface()
{
    return NetworkInterface.GetAllNetworkInterfaces()
        .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
        .Where(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || 
                    ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
        .Where(ni => ni.GetIPProperties().UnicastAddresses
            .Any(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && 
                      !IPAddress.IsLoopback(ip.Address)))
        .FirstOrDefault();
}

private string[] GetAllLocalIPs()
{
    return Dns.GetHostEntry(Dns.GetHostName())
        .AddressList
        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
        .Select(ip => ip.ToString())
        .ToArray();
}

// Enhanced broadcast diagnostic
private void LogBroadcastDiagnostics()
{
    Debug.Log("yy_ === BROADCAST DIAGNOSTICS ===");
    Debug.Log($"yy_ Broadcast Port: {broadcastPort}");
    Debug.Log($"yy_ Response Port: {broadcastPort + 1}");
    
    // Check if ports are available
    CheckPortAvailability(broadcastPort, "Broadcast");
    CheckPortAvailability(broadcastPort + 1, "Response");
    
    Debug.Log("yy_ ============================");
}

private void CheckPortAvailability(int port, string portType)
{
    try
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        listener.Stop();
        Debug.Log($"yy_ {portType} Port {port}: AVAILABLE");
    }
    catch (Exception ex)
    {
        Debug.LogWarning($"yy_ {portType} Port {port}: IN USE or BLOCKED - {ex.Message}");
    }
}// Enhanced broadcast testing methods

private IEnumerator TestDirectUDPCommunication(string targetIP)
{
    Debug.Log($"yy_ === TESTING DIRECT UDP TO {targetIP} ===");
    
    bool testSuccessful = false;
    
    try
    {
        // Send direct UDP message (not broadcast)
        var testClient = new UdpClient();
        string testMessage = $"DIRECT_TEST_FROM_{GetLocalIPAddress()}";
        byte[] data = System.Text.Encoding.UTF8.GetBytes(testMessage);
        var targetEndpoint = new IPEndPoint(IPAddress.Parse(targetIP), broadcastPort);
        
        testClient.Send(data, data.Length, targetEndpoint);
        Debug.Log($"yy_ Direct UDP sent to {targetIP}:{broadcastPort}");
        
        testClient.Close();
        testSuccessful = true;
    }
    catch (Exception ex)
    {
        Debug.LogError($"yy_ Direct UDP test failed: {ex.Message}");
    }
    
    Debug.Log($"yy_ Direct UDP test result: {(testSuccessful ? "SUCCESS" : "FAILED")}");
    yield return null;
}

private IEnumerator EnhancedScanForHostsCoroutine()
{
    Debug.Log("yy_ === ENHANCED HOST SCAN ===");
    
    // First, log network diagnostics
    LogAllNetworkInterfaces();
    LogCurrentNetworkSelection();
    LogBroadcastDiagnostics();
    
    scanForHostsButton.interactable = false;
    
    bool scanSuccessful = false;
    string foundHostIP = "";
    
    // Test 1: Check if we can bind to the response port
    try
    {
        udpListener = new UdpClient(broadcastPort + 1);
        udpListener.Client.ReceiveTimeout = 100;
        Debug.Log($"yy_ ✅ Successfully bound to response port {broadcastPort + 1}");
    }
    catch (Exception ex)
    {
        Debug.LogError($"yy_ ❌ Failed to bind to response port: {ex.Message}");
        FinalizeScan(false, "");
        yield break;
    }
    
    // Test 2: Try different broadcast methods
    yield return StartCoroutine(TryMultipleBroadcastMethods());
    
    // Test 3: Wait for responses
    float scanTime = 0f;
    const float maxScanTime = 5f; // Increased timeout
    
    while (scanTime < maxScanTime && !scanSuccessful)
    {
        yield return new WaitForSeconds(0.1f);
        scanTime += 0.1f;
        
        if (udpListener.Available > 0)
        {
            try
            {
                var remoteEndpoint = new IPEndPoint(IPAddress.Any, broadcastPort + 1);
                byte[] receivedData = udpListener.Receive(ref remoteEndpoint);
                string response = System.Text.Encoding.UTF8.GetString(receivedData);
                
                Debug.Log($"yy_ Received: '{response}' from {remoteEndpoint.Address}");
                
                if (response.StartsWith("HOST_IP:"))
                {
                    foundHostIP = response.Substring(8);
                    scanSuccessful = true;
                    Debug.Log($"yy_ ✅ Found host at: {foundHostIP}");
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"yy_ ⚠️ Error receiving UDP data: {ex.Message}");
            }
        }
        
        // Log progress every second
        if (scanTime % 1f < 0.1f)
        {
            Debug.Log($"yy_ Scanning... {scanTime:F1}s");
        }
    }
    
    if (!scanSuccessful)
    {
        Debug.LogWarning("yy_ ⚠️ No hosts found on network");
        
        // Additional diagnostic: try direct communication to known IPs
        string[] possibleIPs = GetNetworkRangeIPs();
        Debug.Log($"yy_ Trying direct communication to possible IPs: {string.Join(", ", possibleIPs)}");
        
        foreach (string ip in possibleIPs.Take(5)) // Test first 5 IPs
        {
            yield return StartCoroutine(TestDirectUDPCommunication(ip));
        }
    }
    
    yield return FinalizeScan(scanSuccessful, foundHostIP);
}

private IEnumerator TryMultipleBroadcastMethods()
{
    Debug.Log("yy_ === TRYING MULTIPLE BROADCAST METHODS ===");
    
    string request = "FIND_HOST";
    byte[] data = System.Text.Encoding.UTF8.GetBytes(request);
    
    // Method 1: Standard broadcast
    try
    {
        var broadcastClient = new UdpClient();
        broadcastClient.EnableBroadcast = true;
        var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, broadcastPort);
        broadcastClient.Send(data, data.Length, broadcastEndpoint);
        broadcastClient.Close();
        Debug.Log($"yy_ ✅ Standard broadcast sent to 255.255.255.255:{broadcastPort}");
    }
    catch (Exception ex)
    {
        Debug.LogError($"yy_ ❌ Standard broadcast failed: {ex.Message}");
    }
    
    yield return new WaitForSeconds(0.1f);
    
    // Method 2: Subnet-specific broadcast
    try
    {
        string subnetBroadcast = GetSubnetBroadcastAddress();
        if (!string.IsNullOrEmpty(subnetBroadcast))
        {
            var subnetClient = new UdpClient();
            subnetClient.EnableBroadcast = true;
            var subnetEndpoint = new IPEndPoint(IPAddress.Parse(subnetBroadcast), broadcastPort);
            subnetClient.Send(data, data.Length, subnetEndpoint);
            subnetClient.Close();
            Debug.Log($"yy_ ✅ Subnet broadcast sent to {subnetBroadcast}:{broadcastPort}");
        }
    }
    catch (Exception ex)
    {
        Debug.LogError($"yy_ ❌ Subnet broadcast failed: {ex.Message}");
    }
    
    yield return new WaitForSeconds(0.1f);
    
    // Method 3: Direct IP range scan
    string[] networkIPs = GetNetworkRangeIPs();
    foreach (string ip in networkIPs.Take(10)) // Test first 10 IPs in range
    {
        try
        {
            var directClient = new UdpClient();
            var directEndpoint = new IPEndPoint(IPAddress.Parse(ip), broadcastPort);
            directClient.Send(data, data.Length, directEndpoint);
            directClient.Close();
            Debug.Log($"yy_ ✅ Direct scan sent to {ip}:{broadcastPort}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"yy_ ⚠️ Direct scan to {ip} failed: {ex.Message}");
        }
        
        yield return new WaitForSeconds(0.01f); // Small delay between sends
    }
}

private string GetSubnetBroadcastAddress()
{
    var activeInterface = GetActiveNetworkInterface();
    if (activeInterface == null) return null;
    
    var properties = activeInterface.GetIPProperties();
    foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
    {
        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            // Calculate broadcast address
            var ipBytes = ip.Address.GetAddressBytes();
            var maskBytes = ip.IPv4Mask.GetAddressBytes();
            var broadcastBytes = new byte[4];
            
            for (int i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
            }
            
            return new IPAddress(broadcastBytes).ToString();
        }
    }
    return null;
}

private string[] GetNetworkRangeIPs()
{
    var activeInterface = GetActiveNetworkInterface();
    if (activeInterface == null) return new string[0];
    
    var properties = activeInterface.GetIPProperties();
    foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
    {
        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            // Generate IP range for same subnet
            var ipBytes = ip.Address.GetAddressBytes();
            var baseIP = $"{ipBytes[0]}.{ipBytes[1]}.{ipBytes[2]}";
            
            return Enumerable.Range(1, 254)
                .Select(i => $"{baseIP}.{i}")
                .Where(testIP => testIP != ip.Address.ToString()) // Exclude our own IP
                .ToArray();
        }
    }
    return new string[0];
}

private string GetLocalIPAddress()
{
    var activeInterface = GetActiveNetworkInterface();
    if (activeInterface == null) return "127.0.0.1";
    
    var properties = activeInterface.GetIPProperties();
    foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
    {
        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            return ip.Address.ToString();
        }
    }
    return "127.0.0.1";
}

// Deep diagnostics for connection approval timeout when approval is disabled

private void LogDeepConnectionDiagnostics()
{
    Debug.Log("yy_ ======= DEEP CONNECTION DIAGNOSTICS =======");
    
    var nm = NetworkManager.Singleton;
    var config = nm.NetworkConfig;
    
    // Basic settings
    Debug.Log($"yy_ ConnectionApproval: {config.ConnectionApproval}");
    Debug.Log($"yy_ ClientConnectionBufferTimeout: {config.ClientConnectionBufferTimeout}");
    Debug.Log($"yy_ LoadSceneTimeOut: {config.LoadSceneTimeOut}");
    Debug.Log($"yy_ SpawnTimeout: {config.SpawnTimeout}");
    
    // Transport layer diagnostics
    var transport = nm.GetComponent<UnityTransport>();
    if (transport != null)
    {
        Debug.Log($"yy_ TRANSPORT SETTINGS:");
        Debug.Log($"yy_   ConnectTimeoutMS: {transport.ConnectTimeoutMS}");
        Debug.Log($"yy_   MaxConnectAttempts: {transport.MaxConnectAttempts}");
        Debug.Log($"yy_   HeartbeatTimeoutMS: {transport.HeartbeatTimeoutMS}");
        Debug.Log($"yy_   MaxPayloadSize: {transport.MaxPayloadSize}");
        
        // Debug simulator settings (can cause issues)
        Debug.Log($"yy_   DEBUG SIMULATOR:");
        Debug.Log($"yy_     PacketDelayMS: {transport.DebugSimulator.PacketDelayMS}");
        Debug.Log($"yy_     PacketJitterMS: {transport.DebugSimulator.PacketJitterMS}");
        Debug.Log($"yy_     PacketDropRate: {transport.DebugSimulator.PacketDropRate}");
    }
    
    // NetworkManager internal state
    Debug.Log($"yy_ NETWORKMANAGER STATE:");
    Debug.Log($"yy_   IsHost: {nm.IsHost}");
    Debug.Log($"yy_   IsServer: {nm.IsServer}");
    Debug.Log($"yy_   IsClient: {nm.IsClient}");
    Debug.Log($"yy_   IsListening: {nm.IsListening}");
    Debug.Log($"yy_   LocalClientId: {nm.LocalClientId}");
    Debug.Log($"yy_   ConnectedClients.Count: {nm.ConnectedClients.Count}");
    
    // Unity and Netcode versions - VERSION MISMATCH CAN CAUSE THIS
    Debug.Log($"yy_ VERSION INFO:");
    Debug.Log($"yy_   Unity Version: {Application.unityVersion}");
    Debug.Log($"yy_   Platform: {Application.platform}");
    Debug.Log($"yy_   Is Editor: {Application.isEditor}");
    
    // Check if ConnectionApprovalCallback is somehow still set
    Debug.Log($"yy_ ConnectionApprovalCallback is null: {nm.ConnectionApprovalCallback == null}");
    
    Debug.Log("yy_ ===========================================");
}

// Check for Unity Netcode package version issues
private void LogNetcodePackageInfo()
{
    Debug.Log("yy_ === NETCODE PACKAGE DIAGNOSTICS ===");
    
    // Try to get package version info
    try
    {
        // Check if we can access NetworkManager assembly info
        var assembly = typeof(NetworkManager).Assembly;
        Debug.Log($"yy_ NetworkManager Assembly: {assembly.FullName}");
        Debug.Log($"yy_ Assembly Location: {assembly.Location}");
        
        // Check assembly version
        var version = assembly.GetName().Version;
        Debug.Log($"yy_ Assembly Version: {version}");
        
    }
    catch (System.Exception ex)
    {
        Debug.LogWarning($"yy_ Could not get assembly info: {ex.Message}");
    }
    
    Debug.Log("yy_ ====================================");
}

// Enhanced connection process with detailed logging
private IEnumerator DetailedClientConnectionProcess(string targetIP, Role role)
{
    Debug.Log($"yy_ 🔧 === DETAILED CLIENT CONNECTION PROCESS ===");
    Debug.Log($"yy_ Target: {targetIP}:{port}");
    
    // Log diagnostics BEFORE connection
    LogDeepConnectionDiagnostics();
    LogNetcodePackageInfo();
    
    SetTransportConnection(targetIP, port);
    
    // Log transport state after setup
    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
    Debug.Log($"yy_ Transport configured - Address: {transport.ConnectionData.Address}, Port: {transport.ConnectionData.Port}");
    
    if (NetworkManager.Singleton.StartClient())
    {
        Debug.Log("yy_ 🔧 StartClient() returned true, monitoring connection...");
        
        // Monitor connection with detailed state logging
        float timeWaited = 0f;
        bool connectionSuccessful = false;
        
        while (timeWaited < connectionTimeout)
        {
            var nm = NetworkManager.Singleton;
            
            // Check connection state
            if (nm.IsConnectedClient)
            {
                connectionSuccessful = true;
                Debug.Log($"yy_ 🔧 ✅ Connection successful after {timeWaited:F1}s!");
                break;
            }
            
            // Log detailed state every 2 seconds
            if (timeWaited % 2f < 0.1f)
            {
                Debug.Log($"yy_ 🔧 Connection status at {timeWaited:F1}s:");
                Debug.Log($"yy_   IsConnectedClient: {nm.IsConnectedClient}");
                Debug.Log($"yy_   IsClient: {nm.IsClient}");
                Debug.Log($"yy_   IsListening: {nm.IsListening}");
                Debug.Log($"yy_   LocalClientId: {nm.LocalClientId}");
                Debug.Log($"yy_   ConnectedClients.Count: {nm.ConnectedClients.Count}");
            }
            
            yield return new WaitForSeconds(0.1f);
            timeWaited += 0.1f;
        }
        
        // Final state analysis
        if (connectionSuccessful)
        {
            Debug.Log("yy_ 🔧 ✅ CLIENT CONNECTION SUCCESSFUL!");
            LogDeepConnectionDiagnostics();
            
            yield return new WaitForSeconds(1f); // Stabilization
            SetLocation(role);
        }
        else
        {
            Debug.LogError("yy_ 🔧 ❌ CLIENT CONNECTION FAILED!");
            Debug.LogError($"yy_ Final timeout after {connectionTimeout}s");
            LogDeepConnectionDiagnostics();
            HandleConnectionFailure();
        }
    }
    else
    {
        Debug.LogError("yy_ 🔧 ❌ StartClient() returned false!");
        LogDeepConnectionDiagnostics();
        HandleConnectionFailure();
    }
}

// Add this to catch transport-level errors
private void OnTransportFailure()
{
    Debug.LogError("yy_ 🚨 TRANSPORT FAILURE DETECTED!");
    LogDeepConnectionDiagnostics();
    
    // Additional transport diagnostics
    var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
    if (transport != null)
    {
        Debug.LogError($"yy_ Transport state during failure:");
        Debug.LogError($"yy_   Connection Address: {transport.ConnectionData.Address}");
        Debug.LogError($"yy_   Connection Port: {transport.ConnectionData.Port}");
    }
}

// Method to force clear any connection approval callback
private void EnsureNoConnectionApproval()
{
    var nm = NetworkManager.Singleton;
    var config = nm.NetworkConfig;
    
    Debug.Log("yy_ === ENSURING NO CONNECTION APPROVAL ===");
    Debug.Log($"yy_ BEFORE - ConnectionApproval: {config.ConnectionApproval}");
    Debug.Log($"yy_ BEFORE - Callback is null: {nm.ConnectionApprovalCallback == null}");
    
    // Force disable approval
    config.ConnectionApproval = false;
    nm.ConnectionApprovalCallback = null;
    
    Debug.Log($"yy_ AFTER - ConnectionApproval: {config.ConnectionApproval}");
    Debug.Log($"yy_ AFTER - Callback is null: {nm.ConnectionApprovalCallback == null}");
    Debug.Log("yy_ ========================================");
}
    #endregion
}