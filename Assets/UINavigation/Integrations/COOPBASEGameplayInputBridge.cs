using EXW.UI.Navigation;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Optional COOP-BASE adapter. Add only in GameplayScene. It uses the existing
    /// owner-token gate and never changes Time.timeScale or network state.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("EXW/UI Navigation/COOP-BASE Gameplay Bridge")]
    public sealed class COOPBASEGameplayInputBridge : MonoBehaviour
    {
        [SerializeField] private UIFocusCoordinator coordinator;

        private bool _ownsBlock;
        private bool _ownsCursor;
        private CursorLockMode _previousLockState;
        private bool _previousCursorVisible;

        private void OnEnable()
        {
            if (!coordinator) coordinator = GetComponent<UIFocusCoordinator>();
            if (!coordinator) return;

            coordinator.GameplayBlockingChanged += OnBlockingChanged;
            coordinator.InputModeChanged += OnInputModeChanged;
            OnBlockingChanged(coordinator.BlocksGameplay);
        }

        private void OnDisable()
        {
            if (coordinator)
            {
                coordinator.GameplayBlockingChanged -= OnBlockingChanged;
                coordinator.InputModeChanged -= OnInputModeChanged;
            }
            ReleaseBlockAndCursor();
        }

        private void OnBlockingChanged(bool blocked)
        {
            if (blocked)
            {
                if (!_ownsBlock)
                {
                    GameplayInputGate.SetBlocked(this, true);
                    _ownsBlock = true;
                }

                if (coordinator && coordinator.Settings &&
                    coordinator.Settings.ReleaseCursorForGameplayUI)
                {
                    if (!_ownsCursor)
                    {
                        _previousLockState = Cursor.lockState;
                        _previousCursorVisible = Cursor.visible;
                        _ownsCursor = true;
                    }
                    UpdateCursorForUI();
                }
            }
            else
            {
                ReleaseBlockAndCursor();
            }
        }

        private void OnInputModeChanged(UIInputMode mode)
        {
            if (_ownsCursor) UpdateCursorForUI();
        }

        private void UpdateCursorForUI()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = coordinator.InputMode != UIInputMode.GamepadNavigation;
        }

        private void ReleaseBlockAndCursor()
        {
            if (_ownsBlock)
            {
                GameplayInputGate.Clear(this);
                _ownsBlock = false;
            }
            if (_ownsCursor)
            {
                Cursor.lockState = _previousLockState;
                Cursor.visible = _previousCursorVisible;
                _ownsCursor = false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!coordinator) coordinator = GetComponent<UIFocusCoordinator>();
        }
#endif
    }
}
