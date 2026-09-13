using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Fixed snap slots for shelves, racks, machine sockets and display stands.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Destinations/Item Slot Destination")]
    public sealed class NetworkItemSlotDestination : NetworkItemDestination
    {
        [InfoBox(
            "Each ordinary child Transform is one snap slot. Items parent to the " +
            "receiver NetworkObject root, then use the selected slot's local pose.")]
        [SerializeField]
        [ListDrawerSettings(Expanded = true)]
        private Transform[] slots = System.Array.Empty<Transform>();

        [SerializeField] private string actionLabel = "Place";

        public override string ActionLabel =>
            string.IsNullOrWhiteSpace(actionLabel) ? "Place" : actionLabel;
        public override int Capacity => slots != null ? slots.Length : 0;
        public override int OccupiedCount
        {
            get
            {
                EnsureOccupants();
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
        }

        private NetworkWorldItem[] _occupants =
            System.Array.Empty<NetworkWorldItem>();

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
            EnsureOccupants();

            if (receiver == null || Capacity == 0)
            {
                rejectionMessage = "Receiver has no configured slots.";
                return false;
            }

            int freeIndex = -1;

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (_occupants[i] == null && slots[i] != null)
                {
                    freeIndex = i;
                    break;
                }
            }

            if (freeIndex < 0)
            {
                rejectionMessage = "Every slot is occupied.";
                return false;
            }

            Transform slot = slots[freeIndex];
            Transform receiverTransform = receiver.transform;
            float clearance = 0.05f;
            NetworkItemPresentation presentation = item != null
                ? item.GetComponent<NetworkItemPresentation>()
                : null;

            if (presentation != null)
            {
                clearance = presentation.PlacementClearance;
            }

            Vector3 worldPosition = slot.position + slot.up * clearance;
            Vector3 localPosition = receiverTransform.InverseTransformPoint(
                worldPosition);
            Quaternion localRotation =
                Quaternion.Inverse(receiverTransform.rotation) * slot.rotation;

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
            EnsureOccupants();

            if (plan.SlotIndex >= 0 &&
                plan.SlotIndex < _occupants.Length)
            {
                _occupants[plan.SlotIndex] = item;
            }
        }

        public override void ReleaseServer(NetworkWorldItem item)
        {
            EnsureOccupants();

            for (int i = 0; i < _occupants.Length; i++)
            {
                if (_occupants[i] == item)
                {
                    _occupants[i] = null;
                }
            }
        }

        private void EnsureOccupants()
        {
            int targetLength = slots != null ? slots.Length : 0;

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

#if UNITY_EDITOR
        public void EditorConfigure(Transform[] configuredSlots)
        {
            slots = configuredSlots ?? System.Array.Empty<Transform>();
            EnsureOccupants();
        }
#endif
    }
}
