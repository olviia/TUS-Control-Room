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
using UnityEditor;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Utilities;
using UnityEngine.XR.Interaction.Toolkit.Utilities.Curves;

using UnityEngine.SceneManagement;

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
    
    //force register prefab
    public GameObject textureNetworkSynchronizerPrefab;

    private void Awake()
    {
        //force adding prefab
        //networkManager.AddNetworkPrefab(textureNetworkSynchronizerPrefab);
    }
    //add events for client so the scene is loaded in all possible scenarios

    //wait for the network to connect
    public override void OnNetworkSpawn()
    {
        string hostScene = "ControlRoom";
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
            leftRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().raycastMask |= (1 << layerMask);
            rightRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().raycastMask |= (1 << layerMask);
        } 
        else
        {
            Debug.Log("Loading client Scene");
            var layerMask = LayerMask.NameToLayer("Studio");
            Camera.main.cullingMask |= (1 << layerMask);
            leftRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().raycastMask |= (1 << layerMask);
            rightRayInteractor.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>().raycastMask |= (1 << layerMask);
        }
        
        //load the scene after assigning the layer masks
        networkManager.SceneManager.LoadScene(hostScene,
            UnityEngine.SceneManagement.LoadSceneMode.Single);
        
    }
}
