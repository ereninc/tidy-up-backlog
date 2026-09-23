using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GameCaseDistributionBaker
{
    private sealed class SimulatedCase
    {
        public GameObject GameObject;
        public Rigidbody Rigidbody;
        public bool RigidbodyWasAdded;
        public RigidbodyState OriginalState;
    }

    private readonly struct RigidbodyState
    {
        private readonly bool _isKinematic;
        private readonly bool _useGravity;
        private readonly float _mass;
        private readonly float _linearDamping;
        private readonly float _angularDamping;
        private readonly CollisionDetectionMode _collisionDetectionMode;
        private readonly RigidbodyInterpolation _interpolation;
        private readonly Vector3 _linearVelocity;
        private readonly Vector3 _angularVelocity;

        public RigidbodyState(Rigidbody body)
        {
            _isKinematic = body.isKinematic;
            _useGravity = body.useGravity;
            _mass = body.mass;
            _linearDamping = body.linearDamping;
            _angularDamping = body.angularDamping;
            _collisionDetectionMode = body.collisionDetectionMode;
            _interpolation = body.interpolation;
            _linearVelocity = body.linearVelocity;
            _angularVelocity = body.angularVelocity;
        }

        public void Restore(Rigidbody body, bool forceKinematic, bool clearVelocity)
        {
            if (!body)
                return;

            bool targetKinematic = forceKinematic || _isKinematic;
            body.isKinematic = targetKinematic;
            body.mass = _mass;
            body.linearDamping = _linearDamping;
            body.angularDamping = _angularDamping;
            body.collisionDetectionMode = _collisionDetectionMode;
            body.interpolation = _interpolation;
            body.useGravity = _useGravity;

            if (!targetKinematic)
            {
                body.linearVelocity = clearVelocity ? Vector3.zero : _linearVelocity;
                body.angularVelocity = clearVelocity ? Vector3.zero : _angularVelocity;
            }
        }
    }

    private sealed class FrozenBody
    {
        public Rigidbody Body;
        public RigidbodyState State;
    }

    public static void GenerateAll(GameCaseDistributionGenerator generator, bool randomizeSeed = false)
    {
        if (!ValidateGenerator(generator))
            return;

        GameCaseSpawnZone[] zones = generator
            .GetZones()
            .Where(zone => zone && zone.gameObject.activeInHierarchy && zone.IncludeInGenerateAll)
            .ToArray();

        if (zones.Length == 0)
        {
            Debug.LogWarning("[GameCaseDistribution] No enabled spawn zones were found.", generator);
            return;
        }

        Dictionary<GameCaseSpawnZone, int> counts = CalculateCounts(generator, zones);
        int baseSeed = randomizeSeed ? Guid.NewGuid().GetHashCode() : generator.Seed;

        for (int i = 0; i < zones.Length; i++)
        {
            GameCaseSpawnZone zone = zones[i];

            if (!zone.IsValid)
            {
                Debug.LogWarning($"[GameCaseDistribution] Skipped invalid or self-intersecting zone: {zone.name}", zone);
                continue;
            }

            if (!GenerateInternal(generator, zone, counts[zone], baseSeed, i, zones.Length))
                break;
        }

        SceneView.RepaintAll();
    }

    public static void GenerateZone(GameCaseDistributionGenerator generator, GameCaseSpawnZone zone, bool randomizeSeed = false)
    {
        if (!ValidateGenerator(generator) || !zone)
            return;

        if (!zone.IsValid)
        {
            Debug.LogWarning("[GameCaseDistribution] The selected zone is invalid or self-intersecting.", zone);
            return;
        }

        int count = zone.CaseCount;

        if (generator.DistributionMode == GameCaseDistributionGenerator.CountDistributionMode.TargetTotalByArea)
        {
            GameCaseSpawnZone[] zones = generator
                .GetZones()
                .Where(candidate => candidate && candidate.gameObject.activeInHierarchy && candidate.IncludeInGenerateAll && candidate.IsValid)
                .ToArray();

            Dictionary<GameCaseSpawnZone, int> counts = CalculateCounts(generator, zones);

            if (counts.TryGetValue(zone, out int distributedCount))
                count = distributedCount;
        }

        int baseSeed = randomizeSeed ? Guid.NewGuid().GetHashCode() : generator.Seed;
        GenerateInternal(generator, zone, count, baseSeed, 0, 1);
        SceneView.RepaintAll();
    }

    public static void ClearZone(GameCaseSpawnZone zone)
    {
        if (!zone || !zone.GeneratedRoot)
            return;

        GameObject root = zone.GeneratedRoot.gameObject;
        zone.ForgetGeneratedRoot();
        UnityEngine.Object.DestroyImmediate(root);
        EditorUtility.SetDirty(zone);
        MarkSceneDirty(zone.gameObject);
        SceneView.RepaintAll();
    }

    public static void ClearAll(GameCaseDistributionGenerator generator)
    {
        if (!generator)
            return;

        GameCaseSpawnZone[] zones = generator.GetZones();

        for (int i = 0; i < zones.Length; i++)
            ClearZone(zones[i]);

        Transform globalRoot = generator.GeneratedRoot;

        if (globalRoot)
        {
            for (int i = globalRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(globalRoot.GetChild(i).gameObject);
        }

        MarkSceneDirty(generator.gameObject);
        SceneView.RepaintAll();
    }

    public static Dictionary<GameCaseSpawnZone, int> CalculateCounts(
        GameCaseDistributionGenerator generator,
        IReadOnlyList<GameCaseSpawnZone> zones)
    {
        var result = new Dictionary<GameCaseSpawnZone, int>();

        if (zones == null || zones.Count == 0)
            return result;

        if (generator.DistributionMode == GameCaseDistributionGenerator.CountDistributionMode.PerZone)
        {
            for (int i = 0; i < zones.Count; i++)
                result[zones[i]] = Mathf.Max(0, zones[i].CaseCount);

            return result;
        }

        float totalArea = 0f;

        for (int i = 0; i < zones.Count; i++)
        {
            if (zones[i].IsValid)
                totalArea += zones[i].Area;
        }

        if (totalArea <= 0.0001f)
        {
            for (int i = 0; i < zones.Count; i++)
                result[zones[i]] = 0;

            return result;
        }

        var remainders = new List<(GameCaseSpawnZone zone, float remainder)>();
        int assigned = 0;

        for (int i = 0; i < zones.Count; i++)
        {
            GameCaseSpawnZone zone = zones[i];
            float exact = zone.IsValid ? generator.TargetTotal * (zone.Area / totalArea) : 0f;
            int floor = Mathf.FloorToInt(exact);
            result[zone] = floor;
            assigned += floor;
            remainders.Add((zone, exact - floor));
        }

        remainders.Sort((a, b) => b.remainder.CompareTo(a.remainder));
        int remaining = Mathf.Max(0, generator.TargetTotal - assigned);

        for (int i = 0; i < remaining && i < remainders.Count; i++)
            result[remainders[i].zone]++;

        return result;
    }

    private static bool GenerateInternal(
        GameCaseDistributionGenerator generator,
        GameCaseSpawnZone zone,
        int requestedCount,
        int baseSeed,
        int zoneIndex,
        int zoneTotal)
    {
        requestedCount = Mathf.Max(0, requestedCount);
        ClearZone(zone);

        if (requestedCount == 0)
            return true;

        Transform globalRoot = generator.EnsureGeneratedRoot();
        Transform zoneRoot = zone.EnsureGeneratedRoot(globalRoot);
        EditorUtility.SetDirty(generator);
        EditorUtility.SetDirty(zone);

        var random = new System.Random(unchecked(baseSeed + zone.SeedOffset * 397));
        var cases = new List<SimulatedCase>(requestedCount);
        var frozenBodies = new List<FrozenBody>();
        SimulationMode oldSimulationMode = Physics.simulationMode;
        int failedSamples = 0;
        bool completed = false;

        try
        {
            if (generator.FreezeOtherDynamicRigidbodies)
                FreezeOtherBodies(frozenBodies);

            Physics.simulationMode = SimulationMode.Script;
            int spawned = 0;

            while (spawned < requestedCount)
            {
                int spawnCount = Mathf.Min(generator.BatchSize, requestedCount - spawned);

                for (int i = 0; i < spawnCount; i++)
                {
                    if (!zone.TrySampleSurface(random, generator.PointSampleAttempts, out Vector3 surfacePoint))
                    {
                        failedSamples++;
                        continue;
                    }

                    SimulatedCase item = SpawnCase(generator, zoneRoot, surfacePoint, spawned + i, random);

                    if (item != null)
                        cases.Add(item);
                }

                spawned += spawnCount;
                Physics.SyncTransforms();

                if (generator.BatchSimulationTime > 0f && cases.Count > 0)
                {
                    SimulateForDuration(
                        generator,
                        generator.BatchSimulationTime,
                        $"{zone.name}: dropping {spawned}/{requestedCount}",
                        OverallProgress(zoneIndex, zoneTotal, spawned / (float)requestedCount));
                }
            }

            Physics.SyncTransforms();
            SimulateUntilSettled(generator, cases, zone.name, zoneIndex, zoneTotal);
            Physics.SyncTransforms();
            BakeCases(generator, cases);
            completed = true;

            MarkSceneDirty(generator.gameObject);

            if (failedSamples > 0)
            {
                Debug.LogWarning(
                    $"[GameCaseDistribution] {zone.name}: generated {cases.Count}/{requestedCount}. " +
                    $"{failedSamples} points could not find a valid surface. Check Surface/Blocker masks or enlarge the zone.",
                    zone);
            }
            else
            {
                Debug.Log($"[GameCaseDistribution] {zone.name}: baked {cases.Count} cases.", zone);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning($"[GameCaseDistribution] Bake cancelled while generating {zone.name}.", zone);
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, zone);
            return false;
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            if (!completed)
            {
                for (int i = cases.Count - 1; i >= 0; i--)
                {
                    if (cases[i].GameObject)
                        UnityEngine.Object.DestroyImmediate(cases[i].GameObject);
                }
            }

            RestoreOtherBodies(frozenBodies);
            Physics.simulationMode = oldSimulationMode;
            Physics.SyncTransforms();
        }
    }

    private static SimulatedCase SpawnCase(
        GameCaseDistributionGenerator generator,
        Transform parent,
        Vector3 surfacePoint,
        int index,
        System.Random random)
    {
        GameObject instance = PrefabUtility.InstantiatePrefab(generator.CasePrefab, parent) as GameObject;

        if (!instance)
            return null;

        instance.name = $"{generator.CasePrefab.name}_{index:0000}";

        float heightOffset = generator.DropHeight + Range(random, 0f, generator.SpawnVolumeHeight);
        instance.transform.position = surfacePoint + Vector3.up * heightOffset;
        instance.transform.rotation = Quaternion.Euler(
            Range(random, -generator.InitialTilt, generator.InitialTilt),
            Range(random, 0f, 360f),
            Range(random, -generator.InitialTilt, generator.InitialTilt));

        Rigidbody body = instance.GetComponent<Rigidbody>();
        bool wasAdded = !body;

        if (!body)
            body = instance.AddComponent<Rigidbody>();

        var originalState = new RigidbodyState(body);
        body.isKinematic = false;
        body.useGravity = true;
        body.mass = generator.CaseMass;
        body.linearDamping = generator.LinearDamping;
        body.angularDamping = generator.AngularDamping;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.interpolation = RigidbodyInterpolation.None;

        Vector2 direction = RandomInsideUnitCircle(random);
        body.linearVelocity = new Vector3(direction.x, 0f, direction.y) * generator.HorizontalVelocity;
        body.angularVelocity = RandomInsideUnitSphere(random) * generator.AngularVelocity;

        return new SimulatedCase
        {
            GameObject = instance,
            Rigidbody = body,
            RigidbodyWasAdded = wasAdded,
            OriginalState = originalState
        };
    }

    private static void BakeCases(GameCaseDistributionGenerator generator, List<SimulatedCase> cases)
    {
        for (int i = 0; i < cases.Count; i++)
        {
            SimulatedCase item = cases[i];

            if (!item.GameObject || !item.Rigidbody)
                continue;

            if (item.RigidbodyWasAdded)
            {
                UnityEngine.Object.DestroyImmediate(item.Rigidbody);
            }
            else
            {
                item.OriginalState.Restore(
                    item.Rigidbody,
                    generator.KeepPrefabRigidbodyKinematicAfterBake,
                    clearVelocity: true);
            }

            EditorUtility.SetDirty(item.GameObject.transform);
        }
    }

    private static void SimulateForDuration(
        GameCaseDistributionGenerator generator,
        float duration,
        string message,
        float progress)
    {
        int steps = Mathf.CeilToInt(duration / generator.SimulationStep);

        for (int i = 0; i < steps; i++)
        {
            Physics.Simulate(generator.SimulationStep);

            if (i % 10 != 0)
                continue;

            if (EditorUtility.DisplayCancelableProgressBar(
                    "Game Case Distribution Bake",
                    message,
                    Mathf.Clamp01(progress)))
            {
                throw new OperationCanceledException();
            }
        }
    }

    private static void SimulateUntilSettled(
        GameCaseDistributionGenerator generator,
        List<SimulatedCase> cases,
        string zoneName,
        int zoneIndex,
        int zoneTotal)
    {
        int maxSteps = Mathf.CeilToInt(generator.SettleTime / generator.SimulationStep);
        int consecutiveSleepingFrames = 0;
        const int requiredSleepingFrames = 15;

        for (int step = 0; step < maxSteps; step++)
        {
            Physics.Simulate(generator.SimulationStep);
            bool allSleeping = true;

            for (int i = 0; i < cases.Count; i++)
            {
                Rigidbody body = cases[i].Rigidbody;

                if (body && !body.IsSleeping())
                {
                    allSleeping = false;
                    break;
                }
            }

            consecutiveSleepingFrames = allSleeping ? consecutiveSleepingFrames + 1 : 0;

            if (consecutiveSleepingFrames >= requiredSleepingFrames)
                break;

            if (step % 10 != 0)
                continue;

            float zoneProgress = step / (float)Mathf.Max(1, maxSteps);
            float overallProgress = OverallProgress(zoneIndex, zoneTotal, zoneProgress);

            if (EditorUtility.DisplayCancelableProgressBar(
                    "Game Case Distribution Bake",
                    $"{zoneName}: waiting for cases to settle...",
                    overallProgress))
            {
                throw new OperationCanceledException();
            }
        }
    }

    private static void FreezeOtherBodies(List<FrozenBody> frozenBodies)
    {
        Rigidbody[] bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody body = bodies[i];

            if (!body || body.isKinematic || EditorUtility.IsPersistent(body))
                continue;

            frozenBodies.Add(new FrozenBody
            {
                Body = body,
                State = new RigidbodyState(body)
            });

            body.isKinematic = true;
        }
    }

    private static void RestoreOtherBodies(List<FrozenBody> frozenBodies)
    {
        for (int i = 0; i < frozenBodies.Count; i++)
        {
            FrozenBody frozen = frozenBodies[i];

            if (frozen.Body)
                frozen.State.Restore(frozen.Body, forceKinematic: false, clearVelocity: false);
        }
    }

    private static bool ValidateGenerator(GameCaseDistributionGenerator generator)
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning("[GameCaseDistribution] Editor baking is disabled in Play Mode.");
            return false;
        }

        if (!generator)
        {
            Debug.LogWarning("[GameCaseDistribution] Assign a generator first.");
            return false;
        }

        if (!generator.CasePrefab)
        {
            Debug.LogWarning("[GameCaseDistribution] Case Prefab is missing.", generator);
            return false;
        }

        if (!generator.CasePrefab.GetComponentInChildren<Collider>())
        {
            Debug.LogError("[GameCaseDistribution] Case Prefab requires at least one 3D Collider.", generator.CasePrefab);
            return false;
        }

        return true;
    }

    private static float OverallProgress(int zoneIndex, int zoneTotal, float zoneProgress)
    {
        return Mathf.Clamp01((zoneIndex + Mathf.Clamp01(zoneProgress)) / Mathf.Max(1f, zoneTotal));
    }

    private static float Range(System.Random random, float minimum, float maximum)
    {
        return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
    }

    private static Vector2 RandomInsideUnitCircle(System.Random random)
    {
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private static Vector3 RandomInsideUnitSphere(System.Random random)
    {
        float z = Range(random, -1f, 1f);
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float radius = Mathf.Pow((float)random.NextDouble(), 1f / 3f);
        float planar = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(planar * Mathf.Cos(angle), z, planar * Mathf.Sin(angle)) * radius;
    }

    private static void MarkSceneDirty(GameObject context)
    {
        if (context && context.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(context.scene);
    }
}
