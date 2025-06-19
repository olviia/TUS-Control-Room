using UnityEngine;
using OBSWebsocketDotNet;

/// <summary>
/// Static utility class for finding OBS scenes and sources
/// Uses the shared OBS WebSocket connection from ObsOperationBase
/// Perfect for simple one-liner calls from button clicks or other scripts
/// </summary>
public class ObsFinder:ObsOperationBase
{
    /// <summary>
    /// Find a scene by looking for a source with a specific filter property value
    /// </summary>
    /// <param name="filterName">Name of the filter to search for</param>
    /// <param name="propertyName">Name of the property in the filter</param>
    /// <param name="propertyValue">Value the property should match</param>
    /// <returns>Scene name if found, null if not found or connection not ready</returns>
    public string FindSceneByFilterPropertyName(string filterName, string propertyName, string propertyValue)
    {
        if (obsWebSocket == null)
        {
            Debug.LogError("OBS WebSocket not available. Make sure an ObsOperationBase component is in the scene and connected.");
            return null;
        }
        
        return ObsUtilities.FindSceneBySourceFilter(obsWebSocket, filterName, propertyName, propertyValue);
    }

    
    /// <summary>
    /// Check if a scene exists
    /// </summary>
    /// <param name="sceneName">Name of the scene to check</param>
    /// <returns>True if scene exists, false otherwise</returns>
    public bool SceneExists(string sceneName)
    {
        if (obsWebSocket == null)
        {
            Debug.LogError("OBS WebSocket not available. Make sure an ObsOperationBase component is in the scene and connected.");
            return false;
        }
        
        return ObsUtilities.SceneExists(obsWebSocket, sceneName);
    }
    
    /// <summary>
    /// Check if a source exists in a scene
    /// </summary>
    /// <param name="sceneName">Name of the scene</param>
    /// <param name="sourceName">Name of the source</param>
    /// <returns>True if source exists in the scene, false otherwise</returns>
    public bool SourceExistsInScene(string sceneName, string sourceName)
    {
        if (obsWebSocket == null)
        {
            Debug.LogError("OBS WebSocket not available. Make sure an ObsOperationBase component is in the scene and connected.");
            return false;
        }
        
        return ObsUtilities.SourceExistsInScene(obsWebSocket, sceneName, sourceName);
    }
    
    /// <summary>
    /// Check if the OBS connection is ready
    /// </summary>
    /// <returns>True if connected and ready, false otherwise</returns>
    public bool IsObsReady()
    {
       
        return obsWebSocket != null && obsWebSocket.IsConnected;
    }
    



    protected override void ExecuteOperation()
    {
        throw new System.NotImplementedException();
    }
}