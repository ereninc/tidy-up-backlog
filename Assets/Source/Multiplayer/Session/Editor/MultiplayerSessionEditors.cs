#if UNITY_EDITOR
using UnityEditor;
using Unity.Netcode.Transports.SinglePlayer;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerSessionCoordinator))]
    public sealed class MultiplayerSessionCoordinatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            MultiplayerSessionCoordinator coordinator =
                (MultiplayerSessionCoordinator)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Session Setup", EditorStyles.boldLabel);

            bool hasSinglePlayerTransport =
                coordinator.GetComponent<SinglePlayerTransport>() != null;
            bool hasSessionRoster =
                coordinator.GetComponent<MultiplayerSessionRoster>() != null;

            EditorGUILayout.HelpBox(
                hasSinglePlayerTransport
                    ? "SinglePlayerTransport found. Scene names, timeouts and running-session join policy now come from the central MultiplayerSettings asset."
                    : "Add NGO SinglePlayerTransport to NetworkRuntime before testing Play Singleplayer.",
                hasSinglePlayerTransport ? MessageType.Info : MessageType.Error);

            EditorGUILayout.HelpBox(
                hasSessionRoster
                    ? "MultiplayerSessionRoster found. Stable SteamID session slots are active."
                    : "Install MultiplayerSessionRoster on NetworkRuntime. Runtime auto-install is only a safety fallback.",
                hasSessionRoster ? MessageType.Info : MessageType.Error);

            if (!hasSessionRoster &&
                GUILayout.Button("INSTALL SESSION ROSTER", GUILayout.Height(27f)))
            {
                SessionRosterEditorUtility.InstallOnNetworkRuntime();
            }

            if (GUILayout.Button("OPEN MULTIPLAYER SETTINGS", GUILayout.Height(26f)))
            {
                MultiplayerSettingsAssetUtility.CreateOrSelectSettings();
            }

            if (GUILayout.Button("VALIDATE SESSION SETUP", GUILayout.Height(28f)))
            {
                coordinator.ValidateSetup();
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("START SINGLEPLAYER", GUILayout.Height(26f)))
                {
                    coordinator.StartSinglePlayer();
                }

                if (GUILayout.Button("START CO-OP GAME (HOST)", GUILayout.Height(26f)))
                {
                    coordinator.StartCoopGame();
                }

                if (GUILayout.Button("LEAVE + RETURN TO MAIN MENU", GUILayout.Height(26f)))
                {
                    coordinator.LeaveSessionAndReturnToMainMenu();
                }

                if (GUILayout.Button("LOG SESSION STATE", GUILayout.Height(24f)))
                {
                    coordinator.LogSessionState();
                }
            }
        }
    }

    [CustomEditor(typeof(GameplayPauseMenuController))]
    public sealed class GameplayPauseMenuControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            GameplayPauseMenuController controller =
                (GameplayPauseMenuController)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Pause Menu Setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Keep this component on an always-active GameplayCanvas object. " +
                "Pause Menu Root must be a separate child, otherwise disabling " +
                "the panel also disables Escape input.",
                MessageType.Info);

            if (GUILayout.Button("AUTO WIRE KNOWN HIERARCHY", GUILayout.Height(28f)))
            {
                Undo.RecordObject(controller, "Auto Wire Gameplay Pause Menu");
                controller.AutoWireKnownHierarchy();
                EditorUtility.SetDirty(controller);
            }

            if (GUILayout.Button("VALIDATE PAUSE MENU", GUILayout.Height(26f)))
            {
                controller.ValidateSetup();
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("OPEN"))
                {
                    controller.OpenPauseMenu();
                }

                if (GUILayout.Button("BACK / RESUME"))
                {
                    controller.ClosePauseMenu();
                }

                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("RETURN TO MAIN MENU", GUILayout.Height(26f)))
                {
                    controller.ReturnToMainMenu();
                }
            }
        }
    }

    [CustomEditor(typeof(NetworkPlayerPresenceController))]
    public sealed class NetworkPlayerPresenceControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            NetworkPlayerPresenceController presence =
                (NetworkPlayerPresenceController)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Gameplay Only Objects should contain child visuals/UI only. " +
                "Never add the player NetworkObject root, NetworkInteractionController, " +
                "or NetworkItemCarrier.",
                MessageType.Info);

            if (GUILayout.Button("AUTO ASSIGN + VALIDATE", GUILayout.Height(28f)))
            {
                Undo.RecordObject(presence, "Configure Network Player Presence");
                presence.AutoAssignReferences();
                EditorUtility.SetDirty(presence);
                presence.ValidateSetup();
            }
        }
    }

    [CustomEditor(typeof(PersistentSceneLoadingOverlay))]
    public sealed class PersistentSceneLoadingOverlayEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Put this component on persistent NetworkRuntime. Overlay Root must " +
                "be a child canvas/root so the component itself stays enabled while hidden.",
                MessageType.Info);

            if (GUILayout.Button("VALIDATE OVERLAY", GUILayout.Height(26f)))
            {
                ((PersistentSceneLoadingOverlay)target).ValidateSetup();
            }
        }
    }
}
#endif
