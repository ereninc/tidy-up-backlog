using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Free placement at the validated hit point: tables, counters and floors.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Destinations/Item Surface Destination")]
    public sealed class NetworkItemSurfaceDestination :
        NetworkItemDestination
    {
        [InfoBox(
            "Placement uses the server raycast hit point and normal. This first " +
            "slice enforces capacity and upward-facing surfaces; add a collision " +
            "reservation strategy here when final item dimensions are known.")]
        [MinValue(1)]
        [SerializeField] private int capacity = 16;

        [Range(0f, 1f)]
        [SerializeField] private float minimumUpNormal = 0.65f;

        [MinValue(0f)]
        [SerializeField] private float extraSurfaceClearance = 0.01f;

        [SerializeField] private string actionLabel = "Place";

        public override string ActionLabel =>
            string.IsNullOrWhiteSpace(actionLabel) ? "Place" : actionLabel;
        public override int Capacity => Mathf.Max(1, capacity);
        public override int OccupiedCount => _occupants.Count;

        private readonly HashSet<NetworkWorldItem> _occupants =
            new HashSet<NetworkWorldItem>();

        private void OnValidate()
        {
            capacity = Mathf.Max(1, capacity);
            minimumUpNormal = Mathf.Clamp01(minimumUpNormal);
            extraSurfaceClearance = Mathf.Max(0f, extraSurfaceClearance);
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
            RemoveDestroyedOccupants();

            if (_occupants.Count >= Capacity)
            {
                rejectionMessage = "This surface is full.";
                return false;
            }

            Vector3 normal = context.HitNormal.normalized;

            if (!NetworkInteractionValidation.IsFinite(normal) ||
                normal.y < minimumUpNormal)
            {
                rejectionMessage = "Aim at the top of this surface.";
                return false;
            }

            float clearance = 0.05f;
            NetworkItemPresentation presentation = item != null
                ? item.GetComponent<NetworkItemPresentation>()
                : null;

            if (presentation != null)
            {
                clearance = presentation.PlacementClearance;
            }

            Vector3 worldPosition = context.HitPoint +
                                    normal *
                                    (clearance + extraSurfaceClearance);
            Vector3 flatForward = Vector3.ProjectOnPlane(
                carrier.transform.forward,
                Vector3.up);

            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }

            Vector3 surfaceForward = Vector3.ProjectOnPlane(
                flatForward,
                normal);

            if (surfaceForward.sqrMagnitude < 0.0001f)
            {
                surfaceForward = Vector3.ProjectOnPlane(
                    Vector3.forward,
                    normal);
            }

            Quaternion worldRotation = Quaternion.LookRotation(
                surfaceForward.normalized,
                normal);
            NetworkCarryable carryable = item != null
                ? item.GetComponent<NetworkCarryable>()
                : null;

            if (carryable != null)
            {
                worldRotation *= carryable.DropRotationOffset;
            }

            Transform receiverTransform = receiver.transform;
            Vector3 localPosition = receiverTransform.InverseTransformPoint(
                worldPosition);
            Quaternion localRotation =
                Quaternion.Inverse(receiverTransform.rotation) * worldRotation;

            plan = new NetworkItemPlacementPlan(
                NetworkItemDestinationDisposition.Place,
                localPosition,
                localRotation,
                NetworkItemLocationState.NoSlot);
            return true;
        }

        public override void CommitServer(
            NetworkWorldItem item,
            NetworkItemPlacementPlan plan)
        {
            if (item != null)
            {
                _occupants.Add(item);
            }
        }

        public override void ReleaseServer(NetworkWorldItem item)
        {
            if (item != null)
            {
                _occupants.Remove(item);
            }
        }

        private void RemoveDestroyedOccupants()
        {
            _occupants.RemoveWhere(item => item == null);
        }
    }
}
