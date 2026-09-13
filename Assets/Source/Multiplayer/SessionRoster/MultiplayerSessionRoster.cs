using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Host-authoritative bridge around SessionRosterState. Connection approval,
    /// lobby metadata, stable gameplay spawn slots and future host moderation all
    /// share this single source of truth.
    /// </summary>
    [DefaultExecutionOrder(-8600)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Session Roster")]
    public sealed class MultiplayerSessionRoster : MonoBehaviour
    {
        public static MultiplayerSessionRoster Instance { get; private set; }

        public event Action<IReadOnlyList<SessionRosterSlotSnapshot>> RosterChanged;
        public event Action<SessionRosterSlotSnapshot, MultiplayerJoinKind> SlotAdmitted;
        public event Action<SessionRosterSlotSnapshot> SlotBecameOffline;
        public event Action<SessionRosterSlotSnapshot, SessionSlotReleaseReason> SlotReleased;
        public event Action<ulong, SessionRosterAdmissionFailure, string> AdmissionDenied;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamLobbyService lobbyService;

        private SessionRosterState _state;
        private readonly HashSet<ulong> _gracefulExitClientIds =
            new HashSet<ulong>();

        private bool _eventsSubscribed;
        private bool _isDuplicate;
        private double _nextPruneTime;

        public MultiplayerSettings Settings => MultiplayerSettings.Current;
        public int Capacity
        {
            get
            {
                SteamLobbySummary lobby = lobbyService != null
                    ? lobbyService.CurrentLobby
                    : null;

                if (!IsAuthoritative && lobby != null && lobby.MemberLimit > 0)
                {
                    return lobby.MemberLimit;
                }

                return _state != null
                    ? _state.Capacity
                    : Settings.MaximumPlayers;
            }
        }

        public int AssignedCount => IsAuthoritative && _state != null
            ? _state.AssignedCount
            : Slots.Count;

        public int OfflineCount
        {
            get
            {
                if (IsAuthoritative && _state != null)
                {
                    return _state.OfflineCount;
                }

                SteamLobbySummary lobby = lobbyService != null
                    ? lobbyService.CurrentLobby
                    : null;
                return lobby != null ? lobby.ReservedReconnectSeats : 0;
            }
        }

        public int ConnectedCount => Mathf.Max(0, AssignedCount - OfflineCount);
        public int UnassignedCount => Mathf.Max(0, Capacity - AssignedCount);

        public IReadOnlyList<SessionRosterSlotSnapshot> Slots
        {
            get
            {
                if (IsAuthoritative && _state != null)
                {
                    return _state.CreateSnapshot();
                }

                SteamLobbySummary lobby = lobbyService != null
                    ? lobbyService.CurrentLobby
                    : null;
                return lobby != null
                    ? lobby.SessionSlots
                    : Array.Empty<SessionRosterSlotSnapshot>();
            }
        }

        public bool IsAuthoritative =>
            networkManager != null && networkManager.IsServer;

        private SessionSlotRetentionPolicy EffectiveRetentionPolicy =>
            Settings.SupportsSessionRejoin
                ? Settings.SessionSlotRetention
                : SessionSlotRetentionPolicy.ReleaseImmediately;

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
                    "[SessionRoster] Duplicate component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveReferences();
            _state = new SessionRosterState(Settings.MaximumPlayers);
        }

        private void OnEnable()
        {
            if (!_isDuplicate)
            {
                ResolveReferences();
                SubscribeToLobbyEvents();
            }
        }

        private void Update()
        {
            if (!IsAuthoritative ||
                Time.realtimeSinceStartupAsDouble < _nextPruneTime)
            {
                return;
            }

            _nextPruneTime = Time.realtimeSinceStartupAsDouble + 1d;
            PruneExpiredSlots();
        }

        private void OnDisable()
        {
            UnsubscribeFromLobbyEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromLobbyEvents();

            if (!_isDuplicate && Instance == this)
            {
                Instance = null;
            }
        }

        public static MultiplayerSessionRoster GetOrCreate(GameObject owner)
        {
            if (Instance != null)
            {
                return Instance;
            }

            if (owner == null)
            {
                return null;
            }

            MultiplayerSessionRoster roster =
                owner.GetComponent<MultiplayerSessionRoster>();

            if (roster == null && Application.isPlaying)
            {
                roster = owner.AddComponent<MultiplayerSessionRoster>();
                Debug.Log(
                    "[SessionRoster] Runtime component installed automatically. " +
                    "Add it permanently to NetworkRuntime to expose its Inspector.",
                    owner);
            }

            return roster;
        }

        public SessionRosterAdmissionResult TryAdmitClient(
            ulong clientId,
            ulong steamId,
            SteamLobbyState lobbyState,
            bool allowLateJoin)
        {
            EnsureStateCapacity(ResolveCapacity());
            PruneExpiredSlots();
            bool reconciledStaleSlot = ReconcileStaleConnectedSlot(
                steamId,
                clientId,
                lobbyState);

            SessionRosterAdmissionResult result = _state.TryAdmit(
                steamId,
                clientId,
                lobbyState,
                Settings.SupportsSessionRejoin,
                allowLateJoin,
                EffectiveRetentionPolicy,
                Settings.TimedSessionSlotRetentionSeconds,
                Time.realtimeSinceStartupAsDouble);

            if (!result.Approved)
            {
                if (reconciledStaleSlot)
                {
                    PublishRoster();
                }

                AdmissionDenied?.Invoke(steamId, result.Failure, result.Message);
                return result;
            }

            PublishRoster();
            SessionRosterSlotSnapshot slot = FindSlotBySteamId(steamId);

            if (slot != null)
            {
                SlotAdmitted?.Invoke(slot, result.JoinKind);
            }

            Log(
                $"Slot admitted. Slot={result.SlotIndex}, SteamID={steamId}, " +
                $"ClientID={clientId}, Kind={result.JoinKind}");
            return result;
        }

        private bool ReconcileStaleConnectedSlot(
            ulong steamId,
            ulong incomingClientId,
            SteamLobbyState lobbyState)
        {
            if (!IsAuthoritative || networkManager == null || _state == null)
            {
                return false;
            }

            SessionRosterSlotSnapshot existing = FindSlotBySteamId(steamId);

            if (existing == null ||
                !existing.IsConnected ||
                existing.ClientId == incomingClientId ||
                networkManager.ConnectedClients.ContainsKey(existing.ClientId))
            {
                return false;
            }

            _gracefulExitClientIds.Remove(existing.ClientId);

            if (!_state.MarkDisconnected(
                    existing.ClientId,
                    lobbyState,
                    false,
                    Settings.SupportsSessionRejoin,
                    EffectiveRetentionPolicy,
                    Settings.GracefulExitSlotBehaviour,
                    Time.realtimeSinceStartupAsDouble,
                    out SessionRosterSlotSnapshot changedSlot,
                    out SessionSlotReleaseReason? releaseReason))
            {
                return false;
            }

            if (releaseReason.HasValue)
            {
                SlotReleased?.Invoke(changedSlot, releaseReason.Value);
            }
            else
            {
                SlotBecameOffline?.Invoke(changedSlot);
            }

            Log(
                $"Reconciled stale ClientID={existing.ClientId} before " +
                $"SteamID={steamId} admission.");
            return true;
        }

        public void MarkGracefulExitIntent(ulong clientId)
        {
            if (IsAuthoritative)
            {
                _gracefulExitClientIds.Add(clientId);
                Log($"Graceful exit intent received. ClientID={clientId}");
            }
        }

        public void HandleClientDisconnected(ulong clientId)
        {
            if (_state == null || !IsAuthoritative)
            {
                return;
            }

            bool graceful = _gracefulExitClientIds.Remove(clientId);
            SteamLobbyState lobbyState = ResolveLobbyState();

            if (!_state.MarkDisconnected(
                    clientId,
                    lobbyState,
                    graceful,
                    Settings.SupportsSessionRejoin,
                    EffectiveRetentionPolicy,
                    Settings.GracefulExitSlotBehaviour,
                    Time.realtimeSinceStartupAsDouble,
                    out SessionRosterSlotSnapshot changedSlot,
                    out SessionSlotReleaseReason? releaseReason))
            {
                return;
            }

            PublishRoster();

            if (releaseReason.HasValue)
            {
                SlotReleased?.Invoke(changedSlot, releaseReason.Value);
                Log(
                    $"Slot released. Slot={changedSlot.SlotIndex}, " +
                    $"SteamID={changedSlot.SteamId}, Reason={releaseReason.Value}");
                return;
            }

            SlotBecameOffline?.Invoke(changedSlot);
            Log(
                $"Session slot preserved offline. Slot={changedSlot.SlotIndex}, " +
                $"SteamID={changedSlot.SteamId}, Retention=" +
                $"{EffectiveRetentionPolicy}");
        }

        public bool ReleaseOfflineSlotBySteamId(ulong steamId)
        {
            if (!CanHostManageRoster() ||
                !_state.ContainsSteamId(steamId) ||
                _state.IsSteamIdConnected(steamId) ||
                !_state.ReleaseBySteamId(
                    steamId,
                    out SessionRosterSlotSnapshot released))
            {
                return false;
            }

            PublishRoster();
            SlotReleased?.Invoke(released, SessionSlotReleaseReason.HostReleased);
            Log(
                $"Host released offline slot {released.SlotIndex}. " +
                $"SteamID={released.SteamId}");
            return true;
        }

        public bool ReleaseOfflineSlotByIndex(int slotIndex)
        {
            if (!CanHostManageRoster() ||
                !_state.ReleaseBySlotIndex(
                    slotIndex,
                    true,
                    out SessionRosterSlotSnapshot released))
            {
                return false;
            }

            PublishRoster();
            SlotReleased?.Invoke(released, SessionSlotReleaseReason.HostReleased);
            Log(
                $"Host released offline slot {slotIndex}. " +
                $"SteamID={released.SteamId}");
            return true;
        }

        public bool TryGetSteamId(ulong clientId, out ulong steamId)
        {
            steamId = 0UL;
            return _state != null && _state.TryGetSteamId(clientId, out steamId);
        }

        public bool IsSessionMember(ulong steamId)
        {
            return _state != null && _state.ContainsSteamId(steamId);
        }

        public int GetSlotIndexForClient(ulong clientId)
        {
            return _state != null
                ? _state.GetSlotIndexForClient(clientId)
                : -1;
        }

        public int GetSlotIndexForSteamId(ulong steamId)
        {
            return _state != null
                ? _state.GetSlotIndexForSteamId(steamId)
                : -1;
        }

        public int PruneExpiredSlots()
        {
            if (_state == null || !IsAuthoritative)
            {
                return 0;
            }

            List<SessionRosterSlotSnapshot> released =
                new List<SessionRosterSlotSnapshot>();
            int count = _state.PruneExpired(
                EffectiveRetentionPolicy,
                Settings.TimedSessionSlotRetentionSeconds,
                Time.realtimeSinceStartupAsDouble,
                released.Add);

            if (count <= 0)
            {
                return 0;
            }

            PublishRoster();

            for (int i = 0; i < released.Count; i++)
            {
                SessionSlotReleaseReason reason =
                    EffectiveRetentionPolicy ==
                    SessionSlotRetentionPolicy.ReleaseImmediately
                        ? SessionSlotReleaseReason.RetentionDisabled
                        : SessionSlotReleaseReason.RetentionExpired;
                SlotReleased?.Invoke(
                    released[i],
                    reason);
                Log(
                    $"Offline slot released. Slot={released[i].SlotIndex}, " +
                    $"SteamID={released[i].SteamId}, Reason={reason}");
            }

            return count;
        }

        public void LogRoster()
        {
            IReadOnlyList<SessionRosterSlotSnapshot> slots = Slots;
            Debug.Log(
                $"[SessionRoster] Capacity={Capacity}, Assigned={AssignedCount}, " +
                $"Connected={ConnectedCount}, Offline={OfflineCount}",
                this);

            for (int i = 0; i < slots.Count; i++)
            {
                SessionRosterSlotSnapshot slot = slots[i];
                Debug.Log(
                    $"[SessionRoster] [{slot.SlotIndex}] SteamID={slot.SteamId}, " +
                    $"ClientID={slot.ClientId}, Online={slot.IsConnected}, " +
                    $"Host={slot.IsHost}",
                    this);
            }
        }

        public bool ValidateSetup(bool logResult = true)
        {
            ResolveReferences();
            bool valid = isActiveAndEnabled &&
                         networkManager != null &&
                         networkManager.gameObject == gameObject &&
                         lobbyService != null;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log(
                        "[SessionRoster] PASS: runtime references are configured.",
                        this);
                }
                else
                {
                    Debug.LogError(
                        "[SessionRoster] MultiplayerSessionRoster must share " +
                        "NetworkRuntime with NetworkManager. SteamLobbyService " +
                        "may remain on SteamBootstrap, but it must be assigned " +
                        "or discoverable in the loaded scene.",
                        this);
                }
            }

            return valid;
        }

        private void HandleLobbyCreated(SteamLobbySummary lobby)
        {
            _state.Reset(ResolveCapacity());
            _gracefulExitClientIds.Clear();
            PublishRoster();
        }

        private void HandleLobbyJoined(SteamLobbySummary lobby)
        {
            RaiseRosterChanged();
        }

        private void HandleCurrentLobbyUpdated(SteamLobbySummary lobby)
        {
            RaiseRosterChanged();
        }

        private void HandleLobbyLeft(ulong lobbyId)
        {
            if (_state != null)
            {
                IReadOnlyList<SessionRosterSlotSnapshot> slots =
                    _state.CreateSnapshot();

                for (int i = 0; i < slots.Count; i++)
                {
                    SlotReleased?.Invoke(
                        slots[i],
                        SessionSlotReleaseReason.SessionEnded);
                }

                _state.Reset(Settings.MaximumPlayers);
            }

            _gracefulExitClientIds.Clear();
            RaiseRosterChanged();
        }

        private void PublishRoster()
        {
            if (_state == null ||
                lobbyService == null ||
                !lobbyService.IsInLobby ||
                !lobbyService.IsLobbyOwner)
            {
                RaiseRosterChanged();
                return;
            }

            IReadOnlyList<SessionRosterSlotSnapshot> snapshot =
                _state.CreateSnapshot();
            lobbyService.SetSessionRosterData(
                SessionRosterCodec.Encode(snapshot),
                SessionRosterCodec.EncodeSteamIds(snapshot));
            RosterChanged?.Invoke(snapshot);
        }

        private void RaiseRosterChanged()
        {
            RosterChanged?.Invoke(Slots);
        }

        private SessionRosterSlotSnapshot FindSlotBySteamId(ulong steamId)
        {
            IReadOnlyList<SessionRosterSlotSnapshot> slots =
                _state.CreateSnapshot();

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].SteamId == steamId)
                {
                    return slots[i];
                }
            }

            return null;
        }

        private bool CanHostManageRoster()
        {
            return IsAuthoritative &&
                   lobbyService != null &&
                   lobbyService.IsInLobby &&
                   lobbyService.IsLobbyOwner;
        }

        private SteamLobbyState ResolveLobbyState()
        {
            if (MultiplayerFlowController.Instance != null &&
                MultiplayerFlowController.Instance.IsSinglePlayer)
            {
                return SteamLobbyState.Playing;
            }

            return lobbyService != null && lobbyService.CurrentLobby != null
                ? lobbyService.CurrentLobby.State
                : SteamLobbyState.Unknown;
        }

        private int ResolveCapacity()
        {
            return lobbyService != null &&
                   lobbyService.CurrentLobby != null &&
                   lobbyService.CurrentLobby.MemberLimit > 0
                ? lobbyService.CurrentLobby.MemberLimit
                : Settings.MaximumPlayers;
        }

        private void EnsureStateCapacity(int capacity)
        {
            if (_state == null)
            {
                _state = new SessionRosterState(capacity);
                return;
            }

            if (_state.Capacity != capacity && _state.AssignedCount == 0)
            {
                _state.Reset(capacity);
            }
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

            if (lobbyService == null)
            {
                lobbyService = SteamLobbyService.Instance;
            }

            if (lobbyService == null)
            {
                lobbyService = FindFirstObjectByType<SteamLobbyService>(
                    FindObjectsInactive.Include);
            }
        }

        private void SubscribeToLobbyEvents()
        {
            if (_eventsSubscribed || lobbyService == null)
            {
                return;
            }

            lobbyService.LobbyCreated += HandleLobbyCreated;
            lobbyService.LobbyJoined += HandleLobbyJoined;
            lobbyService.CurrentLobbyUpdated += HandleCurrentLobbyUpdated;
            lobbyService.LobbyLeft += HandleLobbyLeft;
            _eventsSubscribed = true;
        }

        private void UnsubscribeFromLobbyEvents()
        {
            if (!_eventsSubscribed || lobbyService == null)
            {
                return;
            }

            lobbyService.LobbyCreated -= HandleLobbyCreated;
            lobbyService.LobbyJoined -= HandleLobbyJoined;
            lobbyService.CurrentLobbyUpdated -= HandleCurrentLobbyUpdated;
            lobbyService.LobbyLeft -= HandleLobbyLeft;
            _eventsSubscribed = false;
        }

        private void Log(string message)
        {
            if (Settings.VerboseLogging)
            {
                Debug.Log($"[SessionRoster] {message}", this);
            }
        }
    }
}
