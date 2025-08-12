using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NetworkRoleRegistry : NetworkBehaviour
{
    // Network-synced dictionary of role mappings
    private NetworkList<RoleMapping> roleMappings;

    [System.Serializable]
    public struct RoleMapping : INetworkSerializable
    {
        public Role role;
        public ulong netcodeClientId;
        public FixedString128Bytes vivoxParticipantId;
        public FixedString64Bytes displayName;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref role);
            serializer.SerializeValue(ref netcodeClientId);
            serializer.SerializeValue(ref vivoxParticipantId);
            serializer.SerializeValue(ref displayName);
        }
    }
    
    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            roleMappings = new NetworkList<RoleMapping>();
        }
        
        // Subscribe to changes so all clients can react
        roleMappings.OnListChanged += OnRoleMappingsChanged;
    }
    // Called when a player joins Vivox channel and gets their participant ID
    [ServerRpc(RequireOwnership = false)]
    public void RegisterPlayerServerRpc(Role role, ulong clientId, string vivoxParticipantId, string displayName)
    {
        Debug.Log($"[NetworkRoleRegistry] Registering: {role} -> NetcodeID:{clientId}, VivoxID:{vivoxParticipantId}");
        
        // Remove any existing entry for this client
        for (int i = roleMappings.Count - 1; i >= 0; i--)
        {
            if (roleMappings[i].netcodeClientId == clientId)
            {
                roleMappings.RemoveAt(i);
            }
        }
        
        // Add new mapping
        roleMappings.Add(new RoleMapping
        {
            role = role,
            netcodeClientId = clientId,
            vivoxParticipantId = vivoxParticipantId,
            displayName = displayName
        });
    }
    
    // Get Vivox participant ID for a specific role
    public string GetVivoxIdForRole(Role targetRole)
    {
        foreach (var mapping in roleMappings)
        {
            if (mapping.role == targetRole)
            {
                return mapping.vivoxParticipantId.ToString();
            }
        }
        return null;
    }
    
    // Get all current mappings (useful for debugging)
    public Dictionary<Role, string> GetAllRoleMappings()
    {
        var result = new Dictionary<Role, string>();
        foreach (var mapping in roleMappings)
        {
            result[mapping.role] = mapping.vivoxParticipantId.ToString();
        }
        return result;
    }
    
    private void OnRoleMappingsChanged(NetworkListEvent<RoleMapping> changeEvent)
    {
        Debug.Log($"[NetworkRoleRegistry] Role mappings updated. Event: {changeEvent.Type}");
        
        // Notify CommunicationManager about role changes
        if (changeEvent.Type == NetworkListEvent<RoleMapping>.EventType.Add)
        {
            var newMapping = changeEvent.Value;
            CommunicationManager.Instance?.OnPlayerRoleRegistered(newMapping.role, newMapping.vivoxParticipantId.ToString());
        }
    }
}
