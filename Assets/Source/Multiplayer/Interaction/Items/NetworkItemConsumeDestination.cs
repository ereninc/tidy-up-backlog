using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Stateless destination for bins, sell points and destructive processors.
    /// A future economy component can listen around the same server transaction.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Destinations/Item Consume Destination")]
    public sealed class NetworkItemConsumeDestination : NetworkItemDestination
    {
        [SerializeField] private string actionLabel = "Discard";

        public override string ActionLabel =>
            string.IsNullOrWhiteSpace(actionLabel) ? "Discard" : actionLabel;
        public override int Capacity => int.MaxValue;
        public override int OccupiedCount => 0;

        public override bool TryBuildPlanServer(
            NetworkItemReceiver receiver,
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            NetworkInteractionContext context,
            out NetworkItemPlacementPlan plan,
            out string rejectionMessage)
        {
            plan = NetworkItemPlacementPlan.Consume();
            rejectionMessage = string.Empty;
            return item != null;
        }

        public override void CommitServer(
            NetworkWorldItem item,
            NetworkItemPlacementPlan plan)
        {
        }

        public override void ReleaseServer(NetworkWorldItem item)
        {
        }
    }
}
