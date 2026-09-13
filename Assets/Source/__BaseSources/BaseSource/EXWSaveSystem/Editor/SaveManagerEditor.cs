#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Editor
{
    [CustomEditor(typeof(SaveManager))]
    public sealed class SaveManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Runtime Monitor", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to inspect the active slot and registered providers.",
                    MessageType.Info);
                return;
            }

            var manager = (SaveManager)target;
            EditorGUILayout.LabelField("Active Slot", manager.HasActiveSlot ? manager.ActiveSlotId : "None");
            EditorGUILayout.Toggle("Dirty", manager.IsDirty);
            EditorGUILayout.Toggle("Saving", manager.IsSaving);
            EditorGUILayout.Toggle("Prepared Load", manager.HasPreparedLoad);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Registered Sections", EditorStyles.miniBoldLabel);

            foreach (ISaveSection section in SaveProviderRegistry.GetSections())
            {
                EditorGUILayout.LabelField(
                    section.Key,
                    $"v{section.CurrentVersion}  order {section.RestoreOrder}");
            }

            EditorGUILayout.LabelField("Context Providers", EditorStyles.miniBoldLabel);

            foreach (ISaveContextProvider provider in SaveProviderRegistry.GetContextProviders())
            {
                EditorGUILayout.LabelField(provider.Key, provider.GetType().Name);
            }

            using (new EditorGUI.DisabledScope(!manager.HasActiveSlot || manager.IsSaving))
            {
                if (GUILayout.Button("Save Active Slot"))
                {
                    SaveNow(manager);
                }
            }

            if (manager.HasPreparedLoad && GUILayout.Button("Restore Prepared Load"))
            {
                SaveOperationResult result = manager.RestorePreparedLoad();
                Debug.Log(result.Success ? result.Message : $"Restore failed: {result.Message}", manager);
            }

            Repaint();
        }

        private static async void SaveNow(SaveManager manager)
        {
            SaveOperationResult result = await manager.SaveActiveSlotAsync(SaveReason.Manual);
            Debug.Log(result.Success ? result.Message : $"Save failed: {result.Message}", manager);
        }
    }
}
#endif
