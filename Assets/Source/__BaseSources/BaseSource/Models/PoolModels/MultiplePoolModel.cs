using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class MultiplePoolModel : ObjectModel
{
    [SerializeField] List<PoolModel> pools;

    public virtual T GetPool<T>(int poolIndex)
    {
        return pools[poolIndex].GetDeactiveItem<T>();
    }

    [Button]
    public void GetPools()
    {
        if (pools != null)
            pools.Clear();
        else
            pools = new List<PoolModel>();

        for (int i = 0; i < transform.childCount; i++)
        {
            PoolModel pool = transform.GetChild(i).GetComponent<PoolModel>();
            if (pool != null)
            {
                pools.Add(pool);
            }
        }
    }

    private void Reset()
    {
        transform.ResetLocal();
    }
}
