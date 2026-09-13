using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Packet-free presentation alignment while held. NGO still synchronizes the
    /// authoritative parent relationship; each peer aligns its local copy to that
    /// player's ordinary camera/hand anchor every LateUpdate.
    /// </summary>
    [RequireComponent(typeof(NetworkWorldItem))]
    [RequireComponent(typeof(NetworkCarryable))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Held Item Pose Follower")]
    public sealed class NetworkHeldItemPoseFollower : MonoBehaviour
    {
        [InfoBox(
            "This is the target-follow presentation layer. It sends no packets. " +
            "The item remains parented to the player's NetworkObject root so NGO " +
            "can synchronize parent changes and late joiners safely.")]
        [Required]
        [SerializeField] private NetworkWorldItem item;

        [Required]
        [SerializeField] private NetworkCarryable carryable;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
        }

        private void LateUpdate()
        {
            if (item == null || carryable == null ||
                !item.IsSpawned || !item.IsHeld)
            {
                return;
            }

            NetworkItemCarrier carrier =
                GetComponentInParent<NetworkItemCarrier>();

            if (carrier == null || carrier.ItemCarryAnchor == null)
            {
                return;
            }

            Transform anchor = carrier.ItemCarryAnchor;
            transform.SetPositionAndRotation(
                anchor.TransformPoint(carryable.GripPositionOffset),
                anchor.rotation * carryable.GripRotationOffset);
        }

        [Button("AUTO ASSIGN REFERENCES")]
        public void AutoAssignReferences()
        {
            if (item == null)
            {
                item = GetComponent<NetworkWorldItem>();
            }

            if (carryable == null)
            {
                carryable = GetComponent<NetworkCarryable>();
            }
        }
    }
}
