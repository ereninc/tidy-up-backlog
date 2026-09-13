using System;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.SaveSystem.Examples
{
    [DisallowMultipleComponent]
    public sealed class ExampleGameStateController : MonoBehaviour
    {
        [SerializeField, Min(1)] private int day = 1;
        [SerializeField] private long money = 250;
        [SerializeField] private string locationId = "laundromat";
        [SerializeField] private List<string> inventoryItemIds = new List<string>();

        public int Day => day;
        public long Money => money;
        public string LocationId => locationId;
        public IReadOnlyList<string> InventoryItemIds => inventoryItemIds;

        public void AdvanceDay()
        {
            day++;
            SaveManager.Instance?.MarkDirty();
        }

        public void AddMoney(long amount)
        {
            money += amount;
            SaveManager.Instance?.MarkDirty();
        }

        public void AddInventoryItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException("Item id cannot be empty.", nameof(itemId));
            }

            inventoryItemIds.Add(itemId);
            SaveManager.Instance?.MarkDirty();
        }

        internal ExampleGameStateData CaptureState()
        {
            return new ExampleGameStateData
            {
                Day = day,
                Money = money,
                LocationId = locationId,
                InventoryItemIds = new List<string>(inventoryItemIds)
            };
        }

        internal void RestoreState(ExampleGameStateData data)
        {
            day = Mathf.Max(1, data.Day);
            money = data.Money;
            locationId = string.IsNullOrWhiteSpace(data.LocationId)
                ? "laundromat"
                : data.LocationId;
            inventoryItemIds = data.InventoryItemIds ?? new List<string>();
        }
    }

    [Serializable]
    public sealed class ExampleGameStateData
    {
        public int Day = 1;
        public long Money;
        public string LocationId = "laundromat";
        public List<string> InventoryItemIds = new List<string>();
    }

    [Serializable]
    public sealed class ExampleGameSummaryContext
    {
        public int Day;
        public long Money;
        public string LocationId;
    }
}
