#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EXW.SaveSystem.Editor
{
    [CustomEditor(typeof(SaveSystemSettings))]
    public sealed class SaveSystemSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(6);

            var settings = (SaveSystemSettings)target;

            try
            {
                EditorGUILayout.LabelField("Resolved Directory", settings.GetSaveDirectory());

                if (GUILayout.Button("Open Save Directory"))
                {
                    Directory.CreateDirectory(settings.GetSaveDirectory());
                    EditorUtility.RevealInFinder(settings.GetSaveDirectory());
                }

                if (GUILayout.Button("Open Save Data Editor"))
                {
                    SaveDataEditorWindow.Open();
                }
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }
    }
}
#endif
