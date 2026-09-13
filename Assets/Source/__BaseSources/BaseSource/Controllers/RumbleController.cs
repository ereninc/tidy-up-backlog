using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;

public class RumbleController : Singleton<RumbleController>
{
	[SerializeField] private RumbleDataSO rumbleData;
	[SerializeField] private bool isRumbleActive; // SET FROM SAVE FILE -> TOGGLE SWITCH
	private Coroutine _coroutine;
	public Action<RumbleType> OnRumbleFeedback;

	[SerializeField] private CustomSwitchElement rumbleSwitchElement;

	public override void Initialize()
	{
		base.Initialize();
		// isRumbleActive = RumbleSettingSaveController.Instance.GetData().isActive;
		isRumbleActive = true;
		
		rumbleSwitchElement.Initialize(isRumbleActive);
	}

	[Button]
	public void RumbleFeedback(RumbleType rumbleType)
	{
		// if (InputController.Instance.activeGameDevice == InputController.GameDevice.KeyboardMouse) return;
		if (!isRumbleActive) return;

		if (_coroutine == null) _coroutine = StartCoroutine(StartRumbling(rumbleType));
	}

	private IEnumerator StartRumbling(RumbleType rumbleType)
	{
		var data = rumbleData.GetRumbleDataByType(rumbleType);
		Gamepad.current.SetMotorSpeeds(data.rumbleValues.frequency.x, data.rumbleValues.frequency.y);
		yield return new WaitForSeconds(data.rumbleValues.duration);
		InputSystem.ResetHaptics();
		_coroutine = null;
	}

	#region [ Subscriptions ]

	public void Invoke_OnRumbleFeedback(RumbleType rumbleType)
	{
		OnRumbleFeedback?.Invoke(rumbleType);
	}

	private void OnEnable()
	{
		OnRumbleFeedback += RumbleFeedback;
	}

	private void OnDisable()
	{
		OnRumbleFeedback -= RumbleFeedback;
	}

	#endregion

	public void ToggleRumble(bool isOn)
	{
		isRumbleActive = isOn;

		if (isRumbleActive)
		{
			Invoke_OnRumbleFeedback(RumbleType.Light);
		}

		var data = new RumbleSettingData
		{
			isActive = isRumbleActive
		};

		// RumbleSettingSaveController.Instance.SetData(data);
	}
}