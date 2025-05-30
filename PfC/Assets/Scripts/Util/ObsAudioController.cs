using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System.Reflection;
using System.Linq;
using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet.Communication;
using TMPro;

public class OBSAudioMixerController : MonoBehaviour
{
    [Header("UI Setup")]
    public Transform audioSourcesParent; // Parent transform for audio source UI elements
    public GameObject audioSourcePrefab; // Prefab for each audio source (should contain Slider and Toggle)
    
    [Header("Settings")]
    [Range(0.1f, 2f)]
    public float updateInterval = 0.4f; // Fallback polling interval
    
    private OBSWebsocket obsWebSocket;
    private WebsocketManager webSocketManager;
    private Dictionary<string, AudioSourceUI> audioSourceUIs = new Dictionary<string, AudioSourceUI>();
    private bool pendingRefresh = false;
    private bool pendingClear = false;
    private bool isConnected = false;
    
    [System.Serializable]
    public class AudioSourceUI
    {
        public GameObject gameObject;
        public Slider volumeSlider;
        public Toggle muteToggle;
        public Text nameLabel;
        public TextMeshProUGUI nameLabelTMP;
        public string sourceName;
        
        public AudioSourceUI(GameObject go, string name)
        {
            gameObject = go;
            sourceName = name;
            volumeSlider = go.GetComponentInChildren<Slider>();
            muteToggle = go.GetComponentInChildren<Toggle>();
            nameLabel = go.GetComponentInChildren<Text>();
            nameLabelTMP = go.GetComponentInChildren<TextMeshProUGUI>();
            
            // Set the source name on both Text and TextMeshPro components
            if (nameLabel != null)
                nameLabel.text = name;
                
            if (nameLabelTMP != null)
                nameLabelTMP.text = name;
        }
    }
    
    void Start()
    {
        InitializeOBSConnection();
    }
    
