using System;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    public enum SteamConnectionRejectReason
    {
        None,
        InvalidPayload,
        UnsupportedPayloadVersion,
        SteamUnavailable,
        NotHosting,
        LobbyUnavailable,
        LobbyMismatch,
        ProductMismatch,
        ProtocolMismatch,
        BuildMismatch,
        LobbyNotWaiting,
        NotLobbyMember,
        ServerFull,
        ReconnectSeatsReserved,
        SessionRosterFull,
        SessionRejoinDisabled,
        LateJoinDisabled,
        SteamIdentityMismatch,
        DuplicateSteamUser,
        InternalError
    }

    /// <summary>
    /// The small identity and compatibility envelope sent with NGO's connection
    /// request. It is validation data, not a trusted authentication token; the host
    /// also compares SteamId with the identity supplied by the Steam transport.
    /// </summary>
    public sealed class SteamConnectionPayload
    {
        public const byte CurrentSchemaVersion = 1;

        public byte SchemaVersion { get; }
        public ulong SteamId { get; }
        public ulong LobbyId { get; }
        public string ProductId { get; }
        public int ProtocolVersion { get; }
        public string BuildId { get; }

        public SteamConnectionPayload(
            byte schemaVersion,
            ulong steamId,
            ulong lobbyId,
            string productId,
            int protocolVersion,
            string buildId)
        {
            SchemaVersion = schemaVersion;
            SteamId = steamId;
            LobbyId = lobbyId;
            ProductId = productId ?? string.Empty;
            ProtocolVersion = protocolVersion;
            BuildId = buildId ?? string.Empty;
        }

        public override string ToString()
        {
            return $"Schema={SchemaVersion}, SteamID={SteamId}, " +
                   $"LobbyID={LobbyId}, Product={ProductId}, " +
                   $"Protocol={ProtocolVersion}, Build={BuildId}";
        }
    }

    /// <summary>
    /// Keeps the wire format in one place so UI, flow and gameplay code never need
    /// to know how NGO connection data is encoded.
    /// </summary>
    public static class SteamConnectionPayloadCodec
    {
        private const uint Magic = 0x31575845; // "EXW1" in little-endian bytes.
        public const int MaximumPayloadBytes = 512;

        public static bool TryEncode(
            SteamConnectionPayload value,
            out byte[] bytes,
            out string error)
        {
            bytes = Array.Empty<byte>();
            error = string.Empty;

            if (value == null)
            {
                error = "Connection payload is null.";
                return false;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream())
                using (BinaryWriter writer =
                       new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(value.SchemaVersion);
                    writer.Write(value.SteamId);
                    writer.Write(value.LobbyId);
                    writer.Write(value.ProductId);
                    writer.Write(value.ProtocolVersion);
                    writer.Write(value.BuildId);
                    writer.Flush();

                    if (stream.Length > MaximumPayloadBytes)
                    {
                        error =
                            $"Connection payload is {stream.Length} bytes; " +
                            $"maximum is {MaximumPayloadBytes}.";
                        return false;
                    }

                    bytes = stream.ToArray();
                    return true;
                }
            }
            catch (Exception exception)
            {
                error =
                    $"Could not encode connection payload: " +
                    $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }

        public static bool TryDecode(
            byte[] bytes,
            out SteamConnectionPayload value,
            out string error)
        {
            value = null;
            error = string.Empty;

            if (bytes == null || bytes.Length == 0)
            {
                error = "Connection payload is empty.";
                return false;
            }

            if (bytes.Length > MaximumPayloadBytes)
            {
                error =
                    $"Connection payload is {bytes.Length} bytes; " +
                    $"maximum is {MaximumPayloadBytes}.";
                return false;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(bytes, false))
                using (BinaryReader reader =
                       new BinaryReader(stream, Encoding.UTF8, true))
                {
                    if (reader.ReadUInt32() != Magic)
                    {
                        error = "Connection payload magic is invalid.";
                        return false;
                    }

                    byte schemaVersion = reader.ReadByte();
                    ulong steamId = reader.ReadUInt64();
                    ulong lobbyId = reader.ReadUInt64();
                    string productId = reader.ReadString();
                    int protocolVersion = reader.ReadInt32();
                    string buildId = reader.ReadString();

                    if (stream.Position != stream.Length)
                    {
                        error = "Connection payload contains trailing data.";
                        return false;
                    }

                    value = new SteamConnectionPayload(
                        schemaVersion,
                        steamId,
                        lobbyId,
                        productId,
                        protocolVersion,
                        buildId);

                    return true;
                }
            }
            catch (Exception exception)
            {
                error =
                    $"Could not decode connection payload: " +
                    $"{exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Sends compatibility data with StartClient and owns NGO's single connection
    /// approval callback on the host.
    /// </summary>
    [DefaultExecutionOrder(-8500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Steam Connection Approval")]
    public sealed class SteamConnectionApproval : MonoBehaviour
    {
        public static SteamConnectionApproval Instance { get; private set; }

        public event Action<ulong, ulong> ClientApproved;
        public event Action<ulong, SteamConnectionRejectReason, string> ClientRejected;
        public event Action<SteamConnectionPayload> LocalPayloadChanged;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private SteamLobbyService lobbyService;
        [SerializeField] private MultiplayerSessionRoster sessionRoster;

        private Action<
            NetworkManager.ConnectionApprovalRequest,
            NetworkManager.ConnectionApprovalResponse> _approvalHandler;

        private SteamConnectionPayload _localPayload;
        private bool _ownsApprovalCallback;
        private bool _eventsSubscribed;
        private bool _isDuplicate;

        public SteamConnectionPayload LocalPayload => _localPayload;
        public string LastPayloadError { get; private set; } = string.Empty;
        public MultiplayerSettings Settings => MultiplayerSettings.Current;
        public bool DeferredPlayerSpawnEnabled =>
            Settings.DeferPlayerObjectUntilGameplay;

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
                    "[ConnectionApproval] Duplicate component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            _approvalHandler = HandleConnectionApproval;
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_isDuplicate || !ValidateReferences())
            {
                enabled = false;
                return;
            }

            if (!InstallApprovalCallback())
            {
                enabled = false;
                return;
            }

            SubscribeToEvents();
            RefreshLocalPayload();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
            UninstallApprovalCallback();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
            UninstallApprovalCallback();

            if (!_isDuplicate && Instance == this)
            {
                Instance = null;
            }
        }

        public bool RefreshLocalPayload()
        {
            if (networkManager == null || lobbyService == null)
            {
                return SetPayloadFailure(
                    "Cannot build connection payload because references are missing.");
            }

            if (!SteamBootstrap.IsSteamAvailable ||
                !SteamBootstrap.IsSteamLoggedOn ||
                SteamBootstrap.LocalSteamId == 0 ||
                !lobbyService.IsInLobby ||
                lobbyService.CurrentLobbyId == 0)
            {
                ClearLocalPayload();
                return false;
            }

            SteamConnectionPayload payload = new SteamConnectionPayload(
                SteamConnectionPayload.CurrentSchemaVersion,
                SteamBootstrap.LocalSteamId,
                lobbyService.CurrentLobbyId,
                lobbyService.ProductId,
                lobbyService.ProtocolVersion,
                lobbyService.BuildId);

            if (!SteamConnectionPayloadCodec.TryEncode(
                    payload,
                    out byte[] bytes,
                    out string error))
            {
                return SetPayloadFailure(error);
            }

            _localPayload = payload;
            LastPayloadError = string.Empty;
            networkManager.NetworkConfig.ConnectionData = bytes;
            LocalPayloadChanged?.Invoke(payload);
            Log($"Local payload ready. {payload}");
            return true;
        }

        public bool TryGetApprovedSteamId(ulong clientId, out ulong steamId)
        {
            steamId = 0UL;
            return sessionRoster != null &&
                   sessionRoster.TryGetSteamId(clientId, out steamId);
        }

        public bool IsReconnectEligible(ulong steamId)
        {
            return steamId != 0 &&
                   Settings.SupportsSessionRejoin &&
                   sessionRoster != null &&
                   sessionRoster.IsSessionMember(steamId);
        }

        /// <summary>
        /// Returns a SteamID-stable gameplay slot. NGO ClientIDs may change after
        /// reconnect, but the returning player keeps the same spawn slot.
        /// </summary>
        public int GetOrAssignGameplaySpawnSlot(ulong clientId)
        {
            return sessionRoster != null
                ? sessionRoster.GetSlotIndexForClient(clientId)
                : -1;
        }

        /// <summary>
        /// Called on the server by the leaving player's owned PlayerObject.
        /// The roster applies the project-specific graceful-exit policy; the
        /// recommended casual co-op profile preserves the slot.
        /// </summary>
        public void MarkClientIntentionalLeave(ulong clientId)
        {
            if (networkManager == null ||
                !networkManager.IsServer ||
                sessionRoster == null)
            {
                return;
            }

            sessionRoster.MarkGracefulExitIntent(clientId);
            Log($"Graceful session exit registered. ClientID={clientId}");
        }

        public static string FormatRejection(
            SteamConnectionRejectReason reason,
            string message)
        {
            string safeMessage = (message ?? string.Empty).Replace('|', '/');
            return $"{reason}|{safeMessage}";
        }

        public static bool TryParseRejection(
            string value,
            out SteamConnectionRejectReason reason,
            out string message)
        {
            reason = SteamConnectionRejectReason.None;
            message = value ?? string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int separatorIndex = value.IndexOf('|');
            string reasonValue = separatorIndex >= 0
                ? value.Substring(0, separatorIndex)
                : value;

            if (!Enum.TryParse(reasonValue, true, out reason) ||
                reason == SteamConnectionRejectReason.None)
            {
                reason = SteamConnectionRejectReason.None;
                return false;
            }

            message = separatorIndex >= 0 && separatorIndex + 1 < value.Length
                ? value.Substring(separatorIndex + 1)
                : string.Empty;

            return true;
        }

        [ContextMenu("Debug/Log Local Connection Payload")]
        private void DebugLogLocalPayload()
        {
            if (!RefreshLocalPayload())
            {
                Debug.LogWarning(
                    $"[ConnectionApproval] Payload unavailable: {LastPayloadError}",
                    this);
                return;
            }

            Debug.Log(
                $"[ConnectionApproval] Payload bytes=" +
                $"{networkManager.NetworkConfig.ConnectionData.Length} | {_localPayload}",
                this);
        }

        private void HandleConnectionApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            SetDefaultResponse(response);

            try
            {
                if (request.ClientNetworkId == NetworkManager.ServerClientId)
                {
                    ApproveLocalHost(request.ClientNetworkId, response);
                    return;
                }

                if (!networkManager.IsServer || !networkManager.IsListening)
                {
                    Reject(
                        request.ClientNetworkId,
                        response,
                        SteamConnectionRejectReason.NotHosting,
                        "This peer is not accepting host connections.");
                    return;
                }

                if (!SteamBootstrap.IsSteamAvailable ||
                    !SteamBootstrap.IsSteamLoggedOn)
                {
                    Reject(
                        request.ClientNetworkId,
                        response,
                        SteamConnectionRejectReason.SteamUnavailable,
                        "Steam is unavailable on the host.");
                    return;
                }

                if (!SteamConnectionPayloadCodec.TryDecode(
                        request.Payload,
                        out SteamConnectionPayload payload,
                        out string payloadError))
                {
                    Reject(
                        request.ClientNetworkId,
                        response,
                        SteamConnectionRejectReason.InvalidPayload,
                        payloadError);
                    return;
                }

                SteamConnectionRejectReason rejectionReason =
                    ValidateRemotePayload(request.ClientNetworkId, payload, out string error);

                if (rejectionReason != SteamConnectionRejectReason.None)
                {
                    Reject(
                        request.ClientNetworkId,
                        response,
                        rejectionReason,
                        error);
                    return;
                }

                Approve(request.ClientNetworkId, payload.SteamId, response);
            }
            catch (Exception exception)
            {
                Reject(
                    request.ClientNetworkId,
                    response,
                    SteamConnectionRejectReason.InternalError,
                    $"Approval failed: {exception.GetType().Name}.");
                Debug.LogException(exception, this);
            }
        }

        private SteamConnectionRejectReason ValidateRemotePayload(
            ulong clientId,
            SteamConnectionPayload payload,
            out string error)
        {
            error = string.Empty;

            if (payload.SchemaVersion != SteamConnectionPayload.CurrentSchemaVersion)
            {
                error =
                    $"Payload schema {payload.SchemaVersion} is unsupported; " +
                    $"expected {SteamConnectionPayload.CurrentSchemaVersion}.";
                return SteamConnectionRejectReason.UnsupportedPayloadVersion;
            }

            if (payload.SteamId == 0 || payload.LobbyId == 0)
            {
                error = "Payload SteamID or LobbyID is zero.";
                return SteamConnectionRejectReason.InvalidPayload;
            }

            SteamLobbySummary lobby = lobbyService.CurrentLobby;

            if (!lobbyService.IsInLobby ||
                !lobbyService.IsLobbyOwner ||
                lobby == null)
            {
                error = "The host does not own an active Steam lobby.";
                return SteamConnectionRejectReason.LobbyUnavailable;
            }

            if (payload.LobbyId != lobby.LobbyId)
            {
                error = "Client and host are in different Steam lobbies.";
                return SteamConnectionRejectReason.LobbyMismatch;
            }

            if (!string.Equals(
                    payload.ProductId,
                    lobbyService.ProductId,
                    StringComparison.Ordinal))
            {
                error = "Product identifiers do not match.";
                return SteamConnectionRejectReason.ProductMismatch;
            }

            if (payload.ProtocolVersion != lobbyService.ProtocolVersion)
            {
                error =
                    $"Protocol {payload.ProtocolVersion} does not match host " +
                    $"protocol {lobbyService.ProtocolVersion}.";
                return SteamConnectionRejectReason.ProtocolMismatch;
            }

            if (!string.Equals(
                    payload.BuildId,
                    lobbyService.BuildId,
                    StringComparison.Ordinal))
            {
                error =
                    $"Build '{payload.BuildId}' does not match host build " +
                    $"'{lobbyService.BuildId}'.";
                return SteamConnectionRejectReason.BuildMismatch;
            }

            if (Settings.RequireTransportIdentityMatch)
            {
                ulong transportSteamId =
                    networkManager.GetTransportIdFromClientId(clientId);

                if (transportSteamId == 0 || transportSteamId != payload.SteamId)
                {
                    error = "Payload SteamID does not match the Steam transport identity.";
                    return SteamConnectionRejectReason.SteamIdentityMismatch;
                }
            }

            if (Settings.RequireLobbyMembership &&
                !lobbyService.ContainsCurrentLobbyMember(payload.SteamId))
            {
                error = "Steam user is not a member of this lobby.";
                return SteamConnectionRejectReason.NotLobbyMember;
            }

            if (sessionRoster == null)
            {
                error = "The host session roster is unavailable.";
                return SteamConnectionRejectReason.InternalError;
            }

            SessionRosterAdmissionResult admission =
                sessionRoster.TryAdmitClient(
                    clientId,
                    payload.SteamId,
                    lobby.State,
                    lobby.AllowsLateJoin);

            if (!admission.Approved)
            {
                error = admission.Message;
                return MapRosterFailure(admission.Failure);
            }

            return SteamConnectionRejectReason.None;
        }

        private void ApproveLocalHost(
            ulong clientId,
            NetworkManager.ConnectionApprovalResponse response)
        {
            ulong steamId = SteamBootstrap.LocalSteamId;

            if (steamId == 0 &&
                MultiplayerFlowController.Instance != null &&
                MultiplayerFlowController.Instance.IsSinglePlayer)
            {
                steamId = SinglePlayerIdentity.LocalPlayerId;
            }

            SteamLobbyState lobbyState = lobbyService != null &&
                                         lobbyService.CurrentLobby != null
                ? lobbyService.CurrentLobby.State
                : SteamLobbyState.Waiting;

            SessionRosterAdmissionResult admission = sessionRoster != null
                ? sessionRoster.TryAdmitClient(
                    clientId,
                    steamId,
                    lobbyState,
                    false)
                : SessionRosterAdmissionResult.Deny(
                    SessionRosterAdmissionFailure.InvalidLobbyState,
                    "The host session roster is unavailable.");

            if (!admission.Approved)
            {
                Reject(
                    clientId,
                    response,
                    MapRosterFailure(admission.Failure),
                    admission.Message);
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject =
                ShouldCreatePlayerObjectDuringApproval();
            response.Reason = string.Empty;
            ApplyPlayerSpawn(clientId, response);

            ClientApproved?.Invoke(clientId, steamId);
            Log($"Local host approved. ClientID={clientId}, SteamID={steamId}");
        }

        private void Approve(
            ulong clientId,
            ulong steamId,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject =
                ShouldCreatePlayerObjectDuringApproval();
            response.Reason = string.Empty;
            ApplyPlayerSpawn(clientId, response);

            ClientApproved?.Invoke(clientId, steamId);
            Log($"Client approved. ClientID={clientId}, SteamID={steamId}");
        }

        private void Reject(
            ulong clientId,
            NetworkManager.ConnectionApprovalResponse response,
            SteamConnectionRejectReason reason,
            string message)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.Reason = FormatRejection(reason, message);

            Debug.LogWarning(
                $"[ConnectionApproval] Client rejected. ClientID={clientId}, " +
                $"Reason={response.Reason}",
                this);

            ClientRejected?.Invoke(clientId, reason, message ?? string.Empty);
        }

        private bool ShouldCreatePlayerObjectDuringApproval()
        {
            return !Settings.DeferPlayerObjectUntilGameplay &&
                   networkManager != null &&
                   networkManager.NetworkConfig != null &&
                   networkManager.NetworkConfig.PlayerPrefab != null;
        }

        private static void SetDefaultResponse(
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = false;
            response.CreatePlayerObject = false;
            response.PlayerPrefabHash = null;
            response.Position = null;
            response.Rotation = null;
            response.Pending = false;
            response.Reason = string.Empty;
        }

        private void ApplyPlayerSpawn(
            ulong clientId,
            NetworkManager.ConnectionApprovalResponse response)
        {
            if (!Settings.UseSceneSpawnPointsDuringApproval ||
                !response.CreatePlayerObject)
            {
                return;
            }

            int spawnSlot = GetOrAssignGameplaySpawnSlot(clientId);

            if (spawnSlot < 0 ||
                !NetworkPlayerSpawnPoint.TryGetBySlot(
                    spawnSlot,
                    out Vector3 position,
                    out Quaternion rotation))
            {
                Log(
                    $"No scene spawn point available for ClientID={clientId}; " +
                    "the Player Prefab transform will be used.");
                return;
            }

            response.Position = position;
            response.Rotation = rotation;
            Log(
                $"Spawn assigned. ClientID={clientId}, Slot={spawnSlot}, " +
                $"Position={position}");
        }

        private bool InstallApprovalCallback()
        {
            if (networkManager.IsListening)
            {
                Debug.LogError(
                    "[ConnectionApproval] Component must be enabled before " +
                    "NetworkManager starts.",
                    this);
                return false;
            }

            Action<
                NetworkManager.ConnectionApprovalRequest,
                NetworkManager.ConnectionApprovalResponse> existing =
                networkManager.ConnectionApprovalCallback;

            if (existing != null && existing != _approvalHandler)
            {
                Debug.LogError(
                    "[ConnectionApproval] NetworkManager already has a different " +
                    "connection approval callback.",
                    this);
                return false;
            }

            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = _approvalHandler;
            _ownsApprovalCallback = true;
            return true;
        }

        private void UninstallApprovalCallback()
        {
            if (!_ownsApprovalCallback || networkManager == null)
            {
                return;
            }

            if (networkManager.ConnectionApprovalCallback == _approvalHandler)
            {
                networkManager.ConnectionApprovalCallback = null;
            }

            if (!networkManager.IsListening)
            {
                networkManager.NetworkConfig.ConnectionApproval = false;
            }

            _ownsApprovalCallback = false;
        }

        private void SubscribeToEvents()
        {
            if (_eventsSubscribed)
            {
                return;
            }

            lobbyService.CurrentLobbyUpdated += HandleCurrentLobbyUpdated;
            lobbyService.LobbyLeft += HandleLobbyLeft;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
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
                lobbyService.CurrentLobbyUpdated -= HandleCurrentLobbyUpdated;
                lobbyService.LobbyLeft -= HandleLobbyLeft;
            }

            if (networkManager != null)
            {
                networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _eventsSubscribed = false;
        }

        private void HandleCurrentLobbyUpdated(SteamLobbySummary lobby)
        {
            RefreshLocalPayload();
        }

        private void HandleLobbyLeft(ulong lobbyId)
        {
            ClearLocalPayload();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            sessionRoster?.HandleClientDisconnected(clientId);
        }

        private static SteamConnectionRejectReason MapRosterFailure(
            SessionRosterAdmissionFailure failure)
        {
            switch (failure)
            {
                case SessionRosterAdmissionFailure.AlreadyConnected:
                    return SteamConnectionRejectReason.DuplicateSteamUser;

                case SessionRosterAdmissionFailure.RejoinDisabled:
                    return SteamConnectionRejectReason.SessionRejoinDisabled;

                case SessionRosterAdmissionFailure.LateJoinDisabled:
                    return SteamConnectionRejectReason.LateJoinDisabled;

                case SessionRosterAdmissionFailure.SessionRosterFull:
                    return SteamConnectionRejectReason.SessionRosterFull;

                case SessionRosterAdmissionFailure.InvalidLobbyState:
                    return SteamConnectionRejectReason.LobbyNotWaiting;

                case SessionRosterAdmissionFailure.InvalidSteamId:
                    return SteamConnectionRejectReason.InvalidPayload;

                default:
                    return SteamConnectionRejectReason.InternalError;
            }
        }

        private void ClearLocalPayload()
        {
            _localPayload = null;
            LastPayloadError = string.Empty;

            if (networkManager != null)
            {
                networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            }
        }

        private bool SetPayloadFailure(string error)
        {
            _localPayload = null;
            LastPayloadError = error ?? "Unknown payload error.";

            if (networkManager != null)
            {
                networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();
            }

            Debug.LogError(
                $"[ConnectionApproval] {LastPayloadError}",
                this);
            return false;
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

            if (sessionRoster == null)
            {
                sessionRoster = GetComponent<MultiplayerSessionRoster>();
            }

            if (sessionRoster == null && Application.isPlaying)
            {
                sessionRoster = MultiplayerSessionRoster.GetOrCreate(gameObject);
            }
        }

        private bool ValidateReferences()
        {
            ResolveReferences();

            if (networkManager == null ||
                lobbyService == null ||
                sessionRoster == null ||
                !sessionRoster.isActiveAndEnabled)
            {
                Debug.LogError(
                    "[ConnectionApproval] NetworkManager and an enabled " +
                    "MultiplayerSessionRoster must be on NetworkRuntime. " +
                    "SteamLobbyService may be assigned from SteamBootstrap.",
                    this);
                return false;
            }

            return true;
        }

        private void Log(string message)
        {
            if (Settings.VerboseLogging)
            {
                Debug.Log($"[ConnectionApproval] {message}", this);
            }
        }
    }
}
