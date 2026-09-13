using Sirenix.OdinInspector;
using UnityEngine.UI;
using TMPro;
using UnityEngine;

public class CustomPrevNextElement : UIElement
{
	public Button leftButton;
	public Button rightButton;
	public TextMeshProUGUI valueText;
	[ShowInInspector] public int CurrentValue => _currentValue;

	private int _currentValue;
	private string[] _values;
	private System.Action<int> _onValueChanged;
	private bool _eventsEnabled = true;

	public void Initialize(string[] values, System.Action<int> onValueChanged)
	{
		this._values = values;
		this._onValueChanged = onValueChanged;
		_currentValue = 0;
		UpdateUI();

		leftButton.onClick.AddListener(() => ChangeValue(-1));
		rightButton.onClick.AddListener(() => ChangeValue(1));
	}

	public void SetValue(int value)
	{
		_currentValue = value;

		if (_eventsEnabled)
		{
			_onValueChanged?.Invoke(_currentValue);
		}

		UpdateUI();
	}

	private void ChangeValue(int delta)
	{
		_currentValue = (_currentValue + delta + _values.Length) % _values.Length;
		UpdateUI();

		if (_eventsEnabled)
		{
			_onValueChanged?.Invoke(_currentValue);
		}
	}

	private void UpdateUI()
	{
		valueText.text = _values[_currentValue];
	}

	public void UpdateLocalizedUI(string localized)
	{
		valueText.text = localized;
	}

	public void Load(int current)
	{
		_eventsEnabled = false;

		_currentValue = current;
		UpdateUI();

		_eventsEnabled = true;
	}

	public void DisableEvents() => _eventsEnabled = false;
	public void EnableEvents() => _eventsEnabled = true;
}