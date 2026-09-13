#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public class DirtyHelperWindow : EditorWindow
{
    private List<Object> _targetObjects = new List<Object>();
    private Vector2 _scrollPos;

    [MenuItem("Tools/Dirty Helper")]
    public static void OpenWindow()
    {
        GetWindow<DirtyHelperWindow>("Dirty Helper");
    }

    private void OnGUI()
    {
        GUILayout.Label("Batch Dirty Tool", EditorStyles.boldLabel);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Add ScriptableObjects or scene GameObjects.\nMark them dirty and save.",
            MessageType.Info
        );

        if (GUILayout.Button("Add Selection"))
        {
            foreach (var obj in Selection.objects)
            {
                if (obj != null && !_targetObjects.Contains(obj))
                    _targetObjects.Add(obj);
            }
        }

        EditorGUILayout.Space();

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        for (int i = 0; i < _targetObjects.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();

            _targetObjects[i] = EditorGUILayout.ObjectField(_targetObjects[i], typeof(Object), true);

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                _targetObjects.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(_targetObjects.Count == 0))
        {
            if (GUILayout.Button("Clear List"))
            {
                _targetObjects.Clear();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Set Dirty & Save All"))
            {
                ProcessAll();
            }
        }
    }

    private void ProcessAll()
    {
        foreach (var obj in _targetObjects)
        {
            if (obj == null)
                continue;

            EditorUtility.SetDirty(obj);
            Debug.Log($"SetDirty → {obj.name}");

            if (EditorUtility.IsPersistent(obj))
            {
                AssetDatabase.SaveAssets();
                Debug.Log("AssetDatabase.SaveAssets()");
            }
            else if (obj is GameObject go)
            {
                var scene = go.scene;
                if (scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    Debug.Log($"MarkSceneDirty → {scene.name}");
                }
            }
        }
    }
}
#endif