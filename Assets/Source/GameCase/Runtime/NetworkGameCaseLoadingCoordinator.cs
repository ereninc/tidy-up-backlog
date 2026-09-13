using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Place one scene NetworkObject with this component in LoadingScene.
    /// The host reads SteamLobbyService.CurrentMembers, builds one manifest,
    /// sends only that manifest to clients, waits for local cover caches, then
    /// performs the single authoritative NGO load to GameScene.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Network Game Case Loading Coordinator")]
    public sealed class NetworkGameCaseLoadingCoordinator : NetworkBehaviour
    {
        private readonly NetworkVariable<uint> _assignmentSeed =
            new NetworkVariable<uint>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort> _copiesPerGame =
            new NetworkVariable<ushort>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort> _distinctGameCount =
            new NetworkVariable<ushort>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _manifestReady =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [Header("Scene Flow")]
        [SerializeField] private string gameplaySceneName = "GameScene";

        [Header("Game Case Set")]
        [SerializeField, Min(1)] private int requiredDistinctGames = 50;
        [SerializeField, Min(1)] private int copiesPerGame = 30;

        [Tooltip(
            "Optional known-good pool used when private/empty libraries leave " +
            "the host below Required Distinct Games.")]
        [SerializeField] private uint[] fallbackAppIds = Array.Empty<uint>();

        [Tooltip("Current game, tools, demos or unwanted AppIds can go here.")]
        [SerializeField] private uint[] excludedAppIds = Array.Empty<uint>();

        [Header("References")]
        [SerializeField] private SteamOwnedGamesClient ownedGamesClient;

        [Header("Cover Cache")]
        [SerializeField] private bool clearPreviousSessionCache = true;

        public float LocalProgress { get; private set; }
        public string LocalStatus { get; private set; } = "Waiting";

        public event Action<float, string> LocalProgressChanged;
        public event Action<string> LocalLoadingFailed;

        private NetworkList<uint> _selectedAppIds;
        private readonly HashSet<ulong> _readyClientIds =
            new HashSet<ulong>();

        private bool _localPreloadStarted;
        private bool _sceneLoadRequested;
        
        private bool IsSinglePlayerSession =>
            MultiplayerSessionCoordinator.Instance != null &&
            MultiplayerSessionCoordinator.Instance.Mode ==
            GameSessionMode.SinglePlayer;

        private void Awake()
        {
            _selectedAppIds = new NetworkList<uint>();

            if (ownedGamesClient == null)
            {
                ownedGamesClient = GetComponent<SteamOwnedGamesClient>();
            }
        }

        private void OnValidate()
        {
            requiredDistinctGames = Mathf.Clamp(
                requiredDistinctGames,
                1,
                ushort.MaxValue);
            copiesPerGame = Mathf.Clamp(
                copiesPerGame,
                1,
                ushort.MaxValue);

            if (ownedGamesClient == null)
            {
                ownedGamesClient = GetComponent<SteamOwnedGamesClient>();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            GameCaseSessionPlan.Clear();
            SetLocalProgress(0f, "Preparing game libraries");
            _manifestReady.OnValueChanged += HandleManifestReadyChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback +=
                    HandleClientDisconnected;
                StartCoroutine(BuildManifestServer());
            }

            if (_manifestReady.Value)
            {
                BeginLocalPreload();
            }
        }

        public override void OnNetworkDespawn()
        {
            _manifestReady.OnValueChanged -= HandleManifestReadyChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -=
                    HandleClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private IEnumerator BuildManifestServer()
        {
            float serviceDeadline = Time.realtimeSinceStartup + 10f;
            
            if (IsSinglePlayerSession)
            {
                yield return BuildSinglePlayerManifestServer();
                yield break;
            }

            while (SteamLobbyService.Instance == null &&
                   Time.realtimeSinceStartup < serviceDeadline)
            {
                yield return null;
            }

            if (!IsServer || !IsSpawned)
            {
                yield break;
            }

            if (SteamLobbyService.Instance == null)
            {
                FailLoadingServer("SteamLobbyService was not ready.");
                yield break;
            }

            IReadOnlyList<SteamLobbyMember> liveMembers =
                SteamLobbyService.Instance.CurrentMembers;
            var members = new List<SteamLobbyMember>();

            if (liveMembers != null)
            {
                for (int i = 0; i < liveMembers.Count; i++)
                {
                    SteamLobbyMember member = liveMembers[i];

                    if (member != null && member.SteamId != 0)
                    {
                        members.Add(member);
                    }
                }
            }

            if (members.Count == 0)
            {
                FailLoadingServer("The Steam lobby has no valid members.");
                yield break;
            }

            var libraries = new IReadOnlyList<uint>[members.Count];

            if (ownedGamesClient != null)
            {
                int remaining = members.Count;
                int completed = 0;

                for (int i = 0; i < members.Count; i++)
                {
                    int capturedIndex = i;
                    SteamLobbyMember member = members[capturedIndex];

                    StartCoroutine(ownedGamesClient.FetchOwnedAppIds(
                        member.SteamId,
                        result =>
                        {
                            if (result.Succeeded)
                            {
                                libraries[capturedIndex] = result.AppIds;
                            }
                            else
                            {
                                libraries[capturedIndex] = Array.Empty<uint>();
                                Debug.LogWarning(
                                    "[GameCaseLoading] Could not read " +
                                    $"{member.PersonaName}'s library: " +
                                    result.Error,
                                    this);
                            }

                            completed++;
                            remaining--;
                            SetLocalProgress(
                                0.35f * completed / members.Count,
                                $"Reading Steam libraries " +
                                $"({completed}/{members.Count})");
                        }));
                }

                while (remaining > 0 && IsSpawned)
                {
                    yield return null;
                }
            }
            else
            {
                for (int i = 0; i < libraries.Length; i++)
                {
                    libraries[i] = Array.Empty<uint>();
                }

                Debug.LogWarning(
                    "[GameCaseLoading] SteamOwnedGamesClient is missing; " +
                    "using only fallback AppIds.",
                    this);
            }

            if (!IsServer || !IsSpawned)
            {
                yield break;
            }

            ulong lobbyId = SteamLobbyService.Instance.CurrentLobbyId;
            uint seed = unchecked(
                (uint)(lobbyId ^ (ulong)DateTime.UtcNow.Ticks));

            if (seed == 0)
            {
                seed = 1;
            }

            var libraryView = new List<IReadOnlyList<uint>>(libraries.Length);

            for (int i = 0; i < libraries.Length; i++)
            {
                libraryView.Add(libraries[i] ?? Array.Empty<uint>());
            }

            List<uint> selected =
                GameCaseSelectionUtility.SelectDistinctFairly(
                    libraryView,
                    fallbackAppIds,
                    excludedAppIds,
                    requiredDistinctGames,
                    seed);

            if (selected.Count != requiredDistinctGames)
            {
                FailLoadingServer(
                    $"Only {selected.Count}/{requiredDistinctGames} unique " +
                    "AppIds were available. Add fallback AppIds or check " +
                    "Steam library visibility.");
                yield break;
            }

            _manifestReady.Value = false;
            _readyClientIds.Clear();
            _selectedAppIds.Clear();

            for (int i = 0; i < selected.Count; i++)
            {
                _selectedAppIds.Add(selected[i]);
            }

            _assignmentSeed.Value = seed;
            _copiesPerGame.Value = (ushort)copiesPerGame;
            _distinctGameCount.Value = (ushort)requiredDistinctGames;
            _manifestReady.Value = true;
            SetLocalProgress(0.4f, "Game manifest ready");
        }

        private void HandleManifestReadyChanged(bool previous, bool current)
        {
            if (current)
            {
                BeginLocalPreload();
            }
        }

        private void BeginLocalPreload()
        {
            if (_localPreloadStarted)
            {
                return;
            }

            _localPreloadStarted = true;
            StartCoroutine(WaitForManifestThenPreload());
        }

        private IEnumerator WaitForManifestThenPreload()
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            int expectedCount = _distinctGameCount.Value;

            while ((expectedCount <= 0 ||
                    _selectedAppIds.Count != expectedCount ||
                    _copiesPerGame.Value == 0 ||
                    _assignmentSeed.Value == 0) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                expectedCount = _distinctGameCount.Value;
            }

            if (!_manifestReady.Value ||
                expectedCount <= 0 ||
                _selectedAppIds.Count != expectedCount ||
                _copiesPerGame.Value == 0 ||
                _assignmentSeed.Value == 0)
            {
                FailLoadingLocal("The replicated game manifest was incomplete.");
                yield break;
            }

            var appIds = new uint[_selectedAppIds.Count];

            for (int i = 0; i < _selectedAppIds.Count; i++)
            {
                appIds[i] = _selectedAppIds[i];
            }

            GameCaseSessionPlan.Set(
                appIds,
                _copiesPerGame.Value,
                _assignmentSeed.Value);

            GameCaseCoverCache cache = GameCaseCoverCache.GetOrCreate();

            if (clearPreviousSessionCache)
            {
                cache.ClearCache();
            }
            
            bool offlineSinglePlayer =
                IsSinglePlayerSession &&
                (!SteamBootstrap.IsSteamAvailable ||
                 !SteamBootstrap.IsSteamLoggedOn);

            cache.SetRequestsEnabled(!offlineSinglePlayer);

            if (offlineSinglePlayer)
            {
                SetLocalProgress(1f, "Offline fallback games ready");
                HandleLocalCoversReady();
                yield break;
            }

            cache.Preload(
                appIds,
                progress => SetLocalProgress(
                    Mathf.Lerp(0.4f, 1f, progress),
                    $"Downloading covers " +
                    $"({Mathf.RoundToInt(progress * appIds.Length)}/" +
                    $"{appIds.Length})"),
                HandleLocalCoversReady);
        }
        
        private IEnumerator BuildSinglePlayerManifestServer()
        {
            var libraryView = new List<IReadOnlyList<uint>>(1);

            bool steamOnline =
                SteamBootstrap.IsSteamAvailable &&
                SteamBootstrap.IsSteamLoggedOn &&
                SteamBootstrap.LocalSteamId != 0;

            if (steamOnline && ownedGamesClient != null)
            {
                SetLocalProgress(0.05f, "Reading local Steam library");

                SteamOwnedGamesResult result = null;

                yield return ownedGamesClient.FetchOwnedAppIds(
                    SteamBootstrap.LocalSteamId,
                    value => result = value);

                if (!IsServer || !IsSpawned)
                {
                    yield break;
                }

                if (result != null && result.Succeeded)
                {
                    libraryView.Add(result.AppIds);

                    SetLocalProgress(
                        0.35f,
                        $"Found {result.AppIds.Count} owned Steam games");
                }
                else
                {
                    Debug.LogWarning(
                        "[GameCaseLoading] Local Steam library could not be read: " +
                        (result?.Error ?? "No result. Using fallback AppIds."),
                        this);
                }
            }
            else
            {
                SetLocalProgress(
                    0.35f,
                    "Steam is offline; using fallback games");

                Debug.Log(
                    "[GameCaseLoading] Offline singleplayer: using fallback AppIds.",
                    this);
            }

            PublishManifestServer(
                libraryView,
                SteamBootstrap.LocalSteamId);
        }
        
        private void PublishManifestServer(
            IReadOnlyList<IReadOnlyList<uint>> libraryView,
            ulong seedSource)
        {
            uint seed = unchecked(
                (uint)(seedSource ^ (ulong)DateTime.UtcNow.Ticks));

            if (seed == 0)
            {
                seed = 1;
            }

            List<uint> selected =
                GameCaseSelectionUtility.SelectDistinctFairly(
                    libraryView,
                    fallbackAppIds,
                    excludedAppIds,
                    requiredDistinctGames,
                    seed);

            if (selected.Count != requiredDistinctGames)
            {
                FailLoadingServer(
                    $"Only {selected.Count}/{requiredDistinctGames} unique " +
                    "AppIds were available. Add more fallback AppIds.");
                return;
            }

            _manifestReady.Value = false;
            _readyClientIds.Clear();
            _selectedAppIds.Clear();

            for (int i = 0; i < selected.Count; i++)
            {
                _selectedAppIds.Add(selected[i]);
            }

            _assignmentSeed.Value = seed;
            _copiesPerGame.Value = (ushort)copiesPerGame;
            _distinctGameCount.Value = (ushort)requiredDistinctGames;
            _manifestReady.Value = true;

            SetLocalProgress(0.4f, "Game manifest ready");
        }

        private void HandleLocalCoversReady()
        {
            SetLocalProgress(1f, "Ready");

            if (IsServer)
            {
                MarkClientReadyServer(NetworkManager.LocalClientId);
            }
            else
            {
                ReportLoadingReadyServerRpc();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportLoadingReadyServerRpc(
            ServerRpcParams rpcParams = default)
        {
            MarkClientReadyServer(rpcParams.Receive.SenderClientId);
        }

        private void MarkClientReadyServer(ulong clientId)
        {
            if (!IsServer || !_manifestReady.Value ||
                NetworkManager == null ||
                !NetworkManager.ConnectedClients.ContainsKey(clientId))
            {
                return;
            }

            _readyClientIds.Add(clientId);
            TryLoadGameplaySceneServer();
        }

        private void TryLoadGameplaySceneServer()
        {
            if (!IsServer || _sceneLoadRequested ||
                NetworkManager == null || !NetworkManager.IsListening)
            {
                return;
            }

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                if (!_readyClientIds.Contains(clientId))
                {
                    return;
                }
            }

            _sceneLoadRequested = true;
            SetLocalProgress(1f, "Opening gameplay scene");
            NetworkManager.SceneManager.LoadScene(
                gameplaySceneName,
                LoadSceneMode.Single);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            _readyClientIds.Remove(clientId);
            TryLoadGameplaySceneServer();
        }

        private void FailLoadingServer(string message)
        {
            Debug.LogError("[GameCaseLoading] " + message, this);
            LoadingFailedClientRpc(message);
        }

        [ClientRpc]
        private void LoadingFailedClientRpc(string message)
        {
            FailLoadingLocal(message);
        }

        private void FailLoadingLocal(string message)
        {
            LocalStatus = message;
            LocalLoadingFailed?.Invoke(message);
            LocalProgressChanged?.Invoke(LocalProgress, LocalStatus);
        }

        private void SetLocalProgress(float progress, string status)
        {
            LocalProgress = Mathf.Clamp01(progress);
            LocalStatus = status ?? string.Empty;
            LocalProgressChanged?.Invoke(LocalProgress, LocalStatus);
        }
    }
}
