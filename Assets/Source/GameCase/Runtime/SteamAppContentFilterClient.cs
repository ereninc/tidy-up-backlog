using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace EXW.Multiplayer
{
    public sealed class SteamContentFilterResult
    {
        private SteamContentFilterResult(
            bool succeeded,
            IReadOnlyList<uint> allowedAppIds,
            int blockedCount,
            int unknownCount,
            string error)
        {
            Succeeded = succeeded;
            AllowedAppIds =
                allowedAppIds ?? Array.Empty<uint>();
            BlockedCount = blockedCount;
            UnknownCount = unknownCount;
            Error = error ?? string.Empty;
        }

        public bool Succeeded { get; }
        public IReadOnlyList<uint> AllowedAppIds { get; }
        public int BlockedCount { get; }
        public int UnknownCount { get; }
        public string Error { get; }

        public static SteamContentFilterResult Success(
            IReadOnlyList<uint> allowedAppIds,
            int blockedCount,
            int unknownCount)
        {
            return new SteamContentFilterResult(
                true,
                allowedAppIds,
                blockedCount,
                unknownCount,
                string.Empty);
        }

        public static SteamContentFilterResult Failure(
            string error)
        {
            return new SteamContentFilterResult(
                false,
                Array.Empty<uint>(),
                0,
                0,
                error);
        }
    }

    /// <summary>
    /// Checks candidate Steam AppIds through the server-side content filter.
    /// Results are cached for the lifetime of this persistent runtime object.
    /// Unknown apps are fail-closed and are not returned as allowed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Steam App Content Filter Client")]
    public sealed class SteamAppContentFilterClient : MonoBehaviour
    {
        private enum CachedDecision : byte
        {
            Allowed = 1,
            Blocked = 2,
            Unknown = 3
        }

        [Tooltip(
            "Example: https://your-project.vercel.app/api/steam-content-filter")]
        [SerializeField]
        private string endpoint;

        [SerializeField, Min(1)]
        private int appIdsPerRequest = 60;

        [SerializeField, Min(5)]
        private int timeoutSeconds = 30;

        private readonly Dictionary<uint, CachedDecision>
            _cache =
                new Dictionary<uint, CachedDecision>();

        public IEnumerator FilterAllowedAppIds(
            IReadOnlyList<uint> appIds,
            Action<SteamContentFilterResult> completed)
        {
            if (appIds == null ||
                appIds.Count == 0)
            {
                completed?.Invoke(
                    SteamContentFilterResult.Success(
                        Array.Empty<uint>(),
                        0,
                        0));

                yield break;
            }

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                completed?.Invoke(
                    SteamContentFilterResult.Failure(
                        "Steam content filter endpoint is missing."));

                yield break;
            }

            var unique =
                new List<uint>(appIds.Count);

            var seen =
                new HashSet<uint>();

            for (int i = 0;
                 i < appIds.Count;
                 i++)
            {
                uint appId =
                    appIds[i];

                if (appId != 0 &&
                    seen.Add(appId))
                {
                    unique.Add(appId);
                }
            }

            var uncached =
                new List<uint>();

            for (int i = 0;
                 i < unique.Count;
                 i++)
            {
                if (!_cache.ContainsKey(unique[i]))
                {
                    uncached.Add(unique[i]);
                }
            }

            int batchSize =
                Mathf.Max(1, appIdsPerRequest);

            for (int offset = 0;
                 offset < uncached.Count;
                 offset += batchSize)
            {
                int count =
                    Mathf.Min(
                        batchSize,
                        uncached.Count - offset);

                var payload =
                    new FilterRequest
                    {
                        appIds =
                            new int[count]
                    };

                for (int i = 0;
                     i < count;
                     i++)
                {
                    payload.appIds[i] =
                        unchecked((int)uncached[offset + i]);
                }

                byte[] body =
                    Encoding.UTF8.GetBytes(
                        JsonUtility.ToJson(payload));

                using (
                    var request =
                        new UnityWebRequest(
                            endpoint,
                            UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler =
                        new UploadHandlerRaw(body);

                    request.downloadHandler =
                        new DownloadHandlerBuffer();

                    request.timeout =
                        Mathf.Max(5, timeoutSeconds);

                    request.SetRequestHeader(
                        "Content-Type",
                        "application/json");

                    yield return
                        request.SendWebRequest();

                    if (request.result !=
                        UnityWebRequest.Result.Success)
                    {
                        completed?.Invoke(
                            SteamContentFilterResult.Failure(
                                "Steam content filter request failed: " +
                                request.error));

                        yield break;
                    }

                    FilterResponse response;

                    try
                    {
                        response =
                            JsonUtility.FromJson<FilterResponse>(
                                request.downloadHandler.text);
                    }
                    catch (Exception exception)
                    {
                        completed?.Invoke(
                            SteamContentFilterResult.Failure(
                                "Could not parse Steam content filter " +
                                "response: " + exception.Message));

                        yield break;
                    }

                    if (response == null)
                    {
                        completed?.Invoke(
                            SteamContentFilterResult.Failure(
                                "Steam content filter returned an empty " +
                                "response."));

                        yield break;
                    }

                    Cache(
                        response.allowedAppIds,
                        CachedDecision.Allowed);

                    Cache(
                        response.blockedAppIds,
                        CachedDecision.Blocked);

                    Cache(
                        response.unknownAppIds,
                        CachedDecision.Unknown);

                    for (int i = 0;
                         i < count;
                         i++)
                    {
                        uint requested =
                            uncached[offset + i];

                        if (!_cache.ContainsKey(requested))
                        {
                            _cache[requested] =
                                CachedDecision.Unknown;
                        }
                    }
                }
            }

            var allowed =
                new List<uint>(unique.Count);

            int blockedCount =
                0;

            int unknownCount =
                0;

            for (int i = 0;
                 i < unique.Count;
                 i++)
            {
                uint appId =
                    unique[i];

                if (!_cache.TryGetValue(
                        appId,
                        out CachedDecision decision))
                {
                    unknownCount++;
                    continue;
                }

                switch (decision)
                {
                    case CachedDecision.Allowed:
                        allowed.Add(appId);
                        break;

                    case CachedDecision.Blocked:
                        blockedCount++;
                        break;

                    default:
                        unknownCount++;
                        break;
                }
            }

            completed?.Invoke(
                SteamContentFilterResult.Success(
                    allowed,
                    blockedCount,
                    unknownCount));
        }

        private void Cache(
            int[] appIds,
            CachedDecision decision)
        {
            if (appIds == null)
            {
                return;
            }

            for (int i = 0;
                 i < appIds.Length;
                 i++)
            {
                if (appIds[i] > 0)
                {
                    _cache[(uint)appIds[i]] =
                        decision;
                }
            }
        }

        [Serializable]
        private sealed class FilterRequest
        {
            public int[] appIds;
        }

        [Serializable]
        private sealed class FilterResponse
        {
            public int[] allowedAppIds;
            public int[] blockedAppIds;
            public int[] unknownAppIds;
        }
    }
}
