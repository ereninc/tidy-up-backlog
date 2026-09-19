using System;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One local-only shelf ghost. It searches the local carry stack from top
    /// to bottom and exposes the exact stack item used by the preview.
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
            Array.Empty<Renderer>();

        private NetworkGameCaseShelfDestination _focusedDestination;

        public NetworkGameCaseShelfDestination FocusedDestination =>
            _focusedDestination;

        public int PreviewStackIndex { get; private set; } = -1;
        public NetworkWorldItem PreviewItem { get; private set; }

        /// <summary>
        /// Local-only event. Index uses carrier order: 0 is bottom and
        /// HeldItemCount - 1 is the visible top.
        /// </summary>
        public event Action<int, NetworkWorldItem> PreviewSelectionChanged;

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
            ClearPreviewSelection();
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
            ClearPreviewSelection();
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
            ClearPreviewSelection();
        }

        private void RefreshPreview()
        {
            NetworkItemCarrier carrier = NetworkItemCarrier.Local;

            if (_focusedDestination == null ||
                carrier == null ||
                !TryFindPreview(
                    carrier,
                    out int stackIndex,
                    out NetworkWorldItem selectedItem,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                ClearPreviewSelection();
                return;
            }

            SetPreviewSelection(stackIndex, selectedItem);
            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetRenderersVisible(true);
        }

        private bool TryFindPreview(
            NetworkItemCarrier carrier,
            out int stackIndex,
            out NetworkWorldItem selectedItem,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            stackIndex = -1;
            selectedItem = null;
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

                if (!_focusedDestination.TryGetPreviewWorldPose(
                        gameCase,
                        out worldPosition,
                        out worldRotation))
                {
                    continue;
                }

                stackIndex = i;
                selectedItem = heldItem;
                return true;
            }

            return false;
        }

        private void SetPreviewSelection(
            int stackIndex,
            NetworkWorldItem selectedItem)
        {
            SetRenderersVisible(true);

            if (PreviewStackIndex == stackIndex &&
                PreviewItem == selectedItem)
            {
                return;
            }

            PreviewStackIndex = stackIndex;
            PreviewItem = selectedItem;
            PreviewSelectionChanged?.Invoke(
                PreviewStackIndex,
                PreviewItem);
        }

        private void ClearPreviewSelection()
        {
            SetRenderersVisible(false);

            if (PreviewStackIndex == -1 && PreviewItem == null)
            {
                return;
            }

            PreviewStackIndex = -1;
            PreviewItem = null;
            PreviewSelectionChanged?.Invoke(-1, null);
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
