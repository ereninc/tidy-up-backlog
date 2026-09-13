using UnityEngine;
public class AudioSettingSaveController :  MonoBehaviour
{
    // protected override PrefType GetPrefType() => PrefType.AudioSetting;
    public static new AudioSettingSaveController Instance => Singleton<AudioSettingSaveController>.Instance;

    public void SetAudioSettings(float master, float gameplay, float music, float ui)
    {    
        // Data.Master = master;
        // Data.Gameplay = gameplay;
        // Data.Music = music;
        // Data.UI = ui;
        // Save();
    }
}

[System.Serializable]
public class AudioSettingData
{
    public float Master = 0.5f;
    public float Gameplay = 1f;
    public float Music = 1f;
    public float UI = 1f;
} 