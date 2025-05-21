using System;
using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class SpawnManager : NetworkBehaviour
{
   [System.Serializable]
   public class SpawnLocation
   {
       public Role role;
       public Transform spawnPoint;
   }
   
   [SerializeField] private List<SpawnLocation> spawnLocations = new List<SpawnLocation>();
   private Dictionary<Role, List<SpawnLocation>> locationsByRole;
   
   // Tracks presenter room assignments (roomIndex -> clientId)
   private NetworkList<PresenterAssignment> presenterAssignments;
   
   public struct PresenterAssignment : INetworkSerializable, IEquatable<PresenterAssignment>
   {
       public int roomIndex;
       public ulong clientId;
       
       public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
       {
           serializer.SerializeValue(ref roomIndex);
           serializer.SerializeValue(ref clientId);
       }
       
       // Implement IEquatable interface
       public bool Equals(PresenterAssignment other)
       {
           return roomIndex == other.roomIndex && clientId == other.clientId;
       }
       
       // Override object.Equals
       public override bool Equals(object obj)
       {
           return obj is PresenterAssignment other && Equals(other);
       }
       
       // Override GetHashCode
       public override int GetHashCode()
       {
           return roomIndex.GetHashCode() ^ clientId.GetHashCode();
       }
   }
   
   private void Awake()
   {
       // Initialize NetworkList before OnNetworkSpawn
       presenterAssignments = new NetworkList<PresenterAssignment>();
       
       // Group locations by role for faster access
       locationsByRole = new Dictionary<Role, List<SpawnLocation>>();
       foreach (var location in spawnLocations)
       {
           if (!locationsByRole.ContainsKey(location.role))
           {
               locationsByRole[location.role] = new List<SpawnLocation>();
           }
           locationsByRole[location.role].Add(location);
       }
   }
   
   public override void OnNetworkSpawn()
   {
       base.OnNetworkSpawn();
       
       if (IsServer)
       {
           presenterAssignments.Clear();
       }
       
       NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
   }
   
   public override void OnNetworkDespawn()
   {
       if (NetworkManager.Singleton != null)
       {
           NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
       }
   }
   
   private void OnClientDisconnect(ulong clientId)
   {
       if (IsServer)
       {
           ReleasePresenterRoom(clientId);
       }
   }
   
   // Place player at correct location based on role
   public void PlacePlayer(GameObject player, Role role)
   {
       if (role == Role.Presenter)
       {
           // Presenters need server-managed room assignment
           if (NetworkManager.Singleton.IsClient)
           {
               RequestPresenterRoomServerRpc(NetworkManager.Singleton.LocalClientId);
           }
       }
       else
       {
           // Other roles have fixed locations
           Transform spawnPoint = GetSpawnPointForRole(role);
           if (spawnPoint != null)
           {
               player.transform.position = spawnPoint.position;
               player.transform.rotation = spawnPoint.rotation;
           }
       }
   }
   
   // Simple getter for non-presenter roles
   public Transform GetSpawnPointForRole(Role role)
   {
       if (locationsByRole.TryGetValue(role, out var locations) && locations.Count > 0)
       {
           return locations[0].spawnPoint;
       }
       return transform; // Fallback
   }
   
   [ServerRpc(RequireOwnership = false)]
   private void RequestPresenterRoomServerRpc(ulong clientId)
   {
       if (!IsServer) return;
       
       // Check if client already has a room
       foreach (var assignment in presenterAssignments)
       {
           if (assignment.clientId == clientId)
           {
               AssignPresenterRoomClientRpc(clientId, assignment.roomIndex);
               return;
           }
       }
       
       // Get presenter locations
       if (!locationsByRole.TryGetValue(Role.Presenter, out var locations) || locations.Count == 0)
       {
           Debug.LogError("No presenter locations defined!");
           return;
       }
       
       // Find available room
       int assignedRoom = FindAvailablePresenterRoom(locations.Count);
       
       // Add to network list
       presenterAssignments.Add(new PresenterAssignment 
       { 
           roomIndex = assignedRoom, 
           clientId = clientId 
       });
       
       // Tell client which room they got
       AssignPresenterRoomClientRpc(clientId, assignedRoom);
   }
   
   private int FindAvailablePresenterRoom(int totalRooms)
   {
       // Find which rooms are already taken
       HashSet<int> takenRooms = new HashSet<int>();
       foreach (var assignment in presenterAssignments)
       {
           takenRooms.Add(assignment.roomIndex);
       }
       
       // Find available room
       for (int i = 0; i < totalRooms; i++)
       {
           if (!takenRooms.Contains(i))
           {
               return i; // Found an available room
           }
       }
       
       // All rooms taken, pick random one
       return Random.Range(0, totalRooms);
   }
   
   [ClientRpc]
   private void AssignPresenterRoomClientRpc(ulong clientId, int roomIndex)
   {
       // Only process for our client ID
       if (clientId != NetworkManager.Singleton.LocalClientId) return;
       
       // Get our player object
       GameObject player = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
       
       // Get the room
       if (locationsByRole.TryGetValue(Role.Presenter, out var locations) && 
           roomIndex >= 0 && roomIndex < locations.Count)
       {
           Transform spawnPoint = locations[roomIndex].spawnPoint;
           player.transform.position = spawnPoint.position;
           player.transform.rotation = spawnPoint.rotation;
       }
   }
   
   private void ReleasePresenterRoom(ulong clientId)
   {
       if (!IsServer) return;
       
       for (int i = 0; i < presenterAssignments.Count; i++)
       {
           if (presenterAssignments[i].clientId == clientId)
           {
               presenterAssignments.RemoveAt(i);
               break;
           }
       }
   }
}