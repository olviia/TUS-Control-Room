using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ClickForwarder : MonoBehaviour
{
    void OnMouseDown()
    {
        GetComponentInParent<SourceObject>()?.OnSourceClicked();
    }
}
