using System;
using System.Collections.Generic;
using System.Linq;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Static utility class for common OBS operations
/// Provides reusable methods that can be used by any OBS-related script
/// </summary>
public static class ObsUtilities
{
    #region Scene Operations
    
    /// <summary>
    /// Check if a scene with the given name exists in OBS
    /// </summary>
    public static bool SceneExists(OBSWebsocket obsWebSocket, string sceneName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var sceneList = obsWebSocket.GetSceneList();
            return sceneList.Scenes.Any(scene => scene.Name == sceneName);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if scene exists: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Create a new scene in OBS
    /// </summary>
    public static bool CreateScene(OBSWebsocket obsWebSocket, string sceneName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            obsWebSocket.CreateScene(sceneName);
            Debug.Log($"Scene '{sceneName}' created in OBS");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating scene '{sceneName}': {e.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region Source Operations
    
    /// <summary>
    /// Check if a source with the given name exists in the specified scene
    /// </summary>
    public static bool SourceExistsInScene(OBSWebsocket obsWebSocket, string sceneName, string sourceName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var sceneItemList = obsWebSocket.GetSceneItemList(sceneName);
            return sceneItemList.Any(item => item.SourceName.Equals(sourceName));
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if source exists: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Create an NDI source in the specified scene
    /// </summary>
    public static bool CreateNdiSource(OBSWebsocket obsWebSocket, string sceneName, string sourceName, string ndiSourceName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var inputSettings = new JObject
            {
                ["ndi_source_name"] = ndiSourceName,
                ["ndi_behavior"] = 0
            };
            
            obsWebSocket.CreateInput(
                sceneName,
                sourceName,
                "ndi_source",
                inputSettings,
                true
            );
            
            Debug.Log($"Created NDI source '{sourceName}' in scene '{sceneName}' connected to '{ndiSourceName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating NDI source: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Create a scene source (adds one scene as a source in another scene)
    /// </summary>
    public static bool CreateSceneSource(OBSWebsocket obsWebSocket, string targetSceneName, string sourceSceneName, string sourceName = null)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        // Use source scene name as source name if not specified
        if (string.IsNullOrEmpty(sourceName))
            sourceName = sourceSceneName;
            
        // try
        // {
            Debug.LogWarning("on create scene source");
            var inputSettings = new JObject
            {
            };
            
            obsWebSocket.CreateSceneItem(
                targetSceneName,
                sourceName,
                true
            );
            
            Debug.Log($"Added scene '{sourceSceneName}' as source '{sourceName}' to scene '{targetSceneName}'");
            return true;
        // }
        // catch (Exception e)
        // {
        //     Debug.LogError($"Error creating scene source: {e.Message}");
        //     return false;
        // }
    }
    
    /// <summary>
    /// Update NDI source settings
    /// </summary>
    public static bool UpdateNdiSource(OBSWebsocket obsWebSocket, string sourceName, string ndiSourceName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var inputSettings = new JObject
            {
                ["ndi_source_name"] = ndiSourceName,
                ["ndi_behavior"] = 0
            };
            
            obsWebSocket.SetInputSettings(sourceName, inputSettings, true);
            Debug.Log($"Updated NDI source '{sourceName}' to connect to '{ndiSourceName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating NDI source: {e.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region Scene Item Operations
    
    /// <summary>
    /// Move a scene item to the bottom layer (index 0)
    /// </summary>
    public static bool MoveSceneItemToBottom(OBSWebsocket obsWebSocket, string sceneName, string sourceName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            // Get the scene item ID first
            var sceneItemList = obsWebSocket.GetSceneItemList(sceneName);
            var sceneItem = sceneItemList.FirstOrDefault(item => item.SourceName == sourceName);
            
            if (sceneItem == null)
            {
                Debug.LogError($"Scene item '{sourceName}' not found in scene '{sceneName}'");
                return false;
            }
            
            // Move to bottom (index 0)
            obsWebSocket.SetSceneItemIndex(sceneName, sceneItem.ItemId, 0);
            Debug.Log($"Moved scene item '{sourceName}' to bottom of scene '{sceneName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error moving scene item to bottom: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Move a scene item to a specific index
    /// </summary>
    public static bool MoveSceneItemToIndex(OBSWebsocket obsWebSocket, string sceneName, string sourceName, int index)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var sceneItemList = obsWebSocket.GetSceneItemList(sceneName);
            var sceneItem = sceneItemList.FirstOrDefault(item => item.SourceName == sourceName);
            
            if (sceneItem == null)
            {
                Debug.LogError($"Scene item '{sourceName}' not found in scene '{sceneName}'");
                return false;
            }
            
            obsWebSocket.SetSceneItemIndex(sceneName, sceneItem.ItemId, index);
            Debug.Log($"Moved scene item '{sourceName}' to index {index} in scene '{sceneName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error moving scene item to index: {e.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region Filter Operations
    
    /// <summary>
    /// Check if a specific filter exists on a source
    /// </summary>
    public static bool FilterExists(OBSWebsocket obsWebSocket, string sourceName, string filterName)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var filters = obsWebSocket.GetSourceFilterList(sourceName);
            return filters.Any(filter => filter.Name == filterName);
        }
        catch (Exception e)
        {
            Debug.LogError($"Error checking if filter exists: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Add an NDI output filter to a source
    /// </summary>
    public static bool CreateNdiOutputFilter(OBSWebsocket obsWebSocket, string sourceName, string filterName, string ndiOutputName, string ndiPropertyName = "ndi_filter_ndiname")
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var filterSettings = new JObject
            {
                [ndiPropertyName] = ndiOutputName
            };
            
            obsWebSocket.CreateSourceFilter(
                sourceName,
                filterName,
                "ndi_filter",
                filterSettings
            );
            
            Debug.Log($"Added NDI output filter '{filterName}' to source '{sourceName}' with output name '{ndiOutputName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error adding NDI output filter: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Update NDI output filter settings
    /// </summary>
    public static bool UpdateNdiOutputFilter(OBSWebsocket obsWebSocket, string sourceName, string filterName, string ndiOutputName, string ndiPropertyName = "ndi_filter_ndiname")
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            throw new InvalidOperationException("OBS WebSocket is not connected");
            
        try
        {
            var filterSettings = new JObject
            {
                [ndiPropertyName] = ndiOutputName
            };
            
            obsWebSocket.SetSourceFilterSettings(sourceName, filterName, filterSettings);
            Debug.Log($"Updated NDI output filter '{filterName}' on source '{sourceName}' with output name '{ndiOutputName}'");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating NDI output filter: {e.Message}");
            return false;
        }
    }
    
    #endregion
    
    #region Scene Finding Operations

/// <summary>
/// Find a scene that contains a source with a specific filter property value
/// </summary>
public static string FindSceneBySourceFilter(OBSWebsocket obsWebSocket, string filterName, string propertyName, string propertyValue)
{
    if (obsWebSocket == null || !obsWebSocket.IsConnected)
        throw new InvalidOperationException("OBS WebSocket is not connected");
    try
    {
        var sceneList = obsWebSocket.GetSceneList();
        
        foreach (var scene in sceneList.Scenes)
        {
            var sceneItemList = obsWebSocket.GetSceneItemList(scene.Name);
            
            foreach (var sceneItem in sceneItemList)
            {
                try
                {
                    var filters = obsWebSocket.GetSourceFilterList(sceneItem.SourceName);
                    
                    foreach (var filter in filters)
                    {
                        if (filter.Name == filterName)
                        {
                            var filterSettings = obsWebSocket.GetSourceFilter(sceneItem.SourceName, filterName);
                            if (filterSettings.Settings.ContainsKey(propertyName))
                            {
                                
                                string currentValue = filterSettings.Settings[propertyName]?.ToString();
                                if (propertyValue.Contains(currentValue))
                                {
                                    Debug.Log($"Found scene '{scene.Name}' with source '{sceneItem.SourceName}' having filter '{filterName}' with {propertyName}='{propertyValue}'");
                                    return scene.Name;
                                }
                            }
                        }
                    }
                }
                catch (Exception sourceEx)
                {
                    Debug.LogWarning($"Error checking filters for source '{sceneItem.SourceName}': {sourceEx.Message}");
                    continue;
                }
            }
        }
        
        Debug.Log($"No scene found with filter '{filterName}' having {propertyName}='{propertyValue}'");
        return null;
    }
    catch (Exception e)
    {
        Debug.LogError($"Error searching for scene by filter: {e.Message}");
        return null;
    }
}

#endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Get available input kinds for debugging
    /// </summary>
    public static List<string> GetAvailableInputKinds(OBSWebsocket obsWebSocket)
    {
        if (obsWebSocket == null || !obsWebSocket.IsConnected)
            return new List<string>();
            
        try
        {
            return obsWebSocket.GetInputKindList();
        }
        catch
        {
            return new List<string>();
        }
    }
    
    #endregion
}