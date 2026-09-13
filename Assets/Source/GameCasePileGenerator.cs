using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameCasePileGenerator : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject casePrefab;

    [Header("Pile")]
    [Min(1)]
    [SerializeField] private int amount = 50;

    [Range(1, 30)]
    [SerializeField] private int layerCount = 6;

    [Min(0.01f)]
    [SerializeField] private float baseRadius = 1.5f;

    [Range(0.05f, 1f)]
    [SerializeField] private float topRadiusMultiplier = 0.2f;

    [Min(0.001f)]
    [SerializeField] private float layerHeight = 0.12f;

    [Header("Randomness")]
    [SerializeField]
    private Vector2 verticalJitter = new Vector2(-0.015f, 0.025f);

    [Range(0f, 1f)]
    [SerializeField] private float positionJitter = 0.12f;

    [Range(0f, 45f)]
    [SerializeField] private float maxTilt = 10f;

    [SerializeField] private bool randomYaw = true;

    [Range(0f, 1f)]
    [SerializeField] private float layerCenterDrift = 0.15f;

    [Header("Spacing")]
    [Min(0f)]
    [SerializeField] private float minimumHorizontalSpacing = 0.18f;

    [Range(1, 100)]
    [SerializeField] private int placementAttempts = 25;

    [Header("Seed")]
    [SerializeField] private int seed = 12345;

    [Header("Generated")]
    [SerializeField] private Transform generatedRoot;

    private readonly List<Vector2> _layerPositions = new();

    public Transform GeneratedRoot => generatedRoot;
    public int Amount => amount;

