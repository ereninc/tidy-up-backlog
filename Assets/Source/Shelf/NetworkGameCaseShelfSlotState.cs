using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Replicates the small amount of shared state needed by one logical shelf
    /// slot. A false-to-true completion transition grants the shared reward on
    /// the server and raises local completion events on every peer.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Slot State")]
    public sealed class NetworkGameCaseShelfSlotState : NetworkBehaviour
    {
        private readonly NetworkVariable<GameCaseShelfSlotSnapshot> _snapshot =
            new NetworkVariable<GameCaseShelfSlotSnapshot>(
                GameCaseShelfSlotSnapshot.Empty(0),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [TitleGroup("Completion Reward")]
        [Tooltip(
            "Granted once to the shared wallet when this slot first becomes complete.")]
        [SerializeField]
        [MinValue(0)]
        private long completionReward = 100;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Slot")]
        public uint LockedAppId => _snapshot.Value.LockedAppId;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Slot")]
        public int OccupiedCount => _snapshot.Value.OccupiedCount;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Slot")]
        public int NextFreeIndex => _snapshot.Value.NextFreeIndex;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Slot")]
        public bool IsComplete => _snapshot.Value.IsComplete;

        public GameCaseShelfSlotSnapshot Snapshot => _snapshot.Value;
        public long CompletionReward => Math.Max(0L, completionReward);

        /// <summary>
        /// Instance event for systems already holding this exact slot.
        /// </summary>
        public event Action<GameCaseShelfCompletionData> SlotCompleted;

        /// <summary>
        /// Global local event for VFX, audio, floating text and telemetry.
        /// Subscribe on each client that wants to present the completion.
        /// </summary>
        public static event Action<GameCaseShelfCompletionData>
            AnySlotCompleted;

        /// <summary>
        /// Raised once on the server and each connected client when every
        /// session case has been committed to a shelf slot. Late joiners do
        /// not replay this celebration.
        /// </summary>
        public static event Action AllCasesPlaced;

        private static readonly Dictionary<NetworkGameCaseShelfSlotState, int>
            ServerPlacedCounts =
                new Dictionary<NetworkGameCaseShelfSlotState, int>();

        private static int _serverPlacedCaseCount;
        private static bool _allCasesPlacedServer;

        public event Action<
            GameCaseShelfSlotSnapshot,
            GameCaseShelfSlotSnapshot> SnapshotChanged;

        private uint _raisedCompletionRevision;
        private uint _notifiedSnapshotRevision;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            GameCaseSessionPlan.Changed -= HandleSessionPlanChanged;
            ServerPlacedCounts.Clear();
            _serverPlacedCaseCount = 0;
            _allCasesPlacedServer = false;
            AnySlotCompleted = null;
            AllCasesPlaced = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _snapshot.OnValueChanged += HandleSnapshotChanged;

            if (IsServer)
            {
                RegisterSessionSlotServer();
            }

            if (IsServer && _snapshot.Value.Revision == 0)
            {
                _snapshot.Value = GameCaseShelfSlotSnapshot.Empty(1);
            }

            // Intentionally current/current. A late joiner receives the final
            // visual state but must not replay rewards or celebration events.
            _notifiedSnapshotRevision = _snapshot.Value.Revision;
            SnapshotChanged?.Invoke(_snapshot.Value, _snapshot.Value);
            UpdateSessionProgressServer();
        }

        public override void OnNetworkDespawn()
        {
            _snapshot.OnValueChanged -= HandleSnapshotChanged;
            UnregisterSessionSlotServer();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            UnregisterSessionSlotServer();
            base.OnDestroy();
        }

        internal void PublishServer(
            uint lockedAppId,
            int occupiedCount,
            int nextFreeIndex,
            bool isComplete)
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            int safeCount = Mathf.Clamp(
                occupiedCount,
                0,
                ushort.MaxValue);
            int safeNextIndex = Mathf.Clamp(
                nextFreeIndex,
                -1,
                short.MaxValue);
            uint nextRevision = _snapshot.Value.Revision + 1;

            if (nextRevision == 0)
            {
                nextRevision = 1;
            }

            GameCaseShelfSlotSnapshot previous = _snapshot.Value;
            var current = new GameCaseShelfSlotSnapshot
            {
                LockedAppId = safeCount > 0 ? lockedAppId : 0,
                OccupiedCount = (ushort)safeCount,
                NextFreeIndex = (short)safeNextIndex,
                IsComplete = isComplete,
                Revision = nextRevision
            };

            _snapshot.Value = current;

            // Row authority must see this commit before the next placement.
            // NGO's callback and this fallback share one revision guard.
            HandleSnapshotChanged(previous, current);
        }

        private void HandleSnapshotChanged(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            if (_notifiedSnapshotRevision == current.Revision)
            {
                return;
            }

            _notifiedSnapshotRevision = current.Revision;
            SnapshotChanged?.Invoke(previous, current);
            TryRaiseCompletion(previous, current);
            UpdateSessionProgressServer();
        }

        private void RegisterSessionSlotServer()
        {
            if (ServerPlacedCounts.ContainsKey(this))
            {
                return;
            }

            if (ServerPlacedCounts.Count == 0)
            {
                GameCaseSessionPlan.Changed += HandleSessionPlanChanged;
            }

            ServerPlacedCounts.Add(this, 0);
        }

        private void UpdateSessionProgressServer()
        {
            if (!IsServer || !IsSpawned ||
                !ServerPlacedCounts.TryGetValue(this, out int previousCount))
            {
                return;
            }

            int currentCount = OccupiedCount;
            ServerPlacedCounts[this] = currentCount;
            _serverPlacedCaseCount += currentCount - previousCount;
            TryRaiseAllCasesPlacedServer(this);
        }

        private void UnregisterSessionSlotServer()
        {
            if (!ServerPlacedCounts.TryGetValue(this, out int previousCount))
            {
                return;
            }

            ServerPlacedCounts.Remove(this);
            _serverPlacedCaseCount -= previousCount;

            if (ServerPlacedCounts.Count == 0)
            {
                GameCaseSessionPlan.Changed -= HandleSessionPlanChanged;
                _serverPlacedCaseCount = 0;
                _allCasesPlacedServer = false;
            }
        }

        private static void HandleSessionPlanChanged()
        {
            foreach (NetworkGameCaseShelfSlotState slot in ServerPlacedCounts.Keys)
            {
                TryRaiseAllCasesPlacedServer(slot);
                break;
            }
        }

        private static void TryRaiseAllCasesPlacedServer(
            NetworkGameCaseShelfSlotState source)
        {
            int requiredCount = GameCaseSessionPlan.TotalCaseCount;

            if (_allCasesPlacedServer || requiredCount <= 0 ||
                _serverPlacedCaseCount < requiredCount ||
                source == null || !source.IsServer || !source.IsSpawned)
            {
                return;
            }

            _allCasesPlacedServer = true;
            Debug.Log(
                "[GameCaseShelf] All session cases placed | " +
                $"Placed={_serverPlacedCaseCount} | Target={requiredCount}",
                source);

            source.NotifyAllCasesPlacedClientRpc();
            AllCasesPlaced?.Invoke();
        }

        [ClientRpc]
        private void NotifyAllCasesPlacedClientRpc()
        {
            if (!IsServer)
            {
                AllCasesPlaced?.Invoke();
            }
        }

        private void TryRaiseCompletion(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            if (previous.IsComplete ||
                !current.IsComplete ||
                current.LockedAppId == 0 ||
                _raisedCompletionRevision == current.Revision)
            {
                return;
            }

            _raisedCompletionRevision = current.Revision;

            uint appId = current.LockedAppId;
            string gameName = GameCaseSessionPlan.GetGameName(appId);
            uint playtimeMinutes =
                GameCaseSessionPlaytimePlan.GetOrDefault(appId);
            long rewardAmount = CompletionReward;

            bool rewardGranted = false;

            if (IsServer && rewardAmount > 0)
            {
                NetworkSharedWallet wallet = NetworkSharedWallet.Instance;

                if (wallet != null)
                {
                    rewardGranted = wallet.GrantServer(
                        rewardAmount,
                        $"Shelf completed: {gameName} ({appId})");
                }

                if (!rewardGranted)
                {
                    Debug.LogError(
                        "[GameCaseShelf] Slot completed but its shared " +
                        $"wallet reward (+{rewardAmount}) could not be granted.",
                        this);
                }
            }

            var data = new GameCaseShelfCompletionData(
                this,
                transform.position,
                transform.rotation,
                appId,
                gameName,
                playtimeMinutes,
                current.OccupiedCount,
                rewardAmount,
                current.Revision,
                IsServer);

            Debug.Log(
                "[GameCaseShelf] Slot completed | " +
                $"Game={data.GameName} | " +
                $"AppId={data.AppId} | " +
                $"Cases={data.CaseCount} | " +
                $"Playtime={data.PlaytimeHours:0.0}h " +
                $"({data.PlaytimeMinutes}m) | " +
                $"Position={data.WorldPosition} | " +
                $"Reward=+{data.RewardAmount} | " +
                $"Authority={(IsServer ? "Server" : "Client")}",
                this);

            SlotCompleted?.Invoke(data);
            AnySlotCompleted?.Invoke(data);
        }
    }
}
