using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class ParticlePoolModel : PoolModel
{
    [Button]
    public void GetDeactiveItems()
    {
        InitializeOnEditor();
    }
}