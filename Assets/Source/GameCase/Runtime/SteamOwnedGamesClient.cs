using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EXW.Multiplayer
{
    public enum SteamOwnedGamesSource
    {
        BackendProxy = 0,
        DirectWebApiDevelopmentOnly = 1
    }

    public sealed class SteamOwnedGameData
    {
        public SteamOwnedGameData(
            uint appId,
            string name,
            uint playtimeMinutes)
        {
            AppId = appId;
            Name = name?.Trim() ?? string.Empty;
            PlaytimeMinutes = playtimeMinutes;
        }

        public SteamOwnedGameData(
            uint appId,
            uint playtimeMinutes)
            : this(appId, string.Empty, playtimeMinutes)
        {
        }

        public uint AppId { get; }

        public string Name { get; }

        /// <summary>
        /// Steam's playtime_forever value, in minutes.
        /// </summary>
        public uint PlaytimeMinutes { get; }

        public float PlaytimeHours =>
            PlaytimeMinutes / 60f;
    }

    public sealed class SteamOwnedGamesResult
    {
        private SteamOwnedGamesResult(
            bool succeeded,
            IReadOnlyList<SteamOwnedGameData> games,
            string error)
        {
            Succeeded = succeeded;

            Games =
                games ??
                Array.Empty<SteamOwnedGameData>();

            Error =
                error ??
                string.Empty;

            var appIds =
                new uint[Games.Count];

            for (int i = 0;
                 i < Games.Count;
                 i++)
            {
                appIds[i] =
                    Games[i].AppId;
            }

            AppIds = appIds;
        }

        public bool Succeeded { get; }

        public IReadOnlyList<SteamOwnedGameData> Games { get; }

        /// <summary>
        /// Preserved for systems which only care about AppIds.
        /// </summary>
        public IReadOnlyList<uint> AppIds { get; }

        public string Error { get; }

        public static SteamOwnedGamesResult Success(
            IReadOnlyList<SteamOwnedGameData> games)
        {
            return new SteamOwnedGamesResult(
                true,
                games,
                string.Empty);
        }

        public static SteamOwnedGamesResult Failure(
            string error)
        {
            return new SteamOwnedGamesResult(
                false,
                Array.Empty<SteamOwnedGameData>(),
                error);
        }
    }

    /// <summary>
    /// Fetches owned Steam games including display name and lifetime playtime.
    ///
    /// Production mode expects a backend proxy so a Steam Web API key
    /// is never shipped inside the game.
    ///
    /// Direct mode reads the API key from an environment variable
    /// and refuses release builds.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Steam Owned Games Client")]
    public sealed class SteamOwnedGamesClient : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        private SteamOwnedGamesSource source =
            SteamOwnedGamesSource.DirectWebApiDevelopmentOnly;

        [Tooltip(
            "Example: https://example.com/steam/owned-games?steamId={steamId}")]
        [SerializeField]
        private string backendUrlTemplate;

        [Header("Development Direct Web API")]
        [SerializeField]
        private string webApiKeyEnvironmentVariable =
            "STEAM_WEB_API_KEY";

        [SerializeField]
        private string directWebApiEndpoint =
            "https://api.steampowered.com/" +
            "IPlayerService/GetOwnedGames/v0001/";

        [Header("Request")]
        [SerializeField, Min(5)]
        private int timeoutSeconds = 20;

        public IEnumerator FetchOwnedAppIds(
            ulong steamId,
            Action<SteamOwnedGamesResult> completed)
        {
            if (steamId == 0)
            {
                completed?.Invoke(
                    SteamOwnedGamesResult.Failure(
                        "SteamId is zero."));

                yield break;
            }

            if (!TryBuildRequestUrl(
                    steamId,
                    out string url,
                    out string error))
            {
                completed?.Invoke(
                    SteamOwnedGamesResult.Failure(
                        error));

                yield break;
            }

            using (
                UnityWebRequest request =
                    UnityWebRequest.Get(url))
            {
                request.timeout =
                    Mathf.Max(
                        5,
                        timeoutSeconds);

                yield return
                    request.SendWebRequest();

                if (request.result !=
                    UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(
                        SteamOwnedGamesResult.Failure(
                            $"Owned-games request failed for " +
                            $"{steamId}: {request.error}"));

                    yield break;
                }

                if (!TryParseGames(
                        request.downloadHandler.text,
                        out IReadOnlyList<SteamOwnedGameData> games,
                        out error))
                {
                    completed?.Invoke(
                        SteamOwnedGamesResult.Failure(
                            error));

                    yield break;
                }

                completed?.Invoke(
                    SteamOwnedGamesResult.Success(
                        games));
            }
        }

        private bool TryBuildRequestUrl(
            ulong steamId,
            out string url,
            out string error)
        {
            url = string.Empty;
            error = string.Empty;

            if (source ==
                SteamOwnedGamesSource.BackendProxy)
            {
                if (string.IsNullOrWhiteSpace(
                        backendUrlTemplate) ||
                    !backendUrlTemplate.Contains(
                        "{steamId}"))
                {
                    error =
                        "Backend URL must contain the " +
                        "{steamId} placeholder.";

                    return false;
                }

                url =
                    backendUrlTemplate.Replace(
                        "{steamId}",
                        steamId.ToString());

                return true;
            }

            if (!Application.isEditor &&
                !Debug.isDebugBuild)
            {
                error =
                    "Direct Steam Web API mode is disabled " +
                    "in release builds.";

                return false;
            }

            string key =
                Environment.GetEnvironmentVariable(
                    webApiKeyEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(key))
            {
                error =
                    $"Environment variable " +
                    $"{webApiKeyEnvironmentVariable} " +
                    "is missing on the host machine.";

                return false;
            }

            string separator =
                directWebApiEndpoint.Contains("?")
                    ? "&"
                    : "?";

            url =
                directWebApiEndpoint +
                separator +
                "key=" +
                UnityWebRequest.EscapeURL(key) +
                "&steamid=" +
                steamId +
                "&include_appinfo=1" +
                "&include_played_free_games=1" +
                "&format=json";

            return true;
        }

        private static bool TryParseGames(
            string json,
            out IReadOnlyList<SteamOwnedGameData> games,
            out string error)
        {
            games =
                Array.Empty<SteamOwnedGameData>();

            error =
                string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error =
                    "Owned-games response was empty.";

                return false;
            }

            try
            {
                /*
                 * Production backend - preferred new shape:
                 *
                 * {
                 *   "games": [
                 *     {
                 *       "appId": 730,
                 *       "name": "Counter-Strike 2",
                 *       "playtimeMinutes": 12345
                 *     }
                 *   ]
                 * }
                 */
                BackendEnvelope backend =
                    JsonUtility.FromJson<BackendEnvelope>(
                        json);

                if (backend != null)
                {
                    if (backend.games != null)
                    {
                        games =
                            SanitizeBackendGames(
                                backend.games);

                        return true;
                    }

                    /*
                     * Legacy backend shape:
                     *
                     * {
                     *   "appIds": [730, ...]
                     * }
                     *
                     * Still supported, but playtime becomes 0.
                     */
                    if (backend.appIds != null)
                    {
                        games =
                            SanitizeLegacyAppIds(
                                backend.appIds);

                        return true;
                    }
                }

                /*
                 * Valve Web API:
                 *
                 * response.games[].appid
                 * response.games[].name
                 * response.games[].playtime_forever
                 */
                ValveEnvelope valve =
                    JsonUtility.FromJson<ValveEnvelope>(
                        json);

                if (valve != null &&
                    valve.response != null)
                {
                    OwnedGame[] rawGames =
                        valve.response.games;

                    if (rawGames == null)
                    {
                        games =
                            Array.Empty<SteamOwnedGameData>();

                        return true;
                    }

                    games =
                        SanitizeValveGames(
                            rawGames);

                    return true;
                }
            }
            catch (Exception exception)
            {
                error =
                    "Could not parse owned-games response: " +
                    exception.Message;

                return false;
            }

            error =
                "Owned-games response had an unknown JSON shape.";

            return false;
        }

        private static IReadOnlyList<SteamOwnedGameData>
            SanitizeValveGames(
                OwnedGame[] raw)
        {
            var result =
                new List<SteamOwnedGameData>(
                    raw.Length);

            var indices =
                new Dictionary<uint, int>();

            for (int i = 0;
                 i < raw.Length;
                 i++)
            {
                OwnedGame game =
                    raw[i];

                if (game == null ||
                    game.appid <= 0)
                {
                    continue;
                }

                uint appId =
                    (uint)game.appid;

                uint playtime =
                    game.playtime_forever > 0
                        ? (uint)game.playtime_forever
                        : 0u;

                AddOrMerge(
                    result,
                    indices,
                    appId,
                    game.name,
                    playtime);
            }

            return result;
        }

        private static IReadOnlyList<SteamOwnedGameData>
            SanitizeBackendGames(
                BackendGame[] raw)
        {
            var result =
                new List<SteamOwnedGameData>(
                    raw.Length);

            var indices =
                new Dictionary<uint, int>();

            for (int i = 0;
                 i < raw.Length;
                 i++)
            {
                BackendGame game =
                    raw[i];

                if (game == null)
                {
                    continue;
                }

                int rawAppId =
                    game.appId > 0
                        ? game.appId
                        : game.appid;

                if (rawAppId <= 0)
                {
                    continue;
                }

                int rawPlaytime =
                    game.playtimeMinutes > 0
                        ? game.playtimeMinutes
                        : game.playtime_forever;

                uint playtime =
                    rawPlaytime > 0
                        ? (uint)rawPlaytime
                        : 0u;

                AddOrMerge(
                    result,
                    indices,
                    (uint)rawAppId,
                    game.name,
                    playtime);
            }

            return result;
        }

        private static IReadOnlyList<SteamOwnedGameData>
            SanitizeLegacyAppIds(
                int[] raw)
        {
            var result =
                new List<SteamOwnedGameData>(
                    raw.Length);

            var unique =
                new HashSet<uint>();

            for (int i = 0;
                 i < raw.Length;
                 i++)
            {
                if (raw[i] <= 0)
                {
                    continue;
                }

                uint appId =
                    (uint)raw[i];

                if (!unique.Add(appId))
                {
                    continue;
                }

                result.Add(
                    new SteamOwnedGameData(
                        appId,
                        string.Empty,
                        0));
            }

            return result;
        }

        private static void AddOrMerge(
            List<SteamOwnedGameData> result,
            Dictionary<uint, int> indices,
            uint appId,
            string name,
            uint playtimeMinutes)
        {
            string sanitizedName =
                name?.Trim() ?? string.Empty;

            if (indices.TryGetValue(
                    appId,
                    out int existingIndex))
            {
                SteamOwnedGameData existing =
                    result[existingIndex];

                uint mergedPlaytime =
                    Math.Max(
                        playtimeMinutes,
                        existing.PlaytimeMinutes);

                string mergedName =
                    !string.IsNullOrWhiteSpace(existing.Name)
                        ? existing.Name
                        : sanitizedName;

                if (mergedPlaytime != existing.PlaytimeMinutes ||
                    !string.Equals(
                        mergedName,
                        existing.Name,
                        StringComparison.Ordinal))
                {
                    result[existingIndex] =
                        new SteamOwnedGameData(
                            appId,
                            mergedName,
                            mergedPlaytime);
                }

                return;
            }

            indices.Add(
                appId,
                result.Count);

            result.Add(
                new SteamOwnedGameData(
                    appId,
                    sanitizedName,
                    playtimeMinutes));
        }

        [Serializable]
        private sealed class BackendEnvelope
        {
            public BackendGame[] games;
            public int[] appIds;
        }

        [Serializable]
        private sealed class BackendGame
        {
            public int appId;

            // Supported alternate Valve-style naming.
            public int appid;

            public string name;

            public int playtimeMinutes;

            // Supported alternate Valve-style naming.
            public int playtime_forever;
        }

        [Serializable]
        private sealed class ValveEnvelope
        {
            public ValveResponse response;
        }

        [Serializable]
        private sealed class ValveResponse
        {
            public int game_count;
            public OwnedGame[] games;
        }

        [Serializable]
        private sealed class OwnedGame
        {
            public int appid;

            public string name;

            /*
             * Lifetime playtime in minutes.
             */
            public int playtime_forever;
        }
    }
}
