using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO_Localization/LanguageLocalization")]
public class LanguageLocalizationDataSO : ScriptableObject
{
    public LanguageLocalizationDataModel[] languageLocalizationDataModels;

    public string GetSpecificLanguageText(Locales locale)
    {
        return languageLocalizationDataModels.Find(x => x.Locale == locale).Language;
    }
}

[System.Serializable]
public class LanguageLocalizationDataModel
{
    public Locales Locale;
    public string Language;
}