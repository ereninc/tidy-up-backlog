using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EXW.UI.Navigation
{
    /// <summary>Scene-local focus owner. The game's controller owns panel visibility and animation.</summary>
    [RequireComponent(typeof(UIInputModeTracker))]
    [DisallowMultipleComponent]
    [AddComponentMenu("EXW/UI Navigation/Focus Coordinator")]
    public sealed class UIFocusCoordinator : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private EventSystem eventSystem;
        [SerializeField] private InputSystemUIInputModule uiInputModule;
        [SerializeField] private UIInputModeTracker inputModeTracker;
        [SerializeField] private UIPanelFocus initialPanel;

        [Header("Asset")]
        [SerializeField] private UINavigationSettingsSO settings;

        // Order reflects the most recently shown panel; it never controls visibility.
        private readonly List<UIPanelFocus> _panels = new List<UIPanelFocus>();
        private InputAction _backAction;
        private bool _ownsBackAction;
        private bool _configured;
        private bool _blocksGameplay;
        private bool _pendingFocus;
        private bool _pendingBack;
        private bool _backWasControl;
        private bool _restoreModuleSettings;
        private bool _originalDeselectOnBackgroundClick;
        private float _originalMoveRepeatDelay;
        private float _originalMoveRepeatRate;
        private UIPanelFocus _backPanel;
        private Selectable _preferredSelection;
        private GameObject _observedSelection;
        private GameObject _dismissedSelection;
        private int _openedFrame = -1;
        private int _transitionFrame = -1;
        private UIInputMode _lastBroadcastMode = UIInputMode.Unknown;

        public UIPanelFocus TopPanel
        {
            get
            {
                for (int i = _panels.Count - 1; i >= 0; i--)
                    if (_panels[i] && _panels[i].IsVisible) return _panels[i];
                return null;
            }
        }

        public bool HasOpenPanel => TopPanel != null;
        public bool BlocksGameplay => isActiveAndEnabled && ComputeGameplayBlock();
        public bool TransitionedThisFrame => _transitionFrame == Time.frameCount;
        public UIInputMode InputMode => inputModeTracker ? inputModeTracker.CurrentMode : UIInputMode.Unknown;
        public UIInputModeTracker DeviceTracker => inputModeTracker;
        public UINavigationSettingsSO Settings => settings;

        public event Action<bool> GameplayBlockingChanged;
        public event Action<UIInputMode> InputModeChanged;

        private void Reset() => ResolveReferences();
        private void Awake() => ResolveReferences();

        private void OnEnable()
        {
            Configure();
            RefreshGameplayBlock();
            if (TopPanel) RequestFocus(reset: true);
        }

        private void Start()
        {
            // A manually assigned initial panel is a fallback when nothing registered itself.
            if (!HasOpenPanel && initialPanel && initialPanel.isActiveAndEnabled)
                initialPanel.NotifyShown();
        }

        private void OnDisable()
        {
            TearDown();
            _pendingBack = false;
            _pendingFocus = false;
            SetGameplayBlocked(false);
        }

        private void LateUpdate()
        {
            if (!_configured) Configure();

            if (_pendingBack)
            {
                UIPanelFocus requestedPanel = _backPanel;
                bool controlOwnedCancel = _backWasControl;
                _pendingBack = false;
                _backPanel = null;
                _backWasControl = false;

                // Let the UI module process Cancel on its selected control first.
                if (requestedPanel && requestedPanel == TopPanel && Time.frameCount != _openedFrame)
                {
                    if (controlOwnedCancel)
                    {
                        requestedPanel.TryCloseExpandedDropdown();
                        TryHandleControlBack(requestedPanel);
                    }
                    else TryGoBack();
                }
            }

            UIPanelFocus top = TopPanel;
            if (!eventSystem || !top)
            {
                if (_dismissedSelection && eventSystem &&
                    eventSystem.currentSelectedGameObject == _dismissedSelection)
                    eventSystem.SetSelectedGameObject(null);
                _dismissedSelection = null;
                _pendingFocus = false;
                _preferredSelection = null;
                RefreshGameplayBlock();
                return;
            }

            if (_dismissedSelection && !top.IsReady &&
                eventSystem.currentSelectedGameObject == _dismissedSelection)
                eventSystem.SetSelectedGameObject(null);
            _dismissedSelection = null;
            if (_pendingFocus && top.IsReady)
            {
                Selectable preferred = _preferredSelection;
                _pendingFocus = false;
                _preferredSelection = null;
                if ((preferred || ShouldMaintainFocus()) &&
                    (preferred || !HasValidTopSelection())) FocusTop(preferred);
            }

            if (top.IsReady && ShouldMaintainFocus() && !HasValidTopSelection()) FocusTop();

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected != _observedSelection)
            {
                _observedSelection = selected;
                Selectable selectable = selected ? selected.GetComponent<Selectable>() : null;
                if (top.IsUsable(selectable))
                {
                    top.Remember(selectable);
                    ScrollIntoView(selectable);
                }
            }
            RefreshGameplayBlock();
        }

        /// <summary>Called by UIPanelFocus when the game's UI makes a panel visible.</summary>
        public void Register(UIPanelFocus panel)
        {
            if (!panel || !panel.IsVisible || panel.Coordinator != this) return;
            UIPanelFocus previous = TopPanel;
            if (previous == panel)
            {
                if (!HasValidTopSelection()) RequestFocus();
                return;
            }

            CaptureSelection(previous);
            _panels.Remove(panel);
            _panels.Add(panel);
            _openedFrame = Time.frameCount;
            _transitionFrame = Time.frameCount;
            RequestFocus(reset: true);
            RefreshGameplayBlock();
        }

        /// <summary>Called by UIPanelFocus on disable or an external hide signal.</summary>
        public void Unregister(UIPanelFocus panel)
        {
            if (!panel || !_panels.Contains(panel)) return;
            bool wasTop = _panels[_panels.Count - 1] == panel;
            CaptureSelection(panel);
            if (eventSystem)
            {
                GameObject selected = eventSystem.currentSelectedGameObject;
                if (selected && panel.Contains(selected.GetComponent<Selectable>()))
                    _dismissedSelection = selected;
            }

            _panels.Remove(panel);
            _transitionFrame = Time.frameCount;
            if (wasTop) RequestFocus(reset: true);
            RefreshGameplayBlock();
        }

        public bool IsTop(UIPanelFocus panel) => panel && TopPanel == panel;

        public void OnPanelReady(UIPanelFocus panel)
        {
            if (IsTop(panel)) RequestFocus();
        }

        public void OnPanelRefreshed(UIPanelFocus panel, Selectable preferred = null)
        {
            if (!IsTop(panel)) return;
            if (panel.IsUsable(preferred)) RequestFocus(preferred);
            else if (!HasValidTopSelection()) RequestFocus();
        }

        /// <summary>Optional hook for the gameplay input reader (for example, Pause).</summary>
        public void ReportGameplayDevice(InputDevice device)
        {
            if (inputModeTracker) inputModeTracker.ReportGameplayDevice(device);
        }

        /// <summary>Request Back from a button or an existing menu router.</summary>
        public bool TryGoBack()
        {
            UIPanelFocus top = TopPanel;
            if (!top || Time.frameCount == _openedFrame || !top.IsReady) return false;
            if (top.TryCloseExpandedDropdown()) return true;
            if (TryHandleControlBack(top)) return true;
            return top.RequestBack();
        }

        [ContextMenu("Validate UI Navigation Setup")]
        public void ValidateSetup()
        {
            ResolveReferences();
            if (!settings) Debug.LogError("[UI Navigation] Assign a Settings asset.", this);
            if (!eventSystem) Debug.LogError("[UI Navigation] Assign an EventSystem.", this);
            if (!uiInputModule) Debug.LogError("[UI Navigation] Assign an InputSystemUIInputModule.", this);
            if (!inputModeTracker) Debug.LogError("[UI Navigation] UIInputModeTracker is missing.", this);
            if (uiInputModule && (uiInputModule.move == null || uiInputModule.submit == null))
                Debug.LogError("[UI Navigation] UI Move and Submit actions must be assigned.", this);
            if (uiInputModule && (!uiInputModule.cancel && (!settings || !settings.BackActionOverride)))
                Debug.LogWarning("[UI Navigation] Cancel is unassigned. TryGoBack() still works.", this);
        }

        private void Configure()
        {
            if (_configured) return;
            ResolveReferences();
            if (!eventSystem || !uiInputModule || !inputModeTracker || !settings) return;

            _originalDeselectOnBackgroundClick = uiInputModule.deselectOnBackgroundClick;
            _originalMoveRepeatDelay = uiInputModule.moveRepeatDelay;
            _originalMoveRepeatRate = uiInputModule.moveRepeatRate;
            _restoreModuleSettings = true;
            uiInputModule.deselectOnBackgroundClick = !settings.PreserveSelectionOnBackgroundClick;
            uiInputModule.moveRepeatDelay = settings.MoveRepeatDelay;
            uiInputModule.moveRepeatRate = settings.MoveRepeatRate;

            inputModeTracker.Configure(uiInputModule, settings);
            inputModeTracker.InputUsed += OnInputUsed;

            InputActionReference backReference = settings.BackActionOverride
                ? settings.BackActionOverride : uiInputModule.cancel;
            _backAction = backReference ? backReference.action : null;
            if (_backAction != null)
            {
                _backAction.performed += OnBackPerformed;
                // A separate override has no UI module to enable it.
                if (settings.BackActionOverride && !_backAction.enabled)
                {
                    _backAction.Enable();
                    _ownsBackAction = true;
                }
            }
            _configured = true;
        }

        private void TearDown()
        {
            if (!_configured) return;
            if (inputModeTracker)
            {
                inputModeTracker.InputUsed -= OnInputUsed;
                inputModeTracker.Configure(null, null);
            }
            if (_backAction != null)
            {
                _backAction.performed -= OnBackPerformed;
                if (_ownsBackAction) _backAction.Disable();
            }
            if (uiInputModule && _restoreModuleSettings)
            {
                uiInputModule.deselectOnBackgroundClick = _originalDeselectOnBackgroundClick;
                uiInputModule.moveRepeatDelay = _originalMoveRepeatDelay;
                uiInputModule.moveRepeatRate = _originalMoveRepeatRate;
            }
            _backAction = null;
            _ownsBackAction = false;
            _restoreModuleSettings = false;
            _configured = false;
        }

        private void OnBackPerformed(InputAction.CallbackContext context)
        {
            if (inputModeTracker) inputModeTracker.ReportGameplayDevice(context.control.device);
            if (_pendingBack) return;
            _backPanel = TopPanel;
            _backWasControl = _backPanel &&
                              (_backPanel.HasExpandedDropdown() || IsEditingText() ||
                               SelectedHandlesNativeCancel());
            _pendingBack = true;
        }

        private void OnInputUsed(UIInputMode mode, UIInputIntent intent)
        {
            if (mode != _lastBroadcastMode)
            {
                _lastBroadcastMode = mode;
                InputModeChanged?.Invoke(mode);
            }
            UIPanelFocus top = TopPanel;
            if (!top || !eventSystem) return;

            if (mode == UIInputMode.Pointer)
            {
                if (settings.ClearSelectionOnPointerUse && !IsEditingText())
                {
                    CaptureSelection(top);
                    eventSystem.SetSelectedGameObject(null);
                    _observedSelection = null;
                    _pendingFocus = false;
                    _preferredSelection = null;
                }
                return;
            }

            if (mode == UIInputMode.KeyboardNavigation && !settings.SelectOnKeyboardNavigation) return;
            if (!top.IsReady || HasValidTopSelection()) return;

            // Resolve at frame end, after the UI module has seen this input. This keeps
            // the first direction on the default and prevents the first Submit from clicking it.
            RequestFocus();
        }

        private bool ShouldMaintainFocus()
        {
            if (!settings) return true;
            if (InputMode == UIInputMode.KeyboardNavigation && !settings.SelectOnKeyboardNavigation)
                return false;
            return !settings.ClearSelectionOnPointerUse ||
                   InputMode == UIInputMode.GamepadNavigation ||
                   InputMode == UIInputMode.KeyboardNavigation;
        }

        private bool HasValidTopSelection()
        {
            UIPanelFocus top = TopPanel;
            if (!top || !eventSystem) return false;
            // TMP creates dropdown options outside the panel transform.
            if (top.HasExpandedDropdown()) return true;
            GameObject selected = eventSystem.currentSelectedGameObject;
            return selected && top.IsUsable(selected.GetComponent<Selectable>());
        }

        private void CaptureSelection(UIPanelFocus panel)
        {
            if (!panel || !eventSystem) return;
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected) panel.Remember(selected.GetComponent<Selectable>());
        }

        private void FocusTop(Selectable preferred = null)
        {
            UIPanelFocus top = TopPanel;
            if (!eventSystem || !top || !top.IsReady) return;
            Selectable target = top.ResolveSelection(preferred);
            if (!target) return;
            if (eventSystem.currentSelectedGameObject != target.gameObject)
                eventSystem.SetSelectedGameObject(target.gameObject);
            _observedSelection = target.gameObject;
            top.Remember(target);
            ScrollIntoView(target);
        }

        private void RequestFocus(Selectable preferred = null, bool reset = false)
        {
            if (reset || !_pendingFocus || preferred) _preferredSelection = preferred;
            _pendingFocus = true;
        }

        private bool IsEditingText()
        {
            if (!eventSystem) return false;
            GameObject selected = eventSystem.currentSelectedGameObject;
            if (!selected) return false;
            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            InputField legacy = selected.GetComponent<InputField>();
            return (tmp && tmp.isFocused) || (legacy && legacy.isFocused);
        }

        private bool SelectedHandlesNativeCancel()
        {
            if (!eventSystem) return false;
            GameObject selected = eventSystem.currentSelectedGameObject;
            return selected && ExecuteEvents.CanHandleEvent<ICancelHandler>(selected);
        }

        private bool TryHandleControlBack(UIPanelFocus top)
        {
            if (!eventSystem) return false;
            GameObject selected = eventSystem.currentSelectedGameObject;
            Selectable selectable = selected ? selected.GetComponent<Selectable>() : null;
            if (!top.IsUsable(selectable)) return false;

            for (Transform parent = selected.transform; parent != null; parent = parent.parent)
            {
                foreach (MonoBehaviour behaviour in parent.GetComponents<MonoBehaviour>())
                    if (behaviour is IUIBackHandler handler && handler.TryHandleBack()) return true;
                if (parent == top.transform) break;
            }

            TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
            if (tmp && tmp.isFocused)
            {
                tmp.DeactivateInputField();
                return true;
            }
            InputField legacy = selected.GetComponent<InputField>();
            if (legacy && legacy.isFocused)
            {
                legacy.DeactivateInputField();
                return true;
            }
            return false;
        }

        private bool ComputeGameplayBlock()
        {
            foreach (UIPanelFocus panel in _panels)
                if (panel && panel.IsVisible && panel.BlocksGameplay) return true;
            return false;
        }

        private void RefreshGameplayBlock() =>
            SetGameplayBlocked(isActiveAndEnabled && ComputeGameplayBlock());

        private void SetGameplayBlocked(bool blocked)
        {
            if (_blocksGameplay == blocked) return;
            _blocksGameplay = blocked;
            GameplayBlockingChanged?.Invoke(blocked);
        }

        private void ScrollIntoView(Selectable selectable)
        {
            if (!settings || !settings.ScrollSelectedItemIntoView) return;
            ScrollRect scroll = selectable.GetComponentInParent<ScrollRect>();
            RectTransform target = selectable.transform as RectTransform;
            if (!scroll || !scroll.content || !target || !target.IsChildOf(scroll.content)) return;
            RectTransform viewport = scroll.viewport ? scroll.viewport : scroll.transform as RectTransform;
            if (!viewport) return;

            Canvas.ForceUpdateCanvases();
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
            Rect visible = viewport.rect;
            Vector2 offset = Vector2.zero;
            if (scroll.horizontal && bounds.size.x <= visible.width)
            {
                if (bounds.min.x < visible.xMin) offset.x = visible.xMin - bounds.min.x;
                else if (bounds.max.x > visible.xMax) offset.x = visible.xMax - bounds.max.x;
            }
            if (scroll.vertical && bounds.size.y <= visible.height)
            {
                if (bounds.min.y < visible.yMin) offset.y = visible.yMin - bounds.min.y;
                else if (bounds.max.y > visible.yMax) offset.y = visible.yMax - bounds.max.y;
            }
            if (offset.sqrMagnitude < 0.001f) return;
            scroll.StopMovement();
            scroll.content.anchoredPosition += offset;
        }

        private void ResolveReferences()
        {
            if (!inputModeTracker) inputModeTracker = GetComponent<UIInputModeTracker>();
            if (!eventSystem) eventSystem = GetComponentInChildren<EventSystem>(true);
            if (!uiInputModule && eventSystem)
                uiInputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        }

#if UNITY_EDITOR
        private void OnValidate() => ResolveReferences();
#endif
    }
}
