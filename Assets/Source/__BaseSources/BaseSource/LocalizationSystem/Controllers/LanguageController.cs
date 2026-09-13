using Sirenix.OdinInspector;
// using Steamworks;
using System;
using System.Collections.Generic;

public class LanguageController : Singleton<LanguageController>
{
	public LocalizationLibraryDataSO LocalizationLibrary;
    public Locales CurrentLocale = Locales.EN;
    public Action OnLanguageChanged;
    public bool hasManuallyChanged = false;
    
    private static readonly Dictionary<string, Locales> languageLocaleMap = new Dictionary<string, Locales>
    {
        { "english", Locales.EN },
        { "turkish", Locales.TR },
        { "french", Locales.F },
        { "german", Locales.DE },
        { "spanish", Locales.S },
        { "portuguese", Locales.P },
        { "polish", Locales.PL },
        { "schinese", Locales.SC },
        { "tchinese", Locales.TC },
        { "japanese", Locales.J },
        { "koreana", Locales.K },
        { "russian", Locales.R }
    };

    public override void Initialize()
    {
        base.Initialize();

        // LanguageSaveController.Instance.Load();
        //
        // var languageData = LanguageSaveController.Instance.GetData();
        //
        // hasManuallyChanged = languageData.hasChanged;

        if (!hasManuallyChanged)
        {
            // if (SteamManager.Initialized)
            // {
            //     CurrentLocale = GetLocaleBySteamClient(SteamUtils.GetSteamUILanguage());
            // }
            // else
            // {
                CurrentLocale = Locales.EN;
            // }
        }
        else
        {
            // CurrentLocale = languageData.locale;
        }
        
        CurrentLocale = Locales.EN; //temp
        OnLanguageChanged?.Invoke();
    }

    private Locales GetLocaleBySteamClient(string gameLanguage)
    {
        return languageLocaleMap.TryGetValue(gameLanguage, out var locale) ? locale : Locales.EN;
    }

    [Button]
    public void SetLanguage(int index)
    {
        Locales targetLocale = (Locales)index;

        CurrentLocale = (Locales)index;
        OnLanguageChanged?.Invoke();

        var data = new LanguageData
        {
	        locale = targetLocale,
	        hasChanged = true
        };

        // LanguageSaveController.Instance.SetData(data);
    }

    [Button]
    public void E_CycleLanguages()
    {
        var currentLanguageIndex = (int)CurrentLocale;
        currentLanguageIndex++;
        SetLanguage(currentLanguageIndex % 12);
    }
}

public static class LocalizationHelper
{
	public static string GetSpecificUIText(int id)
	{
		return LanguageController.Instance.LocalizationLibrary.UILocalizationData.GetSpecificIDText(id);
	}
	
	public static string GetLocalizedLanguageName(Locales type)
	{
		return LanguageController.Instance.LocalizationLibrary.LanguageLocalizationData.GetSpecificLanguageText(type);
	}
}

public enum Locales
{
	EN = 0,
	TR = 1,
	DE = 2,
	SC = 3,
	TC = 4,
	J = 5,
	K = 6,
	S = 7,
	F = 8,
	P = 9,
	R = 10,
	PL = 11
}