using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Stable identity record used by gameplay, permissions and save ownership.
    /// NGO ClientId is connection-scoped; SteamId is the durable player identity.
    /// </summary>
    public sealed class NetworkPlayerRecord
    {
        public ulong ClientId { get; }
        public ulong SteamId { get; }
        public string PersonaName { get; }
        public bool IsLocalPlayer { get; }
        public bool IsHost { get; }

        public NetworkPlayerRecord(
            ulong clientId,
            ulong steamId,
            string personaName,
            bool isLocalPlayer,
            bool isHost)
        {
            ClientId = clientId;
            SteamId = steamId;
            PersonaName = personaName ?? string.Empty;
            IsLocalPlayer = isLocalPlayer;
            IsHost = isHost;
        }

        internal bool HasSameValues(NetworkPlayerRecord other)
        {
            return other != null &&
                   ClientId == other.ClientId &&
                   SteamId == other.SteamId &&
                   string.Equals(
                       PersonaName,
                       other.PersonaName,
                       StringComparison.Ordinal) &&
                   IsLocalPlayer == other.IsLocalPlayer &&
                   IsHost == other.IsHost;
        }

        public override string ToString()
        {
            return $"ClientID={ClientId}, SteamID={SteamId}, " +
                   $"Name={PersonaName}, Local={IsLocalPlayer}, Host={IsHost}";
        }
    }

    [DefaultExecutionOrder(-8100)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Network Player Registry")]
    public sealed class NetworkPlayerRegistry : MonoBehaviour
    {
        public static NetworkPlayerRegistry Instance { get; private set; }

        public event Action<NetworkPlayerRecord> PlayerAdded;
        public event Action<NetworkPlayerRecord> PlayerUpdated;
        public event Action<NetworkPlayerRecord> PlayerRemoved;
        public event Action<IReadOnlyList<NetworkPlayerRecord>> RosterChanged;
        public event Action RegistryCleared;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamLobbyService lobbyService;
        [SerializeField] private SteamConnectionApproval connectionApproval;
        [SerializeField] private MultiplayerFlowController flowController;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private readonly Dictionary<ulong, NetworkPlayerRecord> _playersByClientId =
            new Dictionary<ulong, NetworkPlayerRecord>();

        private readonly Dictionary<ulong, ulong> _clientIdBySteamId =
            new Dictionary<ulong, ulong>();

        private readonly List<NetworkPlayerRecord> _rosterSnapshot =
            new List<NetworkPlayerRecord>();

        private bool _eventsSubscribed;
        private bool _isDuplicate;

        public IReadOnlyList<NetworkPlayerRecord> Players => _rosterSnapshot;
        public int Count => _playersByClientId.Count;

        /// <summary>
        /// True on the host, where the registry can authoritatively resolve every
        /// connected NGO client through connection approval and the Steam transport.
        /// A remote client initially knows itself and the host; later player-prefab
        /// identity replication can fill in the rest through RegisterReplicatedIdentity.
        /// </summary>
        public bool IsAuthoritativeRoster =>
            networkManager != null && networkManager.IsServer;

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
                    "[PlayerRegistry] Duplicate component destroyed.",
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
            SynchronizeExistingConnections();
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

        public bool TryGetByClientId(
            ulong clientId,
            out NetworkPlayerRecord player)
        {
            return _playersByClientId.TryGetValue(clientId, out player);
        }

        public bool TryGetBySteamId(
            ulong steamId,
            out NetworkPlayerRecord player)
        {
            player = null;

            return _clientIdBySteamId.TryGetValue(steamId, out ulong clientId) &&
                   _playersByClientId.TryGetValue(clientId, out player);
        }

        public bool TryGetLocalPlayer(out NetworkPlayerRecord player)
        {
            player = null;

            if (networkManager == null || !networkManager.IsClient)
            {
                return false;
            }

            return TryGetByClientId(networkManager.LocalClientId, out player);
        }

        /// <summary>
        /// Extension point for the future NetworkPlayerIdentity component. Calling
        /// this changes only the local read model; server authority still comes from
        /// connection approval.
        /// </summary>
        public bool RegisterReplicatedIdentity(
            ulong clientId,
            ulong steamId,
            string personaName)
        {
            if (steamId == 0)
            {
                return false;
            }

            if (networkManager != null && networkManager.IsServer &&
                connectionApproval.TryGetApprovedSteamId(
                    clientId,
                    out ulong approvedSteamId) &&
                approvedSteamId != steamId)
            {
                Debug.LogWarning(
                    $"[PlayerRegistry] Ignored replicated identity mismatch. " +
                    $"ClientID={clientId}, ApprovedSteamID={approvedSteamId}, " +
                    $"ReceivedSteamID={steamId}",
                    this);
                return false;
            }

            return RegisterOrUpdate(
                clientId,
                steamId,
                ResolvePersonaName(steamId, personaName),
                networkManager != null &&
                networkManager.IsClient &&
                clientId == networkManager.LocalClientId,
                steamId == lobbyService.CurrentHostSteamId);
        }

        public bool RemoveReplicatedIdentity(ulong clientId)
        {
            return RemovePlayer(clientId);
        }

        [ContextMenu("Debug/Log Player Registry")]
        public void LogRegistry()
        {
            Debug.Log(
                $"[PlayerRegistry] Count={Count}, " +
                $"Authoritative={IsAuthoritativeRoster}",
                this);

            for (int i = 0; i < _rosterSnapshot.Count; i++)
            {
                Debug.Log(
                    $"[PlayerRegistry] [{i}] {_rosterSnapshot[i]}",
                    this);
            }
        }

        private void SubscribeToEvents()
        {
            if (_eventsSubscribed)
            {
                return;
            }

            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            lobbyService.LobbyMembersChanged += HandleLobbyMembersChanged;
            lobbyService.LobbyLeft += HandleLobbyLeft;
            flowController.SessionEnded += HandleSessionEnded;
            _eventsSubscribed = true;
        }

        private void UnsubscribeFromEvents()
        {
            if (!_eventsSubscribed)
            {
                return;
            }

            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (lobbyService != null)
            {
                lobbyService.LobbyMembersChanged -= HandleLobbyMembersChanged;
                lobbyService.LobbyLeft -= HandleLobbyLeft;
            }

            if (flowController != null)
            {
                flowController.SessionEnded -= HandleSessionEnded;
            }

            _eventsSubscribed = false;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                RegisterServerView(clientId);
                return;
            }

            RegisterRemoteClientView();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager.IsServer)
            {
                RemovePlayer(clientId);
                return;
            }

            ClearRegistry();
        }

        private void HandleLobbyMembersChanged(
            IReadOnlyList<SteamLobbyMember> members)
        {
            if (_playersByClientId.Count == 0)
            {
                return;
            }

            NetworkPlayerRecord[] records = _rosterSnapshot.ToArray();

            for (int i = 0; i < records.Length; i++)
            {
                NetworkPlayerRecord current = records[i];
                string resolvedName = ResolvePersonaName(
                    current.SteamId,
                    current.PersonaName);

                RegisterOrUpdate(
                    current.ClientId,
                    current.SteamId,
                    resolvedName,
                    current.IsLocalPlayer,
                    current.IsHost);
            }
        }

        private void HandleLobbyLeft(ulong lobbyId)
        {
            ClearRegistry();
        }

        private void HandleSessionEnded(MultiplayerSessionRole role)
        {
            ClearRegistry();
        }

        private void SynchronizeExistingConnections()
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return;
            }

            if (networkManager.IsServer)
            {
                IReadOnlyList<ulong> clientIds = networkManager.ConnectedClientsIds;

                for (int i = 0; i < clientIds.Count; i++)
                {
                    RegisterServerView(clientIds[i]);
                }

                return;
            }

            if (networkManager.IsConnectedClient)
            {
                RegisterRemoteClientView();
            }
        }

        private void RegisterServerView(ulong clientId)
        {
            ulong steamId;

            if (clientId == NetworkManager.ServerClientId)
            {
                steamId = SteamBootstrap.LocalSteamId;

                if (steamId == 0 && flowController.IsSinglePlayer)
                {
                    steamId = SinglePlayerIdentity.LocalPlayerId;
                }
            }
            else if (!connectionApproval.TryGetApprovedSteamId(clientId, out steamId))
            {
                steamId = networkManager.GetTransportIdFromClientId(clientId);
            }

            if (steamId == 0)
            {
                Debug.LogWarning(
                    $"[PlayerRegistry] Could not resolve SteamID for " +
                    $"ClientID={clientId}.",
                    this);
                return;
            }

            RegisterOrUpdate(
                clientId,
                steamId,
                ResolvePersonaName(steamId, string.Empty),
                clientId == networkManager.LocalClientId,
                clientId == NetworkManager.ServerClientId);
        }

        private void RegisterRemoteClientView()
        {
            ulong hostSteamId = lobbyService.CurrentHostSteamId;

            if (hostSteamId != 0)
            {
                RegisterOrUpdate(
                    NetworkManager.ServerClientId,
                    hostSteamId,
                    ResolvePersonaName(hostSteamId, string.Empty),
                    networkManager.LocalClientId == NetworkManager.ServerClientId,
                    true);
            }

            ulong localSteamId = SteamBootstrap.LocalSteamId;

            if (networkManager.IsClient && localSteamId != 0)
            {
                RegisterOrUpdate(
                    networkManager.LocalClientId,
                    localSteamId,
                    ResolvePersonaName(
                        localSteamId,
                        SteamBootstrap.LocalPersonaName),
                    true,
                    localSteamId == hostSteamId);
            }
        }

        private bool RegisterOrUpdate(
            ulong clientId,
            ulong steamId,
            string personaName,
            bool isLocalPlayer,
            bool isHost)
        {
            if (steamId == 0)
            {
                return false;
            }

            if (_clientIdBySteamId.TryGetValue(
                    steamId,
                    out ulong existingClientId) &&
                existingClientId != clientId)
            {
                Debug.LogWarning(
                    $"[PlayerRegistry] SteamID={steamId} is already mapped to " +
                    $"ClientID={existingClientId}; ClientID={clientId} was ignored.",
                    this);
                return false;
            }

            NetworkPlayerRecord next = new NetworkPlayerRecord(
                clientId,
                steamId,
                personaName,
                isLocalPlayer,
                isHost);

            if (_playersByClientId.TryGetValue(
                    clientId,
                    out NetworkPlayerRecord previous))
            {
                if (previous.HasSameValues(next))
                {
                    return true;
                }

                if (previous.SteamId != steamId)
                {
                    _clientIdBySteamId.Remove(previous.SteamId);
                }

                _playersByClientId[clientId] = next;
                _clientIdBySteamId[steamId] = clientId;
                RebuildRosterSnapshot();
                Log($"Player updated. {next}");
                PlayerUpdated?.Invoke(next);
                PublishRosterChanged();
                return true;
            }

            _playersByClientId.Add(clientId, next);
            _clientIdBySteamId[steamId] = clientId;
            RebuildRosterSnapshot();
            Log($"Player added. {next}");
            PlayerAdded?.Invoke(next);
            PublishRosterChanged();
            return true;
        }

        private bool RemovePlayer(ulong clientId)
        {
            if (!_playersByClientId.TryGetValue(
                    clientId,
                    out NetworkPlayerRecord removed))
            {
                return false;
            }

            _playersByClientId.Remove(clientId);
            _clientIdBySteamId.Remove(removed.SteamId);
            RebuildRosterSnapshot();
            Log($"Player removed. {removed}");
            PlayerRemoved?.Invoke(removed);
            PublishRosterChanged();
            return true;
        }

        private void ClearRegistry()
        {
            if (_playersByClientId.Count == 0)
            {
                return;
            }

            _playersByClientId.Clear();
            _clientIdBySteamId.Clear();
            _rosterSnapshot.Clear();
            Log("Registry cleared.");
            RegistryCleared?.Invoke();
            PublishRosterChanged();
        }

        private void RebuildRosterSnapshot()
        {
            _rosterSnapshot.Clear();

            foreach (NetworkPlayerRecord player in _playersByClientId.Values)
            {
                _rosterSnapshot.Add(player);
            }

            _rosterSnapshot.Sort(
                (left, right) => left.ClientId.CompareTo(right.ClientId));
        }

        private void PublishRosterChanged()
        {
            RosterChanged?.Invoke(_rosterSnapshot.ToArray());
        }

        private string ResolvePersonaName(ulong steamId, string fallback)
        {
            if (steamId == SinglePlayerIdentity.LocalPlayerId &&
                flowController != null && flowController.IsSinglePlayer)
            {
                return SinglePlayerIdentity.DefaultPlayerName;
            }

            IReadOnlyList<SteamLobbyMember> members = lobbyService.CurrentMembers;

            for (int i = 0; i < members.Count; i++)
            {
                SteamLobbyMember member = members[i];

                if (member.SteamId == steamId &&
                    !string.IsNullOrWhiteSpace(member.PersonaName))
                {
                    return member.PersonaName;
                }
            }

            if (steamId == SteamBootstrap.LocalSteamId &&
                !string.IsNullOrWhiteSpace(SteamBootstrap.LocalPersonaName))
            {
                return SteamBootstrap.LocalPersonaName;
            }

            return !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : steamId.ToString(CultureInfo.InvariantCulture);
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

            if (connectionApproval == null)
            {
                connectionApproval = GetComponent<SteamConnectionApproval>();
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
                connectionApproval == null ||
                flowController == null)
            {
                Debug.LogError(
                    "[PlayerRegistry] NetworkManager, SteamLobbyService, " +
                    "SteamConnectionApproval and MultiplayerFlowController must be " +
                    "on the same GameObject or assigned in the Inspector.",
                    this);
                return false;
            }

            return true;
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[PlayerRegistry] {message}", this);
            }
        }
    }
}
