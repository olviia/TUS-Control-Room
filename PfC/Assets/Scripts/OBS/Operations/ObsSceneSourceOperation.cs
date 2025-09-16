using UnityEngine;

/// <summary>
/// OBS operation that adds one scene as a source to another scene
/// Optionally positions it at the bottom layer
/// </summary>
public class ObsSceneSourceOperation : ObsOperationBase
{
    
     private string targetSceneName = "MainStream";
     private string sourceSceneName = "PlayerView";
     private string customSourceName = ""; // Optional: override source name
    
    
     private bool moveToBottom = true;
     private int customLayerIndex = -1; // -1 means use moveToBottom setting
    
     private bool createTargetSceneIfMissing = true;
    
    private string searchFilterName = Constants.DEDICATED_NDI_OUTPUT;
    private string searchPropertyName = "ndi_filter_ndiname";
    private string searchPropertyValue = "";
    /// <summary>
    /// Execute the scene source operation
    /// This is called automatically when OBS connects
    /// </summary>
    protected override void ExecuteOperation()
    {
        // Validate our settings first
        if (!ValidateSettings())
        {
            return;
        }
        
        // Step 1: Ensure target scene exists
        if (!EnsureTargetSceneExists())
        {
            return;
        }
        
        // Step 2: Add the scene source
        if (!AddSceneSource())
        {
            return;
        }
        
        // Step 3: Position the source if requested
        PositionSceneSource();
        
        LogDetailed($"Scene source operation completed successfully");
    }
    
    /// <summary>
    /// Validate all the settings before executing
    /// </summary>
    private bool ValidateSettings()
    {
        if (!ValidateObsConnection())
        {
            return false;
        }
        
        if (!ValidateStringParameter(targetSceneName, "Target Scene Name"))
        {
            return false;
        }
        
        if (!ValidateStringParameter(sourceSceneName, "Source Scene Name"))
        {
            return false;
        }
        
        // Check that source scene exists in OBS
        if (!ExecuteWithErrorHandling(
            () => ObsUtilities.SceneExists(obsWebSocket, sourceSceneName),
            $"Checking if source scene '{sourceSceneName}' exists"))
        {
            LogError($"Source scene '{sourceSceneName}' does not exist in OBS");
            return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Ensure the target scene exists, create if needed
    /// </summary>
    private bool EnsureTargetSceneExists()
    {
        bool sceneExists = ExecuteWithErrorHandling(
            () => ObsUtilities.SceneExists(obsWebSocket, targetSceneName),
            $"Checking if target scene '{targetSceneName}' exists");
        
        if (!sceneExists)
        {
            if (createTargetSceneIfMissing)
            {
                LogDetailed($"Target scene '{targetSceneName}' doesn't exist, creating it");
                return ExecuteWithErrorHandling(
                    () => ObsUtilities.CreateScene(obsWebSocket, targetSceneName),
                    $"Creating target scene '{targetSceneName}'");
            }
            else
            {
                LogError($"Target scene '{targetSceneName}' does not exist and createTargetSceneIfMissing is false");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Add the scene source to the target scene
    /// </summary>
    private bool AddSceneSource()
    {
        string sourceName = GetSourceName();
        
        // Check if source already exists
        bool sourceExists = ObsUtilities.SourceExistsInScene(obsWebSocket, targetSceneName, sourceName);
        if (sourceExists)
        {
            LogDetailed($"Source '{sourceName}' already exists in scene '{targetSceneName}'");
            return true;
        }

        return ObsUtilities.CreateSceneSource(obsWebSocket, targetSceneName, sourceSceneName, sourceName);
    }
    
    /// <summary>
    /// Position the scene source according to settings
    /// </summary>
    private void PositionSceneSource()
    {
        string sourceName = GetSourceName();
        
        if (customLayerIndex >= 0)
        {
            ObsUtilities.MoveSceneItemToIndex(obsWebSocket, targetSceneName, sourceName, customLayerIndex);
        }
        else if (moveToBottom)
        {
            // Move to bottom
            ObsUtilities.MoveSceneItemToBottom(obsWebSocket, targetSceneName, sourceName);
        }
        // If neither option is selected, leave the source where OBS places it by default
    }
    
    /// <summary>
    /// Get the name to use for the source in the target scene
    /// </summary>
    private string GetSourceName()
    {
        // Use custom name if provided, otherwise use the source scene name
        return string.IsNullOrEmpty(customSourceName) ? sourceSceneName : customSourceName;
    }
    
    #region Public Methods for Runtime Use
    
    /// <summary>
    /// Change the target scene name and re-execute the operation
    /// Useful for runtime configuration
    /// </summary>
    public void SetTargetScene(string newTargetSceneName)
    {
        targetSceneName = newTargetSceneName;
        LogDetailed($"Target scene changed to '{targetSceneName}'");
        
        if (IsReadyToExecute())
        {
            ExecuteManually();
        }
    }
    
    /// <summary>
    /// Change the source scene name and re-execute the operation
    /// Useful for runtime configuration
    /// </summary>
    public void SetSourceScene(string newSourceSceneName)
    {
        sourceSceneName = newSourceSceneName;
        LogDetailed($"Source scene changed to '{sourceSceneName}'");
        
        if (IsReadyToExecute())
        {
            ExecuteManually();
        }
    }
    
    /// <summary>
    /// Configure and execute the operation programmatically
    /// </summary>
    public void ConfigureAndExecute(string targetScene, string sourceScene, bool moveToBottom = true, string customName = "")
    {
        this.targetSceneName = targetScene;
        this.sourceSceneName = sourceScene;
        this.moveToBottom = moveToBottom;
        this.customSourceName = customName;
        
        LogDetailed($"Configured: Target='{targetScene}', Source='{sourceScene}', MoveToBottom={moveToBottom}");
        
        if (IsReadyToExecute())
        {
            ExecuteOperation();
        }
        else
        {
            LogDetailed("Configuration saved, will execute when OBS connection is ready");
        }
    }

    
    
    #endregion

    public void SetTvProgrammeScene(string ndiName)
    {
        //add code to remove the item
        //to do it, use RemoveSceneItem()
        
        string name = ObsUtilities.FindSceneBySourceFilter(SharedObsWebSocket, Constants.DEDICATED_NDI_OUTPUT,
            "ndi_filter_ndiname",
            ndiName);
            
        ConfigureAndExecute("StreamLive", name, true, name);
    }


}