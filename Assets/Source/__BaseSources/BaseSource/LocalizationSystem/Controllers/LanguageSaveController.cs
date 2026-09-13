using UnityEngine;

public class LanguageSaveController : MonoBehaviour
{
    // protected override PrefType GetPrefType() => PrefType.Localization;
}

[System.Serializable]
public class LanguageData
{
    public Locales locale;
    public bool hasChanged;
}