using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Owns the one synchronous shutdown path used by application quit, Alt+F4
    /// and Unity Editor Play Mode exit. SteamAPI must always be the final system
    /// that is shut down.
    /// </summary>
    [DefaultExecutionOrder(-11000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Multiplayer Shutdown Coordinator")]
    public sealed class MultiplayerShutdownCoordinator : MonoBehaviour
    {
        public static MultiplayerShutdownCoordinator Instance { get; private set; }

        [Title("MULTIPLAYER SHUTDOWN COORDINATOR")]
        [InfoBox(
            "Runs a synchronous shutdown in this order: Flow callbacks > " +
            "NGO/Transport > Steam Lobby > Steam API. Keep this component on " +
            "the persistent NetworkRuntime object.")]
        [FoldoutGroup("Runtime References", expanded: true)]
        [Required]
        [SerializeField] private SteamBootstrap steamBootstrap;

        [FoldoutGroup("Runtime References")]
        [Required]
        [SerializeField] private MultiplayerFlowController flowController;

        [FoldoutGroup("Runtime References")]
        [Required]
        [SerializeField] private SteamLobbyService lobbyService;

        [FoldoutGroup("Runtime References")]
        [Required]
        [SerializeField] private NetworkManager networkManager;

        [FoldoutGroup("Runtime References")]
        [Required]
        [SerializeField] private SteamNetworkingSocketsTransport steamTransport;

        [FoldoutGroup("Debug")]
        [SerializeField] private bool verboseLogging = true;

        private bool _isDuplicate;
        private bool _shutdownInProgress;
        private bool _shutdownCompleted;
        private bool _shutdownHadErrors;
        private string _lastShutdownReason = "Not requested";

        [ShowInInspector, ReadOnly, BoxGroup("Live Status")]
        public bool IsConfigured =>
            steamBootstrap != null &&
            flowController != null &&
            lobbyService != null &&
            networkManager != null &&
            steamTransport != null;

        [ShowInInspector, ReadOnly, BoxGroup("Live Status")]
        public bool ShutdownInProgress => _shutdownInProgress;

        [ShowInInspector, ReadOnly, BoxGroup("Live Status")]
        public bool ShutdownCompleted => _shutdownCompleted;

        [ShowInInspector, ReadOnly, BoxGroup("Live Status")]
        public bool ShutdownHadErrors => _shutdownHadErrors;

        [ShowInInspector, ReadOnly, BoxGroup("Live Status")]
        public string LastShutdownReason => _lastShutdownReason;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        private void Reset()
        {
            ResolveReferences();
        }

        private void OnValidate()
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
                    "[MultiplayerShutdown] Duplicate coordinator destroyed.",
                    this);
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveReferences();

            if (!IsConfigured)
            {
                Debug.LogError(
                    "[MultiplayerShutdown] Runtime references are incomplete. " +
                    "Keep the coordinator on the same NetworkRuntime GameObject " +
                    "as the other multiplayer components.",
                    this);
            }
        }

        private void OnApplicationQuit()
        {
            ShutdownImmediately("Application quit");
        }

        private void OnDestroy()
        {
            if (_isDuplicate || Instance != this)
            {
                return;
            }

            // Covers an unexpected runtime destruction of NetworkRuntime. The
            // normal quit/editor paths will already have completed this method.
            if (!_shutdownCompleted && SteamBootstrap.IsSteamAvailable)
            {
                ShutdownImmediately("Shutdown coordinator destroyed");
            }

            Instance = null;
        }

        [Button("TEST QUIT CLEANUP (ENDS SESSION)", ButtonSizes.Large)]
        [GUIColor(0.95f, 0.55f, 0.20f)]
        [EnableIf(nameof(CanTestShutdown))]
        public void TestOrderlyShutdown()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning(
                    "[MultiplayerShutdown] Test is available only in Play Mode.",
                    this);
                return;
            }

            ShutdownImmediately("Odin inspector test button");
        }

        private bool CanTestShutdown()
        {
            return Application.isPlaying &&
                   !_shutdownInProgress &&
                   !_shutdownCompleted;
        }

        /// <summary>
        /// Executes every shutdown step synchronously and exactly once. Each step
        /// is isolated so a failure cannot prevent SteamAPI.Shutdown from running.
        /// </summary>
        public void ShutdownImmediately(string reason)
        {
            if (_shutdownCompleted || _shutdownInProgress)
            {
                return;
            }

            _shutdownInProgress = true;
            _shutdownHadErrors = false;
            _lastShutdownReason = string.IsNullOrWhiteSpace(reason)
                ? "Unspecified shutdown"
                : reason;

            ResolveReferences();
            Log($"BEGIN | Reason={_lastShutdownReason}");

            RunStep(1, "Quiesce multiplayer flow", () =>
            {
                flowController?.PrepareForApplicationExit();
            });

            RunStep(2, "Shutdown NGO and Steam transport", () =>
            {
                if (networkManager != null &&
                    (networkManager.IsListening ||
                     networkManager.IsServer ||
                     networkManager.IsClient ||
                     networkManager.ShutdownInProgress))
                {
                    // No future frame is guaranteed during quit, so discard NGO's
                    // queued messages and force its internal shutdown now.
                    networkManager.Shutdown(true);
                }

                // NGO may already have called this. The custom transport's
                // Shutdown implementation is idempotent, so this closes any native
                // handles that are still alive before SteamAPI disappears.
                if (steamTransport != null && steamTransport.IsRunning)
                {
                    steamTransport.Shutdown();
                }
            });

            RunStep(3, "Leave Steam lobby and dispose lobby callbacks", () =>
            {
                lobbyService?.ShutdownImmediatelyForApplicationExit();
            });

            RunStep(4, "Shutdown Steam API", () =>
            {
                steamBootstrap?.ShutdownSteamForApplicationExit();
            });

            _shutdownInProgress = false;
            _shutdownCompleted = true;

            if (_shutdownHadErrors)
            {
                Debug.LogWarning(
                    "[MultiplayerShutdown] COMPLETE WITH ERRORS. Review the " +
                    "numbered shutdown logs above.",
                    this);
            }
            else
            {
                Log("COMPLETE | NGO, transport, lobby and Steam are closed.");
            }
        }

        /// <summary>
        /// Used by the Editor bridge before Play Mode teardown begins.
        /// </summary>
        public static bool TryShutdownActiveCoordinator(string reason)
        {
            MultiplayerShutdownCoordinator coordinator = Instance;

            if (coordinator == null)
            {
                coordinator = UnityEngine.Object
                    .FindFirstObjectByType<MultiplayerShutdownCoordinator>();
            }

            if (coordinator == null)
            {
                return false;
            }

            coordinator.ShutdownImmediately(reason);
            return true;
        }

        private void RunStep(int number, string name, Action action)
        {
            Log($"STEP {number}/4 | {name}");

            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                _shutdownHadErrors = true;
                Debug.LogError(
                    $"[MultiplayerShutdown] STEP {number}/4 FAILED | {name} | " +
                    $"{exception.GetType().Name}: {exception.Message}",
                    this);
                Debug.LogException(exception, this);
            }
        }

        private void ResolveReferences()
        {
            if (steamBootstrap == null)
            {
                steamBootstrap = GetComponent<SteamBootstrap>();
            }

            if (flowController == null)
            {
                flowController = GetComponent<MultiplayerFlowController>();
            }

            if (lobbyService == null)
            {
                lobbyService = GetComponent<SteamLobbyService>();
            }

            if (networkManager == null)
            {
                networkManager = GetComponent<NetworkManager>();
            }

            if (steamTransport == null)
            {
                steamTransport = GetComponent<SteamNetworkingSocketsTransport>();
            }
        }

        private void Log(string message)
        {
            if (verboseLogging)
            {
                Debug.Log($"[MultiplayerShutdown] {message}", this);
            }
        }
    }
}
