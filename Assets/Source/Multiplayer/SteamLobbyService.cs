using System;
using System.Collections.Generic;
using System.Globalization;
using Steamworks;
using UnityEngine;

namespace EXW.Multiplayer
{
    public enum SteamLobbyVisibility
    {
        Private,
        FriendsOnly,
        Public
    }

    public enum SteamLobbyState
    {
        Unknown,
        Waiting,
        Playing,
        Closing
    }

    public enum SteamLobbyOperation
    {
        None,
        CreatingLobby,
        RequestingLobbyList,
        JoiningLobby,
        LeavingLobby
    }

    public static class SteamLobbyDataKeys
    {
        public const string ProductId = "product_id";
        public const string ProtocolVersion = "protocol";
        public const string BuildId = "build_id";
        public const string LobbyName = "lobby_name";
        public const string LobbyState = "lobby_state";
        public const string HostSteamId = "host_steam_id";
        public const string AllowReconnect = "allow_reconnect";
        public const string AllowLateJoin = "allow_late_join";
        public const string ReconnectSteamIds = "reconnect_steam_ids";
        public const string SessionId = "session_id";
        public const string SessionRoster = "session_roster_v1";
    }

    public sealed class SteamLobbySummary
    {
        public ulong LobbyId { get; }
        public string Name { get; }
        public ulong OwnerSteamId { get; }
        public ulong HostSteamId { get; }
        public int MemberCount { get; }
        public int MemberLimit { get; }
        public SteamLobbyState State { get; }
        public string BuildId { get; }
        public bool AllowsReconnect { get; }
        public bool AllowsLateJoin { get; }
        public bool IsLocalPlayerReconnectEligible { get; }
        public int ReservedReconnectSeats { get; }
        public string SessionId { get; }
        public bool HasSessionRosterMetadata { get; }
        public IReadOnlyList<SessionRosterSlotSnapshot> SessionSlots { get; }
        public int AssignedSessionSlots => SessionSlots.Count;
        public int UnassignedSessionSlots => MemberLimit <= 0
            ? int.MaxValue
            : Mathf.Max(0, MemberLimit - AssignedSessionSlots);
        public bool IsLocalPlayerSessionMember =>
            FindSessionSlot(SteamBootstrap.LocalSteamId) != null;
        public MultiplayerJoinKind LocalJoinKind
        {
            get
            {
                if (State == SteamLobbyState.Playing &&
                    IsLocalPlayerSessionMember &&
                    !IsLocalPlayerReconnectEligible)
                {
                    return MultiplayerJoinKind.Denied;
                }

                return MultiplayerJoinPolicy.Classify(
                    State,
                    AllowsLateJoin,
                    IsLocalPlayerReconnectEligible,
                    HasUnassignedSessionSlot);
            }
        }

        public bool HasUnassignedSessionSlot =>
            !HasSessionRosterMetadata ||
            MemberLimit <= 0 ||
            AssignedSessionSlots < MemberLimit;

        public bool HasSteamLobbyOpenSlot =>
            MemberLimit <= 0 || MemberCount < MemberLimit;

        public bool HasOpenSlot
        {
            get
            {
                if (MemberLimit <= 0)
                {
                    return true;
                }

                if (State == SteamLobbyState.Playing &&
                    HasSessionRosterMetadata)
                {
                    return IsLocalPlayerSessionMember ||
                           (AllowsLateJoin && HasUnassignedSessionSlot);
                }

                int reservationsBlockingLocalPlayer =
                    IsLocalPlayerReconnectEligible
                        ? Mathf.Max(0, ReservedReconnectSeats - 1)
                        : ReservedReconnectSeats;

                return MemberCount + reservationsBlockingLocalPlayer <
                       MemberLimit;
            }
        }

        public bool IsJoinableForLocalPlayer =>
            LocalJoinKind != MultiplayerJoinKind.Denied;

        public SteamLobbySummary(
            ulong lobbyId,
            string name,
            ulong ownerSteamId,
            ulong hostSteamId,
            int memberCount,
            int memberLimit,
            SteamLobbyState state,
            string buildId,
            bool allowsReconnect,
            bool allowsLateJoin,
            bool isLocalPlayerReconnectEligible,
            int reservedReconnectSeats,
            string sessionId,
            bool hasSessionRosterMetadata,
            IReadOnlyList<SessionRosterSlotSnapshot> sessionSlots)
        {
            LobbyId = lobbyId;
            Name = name ?? string.Empty;
            OwnerSteamId = ownerSteamId;
            HostSteamId = hostSteamId;
            MemberCount = memberCount;
            MemberLimit = memberLimit;
            State = state;
            BuildId = buildId ?? string.Empty;
            AllowsReconnect = allowsReconnect;
            AllowsLateJoin = allowsLateJoin;
            IsLocalPlayerReconnectEligible =
                isLocalPlayerReconnectEligible;
            ReservedReconnectSeats = Mathf.Max(0, reservedReconnectSeats);
            SessionId = sessionId ?? string.Empty;
            HasSessionRosterMetadata = hasSessionRosterMetadata;
            SessionSlots = sessionSlots ??
                           Array.Empty<SessionRosterSlotSnapshot>();
        }

        public override string ToString()
        {
            string joinPolicy = LocalJoinKind.ToString();

            return $"{Name} | {MemberCount}/{MemberLimit} | " +
                   $"LobbyID={LobbyId} | HostSteamID={HostSteamId} | " +
                   $"State={State} | Roster={AssignedSessionSlots}/" +
                   $"{MemberLimit} | JoinPolicy={joinPolicy}";
        }

        public SessionRosterSlotSnapshot FindSessionSlot(ulong steamId)
        {
            if (steamId == 0)
            {
                return null;
            }

            for (int i = 0; i < SessionSlots.Count; i++)
            {
                if (SessionSlots[i].SteamId == steamId)
                {
                    return SessionSlots[i];
                }
            }

            return null;
        }
    }

    public sealed class SteamLobbyMember
    {
        public ulong SteamId { get; }
        public string PersonaName { get; }
        public bool IsOwner { get; }
        public bool IsLocalPlayer { get; }

        public SteamLobbyMember(
            ulong steamId,
            string personaName,
            bool isOwner,
            bool isLocalPlayer)
        {
            SteamId = steamId;
            PersonaName = personaName ?? string.Empty;
            IsOwner = isOwner;
            IsLocalPlayer = isLocalPlayer;
        }
    }

