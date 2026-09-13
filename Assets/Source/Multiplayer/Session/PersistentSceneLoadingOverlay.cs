using TMPro;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Optional persistent presentation adapter for gameplay scene transitions.
    /// Put this component on NetworkRuntime and assign a child overlay root.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Session/Persistent Scene Loading Overlay")]
    public sealed class PersistentSceneLoadingOverlay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject overlayRoot;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Behaviour")]
        [SerializeField] private string defaultMessage = "Loading...";

        private MultiplayerSessionCoordinator _coordinator;
        private float _nextBindAttempt;

        private void Awake()
        {
            SetVisible(false, string.Empty);
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void Update()
        {
            if (_coordinator == MultiplayerSessionCoordinator.Instance &&
                _coordinator != null)
            {
                return;
            }

            if (Time.unscaledTime >= _nextBindAttempt)
            {
                _nextBindAttempt = Time.unscaledTime + 0.25f;
                TryBind();
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        public bool ValidateSetup(bool logResult = true)
        {
            bool valid = overlayRoot != null && overlayRoot != gameObject;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log("[SceneLoadingOverlay] PASS: overlay root assigned.", this);
                }
                else
                {
                    Debug.LogError(
                        "[SceneLoadingOverlay] Assign a child GameObject as Overlay Root. " +
                        "Do not assign NetworkRuntime itself.",
                        this);
                }
            }

            return valid;
        }

        private void TryBind()
        {
            MultiplayerSessionCoordinator coordinator =
                MultiplayerSessionCoordinator.Instance;

            if (_coordinator == coordinator)
            {
                Refresh();
                return;
            }

            Unbind();
            _coordinator = coordinator;

            if (_coordinator == null)
            {
                return;
            }

            _coordinator.StateChanged += HandleStateChanged;
            _coordinator.StatusChanged += HandleStatusChanged;
            Refresh();
        }

        private void Unbind()
        {
            if (_coordinator != null)
            {
                _coordinator.StateChanged -= HandleStateChanged;
                _coordinator.StatusChanged -= HandleStatusChanged;
            }

            _coordinator = null;
        }

        private void HandleStateChanged(GameSessionState state)
        {
            Refresh();
        }

        private void HandleStatusChanged(string message)
        {
            Refresh();
        }

        private void Refresh()
        {
            bool visible = _coordinator != null && _coordinator.IsLoading;
            string message = _coordinator != null
                ? _coordinator.StatusMessage
                : defaultMessage;

            SetVisible(visible, message);
        }

        private void SetVisible(bool visible, string message)
        {
            if (messageText != null && visible)
            {
                messageText.text = string.IsNullOrWhiteSpace(message)
                    ? defaultMessage
                    : message;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }

            if (overlayRoot != null && overlayRoot != gameObject)
            {
                overlayRoot.SetActive(visible);
            }
        }
    }
}
