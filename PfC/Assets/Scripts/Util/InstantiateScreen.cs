using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstantiateScreen : MonoBehaviour
{
    /// <summary>
    /// For director to create more previews
    /// Will spawn in front of the director
    /// And set extra screens item as parent
    /// </summary>
    public GameObject screen;

    public void SpawnScreen()
    {
        var position = this.transform.position;
        var newScreen = Instantiate(screen, position, Quaternion.identity);
        newScreen.transform.SetParent(this.transform);
    }
}
