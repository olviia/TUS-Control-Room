using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class NetworkSceneManager : NetworkBehaviour
{
    [SerializeField] private string hostScene;
    [SerializeField] private string clientScene;

    public override void OnNetworkSpawn()
    {
        if (IsClient)
        {
            var status = NetworkManager.SceneManager.LoadScene(clientScene, 
                                                               UnityEngine.SceneManagement.LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"Failed to load {clientScene} " +
                      $"with a {nameof(SceneEventProgressStatus)}: {status}");
            }
        } 
        else
        {
            var status = NetworkManager.SceneManager.LoadScene(hostScene,
                                                   UnityEngine.SceneManagement.LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"Failed to load {hostScene} " +
                      $"with a {nameof(SceneEventProgressStatus)}: {status}");
            }
        }
    }
}