#if UNITY_EDITOR

    public void Generate()
    {
        if (!casePrefab)
        {
            Debug.LogWarning(
                "[GameCasePileGenerator] Case Prefab is not assigned.",
                this
            );

            return;
        }

        if (amount <= 0)
            return;

        EnsureRoot();

        // Generate her çağrıldığında eski pile temizlenir.
        ClearPile();

        Random.State previousRandomState = Random.state;
        Random.InitState(seed);

        int[] casesPerLayer = CalculateLayerDistribution();

        Vector2 layerCenter = Vector2.zero;

        for (int layer = 0; layer < layerCount; layer++)
        {
            int count = casesPerLayer[layer];

            if (count <= 0)
                continue;

            float normalizedLayer =
                layerCount <= 1
                    ? 0f
                    : layer / (float)(layerCount - 1);

            float radiusMultiplier =
                Mathf.Lerp(
                    1f,
                    topRadiusMultiplier,
                    normalizedLayer
                );

            float layerRadius =
                baseRadius * radiusMultiplier;

            // Yığın yukarı çıktıkça hafif yana kayabilsin.
            Vector2 drift =
                Random.insideUnitCircle *
                baseRadius *
                layerCenterDrift *
                normalizedLayer;

            layerCenter +=
                drift / Mathf.Max(1, layerCount);

            _layerPositions.Clear();

            for (int i = 0; i < count; i++)
            {
                Vector2 position2D =
                    FindPositionInLayer(
                        layerRadius,
                        layerCenter
                    );

                float y =
                    layer * layerHeight +
                    Random.Range(
                        verticalJitter.x,
                        verticalJitter.y
                    );

                Vector3 localPosition =
                    new Vector3(
                        position2D.x,
                        y,
                        position2D.y
                    );

                GameObject instance =
                    PrefabUtility.InstantiatePrefab(
                        casePrefab,
                        generatedRoot
                    ) as GameObject;

                if (!instance)
                    continue;

                instance.transform.localPosition =
                    localPosition;

                float yaw =
                    randomYaw
                        ? Random.Range(0f, 360f)
                        : 0f;

                float tiltX =
                    Random.Range(-maxTilt, maxTilt);

                float tiltZ =
                    Random.Range(-maxTilt, maxTilt);

                instance.transform.localRotation =
                    Quaternion.Euler(
                        tiltX,
                        yaw,
                        tiltZ
                    );
            }
        }

        Random.state = previousRandomState;

        EditorUtility.SetDirty(this);
        EditorUtility.SetDirty(gameObject);

        SceneView.RepaintAll();

        Debug.Log(
            $"[GameCasePileGenerator] Generated {amount} cases.",
            this
        );
    }

    public void RandomizeAndGenerate()
    {
        seed = Guid.NewGuid().GetHashCode();

        Generate();
    }

    /// <summary>
    /// Generated Root kalır, sadece child'ları silinir.
    /// </summary>
    public void ClearPile()
    {
        if (!generatedRoot)
            return;

        for (int i = generatedRoot.childCount - 1; i >= 0; i--)
        {
            Transform child =
                generatedRoot.GetChild(i);

            if (!child)
                continue;

            DestroyImmediate(child.gameObject);
        }

        EditorUtility.SetDirty(this);
        SceneView.RepaintAll();
    }

    /// <summary>
    /// Root dahil her şeyi siler.
    /// </summary>
    public void RemoveRoot()
    {
        if (!generatedRoot)
            return;

        GameObject rootObject =
            generatedRoot.gameObject;

        generatedRoot = null;

        DestroyImmediate(rootObject);

        EditorUtility.SetDirty(this);
        SceneView.RepaintAll();
    }

    private void EnsureRoot()
    {
        if (generatedRoot)
            return;

        Transform existing =
            transform.Find("GameCaseRoot");

        if (existing)
        {
            generatedRoot = existing;
            return;
        }

        GameObject root =
            new GameObject("GameCaseRoot");

        root.transform.SetParent(transform);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        generatedRoot = root.transform;

        EditorUtility.SetDirty(this);
    }

    private int[] CalculateLayerDistribution()
    {
        int safeLayerCount =
            Mathf.Max(1, layerCount);

        int[] distribution =
            new int[safeLayerCount];

        float[] weights =
            new float[safeLayerCount];

        float totalWeight = 0f;

        for (int i = 0; i < safeLayerCount; i++)
        {
            float t =
                safeLayerCount <= 1
                    ? 0f
                    : i / (float)(safeLayerCount - 1);

            float radius =
                Mathf.Lerp(
                    1f,
                    topRadiusMultiplier,
                    t
                );

            // Circle area ~= radius²
            float weight =
                radius * radius;

            weights[i] = weight;
            totalWeight += weight;
        }

        int assigned = 0;

        for (int i = 0; i < safeLayerCount; i++)
        {
            int count =
                Mathf.FloorToInt(
                    amount *
                    (weights[i] / totalWeight)
                );

            distribution[i] = count;
            assigned += count;
        }

        int remaining =
            amount - assigned;

        int index = 0;

        while (remaining > 0)
        {
            distribution[index]++;

            remaining--;
            index++;

            if (index >= safeLayerCount)
                index = 0;
        }

        return distribution;
    }

    private Vector2 FindPositionInLayer(
        float radius,
        Vector2 layerCenter)
    {
        Vector2 bestCandidate =
            layerCenter;

        float minDistanceSqr =
            minimumHorizontalSpacing *
            minimumHorizontalSpacing;

        for (int attempt = 0;
             attempt < placementAttempts;
             attempt++)
        {
            float distance =
                Mathf.Sqrt(Random.value) *
                radius;

            float angle =
                Random.Range(
                    0f,
                    Mathf.PI * 2f
                );

            Vector2 candidate =
                layerCenter +
                new Vector2(
                    Mathf.Cos(angle),
                    Mathf.Sin(angle)
                ) *
                distance;

            candidate +=
                Random.insideUnitCircle *
                positionJitter;

            bestCandidate = candidate;

            bool valid = true;

            for (int i = 0;
                 i < _layerPositions.Count;
                 i++)
            {
                float sqrDistance =
                    (
                        candidate -
                        _layerPositions[i]
                    ).sqrMagnitude;

                if (sqrDistance <
                    minDistanceSqr)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            _layerPositions.Add(candidate);

            return candidate;
        }

        // Çok doluysa sonsuz arama yapma.
        _layerPositions.Add(bestCandidate);

        return bestCandidate;
    }

#endif
}