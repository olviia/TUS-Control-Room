/* Filename: CommunicationManager.cs
 * Creator: Deniz Mevlevioglu
 * Date: 09/04/2025
 */

using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Vivox;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;


public class CommunicationManager : MonoBehaviour
{
    static object m_Lock = new object();
    static CommunicationManager m_Instance;
    private Role currentRole;
    private string currentChannelName;
    private bool isInitialized = false;
    private bool isJoiningChannel = false;

    
    private NetworkRoleRegistry roleRegistry;


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

        var options = new InitializationOptions();

        await UnityServices.InitializeAsync(options);
        await AuthenticationService.Instance.SignInAnonymouslyAsync();


        var vivoxConfig = new VivoxConfigurationOptions
        {
            DisableAudioDucking = true, // Prevents audio interference
            DynamicVoiceProcessingSwitching = true
        };

        // Initialize Vivox with configuration
        await VivoxService.Instance.InitializeAsync(vivoxConfig);
    }

    public async Task InitializeAsync(Role role)
    {
        
        if (isInitialized)
        {
            Debug.LogWarning("[CommunicationManager] Already initialized");
            return;
        }
        // Find the network role registry
        roleRegistry = FindObjectOfType<NetworkRoleRegistry>();
        if (roleRegistry == null)
        {
            Debug.LogError("[CommunicationManager] NetworkRoleRegistry not found!");
            return;
        }

        // Set role first
        currentRole = role;

        // Login to Vivox
        await VivoxService.Instance.LoginAsync();
        Debug.Log("[CommunicationManager] Vivox login successful");

        isInitialized = true;

        // Auto-join channel based on role
        await JoinRoleBasedChannel();
        // ConfigureAudioAfterChannelJoin();
    }

    public async Task JoinRoleBasedChannel()
    {
        try
        {
            // Determine channel name based on role
            string channelName = GetChannelNameForRole(currentRole);

            currentChannelName = channelName;
            var channelOptions = new ChannelOptions
            {
                MakeActiveChannelUponJoining = true
            };
            //to 1000% finish login
            await WaitForNetworkConnection();
            await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly, channelOptions);

            // After joining, register ourselves in the network registry
            await RegisterWithNetworkRegistry();
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



    private async Task WaitForNetworkConnection()
    {
        while (!NetworkManager.Singleton.IsConnectedClient && !NetworkManager.Singleton.IsHost)
        {
            await Task.Delay(100); // Short polling instead of arbitrary delay
        }

        Debug.Log("[CommunicationManager] ✅ Network connection confirmed, proceeding with Vivox");
    }

    private void ConfigureAudioAfterChannelJoin()
    {
        try
        {
            VivoxService.Instance.UnmuteInputDevice();
            VivoxService.Instance.UnmuteOutputDevice();
            VivoxService.Instance.SetInputDeviceVolume(10);
            VivoxService.Instance.SetOutputDeviceVolume(15);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Audio] Failed to configure audio: {e.Message}");
        }
    }

    #region Roles Setup
    public void SetRole(Role role)
    {
        currentRole = role;
    }
    private string GetChannelNameForRole(Role role)
    {
        // Define your channel naming logic here
        switch (role)
        {
            case Role.Director:
                return "studio-tus-channel";
            case Role.Presenter:
                return "studio-tus-channel";
            case Role.Audience:
                return "studio-tus-channel";
            default:
                Debug.LogWarning($"[CommunicationManager] Unknown role: {role}");
                return "studio-tus-channel";
        }
    }
    

    private async Task RegisterWithNetworkRegistry()
    {
        // Wait a moment for Vivox to fully establish participant info
        await Task.Delay(500);
        
        // Get our Vivox participant ID
        var loginSession = this.LoginSession;
        if (loginSession?.LoginSessionId != null)
        {
            string ourVivoxId = loginSession.LoginSessionId.Name; // This is our participant ID
            ulong ourNetcodeId = NetworkManager.Singleton.LocalClientId;
            
            // Register with the network registry
            roleRegistry.RegisterPlayerServerRpc(currentRole, ourNetcodeId, ourVivoxId, ourVivoxId);
            
            Debug.Log($"[CommunicationManager] Registered role {currentRole} with VivoxID: {ourVivoxId}");
        }
    }
    #endregion


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