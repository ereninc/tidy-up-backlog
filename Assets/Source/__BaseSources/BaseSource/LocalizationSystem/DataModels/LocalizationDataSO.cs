using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class LocalizationDataSO<T> : ScriptableObject
{
    public string sheetId;
    public string gridId;
    public List<T> dataModels;

    public Dictionary<Locales, Func<T, string>> localeTextMap;

    public abstract void Sync();
}
