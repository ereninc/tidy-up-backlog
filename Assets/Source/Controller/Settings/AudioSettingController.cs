using MEC;
using UnityEngine;

public class AudioSettingController : ControllerBaseModel
{
	[Header("Slider Elements")]
	public CustomSliderElement masterSlider;
	public CustomSliderElement gameplaySlider;
	public CustomSliderElement musicSlider;
	public CustomSliderElement uiSfxSlider;

	[Header("Audio Mixer Settings")]
	[SerializeField] private float logMultiplier = 40;

	private float _previousMasterVolume;
	private float _previousGameVolume;
	private float _previousMusicVolume;
	private float _previousUIVolume;

	private bool _firstTimeSetup = true;

	public override void Initialize()
	{
		base.Initialize();

		Timing.CallDelayed(0.1f, () =>
		{
			// var savedData = AudioSettingSaveController.Instance.GetData();
			
			// masterSlider.SetValue(savedData.Master, false);
			// gameplaySlider.SetValue(savedData.Gameplay, false);
			// musicSlider.SetValue(savedData.Music, false);
			// uiSfxSlider.SetValue(savedData.UI, false);
			//
			// ApplyAudioSettingsToMixer(savedData);
		});

		masterSlider.OnValueChanged.AddListener(VolumeSetMaster);
		gameplaySlider.OnValueChanged.AddListener(VolumeSetGameplay);
		musicSlider.OnValueChanged.AddListener(VolumeSetMusic);
		uiSfxSlider.OnValueChanged.AddListener(VolumeSetUserInterface);
	}

	/// <summary>
	/// Apply volume to mixer
	/// </summary>
	private void SetVolume(string parameterName, float volume, ref float previousVolume)
	{
		if (_firstTimeSetup || !Mathf.Approximately(volume, previousVolume))
		{
			_firstTimeSetup = false;

			if (volume <= 5f)
			{
				AudioController.Library.AudioMixer.SetFloat(parameterName, -80f);
			}
			else
			{
				AudioController.Library.AudioMixer.SetFloat(parameterName, Mathf.Log10(volume) * logMultiplier);
			}

			previousVolume = volume;
		}
	}

	private void VolumeSetMaster(float volume)
	{
		SetVolume("Master_Volume", volume, ref _previousMasterVolume);
		SaveAudioSettings();
	}

	private void VolumeSetGameplay(float volume)
	{
		SetVolume("Gameplay_Volume", volume, ref _previousGameVolume);
		SaveAudioSettings();
	}

	private void VolumeSetMusic(float volume)
	{
		SetVolume("Music_Volume", volume, ref _previousMusicVolume);
		SaveAudioSettings();
	}

	private void VolumeSetUserInterface(float volume)
	{
		SetVolume("UserInterface_Volume", volume, ref _previousUIVolume);
		SaveAudioSettings();
	}

	private void SaveAudioSettings()
	{
		AudioSettingSaveController.Instance.SetAudioSettings(
			masterSlider.Slider.value,
			gameplaySlider.Slider.value,
			musicSlider.Slider.value,
			uiSfxSlider.Slider.value
		);
	}

	private void ApplyAudioSettingsToMixer(AudioSettingData savedData)
	{
		_firstTimeSetup = true;

		SetVolume("Master_Volume", savedData.Master, ref _previousMasterVolume);
		SetVolume("Gameplay_Volume", savedData.Gameplay, ref _previousGameVolume);
		SetVolume("Music_Volume", savedData.Music, ref _previousMusicVolume);
		SetVolume("UserInterface_Volume", savedData.UI, ref _previousUIVolume);
	}
}