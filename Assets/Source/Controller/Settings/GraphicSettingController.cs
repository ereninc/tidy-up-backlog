using System.Linq;
using MEC;
using UnityEngine;

public class GraphicSettingController : ControllerBaseModel
{
	[Header("Element")]
	public CustomPrevNextElement resolutionSettingElement;
	public CustomPrevNextElement screenModeSettingElement; //FULL or WINDOWED or BORDERLESS
	public CustomPrevNextElement qualitySettingElement;

	private Resolution[] _resolutions;
	private readonly string[] _screenModes =
	{
		"Fullscreen", "Windowed", "Borderless"
	};
	private string[] _qualityLevels;

	public override void Initialize()
	{
		base.Initialize();

		SetResolutionSettingElement();
		SetScreenModeSettingElement();
		SetQualitySettingElement();

		Timing.CallDelayed(0.1f, LoadSettings);
	}

	#region [ INITIALIZE SETTING ELEMENTS ]

	private void SetResolutionSettingElement()
	{
		Resolution[] resolutions = Screen.resolutions
			.Select(resolution => new Resolution
			{
				width = resolution.width,
				height = resolution.height
			})
			.Distinct()
			.ToArray();

		_resolutions = resolutions;
		int currentResolutionIndex = 0;
		for (int i = 0; i < _resolutions.Length; i++)
		{
			if (_resolutions[i].width == Screen.currentResolution.width &&
				_resolutions[i].height == Screen.currentResolution.height)
			{
				currentResolutionIndex = i;
				break;
			}
		}

		string[] resolutionsTexts = resolutions
			.Select(res => $"{res.width} x {res.height}")
			.ToArray();

		resolutionSettingElement.Initialize(resolutionsTexts, OnResolutionChanged);
		
		resolutionSettingElement.DisableEvents();
		resolutionSettingElement.Load(currentResolutionIndex);
		// resolutionSettingElement.SetValue(currentResolutionIndex);
		resolutionSettingElement.EnableEvents();
	}

	private void SetScreenModeSettingElement()
	{
		screenModeSettingElement.Initialize(_screenModes, OnScreenModeChanged);
	}

	private void SetQualitySettingElement()
	{
		_qualityLevels = QualitySettings.names;
		qualitySettingElement.Initialize(_qualityLevels, OnQualityChanged);
	}

	#endregion

	private void LoadSettings()
	{
		// var savedData = GraphicSettingSaveController.Instance.GetData();

		// Debug.Log($"[Load] Loaded Data - Resolution: {savedData.ResolutionIndex}, ScreenMode: {savedData.ScreenModeIndex}, Quality: {savedData.QualityIndex}");
		// resolutionSettingElement.DisableEvents();
		// screenModeSettingElement.DisableEvents();
		// qualitySettingElement.DisableEvents();
		//
		// resolutionSettingElement.Load(savedData.ResolutionIndex);
		// screenModeSettingElement.Load(savedData.ScreenModeIndex);
		// qualitySettingElement.Load(savedData.QualityIndex);
		//
		// resolutionSettingElement.EnableEvents();
		// screenModeSettingElement.EnableEvents();
		// qualitySettingElement.EnableEvents();
		//
		// Timing.CallDelayed(0.1f, () =>
		// {
		// 	SetResolution(savedData.ResolutionIndex);
		// 	SetScreenMode(savedData.ScreenModeIndex);
		// 	SetQuality(savedData.QualityIndex);
		// });

		GraphicSettingSaveController.Instance.FinishLoading();
	}

	private void OnResolutionChanged(int index)
	{
		SetResolution(index);
		GraphicSettingSaveController.Instance.SetGraphicSettings(resolutionSettingElement.CurrentValue, screenModeSettingElement.CurrentValue, qualitySettingElement.CurrentValue);
	}

	private void OnScreenModeChanged(int index)
	{
		SetScreenMode(index);
		GraphicSettingSaveController.Instance.SetGraphicSettings(resolutionSettingElement.CurrentValue, screenModeSettingElement.CurrentValue, qualitySettingElement.CurrentValue);
	}

	private void OnQualityChanged(int index)
	{
		SetQuality(index);
		GraphicSettingSaveController.Instance.SetGraphicSettings(resolutionSettingElement.CurrentValue, screenModeSettingElement.CurrentValue, qualitySettingElement.CurrentValue);
	}

	private void SetResolution(int index)
	{
		if (index < 0 || index >= _resolutions.Length) return;

		Resolution res = _resolutions[index];
		Screen.SetResolution(res.width, res.height, Screen.fullScreenMode);
	}

	private void SetScreenMode(int index)
	{
		FullScreenMode mode = index switch
		{
			0 => FullScreenMode.FullScreenWindow,
			1 => FullScreenMode.Windowed,
			2 => FullScreenMode.ExclusiveFullScreen,
			_ => Screen.fullScreenMode
		};

		Screen.fullScreenMode = mode;
	}

	private void SetQuality(int index)
	{
		QualitySettings.SetQualityLevel(index, false);
	}
}