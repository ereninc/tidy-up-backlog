using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Interactable station endpoint. Acceptance policy and destination behavior
    /// are composed through assets/components instead of item-type switches.
    /// </summary>
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
            "Receiver = interaction endpoint, Filter = accepted item semantics, " +
            "Destination = placement/consume strategy. Swap components instead " +
            "of editing the carrier or item code.")]
        [TitleGroup("Receiver")]
        [SerializeField] private string receiverDisplayName = "Item Receiver";

        [TitleGroup("Receiver")]
        [SerializeField] private NetworkItemAcceptanceFilter acceptanceFilter;

        [TitleGroup("Receiver")]
        [Required]
        [SerializeField] private NetworkItemDestination destination;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Receiver")]
        public int OccupiedCount => IsSpawned
            ? _occupiedCount.Value
            : destination != null
                ? destination.OccupiedCount
                : 0;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Receiver")]
        public int Capacity => destination != null
            ? destination.Capacity
            : 0;
        public NetworkItemDestination Destination => destination;

        protected override void Reset()
        {
            base.Reset();
            AutoAssignDestination();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignDestination();
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
            return string.IsNullOrWhiteSpace(receiverDisplayName)
                ? base.GetInteractionDisplayName(interactor)
                : receiverDisplayName;
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

            if (!carrier.HasHeldItem)
            {
                return "Hold an Item";
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

            if (carrier == null ||
                !carrier.TryGetHeldItem(out NetworkWorldItem item))
            {
                rejectionMessage = "Hold an item first.";
                return false;
            }

            return TryBuildPlacementPlanServer(
                item,
                carrier,
                context,
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
                rejectionMessage = "Receiver is not spawned on the server.";
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
        public void EditorConfigure(
            string displayName,
            NetworkItemDestination configuredDestination)
        {
            receiverDisplayName = displayName;
            destination = configuredDestination;
        }
#endif
    }
}
