using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Teleport : MonoBehaviour
{
   public GameObject what;
   public GameObject where;
   
   public void ToLocation()
   {
      what.transform.position = where.transform.position;
      what.transform.rotation = where.transform.rotation;
   }
}
