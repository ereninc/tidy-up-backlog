using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Network Game Case Loading Coordinator")]
    public sealed class NetworkGameCaseLoadingCoordinator :
        NetworkBehaviour
    {
        private readonly NetworkVariable<uint>
            _assignmentSeed =
                new NetworkVariable<uint>(
                    0,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort>
            _copiesPerGame =
                new NetworkVariable<ushort>(
                    0,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ushort>
            _distinctGameCount =
                new NetworkVariable<ushort>(
                    0,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool>
            _manifestReady =
                new NetworkVariable<bool>(
                    false,
                    NetworkVariableReadPermission.Everyone,
                    NetworkVariableWritePermission.Server);

        [Header("Scene Flow")]
        [SerializeField]
        private string gameplaySceneName =
            "GameScene";

        [Tooltip(
            "Keeps the completed progress bar visible briefly before NGO " +
            "opens the gameplay scene.")]
        [SerializeField, Min(0f)]
        private float fullBarHoldSeconds = 0.35f;

        [Header("Game Case Set")]
        [SerializeField, Min(1)]
        private int requiredDistinctGames = 50;

        [SerializeField, Min(1)]
        private int copiesPerGame = 30;

        [Tooltip(
            "Optional known-good pool used when Steam libraries " +
            "do not provide enough games.")]
        [SerializeField]
        private uint[] fallbackAppIds =
            Array.Empty<uint>();

        [Tooltip(
            "Current game, tools, demos or unwanted AppIds.")]
        [SerializeField]
        private uint[] excludedAppIds =
            Array.Empty<uint>();

        [Header("Cover Validation")]
        [SerializeField, Min(1)]
        private int extraCandidatesPerPass = 25;

        [SerializeField, Min(1)]
        private int maximumCoverCandidates = 500;

        [Header("References")]
        [SerializeField]
        private SteamOwnedGamesClient ownedGamesClient;

        [SerializeField]
        private SteamAppContentFilterClient contentFilterClient;

        [Header("Cover Cache")]
        [SerializeField]
        private bool clearPreviousSessionCache = true;

        public float LocalProgress { get; private set; }

        public string LocalStatus { get; private set; } =
            "Waiting";

        public event Action<float, string>
            LocalProgressChanged;

        public event Action<string>
            LocalLoadingFailed;

        private NetworkList<uint> _selectedAppIds;

        /* Same index as _selectedAppIds. */
        private NetworkList<FixedString128Bytes>
            _selectedGameNames;

        /*
         * Same index as _selectedAppIds.
         *
         * Multiplayer:
         * average lifetime playtime among lobby members
         * who own that AppId.
         */
        private NetworkList<uint>
            _selectedPlaytimeMinutes;

        private readonly HashSet<ulong>
            _readyClientIds =
                new HashSet<ulong>();

        private bool _localPreloadStarted;

        private bool _sceneLoadRequested;

        private bool _serverCoverCachePrimed;

        private bool IsSinglePlayerSession =>
            MultiplayerSessionCoordinator.Instance != null &&
            MultiplayerSessionCoordinator.Instance.Mode ==
            GameSessionMode.SinglePlayer;

        private void Awake()
        {
            _selectedAppIds =
                new NetworkList<uint>();

            _selectedGameNames =
                new NetworkList<FixedString128Bytes>();

            _selectedPlaytimeMinutes =
                new NetworkList<uint>();

            if (!ownedGamesClient)
            {
                ownedGamesClient =
                    GetComponent<SteamOwnedGamesClient>();
            }

            if (!contentFilterClient)
            {
                contentFilterClient =
                    GetComponent<SteamAppContentFilterClient>();
            }
        }

        private void OnValidate()
        {
            requiredDistinctGames =
                Mathf.Clamp(
                    requiredDistinctGames,
                    1,
                    ushort.MaxValue);

            copiesPerGame =
                Mathf.Clamp(
                    copiesPerGame,
                    1,
                    ushort.MaxValue);

            extraCandidatesPerPass =
                Mathf.Max(
                    1,
                    extraCandidatesPerPass);

            maximumCoverCandidates =
                Mathf.Max(
                    requiredDistinctGames,
                    maximumCoverCandidates);

            if (!ownedGamesClient)
            {
                ownedGamesClient =
                    GetComponent<SteamOwnedGamesClient>();
            }

            if (!contentFilterClient)
            {
                contentFilterClient =
                    GetComponent<SteamAppContentFilterClient>();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            GameCaseSessionPlan.Clear();
            GameCaseSessionPlaytimePlan.Clear();

            SetLocalProgress(
                0f,
                "Preparing game libraries");

            _manifestReady.OnValueChanged +=
                HandleManifestReadyChanged;

            if (IsServer)
            {
                NetworkManager.OnClientDisconnectCallback +=
                    HandleClientDisconnected;

                StartCoroutine(
                    BuildManifestServer());
            }

            if (_manifestReady.Value)
            {
                BeginLocalPreload();
            }
        }

        public override void OnNetworkDespawn()
        {
            _manifestReady.OnValueChanged -=
                HandleManifestReadyChanged;

            if (IsServer &&
                NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -=
                    HandleClientDisconnected;
            }

            base.OnNetworkDespawn();
        }

        private IEnumerator BuildManifestServer()
        {
            if (IsSinglePlayerSession)
            {
                yield return
                    BuildSinglePlayerManifestServer();

                yield break;
            }

            float deadline =
                Time.realtimeSinceStartup +
                10f;

            while (
                SteamLobbyService.Instance == null &&
                Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!IsServer ||
                !IsSpawned)
            {
                yield break;
            }

            if (SteamLobbyService.Instance == null)
            {
                FailLoadingServer(
                    "SteamLobbyService was not ready.");

                yield break;
            }

            IReadOnlyList<SteamLobbyMember> liveMembers =
                SteamLobbyService.Instance.CurrentMembers;

            var members =
                new List<SteamLobbyMember>();

            if (liveMembers != null)
            {
                for (int i = 0;
                     i < liveMembers.Count;
                     i++)
                {
                    SteamLobbyMember member =
                        liveMembers[i];

                    if (member != null &&
                        member.SteamId != 0)
                    {
                        members.Add(member);
                    }
                }
            }

            if (members.Count == 0)
            {
                FailLoadingServer(
                    "The Steam lobby has no valid members.");

                yield break;
            }

            var results =
                new SteamOwnedGamesResult[
                    members.Count];

            bool hideNsfwGames =
                MultiplayerFlowController.Instance != null &&
                MultiplayerFlowController.Instance.HideNsfwGames;

            if (ownedGamesClient != null)
            {
                int remaining =
                    members.Count;

                int completed =
                    0;

                for (int i = 0;
                     i < members.Count;
                     i++)
                {
                    int capturedIndex =
                        i;

                    SteamLobbyMember member =
                        members[capturedIndex];

                    StartCoroutine(
                        ownedGamesClient.FetchOwnedAppIds(
                            member.SteamId,
                            result =>
                            {
                                results[capturedIndex] =
                                    result;

                                if (!result.Succeeded)
                                {
                                    Debug.LogWarning(
                                        "[GameCaseLoading] Could not read " +
                                        $"{member.PersonaName}'s library: " +
                                        result.Error,
                                        this);
                                }

                                completed++;
                                remaining--;

                                SetLocalProgress(
                                    0.30f *
                                    completed /
                                    members.Count,
                                    $"Reading Steam libraries " +
                                    $"({completed}/{members.Count})");
                            }));
                }

                while (
                    remaining > 0 &&
                    IsSpawned)
                {
                    yield return null;
                }
            }
            else
            {
                Debug.LogWarning(
                    "[GameCaseLoading] SteamOwnedGamesClient is missing.",
                    this);
            }

            if (!IsServer ||
                !IsSpawned)
            {
                yield break;
            }

            var libraryView =
                new List<IReadOnlyList<uint>>(
                    members.Count);

            for (int i = 0;
                 i < results.Length;
                 i++)
            {
                SteamOwnedGamesResult result =
                    results[i];

                libraryView.Add(
                    result != null &&
                    result.Succeeded
                        ? result.AppIds
                        : Array.Empty<uint>());
            }

            Dictionary<uint, uint>
                averagePlaytimeByAppId =
                    BuildAveragePlaytimeMap(
                        results);

            Dictionary<uint, string>
                gameNameByAppId =
                    BuildGameNameMap(
                        results);

            ulong lobbyId =
                SteamLobbyService.Instance.CurrentLobbyId;

            yield return
                BuildAndPublishManifestServer(
                    libraryView,
                    averagePlaytimeByAppId,
                    gameNameByAppId,
                    lobbyId,
                    true,
                    hideNsfwGames);
        }

        private IEnumerator BuildSinglePlayerManifestServer()
        {
            var libraryView =
                new List<IReadOnlyList<uint>>(1);

            var playtimeByAppId =
                new Dictionary<uint, uint>();

            var gameNameByAppId =
                new Dictionary<uint, string>();

            bool steamOnline =
                SteamBootstrap.IsSteamAvailable &&
                SteamBootstrap.IsSteamLoggedOn &&
                SteamBootstrap.LocalSteamId != 0;

            if (steamOnline &&
                ownedGamesClient != null)
            {
                SetLocalProgress(
                    0.05f,
                    "Reading local Steam library");

                SteamOwnedGamesResult result =
                    null;

                yield return
                    ownedGamesClient.FetchOwnedAppIds(
                        SteamBootstrap.LocalSteamId,
                        value => result = value);

                if (!IsServer ||
                    !IsSpawned)
                {
                    yield break;
                }

                if (result != null &&
                    result.Succeeded)
                {
                    libraryView.Add(
                        result.AppIds);

                    for (int i = 0;
                         i < result.Games.Count;
                         i++)
                    {
                        SteamOwnedGameData game =
                            result.Games[i];

                        playtimeByAppId[game.AppId] =
                            game.PlaytimeMinutes;

                        if (!string.IsNullOrWhiteSpace(game.Name))
                        {
                            gameNameByAppId[game.AppId] =
                                game.Name;
                        }
                    }

                    SetLocalProgress(
                        0.30f,
                        $"Found {result.AppIds.Count} owned Steam games");
                }
                else
                {
                    Debug.LogWarning(
                        "[GameCaseLoading] Local Steam library " +
                        "could not be read: " +
                        (result?.Error ??
                         "No result. Using fallback AppIds."),
                        this);
                }
            }
            else
            {
                SetLocalProgress(
                    0.30f,
                    "Steam is offline; using fallback games");
            }

            yield return
                BuildAndPublishManifestServer(
                    libraryView,
                    playtimeByAppId,
                    gameNameByAppId,
                    SteamBootstrap.LocalSteamId,
                    steamOnline,
                    false);
        }

        private IEnumerator BuildAndPublishManifestServer(
            IReadOnlyList<IReadOnlyList<uint>> libraryView,
            IReadOnlyDictionary<uint, uint> playtimeByAppId,
            IReadOnlyDictionary<uint, string> gameNameByAppId,
            ulong seedSource,
            bool validateVerticalCovers,
            bool hideNsfwGames)
        {
            uint seed =
                unchecked(
                    (uint)(
                        seedSource ^
                        (ulong)DateTime.UtcNow.Ticks));

            if (seed == 0)
            {
                seed = 1;
            }

            if (!validateVerticalCovers)
            {
                List<uint> selected =
                    GameCaseSelectionUtility.SelectDistinctFairly(
                        libraryView,
                        fallbackAppIds,
                        excludedAppIds,
                        requiredDistinctGames,
                        seed);

                if (selected.Count !=
                    requiredDistinctGames)
                {
                    FailLoadingServer(
                        $"Only {selected.Count}/" +
                        $"{requiredDistinctGames} unique AppIds available.");

                    yield break;
                }

                PublishManifestServer(
                    selected,
                    playtimeByAppId,
                    gameNameByAppId,
                    seed);

                yield break;
            }

            GameCaseCoverCache cache =
                GameCaseCoverCache.GetOrCreate();

            if (clearPreviousSessionCache)
            {
                cache.ClearCache();
            }

            cache.SetRequestsEnabled(
                true);

            _serverCoverCachePrimed =
                true;

            int requestedCandidateCount =
                Mathf.Min(
                    maximumCoverCandidates,
                    requiredDistinctGames +
                    extraCandidatesPerPass);

            int previousRawCandidateCount =
                -1;

            while (true)
            {
                List<uint> rawCandidates =
                    GameCaseSelectionUtility.SelectDistinctFairly(
                        libraryView,
                        fallbackAppIds,
                        excludedAppIds,
                        requestedCandidateCount,
                        seed);

                if (rawCandidates.Count <
                    requiredDistinctGames)
                {
                    FailLoadingServer(
                        $"Only {rawCandidates.Count} unique AppIds available.");

                    yield break;
                }

                if (previousRawCandidateCount >= 0 &&
                    rawCandidates.Count <=
                    previousRawCandidateCount)
                {
                    FailLoadingServer(
                        "Could not find enough eligible games.");

                    yield break;
                }

                previousRawCandidateCount =
                    rawCandidates.Count;

                List<uint> candidates =
                    rawCandidates;

                if (hideNsfwGames)
                {
                    if (contentFilterClient == null)
                    {
                        FailLoadingServer(
                            "Hide NSFW Games is enabled, but the Steam " +
                            "content filter client is missing.");

                        yield break;
                    }

                    SteamContentFilterResult filterResult =
                        null;

                    yield return
                        contentFilterClient.FilterAllowedAppIds(
                            rawCandidates,
                            value => filterResult = value);

                    if (!IsServer ||
                        !IsSpawned)
                    {
                        yield break;
                    }

                    if (filterResult == null ||
                        !filterResult.Succeeded)
                    {
                        FailLoadingServer(
                            filterResult?.Error ??
                            "Steam content filtering returned no result.");

                        yield break;
                    }

                    candidates =
                        new List<uint>(
                            filterResult.AllowedAppIds);

                    if (filterResult.BlockedCount > 0 ||
                        filterResult.UnknownCount > 0)
                    {
                        Debug.Log(
                            "[GameCaseLoading] NSFW filter removed " +
                            $"{filterResult.BlockedCount} adult and " +
                            $"{filterResult.UnknownCount} unverified AppIds.",
                            this);
                    }

                    if (candidates.Count <
                        requiredDistinctGames)
                    {
                        if (rawCandidates.Count <
                            requestedCandidateCount)
                        {
                            FailLoadingServer(
                                $"Only {candidates.Count}/" +
                                $"{requiredDistinctGames} verified safe " +
                                "games were available.");

                            yield break;
                        }

                        int nextSafePass =
                            Mathf.Min(
                                maximumCoverCandidates,
                                requestedCandidateCount +
                                extraCandidatesPerPass);

                        if (nextSafePass ==
                            requestedCandidateCount)
                        {
                            FailLoadingServer(
                                $"Only {candidates.Count}/" +
                                $"{requiredDistinctGames} verified safe " +
                                "games were found before reaching the " +
                                "candidate limit.");

                            yield break;
                        }

                        requestedCandidateCount =
                            nextSafePass;

                        continue;
                    }
                }

                bool preloadFinished =
                    false;

                cache.Preload(
                    candidates,
                    progress =>
                    {
                        SetLocalProgress(
                            Mathf.Lerp(
                                0.31f,
                                0.39f,
                                progress),
                            $"Checking Steam cover artwork " +
                            $"({Mathf.RoundToInt(progress * candidates.Count)}/" +
                            $"{candidates.Count})");
                    },
                    () =>
                    {
                        preloadFinished =
                            true;
                    });

                while (
                    !preloadFinished &&
                    IsSpawned)
                {
                    yield return null;
                }

                if (!IsServer ||
                    !IsSpawned)
                {
                    yield break;
                }

                var valid =
                    new List<uint>(
                        requiredDistinctGames);

                int missing =
                    0;

                for (int i = 0;
                     i < candidates.Count;
                     i++)
                {
                    uint appId =
                        candidates[i];

                    if (cache.TryGet(
                            appId,
                            out _))
                    {
                        if (valid.Count <
                            requiredDistinctGames)
                        {
                            valid.Add(
                                appId);
                        }
                    }
                    else
                    {
                        missing++;
                    }
                }

                if (valid.Count >=
                    requiredDistinctGames)
                {
                    if (missing > 0)
                    {
                        Debug.Log(
                            $"[GameCaseLoading] Skipped {missing} " +
                            "AppIds without vertical Steam artwork.",
                            this);
                    }

                    PublishManifestServer(
                        valid,
                        playtimeByAppId,
                        gameNameByAppId,
                        seed);

                    yield break;
                }

                if (rawCandidates.Count <
                    requestedCandidateCount)
                {
                    FailLoadingServer(
                        $"Only {valid.Count}/" +
                        $"{requiredDistinctGames} games had usable " +
                        "vertical artwork.");

                    yield break;
                }

                int next =
                    Mathf.Min(
                        maximumCoverCandidates,
                        requestedCandidateCount +
                        extraCandidatesPerPass);

                if (next ==
                    requestedCandidateCount)
                {
                    FailLoadingServer(
                        $"Only {valid.Count}/" +
                        $"{requiredDistinctGames} valid-cover games " +
                        "were found before reaching the candidate limit.");

                    yield break;
                }

                requestedCandidateCount =
                    next;
            }
        }

        private void PublishManifestServer(
            IReadOnlyList<uint> selected,
            IReadOnlyDictionary<uint, uint> playtimeByAppId,
            IReadOnlyDictionary<uint, string> gameNameByAppId,
            uint seed)
        {
            if (!IsServer ||
                !IsSpawned)
            {
                return;
            }

            if (selected == null ||
                selected.Count !=
                requiredDistinctGames)
            {
                FailLoadingServer(
                    "Validated game manifest has an invalid size.");

                return;
            }

            _manifestReady.Value =
                false;

            _readyClientIds.Clear();

            _selectedAppIds.Clear();
            _selectedGameNames.Clear();
            _selectedPlaytimeMinutes.Clear();

            for (int i = 0;
                 i < selected.Count;
                 i++)
            {
                uint appId =
                    selected[i];

                _selectedAppIds.Add(
                    appId);

                string gameName =
                    gameNameByAppId != null &&
                    gameNameByAppId.TryGetValue(
                        appId,
                        out string resolvedName)
                        ? resolvedName
                        : string.Empty;

                _selectedGameNames.Add(
                    ToNetworkGameName(
                        appId,
                        gameName));

                uint playtime =
                    playtimeByAppId != null &&
                    playtimeByAppId.TryGetValue(
                        appId,
                        out uint value)
                        ? value
                        : 0u;

                _selectedPlaytimeMinutes.Add(
                    playtime);
            }

            _assignmentSeed.Value =
                seed;

            _copiesPerGame.Value =
                (ushort)copiesPerGame;

            _distinctGameCount.Value =
                (ushort)requiredDistinctGames;

            _manifestReady.Value =
                true;

            SetLocalProgress(
                0.40f,
                "Game manifest ready");
        }

        private void HandleManifestReadyChanged(
            bool previous,
            bool current)
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

            _localPreloadStarted =
                true;

            StartCoroutine(
                WaitForManifestThenPreload());
        }

        private IEnumerator WaitForManifestThenPreload()
        {
            float deadline =
                Time.realtimeSinceStartup +
                10f;

            int expectedCount =
                _distinctGameCount.Value;

            while (
                (
                    expectedCount <= 0 ||
                    _selectedAppIds.Count !=
                    expectedCount ||
                    _selectedGameNames.Count !=
                    expectedCount ||
                    _selectedPlaytimeMinutes.Count !=
                    expectedCount ||
                    _copiesPerGame.Value == 0 ||
                    _assignmentSeed.Value == 0
                ) &&
                Time.realtimeSinceStartup <
                deadline)
            {
                yield return null;

                expectedCount =
                    _distinctGameCount.Value;
            }

            if (!_manifestReady.Value ||
                expectedCount <= 0 ||
                _selectedAppIds.Count != expectedCount ||
                _selectedGameNames.Count != expectedCount ||
                _selectedPlaytimeMinutes.Count != expectedCount ||
                _copiesPerGame.Value == 0 ||
                _assignmentSeed.Value == 0)
            {
                FailLoadingLocal(
                    "The replicated game manifest was incomplete.");

                yield break;
            }

            var appIds =
                new uint[expectedCount];

            var playtimes =
                new uint[expectedCount];

            var gameNames =
                new string[expectedCount];

            for (int i = 0;
                 i < expectedCount;
                 i++)
            {
                appIds[i] =
                    _selectedAppIds[i];

                gameNames[i] =
                    _selectedGameNames[i].ToString();

                playtimes[i] =
                    _selectedPlaytimeMinutes[i];
            }

            GameCaseSessionPlan.Set(
                appIds,
                gameNames,
                _copiesPerGame.Value,
                _assignmentSeed.Value);

            GameCaseSessionPlaytimePlan.Set(
                appIds,
                playtimes);

            SetLocalProgress(
                Mathf.Max(LocalProgress, 0.40f),
                "Game manifest ready");

            GameCaseCoverCache cache =
                GameCaseCoverCache.GetOrCreate();

            bool preserveValidatedHostCache =
                IsServer &&
                _serverCoverCachePrimed;

            if (clearPreviousSessionCache &&
                !preserveValidatedHostCache)
            {
                cache.ClearCache();
            }

            bool offlineSinglePlayer =
                IsSinglePlayerSession &&
                (
                    !SteamBootstrap.IsSteamAvailable ||
                    !SteamBootstrap.IsSteamLoggedOn
                );

            cache.SetRequestsEnabled(
                !offlineSinglePlayer);

            if (offlineSinglePlayer)
            {
                SetLocalProgress(
                    0.97f,
                    "Building offline cover array");

                if (!cache.BuildCoverArray(
                        appIds))
                {
                    FailLoadingLocal(
                        "Could not build offline cover array.");

                    yield break;
                }

                HandleLocalCoversReady();

                yield break;
            }

            cache.Preload(
                appIds,
                progress =>
                {
                    SetLocalProgress(
                        Mathf.Lerp(
                            0.40f,
                            0.95f,
                            progress),
                        $"Downloading covers " +
                        $"({Mathf.RoundToInt(progress * appIds.Length)}/" +
                        $"{appIds.Length})");
                },
                () =>
                {
                    SetLocalProgress(
                        0.97f,
                        "Building cover texture array");

                    if (!cache.BuildCoverArray(
                            appIds))
                    {
                        FailLoadingLocal(
                            "Could not build game case cover array.");

                        return;
                    }

                    HandleLocalCoversReady();
                });
        }

        private static Dictionary<uint, string>
            BuildGameNameMap(
                IReadOnlyList<SteamOwnedGamesResult> results)
        {
            var names =
                new Dictionary<uint, string>();

            if (results == null)
            {
                return names;
            }

            for (int i = 0;
                 i < results.Count;
                 i++)
            {
                SteamOwnedGamesResult result =
                    results[i];

                if (result == null ||
                    !result.Succeeded)
                {
                    continue;
                }

                for (int j = 0;
                     j < result.Games.Count;
                     j++)
                {
                    SteamOwnedGameData game =
                        result.Games[j];

                    if (game.AppId == 0 ||
                        string.IsNullOrWhiteSpace(game.Name) ||
                        names.ContainsKey(game.AppId))
                    {
                        continue;
                    }

                    names.Add(
                        game.AppId,
                        game.Name.Trim());
                }
            }

            return names;
        }

        private static FixedString128Bytes ToNetworkGameName(
            uint appId,
            string gameName)
        {
            string value =
                string.IsNullOrWhiteSpace(gameName)
                    ? $"App {appId}"
                    : gameName.Trim()
                        .Replace('\r', ' ')
                        .Replace('\n', ' ');

            const int maximumUtf8Bytes = 120;

            while (
                value.Length > 1 &&
                Encoding.UTF8.GetByteCount(value) >
                maximumUtf8Bytes)
            {
                int newLength =
                    value.Length - 1;

                if (newLength > 0 &&
                    char.IsHighSurrogate(
                        value[newLength - 1]))
                {
                    newLength--;
                }

                value =
                    value.Substring(
                        0,
                        Mathf.Max(1, newLength));
            }

            return new FixedString128Bytes(value);
        }

        private static Dictionary<uint, uint>
            BuildAveragePlaytimeMap(
                IReadOnlyList<SteamOwnedGamesResult> results)
        {
            var accumulators =
                new Dictionary<uint, PlaytimeAccumulator>();

            if (results == null)
            {
                return new Dictionary<uint, uint>();
            }

            for (int i = 0;
                 i < results.Count;
                 i++)
            {
                SteamOwnedGamesResult result =
                    results[i];

                if (result == null ||
                    !result.Succeeded)
                {
                    continue;
                }

                for (int j = 0;
                     j < result.Games.Count;
                     j++)
                {
                    SteamOwnedGameData game =
                        result.Games[j];

                    if (!accumulators.TryGetValue(
                            game.AppId,
                            out PlaytimeAccumulator accumulator))
                    {
                        accumulator =
                            default;
                    }

                    accumulator.TotalMinutes +=
                        game.PlaytimeMinutes;

                    accumulator.OwnerCount++;

                    accumulators[game.AppId] =
                        accumulator;
                }
            }

            var averages =
                new Dictionary<uint, uint>(
                    accumulators.Count);

            foreach (
                KeyValuePair<uint, PlaytimeAccumulator> pair
                in accumulators)
            {
                if (pair.Value.OwnerCount <= 0)
                {
                    continue;
                }

                ulong average =
                    pair.Value.TotalMinutes /
                    (ulong)pair.Value.OwnerCount;

                averages[pair.Key] =
                    average > uint.MaxValue
                        ? uint.MaxValue
                        : (uint)average;
            }

            return averages;
        }

        private void HandleLocalCoversReady()
        {
            SetLocalProgress(
                0.98f,
                "Waiting for everyone to finish packing");

            if (IsServer)
            {
                MarkClientReadyServer(
                    NetworkManager.LocalClientId);
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
            MarkClientReadyServer(
                rpcParams.Receive.SenderClientId);
        }

        private void MarkClientReadyServer(
            ulong clientId)
        {
            if (!IsServer ||
                !_manifestReady.Value ||
                NetworkManager == null ||
                !NetworkManager.ConnectedClients.ContainsKey(
                    clientId))
            {
                return;
            }

            _readyClientIds.Add(
                clientId);

            TryLoadGameplaySceneServer();
        }

        private void TryLoadGameplaySceneServer()
        {
            if (!IsServer ||
                _sceneLoadRequested ||
                NetworkManager == null ||
                !NetworkManager.IsListening)
            {
                return;
            }

            foreach (
                ulong clientId
                in NetworkManager.ConnectedClientsIds)
            {
                if (!_readyClientIds.Contains(
                        clientId))
                {
                    return;
                }
            }

            _sceneLoadRequested =
                true;

            SetLocalProgress(
                1f,
                "Opening gameplay scene");

            ShowGameplayOpeningClientRpc();

            StartCoroutine(
                LoadGameplaySceneAfterFullBar());
        }

        [ClientRpc]
        private void ShowGameplayOpeningClientRpc()
        {
            // The host already applied this locally before sending the RPC.
            if (IsServer)
            {
                return;
            }

            SetLocalProgress(
                1f,
                "Opening gameplay scene");
        }

        private IEnumerator LoadGameplaySceneAfterFullBar()
        {
            float deadline =
                Time.realtimeSinceStartup +
                Mathf.Max(0f, fullBarHoldSeconds);

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!IsServer ||
                NetworkManager == null ||
                !NetworkManager.IsListening ||
                NetworkManager.SceneManager == null)
            {
                _sceneLoadRequested = false;

                FailLoadingServer(
                    "The server stopped before gameplay could open.");

                yield break;
            }

            SceneEventProgressStatus result =
                NetworkManager.SceneManager.LoadScene(
                    gameplaySceneName,
                    LoadSceneMode.Single);

            if (result != SceneEventProgressStatus.Started)
            {
                _sceneLoadRequested = false;

                FailLoadingServer(
                    $"NGO rejected scene '{gameplaySceneName}' load: " +
                    result + ".");
            }
        }

        private void HandleClientDisconnected(
            ulong clientId)
        {
            if (!IsServer)
            {
                return;
            }

            _readyClientIds.Remove(
                clientId);

            TryLoadGameplaySceneServer();
        }

        private void FailLoadingServer(
            string message)
        {
            Debug.LogError(
                "[GameCaseLoading] " +
                message,
                this);

            LoadingFailedClientRpc(
                message);
        }

        [ClientRpc]
        private void LoadingFailedClientRpc(
            string message)
        {
            FailLoadingLocal(
                message);
        }

        private void FailLoadingLocal(
            string message)
        {
            LocalStatus =
                message;

            LocalLoadingFailed?.Invoke(
                message);

            LocalProgressChanged?.Invoke(
                LocalProgress,
                LocalStatus);
        }

        private void SetLocalProgress(
            float progress,
            string status)
        {
            LocalProgress =
                Mathf.Clamp01(
                    progress);

            LocalStatus =
                status ??
                string.Empty;

            LocalProgressChanged?.Invoke(
                LocalProgress,
                LocalStatus);

            // Before the manifest exists, only the server performs the Steam
            // and validation work. Mirror that preparation progress so remote
            // clients do not stare at an empty bar while the host is busy.
            if (IsServer &&
                IsSpawned &&
                !_manifestReady.Value)
            {
                ShowPreparationProgressClientRpc(
                    LocalProgress,
                    LocalStatus);
            }
        }

        [ClientRpc]
        private void ShowPreparationProgressClientRpc(
            float progress,
            string status)
        {
            if (IsServer)
            {
                return;
            }

            LocalProgress =
                Mathf.Clamp01(progress);

            LocalStatus =
                status ??
                string.Empty;

            LocalProgressChanged?.Invoke(
                LocalProgress,
                LocalStatus);
        }

        private struct PlaytimeAccumulator
        {
            public ulong TotalMinutes;
            public int OwnerCount;
        }
    }
}
