using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.UI.Navigation
{
	[CreateAssetMenu(menuName = "EXW/UI Navigation/Settings", fileName = "UINavigationSettings")]
	public sealed class UINavigationSettingsSO : ScriptableObject
	{
		[Header("Input")]
		[Tooltip("Optional. Otherwise the coordinator takes Cancel from its Input System UI Input Module.")]
		[SerializeField] private InputActionReference backActionOverride;
		[SerializeField] private bool selectOnKeyboardNavigation = true;
		[SerializeField] private bool clearSelectionOnPointerUse;
		[SerializeField] private bool preserveSelectionOnBackgroundClick = true;
		[SerializeField, Min(1f)] private float pointerMoveThresholdPixels = 6f;

		[Header("Navigation Repeat")]
		[SerializeField, Min(0f)] private float moveRepeatDelay = 0.4f;
		[SerializeField, Min(0.01f)] private float moveRepeatRate = 0.1f;

		[Header("Focus")]
		[SerializeField] private bool scrollSelectedItemIntoView = true;

		[Header("Gameplay Bridge")]
		[SerializeField] private bool releaseCursorForGameplayUI = true;

		public InputActionReference BackActionOverride => backActionOverride;
		public bool SelectOnKeyboardNavigation => selectOnKeyboardNavigation;
		public bool ClearSelectionOnPointerUse => clearSelectionOnPointerUse;
		public bool PreserveSelectionOnBackgroundClick => preserveSelectionOnBackgroundClick;
		public float PointerMoveThresholdPixels => pointerMoveThresholdPixels;
		public float MoveRepeatDelay => moveRepeatDelay;
		public float MoveRepeatRate => moveRepeatRate;
		public bool ScrollSelectedItemIntoView => scrollSelectedItemIntoView;
		public bool ReleaseCursorForGameplayUI => releaseCursorForGameplayUI;
	}
}