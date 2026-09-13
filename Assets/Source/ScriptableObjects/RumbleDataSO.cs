using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RumbleDataSO", menuName = "ScriptableObjects/RumbleDataSO")]
public class RumbleDataSO : ScriptableObject
{
    public RumbleData[] rumbleData;

    [Button]
    public RumbleData GetRumbleDataByType(RumbleType type)
    {
        for (int i = 0; i < rumbleData.Length; i++)
        {
            if (rumbleData[i].rumbleType == type)
            {
                return rumbleData[i];
            }
        }
        return null;
    }
}

[System.Serializable]
public class RumbleData
{
    public RumbleType rumbleType;
    public RumbleValueData rumbleValues;
}

[System.Serializable]
public class RumbleValueData
{
    public Vector2 frequency;
    public float duration;
}

public enum RumbleType
{
    Light,
    Medium,
    Hard
}