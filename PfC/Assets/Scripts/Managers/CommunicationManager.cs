/* Filename: CommunicationManager.cs
 * Creator: Deniz Mevlevioglu
 * Date: 09/04/2025
 */
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Vivox;
using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;


public class CommunicationManager : MonoBehaviour
{
    static object m_Lock = new object();
    static CommunicationManager m_Instance;
    private Role currentRole;    
    private string currentChannelName;
    private bool isInitialized = false;
    private bool isJoiningChannel = false;



    public static CommunicationManager Instance
    {
        get
        {
            lock (m_Lock)
            {
                if (m_Instance == null)
                {
                    // Search for existing instance
                    m_Instance = (CommunicationManager)FindObjectOfType(typeof(CommunicationManager));

                    // Create new instance if one doesn't already exist
                    if (m_Instance == null)
                    {
                        var singletonObject = new GameObject();
                        m_Instance = singletonObject.AddComponent<CommunicationManager>();
                        singletonObject.name = typeof(CommunicationManager).ToString() + " (Singleton)";
                    }
                }
                DontDestroyOnLoad(m_Instance.gameObject);
                return m_Instance;
            }
        }
    }
    async void Awake()
    {
        if (m_Instance != this && m_Instance != null)
        {
            Debug.LogWarning(
                "Multiple CommunicationManager detected in the scene. Only one CommunicationManager can exist at a time. The duplicate will be destroyed.");
            Destroy(this);
            return;
        }

        // Don't auto-initialize here, wait for explicit call
        Debug.Log("[CommunicationManager] Ready for initialization");
    }

    public async Task InitializeAsync(Role role)
    {
        if (isInitialized)
        {
            Debug.LogWarning("[CommunicationManager] Already initialized");
            return ;
        }

        try
        {
            Debug.Log($"[CommunicationManager] Starting initialization for player: {role}");
            
            // Set role first
            currentRole = role;
            
            // Initialize Unity Services
            var options = new InitializationOptions();
            await UnityServices.InitializeAsync(options);
            Debug.Log("[CommunicationManager] Unity Services initialized");

            await AuthenticationService.Instance.SignInAnonymouslyAsync();

            var vivoxConfig = new VivoxConfigurationOptions
            {
                DisableAudioDucking = true,  // Prevents audio interference
                DynamicVoiceProcessingSwitching = false,  // Stable audio processing
            };

            // Initialize Vivox with configuration
            await VivoxService.Instance.InitializeAsync(vivoxConfig);
            Debug.Log("[CommunicationManager] Vivox initialized with proper config");
            
            
            // Login to Vivox
            await VivoxService.Instance.LoginAsync();
            Debug.Log("[CommunicationManager] Vivox login successful");

            isInitialized = true;
            
            // Auto-join channel based on role
            await JoinRoleBasedChannel();
            ConfigureAudioAfterChannelJoin();
            
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommunicationManager] Initialization failed: {e.Message}");
        }
        
    }
    
    private async Task JoinRoleBasedChannel()
    {
        try
        {
            // Determine channel name based on role
            string channelName = GetChannelNameForRole(currentRole);
            
            if (string.IsNullOrEmpty(channelName))
            {
                Debug.LogError("[CommunicationManager] Invalid channel name for role");
                return;
            }

            currentChannelName = channelName;
            var channelOptions = new ChannelOptions
            { 
                MakeActiveChannelUponJoining = true  
            };
 
            Debug.Log($"[CommunicationManager] Joining channel: {channelName} as {currentRole}");
            
            await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly, channelOptions);
            
            //
            // string echoChannelName = "echo_test_" + System.DateTime.Now.Ticks; // Unique echo channel
            //
            // await VivoxService.Instance.JoinEchoChannelAsync(echoChannelName, ChatCapability.AudioOnly);

            Debug.Log($"[CommunicationManager] Successfully joined channel: {channelName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommunicationManager] Failed to join channel: {e.Message}");
        }
    }
    private string GetChannelNameForRole(Role role)
    {
        // Define your channel naming logic here
        switch (role)
        {
            case Role.Director:
                return "studio-tus-channel";
            case Role.Audience:
                return "studio-tus-channel";
            default:
                Debug.LogWarning($"[CommunicationManager] Unknown role: {role}");
                return "studio-tus-channel";
        }
    }
    
    private void ConfigureAudioAfterChannelJoin()
    {
        try
        {
            VivoxService.Instance.UnmuteInputDevice();
            VivoxService.Instance.UnmuteOutputDevice();
            VivoxService.Instance.SetInputDeviceVolume(25);
            VivoxService.Instance.SetOutputDeviceVolume(40);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Audio] Failed to configure audio: {e.Message}");
        }
    }

    public void SetRole(Role role)
    {
        currentRole = role;
    }
    
    [ContextMenu("Test Audio")]
    public void TestAudio()
    {
            Debug.Log($"[Audio Status] Input Device: {VivoxService.Instance.ActiveInputDevice.DeviceName ?? "None"}");
            Debug.Log($"[Audio Status] Output Device: {VivoxService.Instance.ActiveOutputDevice?.DeviceName ?? "None"}");
            Debug.Log($"[Audio Status] Input Muted: {VivoxService.Instance.IsInputDeviceMuted}");
            Debug.Log($"[Audio Status] Output Muted: {VivoxService.Instance.IsOutputDeviceMuted}");
            Debug.Log($"[Audio Status] Input Volume: {VivoxService.Instance.InputDeviceVolume}");
            Debug.Log($"[Audio Status] Output Volume: {VivoxService.Instance.OutputDeviceVolume}");
  
    }


   

}