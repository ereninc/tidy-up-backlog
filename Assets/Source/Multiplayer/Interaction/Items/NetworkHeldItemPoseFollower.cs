using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
	[RequireComponent(typeof(NetworkWorldItem))]
	[RequireComponent(typeof(NetworkCarryable))]
	[DisallowMultipleComponent]
	[AddComponentMenu("Multiplayer/Items/Network Held Item Pose Follower")]
	public sealed class NetworkHeldItemPoseFollower : MonoBehaviour
	{
		[InfoBox(
			"Packet-free stack alignment. Every peer derives this item's pose " +
			"from the replicated carrier stack index.")]
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
			if (!item || !carryable || !item.IsSpawned || !item.IsHeld)
			{
				return;
			}

			NetworkItemCarrier carrier =
				GetComponentInParent<NetworkItemCarrier>();

			if (!carrier ||
			    !carrier.TryGetStackIndex(item, out int stackIndex) ||
			    !carrier.TryGetCarryWorldPose(
				    carryable,
				    stackIndex,
				    out Vector3 worldPosition,
				    out Quaternion worldRotation))
			{
				return;
			}

			transform.SetPositionAndRotation(worldPosition, worldRotation);
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