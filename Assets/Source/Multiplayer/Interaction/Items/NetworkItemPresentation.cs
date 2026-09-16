using System.Collections;
using System.Collections.Generic;
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

        [TitleGroup("Drop Collision Grace")]
        [Tooltip(
            "Temporarily ignores collisions only between a dropped item and " +
            "the player who dropped it. Ground, shelves, other items and " +
            "other players are unaffected.")]
        [SerializeField] private bool ignoreDroppingPlayerCollision = true;

        [TitleGroup("Drop Collision Grace")]
        [MinValue(0f)]
        [SuffixLabel("s")]
        [SerializeField] private float minimumDropCollisionGrace = 0.2f;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public float PlacementClearance => placementClearanceOverride > 0f
            ? placementClearanceOverride
            : _cachedPlacementClearance;

        private float _cachedPlacementClearance = 0.05f;
        private Coroutine _dropCollisionGraceRoutine;

        private readonly List<IgnoredCollisionPair>
            _ignoredDropperCollisionPairs =
                new List<IgnoredCollisionPair>();

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
            minimumDropCollisionGrace = Mathf.Max(
                0f,
                minimumDropCollisionGrace);
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
            CancelDropCollisionGrace();

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

            if (current.IsHeld)
            {
                CancelDropCollisionGrace();
                return;
            }

            if (previous.IsHeld && current.IsWorld)
            {
                BeginDropCollisionGrace(previous.HolderClientId);
            }
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

        private void BeginDropCollisionGrace(ulong droppingClientId)
        {
            CancelDropCollisionGrace();

            if (!ignoreDroppingPlayerCollision ||
                droppingClientId == NetworkItemLocationState.NoClient ||
                worldColliders == null || worldColliders.Length == 0)
            {
                return;
            }

            NetworkItemCarrier droppingCarrier = FindCarrier(droppingClientId);

            if (droppingCarrier == null)
            {
                return;
            }

            Collider[] playerColliders =
                droppingCarrier.GetComponentsInChildren<Collider>(true);

            for (int itemIndex = 0;
                 itemIndex < worldColliders.Length;
                 itemIndex++)
            {
                Collider itemCollider = worldColliders[itemIndex];

                if (itemCollider == null || itemCollider.isTrigger)
                {
                    continue;
                }

                for (int playerIndex = 0;
                     playerIndex < playerColliders.Length;
                     playerIndex++)
                {
                    Collider playerCollider = playerColliders[playerIndex];

                    if (playerCollider == null ||
                        playerCollider == itemCollider ||
                        playerCollider.isTrigger ||
                        playerCollider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(
                        itemCollider,
                        playerCollider,
                        true);

                    _ignoredDropperCollisionPairs.Add(
                        new IgnoredCollisionPair(
                            itemCollider,
                            playerCollider));
                }
            }

            if (_ignoredDropperCollisionPairs.Count == 0)
            {
                return;
            }

            _dropCollisionGraceRoutine = StartCoroutine(
                RestoreDropperCollisionWhenClear());
        }

        private static NetworkItemCarrier FindCarrier(ulong ownerClientId)
        {
            NetworkItemCarrier[] carriers =
                FindObjectsByType<NetworkItemCarrier>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            for (int i = 0; i < carriers.Length; i++)
            {
                NetworkItemCarrier carrier = carriers[i];

                if (carrier != null &&
                    carrier.IsSpawned &&
                    carrier.OwnerClientId == ownerClientId)
                {
                    return carrier;
                }
            }

            return null;
        }

        private IEnumerator RestoreDropperCollisionWhenClear()
        {
            float startedAt = Time.time;

            while (Time.time - startedAt < minimumDropCollisionGrace)
            {
                yield return new WaitForFixedUpdate();
            }

            while (IsTouchingDroppingPlayer())
            {
                yield return new WaitForFixedUpdate();
            }

            _dropCollisionGraceRoutine = null;
            RestoreIgnoredDropperCollisions();
        }

        private bool IsTouchingDroppingPlayer()
        {
            for (int i = 0;
                 i < _ignoredDropperCollisionPairs.Count;
                 i++)
            {
                IgnoredCollisionPair pair =
                    _ignoredDropperCollisionPairs[i];

                Collider itemCollider = pair.ItemCollider;
                Collider playerCollider = pair.PlayerCollider;

                if (itemCollider == null ||
                    playerCollider == null ||
                    !itemCollider.enabled ||
                    !playerCollider.enabled)
                {
                    continue;
                }

                if (itemCollider.bounds.Intersects(playerCollider.bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private void CancelDropCollisionGrace()
        {
            if (_dropCollisionGraceRoutine != null)
            {
                StopCoroutine(_dropCollisionGraceRoutine);
                _dropCollisionGraceRoutine = null;
            }

            RestoreIgnoredDropperCollisions();
        }

        private void RestoreIgnoredDropperCollisions()
        {
            for (int i = 0;
                 i < _ignoredDropperCollisionPairs.Count;
                 i++)
            {
                IgnoredCollisionPair pair =
                    _ignoredDropperCollisionPairs[i];

                if (pair.ItemCollider != null &&
                    pair.PlayerCollider != null)
                {
                    Physics.IgnoreCollision(
                        pair.ItemCollider,
                        pair.PlayerCollider,
                        false);
                }
            }

            _ignoredDropperCollisionPairs.Clear();
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

        private readonly struct IgnoredCollisionPair
        {
            public IgnoredCollisionPair(
                Collider itemCollider,
                Collider playerCollider)
            {
                ItemCollider = itemCollider;
                PlayerCollider = playerCollider;
            }

            public Collider ItemCollider { get; }
            public Collider PlayerCollider { get; }
        }
    }
}