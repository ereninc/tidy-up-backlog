#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
    [CustomEditor(typeof(MultiplayerSessionRoster))]
    public sealed class MultiplayerSessionRosterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            MultiplayerSessionRoster roster =
                (MultiplayerSessionRoster)target;
            MultiplayerSettings settings = MultiplayerSettings.Current;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Session Roster", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                BuildPolicySummary(settings),
                MessageType.Info);

            if (Application.isPlaying)
            {
                DrawLiveRoster(roster);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Live slot ownership appears here in Play Mode.",
                    MessageType.None);
            }

            if (GUILayout.Button("VALIDATE ROSTER SETUP", GUILayout.Height(27f)))
            {
                roster.ValidateSetup();
            }

            if (GUILayout.Button("RUN SESSION ROSTER SELF TEST", GUILayout.Height(27f)))
            {
                SessionRosterSelfTest.Run(roster);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("LOG LIVE ROSTER", GUILayout.Height(25f)))
                {
                    roster.LogRoster();
                }

                EditorGUILayout.Space(5f);
                EditorGUILayout.LabelField(
                    "Host Moderation — Offline Slots",
                    EditorStyles.boldLabel);

                int capacity = Mathf.Max(1, roster.Capacity);

                for (int i = 0; i < capacity; i++)
                {
                    int slotIndex = i;

                    if (GUILayout.Button($"RELEASE SLOT {slotIndex + 1}"))
                    {
                        if (!roster.ReleaseOfflineSlotByIndex(slotIndex))
                        {
                            Debug.LogWarning(
                                $"[SessionRoster] Slot {slotIndex} was not an " +
                                "offline host-managed slot.",
                                roster);
                        }
                    }
                }
            }
        }

        private static string BuildPolicySummary(MultiplayerSettings settings)
        {
            string retention;

            switch (settings.SessionSlotRetention)
            {
                case SessionSlotRetentionPolicy.Timed:
                    retention =
                        $"{settings.TimedSessionSlotRetentionSeconds:0}s after " +
                        "disconnect";
                    break;

                case SessionSlotRetentionPolicy.ReleaseImmediately:
                    retention = "released immediately";
                    break;

                default:
                    retention = "until the host session ends";
                    break;
            }

            return
                $"Capacity: {settings.MaximumPlayers} stable SteamID slots.\n" +
                $"Offline retention: {retention}.\n" +
                $"Graceful exit: {settings.GracefulExitSlotBehaviour}.\n" +
                "Only unassigned slots accept new late-join players; reconnecting " +
                "SteamIDs reclaim their original slot.";
        }

        private static void DrawLiveRoster(MultiplayerSessionRoster roster)
        {
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField("Live Roster", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Summary",
                $"{roster.ConnectedCount} online  •  " +
                $"{roster.OfflineCount} offline  •  " +
                $"{roster.UnassignedCount} open");

            IReadOnlyList<SessionRosterSlotSnapshot> slots = roster.Slots;

            for (int slotIndex = 0; slotIndex < roster.Capacity; slotIndex++)
            {
                SessionRosterSlotSnapshot slot = null;

                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].SlotIndex == slotIndex)
                    {
                        slot = slots[i];
                        break;
                    }
                }

                string value = slot == null
                    ? "Unassigned"
                    : $"SteamID={slot.SteamId}  •  " +
                      $"{(slot.IsConnected ? "Online" : "Offline")}" +
                      (slot.IsHost ? "  •  Host" : string.Empty);
                EditorGUILayout.LabelField($"Slot {slotIndex + 1}", value);
            }
        }
    }

    public static class SessionRosterEditorUtility
    {
        [MenuItem(
            "Tools/EXW/Multiplayer/Install Session Roster On NetworkRuntime",
            priority = 5)]
        public static void InstallOnNetworkRuntime()
        {
            NetworkManager networkManager =
                Object.FindFirstObjectByType<NetworkManager>(
                    FindObjectsInactive.Include);

            if (networkManager == null)
            {
                Debug.LogError(
                    "[SessionRoster] No NetworkManager was found in the loaded scene.");
                return;
            }

            SteamLobbyService lobbyService =
                Object.FindFirstObjectByType<SteamLobbyService>(
                    FindObjectsInactive.Include);

            if (lobbyService == null)
            {
                Debug.LogError(
                    "[SessionRoster] No SteamLobbyService was found. It may " +
                    "remain on SteamBootstrap, but the scene containing that " +
                    "persistent object must be open during installation.");
                return;
            }

            MultiplayerSessionRoster roster =
                networkManager.GetComponent<MultiplayerSessionRoster>();

            if (roster == null)
            {
                roster = Undo.AddComponent<MultiplayerSessionRoster>(
                    networkManager.gameObject);
                EditorUtility.SetDirty(networkManager.gameObject);
                Debug.Log(
                    "[SessionRoster] Installed on NetworkRuntime.",
                    roster);
            }

            Undo.RecordObject(roster, "Configure Session Roster References");
            SerializedObject serializedRoster = new SerializedObject(roster);
            serializedRoster.FindProperty("networkManager").objectReferenceValue =
                networkManager;
            serializedRoster.FindProperty("lobbyService").objectReferenceValue =
                lobbyService;
            serializedRoster.ApplyModifiedProperties();
            EditorUtility.SetDirty(roster);

            Selection.activeObject = roster;
            EditorGUIUtility.PingObject(roster);
            roster.ValidateSetup();
        }

        [MenuItem(
            "Tools/EXW/Multiplayer/Run Session Roster Self Test",
            priority = 21)]
        public static void RunSelfTest()
        {
            SessionRosterSelfTest.Run(Selection.activeObject);
        }
    }

    internal static class SessionRosterSelfTest
    {
        private const SessionSlotRetentionPolicy Keep =
            SessionSlotRetentionPolicy.UntilSessionEnds;

        public static void Run(Object context)
        {
            int passed = 0;
            int total = 0;

            SessionRosterState state = new SessionRosterState(4);
            SessionRosterAdmissionResult host = Admit(
                state, 11UL, 0UL, SteamLobbyState.Waiting, true, false, Keep, 0d);
            Check(host.Approved && host.SlotIndex == 0, "host owns slot 0");

            SessionRosterAdmissionResult client = Admit(
                state, 22UL, 1UL, SteamLobbyState.Waiting, true, false, Keep, 1d);
            Check(client.Approved && client.SlotIndex == 1, "client owns slot 1");
            Check(
                state.AssignedCount == 2 && state.ConnectedCount == 2,
                "waiting-room counts");

            bool disconnected = state.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                false,
                true,
                Keep,
                GracefulExitSlotPolicy.PreserveSlot,
                2d,
                out SessionRosterSlotSnapshot offline,
                out SessionSlotReleaseReason? releaseReason);
            Check(
                disconnected &&
                !releaseReason.HasValue &&
                offline != null &&
                offline.SlotIndex == 1 &&
                state.OfflineCount == 1,
                "unexpected disconnect preserves slot");

            SessionRosterAdmissionResult reconnect = Admit(
                state, 22UL, 91UL, SteamLobbyState.Playing, true, false, Keep, 3d);
            Check(
                reconnect.Approved &&
                reconnect.JoinKind == MultiplayerJoinKind.Reconnect &&
                reconnect.SlotIndex == 1 &&
                state.GetSlotIndexForClient(91UL) == 1,
                "new NGO ClientID reclaims stable slot");

            SessionRosterState waitingLeave = new SessionRosterState(2);
            Admit(
                waitingLeave, 10UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(
                waitingLeave, 20UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            waitingLeave.MarkDisconnected(
                1UL,
                SteamLobbyState.Waiting,
                false,
                true,
                Keep,
                GracefulExitSlotPolicy.PreserveSlot,
                1d,
                out _,
                out SessionSlotReleaseReason? waitingReason);
            Check(
                waitingReason == SessionSlotReleaseReason.WaitingRoomLeave &&
                waitingLeave.UnassignedCount == 1,
                "leaving before gameplay frees the slot");

            SessionRosterState policyDenied = new SessionRosterState(2);
            Admit(
                policyDenied, 10UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(
                policyDenied, 20UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            policyDenied.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                false,
                true,
                Keep,
                GracefulExitSlotPolicy.PreserveSlot,
                1d,
                out _,
                out _);
            SessionRosterAdmissionResult rejoinDenied = Admit(
                policyDenied, 20UL, 2UL, SteamLobbyState.Playing, false, true, Keep, 2d);
            SessionRosterAdmissionResult lateJoinDenied = Admit(
                policyDenied, 30UL, 3UL, SteamLobbyState.Playing, true, false, Keep, 2d);
            Check(
                rejoinDenied.Failure ==
                SessionRosterAdmissionFailure.RejoinDisabled,
                "rejoin policy denial");
            Check(
                lateJoinDenied.Failure ==
                SessionRosterAdmissionFailure.LateJoinDisabled,
                "late-join policy denial");

            SessionRosterState full = new SessionRosterState(4);
            Admit(full, 1UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(full, 2UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            full.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                false,
                true,
                Keep,
                GracefulExitSlotPolicy.PreserveSlot,
                1d,
                out _,
                out _);
            SessionRosterAdmissionResult lateA = Admit(
                full, 3UL, 2UL, SteamLobbyState.Playing, true, true, Keep, 2d);
            SessionRosterAdmissionResult lateB = Admit(
                full, 4UL, 3UL, SteamLobbyState.Playing, true, true, Keep, 2d);
            Check(
                lateA.JoinKind == MultiplayerJoinKind.LateJoin &&
                lateB.JoinKind == MultiplayerJoinKind.LateJoin &&
                full.AssignedCount == 4,
                "late join claims only never-assigned slots");

            SessionRosterAdmissionResult stranger = Admit(
                full, 5UL, 4UL, SteamLobbyState.Playing, true, true, Keep, 3d);
            Check(
                !stranger.Approved &&
                stranger.Failure ==
                SessionRosterAdmissionFailure.SessionRosterFull,
                "offline participant blocks slot theft");

            SessionRosterAdmissionResult original = Admit(
                full, 2UL, 88UL, SteamLobbyState.Playing, true, true, Keep, 3d);
            Check(
                original.Approved &&
                original.JoinKind == MultiplayerJoinKind.Reconnect &&
                original.SlotIndex == 1,
                "roster member bypasses full-session denial");

            full.MarkDisconnected(
                3UL,
                SteamLobbyState.Playing,
                false,
                true,
                Keep,
                GracefulExitSlotPolicy.PreserveSlot,
                4d,
                out _,
                out _);
            bool hostReleased = full.ReleaseBySteamId(4UL, out _);
            SessionRosterAdmissionResult replacement = Admit(
                full, 5UL, 5UL, SteamLobbyState.Playing, true, true, Keep, 5d);
            Check(
                hostReleased &&
                replacement.Approved &&
                replacement.SlotIndex == 3,
                "explicit offline release opens exact slot");

            SessionRosterState graceful = new SessionRosterState(2);
            Admit(graceful, 10UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(graceful, 20UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            graceful.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                true,
                true,
                Keep,
                GracefulExitSlotPolicy.ReleaseSlot,
                2d,
                out _,
                out SessionSlotReleaseReason? gracefulReason);
            Check(
                gracefulReason == SessionSlotReleaseReason.GracefulExitPolicy &&
                graceful.UnassignedCount == 1,
                "configurable graceful exit releases slot");

            SessionRosterState timed = new SessionRosterState(2);
            Admit(timed, 10UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(timed, 20UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            timed.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                false,
                true,
                SessionSlotRetentionPolicy.Timed,
                GracefulExitSlotPolicy.PreserveSlot,
                10d,
                out _,
                out _);
            int earlyPrune = timed.PruneExpired(
                SessionSlotRetentionPolicy.Timed, 5d, 14d, null);
            int latePrune = timed.PruneExpired(
                SessionSlotRetentionPolicy.Timed, 5d, 16d, null);
            Check(
                earlyPrune == 0 && latePrune == 1,
                "timed retention expires predictably");

            SessionRosterState immediate = new SessionRosterState(2);
            Admit(immediate, 10UL, 0UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            Admit(immediate, 20UL, 1UL, SteamLobbyState.Waiting, true, true, Keep, 0d);
            immediate.MarkDisconnected(
                1UL,
                SteamLobbyState.Playing,
                false,
                true,
                SessionSlotRetentionPolicy.ReleaseImmediately,
                GracefulExitSlotPolicy.PreserveSlot,
                1d,
                out _,
                out SessionSlotReleaseReason? immediateReason);
            Check(
                immediateReason == SessionSlotReleaseReason.RetentionDisabled &&
                immediate.UnassignedCount == 1,
                "release-immediately policy frees disconnected slot");

            string encoded = SessionRosterCodec.Encode(full.CreateSnapshot());
            bool decoded = SessionRosterCodec.TryDecode(
                encoded,
                4,
                out List<SessionRosterSlotSnapshot> decodedSlots);
            Check(
                decoded && decodedSlots.Count == full.AssignedCount,
                "lobby metadata codec round trip");
            Check(
                !SessionRosterCodec.TryDecode(
                    "1|0:1;0:2",
                    4,
                    out _),
                "malformed duplicate slot is rejected");

            if (passed == total)
            {
                Debug.Log(
                    $"[SessionRosterSelfTest] PASS {passed}/{total} validations.",
                    context);
            }

            void Check(bool condition, string label)
            {
                total++;

                if (condition)
                {
                    passed++;
                    return;
                }

                Debug.LogError(
                    $"[SessionRosterSelfTest] FAIL: {label}.",
                    context);
            }
        }

        private static SessionRosterAdmissionResult Admit(
            SessionRosterState state,
            ulong steamId,
            ulong clientId,
            SteamLobbyState lobbyState,
            bool allowRejoin,
            bool allowLateJoin,
            SessionSlotRetentionPolicy retention,
            double now)
        {
            return state.TryAdmit(
                steamId,
                clientId,
                lobbyState,
                allowRejoin,
                allowLateJoin,
                retention,
                5d,
                now);
        }
    }
}
#endif
