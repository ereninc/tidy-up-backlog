#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerSettings))]
    public sealed class MultiplayerSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            MultiplayerSettings settings = (MultiplayerSettings)target;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Effective Join Policy", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                BuildPolicySummary(settings),
                MessageType.Info);

            if (GUILayout.Button("LOG EFFECTIVE SETTINGS", GUILayout.Height(28f)))
            {
                Debug.Log(
                    "[MultiplayerSettings] " + BuildPolicySummary(settings),
                    settings);
            }

            if (GUILayout.Button("RUN JOIN POLICY SELF TEST", GUILayout.Height(28f)))
            {
                MultiplayerJoinPolicySelfTest.Run(settings);
            }
        }

        private static string BuildPolicySummary(MultiplayerSettings settings)
        {
            string reconnect;

            if (!settings.SupportsSessionRejoin)
            {
                reconnect = "disabled; disconnected slots are released";
            }
            else if (settings.SessionSlotRetention ==
                     SessionSlotRetentionPolicy.Timed)
            {
                reconnect =
                    $"enabled; offline slots are retained for " +
                    $"{settings.TimedSessionSlotRetentionSeconds:0}s";
            }
            else
            {
                reconnect =
                    "enabled; every participant owns the same slot until the " +
                    "host session ends";
            }

            string lateJoin = settings.DefaultAllowLateJoin
                ? "enabled by default; new players may claim unassigned slots"
                : "disabled by default; only existing roster members can rejoin";

            string gracefulExit = settings.GracefulExitSlotBehaviour ==
                                  GracefulExitSlotPolicy.PreserveSlot
                ? "preserves the player's slot"
                : "releases the player's slot";

            return $"Reconnect: {reconnect}.\nLate Join: {lateJoin}.\n" +
                   $"Return To Main Menu: {gracefulExit}.\n" +
                   $"Last-session UI: " +
                   $"{settings.LastSessionRejoinBehaviour}.\n" +
                   $"Compatibility: {settings.ProductId} / protocol " +
                   $"{settings.ProtocolVersion} / build {settings.BuildId}.";
        }
    }

    internal static class MultiplayerJoinPolicySelfTest
    {
        [MenuItem("Tools/EXW/Multiplayer/Run Join Policy Self Test", priority = 20)]
        public static void RunFromMenu()
        {
            Run(Selection.activeObject);
        }

        public static void Run(Object context)
        {
            int passed = 0;

            passed += Expect(
                SteamLobbyState.Waiting,
                false,
                false,
                MultiplayerJoinKind.WaitingRoom,
                context);
            passed += Expect(
                SteamLobbyState.Playing,
                false,
                true,
                MultiplayerJoinKind.Reconnect,
                context);
            passed += Expect(
                SteamLobbyState.Playing,
                true,
                false,
                MultiplayerJoinKind.LateJoin,
                context);
            passed += Expect(
                SteamLobbyState.Playing,
                true,
                true,
                MultiplayerJoinKind.Reconnect,
                context);
            passed += Expect(
                SteamLobbyState.Playing,
                false,
                false,
                MultiplayerJoinKind.Denied,
                context);
            passed += Expect(
                SteamLobbyState.Closing,
                true,
                true,
                MultiplayerJoinKind.Denied,
                context);
            passed += Expect(
                SteamLobbyState.Playing,
                true,
                false,
                MultiplayerJoinKind.Denied,
                context,
                false);
            passed += Expect(
                SteamLobbyState.Playing,
                true,
                true,
                MultiplayerJoinKind.Reconnect,
                context,
                false);

            if (passed == 8)
            {
                Debug.Log(
                    "[JoinPolicySelfTest] PASS 8/8 validations.",
                    context);
            }
        }

        private static int Expect(
            SteamLobbyState state,
            bool lateJoin,
            bool reconnect,
            MultiplayerJoinKind expected,
            Object context,
            bool hasUnassignedSessionSlot = true)
        {
            MultiplayerJoinKind actual = MultiplayerJoinPolicy.Classify(
                state,
                lateJoin,
                reconnect,
                hasUnassignedSessionSlot);

            if (actual == expected)
            {
                return 1;
            }

            Debug.LogError(
                $"[JoinPolicySelfTest] FAIL: State={state}, " +
                $"LateJoin={lateJoin}, Reconnect={reconnect}, " +
                $"UnassignedSlot={hasUnassignedSessionSlot}, " +
                $"Expected={expected}, Actual={actual}.",
                context);
            return 0;
        }
    }

    public static class MultiplayerSettingsAssetUtility
    {
        private const string ResourcesDirectory = "Assets/Resources";
        private const string AssetPath =
            ResourcesDirectory + "/MultiplayerSettings.asset";

        [MenuItem("Tools/EXW/Multiplayer/Create Or Select Settings", priority = 0)]
        public static void CreateOrSelectSettings()
        {
            MultiplayerSettings settings =
                AssetDatabase.LoadAssetAtPath<MultiplayerSettings>(AssetPath);

            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder(ResourcesDirectory))
                {
                    if (!AssetDatabase.IsValidFolder("Assets"))
                    {
                        Debug.LogError(
                            "[MultiplayerSettings] Project Assets folder was not found.");
                        return;
                    }

                    AssetDatabase.CreateFolder("Assets", "Resources");
                }

                settings = ScriptableObject.CreateInstance<MultiplayerSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
                AssetDatabase.SaveAssets();
                MultiplayerSettings.InvalidateRuntimeCache();
                Debug.Log(
                    $"[MultiplayerSettings] Created {AssetPath}.",
                    settings);
            }

            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("Tools/EXW/Multiplayer/Create Or Select Settings", true)]
        private static bool ValidateCreateOrSelectSettings()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
#endif
