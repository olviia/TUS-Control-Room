using System;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using UnityEngine;
using Klak.Ndi;
using Newtonsoft.Json.Linq;
using Unity.Netcode;

/// <summary>
/// Creates a scene in OBS and adds an NDI source that receives the stream from Unity
/// </summary> 
public class ObsCreateScene : NetworkBehaviour
{
    [Header("Scene Settings")]
    [SerializeField] private string sceneName;
    [SerializeField] private string sourceName = "";
    
    [Header("Filter Settings")]
    [Tooltip("Add this filter allows to create a new NDI output from OBS")]
    [SerializeField] private bool addFilterToSource = true;
    [SerializeField] private string ndiOutputName = "";
    
    [Header("Behavior")]
    [SerializeField] private bool logDetailedInfo = true;
    
    private OBSWebsocket obsWebSocket;
    private NdiSender ndiSender;
    private WebsocketManager webSocketManager;
    private bool isServer;
    private string filterName = "Dedicated NDI® output";
    private string filterType = "ndi_filter";
    private string ndiPropertyName = "ndi_filter_ndiname";
    
    
    // Static property to share server computer name across instances
    public static string ServerComputerName { get; private set; } = "";
    
    
    public override void OnNetworkSpawn()
    {
        isServer = NetworkManager.Singleton.IsServer;
        ///connect in any case
        
        // if (IsClient)
        // {
        //     if (logDetailedInfo)
        //         Debug.Log("ObsCreateScene: Running as client - OBS scene creation disabled");
        //     return;
        // }
        
        // Get the WebsocketManager and NdiSender from the same GameObject
        webSocketManager = FindFirstObjectByType<WebsocketManager>();
        ndiSender = GetComponent<NdiSender>();
        
        if (webSocketManager != null) Debug.Log($"websocket: {webSocketManager}");
        if (ndiSender != null) Debug.Log($"ndiSender: {ndiSender}");

        ServerComputerName = Environment.MachineName;
        
        if (webSocketManager == null)
        {
            Debug.LogError("WebsocketManager was not found on the scene");
        }
        else
        {
            obsWebSocket = (OBSWebsocket)typeof(WebsocketManager)
                .GetField("obsWebSocket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .GetValue(webSocketManager);

            webSocketManager.WsConnected += OnWebSocketConnected;
            
            if (obsWebSocket.IsConnected)
            {
                Debug.Log("WebSocket was already connected, triggering scene creation immediately");
                OnWebSocketConnected(true);
            }
        }
        
        if (ndiSender == null)
        {
            Debug.LogError("NdiSender component not found on the same GameObject");
        }
        
    }

    private void OnWebSocketConnected(bool connected)
    {
        //Add the role maybe
        if (connected)
        {
            // If connected to OBS, try to check and create the scene
            CheckAndCreateScene();
        }
    }

    /// <summary>
    /// Get the full NDI source name in the format used by OBS
    /// </summary>
    private string GetFullNdiSourceName()
    {
        // Get computer name - either from override or from system
        string computerName = Environment.MachineName;
        
        // Format: ComputerName (NdiName)
        return $"{computerName} ({ndiSender.ndiName})";
    }

    /// <summary>
    /// Manually check and create the scene with the current sceneName
    /// </summary>
    public void CheckAndCreateScene()
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("Scene name not set");
            return;
        }
        
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
        {
            Debug.LogError("Not connected to OBS WebSocket");
            return;
        }
        
        try
        {
            bool sceneExists = SceneExists(sceneName);
            
            if (!sceneExists)
            {
                CreateSceneWithNdiSource();
            }
            else
            {
                Log($"Scene '{sceneName}' already exists in OBS");
                // Check if the source exists, create if it doesn't
                CheckAndAddSource();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking/creating scene: {e.Message}");
        }
    }

