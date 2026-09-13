using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EXW.Multiplayer.UI
{
    public enum MainMenuMultiplayerScreen
    {
        Main,
        CoopBrowser,
        LobbyRoom
    }

    /// <summary>
    /// Scene-local UI adapter for the persistent Steam lobby + NGO runtime.
    /// It owns presentation state only; all networking decisions remain inside
    /// MultiplayerFlowController and SteamLobbyService.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/UI/Multiplayer Main Menu Presenter")]
    public sealed class MultiplayerMainMenuPresenter : MonoBehaviour
    {
        [Header("Screen Roots")]
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject coopPanel;
        [SerializeField] private GameObject createLobbyModal;
        [SerializeField] private GameObject lobbyRoomPanel;
        [SerializeField] private GameObject busyOverlay;
        [SerializeField] private GameObject failureOverlay;

        [Header("Main Panel")]
        [SerializeField] private Button playCoopButton;
        [SerializeField] private Button playSinglePlayerButton;
        [SerializeField] private TMP_Text playerNameText;

        [Header("Previous Session (Optional)")]
        [Tooltip(
            "Optional panel shown on Main when this Steam user still owns a " +
            "slot in the last observed running session.")]
        [SerializeField] private GameObject rejoinSessionPanel;
        [SerializeField] private TMP_Text rejoinSessionText;
        [SerializeField] private Button rejoinSessionButton;
        [SerializeField] private Button forgetSessionButton;

        [Header("CO-OP Browser")]
        [SerializeField] private Button openCreateLobbyButton;
        [SerializeField] private Button quickJoinButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private Button joinCodeButton;
        [SerializeField] private Button coopBackButton;
        [SerializeField] private TMP_InputField lobbyCodeInputField;
        [SerializeField] private Transform lobbyListContent;
        [SerializeField] private SteamLobbyListEntryView lobbyEntryTemplate;
        [SerializeField] private TMP_Text noLobbiesText;

        [Header("Create Lobby Modal")]
        [SerializeField] private TMP_InputField lobbyNameInputField;
        [Tooltip(
            "Optional. If assigned, this per-lobby value overrides " +
            "MultiplayerSettings.DefaultAllowLateJoin.")]
        [SerializeField] private Toggle allowLateJoinToggle;
        [SerializeField] private Button createPublicButton;
        [SerializeField] private Button createPrivateButton;
        [SerializeField] private Button createModalBackButton;

        [Header("Lobby Room")]
        [SerializeField] private TMP_Text roomLobbyNameText;
        [SerializeField] private TMP_Text roomLobbyCodeText;
        [SerializeField] private TMP_Text roomMemberCountText;
        [SerializeField] private TMP_Text roomMembersText;
        [SerializeField] private Button inviteFriendsButton;
        [SerializeField] private Button startGameButton;
        [Tooltip("Optional label shown to clients while only the host can start.")]
        [SerializeField] private TMP_Text waitingForHostText;
        [SerializeField] private Button leaveLobbyButton;

        [Header("Operation Overlays")]
        [SerializeField] private TMP_Text busyMessageText;
        [SerializeField] private Button busyCancelButton;
        [SerializeField] private TMP_Text failureMessageText;
        [SerializeField] private Button failureBackButton;

        [Header("Behaviour")]
        [Tooltip("Automatically requests the Steam lobby list when CO-OP is opened.")]
        [SerializeField] private bool refreshWhenCoopPanelOpens = true;

        [Tooltip("Adds (Host) and (You) markers to the room member list.")]
        [SerializeField] private bool decorateMemberNames = true;

        [SerializeField] private bool verboseLogging;

        private readonly List<SteamLobbyListEntryView> _spawnedLobbyEntries =
            new List<SteamLobbyListEntryView>();

        private MultiplayerFlowController _flowController;
        private SteamLobbyService _lobbyService;
        private MultiplayerSessionCoordinator _sessionCoordinator;
        private MainMenuMultiplayerScreen _currentScreen =
            MainMenuMultiplayerScreen.Main;

        private MultiplayerFlowState _lastFlowState = MultiplayerFlowState.Idle;
        private bool _buttonsBound;
        private bool _runtimeBound;
        private bool _returnToBrowserAfterDisconnect;
        private float _nextRuntimeBindAttempt;
        private MultiplayerLastSessionRecord _lastSessionRecord;

        public MainMenuMultiplayerScreen CurrentScreen => _currentScreen;
        public bool RuntimeBound => _runtimeBound;
        public bool HasRejoinableSession => _lastSessionRecord != null;

        private void Reset()
        {
            AutoWireKnownHierarchy();
        }

        private void Awake()
        {
            if (lobbyEntryTemplate != null)
            {
                lobbyEntryTemplate.gameObject.SetActive(false);
            }

            Hide(createLobbyModal);
            Hide(busyOverlay);
            Hide(failureOverlay);
            ShowBaseScreen(MainMenuMultiplayerScreen.Main);
        }

        private void OnEnable()
        {
            BindButtons();
            TryBindRuntime();
            RefreshAllViews();
        }

        private void Update()
        {
            bool runtimeChanged =
                _runtimeBound &&
                (_flowController != MultiplayerFlowController.Instance ||
                 _lobbyService != SteamLobbyService.Instance ||
                 _sessionCoordinator != MultiplayerSessionCoordinator.Instance);

            if (runtimeChanged)
            {
                UnbindRuntime();
            }

            if (!_runtimeBound && Time.unscaledTime >= _nextRuntimeBindAttempt)
            {
                _nextRuntimeBindAttempt = Time.unscaledTime + 0.25f;
                TryBindRuntime();
            }
        }

        private void OnDisable()
        {
            UnbindRuntime();
            UnbindButtons();
        }

        private void OnDestroy()
        {
            ClearSpawnedLobbyEntries();
        }

        public void OpenCoopPanel()
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            if (!SteamBootstrap.IsSteamAvailable || !_lobbyService.IsReady)
            {
                ShowFailure(
                    string.IsNullOrWhiteSpace(SteamBootstrap.InitializationError)
                        ? "Steam is not available. CO-OP cannot be started."
                        : SteamBootstrap.InitializationError);
                return;
            }

            ShowBaseScreen(MainMenuMultiplayerScreen.CoopBrowser);

            if (refreshWhenCoopPanelOpens && _flowController.IsIdle)
            {
                _flowController.RefreshLobbyList();
            }
        }

        public void PlaySinglePlayer()
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            SetBusy(true, "Starting singleplayer...");

            if (!_sessionCoordinator.StartSinglePlayer())
            {
                SetBusy(false, string.Empty);
            }
        }

        public void ReturnToMainPanel()
        {
            if (_flowController != null &&
                (_flowController.IsBusy || _flowController.IsConnected))
            {
                return;
            }

            Hide(createLobbyModal);
            Hide(failureOverlay);
            ShowBaseScreen(MainMenuMultiplayerScreen.Main);
        }

        public void RejoinLastSession()
        {
            if (!CanStartLobbyOperation())
            {
                return;
            }

            if (!_flowController.RejoinLastSession() &&
                !string.IsNullOrWhiteSpace(_flowController.LastError))
            {
                ShowFailure(_flowController.LastError);
            }
        }

        public void ForgetLastSession()
        {
            MultiplayerLastSessionStore.Forget();
            RefreshLastSessionView();
        }

        public void OpenCreateLobbyModal()
        {
            if (!CanStartLobbyOperation())
            {
                return;
            }

            Show(createLobbyModal);

            if (allowLateJoinToggle != null)
            {
                allowLateJoinToggle.SetIsOnWithoutNotify(
                    MultiplayerSettings.Current.DefaultAllowLateJoin);
            }

            if (lobbyNameInputField != null)
            {
                lobbyNameInputField.Select();
                lobbyNameInputField.ActivateInputField();
            }
        }

        public void CloseCreateLobbyModal()
        {
            Hide(createLobbyModal);
        }

        public void CreatePublicLobby()
        {
            CreateLobby(SteamLobbyVisibility.Public);
        }

        public void CreatePrivateLobby()
        {
            CreateLobby(SteamLobbyVisibility.Private);
        }

        public void QuickJoin()
        {
            if (CanStartLobbyOperation())
            {
                _flowController.QuickJoin();
            }
        }

        public void RefreshLobbies()
        {
            if (CanStartLobbyOperation())
            {
                _flowController.RefreshLobbyList();
            }
        }

        public void JoinEnteredCode()
        {
            if (!CanStartLobbyOperation())
            {
                return;
            }

            string code = lobbyCodeInputField != null
                ? lobbyCodeInputField.text
                : string.Empty;

            if (!SteamLobbyJoinCode.TryDecode(
                    code,
                    out ulong lobbyId,
                    out string error))
            {
                ShowFailure(error);
                return;
            }

            _flowController.JoinLobby(lobbyId);
        }

        public void InviteFriends()
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            if (!_flowController.IsConnected || !_lobbyService.IsInLobby)
            {
                ShowFailure("Join a lobby before inviting Steam friends.");
                return;
            }

            // Steam permits every lobby member to invite friends. The existing
            // flow facade intentionally exposes a host-only helper, so this
            // presentation action calls the lobby service's validated API.
            _lobbyService.OpenInviteOverlay();
        }

        public void StartGame()
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            if (!_sessionCoordinator.CanHostStartGame)
            {
                ShowFailure("Only the lobby host can start the game.");
                return;
            }

            if (_sessionCoordinator.StartCoopGame())
            {
                SetBusy(true, _sessionCoordinator.StatusMessage);
            }
        }

        public void LeaveLobby()
        {
            if (!EnsureRuntimeReady())
            {
                return;
            }

            _returnToBrowserAfterDisconnect = true;
            _flowController.Disconnect();
        }

        public void CancelBusyOperation()
        {
            if (_flowController == null ||
                _flowController.State == MultiplayerFlowState.Disconnecting)
            {
                return;
            }

            _returnToBrowserAfterDisconnect = true;
            _flowController.Disconnect();
        }

        public void DismissFailure()
        {
            Hide(failureOverlay);

            if (_flowController != null &&
                _flowController.State == MultiplayerFlowState.Failed)
            {
                _flowController.ClearFailure();
            }

            if (_sessionCoordinator != null)
            {
                _sessionCoordinator.ClearFailure();
            }

            if (_currentScreen == MainMenuMultiplayerScreen.LobbyRoom &&
                (_flowController == null || !_flowController.IsConnected))
            {
                ShowBaseScreen(MainMenuMultiplayerScreen.CoopBrowser);
            }

            RefreshControlAvailability();
        }

        public bool ValidateReferences(bool logResult = true)
        {
            List<string> missing = new List<string>();

            Require(mainPanel, nameof(mainPanel), missing);
            Require(coopPanel, nameof(coopPanel), missing);
            Require(createLobbyModal, nameof(createLobbyModal), missing);
            Require(lobbyRoomPanel, nameof(lobbyRoomPanel), missing);
            Require(busyOverlay, nameof(busyOverlay), missing);
            Require(failureOverlay, nameof(failureOverlay), missing);

            Require(playCoopButton, nameof(playCoopButton), missing);
            Require(playSinglePlayerButton, nameof(playSinglePlayerButton), missing);
            Require(openCreateLobbyButton, nameof(openCreateLobbyButton), missing);
            Require(quickJoinButton, nameof(quickJoinButton), missing);
            Require(refreshButton, nameof(refreshButton), missing);
            Require(joinCodeButton, nameof(joinCodeButton), missing);
            Require(coopBackButton, nameof(coopBackButton), missing);
            Require(lobbyCodeInputField, nameof(lobbyCodeInputField), missing);
            Require(lobbyListContent, nameof(lobbyListContent), missing);
            Require(lobbyEntryTemplate, nameof(lobbyEntryTemplate), missing);

            Require(lobbyNameInputField, nameof(lobbyNameInputField), missing);
            Require(createPublicButton, nameof(createPublicButton), missing);
            Require(createPrivateButton, nameof(createPrivateButton), missing);
            Require(createModalBackButton, nameof(createModalBackButton), missing);

            Require(roomLobbyNameText, nameof(roomLobbyNameText), missing);
            Require(roomMembersText, nameof(roomMembersText), missing);
            Require(inviteFriendsButton, nameof(inviteFriendsButton), missing);
            Require(startGameButton, nameof(startGameButton), missing);
            Require(leaveLobbyButton, nameof(leaveLobbyButton), missing);

            Require(busyMessageText, nameof(busyMessageText), missing);
            Require(busyCancelButton, nameof(busyCancelButton), missing);
            Require(failureMessageText, nameof(failureMessageText), missing);
            Require(failureBackButton, nameof(failureBackButton), missing);

            if (rejoinSessionPanel != null)
            {
                Require(rejoinSessionText, nameof(rejoinSessionText), missing);
                Require(rejoinSessionButton, nameof(rejoinSessionButton), missing);
                Require(forgetSessionButton, nameof(forgetSessionButton), missing);
            }

            bool valid = missing.Count == 0;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log(
                        "[MainMenuUI] PASS: all required references are assigned.",
                        this);
                }
                else
                {
                    Debug.LogError(
                        "[MainMenuUI] Missing references:\n- " +
                        string.Join("\n- ", missing),
                        this);
                }
            }

            return valid;
        }

        /// <summary>
        /// Best-effort editor convenience for the hierarchy names documented in
        /// README. It never searches for the persistent runtime because scene UI
        /// resolves that runtime through its singleton instances at Play time.
        /// </summary>
        public void AutoWireKnownHierarchy()
        {
            mainPanel = FindObject(mainPanel, transform, "MainPanel");
            coopPanel = FindObject(coopPanel, transform, "CoopPanel");
            createLobbyModal = FindObject(
                createLobbyModal,
                transform,
                "CreateLobbyModal");
            lobbyRoomPanel = FindObject(lobbyRoomPanel, transform, "LobbyRoomPanel");
            busyOverlay = FindObject(busyOverlay, transform, "BusyOverlay");
            failureOverlay = FindObject(failureOverlay, transform, "FailureOverlay");

            playCoopButton = FindComponent(
                playCoopButton,
                RootOf(mainPanel),
                "Btn_COOP",
                "Btn_Coop");
            playSinglePlayerButton = FindComponent(
                playSinglePlayerButton,
                RootOf(mainPanel),
                "Btn_Single",
                "Btn_SinglePlayer");
            playerNameText = FindComponent(
                playerNameText,
                RootOf(mainPanel),
                "PlayerNameText",
                "Txt_PlayerName");

            rejoinSessionPanel = FindObject(
                rejoinSessionPanel,
                RootOf(mainPanel),
                "RejoinSessionPanel",
                "LastSessionPanel");
            rejoinSessionText = FindComponent(
                rejoinSessionText,
                RootOf(rejoinSessionPanel),
                "RejoinSessionText",
                "LastSessionText",
                "Txt_LastSession");
            rejoinSessionButton = FindComponent(
                rejoinSessionButton,
                RootOf(rejoinSessionPanel),
                "Btn_RejoinSession",
                "Btn_Rejoin");
            forgetSessionButton = FindComponent(
                forgetSessionButton,
                RootOf(rejoinSessionPanel),
                "Btn_ForgetSession",
                "Btn_Forget");

            openCreateLobbyButton = FindComponent(
                openCreateLobbyButton,
                RootOf(coopPanel),
                "Btn_CreateLobby");
            quickJoinButton = FindComponent(
                quickJoinButton,
                RootOf(coopPanel),
                "Btn_QuickJoin");
            refreshButton = FindComponent(
                refreshButton,
                RootOf(coopPanel),
                "Btn_Refresh");
            joinCodeButton = FindComponent(
                joinCodeButton,
                RootOf(coopPanel),
                "Btn_JoinCode");
            coopBackButton = FindComponent(
                coopBackButton,
                RootOf(coopPanel),
                "Btn_Back");
            lobbyCodeInputField = FindComponent(
                lobbyCodeInputField,
                RootOf(coopPanel),
                "LobbyCodeInputField");
            noLobbiesText = FindComponent(
                noLobbiesText,
                RootOf(coopPanel),
                "NoLobbiesText",
                "Txt_NoLobbies");

            if (lobbyListContent == null && coopPanel != null)
            {
                ScrollRect scrollRect = coopPanel.GetComponentInChildren<ScrollRect>(true);
                lobbyListContent = scrollRect != null ? scrollRect.content : null;
            }

            if (lobbyEntryTemplate == null && lobbyListContent != null)
            {
                lobbyEntryTemplate =
                    lobbyListContent.GetComponentInChildren<SteamLobbyListEntryView>(true);
            }

            lobbyNameInputField = FindComponent(
                lobbyNameInputField,
                RootOf(createLobbyModal),
                "LobbyNameInputField");
            allowLateJoinToggle = FindComponent(
                allowLateJoinToggle,
                RootOf(createLobbyModal),
                "AllowLateJoinToggle",
                "Toggle_AllowLateJoin");
            createPublicButton = FindComponent(
                createPublicButton,
                RootOf(createLobbyModal),
                "Btn_CreatePublic",
                "Btn_Public");
            createPrivateButton = FindComponent(
                createPrivateButton,
                RootOf(createLobbyModal),
                "Btn_CreatePrivate",
                "Btn_Private");
            createModalBackButton = FindComponent(
                createModalBackButton,
                RootOf(createLobbyModal),
                "Btn_Back",
                "Btn_Close");

            roomLobbyNameText = FindComponent(
                roomLobbyNameText,
                RootOf(lobbyRoomPanel),
                "LobbyNameText",
                "Txt_LobbyName");
            roomLobbyCodeText = FindComponent(
                roomLobbyCodeText,
                RootOf(lobbyRoomPanel),
                "LobbyCodeText",
                "Txt_LobbyCode");
            roomMemberCountText = FindComponent(
                roomMemberCountText,
                RootOf(lobbyRoomPanel),
                "MemberCountText",
                "Txt_MemberCount");
            roomMembersText = FindComponent(
                roomMembersText,
                RootOf(lobbyRoomPanel),
                "LobbyMembersText",
                "MembersText",
                "Txt_Members");
            inviteFriendsButton = FindComponent(
                inviteFriendsButton,
                RootOf(lobbyRoomPanel),
                "Btn_InviteFriends",
                "Btn_Invite");
            startGameButton = FindComponent(
                startGameButton,
                RootOf(lobbyRoomPanel),
                "Btn_StartGame",
                "Btn_Start");
            waitingForHostText = FindComponent(
                waitingForHostText,
                RootOf(lobbyRoomPanel),
                "WaitingForHostText",
                "Txt_WaitingForHost");
            leaveLobbyButton = FindComponent(
                leaveLobbyButton,
                RootOf(lobbyRoomPanel),
                "Btn_LeaveLobby",
                "Btn_Leave");

            busyMessageText = FindComponent(
                busyMessageText,
                RootOf(busyOverlay),
                "BusyMessage",
                "Txt_BusyMessage");
            busyCancelButton = FindComponent(
                busyCancelButton,
                RootOf(busyOverlay),
                "Btn_Back",
                "Btn_Cancel");

            failureMessageText = FindComponent(
                failureMessageText,
                RootOf(failureOverlay),
                "FailureMessage",
                "FailedMessage",
                "Txt_FailureMessage");
            failureBackButton = FindComponent(
                failureBackButton,
                RootOf(failureOverlay),
                "Btn_Back",
                "Btn_Close");
        }

        public void LogCurrentUiState()
        {
            Debug.Log(
                $"[MainMenuUI] Screen={_currentScreen} | " +
                $"RuntimeBound={_runtimeBound} | Steam={SteamBootstrap.IsSteamAvailable} | " +
                $"Flow={(_flowController != null ? _flowController.State.ToString() : "None")} | " +
                $"Session={(_sessionCoordinator != null ? _sessionCoordinator.State.ToString() : "None")} | " +
                $"LobbyID={(_lobbyService != null ? _lobbyService.CurrentLobbyId : 0UL)}",
                this);
        }

        private void CreateLobby(SteamLobbyVisibility visibility)
        {
            if (!CanStartLobbyOperation())
            {
                return;
            }

            string lobbyName = lobbyNameInputField != null
                ? lobbyNameInputField.text
                : string.Empty;

            bool? allowLateJoinOverride = allowLateJoinToggle != null
                ? allowLateJoinToggle.isOn
                : (bool?)null;

            Hide(createLobbyModal);
            _flowController.CreateLobby(
                lobbyName,
                visibility,
                allowLateJoinOverride);
        }

        private bool CanStartLobbyOperation()
        {
            if (!EnsureRuntimeReady())
            {
                return false;
            }

            if (!SteamBootstrap.IsSteamAvailable || !_lobbyService.IsReady)
            {
                ShowFailure("Steam is not ready. Please try again in a moment.");
                return false;
            }

            if (!_flowController.IsIdle)
            {
                ShowFailure(
                    string.IsNullOrWhiteSpace(_flowController.StatusMessage)
                        ? "Another multiplayer operation is already running."
                        : _flowController.StatusMessage);
                return false;
            }

            return true;
        }

        private bool EnsureRuntimeReady()
        {
            if (!_runtimeBound)
            {
                TryBindRuntime();
            }

            if (_runtimeBound)
            {
                return true;
            }

            ShowFailure(
                "Multiplayer runtime was not found. Start the game from LoadingScene.");
            return false;
        }

        private bool TryBindRuntime()
        {
            MultiplayerFlowController flow = MultiplayerFlowController.Instance;
            SteamLobbyService lobby = SteamLobbyService.Instance;
            MultiplayerSessionCoordinator session =
                MultiplayerSessionCoordinator.Instance;

            if (flow == null || lobby == null || session == null)
            {
                return false;
            }

            if (_runtimeBound &&
                _flowController == flow &&
                _lobbyService == lobby &&
                _sessionCoordinator == session)
            {
                return true;
            }

            UnbindRuntime();

            _flowController = flow;
            _lobbyService = lobby;
            _sessionCoordinator = session;

            _flowController.StateChanged += HandleFlowStateChanged;
            _flowController.StatusChanged += HandleStatusChanged;
            _flowController.FlowFailed += HandleFlowFailed;
            _flowController.OperationRejected += HandleOperationRejected;
            _flowController.CurrentLobbyUpdated += HandleCurrentLobbyUpdated;
            _flowController.LobbyListUpdated += HandleLobbyListUpdated;
            _flowController.LobbyMembersChanged += HandleLobbyMembersChanged;
            _flowController.SessionEnded += HandleSessionEnded;
            _lobbyService.ServiceReady += HandleLobbyServiceReady;
            _sessionCoordinator.StateChanged += HandleSessionStateChanged;
            _sessionCoordinator.StatusChanged += HandleSessionStatusChanged;
            _sessionCoordinator.OperationFailed += HandleSessionOperationFailed;

            _runtimeBound = true;
            _lastFlowState = _flowController.State;

            if (verboseLogging)
            {
                Debug.Log("[MainMenuUI] Persistent multiplayer runtime bound.", this);
            }

            RefreshAllViews();
            ApplyFlowState(_flowController.State);
            return true;
        }

        private void UnbindRuntime()
        {
            if (_flowController != null)
            {
                _flowController.StateChanged -= HandleFlowStateChanged;
                _flowController.StatusChanged -= HandleStatusChanged;
                _flowController.FlowFailed -= HandleFlowFailed;
                _flowController.OperationRejected -= HandleOperationRejected;
                _flowController.CurrentLobbyUpdated -= HandleCurrentLobbyUpdated;
                _flowController.LobbyListUpdated -= HandleLobbyListUpdated;
                _flowController.LobbyMembersChanged -= HandleLobbyMembersChanged;
                _flowController.SessionEnded -= HandleSessionEnded;
            }

            if (_lobbyService != null)
            {
                _lobbyService.ServiceReady -= HandleLobbyServiceReady;
            }

            if (_sessionCoordinator != null)
            {
                _sessionCoordinator.StateChanged -= HandleSessionStateChanged;
                _sessionCoordinator.StatusChanged -= HandleSessionStatusChanged;
                _sessionCoordinator.OperationFailed -= HandleSessionOperationFailed;
            }

            _runtimeBound = false;
            _flowController = null;
            _lobbyService = null;
            _sessionCoordinator = null;
        }

        private void HandleFlowStateChanged(MultiplayerFlowState state)
        {
            ApplyFlowState(state);
            _lastFlowState = state;
        }

        private void HandleStatusChanged(string message)
        {
            if (_flowController != null && _flowController.IsBusy)
            {
                SetBusy(true, message);
            }
        }

        private void HandleFlowFailed(string message)
        {
            SetBusy(false, string.Empty);
            ShowBaseScreen(MainMenuMultiplayerScreen.CoopBrowser);
            ShowFailure(message);
        }

        private void HandleOperationRejected(string message)
        {
            ShowFailure(message);
        }

        private void HandleCurrentLobbyUpdated(SteamLobbySummary lobby)
        {
            RenderCurrentLobby();
            RefreshControlAvailability();
        }

        private void HandleLobbyListUpdated(
            IReadOnlyList<SteamLobbySummary> lobbies)
        {
            RenderLobbyList(lobbies);
        }

        private void HandleLobbyMembersChanged(
            IReadOnlyList<SteamLobbyMember> members)
        {
            RenderCurrentLobby();
        }

        private void HandleSessionEnded(MultiplayerSessionRole role)
        {
            _returnToBrowserAfterDisconnect = true;
        }

        private void HandleSessionStateChanged(GameSessionState state)
        {
            switch (state)
            {
                case GameSessionState.StartingSinglePlayer:
                case GameSessionState.LoadingGameplay:
                case GameSessionState.ReturningToMainMenu:
                    Hide(failureOverlay);
                    SetBusy(true, _sessionCoordinator.StatusMessage);
                    break;

                case GameSessionState.Lobby:
                    SetBusy(false, string.Empty);
                    ShowBaseScreen(MainMenuMultiplayerScreen.LobbyRoom);
                    RenderCurrentLobby();
                    break;

                case GameSessionState.Gameplay:
                    SetBusy(false, string.Empty);
                    break;

                case GameSessionState.Failed:
                    SetBusy(false, string.Empty);
                    ShowFailure(_sessionCoordinator.LastError);
                    break;
            }

            RefreshControlAvailability();
        }

        private void HandleSessionStatusChanged(string message)
        {
            if (_sessionCoordinator != null && _sessionCoordinator.IsLoading)
            {
                SetBusy(true, message);
            }
        }

        private void HandleSessionOperationFailed(string message)
        {
            SetBusy(false, string.Empty);
            ShowFailure(message);
        }

        private void HandleLobbyServiceReady()
        {
            RefreshAllViews();
        }

        private void ApplyFlowState(MultiplayerFlowState state)
        {
            switch (state)
            {
                case MultiplayerFlowState.Connected:
                    _returnToBrowserAfterDisconnect = false;
                    Hide(failureOverlay);

                    if (_flowController.IsSinglePlayer)
                    {
                        SetBusy(
                            true,
                            _sessionCoordinator != null
                                ? _sessionCoordinator.StatusMessage
                                : "Loading gameplay...");
                    }
                    else
                    {
                        SetBusy(false, string.Empty);
                        ShowBaseScreen(MainMenuMultiplayerScreen.LobbyRoom);
                        RenderCurrentLobby();
                    }
                    break;

                case MultiplayerFlowState.Failed:
                    SetBusy(false, string.Empty);
                    ShowBaseScreen(MainMenuMultiplayerScreen.CoopBrowser);
                    ShowFailure(
                        string.IsNullOrWhiteSpace(_flowController.LastError)
                            ? "Multiplayer operation failed."
                            : _flowController.LastError);
                    break;

                case MultiplayerFlowState.Idle:
                    SetBusy(false, string.Empty);

                    if (_returnToBrowserAfterDisconnect ||
                        _lastFlowState == MultiplayerFlowState.Disconnecting ||
                        _currentScreen == MainMenuMultiplayerScreen.LobbyRoom)
                    {
                        _returnToBrowserAfterDisconnect = false;
                        ShowBaseScreen(MainMenuMultiplayerScreen.CoopBrowser);
                    }
                    break;

                case MultiplayerFlowState.CreatingLobby:
                case MultiplayerFlowState.SearchingLobbies:
                case MultiplayerFlowState.JoiningLobby:
                case MultiplayerFlowState.StartingHost:
                case MultiplayerFlowState.ConnectingClient:
                case MultiplayerFlowState.Disconnecting:
                    Hide(createLobbyModal);
                    Hide(failureOverlay);
                    SetBusy(true, _flowController.StatusMessage);
                    break;
            }

            RefreshControlAvailability();
        }

        private void RefreshAllViews()
        {
            RefreshPlayerName();
            RefreshLastSessionView();

            if (_lobbyService != null)
            {
                RenderLobbyList(_lobbyService.AvailableLobbies);
                RenderCurrentLobby();
            }
            else
            {
                RenderLobbyList(Array.Empty<SteamLobbySummary>());
            }

            RefreshControlAvailability();
        }

        private void RefreshLastSessionView()
        {
            _lastSessionRecord = null;
            MultiplayerSettings settings = MultiplayerSettings.Current;
            bool promptEnabled =
                settings.LastSessionRejoinBehaviour ==
                LastSessionRejoinMode.Prompt;

            if (promptEnabled)
            {
                MultiplayerLastSessionStore.TryLoadCompatible(
                    settings,
                    SteamBootstrap.LocalSteamId,
                    out _lastSessionRecord,
                    out _);
            }

            bool visible = _lastSessionRecord != null &&
                           _currentScreen ==
                           MainMenuMultiplayerScreen.Main;
            SetActive(rejoinSessionPanel, visible);

            if (rejoinSessionText == null || !visible)
            {
                return;
            }

            string lobbyName = string.IsNullOrWhiteSpace(
                _lastSessionRecord.LobbyName)
                ? "Previous Session"
                : _lastSessionRecord.LobbyName;

            rejoinSessionText.text =
                $"Rejoin {lobbyName}  •  Slot " +
                $"{_lastSessionRecord.SlotIndex + 1}";
        }

        private void RefreshPlayerName()
        {
            if (playerNameText == null)
            {
                return;
            }

            playerNameText.text = SteamBootstrap.IsSteamAvailable &&
                                  !string.IsNullOrWhiteSpace(
                                      SteamBootstrap.LocalPersonaName)
                ? SteamBootstrap.LocalPersonaName
                : "Offline";
        }

        private void RenderLobbyList(
            IReadOnlyList<SteamLobbySummary> lobbies)
        {
            ClearSpawnedLobbyEntries();

            if (lobbyEntryTemplate == null || lobbyListContent == null)
            {
                return;
            }

            lobbyEntryTemplate.gameObject.SetActive(false);
            int validLobbyCount = 0;

            if (lobbies != null)
            {
                for (int i = 0; i < lobbies.Count; i++)
                {
                    SteamLobbySummary lobby = lobbies[i];

                    if (lobby == null || lobby.LobbyId == 0)
                    {
                        continue;
                    }

                    SteamLobbyListEntryView entry = Instantiate(
                        lobbyEntryTemplate,
                        lobbyListContent);

                    entry.name = $"Lobby_{lobby.LobbyId}";
                    entry.Bind(lobby, HandleLobbyEntrySelected);
                    entry.gameObject.SetActive(true);
                    _spawnedLobbyEntries.Add(entry);
                    validLobbyCount++;
                }
            }

            if (noLobbiesText != null)
            {
                noLobbiesText.gameObject.SetActive(validLobbyCount == 0);
                noLobbiesText.text = "No compatible lobbies found.";
            }
        }

        private void RenderCurrentLobby()
        {
            SteamLobbySummary lobby =
                _lobbyService != null ? _lobbyService.CurrentLobby : null;

            if (roomLobbyNameText != null)
            {
                roomLobbyNameText.text = lobby != null
                    ? lobby.Name
                    : "Lobby";
            }

            if (roomLobbyCodeText != null)
            {
                string code = lobby != null
                    ? SteamLobbyJoinCode.Encode(lobby.LobbyId)
                    : string.Empty;

                roomLobbyCodeText.text = string.IsNullOrEmpty(code)
                    ? string.Empty
                    : $"#{code}";
            }

            IReadOnlyList<SteamLobbyMember> members =
                _lobbyService != null
                    ? _lobbyService.CurrentMembers
                    : Array.Empty<SteamLobbyMember>();

            int capacity = lobby != null
                ? Mathf.Max(1, lobby.MemberLimit)
                : 4;

            if (roomMemberCountText != null)
            {
                int displayedCount = members.Count > 0
                    ? members.Count
                    : lobby != null ? lobby.MemberCount : 0;

                roomMemberCountText.text = lobby != null &&
                                           lobby.HasSessionRosterMetadata &&
                                           lobby.State == SteamLobbyState.Playing
                    ? $"{displayedCount}/{capacity} Online  •  " +
                      $"{lobby.AssignedSessionSlots}/{capacity} Slots"
                    : $"{displayedCount}/{capacity} Players";
            }

            if (roomMembersText == null)
            {
                return;
            }

            if (lobby != null && lobby.HasSessionRosterMetadata)
            {
                roomMembersText.text = BuildSessionRosterText(
                    lobby,
                    members,
                    capacity);
                return;
            }

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < capacity; i++)
            {
                if (i > 0)
                {
                    builder.AppendLine();
                }

                if (i >= members.Count)
                {
                    builder.Append("<color=#777777>Empty</color>");
                    continue;
                }

                SteamLobbyMember member = members[i];
                string personaName = string.IsNullOrWhiteSpace(member.PersonaName)
                    ? $"Steam {member.SteamId}"
                    : member.PersonaName;

                builder.Append(personaName);

                if (decorateMemberNames)
                {
                    if (member.IsOwner)
                    {
                        builder.Append("  <color=#F5C451>(Host)</color>");
                    }

                    if (member.IsLocalPlayer)
                    {
                        builder.Append("  <color=#62C6FF>(You)</color>");
                    }
                }
            }

            roomMembersText.text = builder.ToString();
        }

        private string BuildSessionRosterText(
            SteamLobbySummary lobby,
            IReadOnlyList<SteamLobbyMember> members,
            int capacity)
        {
            StringBuilder builder = new StringBuilder();

            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                if (slotIndex > 0)
                {
                    builder.AppendLine();
                }

                SessionRosterSlotSnapshot slot =
                    FindSessionSlot(lobby.SessionSlots, slotIndex);

                if (slot == null)
                {
                    builder.Append("<color=#777777>Empty");

                    if (lobby.State == SteamLobbyState.Playing &&
                        lobby.AllowsLateJoin)
                    {
                        builder.Append(" (Open for Late Join)");
                    }

                    builder.Append("</color>");
                    continue;
                }

                SteamLobbyMember member = FindLobbyMember(
                    members,
                    slot.SteamId);
                bool isOnline = member != null;
                string personaName = ResolveRosterPersonaName(
                    slot.SteamId,
                    member);

                builder.Append(personaName);

                if (decorateMemberNames)
                {
                    if (slot.IsHost || slot.SteamId == lobby.HostSteamId)
                    {
                        builder.Append("  <color=#F5C451>(Host)</color>");
                    }

                    if (slot.SteamId == SteamBootstrap.LocalSteamId)
                    {
                        builder.Append("  <color=#62C6FF>(You)</color>");
                    }
                }

                if (lobby.State == SteamLobbyState.Playing)
                {
                    builder.Append(
                        isOnline
                            ? "  <color=#65D17A>Online</color>"
                            : "  <color=#9A9A9A>Offline — Slot Reserved</color>");
                }
            }

            return builder.ToString();
        }

        private static SessionRosterSlotSnapshot FindSessionSlot(
            IReadOnlyList<SessionRosterSlotSnapshot> slots,
            int slotIndex)
        {
            if (slots == null)
            {
                return null;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotIndex == slotIndex)
                {
                    return slots[i];
                }
            }

            return null;
        }

        private static SteamLobbyMember FindLobbyMember(
            IReadOnlyList<SteamLobbyMember> members,
            ulong steamId)
        {
            if (members == null)
            {
                return null;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (members[i].SteamId == steamId)
                {
                    return members[i];
                }
            }

            return null;
        }

        private static string ResolveRosterPersonaName(
            ulong steamId,
            SteamLobbyMember member)
        {
            if (member != null &&
                !string.IsNullOrWhiteSpace(member.PersonaName))
            {
                return member.PersonaName;
            }

            if (SteamBootstrap.IsSteamAvailable)
            {
                try
                {
                    string personaName = SteamFriends.GetFriendPersonaName(
                        new CSteamID(steamId));

                    if (!string.IsNullOrWhiteSpace(personaName))
                    {
                        return personaName;
                    }
                }
                catch
                {
                    // A readable SteamID fallback is enough for offline members.
                }
            }

            return $"Steam {steamId}";
        }

        private void HandleLobbyEntrySelected(SteamLobbySummary lobby)
        {
            if (lobby != null && CanStartLobbyOperation())
            {
                _flowController.JoinLobby(lobby.LobbyId);
            }
        }

        private void ClearSpawnedLobbyEntries()
        {
            for (int i = 0; i < _spawnedLobbyEntries.Count; i++)
            {
                SteamLobbyListEntryView entry = _spawnedLobbyEntries[i];

                if (entry == null)
                {
                    continue;
                }

                entry.gameObject.SetActive(false);
                Destroy(entry.gameObject);
            }

            _spawnedLobbyEntries.Clear();
        }

        private void RefreshControlAvailability()
        {
            bool idle = _flowController != null && _flowController.IsIdle;
            bool connected = _flowController != null && _flowController.IsConnected;
            bool coopConnected = connected && !_flowController.IsSinglePlayer;
            bool steamReady = SteamBootstrap.IsSteamAvailable &&
                              _lobbyService != null &&
                              _lobbyService.IsReady;

            SetInteractable(
                playSinglePlayerButton,
                _sessionCoordinator != null &&
                _sessionCoordinator.CanStartSinglePlayer);
            SetInteractable(
                rejoinSessionButton,
                idle && steamReady && _lastSessionRecord != null);
            SetInteractable(
                forgetSessionButton,
                _lastSessionRecord != null);
            SetInteractable(openCreateLobbyButton, idle && steamReady);
            SetInteractable(quickJoinButton, idle && steamReady);
            SetInteractable(refreshButton, idle && steamReady);
            SetInteractable(joinCodeButton, idle && steamReady);
            SetInteractable(coopBackButton, idle ||
                                            (_flowController != null &&
                                             _flowController.State ==
                                             MultiplayerFlowState.Failed));
            SetInteractable(createPublicButton, idle && steamReady);
            SetInteractable(createPrivateButton, idle && steamReady);
            SetInteractable(inviteFriendsButton, coopConnected &&
                                                   _lobbyService.IsInLobby);
            SetInteractable(leaveLobbyButton, coopConnected ||
                                               (_lobbyService != null &&
                                                _lobbyService.IsInLobby));

            bool hostCanStart =
                _sessionCoordinator != null &&
                _sessionCoordinator.CanHostStartGame;
            bool clientWaiting =
                coopConnected &&
                _flowController.IsClient &&
                _sessionCoordinator != null &&
                _sessionCoordinator.State == GameSessionState.Lobby;

            SetActive(
                startGameButton != null ? startGameButton.gameObject : null,
                hostCanStart);
            SetInteractable(startGameButton, hostCanStart);
            SetActive(
                waitingForHostText != null ? waitingForHostText.gameObject : null,
                clientWaiting);
            SetInteractable(
                busyCancelButton,
                _flowController != null &&
                _flowController.IsBusy &&
                _flowController.State != MultiplayerFlowState.Disconnecting);
        }

        private void ShowBaseScreen(MainMenuMultiplayerScreen screen)
        {
            _currentScreen = screen;
            SetActive(mainPanel, screen == MainMenuMultiplayerScreen.Main);
            SetActive(coopPanel, screen == MainMenuMultiplayerScreen.CoopBrowser);
            SetActive(lobbyRoomPanel, screen == MainMenuMultiplayerScreen.LobbyRoom);
            RefreshLastSessionView();
        }

        private void SetBusy(bool visible, string message)
        {
            SetActive(busyOverlay, visible);

            if (busyMessageText != null && visible)
            {
                busyMessageText.text = string.IsNullOrWhiteSpace(message)
                    ? "Working..."
                    : message;
            }
        }

        private void ShowFailure(string message)
        {
            SetBusy(false, string.Empty);
            SetActive(failureOverlay, true);

            if (failureMessageText != null)
            {
                failureMessageText.text = string.IsNullOrWhiteSpace(message)
                    ? "Multiplayer operation failed."
                    : message;
            }
        }

        private void BindButtons()
        {
            if (_buttonsBound)
            {
                return;
            }

            AddListener(playCoopButton, OpenCoopPanel);
            AddListener(playSinglePlayerButton, PlaySinglePlayer);
            AddListener(rejoinSessionButton, RejoinLastSession);
            AddListener(forgetSessionButton, ForgetLastSession);
            AddListener(coopBackButton, ReturnToMainPanel);
            AddListener(openCreateLobbyButton, OpenCreateLobbyModal);
            AddListener(quickJoinButton, QuickJoin);
            AddListener(refreshButton, RefreshLobbies);
            AddListener(joinCodeButton, JoinEnteredCode);
            AddListener(createPublicButton, CreatePublicLobby);
            AddListener(createPrivateButton, CreatePrivateLobby);
            AddListener(createModalBackButton, CloseCreateLobbyModal);
            AddListener(inviteFriendsButton, InviteFriends);
            AddListener(startGameButton, StartGame);
            AddListener(leaveLobbyButton, LeaveLobby);
            AddListener(busyCancelButton, CancelBusyOperation);
            AddListener(failureBackButton, DismissFailure);

            _buttonsBound = true;
        }

        private void UnbindButtons()
        {
            if (!_buttonsBound)
            {
                return;
            }

            RemoveListener(playCoopButton, OpenCoopPanel);
            RemoveListener(playSinglePlayerButton, PlaySinglePlayer);
            RemoveListener(rejoinSessionButton, RejoinLastSession);
            RemoveListener(forgetSessionButton, ForgetLastSession);
            RemoveListener(coopBackButton, ReturnToMainPanel);
            RemoveListener(openCreateLobbyButton, OpenCreateLobbyModal);
            RemoveListener(quickJoinButton, QuickJoin);
            RemoveListener(refreshButton, RefreshLobbies);
            RemoveListener(joinCodeButton, JoinEnteredCode);
            RemoveListener(createPublicButton, CreatePublicLobby);
            RemoveListener(createPrivateButton, CreatePrivateLobby);
            RemoveListener(createModalBackButton, CloseCreateLobbyModal);
            RemoveListener(inviteFriendsButton, InviteFriends);
            RemoveListener(startGameButton, StartGame);
            RemoveListener(leaveLobbyButton, LeaveLobby);
            RemoveListener(busyCancelButton, CancelBusyOperation);
            RemoveListener(failureBackButton, DismissFailure);

            _buttonsBound = false;
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(
            Button button,
            UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void SetInteractable(Selectable selectable, bool value)
        {
            if (selectable != null)
            {
                selectable.interactable = value;
            }
        }

        private static void Show(GameObject target)
        {
            SetActive(target, true);
        }

        private static void Hide(GameObject target)
        {
            SetActive(target, false);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value)
            {
                target.SetActive(value);
            }
        }

        private static void Require(
            UnityEngine.Object value,
            string fieldName,
            ICollection<string> missing)
        {
            if (value == null)
            {
                missing.Add(fieldName);
            }
        }

        private static Transform RootOf(GameObject value)
        {
            return value != null ? value.transform : null;
        }

        private static GameObject FindObject(
            GameObject current,
            Transform root,
            params string[] names)
        {
            if (current != null)
            {
                return current;
            }

            Transform found = FindTransform(root, names);
            return found != null ? found.gameObject : null;
        }

        private static T FindComponent<T>(
            T current,
            Transform root,
            params string[] names)
            where T : Component
        {
            if (current != null)
            {
                return current;
            }

            Transform found = FindTransform(root, names);
            return found != null ? found.GetComponent<T>() : null;
        }

        private static Transform FindTransform(Transform root, params string[] names)
        {
            if (root == null || names == null)
            {
                return null;
            }

            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);

            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                string expectedName = names[nameIndex];

                for (int childIndex = 0; childIndex < descendants.Length; childIndex++)
                {
                    if (string.Equals(
                            descendants[childIndex].name,
                            expectedName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return descendants[childIndex];
                    }
                }
            }

            return null;
        }
    }
}
