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
                "Multiple CommunicationManager detected in the scene. Only one CommunicationManager can exist at a time. The duplicate VivoxVoiceManager will be destroyed.");
            Destroy(this);
        }
        var options = new InitializationOptions();

        await UnityServices.InitializeAsync(options);
        await VivoxService.Instance.InitializeAsync();
        await VivoxService.Instance.LoginAsync();

        //write code here to set the channel name based on role

        var channelName = "channelName";

        await VivoxService.Instance.JoinGroupChannelAsync(channelName, ChatCapability.AudioOnly);

        Debug.Log(currentRole);
    }

    public async Task InitializeAsync(string playerName)
    {
        AuthenticationService.Instance.SwitchProfile(playerName);
        await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    public void SetRole(Role role)
    {
        currentRole = role;
    }

}