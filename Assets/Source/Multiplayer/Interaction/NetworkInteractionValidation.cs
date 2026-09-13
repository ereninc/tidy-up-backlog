using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Pure validation helpers shared by runtime code and the editor self-test.
    /// Keeping these rules free of Steam and transport dependencies makes the
    /// interaction layer identical for local-host solo and online co-op.
    /// </summary>
    public static class NetworkInteractionValidation
    {
        private const float DirectionEpsilon = 0.000001f;

        public static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        public static bool TryNormalizeDirection(
            Vector3 direction,
            out Vector3 normalized)
        {
            normalized = Vector3.zero;

            if (!IsFinite(direction) ||
                direction.sqrMagnitude < DirectionEpsilon)
            {
                return false;
            }

            normalized = direction.normalized;
            return IsFinite(normalized);
        }

        public static bool IsOriginWithinTolerance(
            Vector3 expectedOrigin,
            Vector3 submittedOrigin,
            float tolerance)
        {
            if (!IsFinite(expectedOrigin) || !IsFinite(submittedOrigin))
            {
                return false;
            }

            float safeTolerance = Mathf.Max(0f, tolerance);
            return (expectedOrigin - submittedOrigin).sqrMagnitude <=
                   safeTolerance * safeTolerance;
        }

        public static bool IsAimWithinTolerance(
            Vector3 actorForward,
            Vector3 submittedDirection,
            float maximumHorizontalAngle)
        {
            if (!TryNormalizeDirection(
                    submittedDirection,
                    out Vector3 normalizedDirection))
            {
                return false;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(
                actorForward,
                Vector3.up);
            Vector3 flatDirection = Vector3.ProjectOnPlane(
                normalizedDirection,
                Vector3.up);

            // Looking almost perfectly up/down carries no useful horizontal aim
            // information, so origin, distance and line-of-sight remain the gates.
            if (flatDirection.sqrMagnitude < DirectionEpsilon)
            {
                return true;
            }

            if (!IsFinite(flatForward) ||
                flatForward.sqrMagnitude < DirectionEpsilon)
            {
                return false;
            }

            float angle = Vector3.Angle(flatForward, flatDirection);
            return angle <= Mathf.Clamp(maximumHorizontalAngle, 0f, 180f);
        }

        public static bool IsWithinDistance(
            Vector3 origin,
            Vector3 point,
            float maximumDistance,
            float tolerance = 0f)
        {
            if (!IsFinite(origin) || !IsFinite(point))
            {
                return false;
            }

            float allowedDistance =
                Mathf.Max(0f, maximumDistance) + Mathf.Max(0f, tolerance);

            // Keeps an inclusive boundary inclusive despite float rounding.
            allowedDistance += 0.0001f;

            return (origin - point).sqrMagnitude <=
                   allowedDistance * allowedDistance;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
