using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One local-only ghost shared by every game-case shelf slot in the scene.
    /// It only moves the configured visual to the focused slot's next valid
    /// pose and toggles its renderers. The visual keeps its own material.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Ghost Presenter")]
    public sealed class GameCaseShelfGhostPresenter : MonoBehaviour
    {
        public static GameCaseShelfGhostPresenter Instance { get; private set; }

        [Header("Ghost Renderers")]
        [Tooltip("All renderers owned by this single global ghost.")]
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

        /// <summary>
        /// Called locally by the focused slot's signal object.
        /// </summary>
        public void Focus(NetworkGameCaseShelfDestination destination)
        {
            if (!isActiveAndEnabled || destination == null)
            {
                return;
            }

            _focusedDestination = destination;
            RefreshPreview();
        }

        /// <summary>
        /// Clears focus only when the caller still owns the current focus.
        /// This keeps slot-to-slot hand-off safe in either callback order.
        /// </summary>
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
                !carrier.TryGetHeldItem(out NetworkWorldItem heldItem) ||
                heldItem == null ||
                !heldItem.TryGetComponent(out NetworkGameCase gameCase) ||
                !_focusedDestination.TryGetPreviewWorldPose(
                    gameCase,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                SetRenderersVisible(false);
                return;
            }

            transform.SetPositionAndRotation(worldPosition, worldRotation);
            SetRenderersVisible(true);
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
                controlledRenderers = GetComponentsInChildren<Renderer>(true);
            }
        }
    }
}
