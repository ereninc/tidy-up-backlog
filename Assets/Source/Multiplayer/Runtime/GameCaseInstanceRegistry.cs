using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local, allocation-light AppId index. It scans the GameplayScene once
    /// after the deterministic case binding has completed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Skills/Game Case Instance Registry")]
    public sealed class GameCaseInstanceRegistry : MonoBehaviour
    {
        private readonly Dictionary<uint, List<NetworkGameCase>>
            _casesByAppId = new Dictionary<uint, List<NetworkGameCase>>();

        public static GameCaseInstanceRegistry Instance { get; private set; }
        public bool IsReady { get; private set; }
        public int RegisteredCaseCount { get; private set; }

        public event Action Rebuilt;

        private Coroutine _bindRoutine;
        private NetworkGameCaseSceneSet _sceneSet;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "Only one GameCaseInstanceRegistry may exist.",
                    this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += HandleSceneLoaded;
            BeginWaitingForBindings();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            StopBindingRoutine();
            UnsubscribeSceneSet();
            Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool TryGetCases(
            uint appId,
            out IReadOnlyList<NetworkGameCase> cases)
        {
            if (appId != 0 &&
                _casesByAppId.TryGetValue(appId, out var mutableCases))
            {
                cases = mutableCases;
                return true;
            }

            cases = Array.Empty<NetworkGameCase>();
            return false;
        }

        [ContextMenu("Rebuild Registry")]
        public void Rebuild()
        {
            _casesByAppId.Clear();
            RegisteredCaseCount = 0;

            NetworkGameCase[] gameCases =
                FindObjectsByType<NetworkGameCase>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            for (int i = 0; i < gameCases.Length; i++)
            {
                NetworkGameCase gameCase = gameCases[i];

                if (gameCase == null || gameCase.AppId == 0)
                {
                    continue;
                }

                if (!_casesByAppId.TryGetValue(
                        gameCase.AppId,
                        out List<NetworkGameCase> list))
                {
                    list = new List<NetworkGameCase>(
                        Mathf.Max(4, GameCaseSessionPlan.CopiesPerGame));
                    _casesByAppId.Add(gameCase.AppId, list);
                }

                list.Add(gameCase);
                RegisteredCaseCount++;
            }

            IsReady = RegisteredCaseCount > 0;
            Rebuilt?.Invoke();

            Debug.Log(
                $"[GameCaseRegistry] Indexed {RegisteredCaseCount} cases " +
                $"across {_casesByAppId.Count} AppIds.",
                this);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BeginWaitingForBindings();
        }

        private void BeginWaitingForBindings()
        {
            StopBindingRoutine();
            UnsubscribeSceneSet();
            Clear();
            _bindRoutine = StartCoroutine(WaitForSceneSet());
        }

        private IEnumerator WaitForSceneSet()
        {
            yield return null;

            _sceneSet = FindFirstObjectByType<NetworkGameCaseSceneSet>();

            if (_sceneSet == null)
            {
                Rebuild();
                yield break;
            }

            if (_sceneSet.IsReady)
            {
                Rebuild();
                yield break;
            }

            _sceneSet.Ready += HandleSceneSetReady;
        }

        private void HandleSceneSetReady()
        {
            UnsubscribeSceneSet();
            Rebuild();
        }

        private void UnsubscribeSceneSet()
        {
            if (_sceneSet != null)
            {
                _sceneSet.Ready -= HandleSceneSetReady;
                _sceneSet = null;
            }
        }

        private void StopBindingRoutine()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
        }

        private void Clear()
        {
            IsReady = false;
            RegisteredCaseCount = 0;
            _casesByAppId.Clear();
        }
    }
}
