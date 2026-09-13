using System;
using System.Collections.Generic;
using System.Text;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Odin-powered Play Mode console. It does not own any multiplayer state;
    /// every action is delegated to MultiplayerFlowController.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Multiplayer Debug Panel")]
    public sealed class MultiplayerDebugPanel : MonoBehaviour
    {
        [Title("MULTIPLAYER DEBUG PANEL", "Steam Lobby + NGO runtime controls")]
        [InfoBox(
            "Use this panel only in Play Mode. Start and join through the flow " +
            "buttons below; direct NetworkManager StartHost/StartClient calls bypass " +
            "the Steam lobby and connection payload.")]
        [BoxGroup("Session Actions")]
        [LabelText("Debug Lobby Name")]
        [SerializeField] private string debugLobbyName = "CO-OP Test Lobby";

        [BoxGroup("Session Actions")]
        [EnumToggleButtons]
        [LabelText("Visibility")]
        [SerializeField] private SteamLobbyVisibility lobbyVisibility =
            SteamLobbyVisibility.Public;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private MultiplayerFlowController flowController;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private MultiplayerSessionContext sessionContext;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private SteamLobbyService lobbyService;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private SteamConnectionApproval connectionApproval;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private NetworkPlayerRegistry playerRegistry;

        [FoldoutGroup("References")]
        [Required]
        [SerializeField] private NetworkManager networkManager;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Runtime")]
        private string RuntimeState => Application.isPlaying
            ? "PLAY MODE"
            : "EDIT MODE - actions are disabled";

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Setup Health")]
        private string SetupHealth
        {
            get
            {
                List<string> issues = new List<string>();

                if (flowController == null || sessionContext == null ||
                    lobbyService == null || connectionApproval == null ||
                    playerRegistry == null || networkManager == null)
                {
                    issues.Add("missing runtime reference");
                }

                if (networkManager != null &&
                    networkManager.NetworkConfig.PlayerPrefab == null)
                {
                    issues.Add("Player Prefab is not assigned");
                }

                if (Application.isPlaying && NetworkPlayerSpawnPoint.Count == 0)
                {
                    issues.Add("no active scene spawn point");
                }

                return issues.Count == 0
                    ? $"READY | PlayerPrefab assigned | " +
                      $"SpawnPoints={NetworkPlayerSpawnPoint.Count}"
                    : "CHECK: " + string.Join(" | ", issues);
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Steam")]
        private string SteamState
        {
            get
            {
                if (!SteamBootstrap.IsSteamAvailable)
                {
                    return $"Unavailable | {SteamBootstrap.InitializationError}";
                }

                return
                    $"{SteamBootstrap.LocalPersonaName} | " +
                    $"SteamID={SteamBootstrap.LocalSteamId} | " +
                    $"LoggedOn={SteamBootstrap.IsSteamLoggedOn} | " +
                    $"SDR={SteamBootstrap.RelayAvailability}";
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Flow")]
        private string FlowState
        {
            get
            {
                if (flowController == null)
                {
                    return "Missing MultiplayerFlowController";
                }

                return
                    $"State={flowController.State} | Role={flowController.Role} | " +
                    $"Pending={flowController.PendingRole}";
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Status")]
        private string FlowStatus => flowController != null
            ? flowController.StatusMessage
            : "-";

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Lobby")]
        private string LobbyState
        {
            get
            {
                SteamLobbySummary lobby = lobbyService != null
                    ? lobbyService.CurrentLobby
                    : null;

                if (lobby == null)
                {
                    int availableCount = lobbyService != null
                        ? lobbyService.AvailableLobbies.Count
                        : 0;

                    return $"No active lobby | Listed={availableCount}";
                }

                return
                    $"{lobby.Name} | {lobby.MemberCount}/{lobby.MemberLimit} | " +
                    $"LobbyID={lobby.LobbyId} | HostSteamID={lobby.HostSteamId} | " +
                    $"State={lobby.State}";
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("NGO")]
        private string NgoState
        {
            get
            {
                if (networkManager == null)
                {
                    return "Missing NetworkManager";
                }

                string localClientId = networkManager.IsConnectedClient
                    ? networkManager.LocalClientId.ToString()
                    : "-";

                int connectedCount = networkManager.IsServer
                    ? networkManager.ConnectedClientsIds.Count
                    : playerRegistry != null
                        ? playerRegistry.Count
                        : 0;

                return
                    $"Listening={networkManager.IsListening} | " +
                    $"Host={networkManager.IsHost} | Client={networkManager.IsClient} | " +
                    $"LocalClientID={localClientId} | KnownPlayers={connectedCount}";
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Session")]
        [LabelText("Approval Payload")]
        private string ApprovalState
        {
            get
            {
                if (connectionApproval == null)
                {
                    return "Missing SteamConnectionApproval";
                }

                SteamConnectionPayload payload = connectionApproval.LocalPayload;

                if (payload == null)
                {
                    return string.IsNullOrWhiteSpace(connectionApproval.LastPayloadError)
                        ? "Not prepared"
                        : connectionApproval.LastPayloadError;
                }

                int byteCount = networkManager != null &&
                                networkManager.NetworkConfig.ConnectionData != null
                    ? networkManager.NetworkConfig.ConnectionData.Length
                    : 0;

                return
                    $"{byteCount} bytes | Schema={payload.SchemaVersion} | " +
                    $"Protocol={payload.ProtocolVersion} | Build={payload.BuildId}";
            }
        }

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Players")]
        [HideLabel]
        [MultiLineProperty(7)]
        private string PlayerRoster => BuildPlayerRoster();

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Available Lobbies")]
        [HideLabel]
        [MultiLineProperty(5)]
        private string AvailableLobbyList => BuildLobbyList();

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Last Debug Action")]
        [HideLabel]
        private string LastAction { get; set; } = "No action yet.";

        private bool CanStartFlow =>
            Application.isPlaying &&
            flowController != null &&
            flowController.IsIdle &&
            (networkManager == null || !networkManager.IsListening) &&
            (lobbyService == null || !lobbyService.IsInLobby);

        private bool IsPlayMode => Application.isPlaying;

        private bool CanJoinFirst =>
            CanStartFlow &&
            lobbyService != null &&
            lobbyService.AvailableLobbies.Count > 0;

        private bool CanDisconnect =>
            Application.isPlaying &&
            flowController != null &&
            (!flowController.IsIdle ||
             (lobbyService != null && lobbyService.IsInLobby) ||
             (networkManager != null && networkManager.IsListening));

        private bool CanClearFailure =>
            Application.isPlaying &&
            flowController != null &&
            flowController.State == MultiplayerFlowState.Failed;

        private bool CanOpenInviteOverlay =>
            Application.isPlaying &&
            flowController != null &&
            flowController.IsHost;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        [HorizontalGroup("Session Actions/Primary")]
        [Button("CREATE HOST", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.9f, 0.45f)]
        [EnableIf(nameof(CanStartFlow))]
        private void CreateHost()
        {
            Execute(
                "Create Host",
                () => flowController.CreateLobby(
                    debugLobbyName,
                    lobbyVisibility));
        }

        [HorizontalGroup("Session Actions/Primary")]
        [Button("QUICK JOIN", ButtonSizes.Large)]
        [GUIColor(0.3f, 0.75f, 1f)]
        [EnableIf(nameof(CanStartFlow))]
        private void QuickJoin()
        {
            Execute(
                "Quick Join",
                () => flowController.QuickJoin(debugLobbyName));
        }

        [HorizontalGroup("Session Actions/Browser")]
        [Button("REFRESH LOBBIES", ButtonSizes.Medium)]
        [GUIColor(1f, 0.8f, 0.25f)]
        [EnableIf(nameof(CanStartFlow))]
        private void RefreshLobbies()
        {
            Execute("Refresh Lobbies", flowController.RefreshLobbyList);
        }

        [HorizontalGroup("Session Actions/Browser")]
        [Button("JOIN FIRST", ButtonSizes.Medium)]
        [GUIColor(0.4f, 0.65f, 1f)]
        [EnableIf(nameof(CanJoinFirst))]
        private void JoinFirstLobby()
        {
            Execute("Join First Lobby", flowController.JoinFirstListedLobby);
        }

        [HorizontalGroup("Session Actions/Management")]
        [Button("DISCONNECT", ButtonSizes.Medium)]
        [GUIColor(1f, 0.35f, 0.35f)]
        [EnableIf(nameof(CanDisconnect))]
        private void Disconnect()
        {
            Execute("Disconnect", flowController.Disconnect);
        }

        [HorizontalGroup("Session Actions/Management")]
        [Button("CLEAR FAILURE", ButtonSizes.Medium)]
        [GUIColor(1f, 0.6f, 0.25f)]
        [EnableIf(nameof(CanClearFailure))]
        private void ClearFailure()
        {
            flowController.ClearFailure();
            LastAction = "Clear Failure requested.";
        }

        [HorizontalGroup("Session Actions/Utilities")]
        [Button("LOG EVERYTHING", ButtonSizes.Medium)]
        [EnableIf(nameof(IsPlayMode))]
        private void LogEverything()
        {
            sessionContext?.LogCurrentSnapshot();
            playerRegistry?.LogRegistry();
            connectionApproval?.RefreshLocalPayload();

            Debug.Log(
                $"[MultiplayerDebugPanel] {FlowState} | {LobbyState} | " +
                $"{NgoState}",
                this);

            LastAction = "Session, registry and payload written to Console.";
        }

        [HorizontalGroup("Session Actions/Utilities")]
        [Button("INVITE OVERLAY (BUILD)", ButtonSizes.Medium)]
        [EnableIf(nameof(CanOpenInviteOverlay))]
        private void OpenInviteOverlay()
        {
            Execute("Open Invite Overlay", flowController.OpenInviteOverlay);
        }

        [Button("AUTO ASSIGN REFERENCES")]
        [FoldoutGroup("References")]
        private void ResolveReferences()
        {
            if (flowController == null)
            {
                flowController = GetComponent<MultiplayerFlowController>();
            }

            if (sessionContext == null)
            {
                sessionContext = GetComponent<MultiplayerSessionContext>();
            }

            if (lobbyService == null)
            {
                lobbyService = GetComponent<SteamLobbyService>();
            }

            if (connectionApproval == null)
            {
                connectionApproval = GetComponent<SteamConnectionApproval>();
            }

            if (playerRegistry == null)
            {
                playerRegistry = GetComponent<NetworkPlayerRegistry>();
            }

            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }
        }

        private void Execute(string actionName, Func<bool> action)
        {
            if (!Application.isPlaying)
            {
                LastAction = $"{actionName} ignored: enter Play Mode first.";
                return;
            }

            if (action == null)
            {
                LastAction = $"{actionName} failed: action is null.";
                return;
            }

            try
            {
                bool accepted = action.Invoke();
                LastAction = accepted
                    ? $"{actionName} accepted at {DateTime.Now:HH:mm:ss}."
                    : $"{actionName} was rejected. Check Flow Status and Console.";
            }
            catch (Exception exception)
            {
                LastAction =
                    $"{actionName} threw {exception.GetType().Name}: " +
                    exception.Message;
                Debug.LogException(exception, this);
            }
        }

        private string BuildPlayerRoster()
        {
            if (playerRegistry == null || playerRegistry.Count == 0)
            {
                return "No registered NGO players.";
            }

            StringBuilder builder = new StringBuilder();
            IReadOnlyList<NetworkPlayerRecord> players = playerRegistry.Players;

            builder.Append("Count=")
                .Append(players.Count)
                .Append(" | Authoritative=")
                .Append(playerRegistry.IsAuthoritativeRoster)
                .AppendLine();

            for (int i = 0; i < players.Count; i++)
            {
                NetworkPlayerRecord player = players[i];
                builder.Append('[')
                    .Append(i)
                    .Append("] ClientID=")
                    .Append(player.ClientId)
                    .Append(" | ")
                    .Append(player.PersonaName)
                    .Append(" | SteamID=")
                    .Append(player.SteamId)
                    .Append(" | Local=")
                    .Append(player.IsLocalPlayer)
                    .Append(" | Host=")
                    .Append(player.IsHost);

                if (i + 1 < players.Count)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }

        private string BuildLobbyList()
        {
            if (lobbyService == null || lobbyService.AvailableLobbies.Count == 0)
            {
                return "No compatible lobby in the latest search result.";
            }

            StringBuilder builder = new StringBuilder();
            IReadOnlyList<SteamLobbySummary> lobbies =
                lobbyService.AvailableLobbies;

            for (int i = 0; i < lobbies.Count; i++)
            {
                SteamLobbySummary lobby = lobbies[i];
                builder.Append('[')
                    .Append(i)
                    .Append("] ")
                    .Append(lobby.Name)
                    .Append(" | ")
                    .Append(lobby.MemberCount)
                    .Append('/')
                    .Append(lobby.MemberLimit)
                    .Append(" | LobbyID=")
                    .Append(lobby.LobbyId);

                if (i + 1 < lobbies.Count)
                {
                    builder.AppendLine();
                }
            }

            return builder.ToString();
        }
    }
}