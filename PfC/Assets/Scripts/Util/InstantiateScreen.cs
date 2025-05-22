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
    [SerializeField] private float sizeMultiplier = 0.5f;

    public void SpawnScreen()
    {
        var position = this.transform.position;
        var rotation = this.transform.rotation;
        var scale = this.transform.localScale;
        scale.x *= sizeMultiplier;
        scale.y *= sizeMultiplier;
        scale.z *= sizeMultiplier;
        var newScreen = Instantiate(screen, position, rotation);
        newScreen.transform.localScale = scale;
        newScreen.transform.SetParent(this.transform);
    }
}
