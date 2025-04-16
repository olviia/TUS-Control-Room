using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RoleSystem
{
    public Role role;
}

public enum Role
{
    Director,
    Journalist,
    Guest,
    Audience
}

//implement system to assign required channels per role
//implement system to assign required layer masks per role
//implement system to assign required interaction masks per role