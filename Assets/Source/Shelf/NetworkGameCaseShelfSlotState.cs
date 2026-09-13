using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Replicates the small amount of shared state needed by one logical shelf
    /// slot. Placement ownership and transforms remain in the existing item
    /// framework.
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

        public event Action<
            GameCaseShelfSlotSnapshot,
            GameCaseShelfSlotSnapshot> SnapshotChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _snapshot.OnValueChanged += HandleSnapshotChanged;

            if (IsServer && _snapshot.Value.Revision == 0)
            {
                _snapshot.Value = GameCaseShelfSlotSnapshot.Empty(1);
            }

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

            _snapshot.Value = new GameCaseShelfSlotSnapshot
            {
                LockedAppId = safeCount > 0 ? lockedAppId : 0,
                OccupiedCount = (ushort)safeCount,
                NextFreeIndex = (short)safeNextIndex,
                IsComplete = isComplete,
                Revision = nextRevision
            };
        }

        private void HandleSnapshotChanged(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            SnapshotChanged?.Invoke(previous, current);
        }
    }
}
