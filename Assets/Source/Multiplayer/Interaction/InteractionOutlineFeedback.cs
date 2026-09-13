using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Interaction/Interaction Outline Feedback")]
    public sealed class InteractionOutlineFeedback : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NetworkInteractable interactable;
        [SerializeField] private global::exOutline outline;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void Awake()
        {
            AutoAssignReferences();
            HideOutline();
        }

        private void OnEnable()
        {
            AutoAssignReferences();

            if (interactable != null)
            {
                interactable.LocalFocusChanged += HandleFocusChanged;
            }

            Apply(interactable != null && interactable.IsLocallyFocused);
        }

        private void OnDisable()
        {
            if (interactable != null)
            {
                interactable.LocalFocusChanged -= HandleFocusChanged;
            }

            HideOutline();
        }

        public void AutoAssignReferences()
        {
            if (interactable == null)
            {
                interactable = GetComponent<NetworkInteractable>();
            }

            if (outline == null)
            {
                outline = GetComponentInChildren<global::exOutline>(true);
            }
        }

        private void HandleFocusChanged(
            bool focused,
            NetworkInteractionController interactor)
        {
            Apply(focused);
        }

        private void Apply(bool focused)
        {
            if (outline == null)
            {
                return;
            }

            if (focused)
            {
                outline.OnSelected();
            }
            else
            {
                outline.OnHide();
            }
        }

        private void HideOutline()
        {
            if (outline != null)
            {
                outline.OnHide();
            }
        }
    }
}