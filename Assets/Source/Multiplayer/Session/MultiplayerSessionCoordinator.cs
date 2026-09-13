using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EXW.Multiplayer
{
    public enum GameSessionMode
    {
        None,
        SinglePlayer,
        CoopHost,
        CoopClient
    }

    public enum GameSessionState
    {
        Idle,
        StartingSinglePlayer,
        Lobby,
        LoadingGameplay,
        Gameplay,
        ReturningToMainMenu,
        Failed
    }

    /// <summary>
    /// Bridges menu/lobby state to NGO's server-authoritative scene manager.
    /// Singleplayer also runs as an NGO host, using SinglePlayerTransport, so
    /// gameplay code follows exactly the same server-authoritative path.
    /// </summary>
    [DefaultExecutionOrder(-7900)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Session/Multiplayer Session Coordinator")]
    public sealed class MultiplayerSessionCoordinator : MonoBehaviour
    {
        public static MultiplayerSessionCoordinator Instance { get; private set; }

        public event Action<GameSessionState> StateChanged;
        public event Action<string> StatusChanged;
        public event Action<string> OperationFailed;

        [Header("Runtime References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private MultiplayerFlowController flowController;
        [SerializeField] private SteamLobbyService lobbyService;
        
        [Header("Game Session Loading")]
        [SerializeField] private string gameSessionLoadingSceneName = "GameSessionLoadingScene";

        private NetworkSceneManager _subscribedSceneManager;
        private bool _flowEventsSubscribed;
        private bool _pendingGameplayLoad;
        private bool _returnToMainMenuRequested;
        private bool _disconnectAfterLeaveNotice;
        private bool _waitingForLatePlayerObject;
        private bool _isDuplicate;
        private float _disconnectAfterLeaveNoticeTime;

        public GameSessionMode Mode { get; private set; } = GameSessionMode.None;
        public GameSessionState State { get; private set; } = GameSessionState.Idle;
        public string StatusMessage { get; private set; } = "Idle";
        public string LastError { get; private set; } = string.Empty;
        public MultiplayerSettings Settings => MultiplayerSettings.Current;

        public bool IsLoading =>
            State == GameSessionState.StartingSinglePlayer ||
            State == GameSessionState.LoadingGameplay ||
            State == GameSessionState.ReturningToMainMenu;

        public bool CanStartSinglePlayer =>
            State == GameSessionState.Idle &&
            flowController != null &&
            flowController.IsIdle;

        public bool CanHostStartGame =>
            State == GameSessionState.Lobby &&
            Mode == GameSessionMode.CoopHost &&
            flowController != null &&
            flowController.IsHost &&
            networkManager != null &&
            networkManager.IsServer &&
            lobbyService != null &&
            lobbyService.IsInLobby &&
            lobbyService.IsLobbyOwner;

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
                    "[SessionCoordinator] Duplicate component destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveReferences();
        }

        private void OnEnable()
        {
            if (!_isDuplicate)
            {
                SubscribeToFlowEvents();
                TrySubscribeToSceneEvents();
                SynchronizeWithFlow();
            }
        }

        private void Update()
        {
            if (!_flowEventsSubscribed)
            {
                ResolveReferences();
                SubscribeToFlowEvents();
            }

            TrySubscribeToSceneEvents();

            if (_waitingForLatePlayerObject)
            {
                TryCompleteLateClientPlayerSpawn();
            }

            if (_pendingGameplayLoad &&
                networkManager != null &&
                networkManager.IsServer &&
                networkManager.IsListening &&
                networkManager.SceneManager != null)
            {
                _pendingGameplayLoad = false;
                BeginServerGameplayLoad();
            }

            if (_returnToMainMenuRequested)
            {
                ProgressIntentionalDisconnectNotice();
                TryCompleteReturnToMainMenu();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromFlowEvents();
            UnsubscribeFromSceneEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeFromFlowEvents();
            UnsubscribeFromSceneEvents();

            if (!_isDuplicate && Instance == this)
            {
                Instance = null;
            }
        }

        public bool StartSinglePlayer()
        {
            ResolveReferences();

            if (!CanStartSinglePlayer)
            {
                return Reject(
                    "Singleplayer cannot start while another session operation is active.");
            }

            if (!ValidateGameplayScene() ||
                !ValidateGameSessionLoadingScene())
            {
                return false;
            }

            Mode = GameSessionMode.SinglePlayer;
            LastError = string.Empty;
            SetState(
                GameSessionState.StartingSinglePlayer,
                "Starting singleplayer...");

            if (flowController.StartSinglePlayer())
            {
                return true;
            }

            Mode = GameSessionMode.None;
            return Fail(
                string.IsNullOrWhiteSpace(flowController.LastError)
                    ? "The offline NGO host could not start."
                    : flowController.LastError);
        }

        public bool StartCoopGame()
        {
            ResolveReferences();

            if (!CanHostStartGame)
            {
                return Reject(
                    "Only the connected Steam lobby host can start the game.");
            }

            if (!ValidateGameplayScene())
            {
                return false;
            }
            
            if (!ValidateGameSessionLoadingScene())
            {
                return false;
            }

            if (!lobbyService.EnterGameplayState())
            {
                return Fail(
                    string.IsNullOrWhiteSpace(lobbyService.LastError)
                        ? "Steam could not apply the gameplay join policy."
                        : lobbyService.LastError,
                    false);
            }

            LastError = string.Empty;
            SetState(
                GameSessionState.LoadingGameplay,
                "Host is loading gameplay...");
            _pendingGameplayLoad = true;
            return true;
        }

        /// <summary>
        /// Leaves the local NGO/Steam session first, then performs a local scene
        /// load only after every transport and lobby cleanup step has completed.
        /// A client leaves only itself; a host shutdown disconnects all clients.
        /// </summary>
        public bool LeaveSessionAndReturnToMainMenu()
        {
            ResolveReferences();

            if (_returnToMainMenuRequested)
            {
                return true;
            }

            if (networkManager == null || flowController == null)
            {
                return Reject(
                    "Cannot return safely because NetworkManager or MultiplayerFlowController is missing.");
            }

            if (!ValidateMainMenuScene())
            {
                return false;
            }

            _pendingGameplayLoad = false;
            _returnToMainMenuRequested = true;
            LastError = string.Empty;
            SetState(
                GameSessionState.ReturningToMainMenu,
                "Leaving session and returning to main menu...");

            if (flowController != null && !IsSessionCleanupComplete())
            {
                if (!TryScheduleIntentionalClientDisconnect())
                {
                    flowController.Disconnect();
                }
            }

            // The actual scene load is deliberately deferred to Update. NGO and
            // Steam callbacks may still be unwinding during this method call.
            return true;
        }

        public void LogSessionState()
        {
            Debug.Log(
                $"[SessionCoordinator] Mode={Mode} | State={State} | " +
                $"Scene={SceneManager.GetActiveScene().name} | " +
                $"NGO Listening={(networkManager != null && networkManager.IsListening)} | " +
                $"Server={(networkManager != null && networkManager.IsServer)} | " +
                $"Status={StatusMessage}",
                this);
        }

        public void ClearFailure()
        {
            LastError = string.Empty;

            if (State == GameSessionState.Failed &&
                (flowController == null || !flowController.IsConnected))
            {
                Mode = GameSessionMode.None;
                SetState(GameSessionState.Idle, "Idle");
            }
        }

        public bool ValidateSetup(bool logResult = true)
        {
            ResolveReferences();
            List<string> missing = new List<string>();

            if (networkManager == null)
            {
                missing.Add(nameof(networkManager));
            }
            else if (networkManager.NetworkConfig == null ||
                     !networkManager.NetworkConfig.EnableSceneManagement)
            {
                missing.Add("NetworkManager.EnableSceneManagement");
            }

            if (flowController == null)
            {
                missing.Add(nameof(flowController));
            }

            if (lobbyService == null)
            {
                missing.Add(nameof(lobbyService));
            }

            if (networkManager != null &&
                networkManager.GetComponent<MultiplayerSessionRoster>() == null)
            {
                missing.Add("NetworkRuntime.MultiplayerSessionRoster");
            }

            if (string.IsNullOrWhiteSpace(Settings.GameplaySceneName))
            {
                missing.Add("MultiplayerSettings.GameplaySceneName");
            }

            if (string.IsNullOrWhiteSpace(Settings.MainMenuSceneName))
            {
                missing.Add("MultiplayerSettings.MainMenuSceneName");
            }

            if (networkManager != null &&
                networkManager.NetworkConfig != null &&
                networkManager.NetworkConfig.PlayerPrefab == null)
            {
                missing.Add("NetworkManager.PlayerPrefab");
            }

            bool valid = missing.Count == 0;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log(
                        "[SessionCoordinator] PASS: runtime references and scene name are configured.",
                        this);
                }
                else
                {
                    Debug.LogError(
                        "[SessionCoordinator] Missing configuration:\n- " +
                        string.Join("\n- ", missing),
                        this);
                }
            }

            return valid;
        }

        private void HandleSessionStarted(MultiplayerSessionRole role)
        {
            if (flowController != null && flowController.IsSinglePlayer)
            {
                Mode = GameSessionMode.SinglePlayer;
                SetState(
                    GameSessionState.LoadingGameplay,
                    "Loading singleplayer gameplay...");
                _pendingGameplayLoad = true;
                return;
            }

            Mode = role == MultiplayerSessionRole.Host
                ? GameSessionMode.CoopHost
                : GameSessionMode.CoopClient;

            bool joiningRunningSession =
                role == MultiplayerSessionRole.Client &&
                lobbyService != null &&
                lobbyService.CurrentLobby != null &&
                lobbyService.CurrentLobby.State == SteamLobbyState.Playing;

            if (joiningRunningSession)
            {
                SetState(
                    GameSessionState.LoadingGameplay,
                    lobbyService.CurrentLobby.IsLocalPlayerReconnectEligible
                        ? "Reconnecting and synchronizing gameplay..."
                        : "Joining running game and synchronizing gameplay...");
                return;
            }

            SetState(
                GameSessionState.Lobby,
                role == MultiplayerSessionRole.Host
                    ? "Lobby ready. Waiting for the host to start."
                    : "Joined lobby. Waiting for the host to start.");
        }

        private void HandleSessionEnded(MultiplayerSessionRole role)
        {
            _pendingGameplayLoad = false;
            _disconnectAfterLeaveNotice = false;
            _waitingForLatePlayerObject = false;
            Mode = GameSessionMode.None;

            if (!_returnToMainMenuRequested &&
                State != GameSessionState.Failed)
            {
                SetState(GameSessionState.Idle, "Idle");
            }
        }

        private void HandleFlowFailed(string message)
        {
            _pendingGameplayLoad = false;
            _disconnectAfterLeaveNotice = false;
            _waitingForLatePlayerObject = false;
            Mode = GameSessionMode.None;

            if (_returnToMainMenuRequested || IsGameplaySceneActive())
            {
                _returnToMainMenuRequested = true;
                LastError = string.IsNullOrWhiteSpace(message)
                    ? "The multiplayer session ended unexpectedly."
                    : message;
                SetState(
                    GameSessionState.ReturningToMainMenu,
                    "Session ended. Returning to main menu...");
                OperationFailed?.Invoke(LastError);
                return;
            }

            Fail(message);
        }

        private void HandleFlowStateChanged(MultiplayerFlowState flowState)
        {
            if (flowState == MultiplayerFlowState.Idle &&
                !flowController.IsConnected &&
                !_returnToMainMenuRequested &&
                State != GameSessionState.StartingSinglePlayer)
            {
                Mode = GameSessionMode.None;
                LastError = string.Empty;
                SetState(GameSessionState.Idle, "Idle");
            }
        }

        private void BeginServerGameplayLoad()
        {
            if (networkManager == null ||
                !networkManager.IsServer ||
                !networkManager.IsListening ||
                networkManager.SceneManager == null)
            {
                RollBackGameplayStart(
                    "NGO server stopped before gameplay could load.");
                return;
            }

            TrySubscribeToSceneEvents();

            // Singleplayer ve co-op host aynı hazırlık sahnesinden geçer.
            string targetSceneName = gameSessionLoadingSceneName;

            SetState(
                GameSessionState.LoadingGameplay,
                Mode == GameSessionMode.SinglePlayer
                    ? "Loading singleplayer gameplay..."
                    : "Preparing game session...");

            SceneEventProgressStatus result =
                networkManager.SceneManager.LoadScene(
                    targetSceneName,
                    LoadSceneMode.Single);

            if (result != SceneEventProgressStatus.Started)
            {
                RollBackGameplayStart(
                    $"NGO rejected scene '{targetSceneName}' load: {result}.");
            }
        }

        private void HandleSceneEvent(SceneEvent sceneEvent)
        {
            if (sceneEvent == null || !IsGameplaySceneEvent(sceneEvent))
            {
                return;
            }

            switch (sceneEvent.SceneEventType)
            {
                case SceneEventType.Load:
                    SetState(
                        GameSessionState.LoadingGameplay,
                        "Loading gameplay...");
                    break;

                case SceneEventType.LoadEventCompleted:
                    CompleteGameplayLoad();
                    break;

                case SceneEventType.Synchronize:
                    if (networkManager != null && !networkManager.IsServer)
                    {
                        SetState(
                            GameSessionState.LoadingGameplay,
                            "Synchronizing running gameplay...");
                    }
                    break;

                case SceneEventType.SynchronizeComplete:
                    CompleteLateClientSynchronization(sceneEvent.ClientId);
                    break;
            }
        }

        private void CompleteGameplayLoad()
        {
            if (networkManager != null &&
                networkManager.IsServer &&
                Settings.ActivatePlayersAfterLoad)
            {
                ActivateConnectedPlayers();
            }

            lobbyService?.CaptureCurrentSessionForRejoin();

            SetState(GameSessionState.Gameplay, "Gameplay ready.");
        }

        private void ActivateConnectedPlayers()
        {
            IReadOnlyList<ulong> connectedClientIds =
                networkManager.ConnectedClientsIds;

            for (int slot = 0; slot < connectedClientIds.Count; slot++)
            {
                ActivateClientPlayer(connectedClientIds[slot], slot);
            }
        }

        private void CompleteLateClientSynchronization(ulong clientId)
        {
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.IsServer && Settings.ActivatePlayersAfterLoad)
            {
                ActivateClientPlayer(clientId, -1);
            }

            if (networkManager.IsClient &&
                clientId == networkManager.LocalClientId)
            {
                _waitingForLatePlayerObject = true;
                TryCompleteLateClientPlayerSpawn();
            }
        }

        private void TryCompleteLateClientPlayerSpawn()
        {
            if (!_waitingForLatePlayerObject ||
                networkManager == null ||
                networkManager.LocalClient == null ||
                networkManager.LocalClient.PlayerObject == null)
            {
                return;
            }

            _waitingForLatePlayerObject = false;
            lobbyService?.CaptureCurrentSessionForRejoin();
            SetState(GameSessionState.Gameplay, "Gameplay synchronized.");
        }

        private void ActivateClientPlayer(ulong clientId, int fallbackSlot)
        {
            if (networkManager == null ||
                !networkManager.ConnectedClients.TryGetValue(
                    clientId,
                    out NetworkClient client))
            {
                Debug.LogWarning(
                    $"[SessionCoordinator] Client record missing for ClientID={clientId}.",
                    this);
                return;
            }

            int slot = SteamConnectionApproval.Instance != null
                ? SteamConnectionApproval.Instance.GetOrAssignGameplaySpawnSlot(
                    clientId)
                : -1;

            if (slot < 0)
            {
                slot = fallbackSlot >= 0
                    ? fallbackSlot
                    : FindConnectedClientIndex(clientId);
            }

            GameObject playerPrefab =
                networkManager.NetworkConfig.PlayerPrefab;
            NetworkObject playerObject = client.PlayerObject;
            Vector3 position = playerObject != null
                ? playerObject.transform.position
                : playerPrefab != null
                    ? playerPrefab.transform.position
                    : Vector3.zero;
            Quaternion rotation = playerObject != null
                ? playerObject.transform.rotation
                : playerPrefab != null
                    ? playerPrefab.transform.rotation
                    : Quaternion.identity;

            if (slot < 0 ||
                !NetworkPlayerSpawnPoint.TryGetBySlot(
                    slot,
                    out position,
                    out rotation))
            {
                Debug.LogWarning(
                    $"[SessionCoordinator] No gameplay spawn point for slot {slot}; " +
                    "the current player pose will be used.",
                    this);
            }

            if (playerObject == null)
            {
                playerObject = SpawnPlayerObject(
                    clientId,
                    position,
                    rotation);

                if (playerObject == null)
                {
                    return;
                }
            }

            NetworkPlayerPresenceController presence =
                playerObject.GetComponent<NetworkPlayerPresenceController>();

            if (presence != null)
            {
                presence.ActivateGameplayServer(position, rotation);
            }
            else
            {
                playerObject.transform.SetPositionAndRotation(
                    position,
                    rotation);
                Debug.LogWarning(
                    $"[SessionCoordinator] Player prefab has no " +
                    $"NetworkPlayerPresenceController. It was spawned, but " +
                    $"central gameplay-presence gating is unavailable. " +
                    $"ClientID={clientId}",
                    playerObject);
            }
        }

        private int FindConnectedClientIndex(ulong clientId)
        {
            IReadOnlyList<ulong> connectedClientIds =
                networkManager.ConnectedClientsIds;

            for (int i = 0; i < connectedClientIds.Count; i++)
            {
                if (connectedClientIds[i] == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsGameplaySceneEvent(SceneEvent sceneEvent)
        {
            if (string.Equals(
                    sceneEvent.SceneName,
                    Settings.GameplaySceneName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            if (sceneEvent.SceneEventType != SceneEventType.Synchronize &&
                sceneEvent.SceneEventType != SceneEventType.SynchronizeComplete)
            {
                return false;
            }

            Scene gameplayScene = SceneManager.GetSceneByName(
                Settings.GameplaySceneName);
            return gameplayScene.IsValid() && gameplayScene.isLoaded;
        }

        private NetworkObject SpawnPlayerObject(
            ulong clientId,
            Vector3 position,
            Quaternion rotation)
        {
            GameObject playerPrefab = networkManager != null &&
                                      networkManager.NetworkConfig != null
                ? networkManager.NetworkConfig.PlayerPrefab
                : null;

            if (playerPrefab == null)
            {
                Debug.LogError(
                    $"[SessionCoordinator] PlayerPrefab is missing; cannot spawn " +
                    $"ClientID={clientId}.",
                    this);
                return null;
            }

            GameObject instance = Instantiate(
                playerPrefab,
                position,
                rotation);

            if (!instance.TryGetComponent(out NetworkObject networkObject))
            {
                Debug.LogError(
                    "[SessionCoordinator] PlayerPrefab has no NetworkObject.",
                    instance);
                Destroy(instance);
                return null;
            }

            try
            {
                // False keeps the player across future network-managed level
                // changes. Normal NGO shutdown still despawns it.
                networkObject.SpawnAsPlayerObject(clientId, false);
                Log($"Spawned gameplay PlayerObject for ClientID={clientId}.");
                return networkObject;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[SessionCoordinator] Could not spawn PlayerObject for " +
                    $"ClientID={clientId}: {exception.Message}",
                    this);
                Destroy(instance);
                return null;
            }
        }

        private void RollBackGameplayStart(string message)
        {
            _pendingGameplayLoad = false;

            if (Mode == GameSessionMode.CoopHost &&
                lobbyService != null &&
                lobbyService.IsInLobby &&
                lobbyService.IsLobbyOwner)
            {
                lobbyService.SetLobbyState(SteamLobbyState.Waiting, true);
                SetState(GameSessionState.Lobby, message);
                Reject(message);
                return;
            }

            Fail(message);

            if (flowController != null && flowController.IsConnected)
            {
                flowController.Disconnect();
            }
        }

        private bool ValidateGameplayScene()
        {
            if (!ValidateSetup(false))
            {
                return Reject(
                    "Session coordinator references are incomplete.");
            }

            if (!Application.CanStreamedLevelBeLoaded(Settings.GameplaySceneName))
            {
                return Reject(
                    $"Scene '{Settings.GameplaySceneName}' is not available. " +
                    "Add it to the Build Profile scene list.");
            }

            return true;
        }
        
        private bool ValidateGameSessionLoadingScene()
        {
            if (string.IsNullOrWhiteSpace(gameSessionLoadingSceneName))
            {
                return Reject("Game Session Loading scene name is empty.");
            }

            if (!Application.CanStreamedLevelBeLoaded(gameSessionLoadingSceneName))
            {
                return Reject(
                    $"Scene '{gameSessionLoadingSceneName}' is not available. " +
                    "Add it to the Build Profile scene list.");
            }

            return true;
        }

        private bool ValidateMainMenuScene()
        {
            if (string.IsNullOrWhiteSpace(Settings.MainMenuSceneName))
            {
                return Reject("Main Menu scene name is empty.");
            }

            if (!Application.CanStreamedLevelBeLoaded(Settings.MainMenuSceneName))
            {
                return Reject(
                    $"Scene '{Settings.MainMenuSceneName}' is not available. " +
                    "Add it to the Build Profile scene list.");
            }

            return true;
        }

        private void TryCompleteReturnToMainMenu()
        {
            if (!_returnToMainMenuRequested || !IsSessionCleanupComplete())
            {
                return;
            }

            _returnToMainMenuRequested = false;
            _pendingGameplayLoad = false;
            _disconnectAfterLeaveNotice = false;
            _waitingForLatePlayerObject = false;
            Mode = GameSessionMode.None;

            Time.timeScale = 1f;
            GameplayInputGate.ClearAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Log(
                $"Loading local main menu scene '{Settings.MainMenuSceneName}'.");
            SceneManager.LoadScene(
                Settings.MainMenuSceneName,
                LoadSceneMode.Single);
            SetState(GameSessionState.Idle, "Idle");
        }

        private bool TryScheduleIntentionalClientDisconnect()
        {
            if (flowController == null ||
                !flowController.IsClient ||
                networkManager == null ||
                networkManager.LocalClient == null ||
                networkManager.LocalClient.PlayerObject == null)
            {
                return false;
            }

            NetworkPlayerIdentity identity =
                networkManager.LocalClient.PlayerObject.GetComponent<
                    NetworkPlayerIdentity>();

            if (identity == null || !identity.NotifyIntentionalSessionLeave())
            {
                return false;
            }

            if (Settings.GracefulExitSlotBehaviour ==
                GracefulExitSlotPolicy.ReleaseSlot)
            {
                MultiplayerLastSessionStore.Forget();
            }

            _disconnectAfterLeaveNotice = true;
            _disconnectAfterLeaveNoticeTime = Time.unscaledTime + 0.15f;
            SetState(
                GameSessionState.ReturningToMainMenu,
                "Updating session slot...");
            return true;
        }

        private void ProgressIntentionalDisconnectNotice()
        {
            if (!_disconnectAfterLeaveNotice ||
                Time.unscaledTime < _disconnectAfterLeaveNoticeTime)
            {
                return;
            }

            _disconnectAfterLeaveNotice = false;

            if (flowController != null && flowController.IsConnected)
            {
                flowController.Disconnect();
            }
        }

        private bool IsSessionCleanupComplete()
        {
            bool networkStopped = networkManager == null ||
                                  (!networkManager.IsListening &&
                                   !networkManager.IsServer &&
                                   !networkManager.IsClient &&
                                   !networkManager.ShutdownInProgress);

            bool lobbyStopped = lobbyService == null ||
                                (!lobbyService.IsInLobby &&
                                 !lobbyService.IsBusy);

            return networkStopped && lobbyStopped && IsFlowStopped();
        }

        private bool IsFlowStopped()
        {
            return flowController == null ||
                   flowController.State == MultiplayerFlowState.Idle ||
                   flowController.State == MultiplayerFlowState.Failed;
        }

        private bool IsGameplaySceneActive()
        {
            return string.Equals(
                SceneManager.GetActiveScene().name,
                Settings.GameplaySceneName,
                StringComparison.Ordinal);
        }

        private void SynchronizeWithFlow()
        {
            if (flowController == null || !flowController.IsConnected)
            {
                return;
            }

            HandleSessionStarted(flowController.Role);
        }

        private void ResolveReferences()
        {
            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (flowController == null)
            {
                flowController = GetComponent<MultiplayerFlowController>();
            }

            if (lobbyService == null)
            {
                lobbyService = GetComponent<SteamLobbyService>();
            }
        }

        private void SubscribeToFlowEvents()
        {
            if (_flowEventsSubscribed || flowController == null)
            {
                return;
            }

            flowController.SessionStarted += HandleSessionStarted;
            flowController.SessionEnded += HandleSessionEnded;
            flowController.FlowFailed += HandleFlowFailed;
            flowController.StateChanged += HandleFlowStateChanged;
            _flowEventsSubscribed = true;
        }

        private void UnsubscribeFromFlowEvents()
        {
            if (!_flowEventsSubscribed || flowController == null)
            {
                return;
            }

            flowController.SessionStarted -= HandleSessionStarted;
            flowController.SessionEnded -= HandleSessionEnded;
            flowController.FlowFailed -= HandleFlowFailed;
            flowController.StateChanged -= HandleFlowStateChanged;
            _flowEventsSubscribed = false;
        }

        private void TrySubscribeToSceneEvents()
        {
            NetworkSceneManager sceneManager =
                networkManager != null ? networkManager.SceneManager : null;

            if (_subscribedSceneManager == sceneManager)
            {
                return;
            }

            UnsubscribeFromSceneEvents();

            if (sceneManager == null)
            {
                return;
            }

            _subscribedSceneManager = sceneManager;
            _subscribedSceneManager.OnSceneEvent += HandleSceneEvent;
        }

        private void UnsubscribeFromSceneEvents()
        {
            if (_subscribedSceneManager != null)
            {
                _subscribedSceneManager.OnSceneEvent -= HandleSceneEvent;
                _subscribedSceneManager = null;
            }
        }

        private void SetState(GameSessionState state, string message)
        {
            bool stateChanged = State != state;
            bool statusChanged = !string.Equals(
                StatusMessage,
                message,
                StringComparison.Ordinal);

            State = state;
            StatusMessage = message ?? string.Empty;

            if (stateChanged)
            {
                Log($"State -> {state}");
                StateChanged?.Invoke(state);
            }

            if (statusChanged)
            {
                Log(StatusMessage);
                StatusChanged?.Invoke(StatusMessage);
            }
        }

        private bool Reject(string message)
        {
            LastError = message ?? string.Empty;
            Debug.LogWarning($"[SessionCoordinator] {LastError}", this);
            OperationFailed?.Invoke(LastError);
            return false;
        }

        private bool Fail(string message, bool changeState = true)
        {
            LastError = string.IsNullOrWhiteSpace(message)
                ? "Session operation failed."
                : message;

            if (changeState)
            {
                SetState(GameSessionState.Failed, LastError);
            }

            Debug.LogError($"[SessionCoordinator] {LastError}", this);
            OperationFailed?.Invoke(LastError);
            return false;
        }

        private void Log(string message)
        {
            if (Settings.VerboseLogging)
            {
                Debug.Log($"[SessionCoordinator] {message}", this);
            }
        }
    }
}
