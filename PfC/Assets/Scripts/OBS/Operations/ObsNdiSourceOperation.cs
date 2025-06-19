using UnityEngine;
using Klak.Ndi;

/// <summary>
/// OBS operation that creates a scene and adds an NDI source that receives the stream from Unity
/// Optionally adds an NDI output filter to create a new NDI output from OBS
/// </summary>
public class ObsNdiSourceOperation : ObsOperationBase
{
    [Header("Scene Settings")]
    [SerializeField] private string sceneName = "UnityNDI";
    [SerializeField] private string sourceName = "Unity Stream";
    
    [Header("NDI Settings")]
    [SerializeField] private bool autoDetectNdiSender = true;
    [SerializeField] private string manualNdiName = ""; // Used if autoDetectNdiSender is false
    
    [Header("Filter Settings")]
    [Tooltip("Add this filter allows to create a new NDI output from OBS")]
    [SerializeField] private bool addFilterToSource = true;
    [SerializeField] private string ndiOutputName = "OBS Output";
    
    [Header("Advanced Settings")]
    [SerializeField] private bool createSceneIfMissing = true;
    [SerializeField] private string filterName = "Dedicated NDI® output";
    [SerializeField] private string filterType = "ndi_filter";
    [SerializeField] private string ndiPropertyName = "ndi_filter_ndiname";
    
    // Component references
    private NdiSender ndiSender;
    
    /// <summary>
    /// Initialize NDI-specific components
    /// </summary>
    protected override void InitializeObsConnection()
    {
        // Get the NdiSender component
        ndiSender = GetComponent<NdiSender>();
        
        if (ndiSender == null && autoDetectNdiSender)
        {
            LogError("NdiSender component not found on the same GameObject, but autoDetectNdiSender is enabled");
        }
        
        // Call base initialization
        base.InitializeObsConnection();
    }
    
    /// <summary>
    /// Execute the NDI source operation
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
        if (!EnsureSceneExists())
        {
            return;
        }
        
        // Step 2: Add or update the NDI source
        if (!AddOrUpdateNdiSource())
        {
            return;
        }
        
        // Step 3: Add NDI output filter if requested
        if (addFilterToSource)
        {
            // Small delay to ensure source is fully created
            AddNdiOutputFilter();
        }
        
        LogDetailed($"NDI source operation completed successfully");
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
        
        if (!ValidateStringParameter(sceneName, "Scene Name"))
        {
            return false;
        }
        
        if (!ValidateStringParameter(sourceName, "Source Name"))
        {
            return false;
        }
        
        // Validate NDI name
        string ndiName = GetNdiName();
        if (string.IsNullOrEmpty(ndiName))
        {
            LogError("NDI name could not be determined. Either enable autoDetectNdiSender with an NdiSender component, or provide a manual NDI name");
            return false;
        }
        
