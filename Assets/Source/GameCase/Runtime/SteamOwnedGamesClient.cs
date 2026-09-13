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

    public sealed class SteamOwnedGamesResult
    {
        private SteamOwnedGamesResult(
            bool succeeded,
            IReadOnlyList<uint> appIds,
            string error)
        {
            Succeeded = succeeded;
            AppIds = appIds ?? Array.Empty<uint>();
            Error = error ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<uint> AppIds { get; }
        public string Error { get; }

        public static SteamOwnedGamesResult Success(
            IReadOnlyList<uint> appIds)
        {
            return new SteamOwnedGamesResult(true, appIds, string.Empty);
        }

        public static SteamOwnedGamesResult Failure(string error)
        {
            return new SteamOwnedGamesResult(
                false,
                Array.Empty<uint>(),
                error);
        }
    }

    /// <summary>
    /// Fetches only AppIds. Production mode expects a backend proxy so a Steam
    /// Web API key is never shipped inside the game. The direct mode reads the
    /// key from a host-machine environment variable and refuses release builds.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Steam Owned Games Client")]
    public sealed class SteamOwnedGamesClient : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField]
        private SteamOwnedGamesSource source =
            SteamOwnedGamesSource.DirectWebApiDevelopmentOnly;

        [Tooltip(
            "Example: https://example.com/steam/owned-games?steamId={steamId}")]
        [SerializeField] private string backendUrlTemplate;

        [Header("Development Direct Web API")]
        [SerializeField]
        private string webApiKeyEnvironmentVariable = "STEAM_WEB_API_KEY";

        [SerializeField]
        private string directWebApiEndpoint =
            "https://api.steampowered.com/" +
            "IPlayerService/GetOwnedGames/v0001/";

        [Header("Request")]
        [SerializeField, Min(5)] private int timeoutSeconds = 20;

        public IEnumerator FetchOwnedAppIds(
            ulong steamId,
            Action<SteamOwnedGamesResult> completed)
        {
            if (steamId == 0)
            {
                completed?.Invoke(
                    SteamOwnedGamesResult.Failure("SteamId is zero."));
                yield break;
            }

            if (!TryBuildRequestUrl(steamId, out string url, out string error))
            {
                completed?.Invoke(SteamOwnedGamesResult.Failure(error));
                yield break;
            }

            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = Mathf.Max(5, timeoutSeconds);
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    completed?.Invoke(SteamOwnedGamesResult.Failure(
                        $"Owned-games request failed for {steamId}: " +
                        request.error));
                    yield break;
                }

                if (!TryParseAppIds(
                        request.downloadHandler.text,
                        out IReadOnlyList<uint> appIds,
                        out error))
                {
                    completed?.Invoke(SteamOwnedGamesResult.Failure(error));
                    yield break;
                }

                completed?.Invoke(SteamOwnedGamesResult.Success(appIds));
            }
        }

        private bool TryBuildRequestUrl(
            ulong steamId,
            out string url,
            out string error)
        {
            url = string.Empty;
            error = string.Empty;

            if (source == SteamOwnedGamesSource.BackendProxy)
            {
                if (string.IsNullOrWhiteSpace(backendUrlTemplate) ||
                    !backendUrlTemplate.Contains("{steamId}"))
                {
                    error =
                        "Backend URL must contain the {steamId} placeholder.";
                    return false;
                }

                url = backendUrlTemplate.Replace(
                    "{steamId}",
                    steamId.ToString());
                return true;
            }

            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                error =
                    "Direct Steam Web API mode is disabled in release builds.";
                return false;
            }

            string key = Environment.GetEnvironmentVariable(
                webApiKeyEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(key))
            {
                error =
                    $"Environment variable {webApiKeyEnvironmentVariable} " +
                    "is missing on the host machine.";
                return false;
            }

            string separator = directWebApiEndpoint.Contains("?") ? "&" : "?";
            url = directWebApiEndpoint + separator +
                  "key=" + UnityWebRequest.EscapeURL(key) +
                  "&steamid=" + steamId +
                  "&include_appinfo=0" +
                  "&include_played_free_games=1" +
                  "&format=json";
            return true;
        }

        private static bool TryParseAppIds(
            string json,
            out IReadOnlyList<uint> appIds,
            out string error)
        {
            appIds = Array.Empty<uint>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Owned-games response was empty.";
                return false;
            }

            try
            {
                BackendEnvelope backend =
                    JsonUtility.FromJson<BackendEnvelope>(json);

                if (backend != null && backend.appIds != null)
                {
                    appIds = Sanitize(backend.appIds);
                    return true;
                }

                ValveEnvelope valve = JsonUtility.FromJson<ValveEnvelope>(json);

                if (valve != null && valve.response != null)
                {
                    OwnedGame[] games = valve.response.games;

                    if (games == null)
                    {
                        appIds = Array.Empty<uint>();
                        return true;
                    }

                    var raw = new int[games.Length];

                    for (int i = 0; i < games.Length; i++)
                    {
                        raw[i] = games[i].appid;
                    }

                    appIds = Sanitize(raw);
                    return true;
                }
            }
            catch (Exception exception)
            {
                error = "Could not parse owned-games response: " +
                        exception.Message;
                return false;
            }

            error = "Owned-games response had an unknown JSON shape.";
            return false;
        }

        private static IReadOnlyList<uint> Sanitize(int[] raw)
        {
            var unique = new HashSet<uint>();
            var result = new List<uint>(raw.Length);

            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] <= 0)
                {
                    continue;
                }

                uint appId = (uint)raw[i];

                if (unique.Add(appId))
                {
                    result.Add(appId);
                }
            }

            return result;
        }

        [Serializable]
        private sealed class BackendEnvelope
        {
            public int[] appIds;
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
        }
    }
}
