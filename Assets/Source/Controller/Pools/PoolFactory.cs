using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

public class PoolFactory : Singleton<PoolFactory>
{
    [Header("SETTINGS")] 
    [SerializeField] private bool isDynamic; //I DID NOT IMPLEMENTED YET
    
    [Space(12)]
    [Header("POOLS")]
    public Pools[] pools;

    public T GetDeactiveItem<T>(PoolEnum poolEnum)
    {
        var pool = SelectPool(poolEnum);
        var pooledObject = pool.GetDeactiveItem<T>();
        return pooledObject;
    }

    private PoolModel SelectPool(PoolEnum pool)
    {
        var selectedPool = pools.FirstOrDefault(x => x.poolEnum == pool);
        return selectedPool?.poolModel;
    }
}

[System.Serializable]
public class Pools
{
    public PoolEnum poolEnum;
    public PoolModel poolModel;
}

public enum PoolEnum
{
    Product,
    Rope,
    SmokeParticle,
    StarParticle
}