#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.UI.Editor
{
    [CustomEditor(typeof(MultiplayerMainMenuPresenter))]
    public sealed class MultiplayerMainMenuPresenterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            MultiplayerMainMenuPresenter presenter =
                (MultiplayerMainMenuPresenter)target;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Setup Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "NetworkRuntime references are intentionally not serialized here. " +
                "The MainMenu scene binds to MultiplayerFlowController, " +
                "SteamLobbyService and MultiplayerSessionCoordinator at Play time. " +
                "Btn_Single and Btn_StartGame are wired by hierarchy name. " +
                "RejoinSessionPanel is optional and is auto-wired when present.",
                MessageType.Info);

            if (GUILayout.Button("AUTO WIRE KNOWN HIERARCHY", GUILayout.Height(30f)))
            {
                Undo.RecordObject(presenter, "Auto Wire Multiplayer Main Menu UI");
                presenter.AutoWireKnownHierarchy();
                EditorUtility.SetDirty(presenter);
                presenter.ValidateReferences();
            }

            if (GUILayout.Button("VALIDATE REFERENCES", GUILayout.Height(26f)))
            {
                presenter.ValidateReferences();
            }

            if (GUILayout.Button("RUN LOBBY CODE SELF TEST", GUILayout.Height(26f)))
            {
                RunLobbyCodeSelfTest(presenter);
            }

            if (GUILayout.Button("FORGET SAVED LAST SESSION", GUILayout.Height(24f)))
            {
                presenter.ForgetLastSession();
                Debug.Log("[MainMenuUI] Saved last-session pointer removed.", presenter);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("LOG CURRENT UI STATE", GUILayout.Height(26f)))
                {
                    presenter.LogCurrentUiState();
                }
            }
        }

        private static void RunLobbyCodeSelfTest(Object context)
        {
            ulong[] testValues =
            {
                1UL,
                480UL,
                109775245199736838UL,
                ulong.MaxValue
            };

            for (int i = 0; i < testValues.Length; i++)
            {
                ulong expected = testValues[i];
                string code = SteamLobbyJoinCode.Encode(expected);

                if (!SteamLobbyJoinCode.TryDecode(
                        code,
                        out ulong actual,
                        out string error) ||
                    actual != expected)
                {
                    Debug.LogError(
                        $"[LobbyCodeSelfTest] FAIL: Value={expected}, " +
                        $"Code={code}, Actual={actual}, Error={error}",
                        context);
                    return;
                }
            }

            const ulong rawDecimalValue = 109775245199736838UL;

            if (!SteamLobbyJoinCode.TryDecode(
                    rawDecimalValue.ToString(),
                    out ulong rawDecoded) ||
                rawDecoded != rawDecimalValue)
            {
                Debug.LogError(
                    "[LobbyCodeSelfTest] FAIL: raw decimal LobbyID was rejected.",
                    context);
                return;
            }

            string validCode = SteamLobbyJoinCode.Encode(rawDecimalValue);
            char replacement = validCode[validCode.Length - 1] == '0' ? '1' : '0';
            string invalidCode = validCode.Substring(0, validCode.Length - 1) +
                                 replacement;

            if (SteamLobbyJoinCode.TryDecode(invalidCode, out _))
            {
                Debug.LogError(
                    "[LobbyCodeSelfTest] FAIL: an invalid checksum was accepted.",
                    context);
                return;
            }

            Debug.Log(
                $"[LobbyCodeSelfTest] PASS: {testValues.Length + 2}/" +
                $"{testValues.Length + 2} validations.",
                context);
        }
    }

    [CustomEditor(typeof(SteamLobbyListEntryView))]
    public sealed class SteamLobbyListEntryViewEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(6f);

            if (GUILayout.Button("VALIDATE ENTRY REFERENCES"))
            {
                ((SteamLobbyListEntryView)target).ValidateReferences();
            }
        }
    }
}
#endif
