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

            // Initialize Vivox
            await VivoxService.Instance.InitializeAsync();
            Debug.Log("[CommunicationManager] Vivox initialized");

            // Login to Vivox
            await VivoxService.Instance.LoginAsync();
            Debug.Log("[CommunicationManager] Vivox login successful");

            isInitialized = true;
            
            // Auto-join channel based on role
            await JoinRoleBasedChannel();
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
            
            Debug.Log($"[CommunicationManager] Joining channel: {channelName} as {currentRole}");
            
            await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);
            
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

    public void SetRole(Role role)
    {
        currentRole = role;
    }

}