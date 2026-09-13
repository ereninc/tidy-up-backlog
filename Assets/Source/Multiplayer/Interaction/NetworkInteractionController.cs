using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Owner-side focus/input and server-side validation for instant interactions.
    /// Attach exactly once to the network player prefab root.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Interaction/Network Interaction Controller")]
    public sealed class NetworkInteractionController : NetworkBehaviour
    {
        private const int RaycastBufferSize = 32;
        private const int MaximumResultMessageCharacters = 160;

        [InfoBox(
            "The center-screen focus ray is local and sends no packets. Pressing " +
            "Interact sends one request; the server re-checks ownership, rate, " +
            "origin, horizontal aim, range, line-of-sight and target rules.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private Camera viewCamera;

        [TitleGroup("References")]
        [Required]
        [Tooltip("Usually the local FPS camera transform.")]
        [SerializeField] private Transform interactionOrigin;

        [TitleGroup("References")]
        [Required]
        [Tooltip(
            "Server-side expected origin. It may be the same camera transform; " +
            "only its position is trusted.")]
        [SerializeField] private Transform serverValidationOrigin;

        [TitleGroup("Targeting")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float interactionDistance = 3.25f;

        [TitleGroup("Targeting")]
        [SerializeField] private LayerMask interactionMask = ~0;

        [TitleGroup("Targeting")]
        [SerializeField] private QueryTriggerInteraction queryTriggers =
            QueryTriggerInteraction.Collide;

        [TitleGroup("Input")]
        [SerializeField] private Key interactionKey = Key.E;

        [TitleGroup("Input")]
        [SerializeField] private bool allowGamepadSouthButton = true;

        [TitleGroup("Input")]
        [Tooltip("Prevents gameplay interactions while a menu owns the cursor.")]
        [SerializeField] private bool requireLockedCursorForInput = true;

        [TitleGroup("Server Validation")]
        [MinValue(0f)]
        [SuffixLabel("s")]
        [SerializeField] private float minimumRequestInterval = 0.08f;

        [TitleGroup("Server Validation")]
        [MinValue(0.05f)]
        [SuffixLabel("m")]
        [Tooltip(
            "Allows for owner-transform replication delay, but still prevents a " +
            "client from submitting an arbitrary ray origin.")]
        [SerializeField] private float originTolerance = 1.75f;

        [TitleGroup("Server Validation")]
        [Range(0f, 180f)]
        [Tooltip(
            "Compared only on the horizontal plane because camera pitch is not " +
            "replicated by the minimal FPS controller.")]
        [SerializeField] private float maximumHorizontalAimAngle = 100f;

        [TitleGroup("Server Validation")]
        [MinValue(0f)]
        [SuffixLabel("m")]
        [SerializeField] private float distanceTolerance = 0.2f;

        [TitleGroup("Diagnostics")]
        [MinValue(0.25f)]
        [SuffixLabel("s")]
        [SerializeField] private float responseTimeout = 3f;

        [TitleGroup("Diagnostics")]
        [SerializeField] private bool logSuccessfulInteractions = true;

        [TitleGroup("Diagnostics")]
        [SerializeField] private bool logRejectedInteractions = true;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Local Controller")]
        public static NetworkInteractionController Local { get; private set; }

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Focused Target")]
        public NetworkInteractable CurrentTarget { get; private set; }

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Prompt")]
        public string CurrentPrompt => CurrentTarget != null
            ? CurrentTarget.GetInteractionPrompt(this)
            : string.Empty;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Display Name")]
        public string CurrentTargetDisplayName => CurrentTarget != null
            ? CurrentTarget.GetInteractionDisplayName(this)
            : string.Empty;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Focus Distance")]
        [SuffixLabel("m")]
        public float CurrentTargetDistance { get; private set; }

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live State")]
        [LabelText("Last Result")]
        public string LastResultSummary => BuildResultSummary();

        public event Action<NetworkInteractable, NetworkInteractable>
            FocusChanged;

        public event Action<InteractionResult> InteractionResultChanged;

        public InteractionResult LastResult { get; private set; } =
            InteractionResult.Empty;

        public string InteractionKeyDisplayName => interactionKey.ToString();
        public bool CanLocallyInteract =>
            IsSpawned && IsOwner &&
            !GameplayInputGate.IsBlocked &&
            CurrentTarget != null;

        private readonly RaycastHit[] _raycastHits =
            new RaycastHit[RaycastBufferSize];

        private uint _nextSequence;
        private double _lastLocalRequestTime = double.NegativeInfinity;
        private double _lastServerRequestTime = double.NegativeInfinity;
        private double _pendingSince;
        private uint _latestResponseSequence;

        private bool CanRequestFromInspector =>
            Application.isPlaying && CanLocallyInteract;

        private void Reset()
        {
            AutoAssignReferences();
            ValidateConfiguration();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
            ValidateConfiguration();
        }

        public override void OnNetworkSpawn()
        {
            AutoAssignReferences();

            if (!IsOwner)
            {
                return;
            }

            Local = this;
            LastResult = InteractionResult.Empty;

            if (viewCamera == null || interactionOrigin == null)
            {
                Debug.LogError(
                    "[Interaction] Local player has no view camera/origin. " +
                    "Run AUTO ASSIGN REFERENCES on the player prefab.",
                    this);
            }
        }

        public override void OnNetworkDespawn()
        {
            ClearFocus();

            if (Local == this)
            {
                Local = null;
            }
        }

        private void OnDisable()
        {
            ClearFocus();

            if (Local == this)
            {
                Local = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner)
            {
                return;
            }

            UpdatePendingTimeout();

            if (GameplayInputGate.IsBlocked)
            {
                ClearFocus();
                return;
            }

            UpdateFocus();

            if (WasInteractionPressedThisFrame())
            {
                RequestCurrentInteraction();
            }
        }

        [Button("AUTO ASSIGN REFERENCES")]
        [PropertyOrder(-10)]
        public void AutoAssignReferences()
        {
            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<Camera>(true);
            }

            if (interactionOrigin == null && viewCamera != null)
            {
                interactionOrigin = viewCamera.transform;
            }

            if (serverValidationOrigin == null)
            {
                serverValidationOrigin = interactionOrigin != null
                    ? interactionOrigin
                    : transform;
            }
        }

        [Button("INTERACT WITH FOCUSED TARGET")]
        [EnableIf(nameof(CanRequestFromInspector))]
        [PropertyOrder(-9)]
        public bool RequestCurrentInteraction()
        {
            uint sequence = NextSequence();

            if (!IsSpawned)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NotReady,
                    "Interaction controller is not network-spawned.");
                return false;
            }

            if (!IsOwner)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NotOwner,
                    "Only the owning player can interact.");
                return false;
            }

            if (GameplayInputGate.IsBlocked)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NotReady,
                    "Gameplay input is currently blocked by UI or loading.");
                return false;
            }

            if (CurrentTarget == null)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NoFocusedTarget,
                    "No interactable is in focus.");
                return false;
            }

            if (!CurrentTarget.IsSpawned ||
                CurrentTarget.NetworkObject == null)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.TargetNotSpawned,
                    "Focused target is not network-spawned.");
                return false;
            }

            double now = Time.realtimeSinceStartupAsDouble;

            if (now - _lastLocalRequestTime < minimumRequestInterval)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.RateLimited,
                    "Interaction input is rate-limited.");
                return false;
            }

            if (!TryBuildViewRay(out Ray ray))
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NotReady,
                    "No valid interaction ray is available.");
                return false;
            }

            _lastLocalRequestTime = now;
            _pendingSince = now;

            SetResult(new InteractionResult(
                sequence,
                InteractionResultState.Pending,
                InteractionRejectReason.None,
                $"Request sent to {CurrentTarget.name}."));

            NetworkObjectReference targetReference =
                new NetworkObjectReference(CurrentTarget.NetworkObject);

            RequestInteractionServerRpc(
                targetReference,
                ray.origin,
                ray.direction,
                sequence);

            return true;
        }

        [Button("CLEAR LAST RESULT")]
        public void ClearLastResult()
        {
            LastResult = InteractionResult.Empty;
            InteractionResultChanged?.Invoke(LastResult);
        }

        private void UpdateFocus()
        {
            if (!TryBuildViewRay(out Ray ray) ||
                !TryGetNearestExternalHit(
                    ray,
                    interactionDistance,
                    out RaycastHit hit))
            {
                SetFocus(null, 0f);
                return;
            }

            NetworkInteractable candidate =
                hit.collider.GetComponentInParent<NetworkInteractable>();

            if (candidate == null ||
                candidate.NetworkObject == NetworkObject ||
                hit.distance > candidate.MaximumInteractionDistance ||
                !candidate.IsAvailableLocally(this))
            {
                SetFocus(null, 0f);
                return;
            }

            SetFocus(candidate, hit.distance);
        }

        private void SetFocus(NetworkInteractable target, float distance)
        {
            if (CurrentTarget == target)
            {
                CurrentTargetDistance = target != null ? distance : 0f;
                return;
            }

            NetworkInteractable previous = CurrentTarget;

            if (previous != null)
            {
                previous.SetLocallyFocused(false, this);
            }

            CurrentTarget = target;
            CurrentTargetDistance = target != null ? distance : 0f;

            if (CurrentTarget != null)
            {
                CurrentTarget.SetLocallyFocused(true, this);
            }

            FocusChanged?.Invoke(previous, CurrentTarget);
        }

        private void ClearFocus()
        {
            SetFocus(null, 0f);
        }

        private bool TryBuildViewRay(out Ray ray)
        {
            if (viewCamera != null)
            {
                ray = viewCamera.ViewportPointToRay(
                    new Vector3(0.5f, 0.5f, 0f));
                return NetworkInteractionValidation.IsFinite(ray.origin) &&
                       NetworkInteractionValidation.TryNormalizeDirection(
                           ray.direction,
                           out _);
            }

            if (interactionOrigin != null)
            {
                ray = new Ray(
                    interactionOrigin.position,
                    interactionOrigin.forward);
                return NetworkInteractionValidation.IsFinite(ray.origin) &&
                       NetworkInteractionValidation.TryNormalizeDirection(
                           ray.direction,
                           out _);
            }

            ray = default;
            return false;
        }

        private bool TryGetNearestExternalHit(
            Ray ray,
            float maximumDistance,
            out RaycastHit nearestHit)
        {
            nearestHit = default;

            int hitCount = Physics.RaycastNonAlloc(
                ray,
                _raycastHits,
                maximumDistance,
                interactionMask,
                queryTriggers);

            float nearestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = _raycastHits[i];

                if (candidate.collider == null ||
                    IsOwnCollider(candidate.collider) ||
                    candidate.distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = candidate.distance;
                nearestHit = candidate;
                found = true;
            }

            return found;
        }

        private bool IsOwnCollider(Collider candidate)
        {
            Transform candidateTransform = candidate.transform;
            return candidateTransform == transform ||
                   candidateTransform.IsChildOf(transform);
        }

        private bool WasInteractionPressedThisFrame()
        {
            if (requireLockedCursorForInput &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null)
            {
                var keyControl = keyboard[interactionKey];

                if (keyControl != null && keyControl.wasPressedThisFrame)
                {
                    return true;
                }
            }

            return allowGamepadSouthButton &&
                   Gamepad.current != null &&
                   Gamepad.current.buttonSouth.wasPressedThisFrame;
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestInteractionServerRpc(
            NetworkObjectReference targetReference,
            Vector3 submittedOrigin,
            Vector3 submittedDirection,
            uint sequence,
            ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!IsSpawned || !IsServer)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.NotReady,
                    "Server interaction controller is not ready.");
                return;
            }

            if (senderClientId != OwnerClientId)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidSender,
                    "RPC sender does not own this player object.");
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;

            if (now - _lastServerRequestTime < minimumRequestInterval)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.RateLimited,
                    "Server rejected interaction spam.");
                return;
            }

            _lastServerRequestTime = now;

            if (!NetworkInteractionValidation.TryNormalizeDirection(
                    submittedDirection,
                    out Vector3 normalizedDirection))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidAim,
                    "Submitted aim direction is invalid.");
                return;
            }

            if (NetworkManager == null ||
                !targetReference.TryGet(
                    out NetworkObject targetObject,
                    NetworkManager))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidTarget,
                    "Target reference could not be resolved.");
                return;
            }

            if (!targetObject.IsSpawned ||
                !targetObject.TryGetComponent(
                    out NetworkInteractable target) ||
                !target.IsSpawned)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.TargetNotSpawned,
                    "Target is not a spawned NetworkInteractable.");
                return;
            }

            if (targetObject == NetworkObject)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidTarget,
                    "A player cannot target its own network root.");
                return;
            }

            Vector3 expectedOrigin = serverValidationOrigin != null
                ? serverValidationOrigin.position
                : transform.position + Vector3.up * 1.6f;

            if (!NetworkInteractionValidation.IsOriginWithinTolerance(
                    expectedOrigin,
                    submittedOrigin,
                    originTolerance))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidOrigin,
                    "Submitted interaction origin is too far from the player.");
                return;
            }

            if (!NetworkInteractionValidation.IsAimWithinTolerance(
                    transform.forward,
                    normalizedDirection,
                    maximumHorizontalAimAngle))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.InvalidAim,
                    "Submitted aim does not match the player's facing direction.");
                return;
            }

            float allowedDistance = Mathf.Min(
                interactionDistance,
                target.MaximumInteractionDistance);

            Ray validationRay = new Ray(
                submittedOrigin,
                normalizedDirection);

            if (!TryGetNearestExternalHit(
                    validationRay,
                    allowedDistance + distanceTolerance,
                    out RaycastHit serverHit))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.OutOfRange,
                    "Server ray did not reach the target.");
                return;
            }

            NetworkInteractable firstHitInteractable =
                serverHit.collider.GetComponentInParent<NetworkInteractable>();

            if (firstHitInteractable != target)
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.Obstructed,
                    "Another collider blocks line-of-sight to the target.");
                return;
            }

            if (!NetworkInteractionValidation.IsWithinDistance(
                    submittedOrigin,
                    serverHit.point,
                    allowedDistance,
                    distanceTolerance))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.OutOfRange,
                    "Target is outside interaction range.");
                return;
            }

            NetworkInteractionContext context =
                new NetworkInteractionContext(
                    senderClientId,
                    this,
                    target,
                    submittedOrigin,
                    normalizedDirection,
                    serverHit.point,
                    serverHit.normal,
                    now);

            bool success = target.TryExecuteServer(
                context,
                out InteractionRejectReason rejectReason,
                out string resultMessage);

            SendResultToClient(
                senderClientId,
                sequence,
                success,
                rejectReason,
                resultMessage);
        }

        private void SendResultToClient(
            ulong clientId,
            uint sequence,
            bool success,
            InteractionRejectReason reason,
            string message)
        {
            if (NetworkManager == null || !NetworkManager.IsListening)
            {
                return;
            }

            string safeMessage = SanitizeResultMessage(message);

            ClientRpcParams clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            };

            ReceiveInteractionResultClientRpc(
                sequence,
                success,
                reason,
                safeMessage,
                clientRpcParams);
        }

        [ClientRpc]
        private void ReceiveInteractionResultClientRpc(
            uint sequence,
            bool success,
            InteractionRejectReason reason,
            string message,
            ClientRpcParams clientRpcParams = default)
        {
            if (!IsOwner || sequence < _latestResponseSequence)
            {
                return;
            }

            _latestResponseSequence = sequence;

            InteractionResult result = new InteractionResult(
                sequence,
                success
                    ? InteractionResultState.Succeeded
                    : InteractionResultState.Rejected,
                success ? InteractionRejectReason.None : reason,
                message);

            SetResult(result);

            if (success && logSuccessfulInteractions)
            {
                Debug.Log(
                    $"[Interaction] Success #{sequence}: {message}",
                    this);
            }
            else if (!success && logRejectedInteractions)
            {
                Debug.LogWarning(
                    $"[Interaction] Rejected #{sequence} ({reason}): " +
                    message,
                    this);
            }
        }

        private void UpdatePendingTimeout()
        {
            if (LastResult.State != InteractionResultState.Pending ||
                Time.realtimeSinceStartupAsDouble - _pendingSince <
                responseTimeout)
            {
                return;
            }

            SetLocalRejected(
                LastResult.Sequence,
                InteractionRejectReason.TimedOut,
                "Server interaction response timed out.");
        }

        private void SetLocalRejected(
            uint sequence,
            InteractionRejectReason reason,
            string message)
        {
            InteractionResult result = new InteractionResult(
                sequence,
                InteractionResultState.Rejected,
                reason,
                message);

            SetResult(result);

            if (logRejectedInteractions)
            {
                Debug.LogWarning(
                    $"[Interaction] Local rejection #{sequence} ({reason}): " +
                    message,
                    this);
            }
        }

        private void SetResult(InteractionResult result)
        {
            LastResult = result;
            InteractionResultChanged?.Invoke(result);
        }

        private uint NextSequence()
        {
            _nextSequence++;

            if (_nextSequence == 0)
            {
                _nextSequence = 1;
            }

            return _nextSequence;
        }

        private void ValidateConfiguration()
        {
            interactionDistance = Mathf.Max(0.1f, interactionDistance);
            minimumRequestInterval = Mathf.Max(0f, minimumRequestInterval);
            originTolerance = Mathf.Max(0.05f, originTolerance);
            maximumHorizontalAimAngle = Mathf.Clamp(
                maximumHorizontalAimAngle,
                0f,
                180f);
            distanceTolerance = Mathf.Max(0f, distanceTolerance);
            responseTimeout = Mathf.Max(0.25f, responseTimeout);
        }

        private string BuildResultSummary()
        {
            if (LastResult.State == InteractionResultState.None)
            {
                return "No interaction result yet.";
            }

            string reason = LastResult.Reason == InteractionRejectReason.None
                ? string.Empty
                : $" | {LastResult.Reason}";

            return $"#{LastResult.Sequence} | {LastResult.State}{reason} | " +
                   LastResult.Message;
        }

        private static string SanitizeResultMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            string trimmed = message.Trim();
            return trimmed.Length <= MaximumResultMessageCharacters
                ? trimmed
                : trimmed.Substring(0, MaximumResultMessageCharacters);
        }
    }
}
