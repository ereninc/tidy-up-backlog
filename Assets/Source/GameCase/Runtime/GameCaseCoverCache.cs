using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Process-wide cover cache. Thirty cases of one game share one Texture2D,
    /// and duplicate requests join the same in-flight download.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Game Case Cover Cache")]
    public sealed class GameCaseCoverCache : MonoBehaviour
    {
        private const string SteamAssetRoot =
            "https://shared.fastly.steamstatic.com/" +
            "store_item_assets/steam/apps/";

        public static GameCaseCoverCache Instance { get; private set; }

        [SerializeField, Range(1, 12)] private int maxConcurrentDownloads = 4;
        [SerializeField, Min(5)] private int timeoutSeconds = 20;

        private readonly Dictionary<uint, Texture2D> _cache =
            new Dictionary<uint, Texture2D>();
        private readonly HashSet<uint> _failed = new HashSet<uint>();
        private readonly Dictionary<uint, List<Action<Texture2D>>> _pending =
            new Dictionary<uint, List<Action<Texture2D>>>();
        private readonly Queue<uint> _queue = new Queue<uint>();

        private int _activeDownloadCount;
        
        public bool RequestsEnabled { get; private set; } = true;

        public void SetRequestsEnabled(bool enabled)
        {
            RequestsEnabled = enabled;
        }

        public static GameCaseCoverCache GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var cacheObject = new GameObject("GameCaseCoverCache");
            return cacheObject.AddComponent<GameCaseCoverCache>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DestroyCachedTextures();
        }

        public bool TryGet(uint appId, out Texture2D texture)
        {
            return _cache.TryGetValue(appId, out texture) && texture != null;
        }

        public void RequestCover(
            uint appId,
            Action<Texture2D> completed)
        {
            if (!RequestsEnabled)
            {
                completed?.Invoke(null);
                return;
            }
            
            if (appId == 0)
            {
                completed?.Invoke(null);
                return;
            }

            if (TryGet(appId, out Texture2D cached))
            {
                completed?.Invoke(cached);
                return;
            }

            if (_failed.Contains(appId))
            {
                completed?.Invoke(null);
                return;
            }

            if (_pending.TryGetValue(
                    appId,
                    out List<Action<Texture2D>> callbacks))
            {
                if (completed != null)
                {
                    callbacks.Add(completed);
                }

                return;
            }

            callbacks = new List<Action<Texture2D>>();

            if (completed != null)
            {
                callbacks.Add(completed);
            }

            _pending.Add(appId, callbacks);
            _queue.Enqueue(appId);
            PumpQueue();
        }

        public void Preload(
            IReadOnlyList<uint> appIds,
            Action<float> progressChanged,
            Action completed)
        {
            if (appIds == null || appIds.Count == 0)
            {
                progressChanged?.Invoke(1f);
                completed?.Invoke();
                return;
            }

            var unique = new HashSet<uint>();

            for (int i = 0; i < appIds.Count; i++)
            {
                if (appIds[i] != 0)
                {
                    unique.Add(appIds[i]);
                }
            }

            if (unique.Count == 0)
            {
                progressChanged?.Invoke(1f);
                completed?.Invoke();
                return;
            }

            int total = unique.Count;
            int finished = 0;

            foreach (uint appId in unique)
            {
                RequestCover(appId, _ =>
                {
                    finished++;
                    progressChanged?.Invoke((float)finished / total);

                    if (finished == total)
                    {
                        completed?.Invoke();
                    }
                });
            }
        }

        public void ClearCache()
        {
            StopAllCoroutines();
            var cancelledCallbacks = new List<Action<Texture2D>>();

            foreach (List<Action<Texture2D>> callbacks in _pending.Values)
            {
                for (int i = 0; i < callbacks.Count; i++)
                {
                    if (callbacks[i] != null)
                    {
                        cancelledCallbacks.Add(callbacks[i]);
                    }
                }
            }

            _pending.Clear();
            _queue.Clear();
            _failed.Clear();
            _activeDownloadCount = 0;
            DestroyCachedTextures();
            _cache.Clear();

            for (int i = 0; i < cancelledCallbacks.Count; i++)
            {
                cancelledCallbacks[i](null);
            }
        }

        private void PumpQueue()
        {
            int limit = Mathf.Max(1, maxConcurrentDownloads);

            while (_activeDownloadCount < limit && _queue.Count > 0)
            {
                uint appId = _queue.Dequeue();
                _activeDownloadCount++;
                StartCoroutine(DownloadCover(appId));
            }
        }

        private IEnumerator DownloadCover(uint appId)
        {
            Texture2D texture = null;
            string[] fileNames =
            {
                "library_600x900.jpg",
                "library_600x900_2x.jpg"
            };

            for (int i = 0; i < fileNames.Length && texture == null; i++)
            {
                string url = SteamAssetRoot + appId + "/" + fileNames[i];

                using (UnityWebRequest request =
                       UnityWebRequestTexture.GetTexture(url, true))
                {
                    request.timeout = Mathf.Max(5, timeoutSeconds);
                    yield return request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        texture = DownloadHandlerTexture.GetContent(request);
                    }
                }
            }

            if (texture != null)
            {
                texture.name = $"SteamCover_{appId}";
                _cache[appId] = texture;
            }
            else
            {
                _failed.Add(appId);
                Debug.LogWarning(
                    $"[GameCaseCoverCache] No vertical cover for AppId {appId}.",
                    this);
            }

            if (_pending.TryGetValue(
                    appId,
                    out List<Action<Texture2D>> callbacks))
            {
                _pending.Remove(appId);

                for (int i = 0; i < callbacks.Count; i++)
                {
                    callbacks[i]?.Invoke(texture);
                }
            }

            _activeDownloadCount = Mathf.Max(
                0,
                _activeDownloadCount - 1);
            PumpQueue();
        }

        private void DestroyCachedTextures()
        {
            foreach (Texture2D texture in _cache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }
    }
}
