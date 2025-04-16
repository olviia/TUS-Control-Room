using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Utilities;
using UnityEngine.XR.Interaction.Toolkit.Utilities.Curves;
using UnityEngine.XR.Interaction.Toolkit;

public class NetworkSceneManager : NetworkBehaviour
{
    //[SerializeField] private string hostScene;
    //[SerializeField] private string clientScene;
    [SerializeField] private GameObject networkObject;
    [SerializeField] private GameObject leftRayInteractor;
    [SerializeField] private GameObject rightRayInteractor;
    private NetworkManager networkManager;

    public override void OnNetworkSpawn()
    {
        networkManager = networkObject.GetComponent<NetworkManager>();
        string hostScene = "ControlRoom";

        var status = NetworkManager.SceneManager.LoadScene(hostScene,
                                                   UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning($"Failed to load {hostScene} " +
                  $"with a {nameof(SceneEventProgressStatus)}: {status}");
        }

        //var raycastMaskLeft = leftRayInteractor.GetComponent<XRRayInteractor>();
        //var raycastMaskRight = rightRayInteractor.GetComponent<XRRayInteractor>();

        if (networkManager.IsServer)
        {
            Debug.Log("Loading host Scene");
            var layerMask = LayerMask.NameToLayer("Director");
            Camera.main.cullingMask |= (1 << layerMask);
            leftRayInteractor.GetComponent<XRRayInteractor>().raycastMask |= (1 << layerMask);
            rightRayInteractor.GetComponent<XRRayInteractor>().raycastMask |= (1 << layerMask);
        } 
        else
        {
            Debug.Log("Loading client Scene");
            var layerMask = LayerMask.NameToLayer("Studio");
            Camera.main.cullingMask |= (1 << layerMask);
            leftRayInteractor.GetComponent<XRRayInteractor>().raycastMask |= (1 << layerMask);
            rightRayInteractor.GetComponent<XRRayInteractor>().raycastMask |= (1 << layerMask);
        }

    }
}
