using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One local-only shelf ghost. It searches the local carry stack from top
    /// to bottom and previews the first game case accepted by the focused slot.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Ghost Presenter")]
    public sealed class GameCaseShelfGhostPresenter : MonoBehaviour
    {
        public static GameCaseShelfGhostPresenter Instance { get; private set; }

        [Header("Ghost Renderers")]
        [SerializeField]
        private Renderer[] controlledRenderers =
            System.Array.Empty<Renderer>();

        private NetworkGameCaseShelfDestination _focusedDestination;

        public NetworkGameCaseShelfDestination FocusedDestination =>
            _focusedDestination;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError(
                    "[GameCaseShelf] More than one global shelf ghost exists " +
                    "in this scene. The duplicate was disabled.",
                    this);
                enabled = false;
                SetRenderersVisible(false);
                return;
            }

            Instance = this;
            ResolveReferences();
            SetRenderersVisible(false);
        }

        private void OnEnable()
        {
            SetRenderersVisible(false);
        }

        private void Update()
        {
            RefreshPreview();
        }

        private void OnDisable()
        {
            _focusedDestination = null;
            SetRenderersVisible(false);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void Focus(NetworkGameCaseShelfDestination destination)
        {
            if (!isActiveAndEnabled || destination == null)
            {
                return;
            }

            _focusedDestination = destination;
            RefreshPreview();
        }

        public void ClearFocus(NetworkGameCaseShelfDestination destination)
        {
            if (_focusedDestination != destination)
            {
                return;
            }

            _focusedDestination = null;
            SetRenderersVisible(false);
        }

        private void RefreshPreview()
        {
            NetworkItemCarrier carrier = NetworkItemCarrier.Local;

            if (_focusedDestination == null ||
                carrier == null ||
                !TryFindPreviewPose(
                    carrier,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                SetRenderersVisible(false);
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetRenderersVisible(true);
        }

        private bool TryFindPreviewPose(
            NetworkItemCarrier carrier,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            for (int i = carrier.HeldItemCount - 1; i >= 0; i--)
            {
                if (!carrier.TryGetHeldItemAt(
                        i,
                        out NetworkWorldItem heldItem) ||
                    !heldItem.TryGetComponent(
                        out NetworkGameCase gameCase))
                {
                    continue;
                }

                if (_focusedDestination.TryGetPreviewWorldPose(
                        gameCase,
                        out worldPosition,
                        out worldRotation))
                {
                    return true;
                }
            }

            return false;
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
            if (controlledRenderers == null ||
                controlledRenderers.Length == 0)
            {
                controlledRenderers =
                    GetComponentsInChildren<Renderer>(true);
            }
        }
    }
}
