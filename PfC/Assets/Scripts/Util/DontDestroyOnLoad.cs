using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DontDestroyOnLoad : MonoBehaviour
{
    /// <summary>
    /// Attach to object to make it persistent 
    /// across scenes
    /// </summary>
    void Awake()
    {
        DontDestroyOnLoad(this.gameObject);
    }

}
