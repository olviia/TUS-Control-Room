using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkSceneManager : NetworkBehaviour
{
    [SerializeField] private string hostScene;
    [SerializeField] private string clientScene;
    [SerializeField] private GameObject networkObject;
    private NetworkManager networkManager;

   
    public override void OnNetworkSpawn()
    {
        networkManager = networkObject.GetComponent<NetworkManager>();

        if (networkManager.IsServer)
        {
            Debug.Log("Loading host Scene");
            var status = NetworkManager.SceneManager.LoadScene(hostScene, 
                                                               UnityEngine.SceneManagement.LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"Failed to load {hostScene} " +
                      $"with a {nameof(SceneEventProgressStatus)}: {status}");
            }
        } 
        else
        {
            Debug.Log("Loading client Scene");
            var status = NetworkManager.SceneManager.LoadScene(clientScene,
                                                   UnityEngine.SceneManagement.LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"Failed to load {clientScene} " +
                      $"with a {nameof(SceneEventProgressStatus)}: {status}");
            }
        }
    }
}
