#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Editor
{
    public sealed class SaveDataEditorWindow : EditorWindow
    {
        private readonly JTokenTreeDrawer _treeDrawer = new JTokenTreeDrawer();

        private SaveSystemSettings _settings;
        private SaveSystemSettings _temporarySettings;
        private SaveFileStore _store;
        private readonly List<SaveSlotInfo> _slots = new List<SaveSlotInfo>();
        private SaveSlotInfo _selectedSlot;
        private JObject _editedRoot;
        private string _rawJson = string.Empty;
        private string _status = "Select a slot.";
        private Vector2 _slotScroll;
        private Vector2 _dataScroll;
        private int _viewMode;
        private bool _busy;
        private bool _changed;

        [MenuItem("EXW Tools/Save System/Data Editor")]
        public static void Open()
        {
            GetWindow<SaveDataEditorWindow>("EXW Save Data");
        }

        private void OnEnable()
        {
            FindSettingsAsset();
            RebuildStore();
            _ = RefreshSlotsAsync();
        }

        private void OnDisable()
        {
            _store?.Dispose();
            _store = null;

            if (_temporarySettings != null)
            {
                DestroyImmediate(_temporarySettings);
                _temporarySettings = null;
            }
        }

        private void OnGUI()
        {
            DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawSlotColumn();
                DrawDataColumn();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                SaveSystemSettings selected;

                using (new EditorGUI.DisabledScope(_busy))
                {
                    selected = (SaveSystemSettings)EditorGUILayout.ObjectField(
                        _settings,
                        typeof(SaveSystemSettings),
                        false,
                        GUILayout.MinWidth(180));
                }

                if (selected != _settings)
                {
                    _settings = selected;
                    RebuildStore();
                    _ = RefreshSlotsAsync();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_busy || _store == null))
                {
                    if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                    {
                        _ = RefreshSlotsAsync();
                    }

                    if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(85)))
                    {
                        string directory = GetActiveSettings().GetSaveDirectory();
                        Directory.CreateDirectory(directory);
                        EditorUtility.RevealInFinder(directory);
                    }
                }
            }
        }

        private void DrawSlotColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(250)))
            {
                EditorGUILayout.LabelField("Save Slots", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    _slotScroll = EditorGUILayout.BeginScrollView(_slotScroll);

                    if (_slots.Count == 0)
                    {
                        EditorGUILayout.LabelField(_busy ? "Reading..." : "No save slots found.");
                    }

                    for (int i = 0; i < _slots.Count; i++)
                    {
                        DrawSlotButton(_slots[i]);
                    }

                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawSlotButton(SaveSlotInfo slot)
        {
            bool selected = _selectedSlot != null &&
                            _selectedSlot.Metadata.SlotId == slot.Metadata.SlotId;

            Color previousColor = GUI.backgroundColor;

            if (!slot.IsValid)
            {
                GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
            }
            else if (slot.Recovered)
            {
                GUI.backgroundColor = new Color(1f, 0.78f, 0.35f);
            }
            else if (selected)
            {
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (GUILayout.Button(
                        slot.Metadata.DisplayName,
                        selected ? EditorStyles.boldLabel : EditorStyles.label))
                {
                    _ = SelectSlotAsync(slot);
                }

                EditorGUILayout.LabelField(slot.Metadata.SlotId, EditorStyles.miniLabel);

                if (slot.IsValid)
                {
                    EditorGUILayout.LabelField(
                        slot.Metadata.LastSavedAtUtc.LocalDateTime.ToString("yyyy-MM-dd  HH:mm:ss"),
                        EditorStyles.miniLabel);

                    if (slot.Recovered)
                    {
                        EditorGUILayout.LabelField(
                            $"Recovered from {slot.Source}" +
                            (slot.BackupIndex > 0 ? $" {slot.BackupIndex}" : string.Empty),
                            EditorStyles.miniBoldLabel);
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("DAMAGED", EditorStyles.miniBoldLabel);
                }
            }

            GUI.backgroundColor = previousColor;
        }

        private void DrawDataColumn()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                DrawSelectedToolbar();

                if (_selectedSlot == null)
                {
                    EditorGUILayout.HelpBox(
                        "Select a valid slot to inspect metadata, context and save sections.",
                        MessageType.None);
                    return;
                }

                if (!_selectedSlot.IsValid)
                {
                    EditorGUILayout.HelpBox(_selectedSlot.Diagnostic, MessageType.Error);
                    return;
                }

                int nextMode = GUILayout.Toolbar(_viewMode, new[] { "GUI", "Raw JSON" });

                if (nextMode != _viewMode)
                {
                    if (nextMode == 1 && _editedRoot != null)
                    {
                        _rawJson = _editedRoot.ToString(Formatting.Indented);
                    }
                    else if (nextMode == 0)
                    {
                        TryParseRawJson(showDialog: true);
                    }

                    _viewMode = nextMode;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    _dataScroll = EditorGUILayout.BeginScrollView(_dataScroll);

                    if (_viewMode == 0)
                    {
                        if (_editedRoot != null)
                        {
                            _changed |= _treeDrawer.Draw(_editedRoot);
                        }
                    }
                    else
                    {
                        GUIStyle textArea = new GUIStyle(EditorStyles.textArea)
                        {
                            wordWrap = false
                        };

                        EditorGUI.BeginChangeCheck();
                        _rawJson = EditorGUILayout.TextArea(
                            _rawJson,
                            textArea,
                            GUILayout.ExpandHeight(true),
                            GUILayout.MinHeight(420));

                        if (EditorGUI.EndChangeCheck())
                        {
                            _changed = true;
                        }
                    }

                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawSelectedToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           _busy || _selectedSlot == null || !_selectedSlot.IsValid))
                {
                    if (GUILayout.Button("Save Edited Data", GUILayout.Height(26)))
                    {
                        _ = SaveEditedDataAsync();
                    }

                    if (GUILayout.Button("Reload", GUILayout.Width(80), GUILayout.Height(26)))
                    {
                        _ = SelectSlotAsync(_selectedSlot);
                    }
                }

                using (new EditorGUI.DisabledScope(_busy || _selectedSlot == null))
                {
                    if (GUILayout.Button("Delete Slot", GUILayout.Width(90), GUILayout.Height(26)))
                    {
                        _ = DeleteSelectedAsync();
                    }
                }

                GUILayout.FlexibleSpace();

                if (_changed)
                {
                    EditorGUILayout.LabelField("Unsaved changes", EditorStyles.miniBoldLabel, GUILayout.Width(110));
                }
            }
        }

        private async Task RefreshSlotsAsync()
        {
            if (_store == null || _busy)
            {
                return;
            }

            _busy = true;
            _status = "Validating save slots and recovery copies...";
            Repaint();

            SaveCatalogResult result = await _store.ListAsync();
            _slots.Clear();

            if (result.Success)
            {
                _slots.AddRange(result.Slots);
                _status = $"Found {_slots.Count} slot(s). Every listed valid slot passed checksum validation.";

                if (_selectedSlot != null)
                {
                    _selectedSlot = _slots.Find(slot =>
                        slot.Metadata.SlotId == _selectedSlot.Metadata.SlotId);
                }
            }
            else
            {
                _status = result.Message;
            }

            _busy = false;
            Repaint();
        }

        private async Task SelectSlotAsync(SaveSlotInfo slot)
        {
            if (_busy)
            {
                return;
            }

            _selectedSlot = slot;
            _editedRoot = null;
            _rawJson = string.Empty;
            _changed = false;

            if (!slot.IsValid)
            {
                _status = slot.Diagnostic;
                Repaint();
                return;
            }

            _busy = true;
            _status = $"Reading {slot.Metadata.SlotId}...";
            Repaint();

            SaveReadResult read = await _store.ReadAsync(slot.Metadata.SlotId);

            if (read.Success)
            {
                var debugDocument = new SaveDebugDocument
                {
                    Metadata = read.Slot.Metadata,
                    Document = read.Document
                };

                _rawJson = new NewtonsoftSaveSerializer().Serialize(debugDocument, pretty: true);
                _editedRoot = JObject.Parse(_rawJson);
                _selectedSlot = read.Slot;
                _status = read.Slot.Recovered
                    ? "Loaded a valid recovery generation. Saving will repair the primary file."
                    : "Slot loaded and ready to inspect.";
            }
            else
            {
                _status = read.Message;
            }

            _busy = false;
            Repaint();
        }

        private async Task SaveEditedDataAsync()
        {
            if (_selectedSlot == null || _busy)
            {
                return;
            }

            if (_viewMode == 1 && !TryParseRawJson(showDialog: true))
            {
                return;
            }

            SaveDebugDocument edited;
            var serializer = new NewtonsoftSaveSerializer();

            try
            {
                edited = serializer.Deserialize<SaveDebugDocument>(
                    _editedRoot.ToString(Formatting.None));

                if (edited.Metadata == null || edited.Document == null)
                {
                    throw new JsonSerializationException("Metadata and document are required.");
                }

                edited.Metadata.SlotId = _selectedSlot.Metadata.SlotId;
                edited.Metadata.LastSavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                edited.Metadata.Reason = SaveReason.DebugEdit;
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Invalid Save Data", exception.Message, "OK");
                return;
            }

            _busy = true;
            _status = "Validating and atomically writing edited save...";
            Repaint();

            SaveOperationResult result = await _store.WriteAsync(edited.Metadata, edited.Document);
            _status = result.Success ? "Edited save written successfully." : result.Message;
            _busy = false;
            _changed = !result.Success;

            if (result.Success)
            {
                await RefreshSlotsAsync();
                SaveSlotInfo refreshed = _slots.Find(slot =>
                    slot.Metadata.SlotId == edited.Metadata.SlotId);

                if (refreshed != null)
                {
                    await SelectSlotAsync(refreshed);
                }
            }

            Repaint();
        }

        private async Task DeleteSelectedAsync()
        {
            if (_selectedSlot == null ||
                !EditorUtility.DisplayDialog(
                    "Delete Save Slot",
                    $"Delete '{_selectedSlot.Metadata.DisplayName}' and all of its backups?",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            _busy = true;
            SaveOperationResult result = await _store.DeleteAsync(_selectedSlot.Metadata.SlotId);
            _status = result.Success ? "Slot and recovery copies deleted." : result.Message;
            _selectedSlot = null;
            _editedRoot = null;
            _rawJson = string.Empty;
            _busy = false;
            await RefreshSlotsAsync();
        }

        private bool TryParseRawJson(bool showDialog)
        {
            try
            {
                _editedRoot = JObject.Parse(_rawJson);
                return true;
            }
            catch (Exception exception)
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Invalid JSON", exception.Message, "OK");
                }

                return false;
            }
        }

        private void FindSettingsAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:SaveSystemSettings");

            if (guids.Length > 0)
            {
                _settings = AssetDatabase.LoadAssetAtPath<SaveSystemSettings>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        private void RebuildStore()
        {
            _store?.Dispose();

            if (_settings != null && _temporarySettings != null)
            {
                DestroyImmediate(_temporarySettings);
                _temporarySettings = null;
            }

            _store = new SaveFileStore(
                GetActiveSettings(),
                new NewtonsoftSaveSerializer(),
                new GZipSaveCompressor());
        }

        private SaveSystemSettings GetActiveSettings()
        {
            if (_settings != null)
            {
                return _settings;
            }

            if (_temporarySettings == null)
            {
                _temporarySettings = SaveSystemSettings.CreateRuntimeDefault();
            }

            return _temporarySettings;
        }
    }
}
#endif
