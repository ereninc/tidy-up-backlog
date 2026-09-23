using System;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local visual response to the replicated reveal command. Arrow objects
    /// are pooled and one manager follows every target in one LateUpdate.
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
            public Transform Arrow;
            public float Phase;
        }

        [Header("Prefab")]
        [SerializeField] private GameObject arrowPrefab;
        [SerializeField] private Transform poolRoot;
        [SerializeField, Min(0)] private int prewarmCount = 32;

        [Header("Pose")]
        [SerializeField] private Vector3 worldOffset =
            new Vector3(0f, 2f, 0f);
        [SerializeField] private Vector3 worldEulerAngles;
        [SerializeField, Min(0f)] private float bobDistance = 0.12f;
        [SerializeField, Min(0f)] private float bobSpeed = 3f;

        private readonly Stack<Transform> _available =
            new Stack<Transform>();
        private readonly List<ActiveIndicator> _active =
            new List<ActiveIndicator>();

        private NetworkSharedSkillService _service;
        private GameCaseInstanceRegistry _registry;
        private uint _pendingSequence;
        private GameCaseRevealState _pendingReveal;
        private double _activeExpiresAt;

        private void Awake()
        {
            if (poolRoot == null)
            {
                poolRoot = transform;
            }

            Prewarm();
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

            if (_active.Count > 0 &&
                (_service == null ||
                 _service.ServerTimeNow >= _activeExpiresAt))
            {
                ReleaseAll();
            }
        }

        private void LateUpdate()
        {
            if (_active.Count == 0)
            {
                return;
            }

            Quaternion rotation = Quaternion.Euler(worldEulerAngles);
            float time = Time.unscaledTime * bobSpeed;

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveIndicator indicator = _active[i];

                if (indicator.Target == null || indicator.Arrow == null)
                {
                    ReleaseAt(i);
                    continue;
                }

                float bob = bobDistance > 0f
                    ? Mathf.Sin(time + indicator.Phase) * bobDistance
                    : 0f;

                indicator.Arrow.SetPositionAndRotation(
                    indicator.Target.transform.position + worldOffset +
                    Vector3.up * bob,
                    rotation);
            }
        }

        private void OnDisable()
        {
            UnbindSources();
            ReleaseAll();
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
            ReleaseAll();

            if (_registry == null || _service == null ||
                arrowPrefab == null ||
                !_registry.TryGetCases(
                    reveal.TargetAppId,
                    out IReadOnlyList<NetworkGameCase> matches))
            {
                return;
            }

            GameCaseRevealTargetMode targetMode =
                GameCaseRevealTargetMode.AllInstances;

            if (_service.Catalog != null &&
                _service.Catalog.TryGetSkill(
                    reveal.SkillId,
                    out SharedSkillDefinition definition))
            {
                targetMode = definition.RevealTargetMode;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                NetworkGameCase gameCase = matches[i];

                if (!IsAllowed(gameCase, targetMode))
                {
                    continue;
                }

                Transform arrow = GetArrow();
                arrow.gameObject.SetActive(true);

                _active.Add(new ActiveIndicator
                {
                    Target = gameCase,
                    Arrow = arrow,
                    Phase = i * 0.37f
                });
            }

            _activeExpiresAt = reveal.ExpiresAt;
        }

        private static bool IsAllowed(
            NetworkGameCase gameCase,
            GameCaseRevealTargetMode mode)
        {
            if (gameCase == null ||
                !gameCase.TryGetComponent(
                    out NetworkWorldItem worldItem))
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

            for (int i = _available.Count;
                 i < prewarmCount;
                 i++)
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

        private void ReleaseAt(int index)
        {
            ActiveIndicator indicator = _active[index];
            _active.RemoveAt(index);

            if (indicator.Arrow == null)
            {
                return;
            }

            indicator.Arrow.gameObject.SetActive(false);
            indicator.Arrow.SetParent(
                poolRoot != null ? poolRoot : transform,
                false);
            _available.Push(indicator.Arrow);
        }

        private void ReleaseAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ReleaseAt(i);
            }

            _activeExpiresAt = 0d;
        }
    }
}
