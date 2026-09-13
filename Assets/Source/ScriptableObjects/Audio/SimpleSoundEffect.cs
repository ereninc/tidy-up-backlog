using UnityEngine;

[CreateAssetMenu(menuName = "SO_SFX/Simple Effect")]
public class SimpleSoundEffect : SoundEffect
{
    public AudioClip clip;
    public bool loop;
    public bool neverInterrupt;

    [Space(10)]
    [Range(0f, 1f)]
    public float volume = 1f;

    [Space(10)]
    [Range(-5f, 5f)]
    public float pitch = 1f;
    public bool randomizePitch;
    public float randomizationAmount = .04f;

    public override void Play(AudioSource source, bool overridePitch = false, float overridingPitch = 1f)
    {
        source.clip = clip;
        source.volume = volume;
        source.loop = loop;

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

        source.Play();
    }

    public override bool RequiresDedicatedAudioSource()
    {
        return loop || neverInterrupt;
    }
}