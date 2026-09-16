using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Network Game Case")]
    public sealed class NetworkGameCase : MonoBehaviour
    {
        private static readonly int CoverIndexId =
            Shader.PropertyToID("_CoverIndex");

        private static readonly int ShellColorId =
            Shader.PropertyToID("_ShellColor");

        [Header("Scene Identity")]
        [SerializeField, Min(0)]
        private int sceneCaseIndex;

        [Header("Combined Rendering")]
        [FormerlySerializedAs("artworkRenderer")]
        [SerializeField]
        private Renderer combinedRenderer;

        [Header("Playtime Colors")]

        [Tooltip("Unplayed / almost unplayed.")]
        [SerializeField]
        private Color lowPlaytimeColor =
            new Color(
                0.015f,
                0.035f,
                0.16f,
                1f);

        [Tooltip("Regularly played.")]
        [SerializeField]
        private Color mediumPlaytimeColor =
            new Color(
                0.02f,
                0.22f,
                0.95f,
                1f);

        [Tooltip("Heavily played.")]
        [SerializeField]
        private Color highPlaytimeColor =
            new Color(
                1f,
                0.55f,
                0.03f,
                1f);

        [Tooltip(
            "At this many hours the case reaches full blue.")]
        [SerializeField, Min(0.1f)]
        private float blueAtHours = 20f;

        [Tooltip(
            "At this many hours the case reaches full gold.")]
        [SerializeField, Min(1f)]
        private float goldAtHours = 500f;

        public int SceneCaseIndex =>
            sceneCaseIndex;

        public uint AppId { get; private set; }

        public uint PlaytimeMinutes { get; private set; }

        public float PlaytimeHours =>
            PlaytimeMinutes / 60f;

        public bool IsBound =>
            AppId != 0;

        public Color CurrentShellColor { get; private set; }

        public event Action<uint, uint> AppIdChanged;

        private MaterialPropertyBlock _propertyBlock;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void Awake()
        {
            AutoAssignReferences();

            _propertyBlock =
                new MaterialPropertyBlock();
        }

        private void OnValidate()
        {
            blueAtHours =
                Mathf.Max(
                    0.1f,
                    blueAtHours);

            goldAtHours =
                Mathf.Max(
                    blueAtHours + 0.1f,
                    goldAtHours);

            AutoAssignReferences();
        }

        private void AutoAssignReferences()
        {
            if (!combinedRenderer)
            {
                combinedRenderer =
                    GetComponentInChildren<Renderer>(
                        true);
            }
        }

        public void BindAppId(
            uint appId)
        {
            uint playtimeMinutes =
                GameCaseSessionPlaytimePlan
                    .GetOrDefault(appId);

            BindAppId(
                appId,
                playtimeMinutes);
        }

        public void BindAppId(
            uint appId,
            uint playtimeMinutes)
        {
            if (appId == 0)
            {
                Debug.LogError(
                    "[NetworkGameCase] Cannot bind zero AppId.",
                    this);

                return;
            }

            uint previous =
                AppId;

            bool appChanged =
                AppId != appId;

            AppId =
                appId;

            PlaytimeMinutes =
                playtimeMinutes;

            ApplyVisualState();

            if (appChanged)
            {
                AppIdChanged?.Invoke(
                    previous,
                    AppId);
            }
        }

        public void RefreshVisual()
        {
            if (!IsBound)
            {
                return;
            }

            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            if (!combinedRenderer)
            {
                Debug.LogError(
                    "[NetworkGameCase] Combined Renderer is missing.",
                    this);

                return;
            }

            GameCaseCoverCache cache =
                GameCaseCoverCache.Instance;

            if (cache == null)
            {
                Debug.LogError(
                    "[NetworkGameCase] GameCaseCoverCache is missing.",
                    this);

                return;
            }

            if (!cache.TryGetSlice(
                    AppId,
                    out int coverIndex))
            {
                Debug.LogError(
                    $"[NetworkGameCase] No cover slice for AppId {AppId}.",
                    this);

                return;
            }

            EnsurePropertyBlock();

            /*
             * Keep any unrelated properties already on the renderer.
             */
            combinedRenderer.GetPropertyBlock(
                _propertyBlock);

            /*
             * Cover Texture2DArray slice.
             */
            _propertyBlock.SetFloat(
                CoverIndexId,
                coverIndex);

            /*
             * Shell playtime color.
             */
            CurrentShellColor =
                EvaluatePlaytimeColor(
                    PlaytimeMinutes);

            _propertyBlock.SetColor(
                ShellColorId,
                CurrentShellColor);

            combinedRenderer.SetPropertyBlock(
                _propertyBlock);
        }

        private Color EvaluatePlaytimeColor(
            uint playtimeMinutes)
        {
            float hours =
                playtimeMinutes / 60f;

            /*
             * 0h -> BlueAtHours
             *
             * Navy -> Blue
             */
            if (hours <= blueAtHours)
            {
                float time =
                    Mathf.Clamp01(
                        hours /
                        blueAtHours);

                time =
                    Mathf.Sqrt(time);

                return Color.Lerp(
                    lowPlaytimeColor,
                    mediumPlaytimeColor,
                    time);
            }

            /*
             * BlueAtHours -> GoldAtHours
             *
             * Blue -> Gold
             */
            float range =
                Mathf.Max(
                    0.01f,
                    goldAtHours -
                    blueAtHours);

            float elapsed =
                Mathf.Clamp(
                    hours -
                    blueAtHours,
                    0f,
                    range);

            float numerator =
                Mathf.Log10(
                    1f + elapsed);

            float denominator =
                Mathf.Log10(
                    1f + range);

            float t =
                denominator > 0f
                    ? numerator /
                      denominator
                    : 1f;

            return Color.Lerp(
                mediumPlaytimeColor,
                highPlaytimeColor,
                Mathf.Clamp01(t));
        }

        private void EnsurePropertyBlock()
        {
            if (_propertyBlock == null)
            {
                _propertyBlock =
                    new MaterialPropertyBlock();
            }
        }

#if UNITY_EDITOR

        [ContextMenu("Debug / Reapply Visual")]
        private void EditorReapplyVisual()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            RefreshVisual();

            Debug.Log(
                $"[NetworkGameCase] " +
                $"AppId={AppId}, " +
                $"Playtime={PlaytimeHours:0.0}h, " +
                $"ShellColor={CurrentShellColor}",
                this);
        }

        public void EditorSetSceneCaseIndex(
            int index)
        {
            sceneCaseIndex =
                Mathf.Max(
                    0,
                    index);
        }

#endif
    }
}