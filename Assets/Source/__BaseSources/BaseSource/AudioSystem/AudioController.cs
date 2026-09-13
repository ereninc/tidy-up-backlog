using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Audio;

public class AudioController : Singleton<AudioController>
{
	public SoundEffectLibrary Library;

	[Min(1), SerializeField] private int _audioSourceCount = 5;

	private AudioSource[] _audioSources;
	private int _audioSourceIndex;

	private Dictionary<int, SoundEffect> _soundEffectDictionary;

	private Dictionary<SoundEffectType, List<AudioSource>> _dedicatedAudioSources;
	[SerializeField] private DedicatedAudioSourceData[] dedicatedAudioSourceData;


	public override void Initialize()
	{
		base.Initialize();

		_audioSources = new AudioSource[_audioSourceCount];
		for (int i = 0; i < _audioSourceCount; i++)
		{
			_audioSources[i] = CreateAudioSource("Audio Source " + i, Library.AudioMixer.FindMatchingGroups("Master/Gameplay")[0]);
		}

		LoadSoundEffects();

		CreateDedicatedAudioSources();

		InitializeSettings();
	}

	private void LoadSoundEffects()
	{
		_soundEffectDictionary = new Dictionary<int, SoundEffect>();

		var soundEffects = Library.soundEffects;

		foreach (var soundEffect in soundEffects)
		{
			var key = soundEffect.soundEffectKey;
			if (key != SoundEffectKey.None)
			{
				_soundEffectDictionary[(int)key] = soundEffect;
			}
		}
	}

	private void InitializeSettings()
	{
		//PLAY SOUNDTRACK
		//if (PersistentAudioSource.Instance.audioSource != null && !PersistentAudioSource.Instance.audioSource.isPlaying)
		//{
		//    musicAudioSource = GetMusicAudioSources();
		//    PlaySoundtrack(SoundEffectKey.Soundtrack_01);
		//}
	}

	private AudioSource CreateAudioSource(string audioSourceName, AudioMixerGroup audioMixerGroup)
	{
		var audioSourceObject = new GameObject();
		audioSourceObject.AddComponent<AudioSource>();
		audioSourceObject.name = audioSourceName;
		audioSourceObject.transform.SetParent(transform);

		var audioSource = audioSourceObject.GetComponent<AudioSource>();
		audioSource.outputAudioMixerGroup = audioMixerGroup;

		return audioSource;
	}

	private void CreateDedicatedAudioSources()
	{
		_dedicatedAudioSources = new Dictionary<SoundEffectType, List<AudioSource>>();

		foreach (SoundEffectType type in Enum.GetValues(typeof(SoundEffectType)))
		{
			if (type == SoundEffectType.Music && PersistentAudioSource.Instance.audioSource != null)
			{
				var sources = new List<AudioSource>
				{
					PersistentAudioSource.Instance.audioSource
				};
				_dedicatedAudioSources.Add(type, sources);
				continue;
			}

			List<AudioSource> audioSources = new List<AudioSource>();

			for (int i = 0; i < GetNumberOfAudioSourcesForType(type); i++)
			{
				var source = CreateAudioSource("Dedicated Audio Source for " + type.ToString() + " Sound FX", GetAudioMixerGroupForType(type));
				audioSources.Add(source);

				if (type == SoundEffectType.Music)
				{
					source.transform.SetParent(PersistentAudioSource.Instance.transform);
					PersistentAudioSource.Instance.audioSource = source;
				}
			}

			_dedicatedAudioSources.Add(type, audioSources);
		}
	}

	private int GetNumberOfAudioSourcesForType(SoundEffectType type)
	{
		for (int i = 0; i < dedicatedAudioSourceData.Length; i++)
		{
			if (type == dedicatedAudioSourceData[i].SoundEffectType)
			{
				return dedicatedAudioSourceData[i].AudioSourceCount;
			}
		}

		return 3;
	}

	private AudioMixerGroup GetAudioMixerGroupForType(SoundEffectType type)
	{
		switch (type)
		{
			case SoundEffectType.Gameplay:
				return Library.AudioMixer.FindMatchingGroups("Master/Gameplay")[0];
			case SoundEffectType.Music:
				return Library.AudioMixer.FindMatchingGroups("Master/Music")[0];
			case SoundEffectType.UserInterface:
				return Library.AudioMixer.FindMatchingGroups("Master/UserInterface")[0];
			default:
				Debug.LogWarning("Unknown SoundEffectType: " + type);
				return Library.AudioMixer.FindMatchingGroups("Master/Gameplay")[0];
		}
	}

	private AudioSource FetchDedicatedAudioSource(SoundEffectType type)
	{
		List<AudioSource> audioSources = _dedicatedAudioSources[type];
		foreach (var audioSource in audioSources)
		{
			if (!audioSource.isPlaying)
			{
				return audioSource;
			}
		}

		//IF ITS NOT AVAILABLE RETURN FIRST ONE :(
		//BUT IT SHOULD BE AVAILABLE LOL
		return audioSources[0];
	}

	private AudioSource FetchAudioSource()
	{
		var audioSource = _audioSources[_audioSourceIndex];
		_audioSourceIndex = (_audioSourceIndex + 1) % _audioSourceCount;
		return audioSource;
	}

	[Button]
	public void PlaySoundEffect(SoundEffectKey soundEffectKey)
	{
		var soundEffect = _soundEffectDictionary[(int)soundEffectKey];

		var audioSource = soundEffect.RequiresDedicatedAudioSource() ? FetchDedicatedAudioSource(soundEffect.soundEffectType) : FetchAudioSource();

		soundEffect.Play(audioSource);
	}

	public void StopSoundEffect(SoundEffectKey soundEffectKey)
	{
		var soundEffect = _soundEffectDictionary[(int)soundEffectKey];

		if (soundEffect.RequiresDedicatedAudioSource())
		{
			var audioSource = FetchDedicatedAudioSource(soundEffect.soundEffectType);
			if (audioSource != null && audioSource.isPlaying)
			{
				audioSource.Stop();
			}
		}
	}

	#region [ SUBSCRIPTION ]

	private void OnEnable()
	{
		// AudioActions.OnChoosePlaceable += PlaySoundEffect;
	}

	private void OnDisable()
	{
		// AudioActions.OnChoosePlaceable -= PlaySoundEffect;
	}

	#endregion
}

[System.Serializable]
public class DedicatedAudioSourceData
{
	public SoundEffectType SoundEffectType;
	public int AudioSourceCount;
}