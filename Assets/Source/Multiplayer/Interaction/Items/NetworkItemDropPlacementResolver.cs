using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Server-side drop query. A normal drop returns a short, clear release
    /// pose above the nearest supported surface; NetworkItemPhysicsSettler then
    /// lets only that item fall and settle. A swap may instead reuse the exact
    /// supported vacancy left by the item being picked up.
    /// </summary>
    internal static class NetworkItemDropPlacementResolver
    {
        private const int CastBufferSize = 96;
        private const int OverlapBufferSize = 128;
        private const int SearchRingCount = 2;
        private const int SearchDirections = 8;
        private const float CastSkin = 0.002f;
        private const float SurfaceSkin = 0.004f;
        private const float MaximumSlopeDegrees = 55f;
        private const float PhysicsReleaseHeight = 0.22f;
        private const float MaximumReleaseHeight = 0.40f;
        private const float ReleaseHeightStep = 0.06f;
        private const float MaximumPlanarJitter = 0.035f;
        private const float VacancyProbeLift = 0.08f;
        private const float VacancyProbeDistance = 0.24f;
        private const float VacancyPoseTolerance = 0.035f;

        private static readonly RaycastHit[] CastHits =
            new RaycastHit[CastBufferSize];
        private static readonly Collider[] OverlapHits =
            new Collider[OverlapBufferSize];

        public static bool TryResolveFreeDrop(
            NetworkWorldItem item,
            Vector3 desiredRootPosition,
            Quaternion desiredRootRotation,
            float probeHeight,
            float probeDistance,
            LayerMask surfaceMask,
            NetworkWorldItem ignoredWorldItem,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (!TryBuildShape(item, out BoxShape shape) ||
                !IsFinite(desiredRootPosition) ||
                !IsFinite(desiredRootRotation))
            {
                return false;
            }

            // Several stack operations may happen before the next fixed step.
            // Queries must see the latest authoritative transforms.
            Physics.SyncTransforms();

            Vector3 jitter = CalculateDeterministicPlanarJitter(item);
            Vector3 firstTarget = desiredRootPosition + jitter;

            if (TryResolveReleaseAt(
                    shape,
                    item,
                    ignoredWorldItem,
                    firstTarget,
                    desiredRootRotation,
                    probeHeight,
                    probeDistance,
                    surfaceMask,
                    out worldPosition))
            {
                worldRotation = desiredRootRotation;
                return true;
            }

            float searchStep = CalculateHorizontalSearchStep(
                shape,
                desiredRootRotation);
            float phase = CalculateSearchPhase(item);

            for (int ring = 1; ring <= SearchRingCount; ring++)
            {
                float radius = searchStep * ring;

                for (int direction = 0;
                     direction < SearchDirections;
                     direction++)
                {
                    float angle = phase +
                                  direction *
                                  (Mathf.PI * 2f / SearchDirections);
                    Vector3 offset = new Vector3(
                        Mathf.Cos(angle) * radius,
                        0f,
                        Mathf.Sin(angle) * radius);

                    if (!TryResolveReleaseAt(
                            shape,
                            item,
                            ignoredWorldItem,
                            firstTarget + offset,
                            desiredRootRotation,
                            probeHeight,
                            probeDistance,
                            surfaceMask,
                            out worldPosition))
                    {
                        continue;
                    }

                    worldRotation = desiredRootRotation;
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveSwapReplacement(
            NetworkWorldItem replacementItem,
            NetworkWorldItem targetItem,
            float probeHeight,
            float probeDistance,
            LayerMask surfaceMask,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (replacementItem == null || targetItem == null ||
                !targetItem.IsSpawned || !targetItem.Location.IsWorld ||
                NetworkItemPhysicsSettler.IsSettlingServer(targetItem) ||
                !TryBuildShape(replacementItem, out BoxShape shape))
            {
                return false;
            }

            Physics.SyncTransforms();

            Vector3 targetPosition = targetItem.transform.position;
            Quaternion targetRotation = targetItem.transform.rotation;
            BoxCollider targetBox = FindPrimaryBoxCollider(targetItem);

            // A reveal/skill can temporarily turn the collider into a trigger.
            // That airborne pose is not a real vacancy and must not be copied.
            if (targetBox == null || !targetBox.enabled ||
                targetBox.isTrigger)
            {
                return false;
            }

            Vector3 probeOrigin =
                targetPosition + Vector3.up * VacancyProbeLift;

            if (!TryCastDown(
                    shape,
                    replacementItem,
                    targetItem,
                    probeOrigin,
                    targetRotation,
                    VacancyProbeDistance,
                    surfaceMask,
                    out RaycastHit supportHit,
                    out Vector3 supportedRootPosition) ||
                Vector3.Distance(
                    supportedRootPosition,
                    targetPosition) > VacancyPoseTolerance ||
                !IsPoseClear(
                    shape,
                    replacementItem,
                    targetItem,
                    supportHit.collider,
                    targetPosition,
                    targetRotation,
                    surfaceMask))
            {
                return false;
            }

            worldPosition = targetPosition;
            worldRotation = targetRotation;
            return true;
        }

        private static bool TryResolveReleaseAt(
            BoxShape shape,
            NetworkWorldItem item,
            NetworkWorldItem ignoredWorldItem,
            Vector3 desiredRootPosition,
            Quaternion desiredRootRotation,
            float probeHeight,
            float probeDistance,
            LayerMask surfaceMask,
            out Vector3 releasePosition)
        {
            releasePosition = Vector3.zero;

            Vector3 castOrigin = desiredRootPosition +
                                 Vector3.up *
                                 Mathf.Max(0.05f, probeHeight);

            if (!TryCastDown(
                    shape,
                    item,
                    ignoredWorldItem,
                    castOrigin,
                    desiredRootRotation,
                    Mathf.Max(0.1f, probeDistance),
                    surfaceMask,
                    out RaycastHit supportHit,
                    out Vector3 supportedRootPosition))
            {
                return false;
            }

            // Items without a Rigidbody retain the legacy static placement.
            // Game cases have a Rigidbody and are released a short distance up.
            float lift = item != null &&
                         item.TryGetComponent(out Rigidbody _)
                ? PhysicsReleaseHeight
                : SurfaceSkin;

            while (lift <= MaximumReleaseHeight + 0.0001f)
            {
                Vector3 candidate = supportedRootPosition +
                                    Vector3.up * lift;

                if (IsPoseClear(
                        shape,
                        item,
                        ignoredWorldItem,
                        supportHit.collider,
                        candidate,
                        desiredRootRotation,
                        surfaceMask))
                {
                    releasePosition = candidate;
                    return true;
                }

                if (lift <= SurfaceSkin + 0.0001f)
                {
                    break;
                }

                lift += ReleaseHeightStep;
            }

            return false;
        }

        private static bool TryCastDown(
            BoxShape shape,
            NetworkWorldItem item,
            NetworkWorldItem ignoredWorldItem,
            Vector3 rootOrigin,
            Quaternion rootRotation,
            float castDistance,
            LayerMask surfaceMask,
            out RaycastHit bestHit,
            out Vector3 rootPositionAtHit)
        {
            bestHit = default;
            rootPositionAtHit = Vector3.zero;

            shape.GetWorldPose(
                rootOrigin,
                rootRotation,
                out Vector3 center,
                out Quaternion boxRotation);

            int hitCount = Physics.BoxCastNonAlloc(
                center,
                ShrinkExtents(shape.HalfExtents, CastSkin),
                Vector3.down,
                CastHits,
                boxRotation,
                castDistance,
                surfaceMask,
                QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            float minimumUpDot = Mathf.Cos(
                MaximumSlopeDegrees * Mathf.Deg2Rad);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = CastHits[i];

                if (hit.collider == null ||
                    hit.distance >= bestDistance ||
                    Vector3.Dot(hit.normal, Vector3.up) < minimumUpDot ||
                    IsIgnoredCollider(
                        hit.collider,
                        item,
                        ignoredWorldItem))
                {
                    continue;
                }

                bestDistance = hit.distance;
                bestHit = hit;
            }

            if (float.IsPositiveInfinity(bestDistance))
            {
                return false;
            }

            rootPositionAtHit = rootOrigin +
                                Vector3.down * bestDistance +
                                Vector3.up * SurfaceSkin;
            return true;
        }

        private static bool IsPoseClear(
            BoxShape shape,
            NetworkWorldItem item,
            NetworkWorldItem ignoredWorldItem,
            Collider supportCollider,
            Vector3 rootPosition,
            Quaternion rootRotation,
            LayerMask surfaceMask)
        {
            shape.GetWorldPose(
                rootPosition,
                rootRotation,
                out Vector3 center,
                out Quaternion boxRotation);

            int overlapCount = Physics.OverlapBoxNonAlloc(
                center,
                ShrinkExtents(
                    shape.HalfExtents,
                    SurfaceSkin * 1.5f),
                OverlapHits,
                boxRotation,
                surfaceMask,
                QueryTriggerInteraction.Ignore);

            NetworkWorldItem supportItem = supportCollider != null
                ? supportCollider.GetComponentInParent<NetworkWorldItem>()
                : null;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider candidate = OverlapHits[i];

                if (candidate == null ||
                    IsIgnoredCollider(
                        candidate,
                        item,
                        ignoredWorldItem) ||
                    candidate == supportCollider ||
                    (supportItem != null &&
                     candidate.GetComponentInParent<NetworkWorldItem>() ==
                     supportItem))
                {
                    continue;
                }

                return false;
            }

            // If the non-alloc buffer filled, the dense region is ambiguous.
            return overlapCount < OverlapHits.Length;
        }

        private static bool TryBuildShape(
            NetworkWorldItem item,
            out BoxShape shape)
        {
            shape = default;
            BoxCollider box = FindPrimaryBoxCollider(item);

            if (item == null || box == null)
            {
                return false;
            }

            Transform root = item.transform;
            Vector3 worldCenter = box.transform.TransformPoint(box.center);
            Vector3 centerOffset = Quaternion.Inverse(root.rotation) *
                                   (worldCenter - root.position);
            Quaternion relativeRotation =
                Quaternion.Inverse(root.rotation) * box.transform.rotation;
            Vector3 halfExtents = Vector3.Scale(
                box.size * 0.5f,
                Abs(box.transform.lossyScale));

            halfExtents.x = Mathf.Max(0.005f, halfExtents.x);
            halfExtents.y = Mathf.Max(0.005f, halfExtents.y);
            halfExtents.z = Mathf.Max(0.005f, halfExtents.z);

            shape = new BoxShape(
                centerOffset,
                relativeRotation,
                halfExtents);
            return true;
        }

        private static BoxCollider FindPrimaryBoxCollider(
            NetworkWorldItem item)
        {
            if (item == null)
            {
                return null;
            }

            return item.TryGetComponent(out BoxCollider rootBox)
                ? rootBox
                : item.GetComponentInChildren<BoxCollider>(true);
        }

        private static bool IsIgnoredCollider(
            Collider candidate,
            NetworkWorldItem item,
            NetworkWorldItem ignoredWorldItem)
        {
            if (candidate == null)
            {
                return true;
            }

            if (item != null &&
                candidate.transform.IsChildOf(item.transform))
            {
                return true;
            }

            if (ignoredWorldItem != null &&
                candidate.transform.IsChildOf(
                    ignoredWorldItem.transform))
            {
                return true;
            }

            return candidate.GetComponentInParent<NetworkItemCarrier>() != null;
        }

        private static Vector3 CalculateDeterministicPlanarJitter(
            NetworkWorldItem item)
        {
            ulong id = item != null && item.IsSpawned
                ? item.NetworkObjectId
                : 1UL;
            uint hash = unchecked((uint)(id ^ (id >> 32)));
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;

            float angle = (hash & 0xffffu) / 65535f *
                          Mathf.PI * 2f;
            float radius = ((hash >> 16) & 0xffffu) / 65535f *
                           MaximumPlanarJitter;

            return new Vector3(
                Mathf.Cos(angle) * radius,
                0f,
                Mathf.Sin(angle) * radius);
        }

        private static float CalculateSearchPhase(NetworkWorldItem item)
        {
            return item != null && item.IsSpawned
                ? (item.NetworkObjectId % 16UL) *
                  (Mathf.PI * 2f / 16f)
                : 0f;
        }

        private static float CalculateHorizontalSearchStep(
            BoxShape shape,
            Quaternion rootRotation)
        {
            Quaternion rotation = rootRotation * shape.RelativeRotation;
            Vector3 axisX = rotation * Vector3.right;
            Vector3 axisY = rotation * Vector3.up;
            Vector3 axisZ = rotation * Vector3.forward;
            Vector3 half = shape.HalfExtents;

            float extentX = Mathf.Abs(axisX.x) * half.x +
                            Mathf.Abs(axisY.x) * half.y +
                            Mathf.Abs(axisZ.x) * half.z;
            float extentZ = Mathf.Abs(axisX.z) * half.x +
                            Mathf.Abs(axisY.z) * half.y +
                            Mathf.Abs(axisZ.z) * half.z;

            return Mathf.Clamp(
                Mathf.Min(extentX, extentZ) * 0.75f,
                0.08f,
                0.24f);
        }

        private static Vector3 ShrinkExtents(
            Vector3 extents,
            float amount)
        {
            return new Vector3(
                Mathf.Max(0.001f, extents.x - amount),
                Mathf.Max(0.001f, extents.y - amount),
                Mathf.Max(0.001f, extents.z - amount));
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct BoxShape
        {
            public BoxShape(
                Vector3 centerOffset,
                Quaternion relativeRotation,
                Vector3 halfExtents)
            {
                CenterOffset = centerOffset;
                RelativeRotation = relativeRotation;
                HalfExtents = halfExtents;
            }

            public Vector3 CenterOffset { get; }
            public Quaternion RelativeRotation { get; }
            public Vector3 HalfExtents { get; }

            public void GetWorldPose(
                Vector3 rootPosition,
                Quaternion rootRotation,
                out Vector3 center,
                out Quaternion rotation)
            {
                center = rootPosition + rootRotation * CenterOffset;
                rotation = rootRotation * RelativeRotation;
            }
        }
    }
}