using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Klak.Ndi;
using UnityEngine;

public class ObsGetSceneByName : MonoBehaviour
{
    private NdiReceiver receiver;
    private bool isFound;

    [SerializeField] private string sourceName;
    // Start is called before the first frame update
    void Start()
    {
     
        receiver = GetComponent<NdiReceiver>();   
        
    }

    private void Update()
    {
        if (!isFound)
        {
            if(NdiFinder.EnumerateSourceNames() == null)
            //it is an array just in case there will be more with that name. 
                receiver.ndiName = GetSource()[0];
        }
    }

    public string[] GetSource()
    {
        var allSources = NdiFinder.EnumerateSourceNames();
        return allSources.Where(source => 
            source.ToLower().Contains(sourceName.ToLower())).ToArray();
    }
}
