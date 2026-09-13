using TMPro;
using UnityEngine;

public class LocalTextModel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private byte id;

    public void SetId(byte id)
    {
        text = GetComponent<TextMeshProUGUI>();
        this.id = id;
    }

    private void OnEnable()
    {
        UpdateText();
        LanguageController.Instance.OnLanguageChanged += UpdateText;
    }

    private void OnDisable()
    {
        if (LanguageController.Instance == null) return;
        LanguageController.Instance.OnLanguageChanged -= UpdateText;
    }

    private void UpdateText()
    {
        if (text == null) return;
        text.text = LocalizationHelper.GetSpecificUIText(id).Replace("：",":").Replace("！","!").Replace("？","?").Replace("（","(").Replace("）",")").Replace("；", ";");
    }

#if UNITY_EDITOR
    private void Reset()
    {
        text = GetComponent<TextMeshProUGUI>();
    }
#endif
}