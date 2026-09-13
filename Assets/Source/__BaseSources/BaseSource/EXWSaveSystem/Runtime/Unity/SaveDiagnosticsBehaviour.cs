using UnityEngine;

namespace EXW.SaveSystem
{
    [DisallowMultipleComponent]
    public sealed class SaveDiagnosticsBehaviour : MonoBehaviour
    {
        [SerializeField] private bool logSuccessfulOperations = true;

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void Start()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                return;
            }

            manager.SaveFinished -= HandleSaveFinished;
            manager.LoadPrepared -= HandleLoadPrepared;
            manager.RestoreFinished -= HandleRestoreFinished;
        }

        [ContextMenu("Save Active Slot")]
        private async void SaveActiveSlot()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                Debug.LogError("SaveManager is not available.", this);
                return;
            }

            SaveOperationResult result = await manager.SaveActiveSlotAsync(SaveReason.Manual);
            Log("Manual save", result);
        }

        [ContextMenu("Validate And List Slots")]
        private async void ValidateAndListSlots()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                Debug.LogError("SaveManager is not available.", this);
                return;
            }

            SaveCatalogResult result = await manager.GetSlotsAsync();

            if (!result.Success)
            {
                Debug.LogError($"Slot validation failed: {result.Message}", this);
                return;
            }

            for (int i = 0; i < result.Slots.Count; i++)
            {
                SaveSlotInfo slot = result.Slots[i];
                Debug.Log(
                    $"[{slot.Metadata.SlotId}] {slot.Metadata.DisplayName} | " +
                    $"valid={slot.IsValid} recovered={slot.Recovered} " +
                    $"sequence={slot.Metadata.Sequence}",
                    this);
            }
        }

        private void TrySubscribe()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                return;
            }

            manager.SaveFinished -= HandleSaveFinished;
            manager.LoadPrepared -= HandleLoadPrepared;
            manager.RestoreFinished -= HandleRestoreFinished;
            manager.SaveFinished += HandleSaveFinished;
            manager.LoadPrepared += HandleLoadPrepared;
            manager.RestoreFinished += HandleRestoreFinished;
        }

        private void HandleSaveFinished(SaveOperationResult result) => Log("Save", result);
        private void HandleLoadPrepared(SaveOperationResult result) => Log("Prepare load", result);
        private void HandleRestoreFinished(SaveOperationResult result) => Log("Restore", result);

        private void Log(string operation, SaveOperationResult result)
        {
            if (result.Success)
            {
                if (logSuccessfulOperations)
                {
                    Debug.Log($"{operation} succeeded. {result.Message}", this);
                }

                return;
            }

            Debug.LogError($"{operation} failed [{result.Error}]: {result.Message}", this);
        }
    }
}