        // Validate filter settings if filter is enabled
        if (addFilterToSource)
        {
            if (!ValidateStringParameter(ndiOutputName, "NDI Output Name"))
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Ensure the target scene exists, create if needed
    /// </summary>
    private bool EnsureSceneExists()
    {
        bool sceneExists = ExecuteWithErrorHandling(
            () => ObsUtilities.SceneExists(obsWebSocket, sceneName),
            $"Checking if scene '{sceneName}' exists");
        
        if (!sceneExists)
        {
            if (createSceneIfMissing)
            {
                LogDetailed($"Scene '{sceneName}' doesn't exist, creating it");
                return ExecuteWithErrorHandling(
                    () => ObsUtilities.CreateScene(obsWebSocket, sceneName),
                    $"Creating scene '{sceneName}'");
            }
            else
            {
                LogError($"Scene '{sceneName}' does not exist and createSceneIfMissing is false");
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Add the NDI source or update existing one
    /// </summary>
    private bool AddOrUpdateNdiSource()
    {
        string fullNdiName = GetFullNdiSourceName(GetNdiName());
        
        // Check if source already exists
        bool sourceExists = ExecuteWithErrorHandling(
            () => ObsUtilities.SourceExistsInScene(obsWebSocket, sceneName, sourceName),
            $"Checking if NDI source '{sourceName}' exists in scene '{sceneName}'");
        
        if (sourceExists)
        {
            LogDetailed($"NDI source '{sourceName}' already exists, updating it");
            return ExecuteWithErrorHandling(
                () => ObsUtilities.UpdateNdiSource(obsWebSocket, sourceName, fullNdiName),
                $"Updating NDI source '{sourceName}' to connect to '{fullNdiName}'");
        }
        else
        {
            LogDetailed($"Creating new NDI source '{sourceName}' connected to '{fullNdiName}'");
            return ExecuteWithErrorHandling(
                () => ObsUtilities.CreateNdiSource(obsWebSocket, sceneName, sourceName, fullNdiName),
                $"Creating NDI source '{sourceName}' in scene '{sceneName}'");
        }
    }
    
    /// <summary>
    /// Add NDI output filter to the source
    /// </summary>
    private void AddNdiOutputFilter()
    {
        // Check if filter already exists
        bool filterExists = ExecuteWithErrorHandling(
            () => ObsUtilities.FilterExists(obsWebSocket, sourceName, filterName),
            $"Checking if filter '{filterName}' exists on source '{sourceName}'");
        
        if (filterExists)
        {
            LogDetailed($"Filter '{filterName}' already exists, updating it");
            ExecuteWithErrorHandling(
                () => ObsUtilities.UpdateNdiOutputFilter(obsWebSocket, sourceName, filterName, ndiOutputName, ndiPropertyName),
                $"Updating NDI output filter '{filterName}' on source '{sourceName}'");
        }
        else
        {
            LogDetailed($"Creating NDI output filter '{filterName}' on source '{sourceName}'");
            ExecuteWithErrorHandling(
                () => ObsUtilities.CreateNdiOutputFilter(obsWebSocket, sourceName, filterName, ndiOutputName, ndiPropertyName),
                $"Adding NDI output filter '{filterName}' to source '{sourceName}'");
        }
    }
    
    /// <summary>
    /// Get the NDI name to use (either from NdiSender component or manual setting)
    /// </summary>
    private string GetNdiName()
    {
        if (autoDetectNdiSender && ndiSender != null)
        {
            return ndiSender.ndiName;
        }
        
        return manualNdiName;
    }
    
    #region Public Methods for Runtime Use
    
    /// <summary>
    /// Change the scene name and re-execute the operation
    /// Useful for runtime configuration
    /// </summary>
    public void SetSceneName(string newSceneName)
    {
        sceneName = newSceneName;
        LogDetailed($"Scene name changed to '{sceneName}'");
        
        if (IsReadyToExecute())
        {
            ExecuteManually();
        }
    }
    
    /// <summary>
    /// Change the source name and re-execute the operation
    /// Useful for runtime configuration
    /// </summary>
    public void SetSourceName(string newSourceName)
    {
        sourceName = newSourceName;
        LogDetailed($"Source name changed to '{sourceName}'");
        
        if (IsReadyToExecute())
        {
            ExecuteManually();
        }
    }
    
    /// <summary>
    /// Change the NDI output name for the filter
    /// </summary>
    public void SetNdiOutputName(string newNdiOutputName)
    {
        ndiOutputName = newNdiOutputName;
        LogDetailed($"NDI output name changed to '{ndiOutputName}'");
        
        // Update filter if it exists and we're connected
        if (IsReadyToExecute() && addFilterToSource)
        {
            AddNdiOutputFilter();
        }
    }
    
    /// <summary>
    /// Configure and execute the operation programmatically
    /// </summary>
    public void ConfigureAndExecute(string sceneName, string sourceName, string ndiName = "", string ndiOutputName = "", bool addFilter = true)
    {
        this.sceneName = sceneName;
        this.sourceName = sourceName;
        this.addFilterToSource = addFilter;
        
        if (!string.IsNullOrEmpty(ndiName))
        {
            this.autoDetectNdiSender = false;
            this.manualNdiName = ndiName;
        }
        
        if (!string.IsNullOrEmpty(ndiOutputName))
        {
            this.ndiOutputName = ndiOutputName;
        }
        
        LogDetailed($"Configured NDI operation: Scene='{sceneName}', Source='{sourceName}', NDI='{GetNdiName()}', AddFilter={addFilter}");
        
        if (IsReadyToExecute())
        {
            ExecuteManually();
        }
        else
        {
            LogDetailed("Configuration saved, will execute when OBS connection is ready");
        }
    }
    
    /// <summary>
    /// Force refresh of the NDI source (useful if NDI stream changes)
    /// </summary>
    public void RefreshNdiSource()
    {
        if (!IsReadyToExecute())
        {
            LogError("Cannot refresh NDI source - OBS connection not ready");
            return;
        }
        
        LogDetailed("Refreshing NDI source");
        AddOrUpdateNdiSource();
    }
    
    #endregion
}