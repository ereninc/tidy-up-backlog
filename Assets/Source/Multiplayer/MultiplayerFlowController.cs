using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.SinglePlayer;
using UnityEngine;

namespace EXW.Multiplayer
{
    public enum MultiplayerFlowState
    {
        Idle,
        CreatingLobby,
        SearchingLobbies,
        JoiningLobby,
        StartingHost,
        ConnectingClient,
        Connected,
        Disconnecting,
        Failed
    }

    public enum MultiplayerSessionRole
    {
        None,
        Host,
        Client
    }

    [DefaultExecutionOrder(-8000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Multiplayer Flow Controller")]
    public sealed class MultiplayerFlowController : MonoBehaviour
    {
        public static MultiplayerFlowController Instance { get; private set; }

        public event Action<MultiplayerFlowState> StateChanged;
        public event Action<string> StatusChanged;
        public event Action<string> FlowFailed;
        public event Action<string> OperationRejected;
        public event Action<MultiplayerSessionRole> SessionStarted;
        public event Action<MultiplayerSessionRole> SessionEnded;
        public event Action<SteamLobbySummary> CurrentLobbyUpdated;
        public event Action<IReadOnlyList<SteamLobbySummary>> LobbyListUpdated;
        public event Action<IReadOnlyList<SteamLobbyMember>> LobbyMembersChanged;
        public event Action<ulong> RemoteClientConnected;
        public event Action<ulong> RemoteClientDisconnected;
        public event Action<ulong, ulong> JoinRequestDeferred;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamNetworkingSocketsTransport steamTransport;
        [Tooltip("NGO's built-in offline transport. Add SinglePlayerTransport beside the Steam transport.")]
        [SerializeField] private SinglePlayerTransport singlePlayerTransport;
        [SerializeField] private SteamLobbyService lobbyService;

        private MultiplayerFlowState _state = MultiplayerFlowState.Idle;
        private MultiplayerSessionRole _role = MultiplayerSessionRole.None;
        private MultiplayerSessionRole _pendingRole = MultiplayerSessionRole.None;

        private bool _quickJoinRequested;
        private string _quickJoinFallbackLobbyName = string.Empty;

        private bool _cleanupInProgress;
        private bool _cleanupEndsInFailure;
        private bool _requestingLobbyLeave;
        private bool _sessionStarted;
        private bool _singlePlayerSession;
        private bool _isDuplicate;
        private bool _isQuitting;
        private bool _eventsSubscribed;

        private float _stateDeadline;
        private float _cleanupDeadline;
        private float _nextLobbyLeaveRetryTime;
        private string _cleanupMessage = string.Empty;

        public MultiplayerFlowState State => _state;
        public MultiplayerSessionRole Role => _role;
        public MultiplayerSessionRole PendingRole => _pendingRole;

        public bool IsIdle => _state == MultiplayerFlowState.Idle;
        public bool IsBusy =>
            _state != MultiplayerFlowState.Idle &&
            _state != MultiplayerFlowState.Connected &&
            _state != MultiplayerFlowState.Failed;

        public bool IsConnected => _state == MultiplayerFlowState.Connected;
        public bool IsHost => IsConnected && _role == MultiplayerSessionRole.Host;
        public bool IsClient => IsConnected && _role == MultiplayerSessionRole.Client;
        public bool IsSinglePlayer => _singlePlayerSession;
        public bool IsCoopSession => IsConnected && !_singlePlayerSession;

        public string StatusMessage { get; private set; } = "Idle";
        public string LastError { get; private set; } = string.Empty;

        public SteamLobbySummary CurrentLobby =>
            lobbyService != null ? lobbyService.CurrentLobby : null;

        public IReadOnlyList<SteamLobbySummary> AvailableLobbies =>
            lobbyService != null
                ? lobbyService.AvailableLobbies
                : Array.Empty<SteamLobbySummary>();

        public IReadOnlyList<SteamLobbyMember> CurrentLobbyMembers =>
            lobbyService != null
                ? lobbyService.CurrentMembers
                : Array.Empty<SteamLobbyMember>();

        public ulong CurrentLobbyId =>
            lobbyService != null ? lobbyService.CurrentLobbyId : 0UL;

        public ulong HostSteamId =>
            lobbyService != null ? lobbyService.CurrentHostSteamId : 0UL;

        public MultiplayerSettings Settings => MultiplayerSettings.Current;

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
                    "[MultiplayerFlow] Duplicate controller component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveReferences();

            if (!ValidateReferences())
            {
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!_isDuplicate && ValidateReferences())
            {
                SubscribeToEvents();
            }
        }

        private void Update()
        {
            if (_isQuitting)
            {
                return;
            }

            if (_state == MultiplayerFlowState.ConnectingClient &&
                Time.realtimeSinceStartup >= _stateDeadline)
            {
                FailAndCleanup(
                    $"Client could not connect to host SteamID={HostSteamId} " +
                    $"within {Settings.ClientConnectionTimeout:0} seconds.");
                return;
            }

            if (_state == MultiplayerFlowState.StartingHost &&
                Time.realtimeSinceStartup >= _stateDeadline)
            {
                FailAndCleanup(
                    $"NGO host did not start within " +
                    $"{Settings.ClientConnectionTimeout:0} seconds.");
                return;
            }

            if (_state == MultiplayerFlowState.Disconnecting)
            {
                ProgressCleanup();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void OnApplicationQuit()
        {
            PrepareForApplicationExit();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (!_isDuplicate && Instance == this)
            {
                Instance = null;
            }
        }

        public bool CreateLobby(
            string lobbyName,
            SteamLobbyVisibility visibility = SteamLobbyVisibility.Public,
            bool? allowLateJoinOverride = null)
        {
            if (!PrepareForNewFlow("create a lobby"))
            {
                return false;
            }

            _pendingRole = MultiplayerSessionRole.Host;
            _quickJoinRequested = false;
            SetState(
                MultiplayerFlowState.CreatingLobby,
                "Creating Steam lobby...");

            bool accepted = lobbyService.CreateLobby(
                lobbyName,
                visibility,
                0,
                allowLateJoinOverride);

            if (!accepted && _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup(GetLobbyServiceError("Steam rejected lobby creation."));
            }

            return accepted;
        }

        public bool RefreshLobbyList()
        {
            if (!PrepareForNewFlow("refresh the lobby list"))
            {
                return false;
            }

            _pendingRole = MultiplayerSessionRole.None;
            _quickJoinRequested = false;
            SetState(
                MultiplayerFlowState.SearchingLobbies,
                "Searching for compatible Steam lobbies...");

            bool accepted = lobbyService.RefreshLobbyList();

            if (!accepted && _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup(GetLobbyServiceError("Steam rejected the lobby search."));
            }

            return accepted;
        }

        public bool QuickJoin(string fallbackLobbyName = "")
        {
            if (!PrepareForNewFlow("quick join"))
            {
                return false;
            }

            _pendingRole = MultiplayerSessionRole.Client;
            _quickJoinRequested = true;
            _quickJoinFallbackLobbyName = fallbackLobbyName ?? string.Empty;

            SetState(
                MultiplayerFlowState.SearchingLobbies,
                "Quick Join: searching for a compatible lobby...");

            bool accepted = lobbyService.RefreshLobbyList();

            if (!accepted && _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup(GetLobbyServiceError("Steam rejected Quick Join."));
            }

            return accepted;
        }

        public bool JoinLobby(ulong lobbyId)
        {
            if (lobbyId == 0)
            {
                return RejectOperation("Cannot join a lobby with LobbyID 0.");
            }

            if (!PrepareForNewFlow("join a lobby"))
            {
                return false;
            }

            return BeginJoinLobby(lobbyId);
        }

        /// <summary>
        /// Attempts the exact lobby + session pair last observed by this Steam
        /// user. SessionId protects against accidentally joining an unrelated
        /// session if a stale LobbyID is ever reused.
        /// </summary>
        public bool RejoinLastSession()
        {
            if (!MultiplayerLastSessionStore.TryLoadCompatible(
                    Settings,
                    SteamBootstrap.LocalSteamId,
                    out MultiplayerLastSessionRecord record,
                    out string error))
            {
                return RejectOperation(error);
            }

            if (!PrepareForNewFlow("rejoin the previous session"))
            {
                return false;
            }

            return BeginJoinLobby(record.LobbyId, record.SessionId);
        }

        public bool JoinFirstListedLobby()
        {
            SteamLobbySummary lobby = SelectQuickJoinLobby(AvailableLobbies);

            if (lobby == null)
            {
                return RejectOperation(
                    "There is no joinable lobby. Refresh the lobby list first.");
            }

            return JoinLobby(lobby.LobbyId);
        }

        /// <summary>
        /// Starts the same NGO host-authoritative gameplay stack without Steam,
        /// sockets or a lobby. The active transport is restored to Steam after
        /// the normal Disconnect cleanup completes.
        /// </summary>
        public bool StartSinglePlayer()
        {
            if (!PrepareForNewFlow("start singleplayer"))
            {
                return false;
            }

            if (singlePlayerTransport == null)
            {
                return RejectOperation(
                    "Cannot start singleplayer; SinglePlayerTransport is missing.");
            }

            if (singlePlayerTransport == steamTransport)
            {
                return RejectOperation(
                    "Singleplayer and Steam transports must be different components.");
            }

            _singlePlayerSession = true;
            _pendingRole = MultiplayerSessionRole.Host;
            _quickJoinRequested = false;

            networkManager.NetworkConfig.NetworkTransport = singlePlayerTransport;
            networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();

            SetState(
                MultiplayerFlowState.StartingHost,
                "Starting offline singleplayer NGO host...");

            StartNgoHost();

            return _state == MultiplayerFlowState.StartingHost ||
                   _state == MultiplayerFlowState.Connected;
        }

        public bool Disconnect()
        {
            if (_state == MultiplayerFlowState.Idle &&
                !HasNetworkActivity() &&
                (lobbyService == null || !lobbyService.IsInLobby))
            {
                LogWarning("Disconnect ignored because there is no active session.");
                return false;
            }

            BeginCleanup(false, "Disconnected.");
            return true;
        }

        /// <summary>
        /// Quiesces the flow before an immediate application/editor shutdown.
        /// This deliberately does not run the normal frame-based cleanup state
        /// machine; MultiplayerShutdownCoordinator owns that synchronous path.
        /// </summary>
        public void PrepareForApplicationExit()
        {
            if (_isQuitting)
            {
                return;
            }

            _isQuitting = true;
            _quickJoinRequested = false;

            // NetworkManager.Shutdown raises stop/disconnect callbacks. They are
            // expected during application exit and must not turn into Failed.
            UnsubscribeFromEvents();

            Log("Application exit prepared; multiplayer flow callbacks quiesced.");
        }

        public bool OpenInviteOverlay()
        {
            if (!IsHost || lobbyService == null || !lobbyService.IsInLobby)
            {
                return RejectOperation(
                    "Only an active host can open the lobby invite overlay.");
            }

            return lobbyService.OpenInviteOverlay();
        }

        public void ClearFailure()
        {
            if (_state != MultiplayerFlowState.Failed || _cleanupInProgress)
            {
                return;
            }

            if (HasNetworkActivity() ||
                (lobbyService != null &&
                 (lobbyService.IsInLobby || lobbyService.IsBusy)))
            {
                PublishNonFatalError(
                    "Failure cannot be cleared while network or lobby state is still active. " +
                    "Run Disconnect again to retry cleanup.");
                return;
            }

            LastError = string.Empty;
            SetState(MultiplayerFlowState.Idle, "Idle");
        }

        [ContextMenu("Debug/Create Public Lobby + Start Host")]
        private void DebugCreatePublicLobby()
        {
            CreateLobby(string.Empty, SteamLobbyVisibility.Public);
        }

        [ContextMenu("Debug/Refresh Lobby List")]
        private void DebugRefreshLobbyList()
        {
            RefreshLobbyList();
        }

        [ContextMenu("Debug/Join First Listed Lobby + Start Client")]
        private void DebugJoinFirstListedLobby()
        {
            JoinFirstListedLobby();
        }

        [ContextMenu("Debug/Quick Join")]
        private void DebugQuickJoin()
        {
            QuickJoin();
        }

        [ContextMenu("Debug/Open Steam Invite Overlay")]
        private void DebugOpenInviteOverlay()
        {
            OpenInviteOverlay();
        }

        [ContextMenu("Debug/Disconnect")]
        private void DebugDisconnect()
        {
            Disconnect();
        }

        [ContextMenu("Debug/Clear Failure")]
        private void DebugClearFailure()
        {
            ClearFailure();
        }

        [ContextMenu("Debug/Log Flow Status")]
        private void DebugLogFlowStatus()
        {
            Debug.Log(
                $"[MultiplayerFlow] State={_state} | Role={_role} | " +
                $"PendingRole={_pendingRole} | LobbyID={CurrentLobbyId} | " +
                $"HostSteamID={HostSteamId} | NGO Listening=" +
                $"{(networkManager != null && networkManager.IsListening)} | " +
                $"TransportRunning=" +
                $"{(steamTransport != null && steamTransport.IsRunning)} | " +
                $"Status={StatusMessage}",
                this);
        }

        private bool BeginJoinLobby(
            ulong lobbyId,
            string expectedSessionId = "")
        {
            _pendingRole = MultiplayerSessionRole.Client;
            _quickJoinRequested = false;

            SetState(
                MultiplayerFlowState.JoiningLobby,
                $"Joining Steam lobby {lobbyId}...");

            bool accepted = lobbyService.JoinLobby(
                lobbyId,
                expectedSessionId);

            if (!accepted && _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup(GetLobbyServiceError("Steam rejected JoinLobby."));
            }

            return accepted;
        }

        private void HandleLobbyCreated(SteamLobbySummary lobby)
        {
            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (_state != MultiplayerFlowState.CreatingLobby ||
                _pendingRole != MultiplayerSessionRole.Host)
            {
                FailAndCleanup(
                    "A Steam lobby was created outside MultiplayerFlowController.");
                return;
            }

            if (lobby == null || lobby.HostSteamId == 0)
            {
                FailAndCleanup("Created lobby does not contain a valid Host SteamID.");
                return;
            }

            if (lobby.HostSteamId != SteamBootstrap.LocalSteamId)
            {
                FailAndCleanup(
                    $"Created lobby host mismatch. LobbyHost={lobby.HostSteamId}, " +
                    $"LocalSteamID={SteamBootstrap.LocalSteamId}");
                return;
            }

            StartNgoHost();
        }

        private void HandleLobbyJoined(SteamLobbySummary lobby)
        {
            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (_state != MultiplayerFlowState.JoiningLobby ||
                _pendingRole != MultiplayerSessionRole.Client)
            {
                FailAndCleanup(
                    "A Steam lobby was joined outside MultiplayerFlowController.");
                return;
            }

            if (lobby == null || lobby.HostSteamId == 0)
            {
                FailAndCleanup("Joined lobby does not contain a valid Host SteamID.");
                return;
            }

            if (lobby.HostSteamId == SteamBootstrap.LocalSteamId)
            {
                FailAndCleanup("Cannot start a client connection to the local Steam user.");
                return;
            }

            StartNgoClient(lobby.HostSteamId);
        }

        private void HandleLobbyLeft(ulong lobbyId)
        {
            Log($"Steam lobby left. LobbyID={lobbyId}");

            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (_state == MultiplayerFlowState.Idle ||
                _state == MultiplayerFlowState.Failed)
            {
                return;
            }

            FailAndCleanup("The local user unexpectedly left the Steam lobby.");
        }

        private void HandleLobbyListUpdated(
            IReadOnlyList<SteamLobbySummary> lobbies)
        {
            LobbyListUpdated?.Invoke(lobbies);

            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (_state != MultiplayerFlowState.SearchingLobbies)
            {
                return;
            }

            if (!_quickJoinRequested)
            {
                _pendingRole = MultiplayerSessionRole.None;
                SetState(
                    MultiplayerFlowState.Idle,
                    $"Lobby search complete. Found {lobbies.Count} lobby/lobbies.");
                return;
            }

            _quickJoinRequested = false;
            SteamLobbySummary selectedLobby = SelectQuickJoinLobby(lobbies);

            if (selectedLobby != null)
            {
                Log($"Quick Join selected lobby {selectedLobby.LobbyId}.");
                BeginJoinLobby(selectedLobby.LobbyId);
                return;
            }

            if (!Settings.QuickJoinCreatesLobbyWhenNoneFound)
            {
                _pendingRole = MultiplayerSessionRole.None;
                SetState(
                    MultiplayerFlowState.Idle,
                    "Quick Join found no available lobby.");
                return;
            }

            _pendingRole = MultiplayerSessionRole.Host;
            SetState(
                MultiplayerFlowState.CreatingLobby,
                "Quick Join found no lobby; creating a public lobby...");

            bool accepted = lobbyService.CreateLobby(
                _quickJoinFallbackLobbyName,
                SteamLobbyVisibility.Public);

            if (!accepted && _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup(
                    GetLobbyServiceError(
                        "Steam rejected the Quick Join fallback lobby."));
            }
        }

        private void HandleCurrentLobbyUpdated(SteamLobbySummary lobby)
        {
            CurrentLobbyUpdated?.Invoke(lobby);
        }

        private void HandleLobbyMembersChanged(
            IReadOnlyList<SteamLobbyMember> members)
        {
            LobbyMembersChanged?.Invoke(members);
        }

        private void HandleLobbyOperationFailed(string message)
        {
            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            switch (_state)
            {
                case MultiplayerFlowState.CreatingLobby:
                case MultiplayerFlowState.SearchingLobbies:
                case MultiplayerFlowState.JoiningLobby:
                    FailAndCleanup(message);
                    break;

                default:
                    PublishNonFatalError(message);
                    break;
            }
        }

        private void HandleSteamJoinRequested(ulong lobbyId, ulong friendSteamId)
        {
            Log(
                $"Steam join request received. LobbyID={lobbyId}, " +
                $"FriendSteamID={friendSteamId}");

            bool canJoinImmediately =
                _state == MultiplayerFlowState.Idle ||
                _state == MultiplayerFlowState.Failed;

            if (!Settings.AutomaticallyAcceptSteamInvites || !canJoinImmediately)
            {
                JoinRequestDeferred?.Invoke(lobbyId, friendSteamId);
                return;
            }

            JoinLobby(lobbyId);
        }

        private void StartNgoHost()
        {
            if (HasNetworkActivity())
            {
                FailAndCleanup(
                    "Cannot start host because NetworkManager or transport is already running.");
                return;
            }

            _stateDeadline =
                Time.realtimeSinceStartup + Settings.ClientConnectionTimeout;

            SetState(
                MultiplayerFlowState.StartingHost,
                _singlePlayerSession
                    ? "Starting offline singleplayer NGO host..."
                    : "Steam lobby ready; starting NGO host...");

            bool started;

            try
            {
                started = networkManager.StartHost();
            }
            catch (Exception exception)
            {
                FailAndCleanup(
                    $"NetworkManager.StartHost threw {exception.GetType().Name}: " +
                    exception.Message);
                return;
            }

            if (!started &&
                !_cleanupInProgress &&
                _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup("NetworkManager.StartHost returned false.");
            }
        }

        private void StartNgoClient(ulong hostSteamId)
        {
            if (HasNetworkActivity())
            {
                FailAndCleanup(
                    "Cannot start client because NetworkManager or transport is already running.");
                return;
            }

            steamTransport.SetTargetSteamId(hostSteamId);

            if (steamTransport.TargetSteamId != hostSteamId)
            {
                FailAndCleanup(
                    $"Steam transport rejected Host SteamID={hostSteamId}.");
                return;
            }

            _stateDeadline =
                Time.realtimeSinceStartup + Settings.ClientConnectionTimeout;

            SetState(
                MultiplayerFlowState.ConnectingClient,
                $"Connecting to host SteamID={hostSteamId}...");

            bool started;

            try
            {
                started = networkManager.StartClient();
            }
            catch (Exception exception)
            {
                FailAndCleanup(
                    $"NetworkManager.StartClient threw {exception.GetType().Name}: " +
                    exception.Message);
                return;
            }

            if (!started &&
                !_cleanupInProgress &&
                _state != MultiplayerFlowState.Failed)
            {
                FailAndCleanup("NetworkManager.StartClient returned false.");
            }
        }

        private void HandleServerStarted()
        {
            if (_cleanupInProgress)
            {
                return;
            }

            if (_state != MultiplayerFlowState.StartingHost ||
                _pendingRole != MultiplayerSessionRole.Host)
            {
                FailAndCleanup(
                    "NGO host started outside the expected multiplayer flow.");
                return;
            }

            SetState(
                MultiplayerFlowState.StartingHost,
                "NGO server is listening; waiting for the local host client...");
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_cleanupInProgress)
            {
                return;
            }

            if (networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId)
                {
                    if (_state == MultiplayerFlowState.StartingHost &&
                        _pendingRole == MultiplayerSessionRole.Host)
                    {
                        MarkSessionConnected(MultiplayerSessionRole.Host);
                    }

                    return;
                }

                if (_role == MultiplayerSessionRole.Host)
                {
                    Log($"Remote NGO client connected. ClientID={clientId}");
                    RemoteClientConnected?.Invoke(clientId);
                }

                return;
            }

            if (_state != MultiplayerFlowState.ConnectingClient ||
                _pendingRole != MultiplayerSessionRole.Client)
            {
                FailAndCleanup(
                    $"NGO client connected outside the expected flow. ClientID={clientId}");
                return;
            }

            if (clientId != networkManager.LocalClientId)
            {
                return;
            }

            MarkSessionConnected(MultiplayerSessionRole.Client);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (_cleanupInProgress)
            {
                return;
            }

            if (_role == MultiplayerSessionRole.Host ||
                _pendingRole == MultiplayerSessionRole.Host)
            {
                if (networkManager.IsServer &&
                    clientId != networkManager.LocalClientId)
                {
                    Log($"Remote NGO client disconnected. ClientID={clientId}");
                    RemoteClientDisconnected?.Invoke(clientId);
                }

                return;
            }

            if (_role == MultiplayerSessionRole.Client ||
                _pendingRole == MultiplayerSessionRole.Client)
            {
                FailAndCleanup(ResolveClientDisconnectMessage());
            }
        }

        private string ResolveClientDisconnectMessage()
        {
            string reason = networkManager != null
                ? networkManager.DisconnectReason
                : string.Empty;

            if (SteamConnectionApproval.TryParseRejection(
                    reason,
                    out SteamConnectionRejectReason rejectReason,
                    out string message))
            {
                return string.IsNullOrWhiteSpace(message)
                    ? $"Connection rejected: {rejectReason}."
                    : $"Connection rejected ({rejectReason}): {message}";
            }

            return string.IsNullOrWhiteSpace(reason)
                ? "Disconnected from the host."
                : reason;
        }

        private void HandleTransportFailure()
        {
            if (_cleanupInProgress || _isQuitting)
            {
                return;
            }

            FailAndCleanup("The NGO transport reported a fatal failure.");
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (!wasHost &&
                (_role == MultiplayerSessionRole.Client ||
                 _pendingRole == MultiplayerSessionRole.Client))
            {
                FailAndCleanup("The NGO client stopped unexpectedly.");
            }
        }

        private void HandleServerStopped(bool wasHost)
        {
            if (_cleanupInProgress)
            {
                ProgressCleanup();
                return;
            }

            if (wasHost &&
                (_role == MultiplayerSessionRole.Host ||
                 _pendingRole == MultiplayerSessionRole.Host))
            {
                FailAndCleanup("The NGO host stopped unexpectedly.");
            }
        }

        private void MarkSessionConnected(MultiplayerSessionRole role)
        {
            _role = role;
            _pendingRole = MultiplayerSessionRole.None;
            _stateDeadline = 0f;
            LastError = string.Empty;

            string status = role == MultiplayerSessionRole.Host
                ? _singlePlayerSession
                    ? "Singleplayer NGO host is ready."
                    : $"Hosting lobby {CurrentLobbyId}."
                : $"Connected to lobby {CurrentLobbyId}.";

            SetState(MultiplayerFlowState.Connected, status);

            if (_sessionStarted)
            {
                return;
            }

            _sessionStarted = true;
            SessionStarted?.Invoke(role);
        }

        private void FailAndCleanup(string message)
        {
            if (_cleanupInProgress)
            {
                return;
            }

            string resolvedMessage = string.IsNullOrWhiteSpace(message)
                ? "Unknown multiplayer flow failure."
                : message;

            LastError = resolvedMessage;
            Debug.LogError($"[MultiplayerFlow] {resolvedMessage}", this);
            BeginCleanup(true, resolvedMessage);
        }

        private void BeginCleanup(bool endsInFailure, string message)
        {
            if (_cleanupInProgress)
            {
                if (endsInFailure && !_cleanupEndsInFailure)
                {
                    _cleanupEndsInFailure = true;
                    _cleanupMessage = message;
                    LastError = message;
                }

                return;
            }

            _cleanupInProgress = true;
            _cleanupEndsInFailure = endsInFailure;
            _cleanupMessage = message ?? string.Empty;
            _cleanupDeadline =
                Time.realtimeSinceStartup + Settings.ShutdownTimeout;
            _nextLobbyLeaveRetryTime = 0f;

            _quickJoinRequested = false;
            SetState(
                MultiplayerFlowState.Disconnecting,
                endsInFailure
                    ? "Cleaning up failed multiplayer session..."
                    : "Disconnecting...");

            RequestNetworkShutdown();
            ProgressCleanup();
        }

        private void RequestNetworkShutdown()
        {
            if (networkManager != null &&
                (networkManager.IsServer || networkManager.IsClient))
            {
                networkManager.Shutdown(true);
                return;
            }

            if (steamTransport != null && steamTransport.IsRunning)
            {
                steamTransport.Shutdown();
            }
        }

        private void ProgressCleanup()
        {
            if (!_cleanupInProgress)
            {
                return;
            }

            if (networkManager != null &&
                (networkManager.IsServer || networkManager.IsClient) &&
                !networkManager.ShutdownInProgress)
            {
                networkManager.Shutdown(true);
            }

            if (lobbyService != null &&
                lobbyService.IsInLobby &&
                !lobbyService.IsBusy &&
                !_requestingLobbyLeave &&
                Time.realtimeSinceStartup >= _nextLobbyLeaveRetryTime)
            {
                _requestingLobbyLeave = true;

                try
                {
                    lobbyService.LeaveLobby();
                }
                finally
                {
                    _requestingLobbyLeave = false;

                    if (_cleanupInProgress)
                    {
                        _nextLobbyLeaveRetryTime =
                            Time.realtimeSinceStartup + 1f;
                    }
                }
            }

            // LeaveLobby raises LobbyLeft synchronously. That callback can finish
            // cleanup, so do not continue and complete the same cleanup twice.
            if (!_cleanupInProgress)
            {
                return;
            }

            bool networkStopped = !HasNetworkActivity();
            bool lobbyStopped =
                lobbyService == null ||
                (!lobbyService.IsInLobby && !lobbyService.IsBusy);

            if (networkStopped && lobbyStopped)
            {
                CompleteCleanup();
                return;
            }

            if (Time.realtimeSinceStartup < _cleanupDeadline)
            {
                return;
            }

            if (steamTransport != null && steamTransport.IsRunning)
            {
                steamTransport.Shutdown();
            }

            string timeoutDetails =
                $"Cleanup timed out. NetworkStopped={networkStopped}, " +
                $"LobbyStopped={lobbyStopped}.";

            Debug.LogWarning($"[MultiplayerFlow] {timeoutDetails}", this);

            if (!_cleanupEndsInFailure)
            {
                _cleanupEndsInFailure = true;
                _cleanupMessage = timeoutDetails;
                LastError = timeoutDetails;
            }

            CompleteCleanup();
        }

        private void CompleteCleanup()
        {
            if (!_cleanupInProgress)
            {
                return;
            }

            MultiplayerSessionRole endedRole = _role;
            bool endedSinglePlayerSession = _singlePlayerSession;
            bool shouldRaiseSessionEnded = _sessionStarted;
            bool endsInFailure = _cleanupEndsInFailure;
            string message = _cleanupMessage;

            if (steamTransport != null && !steamTransport.IsRunning)
            {
                steamTransport.SetTargetSteamId(0);
            }

            _role = MultiplayerSessionRole.None;
            _pendingRole = MultiplayerSessionRole.None;
            _stateDeadline = 0f;
            _cleanupDeadline = 0f;
            _nextLobbyLeaveRetryTime = 0f;
            _cleanupInProgress = false;
            _cleanupEndsInFailure = false;
            _requestingLobbyLeave = false;
            _cleanupMessage = string.Empty;
            _quickJoinRequested = false;
            _quickJoinFallbackLobbyName = string.Empty;
            _sessionStarted = false;

            if (shouldRaiseSessionEnded)
            {
                SessionEnded?.Invoke(endedRole);
            }

            RestoreSteamTransportAfterSinglePlayer(endedSinglePlayerSession);
            _singlePlayerSession = false;

            if (endsInFailure)
            {
                LastError = string.IsNullOrWhiteSpace(message)
                    ? "Unknown multiplayer flow failure."
                    : message;

                SetState(
                    MultiplayerFlowState.Failed,
                    $"Multiplayer failed: {LastError}");
                FlowFailed?.Invoke(LastError);
                return;
            }

            LastError = string.Empty;
            SetState(MultiplayerFlowState.Idle, "Idle");
        }

        private bool PrepareForNewFlow(string operationName)
        {
            if (!Application.isPlaying)
            {
                return RejectOperation(
                    $"Cannot {operationName} outside Play Mode.");
            }

            if (!ValidateReferences())
            {
                return RejectOperation(
                    $"Cannot {operationName}; runtime references are invalid.");
            }

            if (_state == MultiplayerFlowState.Failed && !_cleanupInProgress)
            {
                if (HasNetworkActivity() ||
                    lobbyService.IsInLobby ||
                    lobbyService.IsBusy)
                {
                    return RejectOperation(
                        $"Cannot {operationName}; failed session cleanup is incomplete. " +
                        "Run Disconnect to retry cleanup.");
                }

                LastError = string.Empty;
                SetState(MultiplayerFlowState.Idle, "Idle");
            }

            if (_state != MultiplayerFlowState.Idle)
            {
                return RejectOperation(
                    $"Cannot {operationName} while flow state is {_state}.");
            }

            if (HasNetworkActivity())
            {
                return RejectOperation(
                    $"Cannot {operationName}; NetworkManager or transport is already active.");
            }

            if (lobbyService.IsInLobby || lobbyService.IsBusy)
            {
                return RejectOperation(
                    $"Cannot {operationName}; Steam lobby service is not idle.");
            }

            LastError = string.Empty;
            return true;
        }

        private bool HasNetworkActivity()
        {
            bool ngoActive = networkManager != null &&
                             (networkManager.IsListening ||
                              networkManager.IsServer ||
                              networkManager.IsClient ||
                              networkManager.ShutdownInProgress);

            bool transportActive =
                steamTransport != null && steamTransport.IsRunning;

            return ngoActive || transportActive;
        }

        private bool ValidateReferences()
        {
            ResolveReferences();

            if (networkManager == null)
            {
                Debug.LogError(
                    "[MultiplayerFlow] NetworkManager reference is missing.",
                    this);
                return false;
            }

            if (steamTransport == null)
            {
                Debug.LogError(
                    "[MultiplayerFlow] SteamNetworkingSocketsTransport reference is missing.",
                    this);
                return false;
            }

            if (lobbyService == null)
            {
                Debug.LogError(
                    "[MultiplayerFlow] SteamLobbyService reference is missing.",
                    this);
                return false;
            }

            if (networkManager.NetworkConfig == null ||
                networkManager.NetworkConfig.NetworkTransport != steamTransport)
            {
                Debug.LogError(
                    "[MultiplayerFlow] NetworkManager must use the referenced " +
                    "SteamNetworkingSocketsTransport as its active transport.",
                    this);
                return false;
            }

            return true;
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (steamTransport == null)
            {
                steamTransport = GetComponent<SteamNetworkingSocketsTransport>();
            }

            if (singlePlayerTransport == null)
            {
                singlePlayerTransport = GetComponent<SinglePlayerTransport>();
            }

            if (lobbyService == null)
            {
                lobbyService = GetComponent<SteamLobbyService>();
            }
        }

        private void RestoreSteamTransportAfterSinglePlayer(bool wasSinglePlayer)
        {
            if (!wasSinglePlayer || networkManager == null ||
                networkManager.NetworkConfig == null || steamTransport == null)
            {
                return;
            }

            networkManager.NetworkConfig.NetworkTransport = steamTransport;
            networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            steamTransport.SetTargetSteamId(0);
            Log("Singleplayer cleanup restored Steam transport.");
        }

        private void SubscribeToEvents()
        {
            if (_eventsSubscribed)
            {
                return;
            }

            lobbyService.LobbyCreated += HandleLobbyCreated;
            lobbyService.LobbyJoined += HandleLobbyJoined;
            lobbyService.LobbyLeft += HandleLobbyLeft;
            lobbyService.CurrentLobbyUpdated += HandleCurrentLobbyUpdated;
            lobbyService.LobbyListUpdated += HandleLobbyListUpdated;
            lobbyService.LobbyMembersChanged += HandleLobbyMembersChanged;
            lobbyService.LobbyJoinRequested += HandleSteamJoinRequested;
            lobbyService.OperationFailed += HandleLobbyOperationFailed;

            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            networkManager.OnTransportFailure += HandleTransportFailure;
            networkManager.OnClientStopped += HandleClientStopped;
            networkManager.OnServerStopped += HandleServerStopped;

            _eventsSubscribed = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!_eventsSubscribed)
            {
                return;
            }

            if (lobbyService != null)
            {
                lobbyService.LobbyCreated -= HandleLobbyCreated;
                lobbyService.LobbyJoined -= HandleLobbyJoined;
                lobbyService.LobbyLeft -= HandleLobbyLeft;
                lobbyService.CurrentLobbyUpdated -= HandleCurrentLobbyUpdated;
                lobbyService.LobbyListUpdated -= HandleLobbyListUpdated;
                lobbyService.LobbyMembersChanged -= HandleLobbyMembersChanged;
                lobbyService.LobbyJoinRequested -= HandleSteamJoinRequested;
                lobbyService.OperationFailed -= HandleLobbyOperationFailed;
            }

            if (networkManager != null)
            {
                networkManager.OnServerStarted -= HandleServerStarted;
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
                networkManager.OnTransportFailure -= HandleTransportFailure;
                networkManager.OnClientStopped -= HandleClientStopped;
                networkManager.OnServerStopped -= HandleServerStopped;
            }

            _eventsSubscribed = false;
        }

        private SteamLobbySummary SelectQuickJoinLobby(
            IReadOnlyList<SteamLobbySummary> lobbies)
        {
            if (lobbies == null)
            {
                return null;
            }

            SteamLobbySummary reconnectLobby = null;
            SteamLobbySummary waitingLobby = null;
            SteamLobbySummary lateJoinLobby = null;

            for (int i = 0; i < lobbies.Count; i++)
            {
                SteamLobbySummary candidate = lobbies[i];

                if (candidate == null ||
                    candidate.LobbyId == 0 ||
                    candidate.HostSteamId == 0 ||
                    candidate.HostSteamId == SteamBootstrap.LocalSteamId ||
                    !candidate.HasOpenSlot ||
                    !candidate.IsJoinableForLocalPlayer)
                {
                    continue;
                }

                if (candidate.LocalJoinKind == MultiplayerJoinKind.Reconnect)
                {
                    if (reconnectLobby == null ||
                        candidate.MemberCount > reconnectLobby.MemberCount)
                    {
                        reconnectLobby = candidate;
                    }

                    continue;
                }

                if (candidate.LocalJoinKind == MultiplayerJoinKind.WaitingRoom)
                {
                    if (waitingLobby == null ||
                        candidate.MemberCount > waitingLobby.MemberCount)
                    {
                        waitingLobby = candidate;
                    }

                    continue;
                }

                if (candidate.LocalJoinKind == MultiplayerJoinKind.LateJoin &&
                    (lateJoinLobby == null ||
                     candidate.MemberCount > lateJoinLobby.MemberCount))
                {
                    lateJoinLobby = candidate;
                }
            }

            if (Settings.PreferReconnectInQuickJoin && reconnectLobby != null)
            {
                return reconnectLobby;
            }

            return waitingLobby ?? reconnectLobby ?? lateJoinLobby;
        }

        private string GetLobbyServiceError(string fallback)
        {
            return lobbyService != null &&
                   !string.IsNullOrWhiteSpace(lobbyService.LastError)
                ? lobbyService.LastError
                : fallback;
        }

        private bool RejectOperation(string message)
        {
            PublishNonFatalError(message);
            return false;
        }

        private void PublishNonFatalError(string message)
        {
            LastError = string.IsNullOrWhiteSpace(message)
                ? "Multiplayer operation was rejected."
                : message;

            Debug.LogWarning($"[MultiplayerFlow] {LastError}", this);
            OperationRejected?.Invoke(LastError);
        }

        private void SetState(MultiplayerFlowState state, string status)
        {
            bool stateWasChanged = _state != state;
            bool statusWasChanged =
                !string.Equals(StatusMessage, status, StringComparison.Ordinal);

            _state = state;
            StatusMessage = status ?? string.Empty;

            if (stateWasChanged)
            {
                Log($"State -> {_state}");
                StateChanged?.Invoke(_state);
            }

            if (statusWasChanged)
            {
                Log(StatusMessage);
                StatusChanged?.Invoke(StatusMessage);
            }
        }

        private void Log(string message)
        {
            if (Settings.VerboseLogging)
            {
                Debug.Log($"[MultiplayerFlow] {message}", this);
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[MultiplayerFlow] {message}", this);
        }
    }
}
