using System;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local presentation of the replicated reveal command. One manager owns
    /// every pooled arrow and every levitation pose, so revealed cases do not
    /// need their own Update or a NetworkTransform stream.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Skills/Game Case Reveal Indicator Pool")]
    public sealed class GameCaseRevealIndicatorPool : MonoBehaviour
    {
        [Serializable]
        private sealed class ActiveIndicator
        {
            public NetworkGameCase Target;
            public NetworkWorldItem WorldItem;
            public Transform Arrow;
            public float Phase;

            public bool Levitate;
            public Transform StartParent;
            public Vector3 StartPosition;
            public Quaternion StartRotation;

            public Rigidbody Body;
            public bool WasKinematic;
            public bool UsedGravity;

            public Collider[] Colliders;
            public bool[] ColliderWasTrigger;
            public bool[] ColliderTriggerOverridden;
        }

        [Header("Prefab")]
        [SerializeField] private GameObject arrowPrefab;
        [SerializeField] private Transform poolRoot;
        [SerializeField, Min(0)] private int prewarmCount = 32;

        [Header("Arrow Pose")]
        [Tooltip("Used for targets that are not being levitated.")]
        [SerializeField] private Vector3 worldOffset =
            new Vector3(0f, 2f, 0f);
        [Tooltip("Used above a loose case after the case itself has risen.")]
        [SerializeField] private Vector3 levitatingArrowOffset =
            new Vector3(0f, 0.7f, 0f);
        [SerializeField] private Vector3 worldEulerAngles;
        [SerializeField, Min(0f)] private float bobDistance = 0.12f;
        [SerializeField, Min(0f)] private float bobSpeed = 3f;

        [Header("Loose Case Levitation")]
        [Tooltip(
            "Only loose world cases rise. Held and shelf-placed cases are " +
            "never moved by this presenter.")]
        [SerializeField] private bool levitateLooseWorldCases = true;
        [SerializeField, Min(0f)] private float liftHeight = 1.75f;
        [SerializeField, Min(0.01f)] private float riseDuration = 0.45f;
        [SerializeField, Min(0.01f)] private float returnDuration = 0.65f;

        [Header("Levitation Idle Motion")]
        [SerializeField, Min(0f)] private float hoverDistance = 0.08f;
        [SerializeField, Min(0f)] private float hoverSpeed = 2.2f;
        [SerializeField] private Vector3 hoverRotationDegrees =
            new Vector3(2.5f, 7f, 2.5f);
        [SerializeField, Min(0f)] private float hoverRotationSpeed = 1.35f;

        [Header("Levitation Collision")]
        [Tooltip(
            "Turns supported colliders into triggers while a case rises, so " +
            "it does not shove the pile or player. NetworkInteractionController " +
            "must keep Query Triggers set to Collide (its default).")]
        [SerializeField] private bool phaseThroughWorldWhileLevitating = true;

        private readonly Stack<Transform> _available =
            new Stack<Transform>();
        private readonly List<ActiveIndicator> _active =
            new List<ActiveIndicator>();

        private NetworkSharedSkillService _service;
        private GameCaseInstanceRegistry _registry;
        private uint _pendingSequence;
        private GameCaseRevealState _pendingReveal;
        private double _activeStartedAt;
        private double _activeExpiresAt;

        private void Awake()
        {
            if (poolRoot == null)
            {
                poolRoot = transform;
            }

            Prewarm();
        }

        private void OnValidate()
        {
            prewarmCount = Mathf.Max(0, prewarmCount);
            bobDistance = Mathf.Max(0f, bobDistance);
            bobSpeed = Mathf.Max(0f, bobSpeed);
            liftHeight = Mathf.Max(0f, liftHeight);
            riseDuration = Mathf.Max(0.01f, riseDuration);
            returnDuration = Mathf.Max(0.01f, returnDuration);
            hoverDistance = Mathf.Max(0f, hoverDistance);
            hoverSpeed = Mathf.Max(0f, hoverSpeed);
            hoverRotationSpeed = Mathf.Max(0f, hoverRotationSpeed);
        }

        private void OnEnable()
        {
            RebindSources();
        }

        private void Update()
        {
            if (_service != NetworkSharedSkillService.Instance ||
                _registry != GameCaseInstanceRegistry.Instance)
            {
                RebindSources();
            }

            if (_pendingSequence != 0 &&
                _registry != null && _registry.IsReady)
            {
                PresentReveal(_pendingReveal);
                _pendingSequence = 0;
            }

            if (_active.Count > 0 && _service == null)
            {
                ReleaseAll(true);
            }
        }

        private void LateUpdate()
        {
            if (_active.Count == 0 || _service == null)
            {
                return;
            }

            double now = _service.ServerTimeNow;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveIndicator indicator = _active[i];

                if (indicator.Target == null ||
                    indicator.WorldItem == null)
                {
                    ReleaseAt(i, false);
                    continue;
                }

                if (indicator.Levitate &&
                    !CanContinueLevitation(indicator))
                {
                    // Pickup or placement now owns this transform.
                    ReleaseAt(i, false);
                    continue;
                }

                if (now < _activeExpiresAt)
                {
                    if (indicator.Levitate)
                    {
                        EvaluateLevitationPose(
                            indicator,
                            now,
                            out Vector3 position,
                            out Quaternion rotation);
                        ApplyPose(indicator, position, rotation);
                    }

                    UpdateArrow(indicator, now);
                    continue;
                }

                ReleaseArrow(indicator);

                if (!indicator.Levitate)
                {
                    ReleaseAt(i, false);
                    continue;
                }

                float returnProgress = Mathf.Clamp01(
                    (float)((now - _activeExpiresAt) /
                            Math.Max(0.01d, returnDuration)));
                float easedReturn = SmoothStep01(returnProgress);

                EvaluateLevitationPose(
                    indicator,
                    _activeExpiresAt,
                    out Vector3 returnStartPosition,
                    out Quaternion returnStartRotation);

                Vector3 returnedPosition = Vector3.LerpUnclamped(
                    returnStartPosition,
                    indicator.StartPosition,
                    easedReturn);
                Quaternion returnedRotation = Quaternion.SlerpUnclamped(
                    returnStartRotation,
                    indicator.StartRotation,
                    easedReturn);

                ApplyPose(indicator, returnedPosition, returnedRotation);

                if (returnProgress >= 1f)
                {
                    ApplyPose(
                        indicator,
                        indicator.StartPosition,
                        indicator.StartRotation);
                    ReleaseAt(i, false);
                }
            }
        }

        private void OnDisable()
        {
            UnbindSources();
            ReleaseAll(true);
            _pendingSequence = 0;
        }

        private void RebindSources()
        {
            UnbindSources();

            _service = NetworkSharedSkillService.Instance;
            _registry = GameCaseInstanceRegistry.Instance;

            if (_service != null)
            {
                _service.RevealStateChanged += HandleRevealChanged;

                if (_service.RevealState.IsValid &&
                    _service.RevealState.ExpiresAt >
                    _service.ServerTimeNow)
                {
                    QueueReveal(_service.RevealState);
                }
            }

            if (_registry != null)
            {
                _registry.Rebuilt += HandleRegistryRebuilt;
            }
        }

        private void UnbindSources()
        {
            if (_service != null)
            {
                _service.RevealStateChanged -= HandleRevealChanged;
            }

            if (_registry != null)
            {
                _registry.Rebuilt -= HandleRegistryRebuilt;
            }

            _service = null;
            _registry = null;
        }

        private void HandleRevealChanged(
            GameCaseRevealState previous,
            GameCaseRevealState current)
        {
            QueueReveal(current);
        }

        private void HandleRegistryRebuilt()
        {
            if (_pendingSequence != 0)
            {
                PresentReveal(_pendingReveal);
                _pendingSequence = 0;
            }
        }

        private void QueueReveal(GameCaseRevealState reveal)
        {
            if (!reveal.IsValid || _service == null ||
                reveal.ExpiresAt <= _service.ServerTimeNow)
            {
                return;
            }

            _pendingReveal = reveal;
            _pendingSequence = reveal.Sequence;

            if (_registry != null && _registry.IsReady)
            {
                PresentReveal(reveal);
                _pendingSequence = 0;
            }
        }

        private void PresentReveal(GameCaseRevealState reveal)
        {
            ReleaseAll(true);

            if (_registry == null || _service == null ||
                !_registry.TryGetCases(
                    reveal.TargetAppId,
                    out IReadOnlyList<NetworkGameCase> matches))
            {
                return;
            }

            GameCaseRevealTargetMode targetMode =
                GameCaseRevealTargetMode.AllInstances;
            float effectDuration = Mathf.Max(
                0.1f,
                (float)(reveal.ExpiresAt - _service.ServerTimeNow));

            if (_service.Catalog != null &&
                _service.Catalog.TryGetSkill(
                    reveal.SkillId,
                    out SharedSkillDefinition definition))
            {
                targetMode = definition.RevealTargetMode;
                effectDuration = definition.EffectDurationSeconds;
            }

            _activeStartedAt = reveal.ExpiresAt - effectDuration;
            _activeExpiresAt = reveal.ExpiresAt;

            for (int i = 0; i < matches.Count; i++)
            {
                NetworkGameCase gameCase = matches[i];

                if (!IsAllowed(
                        gameCase,
                        targetMode,
                        out NetworkWorldItem worldItem))
                {
                    continue;
                }

                Transform arrow = null;

                if (arrowPrefab != null)
                {
                    arrow = GetArrow();
                    arrow.gameObject.SetActive(true);
                }

                var indicator = new ActiveIndicator
                {
                    Target = gameCase,
                    WorldItem = worldItem,
                    Arrow = arrow,
                    // SceneCaseIndex is deterministic on every peer. Registry
                    // iteration order is intentionally unspecified.
                    Phase = gameCase.SceneCaseIndex * 0.37f
                };

                TryBeginLevitation(indicator);

                if (indicator.Arrow != null || indicator.Levitate)
                {
                    _active.Add(indicator);
                }
            }

            if (_active.Count == 0)
            {
                _activeStartedAt = 0d;
                _activeExpiresAt = 0d;
            }
        }

        private void TryBeginLevitation(ActiveIndicator indicator)
        {
            if (!levitateLooseWorldCases || indicator.Target == null ||
                indicator.WorldItem == null ||
                indicator.WorldItem.IsHeld || indicator.WorldItem.IsPlaced)
            {
                return;
            }

            Transform targetTransform = indicator.Target.transform;
            indicator.Levitate = true;
            indicator.StartParent = targetTransform.parent;
            indicator.StartPosition = targetTransform.position;
            indicator.StartRotation = targetTransform.rotation;

            indicator.Body = indicator.Target.GetComponent<Rigidbody>();

            if (indicator.Body != null)
            {
                indicator.WasKinematic = indicator.Body.isKinematic;
                indicator.UsedGravity = indicator.Body.useGravity;
                indicator.Body.isKinematic = true;
                indicator.Body.useGravity = false;
                indicator.Body.Sleep();
            }

            if (!phaseThroughWorldWhileLevitating)
            {
                return;
            }

            indicator.Colliders =
                indicator.Target.GetComponentsInChildren<Collider>(true);
            indicator.ColliderWasTrigger =
                new bool[indicator.Colliders.Length];
            indicator.ColliderTriggerOverridden =
                new bool[indicator.Colliders.Length];

            for (int i = 0; i < indicator.Colliders.Length; i++)
            {
                Collider targetCollider = indicator.Colliders[i];

                if (targetCollider == null)
                {
                    continue;
                }

                indicator.ColliderWasTrigger[i] = targetCollider.isTrigger;

                // Unity cannot use a non-convex MeshCollider as a trigger.
                if (targetCollider is MeshCollider meshCollider &&
                    !meshCollider.convex)
                {
                    continue;
                }

                targetCollider.isTrigger = true;
                indicator.ColliderTriggerOverridden[i] = true;
            }
        }

        private static bool CanContinueLevitation(
            ActiveIndicator indicator)
        {
            return indicator.Target != null &&
                   indicator.WorldItem != null &&
                   !indicator.WorldItem.IsHeld &&
                   !indicator.WorldItem.IsPlaced &&
                   indicator.Target.transform.parent == indicator.StartParent;
        }

        private void EvaluateLevitationPose(
            ActiveIndicator indicator,
            double sampleTime,
            out Vector3 position,
            out Quaternion rotation)
        {
            float riseProgress = Mathf.Clamp01(
                (float)((sampleTime - _activeStartedAt) /
                        Math.Max(0.01d, riseDuration)));
            float easedRise = SmoothStep01(riseProgress);
            float time = (float)(sampleTime % 10000d);

            float hover = hoverDistance > 0f
                ? Mathf.Sin(
                    time * hoverSpeed + indicator.Phase) *
                  hoverDistance * easedRise
                : 0f;

            position = indicator.StartPosition +
                       Vector3.up * (liftHeight * easedRise + hover);

            Vector3 idleEuler = new Vector3(
                Mathf.Sin(
                    time * hoverRotationSpeed + indicator.Phase) *
                hoverRotationDegrees.x,
                Mathf.Sin(
                    time * hoverRotationSpeed * 0.83f +
                    indicator.Phase + 2.1f) *
                hoverRotationDegrees.y,
                Mathf.Sin(
                    time * hoverRotationSpeed * 1.17f +
                    indicator.Phase + 4.2f) *
                hoverRotationDegrees.z) * easedRise;

            rotation = indicator.StartRotation * Quaternion.Euler(idleEuler);
        }

        private static void ApplyPose(
            ActiveIndicator indicator,
            Vector3 position,
            Quaternion rotation)
        {
            if (indicator.Body != null)
            {
                indicator.Body.position = position;
                indicator.Body.rotation = rotation;
                return;
            }

            if (indicator.Target != null)
            {
                indicator.Target.transform.SetPositionAndRotation(
                    position,
                    rotation);
            }
        }

        private void UpdateArrow(
            ActiveIndicator indicator,
            double serverTime)
        {
            if (indicator.Arrow == null || indicator.Target == null)
            {
                return;
            }

            float time = (float)(serverTime % 10000d);
            float bob = bobDistance > 0f
                ? Mathf.Sin(
                    time * bobSpeed + indicator.Phase) * bobDistance
                : 0f;
            Vector3 offset = indicator.Levitate
                ? levitatingArrowOffset
                : worldOffset;

            indicator.Arrow.SetPositionAndRotation(
                indicator.Target.transform.position + offset +
                Vector3.up * bob,
                Quaternion.Euler(worldEulerAngles));
        }

        private static bool IsAllowed(
            NetworkGameCase gameCase,
            GameCaseRevealTargetMode mode,
            out NetworkWorldItem worldItem)
        {
            worldItem = null;

            if (gameCase == null ||
                !gameCase.TryGetComponent(out worldItem))
            {
                return false;
            }

            switch (mode)
            {
                case GameCaseRevealTargetMode.NotHeld:
                    return !worldItem.IsHeld;

                case GameCaseRevealTargetMode.LooseWorldOnly:
                    return !worldItem.IsHeld && !worldItem.IsPlaced;

                default:
                    return true;
            }
        }

        private void Prewarm()
        {
            if (arrowPrefab == null)
            {
                return;
            }

            for (int i = _available.Count; i < prewarmCount; i++)
            {
                Transform arrow = CreateArrow();
                arrow.gameObject.SetActive(false);
                _available.Push(arrow);
            }
        }

        private Transform GetArrow()
        {
            return _available.Count > 0
                ? _available.Pop()
                : CreateArrow();
        }

        private Transform CreateArrow()
        {
            GameObject instance = Instantiate(
                arrowPrefab,
                poolRoot != null ? poolRoot : transform);
            instance.name = arrowPrefab.name + "_Pooled";
            return instance.transform;
        }

        private void ReleaseArrow(ActiveIndicator indicator)
        {
            if (indicator.Arrow == null)
            {
                return;
            }

            indicator.Arrow.gameObject.SetActive(false);
            indicator.Arrow.SetParent(
                poolRoot != null ? poolRoot : transform,
                false);
            _available.Push(indicator.Arrow);
            indicator.Arrow = null;
        }

        private void ReleaseAt(int index, bool restoreOriginalPose)
        {
            ActiveIndicator indicator = _active[index];
            _active.RemoveAt(index);

            ReleaseArrow(indicator);

            if (restoreOriginalPose && indicator.Levitate &&
                CanContinueLevitation(indicator))
            {
                ApplyPose(
                    indicator,
                    indicator.StartPosition,
                    indicator.StartRotation);
            }

            RestoreLevitationOverrides(indicator);

            if (_active.Count == 0)
            {
                _activeStartedAt = 0d;
                _activeExpiresAt = 0d;
            }
        }

        private static void RestoreLevitationOverrides(
            ActiveIndicator indicator)
        {
            if (indicator.Colliders != null &&
                indicator.ColliderWasTrigger != null &&
                indicator.ColliderTriggerOverridden != null)
            {
                int count = Mathf.Min(
                    indicator.Colliders.Length,
                    Mathf.Min(
                        indicator.ColliderWasTrigger.Length,
                        indicator.ColliderTriggerOverridden.Length));

                for (int i = 0; i < count; i++)
                {
                    Collider targetCollider = indicator.Colliders[i];

                    if (targetCollider != null &&
                        indicator.ColliderTriggerOverridden[i])
                    {
                        targetCollider.isTrigger =
                            indicator.ColliderWasTrigger[i];
                    }
                }
            }

            if (indicator.Body == null)
            {
                return;
            }

            bool itemPresentationOwnsBody =
                indicator.WorldItem != null &&
                (indicator.WorldItem.IsHeld ||
                 indicator.WorldItem.IsPlaced ||
                 (indicator.Target != null &&
                  indicator.Target.transform.parent !=
                  indicator.StartParent));

            indicator.Body.useGravity = indicator.UsedGravity;
            indicator.Body.isKinematic = itemPresentationOwnsBody ||
                                         indicator.WasKinematic;

            if (indicator.Body.isKinematic)
            {
                indicator.Body.Sleep();
            }
        }

        private void ReleaseAll(bool restoreOriginalPose)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ReleaseAt(i, restoreOriginalPose);
            }

            _activeStartedAt = 0d;
            _activeExpiresAt = 0d;
        }

        private static float SmoothStep01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
