using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO_SFX/Advanced Effect")]
public class AdvancedSoundEffect : SoundEffect
{
    public AudioClip[] clips;
    public bool loop;
    public bool neverInterrupt;
    [Space(10)]
    [Range(0f, 1f)]
    public float volume = 1f;
    [Space(10)]
    [Range(-5f, 3f)]
    public float pitch = 1f;
    public bool randomizePitch;
    public float randomizationAmount = 0.04f;
    [Space(10)]
    public bool dontRepeatClips;

    [System.NonSerialized] List<AudioClip> _clipBag;

    void OnEnable()
    {
        if (dontRepeatClips)
        {
            _clipBag = new List<AudioClip>(clips);
        }
    }
    public override void Play(AudioSource source, bool overridePitch = false, float overridingPitch = 1f)
    {
        AudioClip selectedClip;

        if (clips.Length == 1)
        {
            selectedClip = clips[0];
        }
        else if (dontRepeatClips)
        {
            selectedClip = _clipBag.GetRandom();
        }
        else
        {
            selectedClip = clips[Random.Range(0, clips.Length)];
        }


        float selectedPitch;

        if (overridePitch)
        {
            selectedPitch = overridingPitch;
        }
        else if (randomizePitch)
        {
            selectedPitch = pitch + Random.Range(-randomizationAmount, randomizationAmount);
        }
        else
        {
            selectedPitch = pitch;
        }

        source.pitch = selectedPitch;
        source.clip = selectedClip;
        source.volume = volume;

        source.Play();
    }

    public override bool RequiresDedicatedAudioSource()
    {
        return loop || neverInterrupt;
    }
}
