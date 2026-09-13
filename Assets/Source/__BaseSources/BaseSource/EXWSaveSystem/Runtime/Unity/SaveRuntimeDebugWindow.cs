using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EXW.SaveSystem
{
    [DisallowMultipleComponent]
    public sealed class SaveRuntimeDebugWindow : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private KeyCode toggleKey = KeyCode.F8;
        [SerializeField] private bool visibleOnStart;

        private readonly List<SaveSlotInfo> _slots = new List<SaveSlotInfo>();
        private Rect _windowRect = new Rect(30f, 30f, 900f, 680f);
        private Vector2 _slotScroll;
        private Vector2 _jsonScroll;
        private SaveSlotInfo _selected;
        private string _json = string.Empty;
        private string _status = "Refresh and select a slot.";
        private bool _visible;
        private bool _busy;
        private float _confirmWriteUntil;

        private void Awake()
        {
            _visible = visibleOnStart;
        }

        private void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            bool togglePressed = Input.GetKeyDown(toggleKey);
#elif ENABLE_INPUT_SYSTEM
            bool togglePressed = IsNewInputSystemKeyPressed(toggleKey.ToString());
#else
            bool togglePressed = false;
#endif

            if (togglePressed)
            {
                _visible = !_visible;

                if (_visible && _slots.Count == 0)
                {
                    RefreshSlots();
                }
            }
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            _windowRect = GUILayout.Window(
                GetInstanceID(),
                _windowRect,
                DrawWindow,
                "EXW Save Runtime Debugger");
        }

        private void DrawWindow(int windowId)
        {
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = !_busy;

                if (GUILayout.Button("Refresh Slots", GUILayout.Width(110)))
                {
                    RefreshSlots();
                }

                if (GUILayout.Button("Reload Selected", GUILayout.Width(120)) && _selected != null)
                {
                    LoadSelected(_selected);
                }

                GUI.enabled = !_busy && _selected != null && _selected.IsValid;
                string writeLabel = Time.realtimeSinceStartup < _confirmWriteUntil
                    ? "CONFIRM WRITE"
                    : "Write Edited Save";

                if (GUILayout.Button(writeLabel, GUILayout.Width(130)))
                {
                    if (Time.realtimeSinceStartup < _confirmWriteUntil)
                    {
                        WriteEditedSave();
                        _confirmWriteUntil = 0f;
                    }
                    else
                    {
                        _confirmWriteUntil = Time.realtimeSinceStartup + 3f;
                        _status = "Press CONFIRM WRITE within three seconds.";
                    }
                }

                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Toggle: {toggleKey}");
            }

            using (new GUILayout.HorizontalScope())
            {
                using (new GUILayout.VerticalScope(GUILayout.Width(225)))
                {
                    GUILayout.Label("Slots");
                    using (new GUILayout.VerticalScope(GUI.skin.box))
                    {
                        _slotScroll = GUILayout.BeginScrollView(_slotScroll);

                        for (int i = 0; i < _slots.Count; i++)
                        {
                            SaveSlotInfo slot = _slots[i];
                            string suffix = !slot.IsValid
                                ? "  [DAMAGED]"
                                : slot.Recovered ? "  [RECOVERED]" : string.Empty;

                            if (GUILayout.Button(slot.Metadata.DisplayName + suffix))
                            {
                                LoadSelected(slot);
                            }

                            GUILayout.Label(slot.Metadata.SlotId, GUI.skin.label);
                        }

                        GUILayout.EndScrollView();
                    }
                }

                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Label(_selected == null
                        ? "No slot selected"
                        : $"{_selected.Metadata.DisplayName} / {_selected.Metadata.SlotId}");

                    using (new GUILayout.VerticalScope(GUI.skin.box))
                    {
                        _jsonScroll = GUILayout.BeginScrollView(_jsonScroll);
                        _json = GUILayout.TextArea(_json, GUILayout.ExpandHeight(true));
                        GUILayout.EndScrollView();
                    }
                }
            }

            GUILayout.Label(_status, GUI.skin.box);
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 24f));
        }

        private async void RefreshSlots()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                _status = "SaveManager is not available.";
                return;
            }

            _busy = true;
            SaveCatalogResult result = await manager.GetSlotsAsync();
            _slots.Clear();

            if (result.Success)
            {
                _slots.AddRange(result.Slots);
                _status = $"Validated {_slots.Count} slot(s).";
            }
            else
            {
                _status = result.Message;
            }

            _busy = false;
        }

        private async void LoadSelected(SaveSlotInfo slot)
        {
            if (!slot.IsValid)
            {
                _selected = slot;
                _json = string.Empty;
                _status = slot.Diagnostic;
                return;
            }

            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                _status = "SaveManager is not available.";
                return;
            }

            _busy = true;
            SaveDebugReadResult result = await manager.DebugReadSlotAsync(slot.Metadata.SlotId);
            _selected = slot;
            _json = result.Json;
            _status = result.Success ? "Editable JSON loaded." : result.Message;
            _busy = false;
        }

        private async void WriteEditedSave()
        {
            SaveManager manager = SaveManager.Instance;

            if (manager == null)
            {
                _status = "SaveManager is not available.";
                return;
            }

            _busy = true;
            SaveOperationResult result = await manager.DebugWriteSlotAsync(_json);
            _status = result.Success ? "Edited save written atomically." : result.Message;
            _busy = false;

            if (result.Success)
            {
                RefreshSlots();
            }
        }

        public void ToggleVisible()
        {
            _visible = !_visible;

            if (_visible && _slots.Count == 0)
            {
                RefreshSlots();
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static bool IsNewInputSystemKeyPressed(string keyName)
        {
            try
            {
                Type keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
                Type keyType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");

                if (keyboardType == null || keyType == null)
                {
                    return false;
                }

                object keyboard = keyboardType
                    .GetProperty("current", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);

                if (keyboard == null)
                {
                    return false;
                }

                object key = Enum.Parse(keyType, keyName, ignoreCase: true);
                object keyControl = keyboardType
                    .GetProperty("Item", new[] { keyType })
                    ?.GetValue(keyboard, new[] { key });

                return keyControl != null &&
                       (bool)(keyControl.GetType()
                           .GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance)
                           ?.GetValue(keyControl) ?? false);
            }
            catch
            {
                return false;
            }
        }
#endif
#endif
    }
}
