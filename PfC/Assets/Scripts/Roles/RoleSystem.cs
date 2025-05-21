using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RoleDetails
{
    public Role role;
    public string[] layerMasks;
    public string[] interactionMasks;
    public string[] commChannels;
}

public enum Role
{
    Director,
    Presenter,
    Audience
}
