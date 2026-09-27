using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Owner-only FPS motor. All gameplay input comes from the local
    /// NetworkPlayerInputReader, while CharacterController and NGO transform
    /// synchronization keep their existing responsibilities.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(OwnerNetworkTransform))]
    [RequireComponent(typeof(NetworkPlayerInputReader))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Network FPS Player Controller")]
    public sealed class NetworkFpsPlayerController : NetworkBehaviour
    {
        [InfoBox(
            "Only the owning client enables this CharacterController, camera and " +
            "input. Mouse and gamepad use separate look-speed settings because " +
            "mouse delta and stick input have different units.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private CharacterController characterController;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private NetworkPlayerInputReader inputReader;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private Transform cameraPitchRoot;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private Camera playerCamera;

        [TitleGroup("References")]
        [SerializeField] private AudioListener playerAudioListener;

        [TitleGroup("References")]
        [Tooltip("Renderers hidden only for the local FPS owner to prevent camera clipping.")]
        [SerializeField] private Renderer[] localHiddenRenderers;

        [TitleGroup("Movement")]
        [MinValue(0.1f)]
        [SuffixLabel("m/s")]
        [SerializeField] private float walkSpeed = 4f;

        [TitleGroup("Movement")]
        [MinValue(0.1f)]
        [SuffixLabel("m/s")]
        [SerializeField] private float sprintSpeed = 6.5f;

        [TitleGroup("Movement")]
        [MinValue(0f)]
        [SuffixLabel("m")]
        [SerializeField] private float jumpHeight = 1.15f;

        [TitleGroup("Movement")]
        [SerializeField] private float gravity = -22f;

        [TitleGroup("Look/Mouse")]
        [MinValue(0.001f)]
        [SerializeField] private float mouseSensitivity = 0.08f;

        [TitleGroup("Look/Gamepad")]
        [MinValue(1f)]
        [SuffixLabel("deg/s")]
        [SerializeField] private float gamepadLookSpeed = 180f;

        [TitleGroup("Look/Gamepad")]
        [Range(0.1f, 2f)]
        [Tooltip("Vertical stick speed relative to horizontal stick speed.")]
        [SerializeField] private float gamepadVerticalMultiplier = 0.85f;

        [TitleGroup("Look")]
        [Range(30f, 89f)]
        [SerializeField] private float verticalLookLimit = 85f;

        [TitleGroup("Look")]
        [SerializeField] private bool invertY;

        [TitleGroup("Cursor")]
        [SerializeField] private bool lockCursorOnSpawn = true;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Network Spawned")]
        private bool RuntimeSpawned => IsSpawned;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Local Owner")]
        private bool RuntimeLocalOwner => IsSpawned && IsOwner;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Grounded")]
        private bool RuntimeGrounded =>
            characterController != null &&
            characterController.enabled &&
            characterController.isGrounded;

        private float _verticalVelocity;
        private float _pitch;
        private bool _gameplayEnabled = true;

        private bool IsPlayMode => Application.isPlaying;
        public bool GameplayEnabled => _gameplayEnabled;

        private void Awake()
        {
            AutoAssignReferences();
        }

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void OnValidate()
        {
            AutoAssignReferences();

            walkSpeed = Mathf.Max(0.1f, walkSpeed);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            jumpHeight = Mathf.Max(0f, jumpHeight);
            gravity = Mathf.Min(-0.01f, gravity);
            mouseSensitivity = Mathf.Max(0.001f, mouseSensitivity);
            gamepadLookSpeed = Mathf.Max(1f, gamepadLookSpeed);
            gamepadVerticalMultiplier = Mathf.Clamp(
                gamepadVerticalMultiplier,
                0.1f,
                2f);
            verticalLookLimit = Mathf.Clamp(verticalLookLimit, 30f, 89f);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            AutoAssignReferences();
            inputReader?.SetGameplayEnabled(_gameplayEnabled);
            ConfigureOwnership(IsOwner && _gameplayEnabled);

            if (!IsOwner)
            {
                return;
            }

            _pitch = NormalizeAngle(
                cameraPitchRoot != null
                    ? cameraPitchRoot.localEulerAngles.x
                    : 0f);

            if (_gameplayEnabled &&
                !GameplayInputGate.IsBlocked &&
                lockCursorOnSpawn)
            {
                LockCursor();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                ReleaseCursor();
            }

            ConfigureOwnership(false);
            base.OnNetworkDespawn();
        }

        private void OnDisable()
        {
            if (IsOwner)
            {
                ReleaseCursor();
            }
        }

        private void Update()
        {
            if (!_gameplayEnabled || !IsSpawned || !IsOwner ||
                characterController == null ||
                !characterController.enabled ||
                inputReader == null)
            {
                return;
            }

            if (GameplayInputGate.IsBlocked)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    ReleaseCursor();
                }

                return;
            }

            UpdateCursorState();
            UpdateLook();
            UpdateMovement();
        }

        /// <summary>
        /// Used by NetworkPlayerPresenceController while the PlayerObject exists
        /// in a lobby but its camera, movement and input must remain dormant.
        /// Existing prefabs without a presence controller keep the old behaviour.
        /// </summary>
        public void SetGameplayEnabled(bool enabled)
        {
            bool ownershipAlreadyConfigured =
                !IsSpawned ||
                characterController == null ||
                characterController.enabled == (enabled && IsOwner);

            if (_gameplayEnabled == enabled && ownershipAlreadyConfigured)
            {
                inputReader?.SetGameplayEnabled(enabled);
                return;
            }

            _gameplayEnabled = enabled;
            _verticalVelocity = 0f;
            inputReader?.SetGameplayEnabled(enabled);
            ConfigureOwnership(IsSpawned && IsOwner && enabled);

            if (!IsOwner)
            {
                return;
            }

            if (!enabled)
            {
                ReleaseCursor();
            }
            else if (!GameplayInputGate.IsBlocked && lockCursorOnSpawn)
            {
                LockCursor();
            }
        }

        [Button("AUTO ASSIGN REFERENCES")]
        private void AutoAssignReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (inputReader == null)
            {
                inputReader = GetComponent<NetworkPlayerInputReader>();
            }

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (cameraPitchRoot == null && playerCamera != null)
            {
                cameraPitchRoot = playerCamera.transform.parent != null
                    ? playerCamera.transform.parent
                    : playerCamera.transform;
            }

            if (playerAudioListener == null && playerCamera != null)
            {
                playerAudioListener =
                    playerCamera.GetComponent<AudioListener>();
            }

            if (localHiddenRenderers == null || localHiddenRenderers.Length == 0)
            {
                localHiddenRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        [Button("LOCK CURSOR")]
        [HorizontalGroup("Cursor/Debug")]
        [EnableIf(nameof(IsPlayMode))]
        public void LockCursor()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        [Button("RELEASE CURSOR")]
        [HorizontalGroup("Cursor/Debug")]
        [EnableIf(nameof(IsPlayMode))]
        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ConfigureOwnership(bool isLocalOwner)
        {
            if (characterController != null)
            {
                characterController.enabled = isLocalOwner;
            }

            if (playerCamera != null)
            {
                playerCamera.enabled = isLocalOwner;
            }

            if (playerAudioListener != null)
            {
                playerAudioListener.enabled = isLocalOwner;
            }

            if (localHiddenRenderers == null)
            {
                return;
            }

            for (int i = 0; i < localHiddenRenderers.Length; i++)
            {
                Renderer target = localHiddenRenderers[i];

                if (target != null)
                {
                    target.enabled = !isLocalOwner;
                }
            }
        }

        private void UpdateCursorState()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                ReleaseCursor();
                return;
            }

            // A gamepad has no pointer click with which to reclaim gameplay.
            // When a stick/button makes it the active device, immediately hide
            // and lock the cursor while the gameplay input gate is open.
            if (inputReader != null && inputReader.IsGamepad)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    LockCursor();
                }

                return;
            }

            if (mouse != null &&
                mouse.leftButton.wasPressedThisFrame &&
                Cursor.lockState != CursorLockMode.Locked &&
                !IsPointerOverUi())
            {
                LockCursor();
            }
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null &&
                   EventSystem.current.IsPointerOverGameObject();
        }

        private void UpdateLook()
        {
            bool mouseCursorIsReleased =
                inputReader != null &&
                !inputReader.IsGamepad &&
                Cursor.lockState != CursorLockMode.Locked;

            if (cameraPitchRoot == null ||
                inputReader == null ||
                !inputReader.CanReadGameplayInput ||
                mouseCursorIsReleased)
            {
                return;
            }

            Vector2 lookInput = inputReader.Look;

            if (lookInput.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float yawDelta;
            float pitchDelta;

            if (inputReader.IsGamepad)
            {
                yawDelta =
                    lookInput.x * gamepadLookSpeed * Time.unscaledDeltaTime;
                pitchDelta =
                    lookInput.y *
                    gamepadLookSpeed *
                    gamepadVerticalMultiplier *
                    Time.unscaledDeltaTime;
            }
            else
            {
                yawDelta = lookInput.x * mouseSensitivity;
                pitchDelta = lookInput.y * mouseSensitivity;
            }

            transform.Rotate(0f, yawDelta, 0f, Space.Self);

            _pitch += invertY ? pitchDelta : -pitchDelta;
            _pitch = Mathf.Clamp(
                _pitch,
                -verticalLookLimit,
                verticalLookLimit);

            cameraPitchRoot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void UpdateMovement()
        {
            if (inputReader == null || !inputReader.CanReadGameplayInput)
            {
                return;
            }

            Vector2 moveInput = Vector2.ClampMagnitude(
                inputReader.Move,
                1f);

            bool isGrounded = characterController.isGrounded;

            if (isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            if (isGrounded &&
                jumpHeight > 0f &&
                inputReader.JumpPressedThisFrame)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            _verticalVelocity += gravity * Time.deltaTime;

            float speed = inputReader.SprintHeld
                ? sprintSpeed
                : walkSpeed;

            Vector3 planarVelocity =
                (transform.right * moveInput.x + transform.forward * moveInput.y) *
                speed;

            Vector3 velocity = new Vector3(
                planarVelocity.x,
                _verticalVelocity,
                planarVelocity.z);

            characterController.Move(velocity * Time.deltaTime);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }
    }
}
