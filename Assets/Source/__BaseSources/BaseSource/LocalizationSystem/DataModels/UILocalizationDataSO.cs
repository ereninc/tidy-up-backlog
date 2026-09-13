using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "SO_Localization/UILocalization")]
public class UILocalizationDataSO : LocalizationDataSO<UILocalizationDataModel>
{
    [Button]
    public override void Sync()
    {
        ReadGoogleSheets.FillData<UILocalizationDataModel>(sheetId, gridId, list =>
        {
            dataModels = list;
            ReadGoogleSheets.SetDirty(this);
        });
    }

    [Button]
    public string GetSpecificIDText(int id)
    {
        var data = dataModels.Find(x => x.id == id);
        if (data == null) return string.Empty;
        if (localeTextMap.TryGetValue(LanguageController.Instance.CurrentLocale, out var getText))
        {
            return getText(data);
        }

        return string.Empty;
    }

    //OnEnable works on SO???
    private void OnEnable()
    {
        localeTextMap = new Dictionary<Locales, Func<UILocalizationDataModel, string>>()
        {
            { Locales.EN, data => data.Translation_EN },
            { Locales.TR, data => data.Translation_TR },
            { Locales.DE, data => data.Translation_G },
            { Locales.SC, data => data.Translation_SC },
            { Locales.TC, data => data.Translation_TC },
            { Locales.J, data => data.Translation_J },
            { Locales.K, data => data.Translation_K },
            { Locales.S, data => data.Translation_S },
            { Locales.F, data => data.Translation_F },
            { Locales.P, data => data.Translation_P },
            { Locales.R, data => data.Translation_R },
            { Locales.PL, data => data.Translation_PL }
        };
    }
}

[System.Serializable]
public class UILocalizationDataModel
{
    public int id;
    public string Translation_EN;
    public string Translation_TR;
    public string Translation_G;
    public string Translation_SC;
    public string Translation_TC;
    public string Translation_J;
    public string Translation_K;
    public string Translation_S;
    public string Translation_F;
    public string Translation_P;
    public string Translation_R;
    public string Translation_PL;
}