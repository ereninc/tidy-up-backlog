using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO_Localization/_Library")]
public class LocalizationLibraryDataSO : ScriptableObject
{
	public UILocalizationDataSO UILocalizationData;
	public LanguageLocalizationDataSO LanguageLocalizationData;
	

	[Button(ButtonSizes.Large), GUIColor(0.15f, 0.75f, 0.75f)]
	public void SyncAll()
	{
		UILocalizationData.Sync();
		// PlaceableLocalizationData.Sync();
		// PhotoModeFilterLocalizationData.Sync();
		// WeatherLocalizationData.Sync();
		// GameModeLocalizationData.Sync();
		// SandboxSizePropertyLocalizationData.Sync();
		// MapThemeLocalizationData.Sync();
		// SandboxPropertyEnvironmentLocalizationData.Sync();
		// SandboxPropertyResourceLocalizationData.Sync();
		// DisasterLocalizationData.Sync();
		// PackNameLocalizationData.Sync();
		// ResourceLocalizationData.Sync();
	}
}