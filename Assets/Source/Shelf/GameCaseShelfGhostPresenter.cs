using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local-only preview for the next valid shelf position. Assign this
    /// GameObject as NetworkItemReceiver's Local Focus Visual. It never sends
    /// network messages and never parents the real held item.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Ghost Presenter")]
    public sealed class GameCaseShelfGhostPresenter : MonoBehaviour
    {
        [Header("Shelf")]
        [SerializeField]
        private NetworkGameCaseShelfDestination destination;

        [Header("Ghost Renderers")]
        [Tooltip("All renderers hidden when the held case is not valid here.")]
        [SerializeField]
        private Renderer[] controlledRenderers =
            System.Array.Empty<Renderer>();

        [SerializeField]
        private Renderer artworkRenderer;

        [SerializeField, Min(0)]
        private int artworkMaterialIndex = 1;

        [SerializeField]
        private string artworkTextureProperty = "_BaseMap";

        private MaterialPropertyBlock _propertyBlock;
        private int _artworkTexturePropertyId;
        private uint _requestedAppId;
        private uint _requestVersion;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            _artworkTexturePropertyId = Shader.PropertyToID(
                string.IsNullOrWhiteSpace(artworkTextureProperty)
                    ? "_BaseMap"
                    : artworkTextureProperty);
        }

        private void OnEnable()
        {
            RefreshPreview(true);
        }

        private void Update()
        {
            // This object is enabled only while locally focused, so polling is
            // cheap and also handles held-item changes without new base hooks.
            RefreshPreview(false);
        }

        private void OnDisable()
        {
            _requestVersion++;
            SetRenderersVisible(false);
        }

        private void RefreshPreview(bool forceCoverRefresh)
        {
            NetworkItemCarrier carrier = NetworkItemCarrier.Local;

            if (carrier == null ||
                !carrier.TryGetHeldItem(out NetworkWorldItem heldItem) ||
                heldItem == null ||
                !heldItem.TryGetComponent(out NetworkGameCase gameCase) ||
                destination == null ||
                !destination.TryGetPreviewWorldPose(
                    gameCase,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                SetRenderersVisible(false);
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetRenderersVisible(true);

            if (forceCoverRefresh || _requestedAppId != gameCase.AppId)
            {
                RequestCover(gameCase.AppId);
            }
        }

        private void RequestCover(uint appId)
        {
            _requestedAppId = appId;
            _requestVersion++;
            uint capturedVersion = _requestVersion;

            GameCaseCoverCache.GetOrCreate().RequestCover(
                appId,
                texture =>
                {
                    if (this == null || texture == null ||
                        capturedVersion != _requestVersion ||
                        _requestedAppId != appId)
                    {
                        return;
                    }

                    ApplyCover(texture);
                });
        }

        private void ApplyCover(Texture texture)
        {
            if (artworkRenderer == null)
            {
                return;
            }

            Material[] materials = artworkRenderer.sharedMaterials;

            if (artworkMaterialIndex < 0 ||
                artworkMaterialIndex >= materials.Length)
            {
                Debug.LogError(
                    $"Ghost artwork material index {artworkMaterialIndex} " +
                    $"is invalid on {artworkRenderer.name}.",
                    this);
                return;
            }

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
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

        private void SetRenderersVisible(bool visible)
        {
            if (controlledRenderers == null)
            {
                return;
            }

            for (int i = 0; i < controlledRenderers.Length; i++)
            {
                if (controlledRenderers[i] != null)
                {
                    controlledRenderers[i].enabled = visible;
                }
            }
        }

        private void ResolveReferences()
        {
            if (destination == null)
            {
                destination = GetComponentInParent<
                    NetworkGameCaseShelfDestination>(true);
            }

            if (controlledRenderers == null ||
                controlledRenderers.Length == 0)
            {
                controlledRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (artworkRenderer == null && controlledRenderers != null &&
                controlledRenderers.Length > 0)
            {
                artworkRenderer = controlledRenderers[0];
            }
        }
    }
}
