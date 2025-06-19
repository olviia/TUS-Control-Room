using System;
using OBSWebsocketDotNet;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Base class for all OBS operations
/// Handles Unity lifecycle, networking, connection management, and common functionality
/// </summary>
public abstract class ObsOperationBase : NetworkBehaviour
{
    [Header("General Settings")]
    [SerializeField] protected bool logDetailedInfo = true;
    [SerializeField] protected bool executeOnlyOnServer = false;
    
    // Protected properties accessible by derived classes
    protected OBSWebsocket obsWebSocket;
    protected WebsocketManager webSocketManager;
    protected bool isInitialized = false;
    
    // Static property to share server computer name across instances
    public static string ServerComputerName { get; private set; } = "";
    
    // Static property to share OBS WebSocket across instances for static access
    public static OBSWebsocket SharedObsWebSocket { get; private set; }
    
    #region Unity Lifecycle

    protected void Awake()
    {
        Debug.LogWarning("obs operation awake");
        InitializeObsConnection();
    }

    #endregion
    
    #region Connection Management
    
    /// <summary>
    /// Initialize the OBS WebSocket connection
    /// </summary>
    protected virtual void InitializeObsConnection()
    {
        // Find the WebsocketManager
        webSocketManager = FindFirstObjectByType<WebsocketManager>();
        
        if (webSocketManager == null)
        {
            Debug.LogError("WebsocketManager was not found in the scene");
            return;
        }
        
        // Get the OBS WebSocket using reflection (keeping your current approach)
        obsWebSocket = (OBSWebsocket)typeof(WebsocketManager)
            .GetField("obsWebSocket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(webSocketManager);
        SharedObsWebSocket = obsWebSocket;
        
        if (obsWebSocket == null)
        {
            Debug.LogError("Failed to get OBS WebSocket from WebsocketManager");
            return;
        }
        
        Debug.LogWarning("found websocket: " + obsWebSocket);
        
        // Set the shared static reference for ObsFinder
        SharedObsWebSocket = obsWebSocket;
        Debug.LogWarning("assigned SharedObsWebSocket: " + SharedObsWebSocket);
        
        
        isInitialized = true;
        Debug.LogWarning("OBS connection initialized successfully");
    }
    
    /// <summary>
    /// Called when WebSocket connection state changes
    /// </summary>
    
    #endregion
    
    #region Abstract Methods
    
    /// <summary>
    /// Execute the specific OBS operation
    /// This must be implemented by derived classes
    /// </summary>
    protected abstract void ExecuteOperation();
    
    #endregion
    
    #region Validation
    
    /// <summary>
    /// Validate that the OBS connection is ready
    /// </summary>
    protected bool ValidateObsConnection()
    {
        if (!isInitialized)
        {
            Debug.LogError("OBS connection not initialized");
            return false;
        }
        
        if (obsWebSocket == null)
        {
            Debug.LogError("OBS WebSocket is null");
            return false;
        }
        
        if (!obsWebSocket.IsConnected)
        {
            Debug.LogError("Not connected to OBS WebSocket");
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Validate that a string parameter is not null or empty
    /// </summary>
    protected bool ValidateStringParameter(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            Debug.LogError($"{parameterName} is not set or is empty");
            return false;
        }
        return true;
    }
    
    #endregion
    
    #region Public Methods
    
    /// <summary>
    /// Manually trigger the operation execution
    /// Useful for calling from other scripts or UI
    /// </summary>
    public virtual void ExecuteManually()
    {
        if (!ValidateObsConnection())
        {
            Debug.LogError("Cannot execute operation - OBS connection not ready");
            return;
        }
        
        Debug.LogWarning("Manual execution triggered");
        ExecuteOperation();
    }
    
    /// <summary>
    /// Check if the operation is ready to execute
    /// </summary>
    public virtual bool IsReadyToExecute()
    {
        return isInitialized && obsWebSocket != null && obsWebSocket.IsConnected;
    }
    
    #endregion
    
    #region Logging
    
    /// <summary>
    /// Log a message if detailed logging is enabled
    /// </summary>
    protected void LogDetailed(string message)
    {
        if (logDetailedInfo)
        {
            Debug.Log($"[{GetType().Name}] {message}");
        }
    }
    
    /// <summary>
    /// Always log errors regardless of detailed logging setting
    /// </summary>
    protected void LogError(string message)
    {
        Debug.LogError($"[{GetType().Name}] {message}");
    }
    
    /// <summary>
    /// Always log warnings regardless of detailed logging setting
    /// </summary>
    protected void LogWarning(string message)
    {
        Debug.LogWarning($"[{GetType().Name}] {message}");
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Get the full NDI source name in the format used by OBS
    /// Helper method for operations that need NDI naming
    /// </summary>
    protected string GetFullNdiSourceName(string ndiName)
    {
        return $"{ServerComputerName} ({ndiName})";
    }
    
    /// <summary>
    /// Execute an operation with error handling
    /// Returns true if successful, false if failed
    /// </summary>
    protected bool ExecuteWithErrorHandling(Func<bool> operation, string operationName)
    {
        try
        {
            bool result = operation();
            if (result)
            {
                Debug.LogWarning($"{operationName} completed successfully");
            }
            else
            {
                Debug.LogError($"{operationName} failed");
            }
            return result;
        }
        catch (Exception e)
        {
            Debug.LogError($"{operationName} failed with exception: {e.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region Convenience Methods for OBS Utilities
    
    /// <summary>
    /// Find a scene that contains a source with a specific filter property
    /// Convenience method that uses this instance's OBS connection
    /// </summary>
    protected string FindSceneBySourceFilter(string filterName, string propertyName, string propertyValue)
    {
        if (!ValidateObsConnection()) return null;
        return ObsUtilities.FindSceneBySourceFilter(obsWebSocket, filterName, propertyName, propertyValue);
    }
    
    // /// <summary>
    // /// Find all scenes that contain sources with a specific filter property
    // /// Convenience method that uses this instance's OBS connection
    // /// </summary>
    // protected string[] FindAllScenesBySourceFilter(string filterName, string propertyName, string propertyValue)
    // {
    //     if (!ValidateObsConnection()) return new string[0];
    //     return ObsUtilities.FindAllScenesBySourceFilter(obsWebSocket, filterName, propertyName, propertyValue);
    // }
    
    /// <summary>
    /// Check if a scene exists
    /// Convenience method that uses this instance's OBS connection
    /// </summary>
    protected bool SceneExists(string sceneName)
    {
        if (!ValidateObsConnection()) return false;
        return ObsUtilities.SceneExists(obsWebSocket, sceneName);
    }
    
    /// <summary>
    /// Check if a source exists in a scene
    /// Convenience method that uses this instance's OBS connection
    /// </summary>
    protected bool SourceExistsInScene(string sceneName, string sourceName)
    {
        if (!ValidateObsConnection()) return false;
        return ObsUtilities.SourceExistsInScene(obsWebSocket, sceneName, sourceName);
    }
    
    /// <summary>
    /// Check if a filter exists on a source
    /// Convenience method that uses this instance's OBS connection
    /// </summary>
    protected bool FilterExists(string sourceName, string filterName)
    {
        if (!ValidateObsConnection()) return false;
        return ObsUtilities.FilterExists(obsWebSocket, sourceName, filterName);
    }
    
    #endregion
}