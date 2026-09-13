using UnityEngine;
using UnityEngine.UI;

public class GameLogoLocalizationModel : MonoBehaviour
{
    [SerializeField] private Image halfHexagon;
    [SerializeField] private Image generalLogo;
    [SerializeField] private Image asianLogo;

    [SerializeField] private LocalizationLogoData[] logoData;

    private void OnEnable()
    {
        UpdateLogoSprite();
        LanguageController.Instance.OnLanguageChanged += UpdateLogoSprite;
    }

    private void OnDisable()
    {
        if (LanguageController.Instance == null) return;
        LanguageController.Instance.OnLanguageChanged -= UpdateLogoSprite;
    }

    private void UpdateLogoSprite()
    {
        var current = LanguageController.Instance.CurrentLocale;

        if (current == Locales.SC || current == Locales.TC || current == Locales.K || current == Locales.J)
        {
            generalLogo.gameObject.SetActive(false);
            halfHexagon.gameObject.SetActive(false);
            asianLogo.gameObject.SetActive(true);
            asianLogo.sprite = logoData.Find(x => x.Locales == current).Logo;
        }
        else
        {
            generalLogo.gameObject.SetActive(true);
            halfHexagon.gameObject.SetActive(true);
            asianLogo.gameObject.SetActive(false);
        }
    }
}

[System.Serializable]
public class LocalizationLogoData
{
    public Locales Locales;
    public Sprite Logo;
}