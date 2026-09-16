using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One-slot hand carried by a network player. The server owns the item and
    /// every transfer; the client only requests a drop.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Carrier")]
    public sealed class NetworkItemCarrier : NetworkBehaviour
    {
        private readonly NetworkVariable<NetworkObjectReference> _heldItem =
            new NetworkVariable<NetworkObjectReference>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "ItemCarryAnchor may be a normal child under the camera/player. NGO " +
            "parents the item NetworkObject to the player NetworkObject root and " +
            "uses this anchor only to calculate the replicated local grip pose.")]
        [TitleGroup("Carry Pose")]
        [Required]
        [SerializeField] private Transform itemCarryAnchor;

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
        [Tooltip(
            "Keeps the final drop pose outside this player's body instead of " +
            "letting the item spawn inside the movement collider.")]
        [SerializeField] private bool preventPlayerOverlapOnDrop = true;

        [TitleGroup("Drop Safety")]
        [MinValue(0f)]
        [SuffixLabel("m")]
        [SerializeField] private float playerDropPadding = 0.08f;

        [TitleGroup("Drop Safety")]
        [Tooltip(
            "Player movement/body colliders. Trigger colliders are ignored. " +
            "AUTO ASSIGN CARRY ANCHOR fills this from the player hierarchy.")]
        [SerializeField] private Collider[] playerBodyColliders;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Carrier")]
        public static NetworkItemCarrier Local { get; private set; }

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Carrier")]
        public bool HasHeldItem => TryGetHeldItem(out _);

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Carrier")]
        public int CarryLimit => 1;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Carrier")]
        public int HeldItemCount => HasHeldItem ? 1 : 0;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Carrier")]
        public string HeldItemName => TryGetHeldItem(out NetworkWorldItem item)
            ? item.DisplayName
            : "Empty";

        public string DropKeyDisplayName => dropKey.ToString();
        public Transform ItemCarryAnchor => itemCarryAnchor;

        public event Action<NetworkWorldItem, NetworkWorldItem> HeldItemChanged;

        private NetworkWorldItem _cachedHeldItem;

        private bool CanDropFromInspector =>
            Application.isPlaying && IsSpawned && IsOwner &&
            !GameplayInputGate.IsBlocked && HasHeldItem;

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
            _heldItem.OnValueChanged += HandleHeldItemReferenceChanged;

            if (IsOwner)
            {
                Local = this;
            }

            RefreshHeldItemCache();

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback +=
                    HandleClientDisconnected;
            }
        }

        public override void OnNetworkDespawn()
        {
            // Fallback for abrupt disconnects where the player NetworkObject is
            // despawned before the manager-level disconnect callback reaches us.
            if (IsServer && NetworkManager != null &&
                NetworkManager.IsListening &&
                TryGetHeldItemForServerCleanup(out _))
            {
                NetworkItemTransferService.TryDrop(
                    this,
                    out string cleanupMessage,
                    true);

                if (!string.IsNullOrWhiteSpace(cleanupMessage))
                {
                    Debug.Log(
                        $"[ItemCarrier] {cleanupMessage}",
                        this);
                }
            }

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -=
                    HandleClientDisconnected;
            }

            _heldItem.OnValueChanged -= HandleHeldItemReferenceChanged;

            if (Local == this)
            {
                Local = null;
            }

            NetworkWorldItem previous = _cachedHeldItem;
            _cachedHeldItem = null;
            HeldItemChanged?.Invoke(previous, null);
            base.OnNetworkDespawn();
        }

        public override void OnLostOwnership()
        {
            // NGO also invokes this on the server when a client loses ownership.
            // It is the earliest reliable abrupt-disconnect cleanup hook for the
            // client-owned player object.
            if (IsServer && NetworkManager != null &&
                NetworkManager.IsListening &&
                TryGetHeldItemForServerCleanup(out _))
            {
                NetworkItemTransferService.TryDrop(
                    this,
                    out _,
                    true);
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

        public bool TryGetHeldItem(out NetworkWorldItem item)
        {
            item = null;

            if (!IsSpawned || NetworkManager == null ||
                !_heldItem.Value.TryGet(
                    out NetworkObject itemObject,
                    NetworkManager))
            {
                return false;
            }

            return itemObject.TryGetComponent(out item) && item != null;
        }

        internal bool TryGetHeldItemForServerCleanup(
            out NetworkWorldItem item)
        {
            item = null;

            if (NetworkManager == null || !NetworkManager.IsServer ||
                !_heldItem.Value.TryGet(
                    out NetworkObject itemObject,
                    NetworkManager))
            {
                return false;
            }

            return itemObject.TryGetComponent(out item) && item != null;
        }

        [Button("AUTO ASSIGN CARRY ANCHOR")]
        public void AutoAssignReferences()
        {
            if (itemCarryAnchor == null)
            {
                Transform existing = null;
                Transform[] children =
                    GetComponentsInChildren<Transform>(true);

                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].name == "ItemCarryAnchor")
                    {
                        existing = children[i];
                        break;
                    }
                }

                if (existing != null)
                {
                    itemCarryAnchor = existing;
                }
            }

            if (playerBodyColliders == null ||
                playerBodyColliders.Length == 0)
            {
                playerBodyColliders =
                    GetComponentsInChildren<Collider>(true);
            }
        }

        [Button("DROP HELD ITEM")]
        [EnableIf(nameof(CanDropFromInspector))]
        public void RequestDropFromInspector()
        {
            if (CanDropFromInspector)
            {
                RequestDropServerRpc();
            }
        }

        internal void SetHeldItemServer(NetworkWorldItem item)
        {
            if (!IsServer || item == null)
            {
                return;
            }

            _heldItem.Value = new NetworkObjectReference(item.NetworkObject);
        }

        internal void ClearHeldItemServer(NetworkWorldItem expectedItem)
        {
            if (!IsServer)
            {
                return;
            }

            if (expectedItem != null &&
                TryGetHeldItem(out NetworkWorldItem current) &&
                current != expectedItem)
            {
                return;
            }

            _heldItem.Value = default;
        }

        /// <summary>
        /// Sends one discrete motion cue through the already-existing player
        /// NetworkBehaviour. Every peer performs the visual interpolation
        /// locally; item transforms are not streamed per frame.
        /// </summary>
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

        internal bool TryGetCarryLocalPose(
            NetworkCarryable carryable,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            Transform anchor = itemCarryAnchor != null
                ? itemCarryAnchor
                : transform;

            Vector3 worldPosition = anchor.TransformPoint(
                carryable != null
                    ? carryable.GripPositionOffset
                    : Vector3.zero);
            Quaternion worldRotation = anchor.rotation *
                                       (carryable != null
                                           ? carryable.GripRotationOffset
                                           : Quaternion.identity);

            localPosition = transform.InverseTransformPoint(worldPosition);
            localRotation = Quaternion.Inverse(transform.rotation) *
                            worldRotation;

            return NetworkInteractionValidation.IsFinite(localPosition) &&
                   IsFinite(localRotation);
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

            if (TryFindDropSurface(
                    probeOrigin,
                    item,
                    out RaycastHit hit))
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

            playerReach = Mathf.Max(
                fallbackPlayerReach,
                playerReach);

            float itemRadius = CalculateItemHorizontalRadius(item);
            itemRadius = Mathf.Max(fallbackItemRadius, itemRadius);

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
                Renderer itemRenderer = renderers[i];

                if (itemRenderer == null)
                {
                    continue;
                }

                // Use the full bounding-sphere radius because the case may be
                // upright in hand and flat after applying DropRotationOffset.
                float horizontalRadius =
                    itemRenderer.bounds.extents.magnitude;

                radius = Mathf.Max(radius, horizontalRadius);
            }

            return radius;
        }

        private bool IsUsablePlayerBodyCollider(
            Collider bodyCollider,
            NetworkWorldItem heldItem)
        {
            if (bodyCollider == null ||
                !bodyCollider.enabled ||
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

            // A player body must never be interpreted as the floor under a
            // dropped item. This also prevents dropping onto another player.
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

            NetworkItemTransferService.TryDrop(
                this,
                out string rejectionMessage,
                true);

            if (!string.IsNullOrWhiteSpace(rejectionMessage))
            {
                Debug.LogWarning(
                    "[ItemCarrier] Disconnect cleanup could not drop item: " +
                    rejectionMessage,
                    this);
            }
        }

        private void HandleHeldItemReferenceChanged(
            NetworkObjectReference previous,
            NetworkObjectReference current)
        {
            RefreshHeldItemCache();
        }

        private void RefreshHeldItemCache()
        {
            NetworkWorldItem previous = _cachedHeldItem;
            TryGetHeldItem(out _cachedHeldItem);

            if (previous != _cachedHeldItem)
            {
                HeldItemChanged?.Invoke(previous, _cachedHeldItem);
            }
        }

        private void ValidateConfiguration()
        {
            dropForwardDistance = Mathf.Max(0.2f, dropForwardDistance);
            groundProbeHeight = Mathf.Max(0.1f, groundProbeHeight);
            groundProbeDistance = Mathf.Max(0.1f, groundProbeDistance);
            playerDropPadding = Mathf.Max(0f, playerDropPadding);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
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