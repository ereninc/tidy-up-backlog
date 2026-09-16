using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class GameCaseRenderProperties :
        MonoBehaviour
    {
        private static readonly int ShellColorId =
            Shader.PropertyToID("_ShellColor");

        private static readonly int CoverIndexId =
            Shader.PropertyToID("_CoverIndex");

        [SerializeField]
        private MeshRenderer targetRenderer;

        private MaterialPropertyBlock _propertyBlock;

        private void Reset()
        {
            AutoAssign();
        }

        private void Awake()
        {
            AutoAssign();

            _propertyBlock =
                new MaterialPropertyBlock();
        }

        private void AutoAssign()
        {
            if (!targetRenderer)
            {
                targetRenderer =
                    GetComponentInChildren<MeshRenderer>(
                        true);
            }
        }

        public void Apply(
            uint appId,
            Color shellColor)
        {
            if (!targetRenderer)
            {
                return;
            }

            GameCaseCoverCache cache =
                GameCaseCoverCache.Instance;

            if (cache == null)
            {
                Debug.LogError(
                    "[GameCaseRenderProperties] " +
                    "GameCaseCoverCache is missing.",
                    this);

                return;
            }

            if (!cache.TryGetSlice(
                    appId,
                    out int coverIndex))
            {
                Debug.LogWarning(
                    $"[GameCaseRenderProperties] " +
                    $"No cover slice for AppId {appId}.",
                    this);

                coverIndex = 0;
            }

            _propertyBlock ??=
                new MaterialPropertyBlock();

            targetRenderer.GetPropertyBlock(
                _propertyBlock);

            _propertyBlock.SetColor(
                ShellColorId,
                shellColor);

            _propertyBlock.SetFloat(
                CoverIndexId,
                coverIndex);

            targetRenderer.SetPropertyBlock(
                _propertyBlock);
        }

        public void Apply(uint appId)
        {
            Apply(appId, Color.white);
        }
    }
}