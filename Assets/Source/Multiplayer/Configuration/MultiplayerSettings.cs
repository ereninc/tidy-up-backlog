using Steamworks;
using UnityEngine;
using UnityEngine.Serialization;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Project-wide multiplayer policy. Keep one asset at
    /// Assets/Resources/MultiplayerSettings.asset so persistent runtime
    /// components and scene-local UI always read the same values.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MultiplayerSettings",
        menuName = "EXW/Multiplayer/Settings",
        order = 0)]
    public sealed class MultiplayerSettings : ScriptableObject
    {
        public const string AssetName = "MultiplayerSettings";
        public const string ResourcesPath = AssetName;

        private static MultiplayerSettings _current;
        private static bool _missingAssetWarningLogged;

        [Header("Build Compatibility")]
        [Tooltip(
            "Unique product namespace used in Steam lobby filters. This is " +
            "especially important while several games share Spacewar AppID 480.")]
        [SerializeField] private string productId = "coop_base_dev";

        [Tooltip(
            "Increase when a networking or lobby protocol change makes older " +
            "clients incompatible.")]
        [SerializeField, Min(1)] private int protocolVersion = 1;

        [Tooltip(
            "Compatible builds must use the same value. Leave empty to use " +
            "Application.version.")]
        [SerializeField] private string buildIdOverride = "dev";

        [Header("Lobby Browser")]
        [SerializeField, Range(2, 4)] private int maximumPlayers = 4;
        [SerializeField, Range(1, 200)] private int maximumSearchResults = 50;
        [SerializeField] private ELobbyDistanceFilter searchDistance =
            ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide;

        [Header("Running Session Join Policy")]
        [Tooltip(
            "A Steam user who was connected when gameplay was running may " +
            "rejoin with the same SteamID after a crash, restart or connection loss.")]
        [SerializeField] private bool allowReconnectToRunningSession = true;

        [Tooltip(
            "New players may discover and join a lobby after gameplay has begun. " +
            "This is the default for newly created lobbies and can be overridden " +
            "per lobby by UI later.")]
        [SerializeField] private bool defaultAllowLateJoin = true;

        [Tooltip(
            "Controls how long an offline participant continues owning their " +
            "SteamID-stable session slot.")]
        [SerializeField] private SessionSlotRetentionPolicy sessionSlotRetention =
            SessionSlotRetentionPolicy.UntilSessionEnds;

        [Tooltip(
            "Used only by Timed retention. After this duration an offline slot " +
            "becomes available to a new late-join player.")]
        [FormerlySerializedAs("reconnectReservationSeconds")]
        [SerializeField, Min(1f)] private float timedSessionSlotRetentionSeconds =
            600f;

        [Tooltip(
            "Defines whether a received graceful-exit notice (currently Return " +
            "To Main Menu) keeps the player's session slot. Crashes, process " +
            "termination and network loss always follow the retention policy above.")]
        [SerializeField] private GracefulExitSlotPolicy gracefulExitSlotBehaviour =
            GracefulExitSlotPolicy.PreserveSlot;

        [Tooltip(
            "Quick Join prefers a running lobby in which the local Steam user has " +
            "an existing session slot before choosing a new waiting lobby.")]
        [SerializeField] private bool preferReconnectInQuickJoin = true;

        [Tooltip(
            "Prompt exposes the last running lobby to optional menu UI. Browser " +
            "Only relies on the normal lobby browser without a dedicated prompt.")]
        [SerializeField] private LastSessionRejoinMode lastSessionRejoinMode =
            LastSessionRejoinMode.Prompt;

        [Header("Connection Approval")]
        [SerializeField] private bool requireLobbyMembership = true;
        [SerializeField] private bool requireTransportIdentityMatch = true;

        [Tooltip(
            "Recommended. NGO approves the connection in the menu without " +
            "creating the FPS PlayerObject; the session coordinator spawns it " +
            "after gameplay scene synchronization completes.")]
        [SerializeField] private bool deferPlayerObjectUntilGameplay = true;

        [Tooltip("Used only when deferred PlayerObject spawning is disabled.")]
        [SerializeField] private bool useSceneSpawnPointsDuringApproval = true;

        [Header("Flow")]
        [SerializeField, Min(5f)] private float clientConnectionTimeout = 20f;
        [SerializeField, Min(2f)] private float shutdownTimeout = 8f;
        [SerializeField] private bool automaticallyAcceptSteamInvites = true;
        [SerializeField] private bool quickJoinCreatesLobbyWhenNoneFound = true;

        [Header("Scenes")]
        [SerializeField] private string gameplaySceneName = "GameplayScene";
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";
        [SerializeField] private bool activatePlayersAfterLoad = true;

        [Header("Diagnostics")]
        [SerializeField] private bool verboseLogging = true;

        public static MultiplayerSettings Current
        {
            get
            {
                if (_current != null)
                {
                    return _current;
                }

                _current = Resources.Load<MultiplayerSettings>(ResourcesPath);

                if (_current != null)
                {
                    return _current;
                }

                _current = CreateInstance<MultiplayerSettings>();
                _current.name = "MultiplayerSettings (Runtime Defaults)";
                _current.hideFlags = HideFlags.HideAndDontSave;

                if (!_missingAssetWarningLogged)
                {
                    _missingAssetWarningLogged = true;
                    Debug.LogWarning(
                        "[MultiplayerSettings] Assets/Resources/" +
                        "MultiplayerSettings.asset was not found. Safe runtime " +
                        "defaults are active. Use Tools > EXW > Multiplayer > " +
                        "Create Or Select Settings to create the editable asset.");
                }

                return _current;
            }
        }

        public static bool HasProjectAsset =>
            Resources.Load<MultiplayerSettings>(ResourcesPath) != null;

        public string ProductId
        {
            get
            {
                string value = productId == null ? string.Empty : productId.Trim();
                return string.IsNullOrEmpty(value) ? "coop_base_dev" : value;
            }
        }

        public int ProtocolVersion => Mathf.Max(1, protocolVersion);

        public string BuildId
        {
            get
            {
                string value = buildIdOverride == null
                    ? string.Empty
                    : buildIdOverride.Trim();

                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }

                value = Application.version == null
                    ? string.Empty
                    : Application.version.Trim();

                return string.IsNullOrEmpty(value) ? "dev" : value;
            }
        }

        public int MaximumPlayers => Mathf.Clamp(maximumPlayers, 2, 4);
        public int MaximumSearchResults =>
            Mathf.Clamp(maximumSearchResults, 1, 200);
        public ELobbyDistanceFilter SearchDistance => searchDistance;

        public bool AllowReconnectToRunningSession =>
            allowReconnectToRunningSession;
        public bool DefaultAllowLateJoin => defaultAllowLateJoin;
        public SessionSlotRetentionPolicy SessionSlotRetention =>
            sessionSlotRetention;
        public float TimedSessionSlotRetentionSeconds =>
            Mathf.Max(1f, timedSessionSlotRetentionSeconds);
        public GracefulExitSlotPolicy GracefulExitSlotBehaviour =>
            gracefulExitSlotBehaviour;
        public bool PreferReconnectInQuickJoin => preferReconnectInQuickJoin;
        public LastSessionRejoinMode LastSessionRejoinBehaviour =>
            lastSessionRejoinMode;

        public bool SupportsSessionRejoin =>
            AllowReconnectToRunningSession &&
            SessionSlotRetention !=
            SessionSlotRetentionPolicy.ReleaseImmediately;

        /// <summary>
        /// Backward-compatible duration view. Zero means session lifetime, as it
        /// did in v4; ReleaseImmediately is represented by reconnect being false
        /// through SupportsSessionRejoin.
        /// </summary>
        public float ReconnectReservationSeconds =>
            SessionSlotRetention == SessionSlotRetentionPolicy.Timed
                ? TimedSessionSlotRetentionSeconds
                : 0f;

        public bool RequireLobbyMembership => requireLobbyMembership;
        public bool RequireTransportIdentityMatch =>
            requireTransportIdentityMatch;
        public bool DeferPlayerObjectUntilGameplay =>
            deferPlayerObjectUntilGameplay;
        public bool UseSceneSpawnPointsDuringApproval =>
            useSceneSpawnPointsDuringApproval;

        public float ClientConnectionTimeout =>
            Mathf.Max(5f, clientConnectionTimeout);
        public float ShutdownTimeout => Mathf.Max(2f, shutdownTimeout);
        public bool AutomaticallyAcceptSteamInvites =>
            automaticallyAcceptSteamInvites;
        public bool QuickJoinCreatesLobbyWhenNoneFound =>
            quickJoinCreatesLobbyWhenNoneFound;

        public string GameplaySceneName => NormalizeSceneName(
            gameplaySceneName,
            "GameplayScene");
        public string MainMenuSceneName => NormalizeSceneName(
            mainMenuSceneName,
            "MainMenuScene");
        public bool ActivatePlayersAfterLoad => activatePlayersAfterLoad;
        public bool VerboseLogging => verboseLogging;

        public bool ShouldKeepLobbyJoinableDuringGameplay(
            bool lobbyAllowsLateJoin)
        {
            return SupportsSessionRejoin || lobbyAllowsLateJoin;
        }

        public bool IsSessionSlotExpired(
            double disconnectedAt,
            double currentTime)
        {
            return SessionSlotRetention == SessionSlotRetentionPolicy.Timed &&
                   currentTime - disconnectedAt >=
                   TimedSessionSlotRetentionSeconds;
        }

        public bool IsReconnectReservationExpired(double disconnectedAt)
        {
            return IsSessionSlotExpired(
                disconnectedAt,
                Time.realtimeSinceStartupAsDouble);
        }

        public static void InvalidateRuntimeCache()
        {
            if (_current != null &&
                (_current.hideFlags & HideFlags.DontSave) != 0)
            {
                DestroyImmediate(_current);
            }

            _current = null;
            _missingAssetWarningLogged = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _current = null;
            _missingAssetWarningLogged = false;
        }

        private static string NormalizeSceneName(string value, string fallback)
        {
            value = value == null ? string.Empty : value.Trim();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private void OnValidate()
        {
            protocolVersion = Mathf.Max(1, protocolVersion);
            maximumPlayers = Mathf.Clamp(maximumPlayers, 2, 4);
            maximumSearchResults = Mathf.Clamp(maximumSearchResults, 1, 200);
            timedSessionSlotRetentionSeconds = Mathf.Max(
                1f,
                timedSessionSlotRetentionSeconds);
            clientConnectionTimeout = Mathf.Max(5f, clientConnectionTimeout);
            shutdownTimeout = Mathf.Max(2f, shutdownTimeout);
        }
    }
}
