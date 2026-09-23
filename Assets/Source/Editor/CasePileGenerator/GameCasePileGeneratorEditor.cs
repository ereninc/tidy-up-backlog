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

        GUILayout.Space(14);

        EditorGUILayout.LabelField(
            "Physics Pile Baker",
            EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "Cases are temporarily simulated with Unity PhysX, then their final transforms are baked. Rigidbody components added by the baker are removed afterwards.",
            MessageType.Info);

        GUILayout.Space(6);

        Color oldColor =
            GUI.backgroundColor;

        GUI.backgroundColor =
            new Color(
                0.35f,
                0.85f,
                0.45f);

        if (GUILayout.Button(
                "DROP + BAKE PILE",
                GUILayout.Height(40)))
        {
            serializedObject.ApplyModifiedProperties();

            Generator.GeneratePhysicsPile();

            EditorUtility.SetDirty(
                Generator);
        }

        GUI.backgroundColor =
            new Color(
                0.35f,
                0.65f,
                1f);

        if (GUILayout.Button(
                "RANDOMIZE + DROP + BAKE",
                GUILayout.Height(32)))
        {
            serializedObject.ApplyModifiedProperties();

            Generator.RandomizeAndGenerate();

            EditorUtility.SetDirty(
                Generator);
        }

        GUILayout.Space(8);

        GUI.backgroundColor =
            new Color(
                1f,
                0.65f,
                0.25f);

        if (GUILayout.Button(
                "CLEAR PILE",
                GUILayout.Height(28)))
        {
            Generator.ClearPile();
        }

        GUI.backgroundColor =
            new Color(
                1f,
                0.35f,
                0.35f);

        if (GUILayout.Button(
                "REMOVE ROOT",
                GUILayout.Height(25)))
        {
            Generator.RemoveRoot();
        }

        GUI.backgroundColor =
            oldColor;

        GUILayout.Space(8);

        if (Generator.GeneratedRoot)
        {
            EditorGUILayout.HelpBox(
                $"Current cases: {Generator.GeneratedRoot.childCount}",
                MessageType.None);
        }
    }
}

#endif