    void Update()
    {
        // Handle pending operations on the main thread
        if (pendingRefresh)
        {
            pendingRefresh = false;
            RefreshAudioSources();
        }
        
        if (pendingClear)
        {
            pendingClear = false;
            ClearAudioSourcesUI();
        }
    }
    
    
    void InitializeOBSConnection()
    {
        try
        {
            webSocketManager = FindObjectOfType<WebsocketManager>();
            if (webSocketManager == null)
            {
                Debug.LogError("WebsocketManager not found in scene!");
                return;
            }
            
            obsWebSocket = (OBSWebsocket)typeof(WebsocketManager)
                .GetField("obsWebSocket", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(webSocketManager);
                
            if (obsWebSocket == null)
            {
                Debug.LogError("Could not access OBSWebsocket from WebsocketManager!");
                return;
            }
            
            // Subscribe to connection events
            obsWebSocket.Connected += OnOBSConnected;
            obsWebSocket.Disconnected += OnOBSDisconnected;
            
            // Subscribe to audio events
            obsWebSocket.InputVolumeChanged += OnInputVolumeChanged;
            obsWebSocket.InputMuteStateChanged += OnInputMuteStateChanged;
            
            // Check if already connected
            if (obsWebSocket.IsConnected)
            {
                OnOBSConnected(null, null);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize OBS connection: {e.Message}");
        }
    }
    
    void OnOBSConnected(object sender, System.EventArgs e)
    {
        Debug.Log("OBS Connected - Scheduling audio sources refresh");
        isConnected = true;
        pendingRefresh = true;
    }
    
    void OnOBSDisconnected(object sender, ObsDisconnectionInfo e)
    {
        Debug.Log("OBS Disconnected - Scheduling audio sources clear");
        isConnected = false;
        pendingClear = true;
    }
    
    void RefreshAudioSources()
    {
        if (!isConnected || obsWebSocket == null) return;
        
        try
        {
            // Get all input sources
            var inputList = obsWebSocket.GetInputList();
            
            // Filter for audio sources
            var audioSources = inputList.Where(input => 
                input.InputKind.Contains("audio") || 
                input.InputKind.Contains("wasapi") ||
                input.InputKind.Contains("pulse") ||
                input.InputKind.Contains("alsa") ||
                input.InputKind.Contains("coreaudio") ||
                input.InputKind == "browser_source" // Browser sources can have audio
            ).ToList();
            
            // Clear existing UI
            ClearAudioSourcesUI();
            
            // Create UI for each audio source
            foreach (var source in audioSources)
            {
                CreateAudioSourceUI(source.InputName);
            }
            
            Debug.Log($"Created UI for {audioSources.Count} audio sources");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to refresh audio sources: {ex.Message}");
        }
    }
    
    void CreateAudioSourceUI(string sourceName)
    {
        if (audioSourcePrefab == null || audioSourcesParent == null)
        {
            Debug.LogError("Audio source prefab or parent not assigned!");
            return;
        }
        
        GameObject sourceUI = Instantiate(audioSourcePrefab, audioSourcesParent);
        AudioSourceUI audioUI = new AudioSourceUI(sourceUI, sourceName);
        
        if (audioUI.volumeSlider != null)
        {
            audioUI.volumeSlider.minValue = 0f;
            audioUI.volumeSlider.maxValue = 100f;
            audioUI.volumeSlider.wholeNumbers = true;
            audioUI.volumeSlider.onValueChanged.AddListener((value) => SetSourceVolume(sourceName, value));
            
            // Get current volume
            try
            {
                var volumeInfo = obsWebSocket.GetInputVolume(sourceName);
                
                // Try different possible property name patterns using reflection
                float volumeDb = -100f;
                float volumeMul = 0f;
                
                var type = volumeInfo.GetType();
                
                // Try to find the dB property with different naming patterns
                var dbProp = type.GetProperty("inputVolumeDb") ?? 
                            type.GetProperty("InputVolumeDb") ?? 
                            type.GetProperty("VolumeDb");
                            
                // Try to find the multiplier property with different naming patterns
                var mulProp = type.GetProperty("inputVolumeMul") ?? 
                             type.GetProperty("InputVolumeMul") ?? 
                             type.GetProperty("VolumeMul");
                
                if (dbProp != null) 
                {
                    var dbValue = dbProp.GetValue(volumeInfo);
                    volumeDb = System.Convert.ToSingle(dbValue);
                }
                
                if (mulProp != null) 
                {
                    var mulValue = mulProp.GetValue(volumeInfo);
                    volumeMul = System.Convert.ToSingle(mulValue);
                }
                
                audioUI.volumeSlider.SetValueWithoutNotify(volumeDb > -100 ? 
                    Mathf.RoundToInt(volumeMul * 100f) : 0);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get initial volume for {sourceName}: {e.Message}");
            }
        }
        
        if (audioUI.muteToggle != null)
        {
            audioUI.muteToggle.onValueChanged.AddListener((muted) => SetSourceMute(sourceName, muted));
            
            // Get current mute state
            try
            {
                var isMuted = obsWebSocket.GetInputMute(sourceName);
                audioUI.muteToggle.SetIsOnWithoutNotify(isMuted);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Could not get initial mute state for {sourceName}: {e.Message}");
            }
        }
        
        audioSourceUIs[sourceName] = audioUI;
    }
    
    void ClearAudioSourcesUI()
    {
        foreach (var audioUI in audioSourceUIs.Values)
        {
            if (audioUI.gameObject != null)
                DestroyImmediate(audioUI.gameObject);
        }
        audioSourceUIs.Clear();
    }
    
    void SetSourceVolume(string sourceName, float volume)
    {
        if (!isConnected || obsWebSocket == null) return;
        
        try
        {
            float volumeMultiplier = volume / 100f;
            obsWebSocket.SetInputVolume(sourceName, volumeMultiplier);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to set volume for {sourceName}: {e.Message}");
        }
    }
    
    void SetSourceMute(string sourceName, bool muted)
    {
        if (!isConnected || obsWebSocket == null) return;
        
        try
        {
            obsWebSocket.SetInputMute(sourceName, muted);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to set mute for {sourceName}: {e.Message}");
        }
    }
    
    void OnInputVolumeChanged(object sender, InputVolumeChangedEventArgs e)
    {
        // First, try to find the input name property using reflection
        string inputName = null;
        float volumeDb = -100f;
        float volumeMul = 0f;
        
        try
        {
            var type = e.GetType();
            var nameProp = type.GetProperty("InputName") ?? 
                          type.GetProperty("inputName") ?? 
                          type.GetProperty("Name") ?? 
                          type.GetProperty("name");
            
            if (nameProp != null)
            {
                inputName = nameProp.GetValue(e)?.ToString();
            }
            
            if (!string.IsNullOrEmpty(inputName))
            {
                // Try to find the dB property with different naming patterns
                var dbProp = type.GetProperty("inputVolumeDb") ?? 
                            type.GetProperty("InputVolumeDb") ?? 
                            type.GetProperty("VolumeDb");
                            
                // Try to find the multiplier property with different naming patterns
                var mulProp = type.GetProperty("inputVolumeMul") ?? 
                             type.GetProperty("InputVolumeMul") ?? 
                             type.GetProperty("VolumeMul");
                
                if (dbProp != null) 
                {
                    var dbValue = dbProp.GetValue(e);
                    volumeDb = System.Convert.ToSingle(dbValue);
                }
                
                if (mulProp != null) 
                {
                    var mulValue = mulProp.GetValue(e);
                    volumeMul = System.Convert.ToSingle(mulValue);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error processing volume event: {ex.Message}");
            return;
        }
        
        if (string.IsNullOrEmpty(inputName))
            return;
            
        // Schedule UI update on main thread
        string capturedInputName = inputName;
        float capturedVolumeDb = volumeDb;
        float capturedVolumeMul = volumeMul;
        
        // Use a simple approach to execute on main thread next frame
        StartCoroutine(UpdateVolumeUI(capturedInputName, capturedVolumeDb, capturedVolumeMul));
    }
    
    private System.Collections.IEnumerator UpdateVolumeUI(string inputName, float volumeDb, float volumeMul)
    {
        //yield return null; // Wait one frame to ensure we're on main thread
        
        if (audioSourceUIs.ContainsKey(inputName))
        {
            var audioUI = audioSourceUIs[inputName];
            if (audioUI.volumeSlider != null)
            {
                float volumePercent = volumeDb > -100 ? 
                    Mathf.RoundToInt(volumeMul * 100f) : 0;
                audioUI.volumeSlider.SetValueWithoutNotify(volumePercent);
            }
        }

        yield return new WaitForSeconds(updateInterval);
    }
    
    void OnInputMuteStateChanged(object sender, InputMuteStateChangedEventArgs e)
    {
        // First, try to find the input name property using reflection
        string inputName = null;
        bool isMuted = false;
        
        try
        {
            var type = e.GetType();
            var nameProp = type.GetProperty("InputName") ?? 
                          type.GetProperty("inputName") ?? 
                          type.GetProperty("Name") ?? 
                          type.GetProperty("name");
            
            if (nameProp != null)
            {
                inputName = nameProp.GetValue(e)?.ToString();
            }
            
            if (!string.IsNullOrEmpty(inputName))
            {
                var mutedProp = type.GetProperty("InputMuted") ?? 
                               type.GetProperty("inputMuted") ?? 
                               type.GetProperty("Muted") ?? 
                               type.GetProperty("muted");
                
                if (mutedProp != null)
                {
                    var mutedValue = mutedProp.GetValue(e);
                    isMuted = System.Convert.ToBoolean(mutedValue);
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Error processing mute event: {ex.Message}");
            return;
        }
        
        if (string.IsNullOrEmpty(inputName))
            return;
            
        // Schedule UI update on main thread
        string capturedInputName = inputName;
        bool capturedMuted = isMuted;
        
        StartCoroutine(UpdateMuteUI(capturedInputName, capturedMuted));
    }
    
    private System.Collections.IEnumerator UpdateMuteUI(string inputName, bool isMuted)
    {
        if (audioSourceUIs.ContainsKey(inputName))
        {
            var audioUI = audioSourceUIs[inputName];
            if (audioUI.muteToggle != null)
            {
                audioUI.muteToggle.SetIsOnWithoutNotify(isMuted);
            }
        }
        
        yield return new WaitForSeconds(updateInterval);
    }
    
    // Public method to manually refresh audio sources
    public void ManualRefresh()
    {
        RefreshAudioSources();
    }
    
    void OnDestroy()
    {
        if (obsWebSocket != null)
        {
            obsWebSocket.Connected -= OnOBSConnected;
            obsWebSocket.Disconnected -= OnOBSDisconnected;
            obsWebSocket.InputVolumeChanged -= OnInputVolumeChanged;
            obsWebSocket.InputMuteStateChanged -= OnInputMuteStateChanged;
        }
    }
    
    void OnValidate()
    {
        // Create default prefab structure if not assigned
        if (audioSourcePrefab == null)
        {
            Debug.LogWarning("Audio Source Prefab not assigned. Create a prefab with Slider and Toggle components.");
        }
    }
}