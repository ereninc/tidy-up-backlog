using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameCaseDistributionGenerator))]
public sealed class GameCaseDistributionGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var generator = (GameCaseDistributionGenerator)target;

        EditorGUILayout.Space(6f);

        if (GUILayout.Button("Open Distribution Window", GUILayout.Height(26f)))
            GameCaseDistributionWindow.Open();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate All"))
                GameCaseDistributionBaker.GenerateAll(generator);

            if (GUILayout.Button("Randomize All"))
                GameCaseDistributionBaker.GenerateAll(generator, randomizeSeed: true);

            if (GUILayout.Button("Clear All"))
                GameCaseDistributionBaker.ClearAll(generator);
        }
    }
}