    [DefaultExecutionOrder(-9000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Steam Lobby Service")]
    public sealed class SteamLobbyService : MonoBehaviour
    {
        private const int MaximumLobbyNameLength = 64;
        private const string WaitingStateValue = "waiting";
        private const string PlayingStateValue = "playing";
        private const string ClosingStateValue = "closing";

        public static SteamLobbyService Instance { get; private set; }

        public event Action ServiceReady;
        public event Action<SteamLobbyOperation> OperationChanged;
        public event Action<SteamLobbySummary> LobbyCreated;
        public event Action<SteamLobbySummary> LobbyJoined;
        public event Action<SteamLobbySummary> CurrentLobbyUpdated;
        public event Action<ulong> LobbyLeft;
        public event Action<IReadOnlyList<SteamLobbySummary>> LobbyListUpdated;
        public event Action<IReadOnlyList<SteamLobbyMember>> LobbyMembersChanged;
        public event Action<ulong, ulong> LobbyJoinRequested;
        public event Action<string> OperationFailed;

        private readonly List<SteamLobbySummary> _availableLobbies =
            new List<SteamLobbySummary>();

        private readonly List<SteamLobbyMember> _currentMembers =
            new List<SteamLobbyMember>();

        private readonly HashSet<ulong> _reconnectEligibleSteamIds =
            new HashSet<ulong>();

        private CallResult<LobbyCreated_t> _lobbyCreatedCallResult;
        private CallResult<LobbyMatchList_t> _lobbyListCallResult;
        private CallResult<LobbyEnter_t> _lobbyEnterCallResult;

        private Callback<LobbyChatUpdate_t> _lobbyChatUpdateCallback;
        private Callback<LobbyDataUpdate_t> _lobbyDataUpdateCallback;
        private Callback<LobbyKicked_t> _lobbyKickedCallback;
        private Callback<GameLobbyJoinRequested_t> _gameLobbyJoinRequestedCallback;

        private SteamLobbySummary _currentLobby;
        private SteamLobbyOperation _operation;
        private ulong _currentLobbyId;
        private string _pendingLobbyName = string.Empty;
        private string _pendingSessionId = string.Empty;
        private string _pendingExpectedSessionId = string.Empty;
        private bool _pendingAllowLateJoin;
        private float _nextInitializationAttemptTime;
        private bool _callbacksRegistered;
        private bool _isDuplicate;
        private bool _isShuttingDownForApplicationExit;

        public bool IsReady =>
            _callbacksRegistered && SteamBootstrap.IsSteamAvailable;

        public bool IsBusy => _operation != SteamLobbyOperation.None;
        public bool IsInLobby => _currentLobbyId != 0;
        public bool IsLobbyOwner =>
            _currentLobby != null &&
            _currentLobby.OwnerSteamId == SteamBootstrap.LocalSteamId;

        public ulong CurrentLobbyId => _currentLobbyId;
        public string CurrentSessionId =>
            _currentLobby != null ? _currentLobby.SessionId : string.Empty;
        public ulong CurrentHostSteamId =>
            _currentLobby != null ? _currentLobby.HostSteamId : 0UL;

        public SteamLobbyOperation CurrentOperation => _operation;
        public SteamLobbySummary CurrentLobby => _currentLobby;
        public IReadOnlyList<SteamLobbySummary> AvailableLobbies => _availableLobbies;
        public IReadOnlyList<SteamLobbyMember> CurrentMembers => _currentMembers;
        public string LastError { get; private set; } = string.Empty;

        public MultiplayerSettings Settings => MultiplayerSettings.Current;
        public string ProductId => Settings.ProductId;
        public int ProtocolVersion => Settings.ProtocolVersion;
        public string BuildId => Settings.BuildId;
        public int MaximumPlayers => Settings.MaximumPlayers;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                _isDuplicate = true;
                enabled = false;
                Debug.LogWarning(
                    "[SteamLobby] Duplicate SteamLobbyService component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            TryRegisterSteamCallbacks();
        }

        private void Update()
        {
            if (_isShuttingDownForApplicationExit ||
                _callbacksRegistered ||
                Time.unscaledTime < _nextInitializationAttemptTime)
            {
                return;
            }

            _nextInitializationAttemptTime = Time.unscaledTime + 0.5f;
            TryRegisterSteamCallbacks();
        }

        private void OnDestroy()
        {
            if (_isDuplicate || Instance != this)
            {
                return;
            }

            ShutdownImmediatelyForApplicationExit();
            Instance = null;
        }

        public bool CreateLobby(
            string lobbyName,
            SteamLobbyVisibility visibility,
            int memberLimit = 0,
            bool? allowLateJoinOverride = null)
        {
            if (!TryBeginOperation(SteamLobbyOperation.CreatingLobby))
            {
                return false;
            }

            if (IsInLobby)
            {
                return AbortOperation(
                    "Cannot create a lobby while already inside another lobby.");
            }

            _pendingLobbyName = NormalizeLobbyName(lobbyName);
            _pendingSessionId = Guid.NewGuid().ToString("N");
            _pendingAllowLateJoin =
                allowLateJoinOverride ?? Settings.DefaultAllowLateJoin;
            _reconnectEligibleSteamIds.Clear();
            int resolvedMemberLimit = memberLimit <= 0
                ? MaximumPlayers
                : Mathf.Clamp(memberLimit, 2, 4);

            try
            {
                SteamAPICall_t apiCall = SteamMatchmaking.CreateLobby(
                    ConvertLobbyType(visibility),
                    resolvedMemberLimit);

                if (apiCall == SteamAPICall_t.Invalid)
                {
                    return AbortOperation("Steam returned an invalid CreateLobby call.");
                }

                _lobbyCreatedCallResult.Set(apiCall);
                Log(
                    $"Creating {visibility} lobby. " +
                    $"Name={_pendingLobbyName}, Limit={resolvedMemberLimit}, " +
                    $"LateJoin={_pendingAllowLateJoin}");
                return true;
            }
            catch (Exception exception)
            {
                return AbortOperation(
                    $"CreateLobby threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        public bool CreateLobby(
            string lobbyName,
            SteamLobbyVisibility visibility,
            bool allowLateJoin)
        {
            return CreateLobby(lobbyName, visibility, 0, allowLateJoin);
        }

        public bool RefreshLobbyList()
        {
            if (!TryBeginOperation(SteamLobbyOperation.RequestingLobbyList))
            {
                return false;
            }

            try
            {
                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyDataKeys.ProductId,
                    ProductId,
                    ELobbyComparison.k_ELobbyComparisonEqual);

                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyDataKeys.ProtocolVersion,
                    ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                    ELobbyComparison.k_ELobbyComparisonEqual);

                SteamMatchmaking.AddRequestLobbyListStringFilter(
                    SteamLobbyDataKeys.BuildId,
                    BuildId,
                    ELobbyComparison.k_ELobbyComparisonEqual);

                SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
                SteamMatchmaking.AddRequestLobbyListDistanceFilter(
                    Settings.SearchDistance);
                SteamMatchmaking.AddRequestLobbyListResultCountFilter(
                    Settings.MaximumSearchResults);

                SteamAPICall_t apiCall = SteamMatchmaking.RequestLobbyList();

                if (apiCall == SteamAPICall_t.Invalid)
                {
                    return AbortOperation(
                        "Steam returned an invalid RequestLobbyList call.");
                }

                _lobbyListCallResult.Set(apiCall);
                Log(
                    $"Requesting lobby list. Product={ProductId}, " +
                    $"Protocol={ProtocolVersion}, Build={BuildId}. " +
                    "Waiting and locally joinable running sessions will be shown.");
                return true;
            }
            catch (Exception exception)
            {
                return AbortOperation(
                    $"RequestLobbyList threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        public bool JoinLobby(
            ulong lobbyId,
            string expectedSessionId = "")
        {
            if (lobbyId == 0)
            {
                return Fail("Cannot join lobby because LobbyID is 0.");
            }

            if (!TryBeginOperation(SteamLobbyOperation.JoiningLobby))
            {
                return false;
            }

            if (IsInLobby)
            {
                return AbortOperation(
                    "Cannot join a lobby while already inside another lobby.");
            }

            _pendingExpectedSessionId = string.IsNullOrWhiteSpace(
                expectedSessionId)
                ? string.Empty
                : expectedSessionId.Trim();

            try
            {
                SteamAPICall_t apiCall = SteamMatchmaking.JoinLobby(
                    new CSteamID(lobbyId));

                if (apiCall == SteamAPICall_t.Invalid)
                {
                    return AbortOperation("Steam returned an invalid JoinLobby call.");
                }

                _lobbyEnterCallResult.Set(apiCall);
                Log($"Joining lobby. LobbyID={lobbyId}");
                return true;
            }
            catch (Exception exception)
            {
                return AbortOperation(
                    $"JoinLobby threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        public bool LeaveLobby()
        {
            if (!IsInLobby)
            {
                LogWarning("LeaveLobby ignored because there is no current lobby.");
                return false;
            }

            if (!TryBeginOperation(SteamLobbyOperation.LeavingLobby))
            {
                return false;
            }

            ulong lobbyId = _currentLobbyId;

            try
            {
                SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
                ClearCurrentLobby(true);
                SetOperation(SteamLobbyOperation.None);
                Log($"Left lobby. LobbyID={lobbyId}");
                return true;
            }
            catch (Exception exception)
            {
                return AbortOperation(
                    $"LeaveLobby threw {exception.GetType().Name}: {exception.Message}");
            }
        }

        /// <summary>
        /// Synchronously leaves the known lobby and disposes every lobby callback
        /// while the Steam API is still alive. Unlike LeaveLobby(), this path is
        /// allowed to cancel an in-flight lobby operation because no later frame
        /// will be available during application/editor shutdown.
        /// </summary>
        public void ShutdownImmediatelyForApplicationExit()
        {
            if (_isShuttingDownForApplicationExit)
            {
                return;
            }

            _isShuttingDownForApplicationExit = true;
            ulong lobbyId = _currentLobbyId;

            try
            {
                if (lobbyId != 0 && SteamBootstrap.IsSteamAvailable)
                {
                    CSteamID steamLobbyId = new CSteamID(lobbyId);

                    // If the host leaves while clients are still members, Steam
                    // can transfer lobby ownership. Hide the lobby first so the
                    // transferred lobby is never advertised with a dead host ID.
                    if (SteamMatchmaking.GetLobbyOwner(steamLobbyId).m_SteamID ==
                        SteamBootstrap.LocalSteamId)
                    {
                        SteamMatchmaking.SetLobbyJoinable(steamLobbyId, false);
                        SteamMatchmaking.SetLobbyData(
                            steamLobbyId,
                            SteamLobbyDataKeys.LobbyState,
                            ClosingStateValue);
                    }

                    SteamMatchmaking.LeaveLobby(steamLobbyId);
                    Log($"Application exit left lobby. LobbyID={lobbyId}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[SteamLobby] Application-exit lobby cleanup failed: " +
                    $"{exception.GetType().Name}: {exception.Message}",
                    this);
            }
            finally
            {
                // Do not raise normal runtime events while the object graph is
                // being torn down.
                _operation = SteamLobbyOperation.None;
                _pendingLobbyName = string.Empty;
                _pendingSessionId = string.Empty;
                _pendingExpectedSessionId = string.Empty;
                _availableLobbies.Clear();
                ClearCurrentLobby(false);
                DisposeSteamCallbacks();
            }
        }

        public bool SetLobbyState(SteamLobbyState state, bool joinable)
        {
            if (!EnsureLobbyOwner("change lobby state"))
            {
                return false;
            }

            if (state != SteamLobbyState.Waiting && state != SteamLobbyState.Playing)
            {
                return Fail("Lobby state must be Waiting or Playing.");
            }

            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            string stateValue = ConvertLobbyState(state);

            bool dataChanged = SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.LobbyState,
                stateValue);

            bool joinableChanged = SteamMatchmaking.SetLobbyJoinable(
                lobbyId,
                joinable);

            if (!dataChanged || !joinableChanged)
            {
                return Fail(
                    $"Steam could not set lobby state to {state} " +
                    $"with Joinable={joinable}.");
            }

            RefreshCurrentLobbySnapshot(true);
            Log($"Lobby state changed. State={state}, Joinable={joinable}");
            return true;
        }

        /// <summary>
        /// Transitions the owned lobby into gameplay and derives Steam's
        /// joinable flag from the reconnect and per-lobby late-join policies.
        /// </summary>
        public bool EnterGameplayState()
        {
            if (!EnsureLobbyOwner("start gameplay"))
            {
                return false;
            }

            RefreshCurrentLobbySnapshot(false);
            bool allowsLateJoin = _currentLobby != null
                ? _currentLobby.AllowsLateJoin
                : _pendingAllowLateJoin;
            bool keepJoinable =
                Settings.ShouldKeepLobbyJoinableDuringGameplay(
                    allowsLateJoin);

            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            bool policyWritten = SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.AllowReconnect,
                FormatBool(Settings.SupportsSessionRejoin));

            if (!Settings.SupportsSessionRejoin)
            {
                _reconnectEligibleSteamIds.Clear();
                policyWritten &= WriteReconnectEligibility();
            }

            if (!policyWritten)
            {
                return Fail(
                    "Steam could not publish the running-session reconnect policy.");
            }

            return SetLobbyState(SteamLobbyState.Playing, keepJoinable);
        }

        /// <summary>
        /// Updates the per-lobby override. A future Create Lobby checkbox can
        /// call CreateLobby(..., allowLateJoin), while a lobby-room toggle may
        /// call this method before or during gameplay.
        /// </summary>
        public bool SetLobbyLateJoinAllowed(bool allowed)
        {
            if (!EnsureLobbyOwner("change the late-join policy"))
            {
                return false;
            }

            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            bool success = SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.AllowLateJoin,
                FormatBool(allowed));

            RefreshCurrentLobbySnapshot(false);
            bool isPlaying = _currentLobby != null &&
                             _currentLobby.State == SteamLobbyState.Playing;

            if (isPlaying)
            {
                success &= SteamMatchmaking.SetLobbyJoinable(
                    lobbyId,
                    Settings.ShouldKeepLobbyJoinableDuringGameplay(allowed));
            }

            if (!success)
            {
                return Fail("Steam could not change the late-join policy.");
            }

            _pendingAllowLateJoin = allowed;
            RefreshCurrentLobbySnapshot(true);
            Log($"Late-join policy changed. Allowed={allowed}");
            return true;
        }

        /// <summary>
        /// Publishes the host-authoritative session-slot ownership snapshot.
        /// reconnectSteamIds mirrors the same owners into the v4 compatibility
        /// key so an older browser can still identify its reconnect eligibility.
        /// </summary>
        public bool SetSessionRosterData(
            string encodedRoster,
            string reconnectSteamIds)
        {
            if (!EnsureLobbyOwner("publish the session roster"))
            {
                return false;
            }

            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            bool success = SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.SessionRoster,
                encodedRoster ?? string.Empty);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ReconnectSteamIds,
                reconnectSteamIds ?? string.Empty);

            if (!success)
            {
                return Fail("Steam could not publish the session roster.");
            }

            _reconnectEligibleSteamIds.Clear();

            foreach (ulong steamId in ParseSteamIds(reconnectSteamIds))
            {
                _reconnectEligibleSteamIds.Add(steamId);
            }

            RefreshCurrentLobbySnapshot(true);
            return true;
        }

        /// <summary>
        /// Publishes the small reconnect allow-list as lobby metadata. Only the
        /// host writes this list; browsers use it to reveal a running lobby to a
        /// returning Steam user without exposing that lobby to unrelated users.
        /// </summary>
        public bool SetReconnectEligibleSteamIds(IEnumerable<ulong> steamIds)
        {
            if (!EnsureLobbyOwner("change reconnect eligibility"))
            {
                return false;
            }

            _reconnectEligibleSteamIds.Clear();

            if (Settings.SupportsSessionRejoin && steamIds != null)
            {
                foreach (ulong steamId in steamIds)
                {
                    if (steamId != 0)
                    {
                        _reconnectEligibleSteamIds.Add(steamId);
                    }
                }
            }

            bool success = WriteReconnectEligibility();

            if (!success)
            {
                return Fail("Steam could not publish reconnect eligibility.");
            }

            RefreshCurrentLobbySnapshot(true);
            return true;
        }

        public bool AddReconnectEligibleSteamId(ulong steamId)
        {
            if (steamId == 0 || !Settings.SupportsSessionRejoin)
            {
                return false;
            }

            if (!EnsureLobbyOwner("add reconnect eligibility"))
            {
                return false;
            }

            if (!_reconnectEligibleSteamIds.Add(steamId))
            {
                return true;
            }

            bool success = WriteReconnectEligibility();

            if (success)
            {
                RefreshCurrentLobbySnapshot(true);
                Log($"Reconnect eligibility added. SteamID={steamId}");
                return true;
            }

            _reconnectEligibleSteamIds.Remove(steamId);
            return Fail("Steam could not add reconnect eligibility.");
        }

        public bool RemoveReconnectEligibleSteamId(ulong steamId)
        {
            if (!EnsureLobbyOwner("remove reconnect eligibility"))
            {
                return false;
            }

            if (!_reconnectEligibleSteamIds.Remove(steamId))
            {
                return true;
            }

            bool success = WriteReconnectEligibility();

            if (success)
            {
                RefreshCurrentLobbySnapshot(true);
                Log($"Reconnect eligibility removed. SteamID={steamId}");
                return true;
            }

            _reconnectEligibleSteamIds.Add(steamId);
            return Fail("Steam could not remove reconnect eligibility.");
        }

        public bool SetLobbyName(string lobbyName)
        {
            if (!EnsureLobbyOwner("rename the lobby"))
            {
                return false;
            }

            string normalizedName = NormalizeLobbyName(lobbyName);
            bool success = SteamMatchmaking.SetLobbyData(
                new CSteamID(_currentLobbyId),
                SteamLobbyDataKeys.LobbyName,
                normalizedName);

            if (!success)
            {
                return Fail("Steam could not change the lobby name.");
            }

            RefreshCurrentLobbySnapshot(true);
            Log($"Lobby renamed to {normalizedName}.");
            return true;
        }

        public bool OpenInviteOverlay()
        {
            if (!EnsureSteamAvailable() || !IsInLobby)
            {
                return Fail(
                    "Cannot open the invite overlay without an active Steam lobby.");
            }

            SteamFriends.ActivateGameOverlayInviteDialog(
                new CSteamID(_currentLobbyId));

            Log($"Opened Steam invite overlay. LobbyID={_currentLobbyId}");
            return true;
        }

        public bool InviteUser(ulong steamId)
        {
            if (!EnsureSteamAvailable() || !IsInLobby)
            {
                return Fail("Cannot invite a user without an active Steam lobby.");
            }

            if (steamId == 0)
            {
                return Fail("Cannot invite a user because SteamID is 0.");
            }

            bool success = SteamMatchmaking.InviteUserToLobby(
                new CSteamID(_currentLobbyId),
                new CSteamID(steamId));

            if (!success)
            {
                return Fail($"Steam could not invite SteamID={steamId}.");
            }

            Log($"Lobby invite sent. SteamID={steamId}");
            return true;
        }

        /// <summary>
        /// Forces a fresh metadata read and stores a local pointer when this
        /// Steam user owns a slot in the running session. Gameplay scene
        /// completion calls this so a fast scene transition cannot race the
        /// lobby-data callback.
        /// </summary>
        public bool CaptureCurrentSessionForRejoin()
        {
            if (!IsReady || !IsInLobby)
            {
                return false;
            }

            try
            {
                RefreshCurrentLobbySnapshot(false);
                return MultiplayerLastSessionStore.Remember(
                    _currentLobby,
                    Settings,
                    SteamBootstrap.LocalSteamId);
            }
            catch (Exception exception)
            {
                LogWarning(
                    $"Could not save the current session pointer: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads Steam's live lobby member list instead of relying on the last
        /// LobbyChatUpdate callback. This is useful during NGO connection approval,
        /// where the incoming transport connection can arrive before the cached
        /// member snapshot has been refreshed on the host.
        /// </summary>
        public bool ContainsCurrentLobbyMember(ulong steamId)
        {
            if (!IsReady || !IsInLobby || steamId == 0)
            {
                return false;
            }

            try
            {
                CSteamID lobbyId = new CSteamID(_currentLobbyId);
                int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);

                for (int i = 0; i < memberCount; i++)
                {
                    if (SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i).m_SteamID ==
                        steamId)
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning(
                    $"Could not read the live lobby member list: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }

            return false;
        }

        [ContextMenu("Debug/Create Public Lobby")]
        private void DebugCreatePublicLobby()
        {
            CreateLobby(string.Empty, SteamLobbyVisibility.Public);
        }

        [ContextMenu("Debug/Refresh Lobby List")]
        private void DebugRefreshLobbyList()
        {
            RefreshLobbyList();
        }

        [ContextMenu("Debug/Join First Listed Lobby")]
        private void DebugJoinFirstListedLobby()
        {
            if (_availableLobbies.Count == 0)
            {
                Fail("There is no listed lobby. Run Debug/Refresh Lobby List first.");
                return;
            }

            JoinLobby(_availableLobbies[0].LobbyId);
        }

        [ContextMenu("Debug/Leave Lobby")]
        private void DebugLeaveLobby()
        {
            LeaveLobby();
        }

        [ContextMenu("Debug/Open Invite Overlay")]
        private void DebugOpenInviteOverlay()
        {
            OpenInviteOverlay();
        }

        [ContextMenu("Debug/Log Current Lobby")]
        private void DebugLogCurrentLobby()
        {
            if (!IsInLobby || _currentLobby == null)
            {
                Debug.Log("[SteamLobby] No current lobby.", this);
                return;
            }

            Debug.Log($"[SteamLobby] Current lobby: {_currentLobby}", this);

            for (int i = 0; i < _currentMembers.Count; i++)
            {
                SteamLobbyMember member = _currentMembers[i];
                Debug.Log(
                    $"[SteamLobby] Member {i}: {member.PersonaName} | " +
                    $"SteamID={member.SteamId} | Owner={member.IsOwner} | " +
                    $"Local={member.IsLocalPlayer}",
                    this);
            }
        }

        private bool TryRegisterSteamCallbacks()
        {
            if (_isShuttingDownForApplicationExit)
            {
                return false;
            }

            if (_callbacksRegistered)
            {
                return true;
            }

            if (!SteamBootstrap.IsSteamAvailable)
            {
                return false;
            }

            try
            {
                _lobbyCreatedCallResult =
                    CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
                _lobbyListCallResult =
                    CallResult<LobbyMatchList_t>.Create(OnLobbyListReceived);
                _lobbyEnterCallResult =
                    CallResult<LobbyEnter_t>.Create(OnLobbyEntered);

                _lobbyChatUpdateCallback =
                    Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdated);
                _lobbyDataUpdateCallback =
                    Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdated);
                _lobbyKickedCallback =
                    Callback<LobbyKicked_t>.Create(OnLobbyKicked);
                _gameLobbyJoinRequestedCallback =
                    Callback<GameLobbyJoinRequested_t>.Create(
                        OnGameLobbyJoinRequested);

                _callbacksRegistered = true;
                Log("Service ready; Steam lobby callbacks registered.");
                ServiceReady?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                DisposeSteamCallbacks();
                Fail(
                    $"Could not register Steam lobby callbacks: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        private void DisposeSteamCallbacks()
        {
            _lobbyCreatedCallResult?.Dispose();
            _lobbyListCallResult?.Dispose();
            _lobbyEnterCallResult?.Dispose();
            _lobbyChatUpdateCallback?.Dispose();
            _lobbyDataUpdateCallback?.Dispose();
            _lobbyKickedCallback?.Dispose();
            _gameLobbyJoinRequestedCallback?.Dispose();

            _lobbyCreatedCallResult = null;
            _lobbyListCallResult = null;
            _lobbyEnterCallResult = null;
            _lobbyChatUpdateCallback = null;
            _lobbyDataUpdateCallback = null;
            _lobbyKickedCallback = null;
            _gameLobbyJoinRequestedCallback = null;
            _callbacksRegistered = false;
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            SetOperation(SteamLobbyOperation.None);

            if (ioFailure)
            {
                Fail("CreateLobby failed because of a Steam I/O failure.");
                return;
            }

            if (result.m_eResult != EResult.k_EResultOK ||
                result.m_ulSteamIDLobby == 0)
            {
                Fail($"CreateLobby failed. Result={result.m_eResult}");
                return;
            }

            _currentLobbyId = result.m_ulSteamIDLobby;

            if (!WriteInitialLobbyData())
            {
                ulong failedLobbyId = _currentLobbyId;
                SteamMatchmaking.LeaveLobby(new CSteamID(failedLobbyId));
                ClearCurrentLobby(false);
                Fail(
                    $"Lobby {failedLobbyId} was created, but its required " +
                    "metadata could not be written. The lobby was left.");
                return;
            }

            RefreshCurrentLobbySnapshot(true);
            Log($"Lobby created. {_currentLobby}");
            LobbyCreated?.Invoke(_currentLobby);
        }

        private void OnLobbyListReceived(
            LobbyMatchList_t result,
            bool ioFailure)
        {
            SetOperation(SteamLobbyOperation.None);
            _availableLobbies.Clear();

            if (ioFailure)
            {
                LobbyListUpdated?.Invoke(_availableLobbies.ToArray());
                Fail("RequestLobbyList failed because of a Steam I/O failure.");
                return;
            }

            int lobbyCount = (int)result.m_nLobbiesMatching;

            for (int i = 0; i < lobbyCount; i++)
            {
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);

                if (lobbyId == CSteamID.Nil)
                {
                    continue;
                }

                if (!IsLobbyCompatible(lobbyId))
                {
                    continue;
                }

                SteamLobbySummary lobby = ReadLobbySummary(lobbyId);

                if (ShouldIncludeLobbyInBrowser(lobby))
                {
                    _availableLobbies.Add(lobby);
                }
            }

            SteamLobbySummary[] snapshot = _availableLobbies.ToArray();
            Log($"Lobby list received. Count={snapshot.Length}");

            for (int i = 0; i < snapshot.Length; i++)
            {
                Log($"Lobby[{i}] {snapshot[i]}");
            }

            LobbyListUpdated?.Invoke(snapshot);
        }

        private void OnLobbyEntered(LobbyEnter_t result, bool ioFailure)
        {
            SetOperation(SteamLobbyOperation.None);
            string expectedSessionId = _pendingExpectedSessionId;
            _pendingExpectedSessionId = string.Empty;

            if (ioFailure)
            {
                Fail("JoinLobby failed because of a Steam I/O failure.");
                return;
            }

            EChatRoomEnterResponse response =
                (EChatRoomEnterResponse)result.m_EChatRoomEnterResponse;

            if (response != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess ||
                result.m_ulSteamIDLobby == 0)
            {
                Fail($"JoinLobby failed. Response={response}");
                return;
            }

            CSteamID lobbyId = new CSteamID(result.m_ulSteamIDLobby);

            if (!IsLobbyCompatible(lobbyId))
            {
                SteamMatchmaking.LeaveLobby(lobbyId);
                Fail(
                    "Joined lobby is not compatible with this product, " +
                    $"protocol or build. LobbyID={result.m_ulSteamIDLobby}");
                return;
            }

            _currentLobbyId = result.m_ulSteamIDLobby;
            RefreshCurrentLobbySnapshot(false);

            if (!string.IsNullOrEmpty(expectedSessionId) &&
                (_currentLobby == null ||
                 !string.Equals(
                     _currentLobby.SessionId,
                     expectedSessionId,
                     StringComparison.Ordinal)))
            {
                ulong rejectedLobbyId = _currentLobbyId;
                SteamMatchmaking.LeaveLobby(lobbyId);
                ClearCurrentLobby(false);
                MultiplayerLastSessionStore.Forget();
                Fail(
                    $"Saved session {expectedSessionId} is no longer active " +
                    $"in lobby {rejectedLobbyId}.");
                return;
            }

            if (_currentLobby == null ||
                !_currentLobby.IsJoinableForLocalPlayer)
            {
                ulong rejectedLobbyId = _currentLobbyId;
                SteamMatchmaking.LeaveLobby(lobbyId);
                ClearCurrentLobby(false);

                if (!string.IsNullOrEmpty(expectedSessionId))
                {
                    MultiplayerLastSessionStore.Forget();
                }

                Fail(
                    $"Lobby {rejectedLobbyId} is already playing and the local " +
                    "Steam user is not eligible for late join or reconnect.");
                return;
            }

            CurrentLobbyUpdated?.Invoke(_currentLobby);
            LobbyMembersChanged?.Invoke(_currentMembers.ToArray());
            Log($"Lobby joined. {_currentLobby}");
            LobbyJoined?.Invoke(_currentLobby);
        }

        private void OnLobbyChatUpdated(LobbyChatUpdate_t update)
        {
            if (update.m_ulSteamIDLobby != _currentLobbyId)
            {
                return;
            }

            EChatMemberStateChange change =
                (EChatMemberStateChange)update.m_rgfChatMemberStateChange;

            Log(
                $"Lobby member changed. SteamID={update.m_ulSteamIDUserChanged}, " +
                $"Change={change}");

            bool localUserWasRemoved =
                update.m_ulSteamIDUserChanged == SteamBootstrap.LocalSteamId &&
                HasLeftLobby(change);

            if (localUserWasRemoved)
            {
                ClearCurrentLobby(true);
                return;
            }

            RefreshCurrentLobbySnapshot(true);
        }

        private void OnLobbyDataUpdated(LobbyDataUpdate_t update)
        {
            if (update.m_bSuccess == 0)
            {
                LogWarning(
                    $"Lobby data update failed. LobbyID={update.m_ulSteamIDLobby}");
                return;
            }

            if (update.m_ulSteamIDLobby == _currentLobbyId)
            {
                RefreshCurrentLobbySnapshot(true);
            }

            int listIndex = FindAvailableLobbyIndex(update.m_ulSteamIDLobby);

            if (listIndex < 0)
            {
                return;
            }

            CSteamID lobbyId = new CSteamID(update.m_ulSteamIDLobby);

            if (!IsLobbyCompatible(lobbyId))
            {
                _availableLobbies.RemoveAt(listIndex);
            }
            else
            {
                SteamLobbySummary lobby = ReadLobbySummary(lobbyId);

                if (ShouldIncludeLobbyInBrowser(lobby))
                {
                    _availableLobbies[listIndex] = lobby;
                }
                else
                {
                    _availableLobbies.RemoveAt(listIndex);
                }
            }

            LobbyListUpdated?.Invoke(_availableLobbies.ToArray());
        }

        private void OnLobbyKicked(LobbyKicked_t update)
        {
            if (update.m_ulSteamIDLobby != _currentLobbyId)
            {
                return;
            }

            ulong lobbyId = _currentLobbyId;
            ClearCurrentLobby(true);
            Fail(
                $"Removed from lobby {lobbyId}. Admin={update.m_ulSteamIDAdmin}, " +
                $"DueToDisconnect={update.m_bKickedDueToDisconnect != 0}");
        }

        private void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t request)
        {
            ulong lobbyId = request.m_steamIDLobby.m_SteamID;
            ulong friendId = request.m_steamIDFriend.m_SteamID;

            Log(
                $"Steam lobby join requested. LobbyID={lobbyId}, " +
                $"FriendSteamID={friendId}");

            LobbyJoinRequested?.Invoke(lobbyId, friendId);
        }

        private bool WriteInitialLobbyData()
        {
            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            bool success = true;

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ProductId,
                ProductId);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ProtocolVersion,
                ProtocolVersion.ToString(CultureInfo.InvariantCulture));

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.BuildId,
                BuildId);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.LobbyName,
                _pendingLobbyName);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.LobbyState,
                WaitingStateValue);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.HostSteamId,
                SteamBootstrap.LocalSteamId.ToString(CultureInfo.InvariantCulture));

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.SessionId,
                _pendingSessionId);

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.SessionRoster,
                "1|");

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.AllowReconnect,
                FormatBool(Settings.SupportsSessionRejoin));

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.AllowLateJoin,
                FormatBool(_pendingAllowLateJoin));

            success &= SteamMatchmaking.SetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ReconnectSteamIds,
                string.Empty);

            success &= SteamMatchmaking.SetLobbyJoinable(lobbyId, true);
            return success;
        }

        private SteamLobbySummary ReadLobbySummary(CSteamID lobbyId)
        {
            ulong ownerSteamId =
                SteamMatchmaking.GetLobbyOwner(lobbyId).m_SteamID;

            string hostSteamIdValue = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.HostSteamId);

            if (!ulong.TryParse(
                    hostSteamIdValue,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ulong hostSteamId))
            {
                hostSteamId = ownerSteamId;
            }

            string name = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.LobbyName);

            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"Lobby {lobbyId.m_SteamID}";
            }

            bool allowsReconnect = ParseBool(
                SteamMatchmaking.GetLobbyData(
                    lobbyId,
                    SteamLobbyDataKeys.AllowReconnect));

            bool allowsLateJoin = ParseBool(
                SteamMatchmaking.GetLobbyData(
                    lobbyId,
                    SteamLobbyDataKeys.AllowLateJoin));

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int memberLimit = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);
            int rosterCapacity = memberLimit > 0
                ? memberLimit
                : MaximumPlayers;

            string encodedRoster = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.SessionRoster);

            bool hasSessionRosterMetadata = SessionRosterCodec.TryDecode(
                encodedRoster,
                rosterCapacity,
                out List<SessionRosterSlotSnapshot> sessionSlots);

            HashSet<ulong> reconnectSteamIds = ParseSteamIds(
                SteamMatchmaking.GetLobbyData(
                    lobbyId,
                    SteamLobbyDataKeys.ReconnectSteamIds));

            if (!hasSessionRosterMetadata)
            {
                sessionSlots = BuildLegacyRosterSlots(
                    reconnectSteamIds,
                    rosterCapacity);
            }

            sessionSlots = HydrateSessionSlotPresence(
                lobbyId,
                sessionSlots,
                hostSteamId);

            bool localPlayerOwnsSessionSlot = ContainsSessionSlot(
                sessionSlots,
                SteamBootstrap.LocalSteamId);

            bool localPlayerCanReconnect =
                allowsReconnect &&
                (localPlayerOwnsSessionSlot ||
                 reconnectSteamIds.Contains(SteamBootstrap.LocalSteamId));

            int reservedReconnectSeats = allowsReconnect
                ? CountOfflineSessionSlots(sessionSlots)
                : 0;

            return new SteamLobbySummary(
                lobbyId.m_SteamID,
                name,
                ownerSteamId,
                hostSteamId,
                memberCount,
                memberLimit,
                ParseLobbyState(
                    SteamMatchmaking.GetLobbyData(
                        lobbyId,
                        SteamLobbyDataKeys.LobbyState)),
                SteamMatchmaking.GetLobbyData(
                    lobbyId,
                    SteamLobbyDataKeys.BuildId),
                allowsReconnect,
                allowsLateJoin,
                localPlayerCanReconnect,
                reservedReconnectSeats,
                SteamMatchmaking.GetLobbyData(
                    lobbyId,
                    SteamLobbyDataKeys.SessionId),
                hasSessionRosterMetadata,
                sessionSlots);
        }

        private void RefreshCurrentLobbySnapshot(bool raiseEvents)
        {
            if (!IsInLobby)
            {
                return;
            }

            CSteamID lobbyId = new CSteamID(_currentLobbyId);
            _currentLobby = ReadLobbySummary(lobbyId);
            RefreshCurrentMembers(lobbyId);

            MultiplayerLastSessionStore.Remember(
                _currentLobby,
                Settings,
                SteamBootstrap.LocalSteamId);

            if (!raiseEvents)
            {
                return;
            }

            CurrentLobbyUpdated?.Invoke(_currentLobby);
            LobbyMembersChanged?.Invoke(_currentMembers.ToArray());
        }

        private void RefreshCurrentMembers(CSteamID lobbyId)
        {
            _currentMembers.Clear();

            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            ulong ownerSteamId =
                SteamMatchmaking.GetLobbyOwner(lobbyId).m_SteamID;

            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId =
                    SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);

                if (memberId == CSteamID.Nil)
                {
                    continue;
                }

                string personaName = memberId.m_SteamID == SteamBootstrap.LocalSteamId
                    ? SteamBootstrap.LocalPersonaName
                    : SteamFriends.GetFriendPersonaName(memberId);

                if (string.IsNullOrWhiteSpace(personaName))
                {
                    personaName = memberId.m_SteamID.ToString(
                        CultureInfo.InvariantCulture);
                }

                _currentMembers.Add(
                    new SteamLobbyMember(
                        memberId.m_SteamID,
                        personaName,
                        memberId.m_SteamID == ownerSteamId,
                        memberId.m_SteamID == SteamBootstrap.LocalSteamId));
            }
        }

        private bool IsLobbyCompatible(CSteamID lobbyId)
        {
            string lobbyProductId = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ProductId);

            string lobbyProtocolVersion = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.ProtocolVersion);

            string lobbyBuildId = SteamMatchmaking.GetLobbyData(
                lobbyId,
                SteamLobbyDataKeys.BuildId);

            return string.Equals(
                       lobbyProductId,
                       ProductId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       lobbyProtocolVersion,
                       ProtocolVersion.ToString(CultureInfo.InvariantCulture),
                       StringComparison.Ordinal) &&
                   string.Equals(
                       lobbyBuildId,
                       BuildId,
                       StringComparison.Ordinal);
        }

        private bool TryBeginOperation(SteamLobbyOperation operation)
        {
            if (!EnsureSteamAvailable())
            {
                return false;
            }

            if (IsBusy)
            {
                return Fail(
                    $"Cannot start {operation}; {_operation} is still in progress.");
            }

            LastError = string.Empty;
            SetOperation(operation);
            return true;
        }

        private bool EnsureSteamAvailable()
        {
            if (!SteamBootstrap.IsSteamAvailable)
            {
                return Fail(
                    "Steam is not initialized. Wait for SteamBootstrap to finish.");
            }

            if (!_callbacksRegistered && !TryRegisterSteamCallbacks())
            {
                return Fail("Steam lobby callbacks are not ready.");
            }

            if (!SteamBootstrap.IsSteamLoggedOn)
            {
                return Fail("The local Steam user is not logged on.");
            }

            return true;
        }

        private bool EnsureLobbyOwner(string action)
        {
            if (!EnsureSteamAvailable())
            {
                return false;
            }

            if (!IsInLobby)
            {
                return Fail($"Cannot {action} without an active lobby.");
            }

            RefreshCurrentLobbySnapshot(false);

            if (!IsLobbyOwner)
            {
                return Fail($"Only the lobby owner can {action}.");
            }

            return true;
        }

        private bool AbortOperation(string message)
        {
            if (_operation == SteamLobbyOperation.JoiningLobby)
            {
                _pendingExpectedSessionId = string.Empty;
            }

            SetOperation(SteamLobbyOperation.None);
            return Fail(message);
        }

        private bool Fail(string message)
        {
            LastError = message;
            Debug.LogError($"[SteamLobby] {message}", this);
            OperationFailed?.Invoke(message);
            return false;
        }

        private void ClearCurrentLobby(bool raiseEvents)
        {
            ulong previousLobbyId = _currentLobbyId;

            _currentLobbyId = 0;
            _currentLobby = null;
            _currentMembers.Clear();
            _reconnectEligibleSteamIds.Clear();
            _pendingAllowLateJoin = false;
            _pendingSessionId = string.Empty;
            _pendingExpectedSessionId = string.Empty;

            if (!raiseEvents || previousLobbyId == 0)
            {
                return;
            }

            LobbyMembersChanged?.Invoke(Array.Empty<SteamLobbyMember>());
            LobbyLeft?.Invoke(previousLobbyId);
        }

        private void SetOperation(SteamLobbyOperation operation)
        {
            if (_operation == operation)
            {
                return;
            }

            _operation = operation;
            OperationChanged?.Invoke(_operation);
        }

        private int FindAvailableLobbyIndex(ulong lobbyId)
        {
            for (int i = 0; i < _availableLobbies.Count; i++)
            {
                if (_availableLobbies[i].LobbyId == lobbyId)
                {
                    return i;
                }
            }

            return -1;
        }

        private string NormalizeLobbyName(string lobbyName)
        {
            string value = lobbyName == null ? string.Empty : lobbyName.Trim();

            if (string.IsNullOrEmpty(value))
            {
                string personaName = SteamBootstrap.LocalPersonaName;
                value = string.IsNullOrWhiteSpace(personaName)
                    ? "Steam Lobby"
                    : $"{personaName}'s Lobby";
            }

            return value.Length <= MaximumLobbyNameLength
                ? value
                : value.Substring(0, MaximumLobbyNameLength);
        }

        private static ELobbyType ConvertLobbyType(
            SteamLobbyVisibility visibility)
        {
            switch (visibility)
            {
                case SteamLobbyVisibility.Private:
                    return ELobbyType.k_ELobbyTypePrivate;

                case SteamLobbyVisibility.FriendsOnly:
                    return ELobbyType.k_ELobbyTypeFriendsOnly;

                default:
                    return ELobbyType.k_ELobbyTypePublic;
            }
        }

        private static string ConvertLobbyState(SteamLobbyState state)
        {
            return state == SteamLobbyState.Playing
                ? PlayingStateValue
                : WaitingStateValue;
        }

        private static SteamLobbyState ParseLobbyState(string value)
        {
            if (string.Equals(
                    value,
                    WaitingStateValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SteamLobbyState.Waiting;
            }

            if (string.Equals(
                    value,
                    PlayingStateValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SteamLobbyState.Playing;
            }

            if (string.Equals(
                    value,
                    ClosingStateValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return SteamLobbyState.Closing;
            }

            return SteamLobbyState.Unknown;
        }

        private bool WriteReconnectEligibility()
        {
            if (!IsInLobby)
            {
                return false;
            }

            List<ulong> sortedSteamIds =
                new List<ulong>(_reconnectEligibleSteamIds);
            sortedSteamIds.Sort();
            string value = string.Join(",", sortedSteamIds);

            return SteamMatchmaking.SetLobbyData(
                new CSteamID(_currentLobbyId),
                SteamLobbyDataKeys.ReconnectSteamIds,
                value);
        }

        private static bool ShouldIncludeLobbyInBrowser(
            SteamLobbySummary lobby)
        {
            return lobby != null &&
                   lobby.LobbyId != 0 &&
                   lobby.HasOpenSlot &&
                   lobby.IsJoinableForLocalPlayer;
        }

        private static string FormatBool(bool value)
        {
            return value ? "1" : "0";
        }

        private static bool ParseBool(string value)
        {
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                   string.Equals(
                       value,
                       bool.TrueString,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<ulong> ParseSteamIds(string csv)
        {
            HashSet<ulong> result = new HashSet<ulong>();

            if (string.IsNullOrWhiteSpace(csv))
            {
                return result;
            }

            string[] values = csv.Split(',');

            for (int i = 0; i < values.Length; i++)
            {
                if (ulong.TryParse(
                        values[i].Trim(),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong steamId) &&
                    steamId != 0)
                {
                    result.Add(steamId);
                }
            }

            return result;
        }

        private static List<SessionRosterSlotSnapshot> BuildLegacyRosterSlots(
            HashSet<ulong> reconnectSteamIds,
            int capacity)
        {
            if (reconnectSteamIds == null || reconnectSteamIds.Count == 0)
            {
                return new List<SessionRosterSlotSnapshot>();
            }

            List<ulong> sortedSteamIds = new List<ulong>(reconnectSteamIds);
            sortedSteamIds.Sort();
            List<SessionRosterSlotSnapshot> result =
                new List<SessionRosterSlotSnapshot>(sortedSteamIds.Count);

            for (int i = 0; i < sortedSteamIds.Count && i < capacity; i++)
            {
                result.Add(new SessionRosterSlotSnapshot(
                    i,
                    sortedSteamIds[i],
                    0UL,
                    false,
                    i == 0,
                    0d,
                    0d,
                    false));
            }

            return result;
        }

        private static bool ContainsSessionSlot(
            IReadOnlyList<SessionRosterSlotSnapshot> sessionSlots,
            ulong steamId)
        {
            if (sessionSlots == null || steamId == 0)
            {
                return false;
            }

            for (int i = 0; i < sessionSlots.Count; i++)
            {
                if (sessionSlots[i].SteamId == steamId)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<SessionRosterSlotSnapshot>
            HydrateSessionSlotPresence(
                CSteamID lobbyId,
                IReadOnlyList<SessionRosterSlotSnapshot> sessionSlots,
                ulong hostSteamId)
        {
            if (sessionSlots == null || sessionSlots.Count == 0)
            {
                return new List<SessionRosterSlotSnapshot>();
            }

            HashSet<ulong> currentMemberIds = new HashSet<ulong>();
            int memberCount = SteamMatchmaking.GetNumLobbyMembers(lobbyId);

            for (int i = 0; i < memberCount; i++)
            {
                CSteamID memberId =
                    SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);

                if (memberId != CSteamID.Nil)
                {
                    currentMemberIds.Add(memberId.m_SteamID);
                }
            }

            List<SessionRosterSlotSnapshot> hydrated =
                new List<SessionRosterSlotSnapshot>(sessionSlots.Count);

            for (int i = 0; i < sessionSlots.Count; i++)
            {
                SessionRosterSlotSnapshot slot = sessionSlots[i];
                hydrated.Add(new SessionRosterSlotSnapshot(
                    slot.SlotIndex,
                    slot.SteamId,
                    0UL,
                    currentMemberIds.Contains(slot.SteamId),
                    slot.SteamId == hostSteamId,
                    slot.AssignedAt,
                    slot.DisconnectedAt,
                    slot.HasDisconnectedAt));
            }

            return hydrated;
        }

        private static int CountOfflineSessionSlots(
            IReadOnlyList<SessionRosterSlotSnapshot> sessionSlots)
        {
            int count = 0;

            if (sessionSlots == null)
            {
                return count;
            }

            for (int i = 0; i < sessionSlots.Count; i++)
            {
                if (!sessionSlots[i].IsConnected)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasLeftLobby(EChatMemberStateChange change)
        {
            const EChatMemberStateChange leftStates =
                EChatMemberStateChange.k_EChatMemberStateChangeLeft |
                EChatMemberStateChange.k_EChatMemberStateChangeDisconnected |
                EChatMemberStateChange.k_EChatMemberStateChangeKicked |
                EChatMemberStateChange.k_EChatMemberStateChangeBanned;

            return (change & leftStates) != 0;
        }

        private void Log(string message)
        {
            if (Settings.VerboseLogging)
            {
                Debug.Log($"[SteamLobby] {message}", this);
            }
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[SteamLobby] {message}", this);
        }
    }
}
