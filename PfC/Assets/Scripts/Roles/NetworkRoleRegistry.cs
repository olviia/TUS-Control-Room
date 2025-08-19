using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public struct VivoxUserRole : INetworkSerializable, System.IEquatable<VivoxUserRole>
{
    public FixedString64Bytes  playerId;
    public Role role;
    
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref playerId);
        serializer.SerializeValue(ref role);
    }
    
    public bool Equals(VivoxUserRole other) => playerId.Equals(other.playerId);
}
public class NetworkRoleRegistry : NetworkBehaviour
{
    private NetworkList<VivoxUserRole> userRoles;
    
    public static NetworkRoleRegistry Instance { get; private set; }
    void Start()
    {
        Debug.Log("started vivox network role registry");
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // Initialize the NetworkList
        userRoles = new NetworkList<VivoxUserRole>();
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void RegisterRoleServerRpc(Role role, string playerId)
    {
        // First, remove any existing role for this player
        RemovePlayerFromList(playerId);
        
        // Then add the new role
        var newUserRole = new VivoxUserRole 
        { 
            playerId = playerId, 
            role = role 
        };
        
        userRoles.Add(newUserRole); // Add to NetworkList
        Debug.Log($"Registered {playerId} as {role}");
       
    }

    [ServerRpc(RequireOwnership = false)]
    public void UnregisterRoleServerRpc(string playerId)
    {
        RemovePlayerFromList(playerId);
    }
    
    // Helper method to remove player from list
    private void RemovePlayerFromList(string playerId)
    {
        // Loop backwards to safely remove while iterating
        for (int i = userRoles.Count - 1; i >= 0; i--)
        {
            if (userRoles[i].playerId == playerId)
            {
                userRoles.RemoveAt(i);
                break; // Only one entry per player should exist
            }
        }
    }
    
    public List<FixedString64Bytes> GetPresentersIDList(Role targetRole)
    {
        var result  =  new List<FixedString64Bytes>();
        foreach (var userRole in userRoles)
        {
            if (userRole.role == targetRole)
            {
                result.Add(userRole.playerId);
            }
        }
        return result;
         
    }

}
