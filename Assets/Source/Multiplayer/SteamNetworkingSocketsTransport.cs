using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Netcode/Steam Networking Sockets Transport")]
    public sealed class SteamNetworkingSocketsTransport : NetworkTransport
    {
        private const byte PacketHeaderVersion = 1;
        private const int PacketHeaderSize = 6;
        private const int MaximumPacketSize = 512 * 1024;

        private static readonly SteamNetworkingConfigValue_t[] NoOptions =
            Array.Empty<SteamNetworkingConfigValue_t>();

        [Header("Steam Connection")]
        [SerializeField, Range(0, 65535)]
        private int virtualPort;

        [Header("Debug")]
        [SerializeField]
        private bool verboseLogging = true;

        private readonly Queue<SteamNetConnectionStatusChangedCallback_t> _statusChanges =
            new Queue<SteamNetConnectionStatusChangedCallback_t>();

        private readonly Dictionary<ulong, HSteamNetConnection> _clientIdToConnection =
            new Dictionary<ulong, HSteamNetConnection>();

        private readonly Dictionary<HSteamNetConnection, ulong> _connectionToClientId =
            new Dictionary<HSteamNetConnection, ulong>();

        private readonly HashSet<HSteamNetConnection> _acceptedConnections =
            new HashSet<HSteamNetConnection>();

        private readonly Dictionary<HSteamNetConnection, uint> _nextSendSequence =
            new Dictionary<HSteamNetConnection, uint>();

        private readonly Dictionary<HSteamNetConnection, uint> _lastReceivedSequence =
            new Dictionary<HSteamNetConnection, uint>();

        private readonly IntPtr[] _receivedMessagePointers = new IntPtr[1];

        private Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusCallback;

        private HSteamListenSocket _listenSocket = HSteamListenSocket.Invalid;
        private HSteamNetConnection _serverConnection = HSteamNetConnection.Invalid;

        private ulong _targetSteamId;
        private bool _isRunning;
        private bool _isServer;
        private bool _clientConnected;

        public override ulong ServerClientId => 0;

        public ulong TargetSteamId => _targetSteamId;
        public bool IsRunning => _isRunning;
        public bool IsServerTransport => _isServer;

        public override bool IsSupported
        {
            get
            {
                RuntimePlatform platform = Application.platform;

                return platform == RuntimePlatform.WindowsEditor ||
                       platform == RuntimePlatform.WindowsPlayer ||
                       platform == RuntimePlatform.LinuxEditor ||
                       platform == RuntimePlatform.LinuxPlayer ||
                       platform == RuntimePlatform.OSXEditor ||
                       platform == RuntimePlatform.OSXPlayer;
            }
        }

        public void SetTargetSteamId(ulong steamId)
        {
            if (_isRunning)
            {
                Debug.LogWarning(
                    "[SteamTransport] Target SteamID cannot be changed while transport is running.",
                    this);

                return;
            }

            _targetSteamId = steamId;
        }

        public override void Initialize(NetworkManager networkManager = null)
        {
            EnsureConnectionCallback();
        }

        public override bool StartServer()
        {
            if (!PrepareForStart())
            {
                return false;
            }

            _isServer = true;

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();

                _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(
                    virtualPort,
                    NoOptions.Length,
                    NoOptions);

                if (_listenSocket == HSteamListenSocket.Invalid)
                {
                    throw new InvalidOperationException(
                        "Steam returned an invalid P2P listen socket.");
                }

                _isRunning = true;

                Log(
                    $"Host listening. SteamID={SteamUser.GetSteamID().m_SteamID}, " +
                    $"VirtualPort={virtualPort}");

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SteamTransport] Could not start host.\n{exception}",
                    this);

                CloseNativeResources();
                ResetSessionState();

                return false;
            }
        }

        public override bool StartClient()
        {
            if (_targetSteamId == 0)
            {
                Debug.LogError(
                    "[SteamTransport] Client could not start because TargetSteamId is 0.",
                    this);

                return false;
            }

            if (!PrepareForStart())
            {
                return false;
            }

            _isServer = false;

            try
            {
                SteamNetworkingUtils.InitRelayNetworkAccess();

                SteamNetworkingIdentity hostIdentity = new SteamNetworkingIdentity();
                hostIdentity.SetSteamID(new CSteamID(_targetSteamId));

                _serverConnection = SteamNetworkingSockets.ConnectP2P(
                    ref hostIdentity,
                    virtualPort,
                    NoOptions.Length,
                    NoOptions);

                if (_serverConnection == HSteamNetConnection.Invalid)
                {
                    throw new InvalidOperationException(
                        "Steam returned an invalid P2P connection.");
                }

                SteamNetworkingSockets.SetConnectionName(
                    _serverConnection,
                    $"NGO Host {_targetSteamId}");

                _isRunning = true;

                Log(
                    $"Connecting to SteamID={_targetSteamId}, " +
                    $"VirtualPort={virtualPort}");

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SteamTransport] Could not start client.\n{exception}",
                    this);

                CloseNativeResources();
                ResetSessionState();

                return false;
            }
        }

        public override void Send(
            ulong clientId,
            ArraySegment<byte> payload,
            NetworkDelivery networkDelivery)
        {
            if (!_isRunning)
            {
                return;
            }

            if (!TryGetSendConnection(clientId, out HSteamNetConnection connection))
            {
                LogWarning($"No connection found for NGO client ID {clientId}.");
                return;
            }

            if (payload.Count > MaximumPacketSize - PacketHeaderSize)
            {
                Debug.LogError(
                    $"[SteamTransport] Payload is too large: {payload.Count} bytes.",
                    this);

                return;
            }

            if (payload.Count > 0 && payload.Array == null)
            {
                Debug.LogError(
                    "[SteamTransport] Payload has data but its backing array is null.",
                    this);

                return;
            }

            byte[] packet = BuildPacket(connection, payload, networkDelivery);
            GCHandle pinnedPacket = default;

            try
            {
                pinnedPacket = GCHandle.Alloc(packet, GCHandleType.Pinned);
                IntPtr packetPointer = pinnedPacket.AddrOfPinnedObject();

                EResult result = SteamNetworkingSockets.SendMessageToConnection(
                    connection,
                    packetPointer,
                    (uint)packet.Length,
                    GetSteamSendFlags(networkDelivery),
                    out _);

                if (result != EResult.k_EResultOK)
                {
                    LogWarning(
                        $"Send failed. ClientId={clientId}, " +
                        $"Delivery={networkDelivery}, Result={result}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SteamTransport] Exception while sending packet.\n{exception}",
                    this);
            }
            finally
            {
                if (pinnedPacket.IsAllocated)
                {
                    pinnedPacket.Free();
                }
            }
        }

        public override NetworkEvent PollEvent(
            out ulong clientId,
            out ArraySegment<byte> payload,
            out float receiveTime)
        {
            clientId = ServerClientId;
            payload = default;
            receiveTime = Time.realtimeSinceStartup;

            if (!_isRunning)
            {
                return NetworkEvent.Nothing;
            }

            while (_statusChanges.Count > 0)
            {
                SteamNetConnectionStatusChangedCallback_t status =
                    _statusChanges.Dequeue();

                if (TryProcessStatusChange(
                        status,
                        out NetworkEvent networkEvent,
                        out clientId))
                {
                    receiveTime = Time.realtimeSinceStartup;
                    return networkEvent;
                }
            }

            if (TryReceiveData(out clientId, out payload))
            {
                receiveTime = Time.realtimeSinceStartup;
                return NetworkEvent.Data;
            }

            return NetworkEvent.Nothing;
        }

        public override void DisconnectLocalClient()
        {
            if (_isServer || _serverConnection == HSteamNetConnection.Invalid)
            {
                return;
            }

            HSteamNetConnection connection = _serverConnection;

            TryCloseConnection(connection, "Local client disconnected");
            RemoveConnectionState(connection);

            _serverConnection = HSteamNetConnection.Invalid;
            _clientConnected = false;

            Log("Local client disconnected.");
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!_isServer)
            {
                return;
            }

            if (!_clientIdToConnection.TryGetValue(
                    clientId,
                    out HSteamNetConnection connection))
            {
                LogWarning(
                    $"Cannot disconnect unknown NGO client ID {clientId}.");

                return;
            }

            TryCloseConnection(connection, "Disconnected by host");
            RemoveConnectionState(connection);

            Log($"Remote client disconnected. SteamID={clientId}");
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            // NGO treats this method as optional.
            // We will connect Steam's detailed ping data to the debug HUD later.
            return 0;
        }

        public override void Shutdown()
        {
            _isRunning = false;

            CloseNativeResources();
            ResetSessionState();
            DisposeConnectionCallback();

            Log("Transport shut down.");
        }

        private bool PrepareForStart()
        {
            if (_isRunning)
            {
                Debug.LogError(
                    "[SteamTransport] Transport is already running.",
                    this);

                return false;
            }

            if (!EnsureSteamReady())
            {
                return false;
            }

            if (!EnsureConnectionCallback())
            {
                return false;
            }

            ResetSessionState();
            return true;
        }

        private bool EnsureSteamReady()
        {
            try
            {
                InteropHelp.TestIfAvailableClient();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[SteamTransport] Steam API is not initialized. " +
                    $"Start multiplayer only after SteamBootstrap succeeds.\n{exception.Message}",
                    this);

                return false;
            }
        }

        private bool EnsureConnectionCallback()
        {
            if (_connectionStatusCallback != null)
            {
                return true;
            }

            if (!EnsureSteamReady())
            {
                return false;
            }

            try
            {
                _connectionStatusCallback =
                    Callback<SteamNetConnectionStatusChangedCallback_t>.Create(
                        OnConnectionStatusChanged);

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SteamTransport] Could not register Steam connection callback.\n{exception}",
                    this);

                return false;
            }
        }

        private void OnConnectionStatusChanged(
            SteamNetConnectionStatusChangedCallback_t status)
        {
            if (!_isRunning)
            {
                return;
            }

            HSteamNetConnection connection = status.m_hConn;

            bool belongsToClient = connection == _serverConnection;

            bool belongsToServer =
                _connectionToClientId.ContainsKey(connection) ||
                _acceptedConnections.Contains(connection) ||
                (_isServer &&
                 _listenSocket != HSteamListenSocket.Invalid &&
                 status.m_info.m_hListenSocket == _listenSocket);

            if (belongsToClient || belongsToServer)
            {
                _statusChanges.Enqueue(status);
            }
        }

        private bool TryProcessStatusChange(
            SteamNetConnectionStatusChangedCallback_t status,
            out NetworkEvent networkEvent,
            out ulong clientId)
        {
            networkEvent = NetworkEvent.Nothing;
            clientId = ServerClientId;

            Log(
                $"Connection state changed: " +
                $"{status.m_eOldState} -> {status.m_info.m_eState}");

            switch (status.m_info.m_eState)
            {
            case ESteamNetworkingConnectionState
                .k_ESteamNetworkingConnectionState_Connecting:

                HandleConnecting(status);
                return false;

            case ESteamNetworkingConnectionState
                .k_ESteamNetworkingConnectionState_FindingRoute:

                Log("Steam is finding a route through SDR.");
                return false;

            case ESteamNetworkingConnectionState
                .k_ESteamNetworkingConnectionState_Connected:

                return HandleConnected(
                    status,
                    out networkEvent,
                    out clientId);

            case ESteamNetworkingConnectionState
                .k_ESteamNetworkingConnectionState_ClosedByPeer:

            case ESteamNetworkingConnectionState
                .k_ESteamNetworkingConnectionState_ProblemDetectedLocally:

                return HandleClosed(
                    status,
                    out networkEvent,
                    out clientId);

            default:
                return false;
            }
        }

        private void HandleConnecting(
            SteamNetConnectionStatusChangedCallback_t status)
        {
            HSteamNetConnection connection = status.m_hConn;

            // Client tarafındaki Connecting durumu, bizim host'a doğru
            // başlattığımız outgoing bağlantıdır. Kapatılmamalıdır.
            if (!_isServer)
            {
                if (connection == _serverConnection)
                {
                    Log(
                        $"Outgoing connection is negotiating. " +
                        $"HostSteamID={_targetSteamId}");
                }

                return;
            }

            // Server yalnızca kendi listen socket'ine gelen bağlantıları kabul eder.
            if (_listenSocket == HSteamListenSocket.Invalid ||
                status.m_info.m_hListenSocket != _listenSocket)
            {
                TryCloseConnection(
                    connection,
                    "Unexpected incoming connection");

                return;
            }

            if (_acceptedConnections.Contains(connection))
            {
                return;
            }

            EResult result =
                SteamNetworkingSockets.AcceptConnection(connection);

            if (result != EResult.k_EResultOK)
            {
                LogWarning(
                    $"Could not accept incoming connection. Result={result}");

                TryCloseConnection(
                    connection,
                    "Steam connection could not be accepted");

                return;
            }

            _acceptedConnections.Add(connection);

            ulong remoteSteamId =
                status.m_info.m_identityRemote.GetSteamID64();

            Log($"Incoming connection accepted. SteamID={remoteSteamId}");
        }

        private bool HandleConnected(
            SteamNetConnectionStatusChangedCallback_t status,
            out NetworkEvent networkEvent,
            out ulong clientId)
        {
            networkEvent = NetworkEvent.Nothing;
            clientId = ServerClientId;

            HSteamNetConnection connection = status.m_hConn;

            if (_isServer)
            {
                if (_connectionToClientId.ContainsKey(connection))
                {
                    return false;
                }

                ulong remoteSteamId =
                    status.m_info.m_identityRemote.GetSteamID64();

                if (remoteSteamId == 0)
                {
                    TryCloseConnection(
                        connection,
                        "Remote Steam identity is invalid");

                    return false;
                }

                if (_clientIdToConnection.TryGetValue(
                        remoteSteamId,
                        out HSteamNetConnection existingConnection) &&
                    existingConnection != connection)
                {
                    TryCloseConnection(
                        connection,
                        "Steam account is already connected");

                    LogWarning(
                        $"Rejected duplicate Steam connection: {remoteSteamId}");

                    return false;
                }

                _acceptedConnections.Remove(connection);
                _clientIdToConnection[remoteSteamId] = connection;
                _connectionToClientId[connection] = remoteSteamId;

                clientId = remoteSteamId;
                networkEvent = NetworkEvent.Connect;

                Log($"Client connected. SteamID={remoteSteamId}");
                return true;
            }

            if (connection != _serverConnection || _clientConnected)
            {
                return false;
            }

            _clientConnected = true;
            clientId = ServerClientId;
            networkEvent = NetworkEvent.Connect;

            Log($"Connected to host. SteamID={_targetSteamId}");
            return true;
        }

        private bool HandleClosed(
            SteamNetConnectionStatusChangedCallback_t status,
            out NetworkEvent networkEvent,
            out ulong clientId)
        {
            networkEvent = NetworkEvent.Nothing;
            clientId = ServerClientId;

            HSteamNetConnection connection = status.m_hConn;
            bool shouldNotifyNgo = false;

            if (_isServer)
            {
                if (_connectionToClientId.TryGetValue(
                        connection,
                        out ulong remoteClientId))
                {
                    clientId = remoteClientId;
                    shouldNotifyNgo = true;
                }
            }
            else if (connection == _serverConnection)
            {
                clientId = ServerClientId;
                shouldNotifyNgo = true;
            }

            LogWarning(
                $"Connection closed. State={status.m_info.m_eState}, " +
                $"Reason={status.m_info.m_eEndReason}, ClientId={clientId}");

            TryCloseConnection(connection, "Connection cleanup");
            RemoveConnectionState(connection);

            if (!_isServer && connection == _serverConnection)
            {
                _serverConnection = HSteamNetConnection.Invalid;
                _clientConnected = false;
                _isRunning = false;
            }

            if (!shouldNotifyNgo)
            {
                return false;
            }

            networkEvent = NetworkEvent.Disconnect;
            return true;
        }

        private bool TryReceiveData(
            out ulong clientId,
            out ArraySegment<byte> payload)
        {
            clientId = ServerClientId;
            payload = default;

            if (_isServer)
            {
                foreach (KeyValuePair<ulong, HSteamNetConnection> pair
                         in _clientIdToConnection)
                {
                    if (TryReceiveFromConnection(
                            pair.Value,
                            pair.Key,
                            out payload))
                    {
                        clientId = pair.Key;
                        return true;
                    }
                }

                return false;
            }

            if (_serverConnection == HSteamNetConnection.Invalid)
            {
                return false;
            }

            if (TryReceiveFromConnection(
                    _serverConnection,
                    ServerClientId,
                    out payload))
            {
                clientId = ServerClientId;
                return true;
            }

            return false;
        }

        private bool TryReceiveFromConnection(
            HSteamNetConnection connection,
            ulong expectedClientId,
            out ArraySegment<byte> payload)
        {
            payload = default;

            while (true)
            {
                int messageCount;

                try
                {
                    messageCount =
                        SteamNetworkingSockets.ReceiveMessagesOnConnection(
                            connection,
                            _receivedMessagePointers,
                            1);
                }
                catch (Exception exception)
                {
                    Debug.LogError(
                        $"[SteamTransport] Receive failed for client " +
                        $"{expectedClientId}.\n{exception}",
                        this);

                    return false;
                }

                if (messageCount <= 0)
                {
                    return false;
                }

                IntPtr messagePointer = _receivedMessagePointers[0];

                if (messagePointer == IntPtr.Zero)
                {
                    continue;
                }

                try
                {
                    SteamNetworkingMessage_t message =
                        SteamNetworkingMessage_t.FromIntPtr(messagePointer);

                    if (message.m_cbSize < PacketHeaderSize ||
                        message.m_cbSize > MaximumPacketSize)
                    {
                        LogWarning(
                            $"Dropped invalid packet with size " +
                            $"{message.m_cbSize}.");

                        continue;
                    }

                    byte headerVersion =
                        Marshal.ReadByte(message.m_pData, 0);

                    if (headerVersion != PacketHeaderVersion)
                    {
                        LogWarning(
                            $"Dropped packet with unsupported header " +
                            $"version {headerVersion}.");

                        continue;
                    }

                    NetworkDelivery delivery =
                        (NetworkDelivery)Marshal.ReadByte(
                            message.m_pData,
                            1);

                    uint sequence = ReadUInt32(
                        message.m_pData,
                        2);

                    if (delivery == NetworkDelivery.UnreliableSequenced &&
                        !AcceptSequence(connection, sequence))
                    {
                        continue;
                    }

                    int payloadLength =
                        message.m_cbSize - PacketHeaderSize;

                    byte[] receivedPayload =
                        new byte[payloadLength];

                    if (payloadLength > 0)
                    {
                        Marshal.Copy(
                            IntPtr.Add(
                                message.m_pData,
                                PacketHeaderSize),
                            receivedPayload,
                            0,
                            payloadLength);
                    }

                    payload =
                        new ArraySegment<byte>(receivedPayload);

                    return true;
                }
                finally
                {
                    SteamNetworkingMessage_t.Release(messagePointer);
                    _receivedMessagePointers[0] = IntPtr.Zero;
                }
            }
        }

        private byte[] BuildPacket(
            HSteamNetConnection connection,
            ArraySegment<byte> payload,
            NetworkDelivery delivery)
        {
            uint sequence = 0;

            if (delivery == NetworkDelivery.UnreliableSequenced)
            {
                _nextSendSequence.TryGetValue(
                    connection,
                    out sequence);

                sequence = unchecked(sequence + 1);
                _nextSendSequence[connection] = sequence;
            }

            byte[] packet =
                new byte[PacketHeaderSize + payload.Count];

            packet[0] = PacketHeaderVersion;
            packet[1] = (byte)delivery;

            WriteUInt32(packet, 2, sequence);

            if (payload.Count > 0)
            {
                Array.Copy(
                    payload.Array,
                    payload.Offset,
                    packet,
                    PacketHeaderSize,
                    payload.Count);
            }

            return packet;
        }

        private bool AcceptSequence(
            HSteamNetConnection connection,
            uint incomingSequence)
        {
            if (!_lastReceivedSequence.TryGetValue(
                    connection,
                    out uint previousSequence))
            {
                _lastReceivedSequence[connection] =
                    incomingSequence;

                return true;
            }

            if (!IsNewerSequence(
                    incomingSequence,
                    previousSequence))
            {
                return false;
            }

            _lastReceivedSequence[connection] =
                incomingSequence;

            return true;
        }

        private bool TryGetSendConnection(
            ulong clientId,
            out HSteamNetConnection connection)
        {
            connection = HSteamNetConnection.Invalid;

            if (_isServer)
            {
                return _clientIdToConnection.TryGetValue(
                    clientId,
                    out connection);
            }

            if (clientId != ServerClientId ||
                !_clientConnected ||
                _serverConnection == HSteamNetConnection.Invalid)
            {
                return false;
            }

            connection = _serverConnection;
            return true;
        }

        private void RemoveConnectionState(
            HSteamNetConnection connection)
        {
            if (_connectionToClientId.TryGetValue(
                    connection,
                    out ulong clientId))
            {
                _connectionToClientId.Remove(connection);

                if (_clientIdToConnection.TryGetValue(
                        clientId,
                        out HSteamNetConnection mappedConnection) &&
                    mappedConnection == connection)
                {
                    _clientIdToConnection.Remove(clientId);
                }
            }

            _acceptedConnections.Remove(connection);
            _nextSendSequence.Remove(connection);
            _lastReceivedSequence.Remove(connection);
        }

        private void CloseNativeResources()
        {
            HashSet<HSteamNetConnection> connectionsToClose =
                new HashSet<HSteamNetConnection>(
                    _clientIdToConnection.Values);

            if (_serverConnection != HSteamNetConnection.Invalid)
            {
                connectionsToClose.Add(_serverConnection);
            }

            foreach (HSteamNetConnection connection
                     in connectionsToClose)
            {
                TryCloseConnection(
                    connection,
                    "Transport shutdown");
            }

            if (_listenSocket != HSteamListenSocket.Invalid)
            {
                try
                {
                    SteamNetworkingSockets.CloseListenSocket(
                        _listenSocket);
                }
                catch (Exception)
                {
                    // Steam may already be shutting down.
                }
            }

            _serverConnection = HSteamNetConnection.Invalid;
            _listenSocket = HSteamListenSocket.Invalid;
        }

        private void TryCloseConnection(
            HSteamNetConnection connection,
            string reason)
        {
            if (connection == HSteamNetConnection.Invalid)
            {
                return;
            }

            try
            {
                SteamNetworkingSockets.CloseConnection(
                    connection,
                    0,
                    reason,
                    false);
            }
            catch (Exception)
            {
                // Steam may already be shutting down.
            }
        }

        private void ResetSessionState()
        {
            _statusChanges.Clear();
            _clientIdToConnection.Clear();
            _connectionToClientId.Clear();
            _acceptedConnections.Clear();
            _nextSendSequence.Clear();
            _lastReceivedSequence.Clear();

            _serverConnection = HSteamNetConnection.Invalid;
            _listenSocket = HSteamListenSocket.Invalid;

            _clientConnected = false;
            _isRunning = false;
            _isServer = false;
        }

        private void DisposeConnectionCallback()
        {
            if (_connectionStatusCallback == null)
            {
                return;
            }

            _connectionStatusCallback.Dispose();
            _connectionStatusCallback = null;
        }

        private static int GetSteamSendFlags(
            NetworkDelivery delivery)
        {
            switch (delivery)
            {
                case NetworkDelivery.Unreliable:
                    return Constants
                        .k_nSteamNetworkingSend_Unreliable;

                case NetworkDelivery.UnreliableSequenced:
                    return Constants
                        .k_nSteamNetworkingSend_UnreliableNoNagle;

                case NetworkDelivery.ReliableSequenced:
                    return Constants
                        .k_nSteamNetworkingSend_ReliableNoNagle;

                case NetworkDelivery.Reliable:
                case NetworkDelivery.ReliableFragmentedSequenced:
                default:
                    return Constants
                        .k_nSteamNetworkingSend_Reliable;
            }
        }

        private static bool IsNewerSequence(
            uint incoming,
            uint previous)
        {
            return incoming != previous &&
                   unchecked((int)(incoming - previous)) > 0;
        }

        private static void WriteUInt32(
            byte[] destination,
            int offset,
            uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(
            IntPtr source,
            int offset)
        {
            return
                (uint)Marshal.ReadByte(source, offset) |
                ((uint)Marshal.ReadByte(source, offset + 1) << 8) |
                ((uint)Marshal.ReadByte(source, offset + 2) << 16) |
                ((uint)Marshal.ReadByte(source, offset + 3) << 24);
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[SteamTransport] {message}", this);
            }
        }

        private void LogWarning(string message)
        {
            if (verboseLogging)
            {
                Debug.LogWarning(
                    $"[SteamTransport] {message}",
                    this);
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }
    }
}