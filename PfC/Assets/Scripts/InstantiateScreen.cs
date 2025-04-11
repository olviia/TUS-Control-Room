using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstantiateScreen : MonoBehaviour
{
    public GameObject screen;

    public void SpawnScreen()
    {
        var position = this.transform.position;
        Instantiate(screen, position, Quaternion.identity);
    }
}
