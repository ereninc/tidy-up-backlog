using System;
using System.Collections;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Replicates the compact game-case manifest for the lifetime of GameplayScene.
    /// Existing players already own the local static plans from LoadingScene;
    /// late joiners rebuild those plans from this in-scene NetworkObject.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Network Game Case Session Manifest")]
    public sealed class NetworkGameCaseSessionManifest : NetworkBehaviour
    {
        private const float ApplyTimeoutSeconds = 15f;
        private const int MaximumGameNameUtf8Bytes = 120;

        public static NetworkGameCaseSessionManifest Instance
        {
            get;
            private set;
        }

        private readonly NetworkVariable<bool> _manifestReady =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort> _distinctGameCount =
            new NetworkVariable<ushort>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort> _copiesPerGame =
            new NetworkVariable<ushort>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<uint> _assignmentSeed =
            new NetworkVariable<uint>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<uint> _manifestVersion =
            new NetworkVariable<uint>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private NetworkList<uint> _appIds;
        private NetworkList<FixedString128Bytes> _gameNames;
        private NetworkList<uint> _playtimeMinutes;

        private Coroutine _applyRoutine;
        private uint _lastAppliedVersion;

        public bool HasReplicatedManifest =>
            IsSpawned &&
            _manifestReady.Value &&
            _manifestVersion.Value != 0;

        public bool IsLocalPlanApplied =>
            GameCaseSessionPlan.IsReady &&
            (
                IsServer
                    ? HasReplicatedManifest
                    : _lastAppliedVersion != 0 &&
                      _lastAppliedVersion == _manifestVersion.Value
            );

        public event Action LocalPlanApplied;

        private void Awake()
        {
            _appIds = new NetworkList<uint>();
            _gameNames = new NetworkList<FixedString128Bytes>();
            _playtimeMinutes = new NetworkList<uint>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "More than one NetworkGameCaseSessionManifest is active.",
                    this);
            }

            Instance = this;
            _manifestReady.OnValueChanged += HandleManifestReadyChanged;

            if (IsServer)
            {
                GameCaseSessionPlan.Changed += HandleServerPlanChanged;
                TryPublishCurrentPlanServer();
            }
            else if (_manifestReady.Value)
            {
                BeginApplyReplicatedPlan();
            }
        }

        public override void OnNetworkDespawn()
        {
            _manifestReady.OnValueChanged -= HandleManifestReadyChanged;

            if (IsServer)
            {
                GameCaseSessionPlan.Changed -= HandleServerPlanChanged;
            }

            if (_applyRoutine != null)
            {
                StopCoroutine(_applyRoutine);
                _applyRoutine = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            _appIds?.Dispose();
            _gameNames?.Dispose();
            _playtimeMinutes?.Dispose();
        }

        private void HandleServerPlanChanged()
        {
            TryPublishCurrentPlanServer();
        }

        /// <summary>
        /// Called automatically when the GameplayScene NetworkObject spawns.
        /// It is also safe to call after a direct-scene plan is created.
        /// </summary>
        public bool TryPublishCurrentPlanServer()
        {
            if (!IsServer ||
                !IsSpawned ||
                !GameCaseSessionPlan.IsReady)
            {
                return false;
            }

            int count = GameCaseSessionPlan.AppIds.Count;

            if (count <= 0 ||
                count > ushort.MaxValue ||
                GameCaseSessionPlan.CopiesPerGame <= 0 ||
                GameCaseSessionPlan.CopiesPerGame > ushort.MaxValue)
            {
                Debug.LogError(
                    "[GameCaseManifest] Local plan has invalid dimensions.",
                    this);
                return false;
            }

            _manifestReady.Value = false;

            _appIds.Clear();
            _gameNames.Clear();
            _playtimeMinutes.Clear();

            for (int i = 0; i < count; i++)
            {
                uint appId = GameCaseSessionPlan.AppIds[i];

                if (appId == 0)
                {
                    Debug.LogError(
                        "[GameCaseManifest] Local plan contains AppId 0.",
                        this);
                    return false;
                }

                _appIds.Add(appId);
                _gameNames.Add(ToNetworkGameName(
                    appId,
                    GameCaseSessionPlan.GetGameName(appId)));
                _playtimeMinutes.Add(
                    GameCaseSessionPlaytimePlan.GetOrDefault(appId));
            }

            _distinctGameCount.Value = (ushort)count;
            _copiesPerGame.Value =
                (ushort)GameCaseSessionPlan.CopiesPerGame;
            _assignmentSeed.Value =
                GameCaseSessionPlan.AssignmentSeed == 0
                    ? 1u
                    : GameCaseSessionPlan.AssignmentSeed;

            uint nextVersion = _manifestVersion.Value + 1u;
            _manifestVersion.Value = nextVersion == 0 ? 1u : nextVersion;
            _manifestReady.Value = true;

            Debug.Log(
                $"[GameCaseManifest] Published {count} games for late join.",
                this);
            return true;
        }

        private void HandleManifestReadyChanged(bool previous, bool current)
        {
            if (!IsServer && current)
            {
                BeginApplyReplicatedPlan();
            }
        }

        private void BeginApplyReplicatedPlan()
        {
            if (IsServer ||
                _applyRoutine != null ||
                !_manifestReady.Value ||
                _manifestVersion.Value == _lastAppliedVersion)
            {
                return;
            }

            _applyRoutine = StartCoroutine(ApplyReplicatedPlanWhenComplete());
        }

        private IEnumerator ApplyReplicatedPlanWhenComplete()
        {
            float deadline =
                Time.realtimeSinceStartup + ApplyTimeoutSeconds;

            while (_manifestReady.Value &&
                   !HasCompleteReplicatedData() &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!_manifestReady.Value || !HasCompleteReplicatedData())
            {
                Debug.LogError(
                    "[GameCaseManifest] Replicated late-join manifest was " +
                    "incomplete.",
                    this);
                _applyRoutine = null;
                yield break;
            }

            int count = _distinctGameCount.Value;
            var appIds = new uint[count];
            var gameNames = new string[count];
            var playtimes = new uint[count];

            for (int i = 0; i < count; i++)
            {
                appIds[i] = _appIds[i];
                gameNames[i] = _gameNames[i].ToString();
                playtimes[i] = _playtimeMinutes[i];
            }

            GameCaseSessionPlan.Set(
                appIds,
                gameNames,
                _copiesPerGame.Value,
                _assignmentSeed.Value);

            GameCaseSessionPlaytimePlan.Set(appIds, playtimes);

            _lastAppliedVersion = _manifestVersion.Value;
            _applyRoutine = null;
            LocalPlanApplied?.Invoke();

            Debug.Log(
                $"[GameCaseManifest] Late-join plan restored: {count} games.",
                this);
        }

        private bool HasCompleteReplicatedData()
        {
            int expectedCount = _distinctGameCount.Value;

            return _manifestReady.Value &&
                   _manifestVersion.Value != 0 &&
                   expectedCount > 0 &&
                   _copiesPerGame.Value > 0 &&
                   _assignmentSeed.Value != 0 &&
                   _appIds.Count == expectedCount &&
                   _gameNames.Count == expectedCount &&
                   _playtimeMinutes.Count == expectedCount;
        }

        private static FixedString128Bytes ToNetworkGameName(
            uint appId,
            string gameName)
        {
            string value = string.IsNullOrWhiteSpace(gameName)
                ? $"App {appId}"
                : gameName.Trim()
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

            while (value.Length > 1 &&
                   Encoding.UTF8.GetByteCount(value) >
                   MaximumGameNameUtf8Bytes)
            {
                int newLength = value.Length - 1;

                if (newLength > 0 &&
                    char.IsHighSurrogate(value[newLength - 1]))
                {
                    newLength--;
                }

                value = value.Substring(0, Mathf.Max(1, newLength));
            }

            return new FixedString128Bytes(value);
        }
    }
}
