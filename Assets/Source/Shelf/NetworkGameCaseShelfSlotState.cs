using System;
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

        public event Action<
            GameCaseShelfSlotSnapshot,
            GameCaseShelfSlotSnapshot> SnapshotChanged;

        private uint _raisedCompletionRevision;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticEvents()
        {
            AnySlotCompleted = null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _snapshot.OnValueChanged += HandleSnapshotChanged;

            if (IsServer && _snapshot.Value.Revision == 0)
            {
                _snapshot.Value = GameCaseShelfSlotSnapshot.Empty(1);
            }

            // Intentionally current/current. A late joiner receives the final
            // visual state but must not replay rewards or celebration events.
            SnapshotChanged?.Invoke(_snapshot.Value, _snapshot.Value);
        }

        public override void OnNetworkDespawn()
        {
            _snapshot.OnValueChanged -= HandleSnapshotChanged;
            base.OnNetworkDespawn();
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

            // NGO normally invokes OnValueChanged for the server write. This
            // explicit call makes the semantic transaction robust to version
            // differences; the revision guard prevents a duplicate.
            TryRaiseCompletion(previous, current);
        }

        private void HandleSnapshotChanged(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            SnapshotChanged?.Invoke(previous, current);
            TryRaiseCompletion(previous, current);
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