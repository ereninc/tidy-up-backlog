using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    public enum GameInputDevice
    {
        KeyboardMouse,
        Gamepad
    }

    /// <summary>
    /// The single local gameplay-input source for one NGO PlayerObject.
    /// Remote player instances never construct or enable Input Actions.
    /// Continuous values can be polled; discrete inputs are also exposed as
    /// local C# events. This component sends no network packets.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Input/Network Player Input Reader")]
    public sealed class NetworkPlayerInputReader : NetworkBehaviour
    {
        public static NetworkPlayerInputReader Local { get; private set; }

        /// <summary>
        /// Raised when the local owner reader appears or disappears.
        /// The argument is null when it disappears.
        /// </summary>
        public static event Action<NetworkPlayerInputReader>
            LocalReaderChanged;

        public event Action<GameInputDevice> DeviceChanged;
        public event Action InteractPressed;
        public event Action DropPressed;
        public event Action ShelfPickupPressed;
        public event Action JumpPressed;
        public event Action PausePressed;
        public event Action<int> SkillPressed;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public GameInputDevice ActiveDevice { get; private set; } =
            GameInputDevice.KeyboardMouse;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public bool GameplayEnabled => _gameplayEnabled;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public bool ActionsReady => _actions != null;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public Vector2 Move => CanReadGameplayInput
            ? _actions.Gameplay.Move.ReadValue<Vector2>()
            : Vector2.zero;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public Vector2 Look => CanReadGameplayInput
            ? _actions.Gameplay.Look.ReadValue<Vector2>()
            : Vector2.zero;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public bool SprintHeld =>
            CanReadGameplayInput &&
            _actions.Gameplay.Sprint.IsPressed();

        public bool IsGamepad =>
            ActiveDevice == GameInputDevice.Gamepad;

        public bool CanReadGameplayInput =>
            IsSpawned &&
            IsOwner &&
            _actions != null &&
            _actions.Gameplay.enabled &&
            _gameplayEnabled &&
            !GameplayInputGate.IsBlocked;

        public bool JumpPressedThisFrame =>
            CanReadGameplayInput &&
            _actions.Gameplay.Jump.WasPressedThisFrame();

        public bool InteractPressedThisFrame =>
            CanReadGameplayInput &&
            _actions.Gameplay.Interact.WasPressedThisFrame();

        public bool DropPressedThisFrame =>
            CanReadGameplayInput &&
            _actions.Gameplay.Drop.WasPressedThisFrame();

        public bool ShelfPickupPressedThisFrame =>
            CanReadGameplayInput &&
            _actions.Gameplay.ShelfPickup.WasPressedThisFrame();

        public bool PausePressedThisFrame =>
            CanReadOwnerInput &&
            _gameplayEnabled &&
            _actions.Gameplay.Pause.WasPressedThisFrame();

        private PlayerInputActions _actions;
        private bool _gameplayEnabled = true;
        private bool _callbacksSubscribed;

        private bool CanReadOwnerInput =>
            IsSpawned &&
            IsOwner &&
            _actions != null &&
            _actions.Gameplay.enabled;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            LocalReaderChanged = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (!IsOwner)
            {
                return;
            }

            InitializeOwnerInput();
        }

        public override void OnNetworkDespawn()
        {
            ShutdownOwnerInput();
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            ShutdownOwnerInput();
        }

        /// <summary>
        /// Presence/gameplay flow can disable consumption without destroying
        /// the action collection. Pause remains separate from the global
        /// GameplayInputGate so it can later close an already-open menu.
        /// </summary>
        public void SetGameplayEnabled(bool enabled)
        {
            _gameplayEnabled = enabled;
        }

        private void InitializeOwnerInput()
        {
            if (_actions != null)
            {
                return;
            }

            if (Local != null && Local != this)
            {
                Debug.LogWarning(
                    "Replacing an existing local NetworkPlayerInputReader. " +
                    "The previous owner PlayerObject should despawn shortly.",
                    this);
            }

            _actions = new PlayerInputActions();
            SubscribeCallbacks();
            _actions.Gameplay.Enable();

            Local = this;

            GameInputDevice initialDevice =
                Gamepad.current != null &&
                Keyboard.current == null &&
                Mouse.current == null
                    ? GameInputDevice.Gamepad
                    : GameInputDevice.KeyboardMouse;

            SetActiveDevice(initialDevice, true);
            LocalReaderChanged?.Invoke(this);
        }

        private void ShutdownOwnerInput()
        {
            if (_actions != null)
            {
                UnsubscribeCallbacks();
                _actions.Gameplay.Disable();
                _actions.Dispose();
                _actions = null;
            }

            if (Local == this)
            {
                Local = null;
                LocalReaderChanged?.Invoke(null);
            }
        }

        private void SubscribeCallbacks()
        {
            if (_actions == null || _callbacksSubscribed)
            {
                return;
            }

            InputActionMap gameplay = _actions.Gameplay.Get();

            for (int i = 0; i < gameplay.actions.Count; i++)
            {
                gameplay.actions[i].performed += HandleAnyActionPerformed;
            }

            _actions.Gameplay.Interact.performed += HandleInteractPerformed;
            _actions.Gameplay.Drop.performed += HandleDropPerformed;
            _actions.Gameplay.ShelfPickup.performed += HandleShelfPickupPerformed;
            _actions.Gameplay.Jump.performed += HandleJumpPerformed;
            _actions.Gameplay.Pause.performed += HandlePausePerformed;
            _actions.Gameplay.Skill1.performed += HandleSkill1Performed;
            _actions.Gameplay.Skill2.performed += HandleSkill2Performed;
            _actions.Gameplay.Skill3.performed += HandleSkill3Performed;

            _callbacksSubscribed = true;
        }

        private void UnsubscribeCallbacks()
        {
            if (_actions == null || !_callbacksSubscribed)
            {
                return;
            }

            InputActionMap gameplay = _actions.Gameplay.Get();

            for (int i = 0; i < gameplay.actions.Count; i++)
            {
                gameplay.actions[i].performed -= HandleAnyActionPerformed;
            }

            _actions.Gameplay.Interact.performed -= HandleInteractPerformed;
            _actions.Gameplay.Drop.performed -= HandleDropPerformed;
            _actions.Gameplay.ShelfPickup.performed -= HandleShelfPickupPerformed;
            _actions.Gameplay.Jump.performed -= HandleJumpPerformed;
            _actions.Gameplay.Pause.performed -= HandlePausePerformed;
            _actions.Gameplay.Skill1.performed -= HandleSkill1Performed;
            _actions.Gameplay.Skill2.performed -= HandleSkill2Performed;
            _actions.Gameplay.Skill3.performed -= HandleSkill3Performed;

            _callbacksSubscribed = false;
        }

        private void HandleAnyActionPerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);
        }

        private void HandleInteractPerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);

            if (CanReadGameplayInput)
            {
                InteractPressed?.Invoke();
            }
        }

        private void HandleDropPerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);

            if (CanReadGameplayInput)
            {
                DropPressed?.Invoke();
            }
        }

        private void HandleJumpPerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);

            if (CanReadGameplayInput)
            {
                JumpPressed?.Invoke();
            }
        }

        private void HandleShelfPickupPerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);

            if (CanReadGameplayInput)
            {
                ShelfPickupPressed?.Invoke();
            }
        }

        private void HandlePausePerformed(
            InputAction.CallbackContext context)
        {
            UpdateActiveDevice(context);

            if (CanReadOwnerInput && _gameplayEnabled)
            {
                PausePressed?.Invoke();
            }
        }

        private void HandleSkill1Performed(
            InputAction.CallbackContext context)
        {
            HandleSkillPerformed(context, 1);
        }

        private void HandleSkill2Performed(
            InputAction.CallbackContext context)
        {
            HandleSkillPerformed(context, 2);
        }

        private void HandleSkill3Performed(
            InputAction.CallbackContext context)
        {
            HandleSkillPerformed(context, 3);
        }

        private void HandleSkillPerformed(
            InputAction.CallbackContext context,
            int oneBasedSkillIndex)
        {
            UpdateActiveDevice(context);

            if (CanReadGameplayInput)
            {
                SkillPressed?.Invoke(oneBasedSkillIndex);
            }
        }

        private void UpdateActiveDevice(
            InputAction.CallbackContext context)
        {
            InputDevice device = context.control?.device;

            if (device == null || IsVirtualMouse(device))
            {
                return;
            }

            if (device is Gamepad)
            {
                SetActiveDevice(GameInputDevice.Gamepad, false);
            }
            else if (device is Keyboard || device is Mouse)
            {
                SetActiveDevice(GameInputDevice.KeyboardMouse, false);
            }
        }

        private void SetActiveDevice(
            GameInputDevice device,
            bool forceNotify)
        {
            if (!forceNotify && ActiveDevice == device)
            {
                return;
            }

            ActiveDevice = device;

            Debug.Log(
                $"[PlayerInput] Active device: {ActiveDevice}",
                this);

            DeviceChanged?.Invoke(ActiveDevice);
        }

        private static bool IsVirtualMouse(InputDevice device)
        {
            if (!(device is Mouse))
            {
                return false;
            }

            return ContainsVirtual(device.layout) ||
                   ContainsVirtual(device.name) ||
                   ContainsVirtual(device.displayName);
        }

        private static bool ContainsVirtual(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(
                       "Virtual",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
