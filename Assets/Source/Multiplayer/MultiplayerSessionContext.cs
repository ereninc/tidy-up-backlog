using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Immutable, UI/gameplay-friendly view of the current multiplayer session.
    /// Consumers read this instead of coordinating SteamLobbyService, NGO and the
    /// flow controller themselves.
    /// </summary>
    public sealed class MultiplayerSessionSnapshot
    {
        public MultiplayerFlowState FlowState { get; }
        public MultiplayerSessionRole Role { get; }
        public MultiplayerSessionRole PendingRole { get; }
        public string StatusMessage { get; }

        public ulong LocalSteamId { get; }
        public bool HasLocalClientId { get; }
        public ulong LocalClientId { get; }

        public ulong LobbyId { get; }
        public string LobbyName { get; }
        public ulong LobbyOwnerSteamId { get; }
        public ulong HostSteamId { get; }
        public int LobbyMemberCount { get; }
        public int LobbyCapacity { get; }

        public string ProductId { get; }
        public int ProtocolVersion { get; }
        public string BuildId { get; }

        public bool IsSessionReady =>
            FlowState == MultiplayerFlowState.Connected &&
            Role != MultiplayerSessionRole.None;

        public bool IsHost =>
            IsSessionReady && Role == MultiplayerSessionRole.Host;

        public bool IsClient =>
            IsSessionReady && Role == MultiplayerSessionRole.Client;

        public bool IsInSteamLobby => LobbyId != 0;

        public MultiplayerSessionSnapshot(
            MultiplayerFlowState flowState,
            MultiplayerSessionRole role,
            MultiplayerSessionRole pendingRole,
            string statusMessage,
            ulong localSteamId,
            bool hasLocalClientId,
            ulong localClientId,
            ulong lobbyId,
            string lobbyName,
            ulong lobbyOwnerSteamId,
            ulong hostSteamId,
            int lobbyMemberCount,
            int lobbyCapacity,
            string productId,
            int protocolVersion,
            string buildId)
        {
            FlowState = flowState;
            Role = role;
            PendingRole = pendingRole;
            StatusMessage = statusMessage ?? string.Empty;
            LocalSteamId = localSteamId;
            HasLocalClientId = hasLocalClientId;
            LocalClientId = localClientId;
            LobbyId = lobbyId;
            LobbyName = lobbyName ?? string.Empty;
            LobbyOwnerSteamId = lobbyOwnerSteamId;
            HostSteamId = hostSteamId;
            LobbyMemberCount = lobbyMemberCount;
            LobbyCapacity = lobbyCapacity;
            ProductId = productId ?? string.Empty;
            ProtocolVersion = protocolVersion;
            BuildId = buildId ?? string.Empty;
        }

        internal bool HasSameValues(MultiplayerSessionSnapshot other)
        {
            return other != null &&
                   FlowState == other.FlowState &&
                   Role == other.Role &&
                   PendingRole == other.PendingRole &&
                   string.Equals(
                       StatusMessage,
                       other.StatusMessage,
                       StringComparison.Ordinal) &&
                   LocalSteamId == other.LocalSteamId &&
                   HasLocalClientId == other.HasLocalClientId &&
                   LocalClientId == other.LocalClientId &&
                   LobbyId == other.LobbyId &&
                   string.Equals(LobbyName, other.LobbyName, StringComparison.Ordinal) &&
                   LobbyOwnerSteamId == other.LobbyOwnerSteamId &&
                   HostSteamId == other.HostSteamId &&
                   LobbyMemberCount == other.LobbyMemberCount &&
                   LobbyCapacity == other.LobbyCapacity &&
                   string.Equals(ProductId, other.ProductId, StringComparison.Ordinal) &&
                   ProtocolVersion == other.ProtocolVersion &&
                   string.Equals(BuildId, other.BuildId, StringComparison.Ordinal);
        }

        public override string ToString()
        {
            return $"State={FlowState}, Role={Role}, LobbyID={LobbyId}, " +
                   $"Members={LobbyMemberCount}/{LobbyCapacity}, " +
                   $"LocalClientID={(HasLocalClientId ? LocalClientId.ToString() : "-")}, " +
                   $"LocalSteamID={LocalSteamId}";
        }
    }

    [DefaultExecutionOrder(-7000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Multiplayer Session Context")]
    public sealed class MultiplayerSessionContext : MonoBehaviour
    {
        public static MultiplayerSessionContext Instance { get; private set; }

        public event Action<MultiplayerSessionSnapshot> SnapshotChanged;
        public event Action<MultiplayerSessionSnapshot> SessionReady;
        public event Action<MultiplayerSessionSnapshot> SessionClosed;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamLobbyService lobbyService;
        [SerializeField] private MultiplayerFlowController flowController;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging;

        private MultiplayerSessionSnapshot _activeSessionSnapshot;
        private bool _eventsSubscribed;
        private bool _isDuplicate;

        public MultiplayerSessionSnapshot Current { get; private set; }

        public bool IsSessionReady => Current != null && Current.IsSessionReady;
        public bool IsHost => Current != null && Current.IsHost;
        public bool IsClient => Current != null && Current.IsClient;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _isDuplicate = true;
                enabled = false;
                Debug.LogWarning(
                    "[SessionContext] Duplicate component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_isDuplicate || !ValidateReferences())
            {
                enabled = false;
                return;
            }

            SubscribeToEvents();
            RebuildSnapshot();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (!_isDuplicate && Instance == this)
            {
                Instance = null;
            }
        }

        [ContextMenu("Debug/Log Session Snapshot")]
        public void LogCurrentSnapshot()
        {
            RebuildSnapshot();
            Debug.Log(
                Current != null
                    ? $"[SessionContext] {Current}"
                    : "[SessionContext] No snapshot.",
                this);
        }

        private void SubscribeToEvents()
        {
            if (_eventsSubscribed)
            {
                return;
            }

            flowController.StateChanged += HandleFlowStateChanged;
            flowController.StatusChanged += HandleStatusChanged;
            flowController.SessionStarted += HandleSessionStarted;
            flowController.SessionEnded += HandleSessionEnded;

            lobbyService.CurrentLobbyUpdated += HandleLobbyUpdated;
            lobbyService.LobbyMembersChanged += HandleLobbyMembersChanged;
            lobbyService.LobbyLeft += HandleLobbyLeft;

            networkManager.OnClientConnectedCallback += HandleNgoClientChanged;
            networkManager.OnClientDisconnectCallback += HandleNgoClientChanged;

            _eventsSubscribed = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!_eventsSubscribed)
            {
                return;
            }

            if (flowController != null)
            {
                flowController.StateChanged -= HandleFlowStateChanged;
                flowController.StatusChanged -= HandleStatusChanged;
                flowController.SessionStarted -= HandleSessionStarted;
                flowController.SessionEnded -= HandleSessionEnded;
            }

            if (lobbyService != null)
            {
                lobbyService.CurrentLobbyUpdated -= HandleLobbyUpdated;
                lobbyService.LobbyMembersChanged -= HandleLobbyMembersChanged;
                lobbyService.LobbyLeft -= HandleLobbyLeft;
            }

            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= HandleNgoClientChanged;
                networkManager.OnClientDisconnectCallback -= HandleNgoClientChanged;
            }

            _eventsSubscribed = false;
        }

        private void HandleFlowStateChanged(MultiplayerFlowState state)
        {
            RebuildSnapshot();
        }

        private void HandleStatusChanged(string status)
        {
            RebuildSnapshot();
        }

        private void HandleSessionStarted(MultiplayerSessionRole role)
        {
            RebuildSnapshot();
            _activeSessionSnapshot = Current;
            Log($"Session ready. {Current}");
            SessionReady?.Invoke(Current);
        }

        private void HandleSessionEnded(MultiplayerSessionRole role)
        {
            MultiplayerSessionSnapshot closedSnapshot =
                _activeSessionSnapshot ?? Current;

            _activeSessionSnapshot = null;
            RebuildSnapshot();
            Log($"Session closed. Previous={closedSnapshot}");
            SessionClosed?.Invoke(closedSnapshot);
        }

        private void HandleLobbyUpdated(SteamLobbySummary lobby)
        {
            RebuildSnapshot();
        }

        private void HandleLobbyMembersChanged(
            IReadOnlyList<SteamLobbyMember> members)
        {
            RebuildSnapshot();
        }

        private void HandleLobbyLeft(ulong lobbyId)
        {
            RebuildSnapshot();
        }

        private void HandleNgoClientChanged(ulong clientId)
        {
            RebuildSnapshot();
        }

        private void RebuildSnapshot()
        {
            if (networkManager == null ||
                lobbyService == null ||
                flowController == null)
            {
                return;
            }

            SteamLobbySummary lobby = lobbyService.CurrentLobby;
            bool hasLocalClientId = networkManager.IsConnectedClient;

            MultiplayerSessionSnapshot next = new MultiplayerSessionSnapshot(
                flowController.State,
                flowController.Role,
                flowController.PendingRole,
                flowController.StatusMessage,
                SteamBootstrap.LocalSteamId,
                hasLocalClientId,
                hasLocalClientId ? networkManager.LocalClientId : 0UL,
                lobby != null ? lobby.LobbyId : 0UL,
                lobby != null ? lobby.Name : string.Empty,
                lobby != null ? lobby.OwnerSteamId : 0UL,
                lobby != null ? lobby.HostSteamId : 0UL,
                lobby != null ? lobby.MemberCount : 0,
                lobby != null ? lobby.MemberLimit : lobbyService.MaximumPlayers,
                lobbyService.ProductId,
                lobbyService.ProtocolVersion,
                lobbyService.BuildId);

            if (Current != null && Current.HasSameValues(next))
            {
                return;
            }

            Current = next;
            SnapshotChanged?.Invoke(Current);
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (lobbyService == null)
            {
                lobbyService = GetComponent<SteamLobbyService>();
            }

            if (flowController == null)
            {
                flowController = GetComponent<MultiplayerFlowController>();
            }
        }

        private bool ValidateReferences()
        {
            ResolveReferences();

            if (networkManager == null ||
                lobbyService == null ||
                flowController == null)
            {
                Debug.LogError(
                    "[SessionContext] NetworkManager, SteamLobbyService and " +
                    "MultiplayerFlowController must be on the same GameObject or " +
                    "assigned in the Inspector.",
                    this);
                return false;
            }

            return true;
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[SessionContext] {message}", this);
            }
        }
    }
}
