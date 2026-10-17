using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [AddComponentMenu("Multiplayer/Computer/Network Store Computer")]
    public sealed class NetworkStoreComputer : NetworkInteractable
    {
        public const string UnavailableMessage = "Computer is not available.";
        private static readonly HashSet<NetworkStoreComputer> ServerComputers = new HashSet<NetworkStoreComputer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetServerComputers() => ServerComputers.Clear();

        internal static bool IsClientUsingComputerServer(NetworkManager manager, ulong clientId)
        {
            foreach (var computer in ServerComputers)
            {
                if (computer != null && computer.NetworkManager == manager &&
                    computer.IsInUse && computer.UserClientId == clientId)
                    return true;
            }
            return false;
        }

        private readonly NetworkVariable<StoreComputerSnapshot> _state =
            new NetworkVariable<StoreComputerSnapshot>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "The normal interaction transaction reserves this server-owned PC. " +
            "Only its current RPC sender and session token may change the app or leave. " +
            "Camera and input presentation start after the replicated grant.")]
        [TitleGroup("Computer")]
        [SerializeField] private StoreComputerApp initialApp;

        [ShowInInspector, ReadOnly, BoxGroup("Computer State")]
        public bool IsInUse => IsSpawned && _state.Value.IsInUse;

        [ShowInInspector, ReadOnly, BoxGroup("Computer State")]
        public ulong UserClientId => _state.Value.UserClientId;

        [ShowInInspector, ReadOnly, BoxGroup("Computer State")]
        public uint SessionToken => _state.Value.SessionToken;

        [ShowInInspector, ReadOnly, BoxGroup("Computer State")]
        public StoreComputerApp ActiveApp => IsSpawned
            ? _state.Value.ActiveApp
            : initialApp;

        public StoreComputerSnapshot State => _state.Value;

        public bool IsLocalUser => IsSpawned && IsClient &&
            NetworkManager != null &&
            _state.Value.CanControl(NetworkManager.LocalClientId, SessionToken);

        public event Action<StoreComputerSnapshot> StateChanged;
        public event Action<StoreComputerApp> AppChanged;
        public event Action<ulong, uint> UseStarted;
        public event Action<ulong, uint> UseEnded;
        public event Action<uint> LocalUseAccepted;
        public event Action LocalUseEnded;

        private NetworkManager _subscribedManager;
        private MultiplayerFlowController _subscribedFlow;
        private NetworkInteractionController _serverUserController;
        private NetworkPlayerPresenceController _serverUserPresence;
        private StoreComputerSnapshot _presentedState;
        private bool _wasLocalUser;
        private bool _listeningToState;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                ServerComputers.Add(this);
                _state.Value = new StoreComputerSnapshot
                {
                    SessionToken = _state.Value.SessionToken,
                    ActiveApp = IsValidApp(initialApp)
                        ? initialApp
                        : StoreComputerApp.Desktop
                };
            }

            _state.OnValueChanged += HandleStateChanged;
            _listeningToState = true;
            SubscribeToSession();
            PublishState(_state.Value, true);
        }

        public override void OnNetworkDespawn()
        {
            ServerComputers.Remove(this);
            if (IsServer)
            {
                ReleaseServer();
            }

            StopListening();
            EndObservedUse();
            EndLocalPresentation();
            base.OnNetworkDespawn();
        }

        private void OnEnable()
        {
            if (IsSpawned && _listeningToState)
            {
                SubscribeToSession();
                PublishState(_state.Value, true);
            }
        }

        protected override void OnDisable()
        {
            if (IsSpawned && IsServer)
            {
                ReleaseServer();
            }

            UnsubscribeFromSession();
            EndLocalPresentation();
            base.OnDisable();
        }

        public override string GetInteractionPrompt(
            NetworkInteractionController interactor)
        {
            return IsInUse ? "Computer In Use" : "Use Computer";
        }

        public override string GetInteractionDisplayName(
            NetworkInteractionController interactor)
        {
            return "Computer";
        }

        public bool RequestRelease(uint sessionToken)
        {
            if (!CanRequestLocally(sessionToken))
            {
                return false;
            }

            ReleaseServerRpc(sessionToken);
            return true;
        }

        public bool RequestApp(StoreComputerApp app, uint sessionToken)
        {
            if (!isActiveAndEnabled || !IsValidApp(app) ||
                !CanRequestLocally(sessionToken))
            {
                return false;
            }

            SetAppServerRpc(app, sessionToken);
            return true;
        }

        public static bool IsValidApp(StoreComputerApp app)
        {
            return app == StoreComputerApp.Desktop ||
                   app == StoreComputerApp.Upgrades ||
                   app == StoreComputerApp.StoreInfo;
        }

        protected override bool CanInteractServer(
            NetworkInteractionContext context,
            out string rejectionMessage)
        {
            if (_state.Value.IsInUse || !TryGetSenderPlayer(
                    context.SenderClientId,
                    out NetworkObject playerObject) ||
                context.PlayerController == null ||
                context.PlayerController.NetworkObject != playerObject ||
                !IsPlayerActive(playerObject))
            {
                rejectionMessage = UnavailableMessage;
                return false;
            }

            rejectionMessage = string.Empty;
            return true;
        }

        protected override bool ExecuteInteractionServer(
            NetworkInteractionContext context,
            out string resultMessage)
        {
            // No await or deferred work separates validation and the commit.
            if (!CanInteractServer(context, out resultMessage))
            {
                return false;
            }

            StoreComputerSnapshot next = _state.Value;
            next.IsInUse = true;
            next.UserClientId = context.SenderClientId;
            next.SessionToken = unchecked(next.SessionToken + 1);

            if (next.SessionToken == 0)
            {
                next.SessionToken = 1;
            }

            ObserveServerUser(context.PlayerController);
            _state.Value = next;
            resultMessage = "Computer reserved.";
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseServerRpc(
            uint sessionToken,
            ServerRpcParams rpcParams = default)
        {
            if (CanControlServer(rpcParams.Receive.SenderClientId, sessionToken))
            {
                ReleaseServer();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetAppServerRpc(
            StoreComputerApp app,
            uint sessionToken,
            ServerRpcParams rpcParams = default)
        {
            if (!IsValidApp(app) ||
                !CanControlServer(rpcParams.Receive.SenderClientId, sessionToken))
            {
                return;
            }

            StoreComputerSnapshot next = _state.Value;
            next.ActiveApp = app;
            _state.Value = next;
        }

        private bool CanRequestLocally(uint sessionToken)
        {
            return IsSpawned && IsClient &&
                   NetworkManager != null && NetworkManager.IsListening &&
                   !NetworkManager.ShutdownInProgress &&
                   _state.Value.CanControl(
                       NetworkManager.LocalClientId,
                       sessionToken);
        }

        private bool CanControlServer(ulong senderClientId, uint sessionToken)
        {
            return IsSpawned && IsServer && isActiveAndEnabled &&
                   _state.Value.CanControl(senderClientId, sessionToken) &&
                   TryGetSenderPlayer(senderClientId, out NetworkObject player) &&
                   _serverUserController != null &&
                   _serverUserController.NetworkObject == player &&
                   IsPlayerActive(player);
        }

        private bool TryGetSenderPlayer(
            ulong senderClientId,
            out NetworkObject playerObject)
        {
            playerObject = null;

            if (NetworkManager == null || !NetworkManager.IsServer ||
                !NetworkManager.IsListening ||
                NetworkManager.ShutdownInProgress ||
                (_subscribedFlow != null && !_subscribedFlow.IsConnected) ||
                !NetworkManager.ConnectedClients.TryGetValue(
                    senderClientId,
                    out NetworkClient client))
            {
                return false;
            }

            playerObject = client.PlayerObject;
            return playerObject != null && playerObject.IsSpawned &&
                   playerObject.IsPlayerObject &&
                   playerObject.OwnerClientId == senderClientId;
        }

        private static bool IsPlayerActive(NetworkObject player)
        {
            return !player.TryGetComponent(
                       out NetworkPlayerPresenceController presence) ||
                   presence.IsGameplayActive;
        }

        private void ObserveServerUser(NetworkInteractionController controller)
        {
            StopObservingServerUser();
            _serverUserController = controller;
            _serverUserController.NetworkDespawned += HandleServerUserDespawned;
            _serverUserPresence = controller.GetComponent<NetworkPlayerPresenceController>();

            if (_serverUserPresence != null)
            {
                _serverUserPresence.GameplayPresenceChanged += HandleServerUserPresenceChanged;
            }
        }

        private void StopObservingServerUser()
        {
            if (_serverUserController != null)
            {
                _serverUserController.NetworkDespawned -= HandleServerUserDespawned;
            }

            if (_serverUserPresence != null)
            {
                _serverUserPresence.GameplayPresenceChanged -= HandleServerUserPresenceChanged;
            }

            _serverUserController = null;
            _serverUserPresence = null;
        }

        private void ReleaseServer()
        {
            StopObservingServerUser();

            if (!IsServer || !_state.Value.IsInUse)
            {
                return;
            }

            StoreComputerSnapshot next = _state.Value;
            next.IsInUse = false;
            next.UserClientId = 0;
            _state.Value = next;
        }

        private void HandleServerUserDespawned()
        {
            ReleaseServer();
        }

        private void HandleServerUserPresenceChanged(bool active)
        {
            if (!active)
            {
                ReleaseServer();
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsServer && _state.Value.IsInUse &&
                _state.Value.UserClientId == clientId)
            {
                ReleaseServer();
            }

            if (NetworkManager != null && NetworkManager.LocalClientId == clientId)
            {
                EndLocalPresentation();
            }
        }

        private void HandleFlowStateChanged(MultiplayerFlowState state)
        {
            if (state == MultiplayerFlowState.Disconnecting ||
                state == MultiplayerFlowState.Failed ||
                state == MultiplayerFlowState.Idle)
            {
                if (IsSpawned && IsServer)
                {
                    ReleaseServer();
                }

                EndLocalPresentation();
            }
        }

        private void HandleSessionEnded(MultiplayerSessionRole role)
        {
            if (IsSpawned && IsServer)
            {
                ReleaseServer();
            }

            EndLocalPresentation();
        }

        private void SubscribeToSession()
        {
            if (_subscribedManager == null && NetworkManager != null)
            {
                _subscribedManager = NetworkManager;
                _subscribedManager.OnClientDisconnectCallback += HandleClientDisconnected;
            }

            if (_subscribedFlow == null && MultiplayerFlowController.Instance != null)
            {
                _subscribedFlow = MultiplayerFlowController.Instance;
                _subscribedFlow.StateChanged += HandleFlowStateChanged;
                _subscribedFlow.SessionEnded += HandleSessionEnded;
            }
        }

        private void UnsubscribeFromSession()
        {
            if (_subscribedManager != null)
            {
                _subscribedManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (_subscribedFlow != null)
            {
                _subscribedFlow.StateChanged -= HandleFlowStateChanged;
                _subscribedFlow.SessionEnded -= HandleSessionEnded;
            }

            _subscribedManager = null;
            _subscribedFlow = null;
        }

        private void StopListening()
        {
            if (_listeningToState)
            {
                _state.OnValueChanged -= HandleStateChanged;
                _listeningToState = false;
            }

            UnsubscribeFromSession();
            StopObservingServerUser();
        }

        private void HandleStateChanged(
            StoreComputerSnapshot previous,
            StoreComputerSnapshot current)
        {
            PublishState(current, false);
        }

        private void PublishState(StoreComputerSnapshot current, bool force)
        {
            StoreComputerSnapshot previous = _presentedState;
            bool newSession = current.IsInUse &&
                (!previous.IsInUse || previous.SessionToken != current.SessionToken);
            bool endedSession = previous.IsInUse &&
                (!current.IsInUse || previous.SessionToken != current.SessionToken);
            _presentedState = current;

            StateChanged?.Invoke(current);

            if (force || previous.ActiveApp != current.ActiveApp)
            {
                AppChanged?.Invoke(current.ActiveApp);
            }

            if (endedSession)
            {
                UseEnded?.Invoke(previous.UserClientId, previous.SessionToken);
            }

            if (newSession)
            {
                UseStarted?.Invoke(current.UserClientId, current.SessionToken);
            }

            bool isLocalUser = isActiveAndEnabled && IsLocalUser;

            if (_wasLocalUser && (!isLocalUser || newSession))
            {
                EndLocalPresentation();
            }

            if (isLocalUser && !_wasLocalUser)
            {
                _wasLocalUser = true;
                LocalUseAccepted?.Invoke(current.SessionToken);
            }
        }

        private void EndLocalPresentation()
        {
            if (!_wasLocalUser)
            {
                return;
            }

            _wasLocalUser = false;
            LocalUseEnded?.Invoke();
        }

        private void EndObservedUse()
        {
            StoreComputerSnapshot previous = _presentedState;
            _presentedState.IsInUse = false;
            _presentedState.UserClientId = 0;

            if (previous.IsInUse)
            {
                UseEnded?.Invoke(previous.UserClientId, previous.SessionToken);
            }
        }
    }
}
