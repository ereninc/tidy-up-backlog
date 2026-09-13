using MEC;
using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;

public class PersistentAudioSource : ControllerBaseModel
{
	public static PersistentAudioSource Instance;
	public AudioSource audioSource;
	public SoundEffectKey[] SoundtrackKeys;
	public bool StartWithSoundtrack = false;

	public override void Initialize()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
		
		if (!StartWithSoundtrack) return;

		if (audioSource != null && !audioSource.isPlaying)
		{
			StartCoroutine(SoundtrackCoroutine(audioSource));
		}

		Timing.CallDelayed(1f, () => StartSoundtrack());
	}

	[Button]
	public void StartSoundtrack()
	{
		PlaySoundtrack(SoundEffectKey.Soundtrack_01);
	}

	private SoundEffectKey GetRandomSoundtrack()
	{
		return SoundtrackKeys.GetRandom();
	}

	private void PlaySoundtrack(SoundEffectKey soundtrackKey)
	{
		AudioController.Instance.PlaySoundEffect(soundtrackKey);
		StartCoroutine(SoundtrackCoroutine(audioSource));
	}

	private IEnumerator SoundtrackCoroutine(AudioSource source)
	{
		var waitForClipRemainingTime = new WaitForSeconds(source.GetClipRemainingTime());
		yield return waitForClipRemainingTime;

		PlaySoundtrack(GetRandomSoundtrack());
	}
}