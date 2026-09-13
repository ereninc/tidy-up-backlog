using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CurrencyEffectModel : TransformObject
{
    public void OnSpawn(Vector3 pos, Vector3 scale) 
    {
        SetActive();
        Transform.position = pos;
        Transform.localScale = scale;
    }

    public void OnDeactive() 
    {
        SetDeactive();
        Transform.position = Vector3.zero;
        Transform.localScale = Vector3.one;
        Transform.rotation = Quaternion.identity;
    }
}