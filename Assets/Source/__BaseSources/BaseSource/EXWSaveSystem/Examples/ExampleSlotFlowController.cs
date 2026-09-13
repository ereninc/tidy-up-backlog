using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EXW.SaveSystem.Examples
{
    [DisallowMultipleComponent]
    public sealed class ExampleSlotFlowController : MonoBehaviour
    {
        public async Task<IReadOnlyList<SaveSlotInfo>> RefreshSlots()
        {
            SaveCatalogResult result = await SaveManager.Instance.GetSlotsAsync();

            if (!result.Success)
            {
                Debug.LogError($"Could not list saves: {result.Message}", this);
                return new SaveSlotInfo[0];
            }

            return result.Slots;
        }

        public SaveOperationResult BeginNewGame(int slotNumber)
        {
            string slotId = $"slot_{slotNumber}";
            return SaveManager.Instance.BeginNewGame(slotId, $"Save {slotNumber}");
        }

        public async Task<SaveOperationResult> SaveGame()
        {
            return await SaveManager.Instance.SaveActiveSlotAsync(SaveReason.Manual);
        }

        public async Task<SaveOperationResult> LoadGame(string slotId)
        {
            SaveManager manager = SaveManager.Instance;
            SaveOperationResult prepared = await manager.PrepareLoadSlotAsync(slotId);

            if (!prepared.Success)
            {
                return prepared;
            }

            AsyncOperation sceneLoad = SceneManager.LoadSceneAsync(prepared.Slot.Metadata.SceneName);

            while (sceneLoad != null && !sceneLoad.isDone)
            {
                await Task.Yield();
            }

            return manager.RestorePreparedLoad();
        }

        public async Task<SaveOperationResult> SaveAndQuit()
        {
            SaveOperationResult result = await SaveManager.Instance.SaveActiveSlotAsync(
                SaveReason.SaveAndQuit);

            if (result.Success)
            {
                Application.Quit();
            }

            return result;
        }

        public static bool TryReadSummary(
            SaveSlotInfo slot,
            out ExampleGameSummaryContext summary)
        {
            return slot.Metadata.TryGetContext("game.summary", out summary);
        }
    }
}
