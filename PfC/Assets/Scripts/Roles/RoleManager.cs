/* Filename: RoleManager.cs
 * Creator: Deniz Mevlevioglu
 * Date: 16/04/2025
 */

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoleManager : MonoBehaviour
{
    public static RoleManager Instance { get; private set; }
    void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    

    /// <summary>
    /// Script for initialising XR setup prefabs
    /// based on the role details from Role class
    /// </summary>
    public Role currentRole;
    private List<RoleDetails> roleList;

    public void CreateRole()
    {
        //set the variables here for the RoleDetails in order
        //to spawn an XR setup with the correct permissions

        //not called anywhere at the moment, will depend on roles and necessity

        var newRole = new RoleDetails()
        {
            role = Role.Director,
            layerMasks = new string[] { "Director", "Studio" },
            interactionMasks = new string[] { "Director", "Studio" },
            commChannels = new string[] { "Director" }
        };

        roleList.Add(newRole);
    }
}