    /// <summary>
    /// Check if a scene with the given name exists in OBS
    /// </summary>
    private bool SceneExists(string name)
    {
        try
        {
            var sceneList = obsWebSocket.GetSceneList();
            foreach (var scene in sceneList.Scenes)
            {
                if (scene.Name == name)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if scene exists: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a source with the given name exists in the scene
    /// </summary>
    private bool SourceExists(string sceneName, string sourceName)
    {
        try
        {
            var sceneItemList = obsWebSocket.GetSceneItemList(sceneName);
            foreach (var item in sceneItemList)
            {
                if (item.SourceName == sourceName)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if source exists: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates a new scene in OBS and adds an NDI source
    /// </summary>
    private void CreateSceneWithNdiSource()
    {
        try
        {
            // Create the scene
            obsWebSocket.CreateScene(sceneName);
            Log($"Scene '{sceneName}' created in OBS");
            
            // Add an NDI source
            AddNdiSourceToScene();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating scene: {e.Message}");
        }
    }

    /// <summary>
    /// Check if source exists and add if needed
    /// </summary>
    private void CheckAndAddSource()
    {
        try
        {
            bool sourceExists = SourceExists(sceneName, sourceName);
            
            if (!sourceExists)
            {
                AddNdiSourceToScene();
            }
            else
            {
                Log($"Source '{sourceName}' already exists in scene '{sceneName}'");
                // Update the source to make sure it's connected to our NDI stream
                UpdateNdiSource();
                
                // Check if filter exists and add if needed
                if (addFilterToSource)
                {
                    CheckAndAddFilter();
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking/adding source: {e.Message}");
        }
    }

    /// <summary>
    /// Add an NDI source to the scene that receives our Unity NDI output
    /// </summary>
    private void AddNdiSourceToScene()
    {
        try
        {
            // Create settings specific for NDI source
            var inputSettings = new JObject();
            
            // Get the full NDI source name including computer name
            string fullNdiName = GetFullNdiSourceName();
            
            // Set the NDI source name
            inputSettings["ndi_source_name"] = fullNdiName;
            //set source settings so it plays always
            inputSettings["ndi_behavior"] = 0;
            
            // Create an NDI source
            obsWebSocket.CreateInput(
                sceneName,      // Scene name
                sourceName,     // Input name 
                "ndi_source",   // Input kind - NDI source
                inputSettings,  // Input settings with the full NDI source name
                true            // Scene item enabled
            );
            
            Log($"Created NDI source '{sourceName}' in scene '{sceneName}' connected to '{fullNdiName}'");
            
            // Add filter to the newly created source if enabled
            if (addFilterToSource)
            {
                // Wait a short time to ensure the source is fully created
                // before adding the filter
                Log("Waiting to add filter to newly created source...");
                System.Threading.Thread.Sleep(500);
                AddFilterToSource();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error adding NDI source to scene: {e.Message} - {e.StackTrace}");
            
            // Try to get a list of available input kinds for debugging
            try
            {
                var inputKinds = obsWebSocket.GetInputKindList();
                Log("Available input kinds: " + string.Join(", ", inputKinds));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting input kinds: {ex.Message}");
            }
        }
    }
    /// <summary>
    /// Add an NDI source to the scene that receives our Unity NDI output
    /// </summary>
    private void AddSceneToScene()
    {
        try
        {
            // Create settings specific for NDI source
            var inputSettings = new JObject();
            
            // Get the full NDI source name including computer name
            string fullNdiName = GetFullNdiSourceName();
            
            // Set the NDI source name
            inputSettings["ndi_source_name"] = fullNdiName;
            //set source settings so it plays always
            inputSettings["ndi_behavior"] = 0;
            
            // Create an NDI source
            obsWebSocket.CreateInput(
                sceneName,      // Scene name
                sourceName,     // Input name 
                "ndi_source",   // Input kind - NDI source
                inputSettings,  // Input settings with the full NDI source name
                true            // Scene item enabled
            );
            
            Log($"Created NDI source '{sourceName}' in scene '{sceneName}' connected to '{fullNdiName}'");
            
            // Add filter to the newly created source if enabled
            if (addFilterToSource)
            {
                // Wait a short time to ensure the source is fully created
                // before adding the filter
                Log("Waiting to add filter to newly created source...");
                System.Threading.Thread.Sleep(500);
                AddFilterToSource();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error adding NDI source to scene: {e.Message} - {e.StackTrace}");
            
            // Try to get a list of available input kinds for debugging
            try
            {
                var inputKinds = obsWebSocket.GetInputKindList();
                Log("Available input kinds: " + string.Join(", ", inputKinds));
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting input kinds: {ex.Message}");
            }
        }
    }
    /// <summary>
    /// Update the NDI source to ensure it's connected to our Unity NDI output
    /// </summary>
    private void UpdateNdiSource()
    {
        try
        {
            // Create settings specific for NDI source
            var inputSettings = new JObject();
            
            // Get the full NDI source name including computer name
            string fullNdiName = GetFullNdiSourceName();
            
            // Set the NDI source name
            inputSettings["ndi_source_name"] = fullNdiName;
            
            inputSettings["ndi_behavior"] = 0;
            
            // Update the existing NDI source
            obsWebSocket.SetInputSettings(
                sourceName,     // Input name
                inputSettings,  // Input settings with the full NDI source name
                true            // Overlay with existing settings
            );
            
            Log($"Updated NDI source '{sourceName}' to connect to '{fullNdiName}'");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating NDI source: {e.Message}");
        }
    }

    /// <summary>
    /// Check if filter exists and add if needed
    /// </summary>
    private void CheckAndAddFilter()
    {
        try
        {
            bool filterExists = FilterExists(sourceName, filterName);
            
            if (!filterExists)
            {
                AddFilterToSource();
            }
            else
            {
                Log($"Filter '{filterName}' already exists on source '{sourceName}'");
                
                // Update filter settings
                UpdateFilterSettings();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking/adding filter: {e.Message}");
        }
    }

    /// <summary>
    /// Check if a specific filter exists on the source
    /// </summary>
    private bool FilterExists(string sourceName, string filterName)
    {
        try
        {
            var filters = obsWebSocket.GetSourceFilterList(sourceName);
            foreach (var filter in filters)
            {
                if (filter.Name == filterName)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if filter exists: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Add "Dedicated NDI® output" filter to the NDI source
    /// </summary>
    private void AddFilterToSource()
    {
        try
        {
            // Create filter settings
            var filterSettings = new JObject();
            
            // Use the manually specified property name
            filterSettings[ndiPropertyName] = ndiOutputName;
            
            // Add the filter to the source
            obsWebSocket.CreateSourceFilter(
                sourceName,     // Source name
                filterName,     // Filter name (e.g., "Dedicated NDI® output")
                filterType,     // Filter type (e.g., "ndi_filter")
                filterSettings  // Filter settings
            );
            
            Log($"Added filter '{filterName}' to source '{sourceName}' with {ndiPropertyName}='{ndiOutputName}'");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error adding filter to source: {e.Message}");
        }
    }

    /// <summary>
    /// Update filter settings with specified property name
    /// </summary>
    private void UpdateFilterSettings()
    {
        try
        {
            // Create settings for the NDI filter
            var filterSettings = new JObject();
            
            // Use the manually specified property name
            filterSettings[ndiPropertyName] = ndiOutputName;
            
            // Update the filter settings
            obsWebSocket.SetSourceFilterSettings(
                sourceName,     // Source name
                filterName,     // Filter name
                filterSettings  // Updated settings
            );
            
            Log($"Updated filter '{filterName}' on source '{sourceName}' with {ndiPropertyName}='{ndiOutputName}'");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating filter settings: {e.Message}");
        }
    }

    /// <summary>
    /// Conditional logging based on detailed info setting
    /// </summary>
    private void Log(string message)
    {
        if (logDetailedInfo)
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// Change the scene name and check/create scene
    /// </summary>
    public void SetSceneName(string newName)
    {
        sceneName = newName;
        CheckAndCreateScene();
    }

    private void OnDestroy()
    {
        // Unsubscribe from events when destroyed
        if (webSocketManager != null)
        {
            webSocketManager.WsConnected -= OnWebSocketConnected;
        }
    }
}