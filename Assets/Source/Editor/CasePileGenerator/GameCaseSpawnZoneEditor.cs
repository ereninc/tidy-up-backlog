using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameCaseSpawnZone))]
public sealed class GameCaseSpawnZoneEditor : Editor
{
    private GameCaseSpawnZone Zone => (GameCaseSpawnZone)target;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        if (!Zone.IsValid)
        {
            EditorGUILayout.HelpBox(
                "This polygon is too small or crosses itself. Move its points until the outline is valid.",
                MessageType.Error);
        }

        GameCaseDistributionGenerator generator = Zone.GetComponentInParent<GameCaseDistributionGenerator>();

        using (new EditorGUI.DisabledScope(!generator))
        {
            EditorGUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Zone"))
                    GameCaseDistributionBaker.GenerateZone(generator, Zone);

                if (GUILayout.Button("Randomize"))
                    GameCaseDistributionBaker.GenerateZone(generator, Zone, randomizeSeed: true);

                if (GUILayout.Button("Clear"))
                    GameCaseDistributionBaker.ClearZone(Zone);
            }
        }
    }

    private void OnSceneGUI()
    {
        if (GameCaseDistributionWindow.IsDrawingLasso || Zone.Points.Count < 3)
            return;

        Event current = Event.current;

        for (int i = 0; i < Zone.Points.Count; i++)
        {
            Vector3 worldPoint = Zone.GetWorldPoint(i);
            float handleSize = HandleUtility.GetHandleSize(worldPoint) * 0.065f;

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(
                worldPoint,
                handleSize,
                Vector3.zero,
                Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Zone, "Move Game Case Zone Point");
                Vector3 local = Zone.transform.InverseTransformPoint(moved);
                Zone.SetLocalPoint(i, new Vector2(local.x, local.z));
                EditorUtility.SetDirty(Zone);
            }

            if (current.control &&
                current.type == EventType.MouseDown &&
                current.button == 0 &&
                HandleUtility.DistanceToCircle(worldPoint, handleSize) < 10f &&
                Zone.Points.Count > 3)
            {
                Undo.RecordObject(Zone, "Remove Game Case Zone Point");
                Zone.RemoveLocalPoint(i);
                EditorUtility.SetDirty(Zone);
                current.Use();
                break;
            }
        }

        for (int i = 0; i < Zone.Points.Count; i++)
        {
            int next = (i + 1) % Zone.Points.Count;
            Vector3 midpoint = Vector3.Lerp(Zone.GetWorldPoint(i), Zone.GetWorldPoint(next), 0.5f);
            float size = HandleUtility.GetHandleSize(midpoint) * 0.035f;
            Handles.color = new Color(1f, 1f, 1f, 0.75f);

            if (!Handles.Button(midpoint, Quaternion.identity, size, size, Handles.DotHandleCap))
                continue;

            Undo.RecordObject(Zone, "Add Game Case Zone Point");
            Vector3 local = Zone.transform.InverseTransformPoint(midpoint);
            Zone.InsertLocalPoint(next, new Vector2(local.x, local.z));
            EditorUtility.SetDirty(Zone);
            break;
        }

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12f, 42f, 280f, 42f), EditorStyles.helpBox);
        GUILayout.Label("Drag points • click edge dots to add • Ctrl-click a point to remove", EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }
}
