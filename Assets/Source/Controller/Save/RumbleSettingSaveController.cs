using UnityEngine;

public class RumbleSettingSaveController : MonoBehaviour
{
	// protected override PrefType GetPrefType() => PrefType.RumbleSetting;
}

[System.Serializable]
public class RumbleSettingData
{
	public bool isActive = true;
}