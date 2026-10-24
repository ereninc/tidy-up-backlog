using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Shelf/Shelf Completion Group")]
    public sealed class GameCaseShelfCompletionGroup : NetworkBehaviour
    {
        [Tooltip("Hierarchy/Inspector order defines target order. Empty uses this scene's roots.")]
        [SerializeField] private Transform[] shelfRoots = Array.Empty<Transform>();

        private readonly NetworkVariable<bool> _complete = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly List<NetworkGameCaseShelfSlotState> _slots = new List<NetworkGameCaseShelfSlotState>();
        private readonly Dictionary<NetworkGameCaseShelfSlotState, bool> _spawned = new Dictionary<NetworkGameCaseShelfSlotState, bool>();

        public bool IsComplete => IsSpawned && _complete.Value;
        public IReadOnlyList<NetworkGameCaseShelfSlotState> Slots => _slots;
        public event Action<bool> CompletionChanged;
        /// <summary>Server-only false-to-true completion notification; late join reads IsComplete.</summary>
        public event Action Completed;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _complete.OnValueChanged += HandleCompletionChanged;
            if (IsServer)
            {
                Completed += LogCompleted;
                RebuildTargetsServer();
                NetworkGameCaseShelfSlotState.SpawnStateChanged += HandleSpawnChanged;
            }
            CompletionChanged?.Invoke(IsComplete);
        }

        public override void OnNetworkDespawn()
        {
            _complete.OnValueChanged -= HandleCompletionChanged;
            NetworkGameCaseShelfSlotState.SpawnStateChanged -= HandleSpawnChanged;
            Completed -= LogCompleted;
            UnbindSlots();
            CompletionChanged?.Invoke(false);
            base.OnNetworkDespawn();
        }

        public void RebuildTargetsServer()
        {
            if (!IsSpawned || !IsServer) return;
            UnbindSlots();
            if (shelfRoots.Length > 0)
            {
                foreach (var root in shelfRoots)
                    if (root != null) CollectSlots(root);
            }
            else
            {
                foreach (var root in gameObject.scene.GetRootGameObjects()) CollectSlots(root.transform);
            }
            RefreshServer();
        }

        private void CollectSlots(Transform root)
        {
            if (root.TryGetComponent(out NetworkGameCaseShelfSlotState slot) &&
                root.TryGetComponent(out NetworkGameCaseShelfDestination shelf) &&
                root.TryGetComponent(out NetworkItemReceiver receiver) && receiver.Destination == shelf)
            {
                AddSlot(slot);
                return;
            }
            for (int i = 0; i < root.childCount; i++) CollectSlots(root.GetChild(i));
        }

        private void AddSlot(NetworkGameCaseShelfSlotState slot)
        {
            if (_spawned.ContainsKey(slot)) return;
            _slots.Add(slot);
            _spawned.Add(slot, slot.IsSpawned && slot.IsServer);
            slot.SnapshotChanged += HandleSnapshotChanged;
            slot.SlotCompleted += HandleSlotCompleted;
        }

        private void UnbindSlots()
        {
            foreach (var slot in _slots)
            {
                if (slot == null) continue;
                slot.SnapshotChanged -= HandleSnapshotChanged;
                slot.SlotCompleted -= HandleSlotCompleted;
            }
            _slots.Clear();
            _spawned.Clear();
        }

        private void HandleSnapshotChanged(GameCaseShelfSlotSnapshot previous, GameCaseShelfSlotSnapshot current)
        {
            // The existing SlotCompleted event follows its wallet reward.
            if (!previous.IsComplete && current.IsComplete) return;
            RefreshServer();
        }

        private void HandleSlotCompleted(GameCaseShelfCompletionData data) => RefreshServer();

        private void HandleSpawnChanged(NetworkGameCaseShelfSlotState slot, bool spawned)
        {
            if (!_spawned.ContainsKey(slot))
            {
                bool inScope = shelfRoots.Length == 0 && slot.gameObject.scene == gameObject.scene;
                foreach (var root in shelfRoots)
                    if (root != null && slot.transform.IsChildOf(root)) inScope = true;
                if (!inScope || !spawned ||
                    !slot.TryGetComponent(out NetworkGameCaseShelfDestination shelf) ||
                    !slot.TryGetComponent(out NetworkItemReceiver receiver) || receiver.Destination != shelf) return;
                AddSlot(slot);
            }
            _spawned[slot] = spawned && slot.IsServer;
            RefreshServer();
        }

        private void RefreshServer()
        {
            if (!IsSpawned || !IsServer) return;
            int count = 0;
            bool complete = true;
            foreach (var slot in _slots)
            {
                if (slot == null || !_spawned[slot]) continue;
                count++;
                complete &= slot.IsComplete;
            }
            _complete.Value = count > 0 && complete;
        }

        private void HandleCompletionChanged(bool previous, bool current)
        {
            CompletionChanged?.Invoke(current);
            if (IsServer && !previous && current) Completed?.Invoke();
        }

        private void LogCompleted() => Debug.Log("All target shelves completed.", this);
    }
}
