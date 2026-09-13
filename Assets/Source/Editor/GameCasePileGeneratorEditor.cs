#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameCasePileGenerator))]
public class GameCasePileGeneratorEditor : Editor
{
    private GameCasePileGenerator Generator =>
        (GameCasePileGenerator)target;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        serializedObject.ApplyModifiedProperties();

        GUILayout.Space(12);

        EditorGUILayout.LabelField(
            "Pile Tools",
            EditorStyles.boldLabel
        );

        GUILayout.Space(4);

        Color defaultColor =
            GUI.backgroundColor;

        GUI.backgroundColor =
            new Color(0.35f, 0.85f, 0.45f);

        if (GUILayout.Button(
                "GENERATE PILE",
                GUILayout.Height(36)))
        {
            serializedObject.ApplyModifiedProperties();

            Generator.Generate();

            EditorUtility.SetDirty(Generator);
        }

        GUI.backgroundColor =
            new Color(0.35f, 0.65f, 1f);

        if (GUILayout.Button(
                "RANDOMIZE + GENERATE",
                GUILayout.Height(28)))
        {
            serializedObject.ApplyModifiedProperties();

            Generator.RandomizeAndGenerate();

            EditorUtility.SetDirty(Generator);
        }

        GUILayout.Space(6);

        GUI.backgroundColor =
            new Color(1f, 0.65f, 0.25f);

        if (GUILayout.Button(
                "CLEAR PILE",
                GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog(
                    "Clear Pile",
                    "Remove all children from GameCaseRoot?",
                    "Clear",
                    "Cancel"))
            {
                Generator.ClearPile();
            }
        }

        GUI.backgroundColor =
            new Color(1f, 0.35f, 0.35f);

        if (GUILayout.Button(
                "REMOVE ROOT",
                GUILayout.Height(24)))
        {
            if (EditorUtility.DisplayDialog(
                    "Remove Generated Root",
                    "Delete GameCaseRoot and all of its children?",
                    "Remove",
                    "Cancel"))
            {
                Generator.RemoveRoot();
            }
        }

        GUI.backgroundColor =
            defaultColor;

        GUILayout.Space(8);

        DrawInfo();
    }

    private void DrawInfo()
    {
        if (!Generator.GeneratedRoot)
        {
            EditorGUILayout.HelpBox(
                "No generated root exists yet. " +
                "Generate will automatically create GameCaseRoot.",
                MessageType.Info
            );

            return;
        }

        EditorGUILayout.HelpBox(
            $"Generated Root: {Generator.GeneratedRoot.name}\n" +
            $"Current Children: {Generator.GeneratedRoot.childCount}",
            MessageType.None
        );
    }
}

#endif