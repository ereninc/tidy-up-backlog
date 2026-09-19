using UnityEngine;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Interaction/Interaction Outline Feedback")]
    public sealed class InteractionOutlineFeedback :
        MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private NetworkInteractable interactable;

        [SerializeField]
        private Renderer outlineRenderer;
        
        private void Reset()
        {
            AutoAssignReferences();
        }

        private void Awake()
        {
            AutoAssignReferences();
        }

        private void OnEnable()
        {
            AutoAssignReferences();

            if (interactable != null)
            {
                interactable.LocalFocusChanged +=
                    HandleFocusChanged;
            }

            Apply(
                interactable != null &&
                interactable.IsLocallyFocused);
        }

        private void OnDisable()
        {
            if (interactable != null)
            {
                interactable.LocalFocusChanged -=
                    HandleFocusChanged;
            }

            HideOutline();
        }

        public void AutoAssignReferences()
        {
            if (!interactable)
            {
                interactable =
                    GetComponent<NetworkInteractable>();
            }

            if (!outlineRenderer)
            {
                outlineRenderer =
                    GetComponentInChildren<MeshRenderer>(
                        true);
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
            if (!outlineRenderer)
            {
                return;
            }

            GlobalInteractionOutlineProxy proxy =
                GlobalInteractionOutlineProxy.Instance;

            if (!proxy)
            {
                return;
            }

            if (focused)
            {
                proxy.Show(outlineRenderer);
            }
            else
            {
                proxy.Hide(outlineRenderer);
            }
        }

        private void HideOutline()
        {
            if (!outlineRenderer)
            {
                return;
            }

            GlobalInteractionOutlineProxy.Instance?.Hide(
                outlineRenderer);
        }
    }
}