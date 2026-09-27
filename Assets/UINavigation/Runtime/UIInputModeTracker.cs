using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace EXW.UI.Navigation
{
    public enum UIInputMode
    {
        Unknown,
        Pointer,
        KeyboardNavigation,
        GamepadNavigation
    }

    public enum UIInputIntent
    {
        Move,
        Submit,
        ExternalDevice,
        Pointer
    }

    /// <summary>Reads the UI module's actions to detect the most recently used device.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EXW/UI Navigation/Input Mode Tracker")]
    public sealed class UIInputModeTracker : MonoBehaviour
    {
        private InputSystemUIInputModule _module;
        private UINavigationSettingsSO _settings;
        private InputAction _move;
        private InputAction _submit;
        private InputAction _point;
        private InputAction _click;
        private Vector2 _lastPointerPosition;
        private bool _hasPointerPosition;
        private bool _bound;

        public UIInputMode CurrentMode { get; private set; } = UIInputMode.Unknown;
        public event Action<UIInputMode, UIInputIntent> InputUsed;

        public void Configure(InputSystemUIInputModule module, UINavigationSettingsSO settings)
        {
            Unbind();
            _module = module;
            _settings = settings;
            if (isActiveAndEnabled) Bind();
        }

        public void ReportGameplayDevice(InputDevice device)
        {
            if (isActiveAndEnabled) Report(device, UIInputIntent.ExternalDevice);
        }

        private void OnEnable() => Bind();
        private void OnDisable() => Unbind();
        private void OnDestroy() => Unbind();

        private void Bind()
        {
            if (_bound || !_module || !_settings) return;
            _move = _module.move != null ? _module.move.action : null;
            _submit = _module.submit != null ? _module.submit.action : null;
            _point = _module.point != null ? _module.point.action : null;
            _click = _module.leftClick != null ? _module.leftClick.action : null;
            if (_move != null) _move.performed += OnMove;
            if (_submit != null) _submit.performed += OnSubmit;
            if (_point != null) _point.performed += OnPoint;
            if (_click != null) _click.performed += OnClick;
            _hasPointerPosition = false;
            _bound = true;
        }

        private void Unbind()
        {
            if (_move != null) _move.performed -= OnMove;
            if (_submit != null) _submit.performed -= OnSubmit;
            if (_point != null) _point.performed -= OnPoint;
            if (_click != null) _click.performed -= OnClick;
            _move = _submit = _point = _click = null;
            _bound = false;
        }

        private void OnMove(InputAction.CallbackContext context)
        {
            if (context.ReadValue<Vector2>().sqrMagnitude >= 0.01f)
                Report(context.control.device, UIInputIntent.Move);
        }

        private void OnSubmit(InputAction.CallbackContext context) =>
            Report(context.control.device, UIInputIntent.Submit);

        private void OnClick(InputAction.CallbackContext context) =>
            Report(context.control.device, UIInputIntent.Pointer);

        private void OnPoint(InputAction.CallbackContext context)
        {
            if (!(context.control.device is Mouse)) return;
            Vector2 position = context.ReadValue<Vector2>();
            if (!_hasPointerPosition)
            {
                _lastPointerPosition = position;
                _hasPointerPosition = true;
                return;
            }
            float threshold = _settings.PointerMoveThresholdPixels;
            if ((position - _lastPointerPosition).sqrMagnitude < threshold * threshold) return;
            _lastPointerPosition = position;
            Report(context.control.device, UIInputIntent.Pointer);
        }

        private void Report(InputDevice device, UIInputIntent intent)
        {
            UIInputMode next;
            if (device is Gamepad || device is Joystick) next = UIInputMode.GamepadNavigation;
            else if (device is Keyboard) next = UIInputMode.KeyboardNavigation;
            else if (device is Mouse || device is Pen || device is Touchscreen) next = UIInputMode.Pointer;
            else return;

            CurrentMode = next;
            InputUsed?.Invoke(next, intent);
        }
    }
}
