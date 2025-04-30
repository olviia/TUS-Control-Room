/* Filename: NetworkSceneManager.cs
 * Creator: Deniz Mevlevioglu
 * Date: 04/04/2025
 */

using System;
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

/// <summary>
/// Temporary solution for calling items based on 
/// whether server or client is loaded
/// Should actually be done in Role Management 
/// using prefabs
/// </summary>
/// 
public class NetworkSceneManager : NetworkBehaviour
{
    //[SerializeField] private string hostScene;
    //[SerializeField] private string clientScene;
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private GameObject leftRayInteractor;
    [SerializeField] private GameObject rightRayInteractor;

    //wait for the network to connect

    public override void OnNetworkSpawn()
    {
        string hostScene = "ControlRoom";
        Debug.Log("server: " + networkManager.IsServer + ", client: " + networkManager.IsClient );

        //separation between server/director and
        //client/journalist, audience,guest, etc
        if (networkManager.IsServer)
        {
            Debug.Log("Loading host Scene");
            var layerMask = LayerMask.NameToLayer("Director");

            //View layer of the camera, should be done in prefab instantiation
            Camera.main.cullingMask |= (1 << layerMask);

            //Selecting who can interact with what object
            //Likely director only one that will need to interact (??)
            //Keeping it here for reference if needed
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
        
        //load the scene after assigning the layer masks
        var status = NetworkManager.SceneManager.LoadScene(hostScene,
            UnityEngine.SceneManagement.LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning($"Failed to load {hostScene} " +
                             $"with a {nameof(SceneEventProgressStatus)}: {status}");
        }

    }
}
