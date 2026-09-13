using Sirenix.OdinInspector;
using UnityEngine;

public abstract class SoundEffect : ScriptableObject
{
    public SoundEffectKey soundEffectKey;
    public SoundEffectType soundEffectType;

    public abstract void Play(AudioSource source, bool overridePitch = false, float overridingPitch = 1f);
    public abstract bool RequiresDedicatedAudioSource();

    public void Play(AudioSource source, int pitchLevel)
    {
        Play(source, true, CalculatePitchForLevel(pitchLevel));
    }

    static readonly float Scale = Mathf.Pow(2f, 1.0f / 12f);

    static float CalculatePitchForLevel(int level)
    {
        return Mathf.Pow(Scale, level);
    }
}