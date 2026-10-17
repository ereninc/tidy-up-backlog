using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class StoreComputerStatisticsPresenter : MonoBehaviour
    {
        [BoxGroup("References"), Required, SerializeField] private StoreComputerDesktopPresenter desktop;
        [BoxGroup("References"), SerializeField] private Transform[] shelfRoots = Array.Empty<Transform>();
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text balanceText;
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text completedShelvesText;
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text completedSlotsText;
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text totalGamesText;
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text totalCasesText;
        [BoxGroup("UI"), Required, SerializeField] private TMP_Text placementText;
        [BoxGroup("UI"), Required, SerializeField] private Slider placementProgress;

        private readonly Dictionary<NetworkGameCaseShelfSlotState, bool> _slots = new Dictionary<NetworkGameCaseShelfSlotState, bool>();
        private readonly List<NetworkGameCaseShelfSlotState[]> _shelves = new List<NetworkGameCaseShelfSlotState[]>();
        private NetworkSharedWallet _wallet;

        private void OnEnable()
        {
            foreach (Transform shelf in shelfRoots)
            {
                if (shelf == null) continue;
                var slots = shelf.GetComponentsInChildren<NetworkGameCaseShelfSlotState>(true);
                if (slots.Length == 0) continue;
                _shelves.Add(slots);
            }
            foreach (var root in gameObject.scene.GetRootGameObjects())
                foreach (var slot in root.GetComponentsInChildren<NetworkGameCaseShelfSlotState>(true))
                    TrackSlot(slot);
            NetworkSharedWallet.InstanceChanged += BindWallet;
            NetworkGameCaseShelfSlotState.SpawnStateChanged += HandleSlotSpawnChanged;
            GameCaseSessionPlan.Changed += RefreshStatistics;
            BindWallet(NetworkSharedWallet.Instance);
            RefreshStatistics();
        }

        private void OnDisable()
        {
            NetworkSharedWallet.InstanceChanged -= BindWallet;
            NetworkGameCaseShelfSlotState.SpawnStateChanged -= HandleSlotSpawnChanged;
            GameCaseSessionPlan.Changed -= RefreshStatistics;
            foreach (var slot in _slots.Keys)
                if (slot != null) slot.SnapshotChanged -= HandleSlotChanged;
            _slots.Clear();
            _shelves.Clear();
            if (_wallet != null) _wallet.BalanceChanged -= HandleBalanceChanged;
            _wallet = null;
        }

        private void BindWallet(NetworkSharedWallet wallet)
        {
            if (_wallet != null) _wallet.BalanceChanged -= HandleBalanceChanged;
            _wallet = wallet;
            if (_wallet != null) _wallet.BalanceChanged += HandleBalanceChanged;
            RefreshBalance();
        }

        private void HandleBalanceChanged(long previous, long current) => RefreshBalance();

        private void RefreshBalance()
        {
            if (balanceText != null)
                balanceText.text = _wallet != null ? "$" + _wallet.Balance.ToString("N0") : "$ --";
            if (desktop != null) desktop.RequestRedraw();
        }

        private void HandleSlotChanged(GameCaseShelfSlotSnapshot previous, GameCaseShelfSlotSnapshot current) => RefreshStatistics();

        private void HandleSlotSpawnChanged(NetworkGameCaseShelfSlotState slot, bool spawned)
        {
            if (slot.gameObject.scene != gameObject.scene) return;
            TrackSlot(slot);
            _slots[slot] = spawned;
            RefreshStatistics();
        }

        private void TrackSlot(NetworkGameCaseShelfSlotState slot)
        {
            if (_slots.ContainsKey(slot)) return;
            _slots.Add(slot, slot.IsSpawned);
            slot.SnapshotChanged += HandleSlotChanged;
        }

        private void RefreshStatistics()
        {
            int placed = 0;
            int completedSlots = 0;
            int activeSlots = 0;
            foreach (var slot in _slots)
            {
                if (slot.Key == null || !slot.Value) continue;
                activeSlots++;
                placed += slot.Key.OccupiedCount;
                if (slot.Key.IsComplete) completedSlots++;
            }
            int completedShelves = 0;
            foreach (var shelf in _shelves)
            {
                bool complete = true;
                foreach (var slot in shelf)
                {
                    if (slot == null || !_slots[slot] || !slot.IsComplete)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete) completedShelves++;
            }
            int total = GameCaseSessionPlan.TotalCaseCount;
            float progress = total > 0 ? Mathf.Clamp01((float)placed / total) : 0f;
            if (completedShelvesText != null) completedShelvesText.text = $"{completedShelves:N0} / {_shelves.Count:N0}";
            if (completedSlotsText != null) completedSlotsText.text = $"{completedSlots:N0} / {activeSlots:N0}";
            if (totalGamesText != null) totalGamesText.text = GameCaseSessionPlan.IsReady ? GameCaseSessionPlan.AppIds.Count.ToString("N0") : "--";
            if (totalCasesText != null) totalCasesText.text = total > 0 ? total.ToString("N0") : "--";
            if (placementText != null) placementText.text = total > 0 ? $"Placed cases  {placed:N0} / {total:N0}  ({progress:P0})" : "Waiting for session data";
            if (placementProgress != null) placementProgress.SetValueWithoutNotify(progress);
            if (desktop != null) desktop.RequestRedraw();
        }
    }
}
