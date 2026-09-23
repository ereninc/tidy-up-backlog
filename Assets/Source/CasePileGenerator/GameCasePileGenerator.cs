using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameCasePileGenerator : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField]
    private GameObject casePrefab;

    [Header("Amount")]
    [Min(1)]
    [SerializeField]
    private int amount = 250;

    [Header("Drop Area")]
    [Tooltip("Kutuları hangi genişlikteki alandan dökeceğiz.")]
    [SerializeField]
    private Vector2 dropAreaSize = new Vector2(2f, 2f);

    [Tooltip("Kutuların düşmeye başlayacağı world Y.")]
    [Min(0.1f)]
    [SerializeField]
    private float dropHeight = 3f;

    [Tooltip("Spawn edilen case'leri yukarı doğru ne kadar dağıtalım.")]
    [Min(0f)]
    [SerializeField]
    private float spawnVolumeHeight = 2f;

    [Header("Drop Behaviour")]
    [Tooltip("Case'ler havaya ilk bırakılırken maksimum eğim.")]
    [Range(0f, 90f)]
    [SerializeField]
    private float initialTilt = 20f;

    [Tooltip("Dökülürken kenarlara doğru hafif hız.")]
    [Min(0f)]
    [SerializeField]
    private float horizontalVelocity = 0.75f;

    [Tooltip("İlk açısal hız.")]
    [Min(0f)]
    [SerializeField]
    private float angularVelocity = 3f;

    [Header("Physics")]
    [Min(0.001f)]
    [SerializeField]
    private float simulationStep = 1f / 60f;

    [Tooltip("Bütün case'ler spawn olduktan sonra en fazla kaç saniye fizik çalışsın.")]
    [Range(1f, 30f)]
    [SerializeField]
    private float settleTime = 8f;

    [Tooltip("Case'leri batch batch bırakmak daha doğal ve daha stabil sonuç verir.")]
    [Range(1, 500)]
    [SerializeField]
    private int batchSize = 75;

    [Tooltip("Her batch bırakıldıktan sonra kaç saniye fizik çalışsın.")]
    [Range(0f, 3f)]
    [SerializeField]
    private float batchSimulationTime = 0.35f;

    [Header("Temporary Rigidbody")]
    [Min(0.001f)]
    [SerializeField]
    private float caseMass = 1f;

    [Range(0f, 5f)]
    [SerializeField]
    private float linearDamping = 0.05f;

    [Range(0f, 5f)]
    [SerializeField]
    private float angularDamping = 0.1f;

    [Header("Ground")]
    [Tooltip("Geçici physics floor'un üst yüzeyi.")]
    [SerializeField]
    private float groundY = 0f;

    [Tooltip("Scene'de ground collider olmasa bile temporary floor oluştur.")]
    [SerializeField]
    private bool createTemporaryGround = true;

    [Tooltip("Temporary floor boyutu.")]
    [SerializeField]
    private Vector2 temporaryGroundSize = new Vector2(50f, 50f);

    [Header("Seed")]
    [SerializeField]
    private int seed = 12345;

    [Header("Generated")]
    [SerializeField]
    private Transform generatedRoot;

    public Transform GeneratedRoot => generatedRoot;
    public int Amount => amount;

