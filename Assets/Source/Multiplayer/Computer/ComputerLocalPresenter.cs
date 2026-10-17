using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    public enum ComputerLocalUseState
    {
        Idle,
        Entering,
        Using,
        Exiting
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Computer/Computer Local Presenter")]
    public sealed class ComputerLocalPresenter : MonoBehaviour
    {
        [TitleGroup("References"), Required]
        [SerializeField] private NetworkStoreComputer computer;
        [TitleGroup("References"), Required]
        [SerializeField] private StoreComputerDesktopPresenter desktop;
        [TitleGroup("References"), Required]
        [Tooltip("Place in front of the display, looking towards it. Never use the network player root.")]
        [SerializeField] private Transform screenViewAnchor;
        [TitleGroup("References")]
        [Tooltip("Gameplay HUD groups only. Do not assign the desktop, pause menu or an ancestor of this component.")]
        [SerializeField] private CanvasGroup[] hudGroups = Array.Empty<CanvasGroup>();

        [TitleGroup("Transition"), MinValue(0.05f)]
        [SerializeField] private float enterDuration = 0.65f;
        [TitleGroup("Transition"), MinValue(0.05f)]
        [SerializeField] private float exitDuration = 0.5f;
        [TitleGroup("Transition"), MinValue(0f)]
        [SerializeField] private float fullscreenBlendDuration = 0.18f;
        [TitleGroup("Transition")]
        [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [TitleGroup("Transition"), Range(10f, 90f)]
        [SerializeField] private float screenFieldOfView = 45f;
        [TitleGroup("Transition"), Range(0.005f, 0.1f)]
        [SerializeField] private float screenNearClip = 0.01f;
        [TitleGroup("Presentation")]
        [SerializeField] private bool hideLocalHeldItems = true;

        public static ComputerLocalPresenter Active { get; private set; }
        [ShowInInspector, ReadOnly, BoxGroup("Local State")]
        public ComputerLocalUseState State { get; private set; }
        public event Action<ComputerLocalUseState> StateChanged;

        private NetworkPlayerInputReader _inputReader;
        private NetworkPlayerPresenceController _presence;
        private MultiplayerSessionCoordinator _session;
        private NetworkItemCarrier _carrier;
        private Camera _camera;
        private Coroutine _transition;
        private uint _sessionToken;
        private Vector3 _savedLocalPosition;
        private Quaternion _savedLocalRotation;
        private float _savedFieldOfView;
        private float _savedNearClip;
        private CursorLockMode _savedCursorLock;
        private bool _savedCursorVisible;
        private bool _ownsInput;
        private bool _fullscreenStarted;
        private bool _releaseRequested;
        private float _presentationBlend;
        private readonly List<Renderer> _rendererBuffer = new List<Renderer>();
        private readonly List<RendererState> _hiddenRenderers = new List<RendererState>();
        private readonly List<HudState> _hiddenHud = new List<HudState>();
        private readonly List<DebugHudState> _hiddenDebugHud = new List<DebugHudState>();

        private readonly struct RendererState
        {
            public readonly Renderer Renderer;
            public readonly bool ForceRenderingOff;
            public RendererState(Renderer renderer)
            {
                Renderer = renderer;
                ForceRenderingOff = renderer.forceRenderingOff;
            }
        }

        private readonly struct HudState
        {
            public readonly CanvasGroup Group;
            public readonly float Alpha;
            public readonly bool Interactable;
            public readonly bool BlocksRaycasts;
            public HudState(CanvasGroup group)
            {
                Group = group;
                Alpha = group.alpha;
                Interactable = group.interactable;
                BlocksRaycasts = group.blocksRaycasts;
            }
        }

        private readonly struct DebugHudState
        {
            public readonly MonoBehaviour Component;
            public readonly bool Enabled;
            public DebugHudState(MonoBehaviour component)
            {
                Component = component;
                Enabled = component.enabled;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;

        private void Reset()
        {
            computer = GetComponent<NetworkStoreComputer>();
            desktop = GetComponent<StoreComputerDesktopPresenter>();
        }

        private void OnEnable()
        {
            if (computer == null) computer = GetComponent<NetworkStoreComputer>();
            if (desktop == null) desktop = GetComponent<StoreComputerDesktopPresenter>();
            if (computer != null)
            {
                computer.LocalUseAccepted += HandleLocalUseAccepted;
                computer.LocalUseEnded += HandleLocalUseEnded;
            }
            if (desktop != null) desktop.LeaveRequested += RequestLeaveComputer;
            NetworkPlayerInputReader.LocalReaderChanged += HandleLocalReaderChanged;
            if (computer != null && computer.IsLocalUser)
                HandleLocalUseAccepted(computer.SessionToken);
        }

        private void OnDisable()
        {
            if (computer != null)
            {
                computer.LocalUseAccepted -= HandleLocalUseAccepted;
                computer.LocalUseEnded -= HandleLocalUseEnded;
            }
            if (desktop != null) desktop.LeaveRequested -= RequestLeaveComputer;
            NetworkPlayerInputReader.LocalReaderChanged -= HandleLocalReaderChanged;
            RequestRelease();
            RestoreLocalState();
        }

        private void HandleLocalUseAccepted(uint token)
        {
            if (!isActiveAndEnabled || computer == null || !computer.IsLocalUser ||
                token != computer.SessionToken) return;
            if (State != ComputerLocalUseState.Idle)
            {
                if (_sessionToken != token) computer.RequestRelease(token);
                return;
            }

            NetworkPlayerInputReader reader = NetworkPlayerInputReader.Local;
            Camera camera = reader != null ? reader.GetComponentInChildren<Camera>(true) : null;
            if (desktop == null || !desktop.isActiveAndEnabled || screenViewAnchor == null || reader == null ||
                !reader.IsSpawned || !reader.IsOwner || !reader.GameplayEnabled ||
                camera == null || !camera.isActiveAndEnabled ||
                (Active != null && Active != this) || GameplayInputGate.IsBlocked)
            {
                Debug.LogWarning("[Computer] Local camera/UI is unavailable; releasing the accepted session.", this);
                computer.RequestRelease(token);
                return;
            }

            Active = this;
            _inputReader = reader;
            _camera = camera;
            _sessionToken = token;
            _releaseRequested = false;
            _savedLocalPosition = camera.transform.localPosition;
            _savedLocalRotation = camera.transform.localRotation;
            _savedFieldOfView = camera.fieldOfView;
            _savedNearClip = camera.nearClipPlane;
            _savedCursorLock = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;

            // The FPS controller and all gameplay readers stop before their camera/body writes.
            // Keep their ownership/presence components enabled throughout the local transition.
            _ownsInput = true;
            GameplayInputGate.SetBlocked(this, true);
            reader.SetPauseSuppressed(this, true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = !reader.IsGamepad;
            camera.nearClipPlane = Mathf.Min(_savedNearClip, screenNearClip);
            BindActiveLifecycle();
            HideHud();
            RefreshHeldVisuals();
            if (!desktop.BeginLocalUse(reader))
            {
                RequestRelease();
                RestoreLocalState();
                return;
            }
            desktop.SetLocalInputEnabled(false);
            SetState(ComputerLocalUseState.Entering);
            if (State == ComputerLocalUseState.Entering)
                _transition = StartCoroutine(Enter());
        }

        private IEnumerator Enter()
        {
            Vector3 startPosition = _camera.transform.position;
            Quaternion startRotation = _camera.transform.rotation;
            float elapsed = 0f;
            while (elapsed < enterDuration)
            {
                if (_camera == null || screenViewAnchor == null)
                {
                    RequestRelease();
                    _transition = null;
                    RestoreLocalState();
                    yield break;
                }
                elapsed += Time.unscaledDeltaTime;
                float t = Ease(elapsed / enterDuration);
                _camera.transform.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, screenViewAnchor.position, t),
                    Quaternion.SlerpUnclamped(startRotation, screenViewAnchor.rotation, t));
                _camera.fieldOfView = Mathf.LerpUnclamped(_savedFieldOfView, screenFieldOfView, t);
                yield return null;
            }
            desktop.BeginFullscreenTransition(_camera);
            _fullscreenStarted = true;
            yield return BlendDesktop(1f);
            desktop.SetLocalInputEnabled(true);
            _transition = null;
            SetState(ComputerLocalUseState.Using);
        }

        public void RequestLeaveComputer()
        {
            if (State == ComputerLocalUseState.Idle || State == ComputerLocalUseState.Exiting) return;
            BeginExit();
            RequestRelease();
        }

        private void HandleLocalUseEnded()
        {
            if (State != ComputerLocalUseState.Idle && State != ComputerLocalUseState.Exiting)
                BeginExit();
        }

        private void BeginExit()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            SetState(ComputerLocalUseState.Exiting);
            if (desktop != null) desktop.SetLocalInputEnabled(false);
            if (!isActiveAndEnabled || _camera == null)
            {
                RestoreLocalState();
                return;
            }
            _transition = StartCoroutine(Exit());
        }

        private IEnumerator Exit()
        {
            if (_fullscreenStarted && desktop != null) yield return BlendDesktop(0f);
            // Unregister the desktop focus/cursor token before restoring our own saved state.
            if (desktop != null) desktop.EndLocalUse();
            _fullscreenStarted = false;
            if (_camera != null)
            {
                Vector3 startPosition = _camera.transform.position;
                Quaternion startRotation = _camera.transform.rotation;
                float startFieldOfView = _camera.fieldOfView;
                float elapsed = 0f;
                while (elapsed < exitDuration && _camera != null)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Ease(elapsed / exitDuration);
                    Transform parent = _camera.transform.parent;
                    Vector3 destination = parent != null
                        ? parent.TransformPoint(_savedLocalPosition) : _savedLocalPosition;
                    Quaternion rotation = parent != null
                        ? parent.rotation * _savedLocalRotation : _savedLocalRotation;
                    _camera.transform.SetPositionAndRotation(
                        Vector3.LerpUnclamped(startPosition, destination, t),
                        Quaternion.SlerpUnclamped(startRotation, rotation, t));
                    _camera.fieldOfView = Mathf.LerpUnclamped(startFieldOfView, _savedFieldOfView, t);
                    yield return null;
                }
            }
            _transition = null;
            RestoreLocalState();
        }

        private IEnumerator BlendDesktop(float target)
        {
            float start = _presentationBlend;
            float elapsed = 0f;
            while (elapsed < fullscreenBlendDuration && desktop != null)
            {
                elapsed += Time.unscaledDeltaTime;
                _presentationBlend = Mathf.Lerp(start, target, Ease(elapsed / fullscreenBlendDuration));
                desktop.SetPresentationBlend(_presentationBlend);
                yield return null;
            }
            _presentationBlend = target;
            if (desktop != null) desktop.SetPresentationBlend(target);
        }

        private float Ease(float progress)
        {
            progress = Mathf.Clamp01(progress);
            return easing != null ? Mathf.Clamp01(easing.Evaluate(progress)) : Mathf.SmoothStep(0f, 1f, progress);
        }

        private void RequestRelease()
        {
            if (_releaseRequested || !_ownsInput || computer == null) return;
            _releaseRequested = true;
            computer.RequestRelease(_sessionToken);
        }

        private void BindActiveLifecycle()
        {
            _presence = _inputReader.GetComponent<NetworkPlayerPresenceController>();
            if (_presence != null) _presence.GameplayPresenceChanged += HandlePresenceChanged;
            _session = MultiplayerSessionCoordinator.Instance;
            if (_session != null) _session.StateChanged += HandleSessionStateChanged;
            _carrier = _inputReader.GetComponent<NetworkItemCarrier>();
            if (_carrier != null) _carrier.HeldStackChanged += RefreshHeldVisuals;
        }

        private void HandleLocalReaderChanged(NetworkPlayerInputReader reader)
        {
            if (_ownsInput && reader != _inputReader)
            {
                RequestRelease();
                RestoreLocalState();
            }
        }

        private void HandlePresenceChanged(bool active)
        {
            if (active) return;
            RequestRelease();
            RestoreLocalState();
        }

        private void HandleSessionStateChanged(GameSessionState state)
        {
            if (state == GameSessionState.Gameplay) return;
            RequestRelease();
            RestoreLocalState();
        }

        private void RefreshHeldVisuals()
        {
            RestoreHeldVisuals();
            if (!hideLocalHeldItems || _carrier == null) return;
            for (int i = 0; i < _carrier.HeldItemCount; i++)
            {
                if (!_carrier.TryGetHeldItemAt(i, out NetworkWorldItem item) || item == null) continue;
                _rendererBuffer.Clear();
                item.GetComponentsInChildren(true, _rendererBuffer);
                foreach (Renderer renderer in _rendererBuffer)
                {
                    if (renderer == null) continue;
                    _hiddenRenderers.Add(new RendererState(renderer));
                    renderer.forceRenderingOff = true;
                }
            }
            _rendererBuffer.Clear();
        }

        private void RestoreHeldVisuals()
        {
            foreach (RendererState state in _hiddenRenderers)
                if (state.Renderer != null) state.Renderer.forceRenderingOff = state.ForceRenderingOff;
            _hiddenRenderers.Clear();
        }

        private void HideHud()
        {
            foreach (MonoBehaviour component in _inputReader.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(component is NetworkInteractionDebugHud) &&
                    !(component is NetworkItemCarrierDebugHud)) continue;
                _hiddenDebugHud.Add(new DebugHudState(component));
                component.enabled = false;
            }
            foreach (CanvasGroup group in hudGroups)
            {
                if (group == null || transform.IsChildOf(group.transform) ||
                    (desktop != null && desktop.transform.IsChildOf(group.transform))) continue;
                _hiddenHud.Add(new HudState(group));
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        private void RestoreLocalState()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            if (!_ownsInput) return;
            if (desktop != null) desktop.EndLocalUse();
            if (_presence != null) _presence.GameplayPresenceChanged -= HandlePresenceChanged;
            if (_session != null) _session.StateChanged -= HandleSessionStateChanged;
            if (_carrier != null) _carrier.HeldStackChanged -= RefreshHeldVisuals;
            RestoreHeldVisuals();
            foreach (HudState state in _hiddenHud)
            {
                if (state.Group == null) continue;
                state.Group.alpha = state.Alpha;
                state.Group.interactable = state.Interactable;
                state.Group.blocksRaycasts = state.BlocksRaycasts;
            }
            _hiddenHud.Clear();
            foreach (DebugHudState state in _hiddenDebugHud)
                if (state.Component != null) state.Component.enabled = state.Enabled;
            _hiddenDebugHud.Clear();
            if (_camera != null)
            {
                _camera.transform.SetLocalPositionAndRotation(_savedLocalPosition, _savedLocalRotation);
                _camera.fieldOfView = _savedFieldOfView;
                _camera.nearClipPlane = _savedNearClip;
            }
            if (_inputReader != null) _inputReader.SetPauseSuppressed(this, false);
            GameplayInputGate.Clear(this);
            bool gameplayReady = _inputReader != null && _inputReader == NetworkPlayerInputReader.Local &&
                _inputReader.IsSpawned && _inputReader.GameplayEnabled &&
                (_session == null || _session.State == GameSessionState.Gameplay);
            if (gameplayReady && !GameplayInputGate.IsBlocked && !SharedDevelopmentIMGUI.IsOpen)
            {
                Cursor.lockState = _savedCursorLock;
                Cursor.visible = _savedCursorVisible;
            }
            else if (!gameplayReady)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            _ownsInput = false;
            _fullscreenStarted = false;
            _presentationBlend = 0f;
            _inputReader = null;
            _camera = null;
            _presence = null;
            _session = null;
            _carrier = null;
            if (Active == this) Active = null;
            SetState(ComputerLocalUseState.Idle);
        }

        private void SetState(ComputerLocalUseState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        [Button("Validate Computer Local Setup")]
        public bool ValidateSetup(bool logResult = true)
        {
            bool valid = computer != null && desktop != null && screenViewAnchor != null;
            if (logResult && !valid)
                Debug.LogError("[Computer] Assign Network Store Computer, Desktop and Screen View Anchor on the local presenter.", this);
            return valid;
        }
    }
}
