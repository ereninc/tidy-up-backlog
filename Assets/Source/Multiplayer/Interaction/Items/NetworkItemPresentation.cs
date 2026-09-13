using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Rebuilds local collision/physics presentation from replicated item state.
    /// It owns no rules and sends no network messages.
    /// </summary>
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Presentation")]
    public sealed class NetworkItemPresentation : MonoBehaviour
    {
        [InfoBox(
            "Held items disable their world colliders and physics. World and " +
            "placed items restore them. Keep normal snap-only items free of a " +
            "NetworkTransform; add network physics as an explicit capability.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private NetworkWorldItem item;

        [TitleGroup("References")]
        [SerializeField] private Rigidbody targetRigidbody;

        [TitleGroup("References")]
        [SerializeField] private Collider[] worldColliders;

        [TitleGroup("Physics")]
        [Tooltip(
            "Enable only for items that intentionally simulate on the server. " +
            "A production physics item should also use NGO NetworkRigidbody and " +
            "NetworkTransform.")]
        [SerializeField] private bool simulatePhysicsWhileInWorld;

        [TitleGroup("Placement")]
        [MinValue(0f)]
        [Tooltip("Zero calculates clearance from enabled colliders.")]
        [SerializeField] private float placementClearanceOverride;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public float PlacementClearance => placementClearanceOverride > 0f
            ? placementClearanceOverride
            : _cachedPlacementClearance;

        private float _cachedPlacementClearance = 0.05f;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void Awake()
        {
            AutoAssignReferences();
            RefreshPlacementClearanceCache();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
            placementClearanceOverride = Mathf.Max(
                0f,
                placementClearanceOverride);
            RefreshPlacementClearanceCache();
        }

        private void OnEnable()
        {
            AutoAssignReferences();

            if (item != null)
            {
                item.LocationChanged += HandleLocationChanged;
                Apply(item.Location);
            }
        }

        private void OnDisable()
        {
            if (item != null)
            {
                item.LocationChanged -= HandleLocationChanged;
            }
        }

        [Button("AUTO ASSIGN REFERENCES")]
        public void AutoAssignReferences()
        {
            if (item == null)
            {
                item = GetComponent<NetworkWorldItem>();
            }

            if (targetRigidbody == null)
            {
                targetRigidbody = GetComponent<Rigidbody>();
            }

            if (worldColliders == null || worldColliders.Length == 0)
            {
                worldColliders = GetComponentsInChildren<Collider>(true);
            }

            RefreshPlacementClearanceCache();
        }

        private void HandleLocationChanged(
            NetworkItemLocationState previous,
            NetworkItemLocationState current)
        {
            Apply(current);
        }

        private void Apply(NetworkItemLocationState state)
        {
            bool held = state.IsHeld;

            if (worldColliders != null)
            {
                for (int i = 0; i < worldColliders.Length; i++)
                {
                    Collider worldCollider = worldColliders[i];

                    if (worldCollider != null)
                    {
                        worldCollider.enabled = !held;
                    }
                }
            }

            if (targetRigidbody == null)
            {
                return;
            }

            bool shouldSimulate = !held &&
                                  state.IsWorld &&
                                  simulatePhysicsWhileInWorld;
            targetRigidbody.isKinematic = !shouldSimulate;

            if (!shouldSimulate)
            {
                targetRigidbody.Sleep();
            }
        }

        private void RefreshPlacementClearanceCache()
        {
            if (placementClearanceOverride > 0f)
            {
                _cachedPlacementClearance = placementClearanceOverride;
                return;
            }

            if (worldColliders == null || worldColliders.Length == 0)
            {
                _cachedPlacementClearance = 0.05f;
                return;
            }

            Bounds combined = default;
            bool hasBounds = false;

            for (int i = 0; i < worldColliders.Length; i++)
            {
                Collider worldCollider = worldColliders[i];

                if (worldCollider == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combined = worldCollider.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(worldCollider.bounds);
                }
            }

            float calculated = hasBounds
                ? Mathf.Max(0.01f, combined.extents.y)
                : 0.05f;

            // Disabled colliders can report empty bounds. Preserve the last valid
            // authored/world value while the item is held.
            if (calculated > 0.011f || _cachedPlacementClearance <= 0f)
            {
                _cachedPlacementClearance = calculated;
            }
        }
    }
}
