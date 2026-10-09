using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Receiver")]
    public sealed class NetworkItemReceiver : NetworkInteractable
    {
        private readonly NetworkVariable<int> _occupiedCount =
            new NetworkVariable<int>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "A receiver scans the carry stack from top to bottom and chooses " +
            "the first item its filter/destination accepts.")]
        [TitleGroup("Receiver")]
        [SerializeField] private string receiverDisplayName = "Item Receiver";

        [TitleGroup("Receiver")]
        [SerializeField] private NetworkItemAcceptanceFilter acceptanceFilter;

        [TitleGroup("Receiver"), Required]
        [SerializeField] private NetworkItemDestination destination;

        [TitleGroup("Receiver")]
        [Tooltip(
            "Selection volumes that may be bypassed when targeting this shelf's " +
            "pickable cases. Do not include physical shelf colliders. If empty, " +
            "legacy shelves use a sole root BoxCollider on a renderer-free slot.")]
        [SerializeField] private Collider[] interactionColliders;

        private Collider _legacyInteractionCollider;

        [ShowInInspector, ReadOnly, BoxGroup("Live Receiver")]
        public int OccupiedCount => IsSpawned
            ? _occupiedCount.Value
            : destination != null ? destination.OccupiedCount : 0;

        [ShowInInspector, ReadOnly, BoxGroup("Live Receiver")]
        public int Capacity => destination != null
            ? destination.Capacity
            : 0;

        public NetworkItemDestination Destination => destination;

        protected override void Reset()
        {
            base.Reset();
            AutoAssignDestination();
            ResolveLegacyInteractionCollider();
        }

        protected override void Awake()
        {
            base.Awake();
            AutoAssignDestination();
            ResolveLegacyInteractionCollider();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignDestination();
            ResolveLegacyInteractionCollider();
        }

        internal bool IsShelfInteractionCollider(Collider candidate)
        {
            if (candidate == null ||
                !(destination is NetworkGameCaseShelfDestination))
            {
                return false;
            }

            if (interactionColliders == null || interactionColliders.Length == 0)
            {
                return candidate == _legacyInteractionCollider;
            }

            for (int i = 0; i < interactionColliders.Length; i++)
            {
                if (interactionColliders[i] == candidate &&
                    candidate.GetComponentInParent<NetworkItemReceiver>() == this)
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolveLegacyInteractionCollider()
        {
            _legacyInteractionCollider = null;

            if ((interactionColliders != null && interactionColliders.Length > 0) ||
                !(destination is NetworkGameCaseShelfDestination) ||
                GetComponentInChildren<Renderer>(true) != null ||
                GetComponentInChildren<MeshFilter>(true) != null ||
                GetComponentInChildren<Rigidbody>(true) != null)
            {
                return;
            }

            Collider[] slotColliders = GetComponentsInChildren<Collider>(true);

            if (slotColliders.Length == 1 && slotColliders[0] is BoxCollider &&
                slotColliders[0].transform == transform)
            {
                _legacyInteractionCollider = slotColliders[0];
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                RefreshOccupiedCountServer();
            }
        }

        public override string GetInteractionDisplayName(
            NetworkInteractionController interactor)
        {
            if (destination is NetworkGameCaseShelfDestination shelf &&
                shelf.EffectiveLockedAppId != 0)
            {
                return GameCaseSessionPlan.GetGameName(shelf.EffectiveLockedAppId);
            }

            return string.IsNullOrWhiteSpace(receiverDisplayName)
                ? base.GetInteractionDisplayName(interactor)
                : receiverDisplayName;
        }

        internal bool TryGetShelfPlacementCandidate(
            NetworkItemCarrier carrier,
            out int stackIndex,
            out NetworkWorldItem selectedItem,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            stackIndex = -1;
            selectedItem = null;
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (!isActiveAndEnabled || !IsSpawned || carrier == null ||
                !carrier.isActiveAndEnabled || !carrier.IsSpawned ||
                !(destination is NetworkGameCaseShelfDestination shelf))
            {
                return false;
            }

            for (int i = carrier.HeldItemCount - 1; i >= 0; i--)
            {
                if (!carrier.TryGetHeldItemAt(i, out NetworkWorldItem candidate) ||
                    candidate == null || !candidate.IsSpawned || !candidate.IsHeld ||
                    candidate.Location.HolderClientId != carrier.OwnerClientId ||
                    (acceptanceFilter != null && !acceptanceFilter.Accepts(candidate)) ||
                    !candidate.TryGetComponent(out NetworkGameCase gameCase) ||
                    !shelf.TryGetPreviewWorldPose(
                        gameCase, out worldPosition, out worldRotation))
                {
                    continue;
                }

                stackIndex = i;
                selectedItem = candidate;
                return true;
            }

            return false;
        }

        public override string GetInteractionPrompt(
            NetworkInteractionController interactor)
        {
            NetworkItemCarrier carrier = interactor != null
                ? interactor.GetComponent<NetworkItemCarrier>()
                : null;

            if (carrier == null)
            {
                return "Carrier Missing";
            }

            if (destination is NetworkGameCaseShelfDestination shelf &&
                shelf.SlotState != null && shelf.SlotState.IsComplete)
            {
                return "Shelf Complete";
            }

            if (!carrier.HasHeldItem)
            {
                return "Hold an Item";
            }

            if (destination is NetworkGameCaseShelfDestination &&
                !TryGetShelfPlacementCandidate(carrier, out _, out _, out _, out _))
            {
                return "No Matching Game";
            }

            return destination != null
                ? destination.ActionLabel
                : "Destination Missing";
        }

        protected override bool CanInteractServer(
            NetworkInteractionContext context,
            out string rejectionMessage)
        {
            NetworkItemCarrier carrier = context.PlayerController != null
                ? context.PlayerController.GetComponent<NetworkItemCarrier>()
                : null;

            if (carrier == null)
            {
                rejectionMessage = "Player has no NetworkItemCarrier.";
                return false;
            }

            return TryFindPlacementCandidateServer(
                carrier,
                context,
                out _,
                out _,
                out rejectionMessage);
        }

        protected override bool ExecuteInteractionServer(
            NetworkInteractionContext context,
            out string resultMessage)
        {
            NetworkItemCarrier carrier =
                context.PlayerController.GetComponent<NetworkItemCarrier>();

            return NetworkItemTransferService.TryReceive(
                carrier,
                this,
                context,
                out resultMessage);
        }

        internal bool TryFindPlacementCandidateServer(
            NetworkItemCarrier carrier,
            NetworkInteractionContext context,
            out NetworkWorldItem selectedItem,
            out NetworkItemPlacementPlan selectedPlan,
            out string rejectionMessage)
        {
            selectedItem = null;
            selectedPlan = default;
            rejectionMessage = "No carried item fits this receiver.";

            if (carrier == null || !carrier.HasHeldItem)
            {
                rejectionMessage = "Hold an item first.";
                return false;
            }

            string firstRejection = string.Empty;

            // Top-to-bottom: empty slots naturally consume the visible top,
            // while a locked game shelf can reach a matching case underneath.
            for (int i = carrier.HeldItemCount - 1; i >= 0; i--)
            {
                if (!carrier.TryGetHeldItemAt(
                        i,
                        out NetworkWorldItem candidate))
                {
                    continue;
                }

                if (TryBuildPlacementPlanServer(
                        candidate,
                        carrier,
                        context,
                        out NetworkItemPlacementPlan plan,
                        out string candidateRejection))
                {
                    selectedItem = candidate;
                    selectedPlan = plan;
                    rejectionMessage = string.Empty;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(firstRejection) &&
                    !string.IsNullOrWhiteSpace(candidateRejection))
                {
                    firstRejection = candidateRejection;
                }
            }

            if (!string.IsNullOrWhiteSpace(firstRejection))
            {
                rejectionMessage = firstRejection;
            }

            return false;
        }

        internal bool TryBuildPlacementPlanServer(
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            NetworkInteractionContext context,
            out NetworkItemPlacementPlan plan,
            out string rejectionMessage)
        {
            plan = default;
            rejectionMessage = string.Empty;

            if (!IsServer || !IsSpawned)
            {
                rejectionMessage = "Item receiver is not spawned on the server.";
                return false;
            }

            if (destination == null)
            {
                rejectionMessage = "Receiver has no destination component.";
                return false;
            }

            if (acceptanceFilter != null &&
                !acceptanceFilter.Accepts(item, out rejectionMessage))
            {
                return false;
            }

            return destination.TryBuildPlanServer(
                this,
                item,
                carrier,
                context,
                out plan,
                out rejectionMessage);
        }

        internal void CommitPlacementServer(
            NetworkWorldItem item,
            NetworkItemPlacementPlan plan)
        {
            if (!IsServer || destination == null)
            {
                return;
            }

            destination.CommitServer(item, plan);
            RefreshOccupiedCountServer();
        }

        internal void ReleaseItemServer(NetworkWorldItem item)
        {
            if (!IsServer || destination == null)
            {
                return;
            }

            destination.ReleaseServer(item);
            RefreshOccupiedCountServer();
        }

        [Button("AUTO ASSIGN DESTINATION")]
        private void AutoAssignDestination()
        {
            if (destination == null)
            {
                destination = GetComponent<NetworkItemDestination>();
            }
        }

        private void RefreshOccupiedCountServer()
        {
            if (IsServer)
            {
                _occupiedCount.Value = destination != null
                    ? destination.OccupiedCount
                    : 0;
            }
        }

#if UNITY_EDITOR
        public Collider[] EditorInteractionColliders => interactionColliders;

        public void EditorConfigure(
            string displayName,
            NetworkItemDestination configuredDestination)
        {
            receiverDisplayName = displayName;
            destination = configuredDestination;
            ResolveLegacyInteractionCollider();
        }

        public void EditorConfigureInteractionColliders(Collider[] colliders)
        {
            interactionColliders = colliders;
            ResolveLegacyInteractionCollider();
        }
#endif
    }
}
