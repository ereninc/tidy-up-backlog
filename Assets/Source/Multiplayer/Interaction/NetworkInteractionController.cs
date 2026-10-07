using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

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
        private const int RaycastOverflowBufferSize = 128;
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
        public string CurrentTargetDisplayName =>
            CurrentTarget is NetworkItemInteractable shelfCase &&
            shelfCase.TryGetShelfReceiver(out NetworkItemReceiver receiver)
                ? receiver.GetInteractionDisplayName(this)
                : CurrentTarget != null
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

        public string InteractionKeyDisplayName => IsUsingGamepad
            ? "A / Cross"
            : "E";

        public string AlternativeInteractionKeyDisplayName => IsUsingGamepad
            ? "Y / Triangle"
            : "F";

        public string CurrentAlternativePrompt =>
            CurrentTarget is NetworkItemReceiver && _alternativeShelfCase != null
            ? _alternativeShelfCase.GetInteractionPrompt(this)
            : string.Empty;

        public bool CanLocallyInteract =>
            IsSpawned && IsOwner &&
            !GameplayInputGate.IsBlocked &&
            _inputReader != null && _inputReader.CanReadGameplayInput &&
            CurrentTarget != null;

        private readonly RaycastHit[] _raycastHits =
            new RaycastHit[RaycastBufferSize];

        private readonly RaycastHit[] _overflowRaycastHits =
            new RaycastHit[RaycastOverflowBufferSize];

        private uint _nextSequence;
        private double _lastLocalRequestTime = double.NegativeInfinity;
        private double _lastServerRequestTime = double.NegativeInfinity;
        private double _pendingSince;
        private uint _latestResponseSequence;
        private NetworkPlayerInputReader _inputReader;
        private NetworkItemInteractable _alternativeShelfCase;
        private bool IsUsingGamepad =>
            _inputReader != null && _inputReader.IsGamepad;

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

            if (GameplayInputGate.IsBlocked || _inputReader == null ||
                !_inputReader.CanReadGameplayInput)
            {
                ClearFocus();
                return;
            }

            UpdateFocus();

            if (requireLockedCursorForInput &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            if (_inputReader.ShelfPickupPressedThisFrame)
            {
                RequestShelfPickupInteraction();
            }
            else if (_inputReader.InteractPressedThisFrame)
            {
                RequestCurrentInteraction();
            }
        }

        [Button("AUTO ASSIGN REFERENCES")]
        [PropertyOrder(-10)]
        public void AutoAssignReferences()
        {
            if (_inputReader == null)
            {
                _inputReader = GetComponent<NetworkPlayerInputReader>();
            }

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
            return RequestInteraction(CurrentTarget, false);
        }

        public bool RequestShelfPickupInteraction()
        {
            if (!TryBuildViewRay(out Ray ray) ||
                !TryGetTargetHit(
                    ray, interactionDistance, 0f, true,
                    out RaycastHit hit, out NetworkInteractable target, out _) ||
                !(target is NetworkItemInteractable shelfCase) ||
                !shelfCase.TryGetShelfReceiver(out _) ||
                hit.distance > shelfCase.MaximumInteractionDistance ||
                !shelfCase.IsAvailableLocally(this))
            {
                return false;
            }

            return RequestInteraction(shelfCase, true);
        }

        private bool RequestInteraction(
            NetworkInteractable target,
            bool preferShelfPickup)
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

            if (GameplayInputGate.IsBlocked || _inputReader == null ||
                !_inputReader.CanReadGameplayInput)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NotReady,
                    "Gameplay input is currently blocked by UI or loading.");
                return false;
            }

            if (target == null)
            {
                SetLocalRejected(
                    sequence,
                    InteractionRejectReason.NoFocusedTarget,
                    "No interactable is in focus.");
                return false;
            }

            if (!target.IsSpawned || target.NetworkObject == null)
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
                $"Request sent to {target.name}."));

            NetworkObjectReference targetReference =
                new NetworkObjectReference(target.NetworkObject);

            RequestInteractionServerRpc(
                targetReference,
                ray.origin,
                ray.direction,
                preferShelfPickup,
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
            _alternativeShelfCase = null;

            if (!TryBuildViewRay(out Ray ray) ||
                !TryGetTargetHit(
                    ray,
                    interactionDistance,
                    0f,
                    false,
                    out RaycastHit hit,
                    out NetworkInteractable candidate,
                    out NetworkItemInteractable alternativeShelfCase))
            {
                SetFocus(null, 0f);
                return;
            }

            if (candidate == null ||
                candidate.NetworkObject == NetworkObject ||
                hit.distance > candidate.MaximumInteractionDistance ||
                !candidate.IsAvailableLocally(this))
            {
                SetFocus(null, 0f);
                return;
            }

            _alternativeShelfCase = alternativeShelfCase;
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
            _alternativeShelfCase = null;
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

        private bool TryGetTargetHit(
            Ray ray,
            float maximumDistance,
            float targetDistanceTolerance,
            bool preferShelfPickup,
            out RaycastHit nearestHit,
            out NetworkInteractable target,
            out NetworkItemInteractable alternativeShelfCase)
        {
            nearestHit = default;
            target = null;
            alternativeShelfCase = null;

            RaycastHit[] hits = _raycastHits;

            int hitCount = Physics.RaycastNonAlloc(
                ray,
                hits,
                maximumDistance,
                interactionMask,
                queryTriggers);

            if (hitCount == hits.Length)
            {
                hits = _overflowRaycastHits;
                hitCount = Physics.RaycastNonAlloc(
                    ray,
                    hits,
                    maximumDistance,
                    interactionMask,
                    queryTriggers);

                // NonAlloc may omit the nearest obstacle when full.
                if (hitCount == hits.Length)
                {
                    return false;
                }
            }

            float nearestDistance = float.PositiveInfinity;
            bool found = false;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = hits[i];

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

            if (!found)
            {
                return false;
            }

            target = nearestHit.collider
                .GetComponentInParent<NetworkInteractable>();

            NetworkItemCarrier carrier = GetComponent<NetworkItemCarrier>();

            if (target is NetworkItemInteractable directCase &&
                directCase.TryGetShelfReceiver(out NetworkItemReceiver directReceiver) &&
                directReceiver.TryGetShelfPlacementCandidate(
                    carrier, out _, out _, out _, out _))
            {
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit obstruction = hits[i];

                    if (obstruction.collider == null ||
                        IsOwnCollider(obstruction.collider) ||
                        obstruction.distance > nearestHit.distance ||
                        obstruction.collider.GetComponentInParent<NetworkInteractable>() == directCase ||
                        directReceiver.IsShelfInteractionCollider(obstruction.collider))
                    {
                        continue;
                    }

                    target = null;
                    return true;
                }

                bool canPickUp = directCase.IsAvailableLocally(this) &&
                    nearestHit.distance <= directCase.MaximumInteractionDistance +
                        targetDistanceTolerance;
                alternativeShelfCase = canPickUp ? directCase : null;

                if (!preferShelfPickup || !canPickUp)
                {
                    target = directReceiver;
                }

                return true;
            }

            NetworkItemReceiver receiver = target as NetworkItemReceiver;

            if (receiver == null || !receiver.IsAvailableLocally(this) ||
                !receiver.IsShelfInteractionCollider(nearestHit.collider))
            {
                return true;
            }

            NetworkItemInteractable shelfCase = null;
            RaycastHit caseHit = default;
            float caseDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidateHit = hits[i];

                if (candidateHit.collider == null ||
                    IsOwnCollider(candidateHit.collider) ||
                    candidateHit.distance >= caseDistance)
                {
                    continue;
                }

                NetworkItemInteractable candidate = candidateHit.collider
                    .GetComponentInParent<NetworkInteractable>() as NetworkItemInteractable;

                if (candidate == null ||
                    candidateHit.distance > candidate.MaximumInteractionDistance +
                        targetDistanceTolerance ||
                    !candidate.IsAvailableLocally(this) ||
                    !candidate.TryGetShelfReceiver(out NetworkItemReceiver caseReceiver) ||
                    caseReceiver != receiver)
                {
                    continue;
                }

                shelfCase = candidate;
                caseHit = candidateHit;
                caseDistance = candidateHit.distance;
            }

            if (shelfCase == null)
            {
                return true;
            }

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit obstruction = hits[i];

                if (obstruction.collider == null ||
                    IsOwnCollider(obstruction.collider) ||
                    obstruction.distance > caseDistance ||
                    obstruction.collider.GetComponentInParent<NetworkInteractable>() == shelfCase ||
                    receiver.IsShelfInteractionCollider(obstruction.collider))
                {
                    continue;
                }

                return true;
            }

            if (receiver.TryGetShelfPlacementCandidate(
                    carrier, out _, out _, out _, out _))
            {
                alternativeShelfCase = shelfCase;

                if (!preferShelfPickup)
                {
                    return true;
                }
            }

            nearestHit = caseHit;
            target = shelfCase;
            return true;
        }

        private bool IsOwnCollider(Collider candidate)
        {
            Transform candidateTransform = candidate.transform;
            return candidateTransform == transform ||
                   candidateTransform.IsChildOf(transform);
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestInteractionServerRpc(
            NetworkObjectReference targetReference,
            Vector3 submittedOrigin,
            Vector3 submittedDirection,
            bool preferShelfPickup,
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

            if (!TryGetTargetHit(
                    validationRay,
                    allowedDistance + distanceTolerance,
                    distanceTolerance,
                    preferShelfPickup,
                    out RaycastHit serverHit,
                    out NetworkInteractable firstHitInteractable,
                    out _))
            {
                SendResultToClient(
                    senderClientId,
                    sequence,
                    false,
                    InteractionRejectReason.OutOfRange,
                    "Server ray did not reach the target.");
                return;
            }

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
