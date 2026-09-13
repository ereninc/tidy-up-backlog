using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Pluggable server-side destination strategy used by a receiver. Implement
    /// new station behavior without modifying carrier, item or interaction code.
    /// </summary>
    public abstract class NetworkItemDestination : MonoBehaviour
    {
        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public abstract string ActionLabel { get; }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public abstract int Capacity { get; }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public abstract int OccupiedCount { get; }

        public abstract bool TryBuildPlanServer(
            NetworkItemReceiver receiver,
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            NetworkInteractionContext context,
            out NetworkItemPlacementPlan plan,
            out string rejectionMessage);

        public abstract void CommitServer(
            NetworkWorldItem item,
            NetworkItemPlacementPlan plan);

        public abstract void ReleaseServer(NetworkWorldItem item);
    }
}
