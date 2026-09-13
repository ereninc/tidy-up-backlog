using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "SO_SFX/_SoundEffectLibrary")]
public class SoundEffectLibrary : ScriptableObject
{
	public AudioMixer AudioMixer;
	
	[Space(4)]
	[Header("Library")]
	public SoundEffect[] soundEffects;
}