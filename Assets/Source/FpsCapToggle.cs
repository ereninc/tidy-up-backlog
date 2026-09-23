using UnityEngine;
using UnityEngine.InputSystem;

public class FpsCapToggle : MonoBehaviour
{
	[SerializeField] private int cappedFrameRate = 144;

	private void Update()
	{
		var keyboard = Keyboard.current;
		if (keyboard == null || !keyboard.lKey.wasPressedThisFrame) return;
		if (!keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed) return;

		// VSync açıksa targetFrameRate sınırı uygulanmayabilir.
		QualitySettings.vSyncCount = 0;
		Application.targetFrameRate = Application.targetFrameRate == cappedFrameRate ? -1 : cappedFrameRate;

		Debug.Log($"FPS cap: {(Application.targetFrameRate == -1 ? "Unlimited" : $"{Application.targetFrameRate}")}");
	}
}