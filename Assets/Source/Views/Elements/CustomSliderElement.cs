using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class CustomSliderElement : UIElement
{
	[Header("UI Elements")]
	[SerializeField] private TextMeshProUGUI valueText;
	[SerializeField] private Slider mainSlider;
	public Slider Slider => mainSlider;

	[Header("Settings")]
	[SerializeField] private float minValue = 0f;
	[SerializeField] private float maxValue = 1f;
	[SerializeField] private bool useXHundred = true; // 0.75 yerine 75 gösterimi
	[SerializeField] private bool showWholeNumbers = false; // Ondalıklı mı, tam sayı mı?
	[SerializeField] private bool useAnimation = true; // DOTween animasyonu olsun mu?

	public UnityEvent<float> OnValueChanged = new UnityEvent<float>();

	private void Start()
	{
		mainSlider.minValue = minValue;
		mainSlider.maxValue = maxValue;
		mainSlider.onValueChanged.AddListener(HandleSliderValueChanged);
		SetValue(mainSlider.value);
	}

	private void HandleSliderValueChanged(float value)
	{
		UpdateUI(value);
		OnValueChanged.Invoke(value);
	}

	private void UpdateUI(float value)
	{
		float displayValue = useXHundred ? value * 100f : value;
		if (showWholeNumbers) displayValue = Mathf.Round(displayValue);

		valueText.text = displayValue.ToString(showWholeNumbers ? "0" : "F1").Replace(",", ".");

		if (useAnimation)
		{
			valueText.transform.DOKill();
			valueText.transform.localScale = Vector3.one;
			valueText.transform.DOPunchScale(Vector3.one * 0.1f, 0.2f);
		}
	}

	public void SetValue(float value, bool notify = true)
	{
		value = Mathf.Clamp(value, minValue, maxValue);

		if (notify)
		{
			mainSlider.value = value;
			HandleSliderValueChanged(value);
		}
		else
		{
			mainSlider.SetValueWithoutNotify(value);
			UpdateUI(value);
		}
	}

	public void SetMinMax(float min, float max)
	{
		minValue = min;
		maxValue = max;
		mainSlider.minValue = min;
		mainSlider.maxValue = max;
	}
}