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

   

   private void Awake()
   {
       
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

   
   // Place player at correct location based on role
   public void PlacePlayer(GameObject player, Role role)
   
       {
           // Other roles have fixed locations
           Transform spawnPoint = GetSpawnPointForRole(role);
           if (spawnPoint != null)
           {
               player.transform.position = spawnPoint.position;
               player.transform.rotation = spawnPoint.rotation;
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
   

}