using UnityEngine;

namespace EXW.SaveSystem
{
    [DisallowMultipleComponent]
    public sealed class SaveAutosaveController : MonoBehaviour
    {
        [SerializeField, Min(15f)] private float intervalSeconds = 120f;
        [SerializeField] private bool saveWhenApplicationPauses = true;

        private float _elapsed;

        private void Update()
        {
            SaveManager manager = SaveManager.Instance;

            if (!manager ||
                !manager.HasActiveSlot ||
                manager.HasPreparedLoad)
            {
                _elapsed = 0f;
                return;
            }

            _elapsed += Time.unscaledDeltaTime;

            if (_elapsed < intervalSeconds)
            {
                return;
            }

            _elapsed = 0f;

            if (manager.IsDirty)
            {
                _ = manager.SaveActiveSlotAsync(SaveReason.Autosave);
            }
        }

        private void OnApplicationPause(bool paused)
        {
            SaveManager manager = SaveManager.Instance;

            if (paused &&
                saveWhenApplicationPauses &&
                manager != null &&
                manager.HasActiveSlot &&
                !manager.HasPreparedLoad &&
                manager.IsDirty)
            {
                _ = manager.SaveActiveSlotAsync(
                    SaveReason.ApplicationPause);
            }
        }
    }
}
