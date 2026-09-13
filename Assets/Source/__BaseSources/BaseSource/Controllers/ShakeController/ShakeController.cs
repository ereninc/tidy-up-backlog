using System.Collections.Generic;
using System.Linq;
using Cinemachine;
using Sirenix.OdinInspector;
using UnityEngine;

public class ShakeController : Singleton<ShakeController>
{
    public List<CameraShakeObject> shakeObjects = new List<CameraShakeObject>();

    #region Shake

    [Button]
    public void CameraShake(ShakeType _shakeType)
    {
        var selectedShake = shakeObjects.FirstOrDefault(x => x.shakeType == _shakeType);
        if (selectedShake != null)
        {
            selectedShake.impulseSource.GenerateImpulse();
        }
        else
        {
            Debug.Log("Shake is null");
        }
    }

    #endregion
}

[System.Serializable]
public class CameraShakeObject
{
    public ShakeType shakeType;
    public CinemachineImpulseSource impulseSource;
}

public enum ShakeType
{
    Light,
    Medium,
    Hard,
    VeryHard,
    ObstacleCollision,
}