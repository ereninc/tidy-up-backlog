using Sirenix.OdinInspector;
using UnityEngine;

public class ParticleFactory : Singleton<ParticleFactory>
{
    [Button]
    public void SpawnParticle(PoolEnum pool, Vector3 pos, Quaternion rot)
    {
        SpawnObject<ParticleModel>(pool, pos, rot);
    }

    private T SpawnObject<T>(PoolEnum poolEnum, Vector3 position, Quaternion rotation)
    {
        ParticleModel particle = PoolFactory.Instance.GetDeactiveItem<ParticleModel>(poolEnum);
        particle.SetPositionAndRotation(position, rotation);
        return (T)((object)particle);
    }
}