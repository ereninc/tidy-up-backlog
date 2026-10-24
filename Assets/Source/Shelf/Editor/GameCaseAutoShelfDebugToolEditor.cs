using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    [CustomEditor(typeof(GameCaseAutoShelfDebugTool))]
    public sealed class GameCaseAutoShelfDebugToolEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var tool = (GameCaseAutoShelfDebugTool)target;
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Server/host Play Mode only. Shift+G toggles start/cancel. Configure target roots on Shelf Completion Group; empty roots target this scene. Registry defaults to GameCaseInstanceRegistry.Instance.", MessageType.Info);
            EditorGUILayout.LabelField("Status", tool.IsBusy ? "Batch running" : "Idle");
            EditorGUILayout.LabelField("Next case index", tool.NextCaseIndex.ToString());
            using (new EditorGUI.DisabledScope(!tool.CanStartBatch))
                if (GUILayout.Button("Place Next Batch")) tool.PlaceNextBatch();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !tool.IsServer || !tool.IsBusy))
                if (GUILayout.Button("Cancel Current Batch")) tool.CancelCurrentBatch();
        }

        public override bool RequiresConstantRepaint() => target != null && ((GameCaseAutoShelfDebugTool)target).IsBusy;
    }
}
