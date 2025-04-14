using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstantiateScreen : MonoBehaviour
{
    public GameObject screen;

    public void SpawnScreen()
    {
        var position = this.transform.position;
        var newScreen = Instantiate(screen, position, Quaternion.identity);
        newScreen.transform.SetParent(this.transform);
    }
}
