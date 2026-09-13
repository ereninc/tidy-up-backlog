using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Server-authoritative destination for one logical game shelf slot. The
    /// first case locks the slot to its AppId; subsequent cases must match.
    /// Positions are generated from one start point and a local step vector.
    /// </summary>
    [RequireComponent(typeof(NetworkItemReceiver))]
    [RequireComponent(typeof(NetworkGameCaseShelfSlotState))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Destination")]
    public sealed class NetworkGameCaseShelfDestination :
        NetworkItemDestination
    {
        [TitleGroup("Layout")]
        [SerializeField]
        [MinValue(1)]
        [MaxValue(256)]
        private int capacity = 10;

        [TitleGroup("Layout")]
        [Tooltip("Pose origin for shelf index zero. Place it at the case pivot.")]
        [SerializeField]
        private Transform startPoint;

        [TitleGroup("Layout")]
        [Tooltip("Offset in Start Point local space for every next case.")]
        [SerializeField]
        private Vector3 localStep = new Vector3(0.12f, 0f, 0f);

        [TitleGroup("Layout")]
        [SerializeField]
        private Vector3 firstItemLocalOffset;

        [TitleGroup("Layout")]
        [SerializeField]
        private Vector3 itemEulerOffset;

        [TitleGroup("Interaction")]
        [SerializeField]
        private string actionLabel = "Place Game";

        [TitleGroup("References")]
        [SerializeField]
        [Required]
        private NetworkGameCaseShelfSlotState slotState;

        [TitleGroup("Gizmos")]
        [SerializeField]
        private Vector3 previewCaseSize = new Vector3(0.11f, 0.18f, 0.025f);

        private NetworkWorldItem[] _occupants =
            System.Array.Empty<NetworkWorldItem>();

        public override string ActionLabel =>
            string.IsNullOrWhiteSpace(actionLabel)
                ? "Place Game"
                : actionLabel;

        public override int Capacity => Mathf.Max(1, capacity);

        public override int OccupiedCount
        {
            get
            {
                if (slotState != null && slotState.IsSpawned &&
                    !slotState.IsServer)
                {
                    return slotState.Snapshot.OccupiedCount;
                }

                EnsureOccupants();
                return CountOccupants();
            }
        }

        public NetworkGameCaseShelfSlotState SlotState => slotState;
        public Transform StartPoint => startPoint != null
            ? startPoint
            : transform;
        public Vector3 LocalStep => localStep;

        private void Reset()
        {
            ResolveReferences();
            EnsureOccupants();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureOccupants();
        }

        private void OnValidate()
        {
            capacity = Mathf.Clamp(capacity, 1, 256);
            previewCaseSize.x = Mathf.Max(0.001f, previewCaseSize.x);
            previewCaseSize.y = Mathf.Max(0.001f, previewCaseSize.y);
            previewCaseSize.z = Mathf.Max(0.001f, previewCaseSize.z);
            ResolveReferences();
            EnsureOccupants();
        }

        public override bool TryBuildPlanServer(
            NetworkItemReceiver receiver,
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            NetworkInteractionContext context,
            out NetworkItemPlacementPlan plan,
            out string rejectionMessage)
        {
            plan = default;
            rejectionMessage = string.Empty;
            ResolveReferences();
            EnsureOccupants();

            if (receiver == null || slotState == null ||
                !slotState.IsSpawned || !slotState.IsServer)
            {
                rejectionMessage = "Game shelf slot is not ready on the server.";
                return false;
            }

            if (item == null ||
                !item.TryGetComponent(out NetworkGameCase gameCase) ||
                !gameCase.IsBound)
            {
                rejectionMessage = "Only a configured game case fits here.";
                return false;
            }

            GameCaseShelfSlotSnapshot snapshot = slotState.Snapshot;

            if (snapshot.IsComplete)
            {
                rejectionMessage = "This game shelf slot is complete.";
                return false;
            }

            if (snapshot.LockedAppId != 0 &&
                snapshot.LockedAppId != gameCase.AppId)
            {
                rejectionMessage =
                    "This shelf slot is reserved for another game.";
                return false;
            }

            int freeIndex = FindFirstFreeIndex();

            if (freeIndex < 0)
            {
                rejectionMessage = "This game shelf slot is full.";
                return false;
            }

            BuildReceiverLocalPose(
                receiver.transform,
                freeIndex,
                out Vector3 localPosition,
                out Quaternion localRotation);

            plan = new NetworkItemPlacementPlan(
                NetworkItemDestinationDisposition.Place,
                localPosition,
                localRotation,
                freeIndex);
            return true;
        }

        public override void CommitServer(
            NetworkWorldItem item,
            NetworkItemPlacementPlan plan)
        {
            ResolveReferences();
            EnsureOccupants();

            if (slotState == null || !slotState.IsServer || item == null ||
                !item.TryGetComponent(out NetworkGameCase gameCase))
            {
                return;
            }

            int index = plan.SlotIndex;

            if (index < 0 || index >= _occupants.Length ||
                _occupants[index] != null)
            {
                Debug.LogError(
                    $"[GameCaseShelf] Invalid or occupied commit index {index}.",
                    this);
                return;
            }

            uint lockedAppId = slotState.Snapshot.LockedAppId;

            if (lockedAppId != 0 && lockedAppId != gameCase.AppId)
            {
                Debug.LogError(
                    "[GameCaseShelf] A mismatched game reached CommitServer.",
                    this);
                return;
            }

            _occupants[index] = item;
            PublishOccupancyServer(
                lockedAppId == 0 ? gameCase.AppId : lockedAppId);
        }

        public override void ReleaseServer(NetworkWorldItem item)
        {
            ResolveReferences();
            EnsureOccupants();

            if (slotState == null || !slotState.IsServer || item == null)
            {
                return;
            }

            if (slotState.Snapshot.IsComplete)
            {
                Debug.LogWarning(
                    "[GameCaseShelf] A completed slot received a release " +
                    "request. Completed slots are intended to be permanent.",
                    this);
                return;
            }

            bool removed = false;

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (_occupants[i] != item)
                {
                    continue;
                }

                _occupants[i] = null;
                removed = true;
                break;
            }

            if (removed)
            {
                PublishOccupancyServer(slotState.Snapshot.LockedAppId);
            }
        }

        /// <summary>
        /// Local, packet-free preview query used by the ghost presenter.
        /// </summary>
        public bool TryGetPreviewWorldPose(
            NetworkGameCase gameCase,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (gameCase == null || !gameCase.IsBound || slotState == null ||
                !slotState.IsSpawned)
            {
                return false;
            }

            GameCaseShelfSlotSnapshot snapshot = slotState.Snapshot;

            if (snapshot.IsComplete || snapshot.NextFreeIndex < 0 ||
                snapshot.NextFreeIndex >= Capacity)
            {
                return false;
            }

            if (snapshot.LockedAppId != 0 &&
                snapshot.LockedAppId != gameCase.AppId)
            {
                return false;
            }

            BuildWorldPose(
                snapshot.NextFreeIndex,
                out worldPosition,
                out worldRotation);
            return true;
        }

        public void GetWorldPose(
            int slotIndex,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            BuildWorldPose(
                Mathf.Clamp(slotIndex, 0, Capacity - 1),
                out worldPosition,
                out worldRotation);
        }

        private void PublishOccupancyServer(uint preferredLockedAppId)
        {
            int occupiedCount = CountOccupants();
            int nextFreeIndex = FindFirstFreeIndex();
            bool isComplete = occupiedCount >= Capacity;

            slotState.PublishServer(
                occupiedCount > 0 ? preferredLockedAppId : 0,
                occupiedCount,
                isComplete ? -1 : nextFreeIndex,
                isComplete);
        }

        private int CountOccupants()
        {
            int count = 0;

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (_occupants[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private int FindFirstFreeIndex()
        {
            for (int i = 0; i < _occupants.Length; i++)
            {
                if (_occupants[i] == null)
                {
                    return i;
                }
            }

            return -1;
        }

        private void BuildReceiverLocalPose(
            Transform receiverTransform,
            int slotIndex,
            out Vector3 localPosition,
            out Quaternion localRotation)
        {
            BuildWorldPose(
                slotIndex,
                out Vector3 worldPosition,
                out Quaternion worldRotation);

            localPosition = receiverTransform.InverseTransformPoint(
                worldPosition);
            localRotation = Quaternion.Inverse(receiverTransform.rotation) *
                            worldRotation;
        }

        private void BuildWorldPose(
            int slotIndex,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            Transform origin = StartPoint;
            Vector3 localPosition = firstItemLocalOffset +
                                    localStep * slotIndex;
            worldPosition = origin.TransformPoint(localPosition);
            worldRotation = origin.rotation *
                            Quaternion.Euler(itemEulerOffset);
        }

        private void ResolveReferences()
        {
            if (slotState == null)
            {
                slotState = GetComponent<NetworkGameCaseShelfSlotState>();
            }
        }

        private void EnsureOccupants()
        {
            int targetLength = Capacity;

            if (_occupants != null && _occupants.Length == targetLength)
            {
                return;
            }

            NetworkWorldItem[] previous = _occupants;
            _occupants = new NetworkWorldItem[targetLength];

            if (previous == null)
            {
                return;
            }

            int copyCount = Mathf.Min(previous.Length, _occupants.Length);

            for (int i = 0; i < copyCount; i++)
            {
                _occupants[i] = previous[i];
            }
        }

        private void OnDrawGizmosSelected()
        {
            Transform origin = StartPoint;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = origin.localToWorldMatrix;

            for (int i = 0; i < Capacity; i++)
            {
                Gizmos.color = i == 0
                    ? new Color(0.2f, 1f, 0.55f, 0.9f)
                    : new Color(0.2f, 0.75f, 1f, 0.65f);
                Gizmos.DrawWireCube(
                    firstItemLocalOffset + localStep * i,
                    previewCaseSize);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
