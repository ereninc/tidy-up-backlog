using System;
using Steamworks;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class SteamBootstrap : MonoBehaviour
{
    public static SteamBootstrap Instance { get; private set; }

    public static bool IsSteamAvailable =>
        Instance != null && Instance._steamInitialized;

    public static bool IsSteamLoggedOn =>
        IsSteamAvailable && SteamUser.BLoggedOn();

    public static ulong LocalSteamId =>
        IsSteamAvailable ? Instance._localSteamId : 0UL;

    public static string LocalPersonaName =>
        IsSteamAvailable ? Instance._localPersonaName : string.Empty;

    public static uint CurrentAppId =>
        IsSteamAvailable ? Instance._currentAppId : 0U;

    public static ESteamNetworkingAvailability RelayAvailability =>
        Instance != null
            ? Instance._relayAvailability
            : ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown;

    public static bool IsRelayReady =>
        RelayAvailability == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current;

    public static string RelayDebugMessage =>
        Instance != null ? Instance._relayDebugMessage : string.Empty;

    public static string InitializationError =>
        Instance != null ? Instance._initializationError : string.Empty;

    [Header("Steam")]
    [Tooltip("Development uses 480 (Valve Spacewar). Replace this with the game's real App ID later.")]
    [SerializeField] private uint appId = 480;

    [Tooltip("In a standalone build, relaunch through Steam when necessary. This is skipped in the Unity Editor.")]
    [SerializeField] private bool restartThroughSteamInBuild = true;

    [Header("Networking")]
    [Tooltip("Warms up access to the Steam Datagram Relay network during startup.")]
    [SerializeField] private bool initializeRelayNetworkAccess = true;

    private bool _steamInitialized;
    private uint _currentAppId;
    private ulong _localSteamId;
    private string _localPersonaName = string.Empty;
    private string _initializationError = string.Empty;

    private ESteamNetworkingAvailability _relayAvailability =
        ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown;

    private string _relayDebugMessage = string.Empty;
    private Callback<SteamRelayNetworkStatus_t> _relayNetworkStatusCallback;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[STEAM] Duplicate SteamBootstrap destroyed.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeSteam();
    }

    private void Update()
    {
        if (!_steamInitialized) return;

        // Steam lobby results, invites and connection-status events are
        // dispatched only while callbacks are being pumped.
        SteamAPI.RunCallbacks();
    }

    private void OnApplicationQuit()
    {
        // Do not rely only on Unity's callback order: if this component receives
        // OnApplicationQuit first, synchronously hand control to the coordinator
        // before touching SteamAPI.
        EXW.Multiplayer.MultiplayerShutdownCoordinator.TryShutdownActiveCoordinator(
            "SteamBootstrap application-quit fallback");

        // Safe fallback when the coordinator is missing or incomplete. If the
        // coordinator already completed this is an idempotent no-op.
        ShutdownSteam();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        if (_steamInitialized)
        {
            EXW.Multiplayer.MultiplayerShutdownCoordinator.TryShutdownActiveCoordinator(
                "SteamBootstrap destroyed");
        }

        ShutdownSteam();
        Instance = null;
    }

    private void InitializeSteam()
    {
        if (_steamInitialized) return;

        _initializationError = string.Empty;

        try
        {
            if (appId == 0)
            {
                FailInitialization("Configured App ID is 0.");
                return;
            }

#if !UNITY_EDITOR
            if (restartThroughSteamInBuild &&
                SteamAPI.RestartAppIfNecessary(new AppId_t(appId)))
            {
                Debug.Log("[STEAM] Relaunching the game through Steam.", this);
                Application.Quit();
                return;
            }
#endif

            if (!Packsize.Test())
            {
                FailInitialization("Steamworks.NET callback packing test failed.");
                return;
            }

            if (!DllCheck.Test())
            {
                FailInitialization("Steamworks native library version check failed.");
                return;
            }

            ESteamAPIInitResult initResult = SteamAPI.InitEx(out string errorMessage);

            if (initResult != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
            {
                string details = string.IsNullOrWhiteSpace(errorMessage)
                    ? initResult.ToString()
                    : $"{initResult}: {errorMessage}";

                FailInitialization(details);
                return;
            }

            _steamInitialized = true;
            _currentAppId = SteamUtils.GetAppID().m_AppId;
            _localSteamId = SteamUser.GetSteamID().m_SteamID;
            _localPersonaName = SteamFriends.GetPersonaName() ?? string.Empty;

            _relayNetworkStatusCallback =
                Callback<SteamRelayNetworkStatus_t>.Create(OnRelayNetworkStatusChanged);

            Debug.Log(
                $"[STEAM] Initialized | AppID: {_currentAppId} | " +
                $"User: {_localPersonaName} | SteamID: {_localSteamId}",
                this);

            if (_currentAppId != appId)
            {
                Debug.LogWarning(
                    $"[STEAM] Configured AppID ({appId}) does not match the active AppID " +
                    $"({_currentAppId}). Check steam_appid.txt.",
                    this);
            }

            if (!SteamUser.BLoggedOn())
            {
                Debug.LogWarning(
                    "[STEAM] API initialized, but the local user is not connected to Steam.",
                    this);
            }

            if (initializeRelayNetworkAccess)
            {
                RequestRelayNetworkAccess();
            }
        }
        catch (Exception exception)
        {
            if (_steamInitialized)
            {
                ShutdownSteam();
            }

            FailInitialization($"{exception.GetType().Name}: {exception.Message}");
            Debug.LogException(exception, this);
        }
    }

    public void RequestRelayNetworkAccess()
    {
        if (!_steamInitialized)
        {
            Debug.LogWarning(
                "[STEAM] Cannot initialize SDR before the Steam API is available.",
                this);
            return;
        }

        SteamNetworkingUtils.InitRelayNetworkAccess();
        RefreshRelayNetworkStatus();
    }

    /// <summary>
    /// Final shutdown step used by MultiplayerShutdownCoordinator. NGO, the
    /// Steam transport and the active lobby must already be closed when this is
    /// called. The operation is idempotent.
    /// </summary>
    public void ShutdownSteamForApplicationExit()
    {
        ShutdownSteam();
    }

    [ContextMenu("Log Current Steam Status")]
    public void LogCurrentStatus()
    {
        if (!_steamInitialized)
        {
            Debug.LogWarning(
                $"[STEAM] Unavailable | Error: {_initializationError}",
                this);
            return;
        }

        RefreshRelayNetworkStatus();

        Debug.Log(
            $"[STEAM] Status | AppID: {_currentAppId} | User: {_localPersonaName} | " +
            $"SteamID: {_localSteamId} | LoggedOn: {SteamUser.BLoggedOn()} | " +
            $"SDR: {_relayAvailability}",
            this);
    }

    private void RefreshRelayNetworkStatus()
    {
        ESteamNetworkingAvailability availability =
            SteamNetworkingUtils.GetRelayNetworkStatus(out SteamRelayNetworkStatus_t status);

        status.m_eAvail = availability;
        ApplyRelayNetworkStatus(status);
    }

    private void OnRelayNetworkStatusChanged(SteamRelayNetworkStatus_t status)
    {
        ApplyRelayNetworkStatus(status);
    }

    private void ApplyRelayNetworkStatus(SteamRelayNetworkStatus_t status)
    {
        string debugMessage = status.m_debugMsg ?? string.Empty;
        bool changed =
            _relayAvailability != status.m_eAvail ||
            !string.Equals(_relayDebugMessage, debugMessage, StringComparison.Ordinal);

        _relayAvailability = status.m_eAvail;
        _relayDebugMessage = debugMessage;

        if (!changed) return;

        Debug.Log(
            $"[STEAM] SDR status: {_relayAvailability} | " +
            $"Config: {status.m_eAvailNetworkConfig} | " +
            $"AnyRelay: {status.m_eAvailAnyRelay} | " +
            $"{_relayDebugMessage}",
            this);
    }

    private void ShutdownSteam()
    {
        if (!_steamInitialized) return;

        _steamInitialized = false;

        try
        {
            _relayNetworkStatusCallback?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[STEAM] Failed to dispose a Steam callback: {exception.Message}", this);
        }

        _relayNetworkStatusCallback = null;

        try
        {
            SteamAPI.Shutdown();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[STEAM] Shutdown failed: {exception.Message}", this);
        }

        _currentAppId = 0;
        _localSteamId = 0;
        _localPersonaName = string.Empty;
        _relayAvailability = ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown;
        _relayDebugMessage = string.Empty;

        Debug.Log("[STEAM] Shutdown complete.", this);
    }

    private void FailInitialization(string reason)
    {
        _initializationError = reason;
        Debug.LogError($"[STEAM] Initialization failed: {reason}", this);
    }
}
