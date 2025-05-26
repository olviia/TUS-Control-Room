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
        
        // Add connection callbacks
        NetworkManager.Singleton.OnServerStarted += OnHostStarted;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
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
        Debug.Log("Director: Attempting to start as host...");
        
        // For multi-machine setup, Directors should use their actual network IP when hosting
        // But can still connect to another host if one already exists
        string hostIP = GetHostIP(); // Use actual network IP for hosting
        
        // Set up transport for hosting with actual network IP
        SetTransportConnection(hostIP, port);
        
        if (NetworkManager.Singleton.StartHost())
        {
            Debug.Log($"Director: Successfully started as HOST on {hostIP}:{port}");
            Debug.Log($"📢 Other machines should connect to: {hostIP}:{port}");
        }
        else
        {
            Debug.Log($"Director: Failed to start as host on {hostIP}. Trying to connect to {targetIP} as client...");
            yield return new WaitForSeconds(1f);
            // Connect to the target IP (could be another Director's host)
            yield return StartCoroutine(ClientConnectionProcess(targetIP));
        }
    }
    
    /// <summary>
    /// Gets the actual network IP of this machine (not localhost)
    /// For hosting purposes
    /// </summary>
    private string GetHostIP()
    {
        // Try to get actual network IP instead of localhost
        string hostIP = "127.0.0.1"; // fallback
        
        try
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    hostIP = ip.ToString();
                    break;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Could not determine network IP: {ex.Message}. Using localhost.");
        }
        
        return hostIP;
    }

    private IEnumerator ClientConnectionProcess(string targetIP)
    {
        Debug.Log($"Attempting to connect as client to {targetIP}:{port}");
        
        // Set up transport for client connection
        SetTransportConnection(targetIP, port);
        
        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Client connection initiated, waiting for result...");
            
            // Wait for connection result
            float timeWaited = 0f;
            while (timeWaited < connectionTimeout && !NetworkManager.Singleton.IsConnectedClient)
            {
                yield return new WaitForSeconds(0.1f);
                timeWaited += 0.1f;
            }
            
            if (NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("Successfully connected as client!");
            }
            else
            {
                Debug.LogError($"Failed to connect to host at {targetIP}:{port} within {connectionTimeout} seconds");
                HandleConnectionFailure();
            }
        }
        else
        {
            Debug.LogError("Failed to start client");
            HandleConnectionFailure();
        }
    }

    private void SetTransportConnection(string ip, ushort port)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetConnectionData(ip, port);
        Debug.Log($"Transport configured for {ip}:{port}");
    }

    private void HandleConnectionFailure()
    {
        Debug.Log("Connection failed - returning to role selection");
        ShowInitialSetup();
    }

    #endregion

    #region Public Methods (for debugging/testing)

    #region Connection Callbacks

    private void OnHostStarted()
    {
        Debug.Log("Host started successfully!");
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        Debug.Log($"🔊 HOST: Running on {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
        Debug.Log($"🔊 HOST: Server ID = {NetworkManager.Singleton.LocalClientId}");
        
        // Start broadcasting our presence
        StartHostBroadcasting();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("Successfully connected to host as client");
            Debug.Log($"🔗 CLIENT: Connected with ID = {clientId}");
        }
        else
        {
            Debug.Log($"Another client connected: {clientId}");
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("Local client disconnected from host");
            HandleLocalClientDisconnect();
        }
        else if (NetworkManager.Singleton.IsHost)
        {
            Debug.Log($"Client {clientId} disconnected from our host");
        }
    }

    private void HandleLocalClientDisconnect()
    {
        Debug.Log("Handling local client disconnect...");
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
        
        Debug.Log("Returned to initial setup - role selection available");
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

    #endregion

    #region Auto-Discovery System

    private void AutoDetectIP()
    {
        string detectedIP = GetHostIP();
        ipInput.text = detectedIP;
        Debug.Log($"🔍 Auto-detected IP: {detectedIP}");
    }

    private void ScanForHosts()
    {
        Debug.Log("🔍 Scanning for hosts on local network...");
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
            Debug.LogError("❌ Failed to create UDP listener: " + ex.Message);
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
            Debug.Log($"📡 Broadcast sent on port {broadcastPort}");
            
            broadcastClient.Close();
            broadcastSent = true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("❌ Failed to send broadcast: " + ex.Message);
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
                        Debug.Log($"✅ Found host at: {foundHostIP}");
                        break;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("⚠️ Error receiving UDP data: " + ex.Message);
                }
            }
        }

        if (!scanSuccessful)
        {
            Debug.Log("⚠️ No hosts found on network");
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
            Debug.LogWarning("Warning during UDP cleanup: " + ex.Message);
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
        string hostIP = GetHostIP();
        
        // Setup UDP broadcaster
        try
        {
            udpBroadcaster = new System.Net.Sockets.UdpClient(broadcastPort);
            udpBroadcaster.Client.ReceiveTimeout = 100; // Short timeout for non-blocking
            Debug.Log($"📡 Host broadcasting on port {broadcastPort}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"❌ Failed to start host broadcasting: {ex.Message}");
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
                        Debug.Log($"📡 Received host discovery request from {remoteEndpoint.Address}");
                        
                        // Send our IP back
                        SendHostResponse(hostIP, remoteEndpoint.Address);
                    }
                }
                catch (System.Exception ex)
                {
                    if (isHostBroadcasting)
                    {
                        Debug.LogWarning($"⚠️ Broadcast receive error: {ex.Message}");
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
            Debug.LogWarning($"Warning during broadcast cleanup: {ex.Message}");
        }
        
        isHostBroadcasting = false;
        Debug.Log("📡 Host broadcasting stopped");
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
            
            Debug.Log($"📡 Sent host IP ({hostIP}) to {clientAddress}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"⚠️ Failed to send host response: {ex.Message}");
        }
    }

    private void StopHostBroadcasting()
    {
        isHostBroadcasting = false;
        udpBroadcaster?.Close();
        udpBroadcaster = null;
    }

    #endregion

    [ContextMenu("Force Disconnect")]
    public void ForceDisconnect()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("Host shutdown");
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
            Debug.Log("Client disconnected");
        }
    }

    [ContextMenu("Show Network Status")]
    public void ShowNetworkStatus()
    {
        Debug.Log($"IsHost: {NetworkManager.Singleton.IsHost}");
        Debug.Log($"IsClient: {NetworkManager.Singleton.IsClient}");
        Debug.Log($"IsConnectedClient: {NetworkManager.Singleton.IsConnectedClient}");
        Debug.Log($"IsServer: {NetworkManager.Singleton.IsServer}");
        
        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient)
        {
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            Debug.Log($"Transport: {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
        }
    }

    #endregion
}