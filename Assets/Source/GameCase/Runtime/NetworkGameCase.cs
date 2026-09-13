using System;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Game-specific data/presentation composed beside NetworkWorldItem.
    /// AppId is derived from the replicated session manifest and the serialized
    /// scene index; it deliberately does not add one NetworkVariable per case.
    /// </summary>
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Network Game Case")]
    public sealed class NetworkGameCase : MonoBehaviour
    {
        [Header("Scene Identity")]
        [SerializeField, Min(0)] private int sceneCaseIndex;

        [Header("Artwork")]
        [SerializeField] private Renderer artworkRenderer;

        [Tooltip("Shell=0 and artwork=1 on one renderer: leave this at 1.")]
        [SerializeField, Min(0)] private int artworkMaterialIndex = 1;

        [Tooltip("URP Lit: _BaseMap. Built-in Standard: _MainTex.")]
        [SerializeField] private string artworkTextureProperty = "_BaseMap";

        [Header("Optional Shell Variation")]
        [SerializeField] private bool tintShellFromAppId;
        [SerializeField, Min(0)] private int shellMaterialIndex;
        [SerializeField] private string shellColorProperty = "_BaseColor";
        [SerializeField, Range(0f, 1f)] private float shellSaturation = 0.65f;
        [SerializeField, Range(0f, 1f)] private float shellValue = 0.85f;

        public int SceneCaseIndex => sceneCaseIndex;
        public uint AppId { get; private set; }
        public bool IsBound => AppId != 0;

        public event Action<uint, uint> AppIdChanged;

        private MaterialPropertyBlock _propertyBlock;
        private int _artworkTexturePropertyId;
        private int _shellColorPropertyId;
        private uint _coverRequestVersion;

        private void Reset()
        {
            artworkRenderer = GetComponentInChildren<Renderer>(true);
        }

        private void Awake()
        {
            if (artworkRenderer == null)
            {
                artworkRenderer = GetComponentInChildren<Renderer>(true);
            }

            if (string.IsNullOrWhiteSpace(artworkTextureProperty))
            {
                artworkTextureProperty = "_BaseMap";
            }

            if (string.IsNullOrWhiteSpace(shellColorProperty))
            {
                shellColorProperty = "_BaseColor";
            }

            _artworkTexturePropertyId =
                Shader.PropertyToID(artworkTextureProperty);
            _shellColorPropertyId =
                Shader.PropertyToID(shellColorProperty);
        }

        public void BindAppId(uint appId)
        {
            if (appId == 0)
            {
                Debug.LogError("Cannot bind a zero Steam AppId.", this);
                return;
            }

            if (AppId == appId)
            {
                return;
            }

            uint previous = AppId;
            AppId = appId;
            _coverRequestVersion++;
            uint requestVersion = _coverRequestVersion;

            if (tintShellFromAppId)
            {
                ApplyShellTint(appId);
            }

            AppIdChanged?.Invoke(previous, AppId);

            GameCaseCoverCache.GetOrCreate().RequestCover(
                appId,
                texture =>
                {
                    if (this == null ||
                        texture == null ||
                        AppId != appId ||
                        _coverRequestVersion != requestVersion)
                    {
                        return;
                    }

                    ApplyCoverTexture(texture);
                });
        }

        public void ApplyCoverTexture(Texture texture)
        {
            if (!TryPrepareMaterialSlot(artworkMaterialIndex))
            {
                return;
            }

            artworkRenderer.GetPropertyBlock(
                _propertyBlock,
                artworkMaterialIndex);
            _propertyBlock.SetTexture(
                _artworkTexturePropertyId,
                texture);
            artworkRenderer.SetPropertyBlock(
                _propertyBlock,
                artworkMaterialIndex);
        }

        private void ApplyShellTint(uint appId)
        {
            if (!TryPrepareMaterialSlot(shellMaterialIndex))
            {
                return;
            }

            float hue = HashToUnitFloat(appId);
            Color color = Color.HSVToRGB(
                hue,
                shellSaturation,
                shellValue);

            artworkRenderer.GetPropertyBlock(
                _propertyBlock,
                shellMaterialIndex);
            _propertyBlock.SetColor(_shellColorPropertyId, color);
            artworkRenderer.SetPropertyBlock(
                _propertyBlock,
                shellMaterialIndex);
        }

        private bool TryPrepareMaterialSlot(int materialIndex)
        {
            if (artworkRenderer == null)
            {
                return false;
            }

            Material[] materials = artworkRenderer.sharedMaterials;

            if (materialIndex < 0 || materialIndex >= materials.Length)
            {
                Debug.LogError(
                    $"Material index {materialIndex} is invalid on " +
                    artworkRenderer.name + ".",
                    this);
                return false;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            return true;
        }

        private static float HashToUnitFloat(uint value)
        {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777216f;
        }

#if UNITY_EDITOR
        public void EditorSetSceneCaseIndex(int index)
        {
            sceneCaseIndex = Mathf.Max(0, index);
        }
#endif
    }
}
