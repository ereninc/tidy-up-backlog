using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class GameCaseDistributionGenerator : MonoBehaviour
{
    public enum CountDistributionMode
    {
        PerZone,
        TargetTotalByArea
    }

    [Header("Prefab")]
    [SerializeField] private GameObject casePrefab;

    [Header("Distribution")]
    [SerializeField] private CountDistributionMode distributionMode = CountDistributionMode.PerZone;
    [Min(0)]
    [SerializeField] private int targetTotal = 4000;
    [SerializeField] private int seed = 12345;

    [Header("Drop")]
    [Min(0f)]
    [SerializeField] private float dropHeight = 3f;
    [Min(0f)]
    [SerializeField] private float spawnVolumeHeight = 2f;
    [Range(0f, 90f)]
    [SerializeField] private float initialTilt = 20f;
    [Min(0f)]
    [SerializeField] private float horizontalVelocity = 0.75f;
    [Min(0f)]
    [SerializeField] private float angularVelocity = 3f;

    [Header("Physics")]
    [Min(0.001f)]
    [SerializeField] private float simulationStep = 1f / 60f;
    [Range(0.1f, 30f)]
    [SerializeField] private float settleTime = 8f;
    [Range(1, 500)]
    [SerializeField] private int batchSize = 75;
    [Range(0f, 3f)]
    [SerializeField] private float batchSimulationTime = 0.35f;
    [Min(1)]
    [SerializeField] private int pointSampleAttempts = 60;

    [Header("Temporary Rigidbody")]
    [Min(0.001f)]
    [SerializeField] private float caseMass = 1f;
    [Range(0f, 5f)]
    [SerializeField] private float linearDamping = 0.05f;
    [Range(0f, 5f)]
    [SerializeField] private float angularDamping = 0.1f;
    [SerializeField] private bool keepPrefabRigidbodyKinematicAfterBake = true;

    [Header("Escape Safety")]
    [Tooltip("Allows a natural spill around the drawn polygon, but rescues cases that travel farther than this.")]
    [Min(0f)]
    [SerializeField] private float allowedSpillDistance = 1.5f;
    [Tooltip("A case below its own sampled floor height by this amount is considered to have fallen through the map.")]
    [FormerlySerializedAs("maximumAllowedFallBelowZone")]
    [Min(0.01f)]
    [SerializeField] private float maximumAllowedFallBelowSurface = 0.25f;
    [Tooltip("Height above a newly sampled valid surface when an escaped case is rescued.")]
    [Min(0.05f)]
    [SerializeField] private float rescueDropHeight = 0.75f;
    [Tooltip("Extra simulation time guaranteed after the most recent rescue, so corrected cases cannot remain in the air.")]
    [Min(0.1f)]
    [SerializeField] private float postRescueSettleTime = 1.5f;
    [Tooltip("Discrete preserves the original loose pile behavior. Use Continuous Speculative only if the rescue system triggers too often on very thin floors.")]
    [SerializeField] private CollisionDetectionMode collisionDetectionDuringBake = CollisionDetectionMode.Discrete;

    [Header("Safety")]
    [Tooltip("Temporarily freezes unrelated dynamic rigidbodies while editor physics is simulated.")]
    [SerializeField] private bool freezeOtherDynamicRigidbodies = true;

    [Header("Roots")]
    [SerializeField] private Transform zonesRoot;
    [SerializeField] private Transform generatedRoot;

    public GameObject CasePrefab => casePrefab;
    public CountDistributionMode DistributionMode => distributionMode;
    public int TargetTotal => targetTotal;
    public int Seed => seed;
    public float DropHeight => dropHeight;
    public float SpawnVolumeHeight => spawnVolumeHeight;
    public float InitialTilt => initialTilt;
    public float HorizontalVelocity => horizontalVelocity;
    public float AngularVelocity => angularVelocity;
    public float SimulationStep => simulationStep;
    public float SettleTime => settleTime;
    public int BatchSize => batchSize;
    public float BatchSimulationTime => batchSimulationTime;
    public int PointSampleAttempts => pointSampleAttempts;
    public float CaseMass => caseMass;
    public float LinearDamping => linearDamping;
    public float AngularDamping => angularDamping;
    public bool KeepPrefabRigidbodyKinematicAfterBake => keepPrefabRigidbodyKinematicAfterBake;
    public float AllowedSpillDistance => allowedSpillDistance;
    public float MaximumAllowedFallBelowSurface => maximumAllowedFallBelowSurface;
    public float RescueDropHeight => rescueDropHeight;
    public float PostRescueSettleTime => postRescueSettleTime;
    public CollisionDetectionMode BakeCollisionDetection => collisionDetectionDuringBake;
    public bool FreezeOtherDynamicRigidbodies => freezeOtherDynamicRigidbodies;
    public Transform ZonesRoot => zonesRoot;
    public Transform GeneratedRoot => generatedRoot;

    public GameCaseSpawnZone[] GetZones(bool includeInactive = true)
    {
        Transform searchRoot = zonesRoot ? zonesRoot : transform;
        return searchRoot.GetComponentsInChildren<GameCaseSpawnZone>(includeInactive);
    }

    public Transform EnsureZonesRoot()
    {
        if (zonesRoot)
            return zonesRoot;

        Transform existing = transform.Find("Zones");

        if (existing)
        {
            zonesRoot = existing;
            return zonesRoot;
        }

        var root = new GameObject("Zones");
        root.transform.SetParent(transform, false);
        zonesRoot = root.transform;
        return zonesRoot;
    }

    public Transform EnsureGeneratedRoot()
    {
        if (generatedRoot)
            return generatedRoot;

        Transform existing = transform.Find("Generated");

        if (existing)
        {
            generatedRoot = existing;
            return generatedRoot;
        }

        var root = new GameObject("Generated");
        root.transform.SetParent(transform, false);
        generatedRoot = root.transform;
        return generatedRoot;
    }

    private void Reset()
    {
        EnsureZonesRoot();
        EnsureGeneratedRoot();
    }

    private void OnValidate()
    {
        targetTotal = Mathf.Max(0, targetTotal);
        simulationStep = Mathf.Max(0.001f, simulationStep);
        batchSize = Mathf.Max(1, batchSize);
        pointSampleAttempts = Mathf.Max(1, pointSampleAttempts);
        caseMass = Mathf.Max(0.001f, caseMass);
        allowedSpillDistance = Mathf.Max(0f, allowedSpillDistance);
        maximumAllowedFallBelowSurface = Mathf.Max(0.01f, maximumAllowedFallBelowSurface);
        rescueDropHeight = Mathf.Max(0.05f, rescueDropHeight);
        postRescueSettleTime = Mathf.Max(0.1f, postRescueSettleTime);
    }
}
