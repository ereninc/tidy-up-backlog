using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Server-authoritative carried-item stack. Items remain parented directly
    /// to the player NetworkObject; the stack only supplies ordering and poses.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Carrier")]
    public sealed class NetworkItemCarrier : NetworkBehaviour
    {
        private readonly NetworkList<NetworkObjectReference> _heldItems =
            new NetworkList<NetworkObjectReference>();

        private readonly NetworkVariable<int> _carryLimit =
            new NetworkVariable<int>(
                1,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "Every held item stays parented to the player NetworkObject root. " +
            "ItemCarryAnchor and the stack steps only calculate local poses.")]
        [TitleGroup("Carry Pose")]
        [Required]
        [SerializeField] private Transform itemCarryAnchor;

        [TitleGroup("Carry Pose")]
        [Tooltip("Anchor-local offset added once per stack index.")]
        [SerializeField] private Vector3 stackLocalPositionStep =
            new Vector3(0f, 0.035f, 0f);

        [TitleGroup("Carry Pose")]
        [Tooltip("Optional anchor-local rotation added once per stack index.")]
        [SerializeField] private Vector3 stackLocalEulerStep;

        [TitleGroup("Carry Limit")]
        [MinValue(1)]
        [SerializeField] private int defaultCarryLimit = 1;

        [TitleGroup("Drop")]
        [SerializeField] private Key dropKey = Key.G;

        [TitleGroup("Drop")]
        [MinValue(0.2f)]
        [SuffixLabel("m")]
        [SerializeField] private float dropForwardDistance = 1.35f;

        [TitleGroup("Drop")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float groundProbeHeight = 1.5f;

        [TitleGroup("Drop")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float groundProbeDistance = 4f;

        [TitleGroup("Drop")]
        [SerializeField] private LayerMask dropSurfaceMask = ~0;

        [TitleGroup("Drop")]
        [SerializeField] private bool requireLockedCursorForInput = true;

        [TitleGroup("Drop Safety")]
        [SerializeField] private bool preventPlayerOverlapOnDrop = true;

        [TitleGroup("Drop Safety")]
        [MinValue(0f)]
        [SuffixLabel("m")]
        [SerializeField] private float playerDropPadding = 0.08f;

        [TitleGroup("Drop Safety")]
        [SerializeField] private Collider[] playerBodyColliders;

        [ShowInInspector, ReadOnly, BoxGroup("Live Carrier")]
        public static NetworkItemCarrier Local { get; private set; }

        [ShowInInspector, ReadOnly, BoxGroup("Live Carrier")]
        public bool HasHeldItem => HeldItemCount > 0;

        [ShowInInspector, ReadOnly, BoxGroup("Live Carrier")]
        public int CarryLimit => IsSpawned
            ? Mathf.Max(1, _carryLimit.Value)
            : Mathf.Max(1, defaultCarryLimit);

        [ShowInInspector, ReadOnly, BoxGroup("Live Carrier")]
        public int HeldItemCount => _heldItems.Count;

        [ShowInInspector, ReadOnly, BoxGroup("Live Carrier")]
        public string HeldItemName => TryGetHeldItem(out NetworkWorldItem item)
            ? item.DisplayName
            : "Empty";

        public string DropKeyDisplayName => dropKey.ToString();
        public Transform ItemCarryAnchor => itemCarryAnchor;

        /// <summary>Compatibility event: previous/current top item.</summary>
        public event Action<NetworkWorldItem, NetworkWorldItem> HeldItemChanged;
        public event Action HeldStackChanged;
        public event Action<int, int> CarryLimitChanged;

        private NetworkWorldItem _cachedTopItem;

        private bool CanDropFromInspector =>
            Application.isPlaying && IsSpawned && IsOwner &&
            !GameplayInputGate.IsBlocked && HasHeldItem;

        private readonly struct ReflowMotion
        {
            public readonly ulong NetworkObjectId;
            public readonly NetworkItemMotionPresenter Presenter;
            public readonly Vector3 StartPosition;
            public readonly Quaternion StartRotation;

            public ReflowMotion(
                NetworkWorldItem item,
                NetworkItemMotionPresenter presenter,
                Vector3 startPosition,
                Quaternion startRotation)
            {
                NetworkObjectId = item.NetworkObjectId;
                Presenter = presenter;
                StartPosition = startPosition;
                StartRotation = startRotation;
            }
        }

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
            base.OnNetworkSpawn();
            AutoAssignReferences();

            _heldItems.OnListChanged += HandleHeldItemsChanged;
            _carryLimit.OnValueChanged += HandleCarryLimitChanged;

            if (IsServer)
            {
                _carryLimit.Value = Mathf.Max(1, defaultCarryLimit);
            }

            if (IsOwner)
            {
                Local = this;
            }

            RefreshTopItemCache();

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback +=
                    HandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null &&
                NetworkManager.IsListening && HasHeldItem)
            {
                NetworkItemTransferService.TryDropAll(
                    this,
                    out _,
                    true);
            }

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -=
                    HandleClientDisconnected;
            }

            _heldItems.OnListChanged -= HandleHeldItemsChanged;
            _carryLimit.OnValueChanged -= HandleCarryLimitChanged;

            if (Local == this)
            {
                Local = null;
            }

            NetworkWorldItem previous = _cachedTopItem;
            _cachedTopItem = null;
            HeldItemChanged?.Invoke(previous, null);
            HeldStackChanged?.Invoke();
            base.OnNetworkDespawn();
        }

        public override void OnLostOwnership()
        {
            if (IsServer && NetworkManager != null &&
                NetworkManager.IsListening && HasHeldItem)
            {
                NetworkItemTransferService.TryDropAll(this, out _, true);
            }

            base.OnLostOwnership();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner ||
                GameplayInputGate.IsBlocked || !HasHeldItem)
            {
                return;
            }

            if (requireLockedCursorForInput &&
                Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            var keyControl = keyboard[dropKey];
            if (keyControl != null && keyControl.wasPressedThisFrame)
            {
                RequestDropServerRpc();
            }
        }

        #region Carry Limit

        [Button]
        public void IncreaseCarryLimit(int amount = 1)
        {
            if (amount > 0)
            {
                SetCarryLimit(amount + CarryLimit);
            }
        }

        [Button]
        public void SetCarryLimit(int value)
        {
            if (!IsSpawned || !IsServer)
            {
                Debug.LogWarning(
                    "Carry limit can only be changed by the spawned server carrier.",
                    this);
                return;
            }

            _carryLimit.Value = Mathf.Max(1, value);
        }

        [Button]
        public void ResetCarryLimitToDefault()
        {
            SetCarryLimit(defaultCarryLimit);
        }

        public bool IsCarryLimitReached()
        {
            return HeldItemCount >= CarryLimit;
        }

        #endregion

        public bool TryGetHeldItem(out NetworkWorldItem item)
        {
            return TryGetHeldItemAt(HeldItemCount - 1, out item);
        }

        public bool TryGetHeldItemAt(
            int stackIndex,
            out NetworkWorldItem item)
        {
            item = null;

            if (NetworkManager == null ||
                stackIndex < 0 || stackIndex >= _heldItems.Count ||
                !_heldItems[stackIndex].TryGet(
                    out NetworkObject itemObject,
                    NetworkManager))
            {
                return false;
            }

            return itemObject.TryGetComponent(out item) && item != null;
        }

        public bool TryGetStackIndex(
            NetworkWorldItem item,
            out int stackIndex)
        {
            stackIndex = -1;

            if (item == null)
            {
                return false;
            }

            for (int i = 0; i < _heldItems.Count; i++)
            {
                if (TryGetHeldItemAt(i, out NetworkWorldItem candidate) &&
                    candidate == item)
                {
                    stackIndex = i;
                    return true;
                }
            }

            return false;
        }

        public bool ContainsHeldItem(NetworkWorldItem item)
        {
            return TryGetStackIndex(item, out _);
        }

        internal bool TryGetHeldItemForServerCleanup(
            out NetworkWorldItem item)
        {
            item = null;
            return NetworkManager != null &&
                   NetworkManager.IsServer &&
                   TryGetHeldItem(out item);
        }

        internal bool AddHeldItemServer(
            NetworkWorldItem item,
            out int stackIndex)
        {
            stackIndex = -1;

            if (!CanWriteStackServer() || item == null ||
                ContainsHeldItem(item) || IsCarryLimitReached())
            {
                return false;
            }

            stackIndex = _heldItems.Count;
            _heldItems.Add(
                new NetworkObjectReference(item.NetworkObject));
            return true;
        }

        internal bool RemoveHeldItemServer(NetworkWorldItem item)
        {
            if (!CanWriteStackServer())
            {
                return false;
            }

            int removeIndex;
            if (item == null)
            {
                removeIndex = _heldItems.Count - 1;
            }
            else if (!TryGetStackIndex(item, out removeIndex))
            {
                return false;
            }

            if (removeIndex < 0 || removeIndex >= _heldItems.Count)
            {
                return false;
            }

            List<ReflowMotion> motions =
                CaptureReflowMotions(removeIndex + 1);

            _heldItems.RemoveAt(removeIndex);
            ReflowStackServer(removeIndex, motions);
            return true;
        }

        // Compatibility with existing item cleanup code.
        internal void SetHeldItemServer(NetworkWorldItem item)
        {
            AddHeldItemServer(item, out _);
        }

        // Null removes an unresolved top reference; a concrete item removes
        // that exact stack entry and closes the gap above it.
        internal void ClearHeldItemServer(NetworkWorldItem expectedItem)
        {
            RemoveHeldItemServer(expectedItem);
        }

        internal bool TryGetCarryWorldPose(
            NetworkCarryable carryable,
            int stackIndex,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            Transform anchor = itemCarryAnchor != null
                ? itemCarryAnchor
                : transform;

            Vector3 positionOffset = carryable != null
                ? carryable.GripPositionOffset
                : Vector3.zero;
            positionOffset += stackLocalPositionStep * stackIndex;

            Quaternion gripRotation = carryable != null
                ? carryable.GripRotationOffset
                : Quaternion.identity;
            Quaternion stackRotation = Quaternion.Euler(
                stackLocalEulerStep * stackIndex);

            worldPosition = anchor.TransformPoint(positionOffset);
            worldRotation = anchor.rotation * stackRotation * gripRotation;

            return NetworkInteractionValidation.IsFinite(worldPosition) &&
                   IsFinite(worldRotation);
        }

        internal bool TryGetCarryLocalPose(
            NetworkCarryable carryable,
            int stackIndex,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            if (!TryGetCarryWorldPose(
                    carryable,
                    stackIndex,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                localPosition = Vector3.zero;
                localRotation = Quaternion.identity;
                return false;
            }

            localPosition = transform.InverseTransformPoint(worldPosition);
            localRotation = Quaternion.Inverse(transform.rotation) *
                            worldRotation;

            return NetworkInteractionValidation.IsFinite(localPosition) &&
                   IsFinite(localRotation);
        }

        // Compatibility overload: pose of the current top item.
        internal bool TryGetCarryLocalPose(
            NetworkCarryable carryable,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            return TryGetCarryLocalPose(
                carryable,
                Mathf.Max(0, HeldItemCount - 1),
                out localPosition,
                out localRotation);
        }

        [Button("AUTO ASSIGN CARRY ANCHOR")]
        public void AutoAssignReferences()
        {
            if (itemCarryAnchor == null)
            {
                Transform[] children =
                    GetComponentsInChildren<Transform>(true);

                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].name == "ItemCarryAnchor")
                    {
                        itemCarryAnchor = children[i];
                        break;
                    }
                }
            }

            if (playerBodyColliders == null ||
                playerBodyColliders.Length == 0)
            {
                playerBodyColliders =
                    GetComponentsInChildren<Collider>(true);
            }
        }

        [Button("DROP TOP ITEM")]
        [EnableIf(nameof(CanDropFromInspector))]
        public void RequestDropFromInspector()
        {
            if (CanDropFromInspector)
            {
                RequestDropServerRpc();
            }
        }

        internal void BroadcastItemMotionServer(
            NetworkWorldItem item,
            Vector3 startWorldPosition,
            Quaternion startWorldRotation,
            uint targetRevision)
        {
            if (!IsServer || !IsSpawned ||
                item == null || !item.IsSpawned)
            {
                return;
            }

            PlayItemMotionClientRpc(
                new NetworkObjectReference(item.NetworkObject),
                startWorldPosition,
                startWorldRotation,
                targetRevision);
        }

        internal bool TryGetDefaultDropPoseServer(
            NetworkWorldItem item,
            NetworkCarryable carryable,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                transform.forward,
                Vector3.up);

            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            float resolvedDropDistance = dropForwardDistance;

            if (preventPlayerOverlapOnDrop && item != null)
            {
                resolvedDropDistance = Mathf.Max(
                    resolvedDropDistance,
                    CalculateSafeDropDistance(item, forward));
            }

            Vector3 target = transform.position +
                             forward * resolvedDropDistance;
            Vector3 probeOrigin = target + Vector3.up * groundProbeHeight;

            float clearance = 0.05f;
            NetworkItemPresentation presentation = item != null
                ? item.GetComponent<NetworkItemPresentation>()
                : null;

            if (presentation != null)
            {
                clearance = presentation.PlacementClearance;
            }

            if (TryFindDropSurface(probeOrigin, item, out RaycastHit hit))
            {
                worldPosition = hit.point + hit.normal * clearance;
            }
            else
            {
                worldPosition = target + Vector3.up * clearance;
            }

            worldRotation = Quaternion.LookRotation(forward, Vector3.up) *
                            (carryable != null
                                ? carryable.DropRotationOffset
                                : Quaternion.identity);

            return NetworkInteractionValidation.IsFinite(worldPosition) &&
                   IsFinite(worldRotation);
        }

        private List<ReflowMotion> CaptureReflowMotions(int firstOldIndex)
        {
            var result = new List<ReflowMotion>(
                Mathf.Max(0, HeldItemCount - firstOldIndex));

            for (int i = firstOldIndex; i < HeldItemCount; i++)
            {
                if (!TryGetHeldItemAt(i, out NetworkWorldItem shiftedItem) ||
                    !shiftedItem.TryGetComponent(
                        out NetworkItemMotionPresenter presenter) ||
                    !presenter.TryCaptureCurrentVisualPose(
                        out Vector3 startPosition,
                        out Quaternion startRotation))
                {
                    continue;
                }

                result.Add(new ReflowMotion(
                    shiftedItem,
                    presenter,
                    startPosition,
                    startRotation));
            }

            return result;
        }

        private void ReflowStackServer(
            int firstNewIndex,
            IReadOnlyList<ReflowMotion> motions)
        {
            if (!CanWriteStackServer())
            {
                return;
            }

            for (int i = firstNewIndex; i < HeldItemCount; i++)
            {
                if (!TryGetHeldItemAt(i, out NetworkWorldItem shiftedItem) ||
                    !shiftedItem.TryGetComponent(
                        out NetworkCarryable carryable) ||
                    !TryGetCarryLocalPose(
                        carryable,
                        i,
                        out Vector3 localPosition,
                        out Quaternion localRotation))
                {
                    continue;
                }

                shiftedItem.transform.localPosition = localPosition;
                shiftedItem.transform.localRotation = localRotation;

                uint revision = shiftedItem.NextRevisionServer();
                shiftedItem.SetLocationServer(
                    NetworkItemLocationState.Held(
                        OwnerClientId,
                        revision));

                for (int motionIndex = 0;
                     motionIndex < motions.Count;
                     motionIndex++)
                {
                    ReflowMotion motion = motions[motionIndex];

                    if (motion.NetworkObjectId !=
                        shiftedItem.NetworkObjectId)
                    {
                        continue;
                    }

                    if (motion.Presenter != null)
                    {
                        BroadcastItemMotionServer(
                            shiftedItem,
                            motion.StartPosition,
                            motion.StartRotation,
                            revision);
                    }

                    break;
                }
            }
        }

        private bool CanWriteStackServer()
        {
            return NetworkManager != null &&
                   NetworkManager.IsServer;
        }

        private float CalculateSafeDropDistance(
            NetworkWorldItem item,
            Vector3 forward)
        {
            const float fallbackPlayerReach = 0.45f;
            const float fallbackItemRadius = 0.3f;
            float playerReach = 0f;

            if (playerBodyColliders != null)
            {
                for (int i = 0; i < playerBodyColliders.Length; i++)
                {
                    Collider bodyCollider = playerBodyColliders[i];
                    if (!IsUsablePlayerBodyCollider(bodyCollider, item))
                    {
                        continue;
                    }

                    Bounds bounds = bodyCollider.bounds;
                    Vector3 extents = bounds.extents;
                    float projectedExtent =
                        Mathf.Abs(forward.x) * extents.x +
                        Mathf.Abs(forward.y) * extents.y +
                        Mathf.Abs(forward.z) * extents.z;
                    float projectedCenter = Vector3.Dot(
                        bounds.center - transform.position,
                        forward);

                    playerReach = Mathf.Max(
                        playerReach,
                        projectedCenter + projectedExtent);
                }
            }

            playerReach = Mathf.Max(fallbackPlayerReach, playerReach);
            float itemRadius = Mathf.Max(
                fallbackItemRadius,
                CalculateItemHorizontalRadius(item));
            return playerReach + itemRadius + playerDropPadding;
        }

        private static float CalculateItemHorizontalRadius(
            NetworkWorldItem item)
        {
            if (item == null)
            {
                return 0f;
            }

            float radius = 0f;
            Renderer[] renderers =
                item.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    radius = Mathf.Max(
                        radius,
                        renderers[i].bounds.extents.magnitude);
                }
            }

            return radius;
        }

        private bool IsUsablePlayerBodyCollider(
            Collider bodyCollider,
            NetworkWorldItem heldItem)
        {
            if (bodyCollider == null || !bodyCollider.enabled ||
                bodyCollider.isTrigger ||
                !bodyCollider.gameObject.activeInHierarchy)
            {
                return false;
            }

            return heldItem == null ||
                   !bodyCollider.transform.IsChildOf(heldItem.transform);
        }

        private bool TryFindDropSurface(
            Vector3 probeOrigin,
            NetworkWorldItem heldItem,
            out RaycastHit bestHit)
        {
            bestHit = default;
            float bestDistance = float.PositiveInfinity;
            RaycastHit[] hits = Physics.RaycastAll(
                probeOrigin,
                Vector3.down,
                groundProbeDistance,
                dropSurfaceMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null ||
                    ShouldIgnoreDropSurface(hit.collider, heldItem) ||
                    hit.distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hit.distance;
                bestHit = hit;
            }

            return bestDistance < float.PositiveInfinity;
        }

        private bool ShouldIgnoreDropSurface(
            Collider candidate,
            NetworkWorldItem heldItem)
        {
            if (candidate == null)
            {
                return true;
            }

            if (heldItem != null &&
                candidate.transform.IsChildOf(heldItem.transform))
            {
                return true;
            }

            if (playerBodyColliders != null)
            {
                for (int i = 0; i < playerBodyColliders.Length; i++)
                {
                    if (playerBodyColliders[i] == candidate)
                    {
                        return true;
                    }
                }
            }

            return candidate.GetComponentInParent<NetworkItemCarrier>() != null;
        }

        [ServerRpc(RequireOwnership = true)]
        private void RequestDropServerRpc(ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
            {
                return;
            }

            if (!NetworkItemTransferService.TryDrop(
                    this,
                    out string rejectionMessage))
            {
                Debug.LogWarning(
                    $"[ItemCarrier] Drop rejected: {rejectionMessage}",
                    this);
            }
        }

        [ClientRpc]
        private void PlayItemMotionClientRpc(
            NetworkObjectReference itemReference,
            Vector3 startWorldPosition,
            Quaternion startWorldRotation,
            uint targetRevision)
        {
            if (NetworkManager == null ||
                !itemReference.TryGet(
                    out NetworkObject itemObject,
                    NetworkManager) ||
                !itemObject.TryGetComponent(
                    out NetworkItemMotionPresenter presenter))
            {
                return;
            }

            presenter.PlayTransitionLocal(
                startWorldPosition,
                startWorldRotation,
                targetRevision);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer || clientId != OwnerClientId || !HasHeldItem)
            {
                return;
            }

            NetworkItemTransferService.TryDropAll(
                this,
                out string message,
                true);

            if (!string.IsNullOrWhiteSpace(message))
            {
                Debug.Log($"[ItemCarrier] {message}", this);
            }
        }

        private void HandleHeldItemsChanged(
            NetworkListEvent<NetworkObjectReference> changeEvent)
        {
            RefreshTopItemCache();
            HeldStackChanged?.Invoke();
        }

        private void HandleCarryLimitChanged(int previous, int current)
        {
            CarryLimitChanged?.Invoke(previous, current);
        }

        private void RefreshTopItemCache()
        {
            NetworkWorldItem previous = _cachedTopItem;
            TryGetHeldItem(out _cachedTopItem);

            if (previous != _cachedTopItem)
            {
                HeldItemChanged?.Invoke(previous, _cachedTopItem);
            }
        }

        private void ValidateConfiguration()
        {
            defaultCarryLimit = Mathf.Max(1, defaultCarryLimit);
            dropForwardDistance = Mathf.Max(0.2f, dropForwardDistance);
            groundProbeHeight = Mathf.Max(0.1f, groundProbeHeight);
            groundProbeDistance = Mathf.Max(0.1f, groundProbeDistance);
            playerDropPadding = Mathf.Max(0f, playerDropPadding);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                   IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

#if UNITY_EDITOR
        public void EditorAssignCarryAnchor(Transform anchor)
        {
            itemCarryAnchor = anchor;
        }
#endif
    }
}
