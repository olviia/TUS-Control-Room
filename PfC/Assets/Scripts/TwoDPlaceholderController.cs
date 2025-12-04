using Klak.Ndi;
using OBSWebsocketDotNet;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Vivox;
using Unity.Services.Authentication;
using System.Linq;

public class TwoDPlaceholderController : MonoBehaviour
{
    public NdiReceiver receiver;
    public NdiReceiver receiversound;

    private string cachedNdiName = null;

    [Header("Vivox Settings")]
    [Tooltip("Auto-join Vivox as audience on start")]
    public bool autoJoinVivoxOnStart = true;

    [Tooltip("Name of the audience channel to join")]
    public string audienceChannelName = "audience-tus-channel";

    async void Start()
    {
        if (autoJoinVivoxOnStart)
        {
            await JoinVivoxAsAudienceStandalone();
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Get list of available NDI sources
        var availableSources = NdiFinder.sourceNames;

        // If current source is not in the list of available NDI sources
        if (string.IsNullOrEmpty(cachedNdiName) || !availableSources.Any(source => source.Contains(cachedNdiName)))
        {
            Debug.Log($"[TwoDPlaceholder] Searching for NDI source containing 'subscene'. Available sources: {string.Join(", ", availableSources)}");

            // Get the source name that contains "subscene"
            string foundSource = availableSources.FirstOrDefault(source => source.ToLower().Contains("subscene"));

            if (!string.IsNullOrEmpty(foundSource))
            {
                // Assign it as NDI name
                receiversound.ndiName = foundSource;
                cachedNdiName = foundSource;
                Debug.Log($"[TwoDPlaceholder] Assigned NDI source: {foundSource}");
            }
            else
            {
                Debug.LogWarning($"[TwoDPlaceholder] No NDI source containing 'subscene' found");
            }
        }
    }

    public void SetCamera1()
    {
        receiver.ndiName = "TIIMEX10 (VirtualCameraFromUnity1)";
    }

    public void SetCamera2()
    {
        receiver.ndiName = "TIIMEX10 (VirtualCameraFromUnity2)";
    }

    public void SetCamera3()
    {
        receiver.ndiName = "TIIMEX10 (VirtualCameraFromUnity3)";
    }

    public void SetTopViewCamera()
    {
        receiver.ndiName = "TIIMEX10 (VirtualCameraFromUnityTopView)";
    }

    /// <summary>
    /// Joins Vivox chat as an audience member without network registration (standalone mode)
    /// </summary>
    public async System.Threading.Tasks.Task JoinVivoxAsAudienceStandalone()
    {
        try
        {
            // Initialize Unity Services if not already initialized
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                var options = new InitializationOptions();
                await UnityServices.InitializeAsync(options);
                Debug.Log("[TwoDPlaceholder] Unity Services initialized");
            }

            // Sign in anonymously if not already signed in
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("[TwoDPlaceholder] Signed in anonymously");
            }

            // Initialize Vivox if not already initialized
            if (!VivoxService.Instance.IsLoggedIn)
            {
                var vivoxConfig = new VivoxConfigurationOptions
                {
                    DisableAudioDucking = true,
                    DynamicVoiceProcessingSwitching = true
                };

                await VivoxService.Instance.InitializeAsync(vivoxConfig);
                await VivoxService.Instance.LoginAsync();
                Debug.Log("[TwoDPlaceholder] Vivox login successful");
            }

            // Join the audience channel
            var channelOptions = new ChannelOptions
            {
                MakeActiveChannelUponJoining = true
            };

            await VivoxService.Instance.JoinGroupChannelAsync(audienceChannelName, ChatCapability.AudioOnly, channelOptions);
            Debug.Log($"[TwoDPlaceholder] Joined audience channel: {audienceChannelName}");

            // Mute the input device for listen-only mode
            VivoxService.Instance.MuteInputDevice();
            Debug.Log("[TwoDPlaceholder] Input muted - listen-only mode enabled");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[TwoDPlaceholder] Failed to join Vivox: {e.Message}");
        }
    }
}
