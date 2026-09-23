using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class GameCaseSpawnZone : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private bool includeInGenerateAll = true;
    [Min(0)]
    [SerializeField] private int caseCount = 250;
    [SerializeField] private int seedOffset;

    [Header("Surface")]
    [Tooltip("Only these layers can be used as the floor below a sampled point.")]
    [SerializeField] private LayerMask surfaceMask = ~0;
    [Tooltip("The ray starts this far above the zone transform Y.")]
    [Min(0.01f)]
    [SerializeField] private float surfaceRayStartHeight = 5f;
    [Min(0.01f)]
    [SerializeField] private float surfaceRayDistance = 20f;
    [Tooltip("A hit farther than this from the zone transform Y is ignored. Keep this small when floors overlap vertically.")]
    [Min(0.01f)]
    [SerializeField] private float maximumSurfaceHeightDifference = 2f;
    [Range(0f, 1f)]
    [SerializeField] private float minimumSurfaceUpDot = 0.75f;

    [Header("Blocked Space")]
    [Tooltip("Sample points touching these layers are rejected. Usually Walls, Props and Shelves; do not include Ground.")]
    [SerializeField] private LayerMask blockerMask;
    [Min(0f)]
    [SerializeField] private float blockerRadius = 0.08f;
    [Min(0f)]
    [SerializeField] private float blockerHeight = 0.2f;

    [Header("Scene View")]
    [SerializeField] private Color color = new Color(0.15f, 0.75f, 1f, 0.22f);
    [SerializeField] private bool showLabel = true;

    [Header("Polygon (local XZ)")]
    [SerializeField, HideInInspector] private List<Vector2> points = new List<Vector2>
    {
        new Vector2(-1f, -1f),
        new Vector2(-1f, 1f),
        new Vector2(1f, 1f),
        new Vector2(1f, -1f)
    };

    [SerializeField, HideInInspector] private Transform generatedRoot;

    [NonSerialized] private readonly RaycastHit[] surfaceHits = new RaycastHit[32];

    public bool IncludeInGenerateAll
    {
        get => includeInGenerateAll;
        set => includeInGenerateAll = value;
    }

    public int CaseCount
    {
        get => caseCount;
        set => caseCount = Mathf.Max(0, value);
    }

    public int SeedOffset => seedOffset;
    public LayerMask SurfaceMask => surfaceMask;
    public LayerMask BlockerMask => blockerMask;
    public float SurfaceRayStartHeight => surfaceRayStartHeight;
    public float SurfaceRayDistance => surfaceRayDistance;
    public float MaximumSurfaceHeightDifference => maximumSurfaceHeightDifference;
    public float MinimumSurfaceUpDot => minimumSurfaceUpDot;
    public float BlockerRadius => blockerRadius;
    public float BlockerHeight => blockerHeight;
    public Color Color => color;
    public bool ShowLabel => showLabel;
    public IReadOnlyList<Vector2> Points => points;
    public Transform GeneratedRoot => generatedRoot;
    public float Area => GameCasePolygonUtility.Area(points) * Mathf.Abs(transform.lossyScale.x * transform.lossyScale.z);
    public bool IsValid => points != null && points.Count >= 3 && Area > 0.001f && GameCasePolygonUtility.IsSimple(points);

    public void SetPointsFromWorld(IReadOnlyList<Vector3> worldPoints)
    {
        points.Clear();

        if (worldPoints == null)
            return;

        for (int i = 0; i < worldPoints.Count; i++)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoints[i]);
            points.Add(new Vector2(local.x, local.z));
        }
    }

    public void SetSceneColor(Color value)
    {
        color = value;
    }

    public void SetLocalPoint(int index, Vector2 point)
    {
        if (index < 0 || index >= points.Count)
            return;

        points[index] = point;
    }

    public void InsertLocalPoint(int index, Vector2 point)
    {
        points.Insert(Mathf.Clamp(index, 0, points.Count), point);
    }

    public void RemoveLocalPoint(int index)
    {
        if (points.Count <= 3 || index < 0 || index >= points.Count)
            return;

        points.RemoveAt(index);
    }

    public Vector3 GetWorldPoint(int index)
    {
        Vector2 point = points[index];
        return transform.TransformPoint(new Vector3(point.x, 0f, point.y));
    }

    public Vector3 GetWorldCenter()
    {
        if (points == null || points.Count == 0)
            return transform.position;

        Vector3 center = Vector3.zero;

        for (int i = 0; i < points.Count; i++)
            center += GetWorldPoint(i);

        return center / points.Count;
    }

    public bool TrySampleSurface(System.Random random, int maxAttempts, out Vector3 surfacePoint)
    {
        surfacePoint = default;

        if (!IsValid)
            return false;

        Rect bounds = GameCasePolygonUtility.CalculateBounds(points);

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float localX = Mathf.Lerp(bounds.xMin, bounds.xMax, (float)random.NextDouble());
            float localZ = Mathf.Lerp(bounds.yMin, bounds.yMax, (float)random.NextDouble());
            var localPoint = new Vector2(localX, localZ);

            if (!GameCasePolygonUtility.ContainsPoint(points, localPoint))
                continue;

            Vector3 worldOnPlane = transform.TransformPoint(new Vector3(localX, 0f, localZ));
            Vector3 rayOrigin = worldOnPlane + Vector3.up * surfaceRayStartHeight;

            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                surfaceHits,
                surfaceRayDistance,
                surfaceMask,
                QueryTriggerInteraction.Ignore);

            bool foundSurface = false;
            RaycastHit bestHit = default;
            float bestHeightDifference = float.PositiveInfinity;

            for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
            {
                RaycastHit candidate = surfaceHits[hitIndex];

                if (Vector3.Dot(candidate.normal, Vector3.up) < minimumSurfaceUpDot)
                    continue;

                float heightDifference = Mathf.Abs(candidate.point.y - worldOnPlane.y);

                if (heightDifference > maximumSurfaceHeightDifference ||
                    heightDifference >= bestHeightDifference)
                {
                    continue;
                }

                foundSurface = true;
                bestHit = candidate;
                bestHeightDifference = heightDifference;
            }

            if (!foundSurface || IsBlocked(bestHit.point))
                continue;

            surfacePoint = bestHit.point;
            return true;
        }

        return false;
    }

    public Transform EnsureGeneratedRoot(Transform globalRoot)
    {
        if (generatedRoot)
            return generatedRoot;

        string expectedName = $"{name}_Cases";
        Transform existing = globalRoot ? globalRoot.Find(expectedName) : null;

        if (existing)
        {
            generatedRoot = existing;
            return generatedRoot;
        }

        var root = new GameObject(expectedName);
        root.transform.SetParent(globalRoot, true);
        generatedRoot = root.transform;
        return generatedRoot;
    }

    public void ForgetGeneratedRoot()
    {
        generatedRoot = null;
    }

    private bool IsBlocked(Vector3 surfacePoint)
    {
        if (blockerMask.value == 0 || blockerRadius <= 0f || blockerHeight <= 0f)
            return false;

        Vector3 halfExtents = new Vector3(blockerRadius, blockerHeight * 0.5f, blockerRadius);
        Vector3 center = surfacePoint + Vector3.up * (blockerHeight * 0.5f + 0.01f);

        return Physics.CheckBox(
            center,
            halfExtents,
            Quaternion.identity,
            blockerMask,
            QueryTriggerInteraction.Ignore);
    }

    private void OnValidate()
    {
        caseCount = Mathf.Max(0, caseCount);
        surfaceRayStartHeight = Mathf.Max(0.01f, surfaceRayStartHeight);
        surfaceRayDistance = Mathf.Max(surfaceRayStartHeight, surfaceRayDistance);
        maximumSurfaceHeightDifference = Mathf.Max(0.01f, maximumSurfaceHeightDifference);
        blockerRadius = Mathf.Max(0f, blockerRadius);
        blockerHeight = Mathf.Max(0f, blockerHeight);
    }
}
