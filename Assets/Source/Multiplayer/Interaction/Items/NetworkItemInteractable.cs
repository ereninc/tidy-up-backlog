using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkWorldItem))]
    [RequireComponent(typeof(NetworkCarryable))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Interactable")]
    public sealed class NetworkItemInteractable : NetworkInteractable
    {
        [InfoBox(
            "Pickup pushes into the replicated carry stack. When the stack is " +
            "full, interacting swaps only its top item.")]
        [TitleGroup("Item"), Required]
        [SerializeField] private NetworkWorldItem item;

        [TitleGroup("Item"), Required]
        [SerializeField] private NetworkCarryable carryable;

        protected override void Reset()
        {
            base.Reset();
            AutoAssignReferences();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignReferences();
        }

        public override bool IsAvailableLocally(
            NetworkInteractionController interactor)
        {
            return base.IsAvailableLocally(interactor) &&
                   item != null && !item.IsHeld;
        }

        public override string GetInteractionDisplayName(
            NetworkInteractionController interactor)
        {
            return item != null
                ? item.DisplayName
                : base.GetInteractionDisplayName(interactor);
        }

        public override string GetInteractionPrompt(
            NetworkInteractionController interactor)
        {
            NetworkItemCarrier carrier = interactor != null
                ? interactor.GetComponent<NetworkItemCarrier>()
                : null;

            if (carrier == null)
            {
                return "Carrier Missing";
            }

            if (carrier.ContainsHeldItem(item))
            {
                return "Holding";
            }

            return carrier.IsCarryLimitReached()
                ? "Swap Top"
                : $"Pick Up ({carrier.HeldItemCount}/{carrier.CarryLimit})";
        }

        protected override bool CanInteractServer(
            NetworkInteractionContext context,
            out string rejectionMessage)
        {
            AutoAssignReferences();

            NetworkItemCarrier carrier = context.PlayerController != null
                ? context.PlayerController.GetComponent<NetworkItemCarrier>()
                : null;

            if (carrier == null)
            {
                rejectionMessage = "Player has no NetworkItemCarrier.";
                return false;
            }

            if (item == null || carryable == null)
            {
                rejectionMessage = "Item carry components are incomplete.";
                return false;
            }

            if (carrier.ContainsHeldItem(item))
            {
                rejectionMessage = "You are already holding this item.";
                return false;
            }

            return carryable.CanPickUpServer(item, out rejectionMessage);
        }

        protected override bool ExecuteInteractionServer(
            NetworkInteractionContext context,
            out string resultMessage)
        {
            NetworkItemCarrier carrier =
                context.PlayerController.GetComponent<NetworkItemCarrier>();

            return NetworkItemTransferService.TryPickup(
                carrier,
                item,
                out resultMessage);
        }

        [Button("AUTO ASSIGN REFERENCES")]
        private void AutoAssignReferences()
        {
            if (item == null)
            {
                item = GetComponent<NetworkWorldItem>();
            }

            if (carryable == null)
            {
                carryable = GetComponent<NetworkCarryable>();
            }
        }
    }
}
