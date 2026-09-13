using System;
using System.Linq;
using MEC;
using UnityEngine;

public class LanguageSettingController : Singleton<LanguageSettingController>
{
	[SerializeField] private CustomPrevNextElement languageSettingElement;

	public override void Initialize()
	{
		base.Initialize();

		Timing.CallDelayed(0.1f, () =>
		{
			var index = (int)LanguageController.Instance.CurrentLocale;

			string[] languageNames = Enum.GetValues(typeof(Locales))
				.Cast<Locales>()
				.Select(language => LocalizationHelper.GetLocalizedLanguageName(language))
				.ToArray();
			languageSettingElement.Initialize(languageNames, OnChanged);

			languageSettingElement.DisableEvents();

			languageSettingElement.SetValue(index);
			languageSettingElement.Load(index);
			languageSettingElement.UpdateLocalizedUI(LocalizationHelper.GetLocalizedLanguageName(LanguageController.Instance.CurrentLocale));

			languageSettingElement.EnableEvents();
		});
	}

	private void OnChanged(int obj)
	{
		LanguageController.Instance.SetLanguage(obj);
	}

	private void UpdateSelectedTexts()
	{
		// for (int i = 0; i < textElements.Length; i++)
		// {
		// 	var text = textElements[i];
		// 	text.UpdateSelectedText();
		// }
		//
		// //PhotoMode
		// weatherElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificWeatherName(((WeatherTypes)weatherElement.CurrentValue)).ToString());
		// filterElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificPhotoFilterName(((PhotoFilter)filterElement.CurrentValue)).ToString());
		//
		// //SandboxPanel
		// sandbox_sizeElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificMapSize((MapSize)sandbox_sizeElement.CurrentValue).ToString());
		// sandbox_biomeElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificMapTheme((MapTheme)sandbox_biomeElement.CurrentValue).ToString());
		// sandbox_weatherElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificWeatherName((WeatherTypes)sandbox_weatherElement.CurrentValue).ToString());
		// sandbox_resourceElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificResourceText((ResourceSetting)sandbox_resourceElement.CurrentValue).ToString());
		// sandbox_environmentElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificEnvironmentText((EnvironmentSetting)sandbox_environmentElement.CurrentValue).ToString());
		//
		// //Other
		// other_questTypeElement.UpdateLocalizedUI(LocalizationHelper.GetSpecificUIText(other_questTypeElement.CurrentValue + 44).ToString());
		//
		// //PackSelection Panel Cards
		// packSelectionScreen.LocalizeButtons();
	}

	private void OnEnable()
	{
		LanguageController.Instance.OnLanguageChanged += UpdateSelectedTexts;
	}

	private void OnDisable()
	{
		if (LanguageController.Instance == null) return;
		LanguageController.Instance.OnLanguageChanged -= UpdateSelectedTexts;
	}
}