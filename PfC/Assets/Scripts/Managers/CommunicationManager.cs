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
using Klak.Ndi;
using Klak.Ndi.Audio;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Vivox.AudioTaps;


public class CommunicationManager : MonoBehaviour
{
    static object m_Lock = new object();
    static CommunicationManager m_Instance;
    private Role currentRole;
    private string directorChannelName = "studio-tus-channel";
    private string audienceChannelName = "audience-tus-channel";
    private bool isInitialized = false;
    private bool isJoiningChannel = false;
    public GameObject vivoxAudioToNdi;

    [Header("Audience Control")]
    [Tooltip("When enabled, all audience members will be muted")]
    public bool muteAudience = false;

    [Header("Audio Bridge Settings")]
    [Tooltip("ID to be assigned to AudioListenerIndividualBridge components")]
    public int audioBridgeId = 0;

    private NetworkRoleRegistry roleRegistry;
    private List<string> joinedChannels = new List<string>();


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
            //to 1000% finish login
            await WaitForNetworkConnection();

            Debug.Log($"[Vivox] Current User id : {AuthenticationService.Instance.PlayerId}");

            // Join channels based on role
            switch (currentRole)
            {
                case Role.Director:
                    // Director joins only the director channel
                    await JoinChannel(directorChannelName, true);
                    break;

                case Role.Presenter:
                    // Presenter joins BOTH director and audience channels
                    await JoinChannel(directorChannelName, true);  // Make director channel active
                    await JoinChannel(audienceChannelName, false); // Audience channel non-active
                    VivoxService.Instance.ParticipantAddedToChannel += SetupPresenterAudioTaps;
                    break;

                case Role.Audience:
                    // Audience joins only the audience channel
                    await JoinChannel(audienceChannelName, true);
                    // Apply mute if the setting is enabled
                    if (muteAudience)
                    {
                        VivoxService.Instance.MuteInputDevice();
                        Debug.Log("[Vivox] Audience member muted on join");
                    }
                    break;

                default:
                    Debug.LogWarning($"[CommunicationManager] Unknown role: {currentRole}");
                    break;
            }

            Debug.Log($"[CommunicationManager] Successfully joined channels for role: {currentRole}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[CommunicationManager] Failed to join channel: {e.Message}");
        }
    }

    private async Task JoinChannel(string channelName, bool makeActive)
    {
        var channelOptions = new ChannelOptions
        {
            MakeActiveChannelUponJoining = makeActive
        };

        await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly, channelOptions);
        joinedChannels.Add(channelName);
        Debug.Log($"[CommunicationManager] Joined channel: {channelName} (Active: {makeActive})");
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

    public string GetDirectorChannelName()
    {
        return directorChannelName;
    }

    public string GetAudienceChannelName()
    {
        return audienceChannelName;
    }

    #endregion

    public void SetupPresenterAudioTaps(VivoxParticipant vivoxParticipant)
    {
        // Configure to capture only presenter's audio from the director channel
        var presentersID = NetworkRoleRegistry.Instance.GetPresentersIDList(Role.Presenter);
        List<VivoxParticipantTap> vivoxTaps =  new List<VivoxParticipantTap>();
        foreach (var id in presentersID)
        {
                GameObject tapObject = new GameObject("PresenterAudioTap");
                tapObject.transform.SetParent(vivoxAudioToNdi.transform);
                tapObject.AddComponent<AudioSource>();
                AudioListenerIndividualBridge audioBridge = tapObject.AddComponent<AudioListenerIndividualBridge>();
                audioBridge.SetBridgeId(audioBridgeId);
                VivoxParticipantTap presenterTap = tapObject.AddComponent<VivoxParticipantTap>();

                presenterTap.ParticipantName = id.ToString();
                presenterTap.ChannelName = directorChannelName; // Capture from studio-tus-channel
                vivoxTaps.Add(presenterTap);
        }

        Debug.Log("[Vivox] PresenterAudioTaps setup completed");
        Debug.Log($"[Vivox] Presenters ID : {string.Join(", ", presentersID)}");

        Debug.Log($"[Vivox] VivoxTaps ({vivoxTaps.Count}):");
        foreach (var tap in vivoxTaps)
        {
            Debug.Log($"  - {tap.ParticipantName} on GameObject: {tap.gameObject.name}");
        }
        // Route to streaming (virtual audio cable, etc.)
        //SetupStreamingPipeline(presenterTap.GetComponent<AudioSource>());
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

    #region Audience Muting

    /// <summary>
    /// Apply the mute setting to audience members. Call this when muteAudience boolean changes.
    /// </summary>
    [ContextMenu("Apply Audience Mute Setting")]
    public void ApplyAudienceMuteSetting()
    {
        if (currentRole == Role.Audience)
        {
            if (muteAudience)
            {
                VivoxService.Instance.MuteInputDevice();
                Debug.Log("[Vivox] Audience muted");
            }
            else
            {
                VivoxService.Instance.UnmuteInputDevice();
                Debug.Log("[Vivox] Audience unmuted");
            }
        }
        else
        {
            Debug.LogWarning("[Vivox] ApplyAudienceMuteSetting called but current role is not Audience");
        }
    }

    /// <summary>
    /// Toggle the audience mute setting and apply it immediately.
    /// </summary>
    [ContextMenu("Toggle Audience Mute")]
    public void ToggleAudienceMute()
    {
        muteAudience = !muteAudience;
        ApplyAudienceMuteSetting();
    }

    /// <summary>
    /// Set the audience mute state.
    /// </summary>
    /// <param name="shouldMute">True to mute audience, false to unmute</param>
    public void SetAudienceMute(bool shouldMute)
    {
        muteAudience = shouldMute;
        ApplyAudienceMuteSetting();
    }

    #endregion
}