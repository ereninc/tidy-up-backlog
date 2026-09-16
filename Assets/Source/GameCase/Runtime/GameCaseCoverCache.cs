using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Process-wide Steam game cover cache.
    ///
    /// - Duplicate AppId requests share one download.
    /// - Missing vertical covers are remembered silently.
    /// - Runtime Texture2DArray is built once from the final validated manifest.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Game Case Cover Cache")]
    public sealed class GameCaseCoverCache : MonoBehaviour
    {
        private const string SteamAssetRoot =
            "https://shared.fastly.steamstatic.com/" +
            "store_item_assets/steam/apps/";

        private static readonly int GlobalCoverArrayId =
            Shader.PropertyToID("_GameCaseCoverArray");

        public static GameCaseCoverCache Instance { get; private set; }

        [Header("Downloads")]
        [SerializeField, Range(1, 12)]
        private int maxConcurrentDownloads = 4;

        [SerializeField, Min(5)]
        private int timeoutSeconds = 20;

        [Header("Runtime Cover Array")]
        [SerializeField, Min(64)]
        private int coverArrayWidth = 256;

        [SerializeField, Min(96)]
        private int coverArrayHeight = 384;

        private readonly Dictionary<uint, Texture2D> _cache =
            new Dictionary<uint, Texture2D>();

        private readonly HashSet<uint> _failed =
            new HashSet<uint>();

        private readonly Dictionary<
            uint,
            List<Action<Texture2D>>> _pending =
            new Dictionary<uint, List<Action<Texture2D>>>();

        private readonly Queue<uint> _queue =
            new Queue<uint>();

        private readonly Dictionary<uint, int> _sliceByAppId =
            new Dictionary<uint, int>();

        private Texture2DArray _coverArray;

        private int _activeDownloadCount;

        public bool RequestsEnabled { get; private set; } = true;

        public Texture2DArray CoverArray => _coverArray;

        public bool HasCoverArray =>
            _coverArray != null;

        public int CachedCoverCount =>
            _cache.Count;

        public int FailedCoverCount =>
            _failed.Count;

        public void SetRequestsEnabled(
            bool enabled)
        {
            RequestsEnabled = enabled;
        }

        public static GameCaseCoverCache GetOrCreate()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var cacheObject =
                new GameObject(
                    "GameCaseCoverCache");

            return cacheObject
                .AddComponent<GameCaseCoverCache>();
        }

        private void Awake()
        {
            if (Instance != null &&
                Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            DontDestroyOnLoad(
                gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            DestroyCoverArray();
            DestroyCachedTextures();
        }

        public bool TryGet(
            uint appId,
            out Texture2D texture)
        {
            return
                _cache.TryGetValue(
                    appId,
                    out texture) &&
                texture != null;
        }

        public bool IsKnownMissing(
            uint appId)
        {
            return
                _failed.Contains(appId);
        }

        public bool TryGetSlice(
            uint appId,
            out int sliceIndex)
        {
            return
                _sliceByAppId.TryGetValue(
                    appId,
                    out sliceIndex);
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

            if (TryGet(
                    appId,
                    out Texture2D cached))
            {
                completed?.Invoke(cached);
                return;
            }

            /*
             * Daha önce CDN'de cover olmadığı doğrulandıysa
             * tekrar request atma.
             */
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

            callbacks =
                new List<Action<Texture2D>>();

            if (completed != null)
            {
                callbacks.Add(completed);
            }

            _pending.Add(
                appId,
                callbacks);

            _queue.Enqueue(appId);

            PumpQueue();
        }

        public void Preload(
            IReadOnlyList<uint> appIds,
            Action<float> progressChanged,
            Action completed)
        {
            if (appIds == null ||
                appIds.Count == 0)
            {
                progressChanged?.Invoke(1f);
                completed?.Invoke();
                return;
            }

            var unique =
                new HashSet<uint>();

            for (int i = 0;
                 i < appIds.Count;
                 i++)
            {
                uint appId =
                    appIds[i];

                if (appId != 0)
                {
                    unique.Add(appId);
                }
            }

            if (unique.Count == 0)
            {
                progressChanged?.Invoke(1f);
                completed?.Invoke();
                return;
            }

            int total =
                unique.Count;

            int finished =
                0;

            foreach (uint appId in unique)
            {
                RequestCover(
                    appId,
                    _ =>
                    {
                        finished++;

                        progressChanged?.Invoke(
                            (float)finished /
                            total);

                        if (finished >= total)
                        {
                            completed?.Invoke();
                        }
                    });
            }
        }

        public bool BuildCoverArray(
            IReadOnlyList<uint> orderedAppIds)
        {
            if (orderedAppIds == null ||
                orderedAppIds.Count == 0)
            {
                Debug.LogError(
                    "[GameCaseCoverCache] Cannot build cover array: " +
                    "AppId list is empty.",
                    this);

                return false;
            }

            DestroyCoverArray();

            int width =
                Mathf.Max(
                    64,
                    coverArrayWidth);

            int height =
                Mathf.Max(
                    96,
                    coverArrayHeight);

            int depth =
                orderedAppIds.Count;

            _coverArray =
                new Texture2DArray(
                    width,
                    height,
                    depth,
                    TextureFormat.RGBA32,
                    true,
                    false)
                {
                    name =
                        "RuntimeGameCaseCoverArray",

                    filterMode =
                        FilterMode.Bilinear,

                    wrapMode =
                        TextureWrapMode.Clamp,

                    anisoLevel = 2
                };

            RenderTexture temporaryRenderTexture =
                RenderTexture.GetTemporary(
                    width,
                    height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB);

            Texture2D readbackTexture =
                new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    false,
                    false);

            Color32[] fallbackPixels =
                CreateFallbackPixels(
                    width,
                    height);

            RenderTexture previous =
                RenderTexture.active;

            int missingFinalCovers =
                0;

            try
            {
                for (int i = 0;
                     i < orderedAppIds.Count;
                     i++)
                {
                    uint appId =
                        orderedAppIds[i];

                    _sliceByAppId[appId] =
                        i;

                    if (TryGet(
                            appId,
                            out Texture2D sourceTexture))
                    {
                        Graphics.Blit(
                            sourceTexture,
                            temporaryRenderTexture);

                        RenderTexture.active =
                            temporaryRenderTexture;

                        readbackTexture.ReadPixels(
                            new Rect(
                                0,
                                0,
                                width,
                                height),
                            0,
                            0,
                            false);

                        readbackTexture.Apply(
                            false,
                            false);

                        _coverArray.SetPixels32(
                            readbackTexture.GetPixels32(),
                            i,
                            0);
                    }
                    else
                    {
                        /*
                         * Online normal akışta buraya artık
                         * girmememiz gerekiyor.
                         *
                         * Client'ta transient network failure
                         * veya offline mode için fallback kalıyor.
                         */
                        missingFinalCovers++;

                        _coverArray.SetPixels32(
                            fallbackPixels,
                            i,
                            0);
                    }
                }
            }
            finally
            {
                RenderTexture.active =
                    previous;

                RenderTexture.ReleaseTemporary(
                    temporaryRenderTexture);

                Destroy(
                    readbackTexture);
            }

            _coverArray.Apply(
                updateMipmaps: true,
                makeNoLongerReadable: true);

            Shader.SetGlobalTexture(
                GlobalCoverArrayId,
                _coverArray);

            Debug.Log(
                $"[GameCaseCoverCache] Built runtime cover array: " +
                $"{width}x{height}, " +
                $"{depth} slices. " +
                $"Missing final covers: {missingFinalCovers}.",
                this);

            return true;
        }

        public void ClearCache()
        {
            StopAllCoroutines();

            var cancelledCallbacks =
                new List<Action<Texture2D>>();

            foreach (
                List<Action<Texture2D>> callbacks
                in _pending.Values)
            {
                for (int i = 0;
                     i < callbacks.Count;
                     i++)
                {
                    if (callbacks[i] != null)
                    {
                        cancelledCallbacks.Add(
                            callbacks[i]);
                    }
                }
            }

            _pending.Clear();
            _queue.Clear();
            _failed.Clear();

            _activeDownloadCount =
                0;

            DestroyCoverArray();
            DestroyCachedTextures();

            _cache.Clear();

            for (int i = 0;
                 i < cancelledCallbacks.Count;
                 i++)
            {
                cancelledCallbacks[i](null);
            }
        }

        private void PumpQueue()
        {
            int limit =
                Mathf.Max(
                    1,
                    maxConcurrentDownloads);

            while (
                _activeDownloadCount < limit &&
                _queue.Count > 0)
            {
                uint appId =
                    _queue.Dequeue();

                _activeDownloadCount++;

                StartCoroutine(
                    DownloadCover(appId));
            }
        }

        private IEnumerator DownloadCover(
            uint appId)
        {
            Texture2D texture =
                null;

            /*
             * Sadece vertical library cover kabul ediyoruz.
             * Horizontal header/capsule kullanmıyoruz.
             */
            string[] fileNames =
            {
                "library_600x900.jpg",
                "library_600x900_2x.jpg"
            };

            for (int i = 0;
                 i < fileNames.Length &&
                 texture == null;
                 i++)
            {
                string url =
                    SteamAssetRoot +
                    appId +
                    "/" +
                    fileNames[i];

                using (
                    UnityWebRequest request =
                        UnityWebRequestTexture.GetTexture(
                            url,
                            true))
                {
                    request.timeout =
                        Mathf.Max(
                            5,
                            timeoutSeconds);

                    yield return
                        request.SendWebRequest();

                    if (request.result ==
                        UnityWebRequest.Result.Success)
                    {
                        texture =
                            DownloadHandlerTexture
                                .GetContent(request);
                    }
                }
            }

            if (texture != null)
            {
                texture.name =
                    $"SteamCover_{appId}";

                _cache[appId] =
                    texture;
            }
            else
            {
                /*
                 * Bilerek warning atmıyoruz.
                 *
                 * Steam library'de vertical artwork olmayan
                 * AppId normal bir durum; selection coordinator
                 * bunun yerine başka oyun seçecek.
                 */
                _failed.Add(appId);
            }

            if (_pending.TryGetValue(
                    appId,
                    out List<Action<Texture2D>> callbacks))
            {
                _pending.Remove(appId);

                for (int i = 0;
                     i < callbacks.Count;
                     i++)
                {
                    callbacks[i]?.Invoke(
                        texture);
                }
            }

            _activeDownloadCount =
                Mathf.Max(
                    0,
                    _activeDownloadCount - 1);

            PumpQueue();
        }

        private static Color32[]
            CreateFallbackPixels(
                int width,
                int height)
        {
            var pixels =
                new Color32[
                    width * height];

            var color =
                new Color32(
                    45,
                    45,
                    45,
                    255);

            for (int i = 0;
                 i < pixels.Length;
                 i++)
            {
                pixels[i] =
                    color;
            }

            return pixels;
        }

        private void DestroyCoverArray()
        {
            if (_coverArray != null)
            {
                Destroy(
                    _coverArray);

                _coverArray =
                    null;
            }

            _sliceByAppId.Clear();

            Shader.SetGlobalTexture(
                GlobalCoverArrayId,
                null);
        }

        private void DestroyCachedTextures()
        {
            foreach (
                Texture2D texture
                in _cache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
        }
    }
}