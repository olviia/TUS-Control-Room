using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RoleManager : MonoBehaviour
{
    private Role currentRole;
    private List<RoleDetails> roleList;

    public void CreateRole()
    {
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
