using System;
using System.Collections;
using System.Collections.Generic;
using Klak.Ndi;
using UnityEngine;

public class NdiFrameToMesh : MonoBehaviour
{
    public NdiReceiver _receiver;
    private Texture sourceTexture;
    private bool isRunning;

    public void Run()
    {
        isRunning = true;
    }

    public void UnRun()
    {
        isRunning = false;
    }
    private void Awake()
    {
        isRunning = false;
    }

    public void Update()
    {
        if (isRunning)
        {
            sourceTexture = _receiver.GetTexture();

            if (!sourceTexture)
            {
                return;
            }
        
            gameObject.GetComponent<MeshRenderer>().material.mainTexture = sourceTexture;
        }
    }
}
