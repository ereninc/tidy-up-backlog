using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Opt-in capability that makes a NetworkWorldItem hand-carryable.
    /// Offsets are relative to the player's ordinary ItemCarryAnchor transform.
    /// </summary>
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Carryable")]
    public sealed class NetworkCarryable : MonoBehaviour
    {
        [TitleGroup("Grip Pose")]
        [SerializeField] private Vector3 gripPositionOffset;

        [TitleGroup("Grip Pose")]
        [SerializeField] private Vector3 gripEulerOffset;

        [TitleGroup("Drop Pose")]
        [SerializeField] private Vector3 dropEulerOffset;

        [TitleGroup("Rules")]
        [Tooltip("Allows an item already placed on a shelf/table to be picked up.")]
        [SerializeField] private bool canPickUpFromPlacement = true;

        public Vector3 GripPositionOffset => gripPositionOffset;
        public Quaternion GripRotationOffset =>
            Quaternion.Euler(gripEulerOffset);
        public Quaternion DropRotationOffset =>
            Quaternion.Euler(dropEulerOffset);

        public bool CanPickUpServer(
            NetworkWorldItem item,
            out string rejectionMessage)
        {
            rejectionMessage = string.Empty;

            if (item == null || !item.IsSpawned)
            {
                rejectionMessage = "Item is not network-spawned.";
                return false;
            }

            if (item.IsHeld)
            {
                rejectionMessage = "Item is already being carried.";
                return false;
            }

            if (item.IsPlaced && !canPickUpFromPlacement)
            {
                rejectionMessage = "Item is locked in its placement.";
                return false;
            }

            return true;
        }
    }
}
