using System;
using System.Collections;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Binds authored GameScene cases to the LoadingScene manifest. The array
    /// order is serialized, so every peer performs the same assignment locally.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Network Game Case Scene Set")]
    public sealed class NetworkGameCaseSceneSet : MonoBehaviour
    {
        [Header("Scene Cases")]
        [SerializeField] private NetworkGameCase[] cases =
            Array.Empty<NetworkGameCase>();

        [SerializeField, Min(1)] private int bindingsPerFrame = 150;

        [Header("Direct GameScene Test")]
        [Tooltip(
            "Used only when GameScene is played directly without LoadingScene.")]
        [SerializeField] private uint[] directSceneTestAppIds =
            Array.Empty<uint>();
        [SerializeField, Min(1)] private int directSceneTestCopiesPerGame = 10;
        [SerializeField] private uint directSceneTestSeed = 12345;

        public int BoundCaseCount { get; private set; }
        public float Progress { get; private set; }
        public bool IsReady { get; private set; }

        public event Action<float> ProgressChanged;
        public event Action Ready;

        private IEnumerator Start()
        {
            if (!GameCaseSessionPlan.IsReady)
            {
                if (directSceneTestAppIds == null ||
                    directSceneTestAppIds.Length == 0)
                {
                    Debug.LogError(
                        "GameCaseSessionPlan is missing. Enter through " +
                        "LoadingScene or configure Direct Scene Test AppIds.",
                        this);
                    yield break;
                }

                GameCaseSessionPlan.Set(
                    directSceneTestAppIds,
                    directSceneTestCopiesPerGame,
                    directSceneTestSeed);
            }

            int[] assignment =
                GameCaseSelectionUtility.BuildExactAssignment(
                    GameCaseSessionPlan.AppIds.Count,
                    GameCaseSessionPlan.CopiesPerGame,
                    GameCaseSessionPlan.AssignmentSeed);

            if (cases == null || cases.Length != assignment.Length)
            {
                Debug.LogError(
                    $"Scene has {cases?.Length ?? 0} registered cases, but " +
                    $"the manifest requires {assignment.Length}. " +
                    "Run Collect And Index Cases after duplicating them.",
                    this);
                yield break;
            }

            int batchSize = Mathf.Max(1, bindingsPerFrame);
            var usedSceneIndices = new bool[assignment.Length];

            for (int i = 0; i < cases.Length; i++)
            {
                NetworkGameCase gameCase = cases[i];

                if (gameCase == null)
                {
                    Debug.LogError(
                        $"Game case array entry {i} is missing.",
                        this);
                    yield break;
                }

                int sceneIndex = gameCase.SceneCaseIndex;

                if (sceneIndex < 0 || sceneIndex >= assignment.Length)
                {
                    Debug.LogError(
                        $"{gameCase.name} has invalid scene case index " +
                        $"{sceneIndex}.",
                        gameCase);
                    yield break;
                }

                if (usedSceneIndices[sceneIndex])
                {
                    Debug.LogError(
                        $"Scene case index {sceneIndex} is duplicated.",
                        gameCase);
                    yield break;
                }

                usedSceneIndices[sceneIndex] = true;
                uint appId =
                    GameCaseSessionPlan.AppIds[assignment[sceneIndex]];
                gameCase.BindAppId(appId);
                BoundCaseCount = i + 1;

                if (BoundCaseCount % batchSize == 0)
                {
                    Progress = (float)BoundCaseCount / cases.Length;
                    ProgressChanged?.Invoke(Progress);
                    yield return null;
                }
            }

            Progress = 1f;
            IsReady = true;
            ProgressChanged?.Invoke(Progress);
            Ready?.Invoke();
        }

#if UNITY_EDITOR
        [ContextMenu("Collect And Index Cases")]
        private void CollectAndIndexCases()
        {
            UnityEditor.Undo.RecordObject(
                this,
                "Collect Game Cases");

            cases = GetComponentsInChildren<NetworkGameCase>(true);

            for (int i = 0; i < cases.Length; i++)
            {
                if (cases[i] == null)
                {
                    continue;
                }

                UnityEditor.Undo.RecordObject(
                    cases[i],
                    "Index Game Case");
                cases[i].EditorSetSceneCaseIndex(i);
                UnityEditor.EditorUtility.SetDirty(cases[i]);
            }

            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log(
                $"[GameCaseSceneSet] Collected and indexed " +
                $"{cases.Length} cases.",
                this);
        }
#endif
    }
}