#if UNITY_EDITOR

    private sealed class SimulatedCase
    {
        public GameObject gameObject;
        public Rigidbody rigidbody;
        public bool rigidbodyWasAdded;
    }

    public void GeneratePhysicsPile()
    {
        if (!casePrefab)
        {
            Debug.LogWarning(
                "[GameCasePileGenerator] Case Prefab is missing.",
                this);

            return;
        }

        BoxCollider prefabCollider =
            casePrefab.GetComponentInChildren<BoxCollider>();

        if (!prefabCollider)
        {
            Debug.LogError(
                "[GameCasePileGenerator] Case prefab requires a BoxCollider.",
                casePrefab);

            return;
        }

        EnsureRoot();
        ClearPile();

        UnityEngine.Random.State oldRandomState =
            UnityEngine.Random.state;

        UnityEngine.Random.InitState(seed);

        SimulationMode oldSimulationMode =
            Physics.simulationMode;

        GameObject tempGround = null;

        var simulatedCases =
            new List<SimulatedCase>(amount);

        try
        {
            Physics.simulationMode =
                SimulationMode.Script;

            if (createTemporaryGround)
            {
                tempGround =
                    CreateTemporaryGround();
            }

            int spawned = 0;

            while (spawned < amount)
            {
                int spawnCount =
                    Mathf.Min(
                        batchSize,
                        amount - spawned);

                for (int i = 0; i < spawnCount; i++)
                {
                    SimulatedCase item =
                        SpawnPhysicsCase(spawned + i);

                    if (item != null)
                        simulatedCases.Add(item);
                }

                spawned += spawnCount;

                Physics.SyncTransforms();

                if (batchSimulationTime > 0f)
                {
                    SimulateForDuration(
                        batchSimulationTime,
                        simulatedCases,
                        $"Dropping cases... {spawned}/{amount}");
                }
            }

            Physics.SyncTransforms();

            SimulateUntilSettled(
                simulatedCases);

            Physics.SyncTransforms();

            BakeCases(simulatedCases);

            EditorUtility.SetDirty(this);
            SceneView.RepaintAll();

            Debug.Log(
                $"[GameCasePileGenerator] Physics pile baked. Cases: {simulatedCases.Count}",
                this);
        }
        catch (Exception exception)
        {
            Debug.LogException(
                exception,
                this);
        }
        finally
        {
            EditorUtility.ClearProgressBar();

            Physics.simulationMode =
                oldSimulationMode;

            UnityEngine.Random.state =
                oldRandomState;

            if (tempGround)
            {
                DestroyImmediate(tempGround);
            }
        }
    }

    public void RandomizeAndGenerate()
    {
        seed =
            Guid.NewGuid().GetHashCode();

        GeneratePhysicsPile();
    }

    private SimulatedCase SpawnPhysicsCase(
        int index)
    {
        GameObject instance =
            PrefabUtility.InstantiatePrefab(
                casePrefab,
                generatedRoot) as GameObject;

        if (!instance)
            return null;

        instance.name =
            $"{casePrefab.name}_{index:0000}";

        Transform t =
            instance.transform;

        //
        // SPAWN POSITION
        //

        Vector3 center =
            transform.position;

        float x =
            UnityEngine.Random.Range(
                -dropAreaSize.x * 0.5f,
                dropAreaSize.x * 0.5f);

        float z =
            UnityEngine.Random.Range(
                -dropAreaSize.y * 0.5f,
                dropAreaSize.y * 0.5f);

        float y =
            groundY +
            dropHeight +
            UnityEngine.Random.Range(
                0f,
                spawnVolumeHeight);

        t.position =
            new Vector3(
                center.x + x,
                y,
                center.z + z);

        //
        // INITIAL ROTATION
        //

        float yaw =
            UnityEngine.Random.Range(
                0f,
                360f);

        float pitch =
            UnityEngine.Random.Range(
                -initialTilt,
                initialTilt);

        float roll =
            UnityEngine.Random.Range(
                -initialTilt,
                initialTilt);

        t.rotation =
            Quaternion.Euler(
                pitch,
                yaw,
                roll);

        //
        // RIGIDBODY
        //

        Rigidbody rb =
            instance.GetComponent<Rigidbody>();

        bool addedRigidbody = false;

        if (!rb)
        {
            rb =
                instance.AddComponent<Rigidbody>();

            addedRigidbody = true;
        }

        rb.isKinematic = false;
        rb.useGravity = true;

        rb.mass =
            caseMass;

        rb.linearDamping =
            linearDamping;

        rb.angularDamping =
            angularDamping;

        rb.collisionDetectionMode =
            CollisionDetectionMode.Discrete;

        rb.interpolation =
            RigidbodyInterpolation.None;

        //
        // INITIAL VELOCITY
        //

        Vector2 randomDirection =
            UnityEngine.Random.insideUnitCircle;

        rb.linearVelocity =
            new Vector3(
                randomDirection.x *
                horizontalVelocity,

                0f,

                randomDirection.y *
                horizontalVelocity);

        rb.angularVelocity =
            UnityEngine.Random.insideUnitSphere *
            angularVelocity;

        return new SimulatedCase
        {
            gameObject = instance,
            rigidbody = rb,
            rigidbodyWasAdded = addedRigidbody
        };
    }

    private void SimulateForDuration(
        float duration,
        List<SimulatedCase> cases,
        string progressTitle)
    {
        int steps =
            Mathf.CeilToInt(
                duration / simulationStep);

        for (int i = 0; i < steps; i++)
        {
            Physics.Simulate(
                simulationStep);

            if (i % 10 == 0)
            {
                float progress =
                    i / (float)steps;

                bool cancel =
                    EditorUtility.DisplayCancelableProgressBar(
                        "Game Case Physics Bake",
                        progressTitle,
                        progress);

                if (cancel)
                    throw new OperationCanceledException(
                        "Physics bake cancelled.");
            }
        }
    }

    private void SimulateUntilSettled(
        List<SimulatedCase> cases)
    {
        int maxSteps =
            Mathf.CeilToInt(
                settleTime / simulationStep);

        int consecutiveSleepingFrames = 0;

        const int requiredSleepingFrames = 15;

        for (int step = 0;
             step < maxSteps;
             step++)
        {
            Physics.Simulate(
                simulationStep);

            bool allSleeping = true;

            for (int i = 0;
                 i < cases.Count;
                 i++)
            {
                Rigidbody rb =
                    cases[i].rigidbody;

                if (!rb)
                    continue;

                if (!rb.IsSleeping())
                {
                    allSleeping = false;
                    break;
                }
            }

            if (allSleeping)
            {
                consecutiveSleepingFrames++;

                if (consecutiveSleepingFrames >=
                    requiredSleepingFrames)
                {
                    break;
                }
            }
            else
            {
                consecutiveSleepingFrames = 0;
            }

            if (step % 10 == 0)
            {
                float progress =
                    step / (float)maxSteps;

                bool cancel =
                    EditorUtility.DisplayCancelableProgressBar(
                        "Game Case Physics Bake",
                        "Waiting for pile to settle...",
                        progress);

                if (cancel)
                    throw new OperationCanceledException(
                        "Physics bake cancelled.");
            }
        }
    }

    private void BakeCases(
        List<SimulatedCase> cases)
    {
        for (int i = 0;
             i < cases.Count;
             i++)
        {
            SimulatedCase item =
                cases[i];

            if (!item.gameObject)
                continue;

            Rigidbody rb =
                item.rigidbody;

            if (!rb)
                continue;

            //
            // Rigidbody bizim tarafımızdan eklendiyse
            // bake sonrası tamamen kaldır.
            //

            if (item.rigidbodyWasAdded)
            {
                DestroyImmediate(rb);
            }
            else
            {
                //
                // Prefab zaten Rigidbody içeriyorsa,
                // sahnede fizik devam etmesin.
                //
                rb.linearVelocity =
                    Vector3.zero;

                rb.angularVelocity =
                    Vector3.zero;

                rb.isKinematic =
                    true;
            }

            EditorUtility.SetDirty(
                item.gameObject.transform);
        }
    }

    private GameObject CreateTemporaryGround()
    {
        GameObject ground =
            new GameObject(
                "__TEMP_GameCasePhysicsGround");

        ground.hideFlags =
            HideFlags.HideAndDontSave;

        BoxCollider collider =
            ground.AddComponent<BoxCollider>();

        const float thickness = 1f;

        collider.size =
            new Vector3(
                temporaryGroundSize.x,
                thickness,
                temporaryGroundSize.y);

        //
        // Collider'ın TOP surface'i tam groundY olsun.
        //

        ground.transform.position =
            new Vector3(
                transform.position.x,
                groundY - thickness * 0.5f,
                transform.position.z);

        return ground;
    }

    public void ClearPile()
    {
        if (!generatedRoot)
            return;

        for (int i =
                 generatedRoot.childCount - 1;
             i >= 0;
             i--)
        {
            Transform child =
                generatedRoot.GetChild(i);

            if (child)
                DestroyImmediate(
                    child.gameObject);
        }

        EditorUtility.SetDirty(this);
        SceneView.RepaintAll();
    }

    public void RemoveRoot()
    {
        if (!generatedRoot)
            return;

        GameObject root =
            generatedRoot.gameObject;

        generatedRoot = null;

        DestroyImmediate(root);

        EditorUtility.SetDirty(this);
        SceneView.RepaintAll();
    }

    private void EnsureRoot()
    {
        if (generatedRoot)
            return;

        Transform existing =
            transform.Find(
                "GameCaseRoot");

        if (existing)
        {
            generatedRoot =
                existing;

            return;
        }

        GameObject root =
            new GameObject(
                "GameCaseRoot");

        root.transform.SetParent(
            transform);

        root.transform.localPosition =
            Vector3.zero;

        root.transform.localRotation =
            Quaternion.identity;

        root.transform.localScale =
            Vector3.one;

        generatedRoot =
            root.transform;

        EditorUtility.SetDirty(this);
    }

#endif
}