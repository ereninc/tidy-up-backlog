#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Editor
{
    [CustomEditor(typeof(PersistentSaveId))]
    public sealed class PersistentSaveIdEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var persistentId = (PersistentSaveId)target;
            EditorGUILayout.LabelField("Persistent ID", persistentId.Value);

            if (GUILayout.Button("Copy ID"))
            {
                EditorGUIUtility.systemCopyBuffer = persistentId.Value;
            }

            Color previousColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.35f);

            if (GUILayout.Button("Regenerate ID") &&
                EditorUtility.DisplayDialog(
                    "Regenerate Persistent ID",
                    "Existing save files referencing this object will no longer find it. Continue?",
                    "Regenerate",
                    "Cancel"))
            {
                Undo.RecordObject(persistentId, "Regenerate Persistent Save ID");
                persistentId.RegenerateEditorValue();
            }

            GUI.backgroundColor = previousColor;

            if (HasDuplicate(persistentId))
            {
                EditorGUILayout.HelpBox(
                    "Another loaded scene object has the same persistent ID.",
                    MessageType.Error);
            }
        }

        private static bool HasDuplicate(PersistentSaveId target)
        {
            PersistentSaveId[] all = Resources.FindObjectsOfTypeAll<PersistentSaveId>();

            for (int i = 0; i < all.Length; i++)
            {
                PersistentSaveId candidate = all[i];

                if (candidate == target || EditorUtility.IsPersistent(candidate))
                {
                    continue;
                }

                if (candidate.Value == target.Value)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
#endif
