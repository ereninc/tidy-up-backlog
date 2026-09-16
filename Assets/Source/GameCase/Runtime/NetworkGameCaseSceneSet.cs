using System;
using System.Collections;
using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Network Game Case Scene Set")]
    public sealed class NetworkGameCaseSceneSet :
        MonoBehaviour
    {
        [Header("Scene Cases")]
        [SerializeField]
        private NetworkGameCase[] cases =
            Array.Empty<NetworkGameCase>();

        [SerializeField, Min(1)]
        private int bindingsPerFrame = 150;

        [Header("Direct GameScene Test")]
        [Tooltip(
            "Used only when GameScene is played directly without LoadingScene.")]
        [SerializeField]
        private uint[] directSceneTestAppIds =
            Array.Empty<uint>();

        [SerializeField, Min(1)]
        private int directSceneTestCopiesPerGame = 10;

        [SerializeField]
        private uint directSceneTestSeed = 12345;

        public int BoundCaseCount { get; private set; }

        public float Progress { get; private set; }

        public bool IsReady { get; private set; }

        public event Action<float>
            ProgressChanged;

        public event Action
            Ready;

        private bool _directGameScene;

        private bool _startupFailed;

        private IEnumerator Start()
        {
            BoundCaseCount =
                0;

            Progress =
                0f;

            IsReady =
                false;

            _directGameScene =
                !GameCaseSessionPlan.IsReady;

            if (_directGameScene)
            {
                if (!TryCreateDirectScenePlan())
                {
                    yield break;
                }
            }

            yield return
                EnsureCoverArrayReady();

            if (_startupFailed)
            {
                yield break;
            }

            int[] assignment =
                GameCaseSelectionUtility
                    .BuildExactAssignment(
                        GameCaseSessionPlan.AppIds.Count,
                        GameCaseSessionPlan.CopiesPerGame,
                        GameCaseSessionPlan.AssignmentSeed);

            if (cases == null ||
                cases.Length !=
                assignment.Length)
            {
                Debug.LogError(
                    $"Scene has {cases?.Length ?? 0} registered cases, " +
                    $"but manifest requires {assignment.Length}. " +
                    "Run Collect And Index Cases.",
                    this);

                yield break;
            }

            int batchSize =
                Mathf.Max(
                    1,
                    bindingsPerFrame);

            var usedSceneIndices =
                new bool[
                    assignment.Length];

            for (int i = 0;
                 i < cases.Length;
                 i++)
            {
                NetworkGameCase gameCase =
                    cases[i];

                if (!gameCase)
                {
                    Debug.LogError(
                        $"Game case array entry {i} is missing.",
                        this);

                    yield break;
                }

                int sceneIndex =
                    gameCase.SceneCaseIndex;

                if (sceneIndex < 0 ||
                    sceneIndex >=
                    assignment.Length)
                {
                    Debug.LogError(
                        $"{gameCase.name} has invalid scene index " +
                        $"{sceneIndex}.",
                        gameCase);

                    yield break;
                }

                if (usedSceneIndices[
                        sceneIndex])
                {
                    Debug.LogError(
                        $"Scene case index {sceneIndex} duplicated.",
                        gameCase);

                    yield break;
                }

                usedSceneIndices[
                    sceneIndex] =
                    true;

                int appIdIndex =
                    assignment[
                        sceneIndex];

                uint appId =
                    GameCaseSessionPlan.AppIds[
                        appIdIndex];

                uint playtimeMinutes =
                    GameCaseSessionPlaytimePlan
                        .GetOrDefault(appId);

                gameCase.BindAppId(
                    appId,
                    playtimeMinutes);

                BoundCaseCount =
                    i + 1;

                if (BoundCaseCount %
                    batchSize == 0)
                {
                    UpdateBindingProgress();

                    yield return null;
                }
            }

            Progress =
                1f;

            IsReady =
                true;

            ProgressChanged?.Invoke(
                Progress);

            Ready?.Invoke();

            Debug.Log(
                $"[GameCaseSceneSet] Bound {BoundCaseCount} cases.",
                this);
        }

        private bool TryCreateDirectScenePlan()
        {
            if (directSceneTestAppIds == null ||
                directSceneTestAppIds.Length == 0)
            {
                Debug.LogError(
                    "GameCaseSessionPlan is missing. " +
                    "Enter through LoadingScene or configure " +
                    "Direct Scene Test AppIds.",
                    this);

                return false;
            }

            GameCaseSessionPlan.Set(
                directSceneTestAppIds,
                directSceneTestCopiesPerGame,
                directSceneTestSeed);

            /*
             * Direct GameScene test has no Steam library
             * metadata, so those cases default to 0h/navy.
             */
            GameCaseSessionPlaytimePlan.Clear();

            return true;
        }

        private IEnumerator EnsureCoverArrayReady()
        {
            GameCaseCoverCache cache =
                GameCaseCoverCache.GetOrCreate();

            if (HasAllRequiredSlices(
                    cache))
            {
                yield break;
            }

            if (!_directGameScene)
            {
                Debug.LogError(
                    "[GameCaseSceneSet] Cover Texture2DArray was not " +
                    "ready when GameScene opened.",
                    this);

                _startupFailed =
                    true;

                yield break;
            }

            cache.SetRequestsEnabled(
                true);

            bool preloadComplete =
                false;

            cache.Preload(
                GameCaseSessionPlan.AppIds,
                preloadProgress =>
                {
                    Progress =
                        Mathf.Lerp(
                            0f,
                            0.25f,
                            preloadProgress);

                    ProgressChanged?.Invoke(
                        Progress);
                },
                () =>
                {
                    preloadComplete =
                        true;
                });

            while (!preloadComplete)
            {
                yield return null;
            }

            if (!cache.BuildCoverArray(
                    GameCaseSessionPlan.AppIds))
            {
                Debug.LogError(
                    "[GameCaseSceneSet] Could not build cover array.",
                    this);

                _startupFailed =
                    true;

                yield break;
            }

            Progress =
                0.25f;

            ProgressChanged?.Invoke(
                Progress);
        }

        private static bool HasAllRequiredSlices(
            GameCaseCoverCache cache)
        {
            if (cache == null ||
                !cache.HasCoverArray ||
                !GameCaseSessionPlan.IsReady)
            {
                return false;
            }

            for (int i = 0;
                 i <
                 GameCaseSessionPlan.AppIds.Count;
                 i++)
            {
                if (!cache.TryGetSlice(
                        GameCaseSessionPlan.AppIds[i],
                        out _))
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdateBindingProgress()
        {
            if (cases == null ||
                cases.Length == 0)
            {
                Progress =
                    1f;

                ProgressChanged?.Invoke(
                    Progress);

                return;
            }

            float bindingProgress =
                (float)BoundCaseCount /
                cases.Length;

            Progress =
                _directGameScene
                    ? Mathf.Lerp(
                        0.25f,
                        1f,
                        bindingProgress)
                    : bindingProgress;

            ProgressChanged?.Invoke(
                Progress);
        }

#if UNITY_EDITOR

        [ContextMenu(
            "Collect And Index Cases")]
        private void CollectAndIndexCases()
        {
            UnityEditor.Undo.RecordObject(
                this,
                "Collect Game Cases");

            cases =
                GetComponentsInChildren<NetworkGameCase>(
                    true);

            for (int i = 0;
                 i < cases.Length;
                 i++)
            {
                if (!cases[i])
                {
                    continue;
                }

                UnityEditor.Undo.RecordObject(
                    cases[i],
                    "Index Game Case");

                cases[i]
                    .EditorSetSceneCaseIndex(
                        i);

                UnityEditor.EditorUtility.SetDirty(
                    cases[i]);
            }

            UnityEditor.EditorUtility.SetDirty(
                this);

            Debug.Log(
                $"[GameCaseSceneSet] Collected and indexed " +
                $"{cases.Length} cases.",
                this);
        }

#endif
    }
}