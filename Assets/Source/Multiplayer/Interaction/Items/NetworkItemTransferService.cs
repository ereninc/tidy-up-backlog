using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Synchronous server-only item transactions. Rules and final transforms
    /// stay authoritative; an optional NetworkItemMotionPresenter sends one
    /// transition cue so each peer draws the movement locally.
    /// </summary>
    public static class NetworkItemTransferService
    {
        public static bool TryPickup(
            NetworkItemCarrier carrier,
            NetworkWorldItem item,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!ValidateServerPair(carrier, item, out resultMessage))
            {
                return false;
            }

            return carrier.HasHeldItem
                ? TrySwap(carrier, item, out resultMessage)
                : TryPickupIntoEmptyHands(carrier, item, out resultMessage);
        }

        /// <summary>
        /// One-slot carry behavior: the current item is safely dropped using
        /// the carrier's normal drop calculation, then the target is picked up.
        /// If the second step unexpectedly fails, the old item is restored when
        /// possible instead of silently leaving the player's hands empty.
        /// </summary>
        private static bool TrySwap(
            NetworkItemCarrier carrier,
            NetworkWorldItem targetItem,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!carrier.TryGetHeldItem(out NetworkWorldItem previousItem) ||
                previousItem == null)
            {
                resultMessage = "The currently held item could not be resolved.";
                return false;
            }

            if (previousItem == targetItem)
            {
                resultMessage = "You are already holding this item.";
                return false;
            }

            if (!previousItem.IsHeld ||
                previousItem.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and held item state do not agree.";
                return false;
            }

            NetworkCarryable targetCarryable =
                targetItem.GetComponent<NetworkCarryable>();

            if (targetCarryable == null ||
                !targetCarryable.CanPickUpServer(
                    targetItem,
                    out resultMessage))
            {
                return false;
            }

            string previousName = previousItem.DisplayName;
            string targetName = targetItem.DisplayName;

            if (!TryDrop(carrier, out string dropMessage))
            {
                resultMessage = "Swap could not drop the current item: " +
                                dropMessage;
                return false;
            }

            if (TryPickupIntoEmptyHands(
                    carrier,
                    targetItem,
                    out string pickupMessage))
            {
                resultMessage = $"Swapped {previousName} for {targetName}.";
                return true;
            }

            bool restoredPrevious = TryPickupIntoEmptyHands(
                carrier,
                previousItem,
                out string restoreMessage);

            resultMessage = restoredPrevious
                ? $"Could not pick up {targetName}: {pickupMessage} " +
                  $"{previousName} was restored."
                : $"Could not pick up {targetName}: {pickupMessage} " +
                  $"The previous item also could not be restored: " +
                  restoreMessage;
            return false;
        }

        private static bool TryPickupIntoEmptyHands(
            NetworkItemCarrier carrier,
            NetworkWorldItem item,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!ValidateServerPair(carrier, item, out resultMessage))
            {
                return false;
            }

            if (carrier.HasHeldItem)
            {
                resultMessage = "The carry slot is still occupied.";
                return false;
            }

            NetworkCarryable carryable = item.GetComponent<NetworkCarryable>();

            if (carryable == null ||
                !carryable.CanPickUpServer(item, out resultMessage))
            {
                return false;
            }

            if (!carrier.TryGetCarryLocalPose(
                    carryable,
                    out Vector3 localPosition,
                    out Quaternion localRotation))
            {
                resultMessage = "Carry anchor produced an invalid pose.";
                return false;
            }

            NetworkItemReceiver previousReceiver = null;

            if (item.IsPlaced &&
                !item.TryResolveReceiver(out previousReceiver))
            {
                resultMessage =
                    "Item placement receiver could not be resolved.";
                return false;
            }

            NetworkItemMotionPresenter motion = CaptureMotionStart(
                item,
                out Vector3 visualStartPosition,
                out Quaternion visualStartRotation);
            uint revision = item.NextRevisionServer();

            item.PrepareParentPoseServer(localPosition, localRotation);

            if (!item.NetworkObject.TrySetParent(
                    carrier.NetworkObject,
                    false))
            {
                item.CancelPreparedParentPoseServer();
                resultMessage = "NGO could not parent the item to the player.";
                return false;
            }

            item.ApplyPreparedParentPoseServer();
            previousReceiver?.ReleaseItemServer(item);
            item.SetLocationServer(NetworkItemLocationState.Held(
                carrier.OwnerClientId,
                revision));
            carrier.SetHeldItemServer(item);
            PlayMotion(
                carrier,
                item,
                motion,
                visualStartPosition,
                visualStartRotation,
                revision);

            resultMessage = $"Picked up {item.DisplayName}.";
            return true;
        }

        public static bool TryDrop(
            NetworkItemCarrier carrier,
            out string resultMessage,
            bool disconnectCleanup = false)
        {
            resultMessage = string.Empty;

            bool cleanupCanRun = disconnectCleanup &&
                                 carrier != null &&
                                 carrier.NetworkManager != null &&
                                 carrier.NetworkManager.IsServer;

            if (carrier == null ||
                (!carrier.IsSpawned && !cleanupCanRun) ||
                (!carrier.IsServer && !cleanupCanRun))
            {
                resultMessage = "Carrier is not ready on the server.";
                return false;
            }

            NetworkWorldItem item;
            bool resolvedItem = cleanupCanRun
                ? carrier.TryGetHeldItemForServerCleanup(out item)
                : carrier.TryGetHeldItem(out item);

            if (!resolvedItem || item == null || !item.IsSpawned)
            {
                if (carrier.IsSpawned)
                {
                    carrier.ClearHeldItemServer(null);
                }

                resultMessage = "No spawned item is being carried.";
                return false;
            }

            if (!item.IsHeld ||
                item.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and item state do not agree.";
                return false;
            }

            NetworkCarryable carryable = item.GetComponent<NetworkCarryable>();

            if (!carrier.TryGetDefaultDropPoseServer(
                    item,
                    carryable,
                    out Vector3 worldPosition,
                    out Quaternion worldRotation))
            {
                resultMessage = "Could not calculate a safe drop pose.";
                return false;
            }

            NetworkItemMotionPresenter motion = CaptureMotionStart(
                item,
                out Vector3 visualStartPosition,
                out Quaternion visualStartRotation);
            uint revision = item.NextRevisionServer();

            item.PrepareParentPoseServer(worldPosition, worldRotation);
            bool detached = item.transform.parent == null ||
                            item.NetworkObject.TryRemoveParent(false);

            if (!detached)
            {
                item.CancelPreparedParentPoseServer();
                resultMessage = "NGO could not detach the item from the player.";
                return false;
            }

            item.ApplyPreparedParentPoseServer();
            item.SetLocationServer(NetworkItemLocationState.World(revision));

            if (carrier.IsSpawned)
            {
                carrier.ClearHeldItemServer(item);
            }

            PlayMotion(
                carrier,
                item,
                motion,
                visualStartPosition,
                visualStartRotation,
                revision);

            resultMessage = disconnectCleanup
                ? $"Dropped {item.DisplayName} during disconnect cleanup."
                : $"Dropped {item.DisplayName}.";
            return true;
        }

        public static bool TryReceive(
            NetworkItemCarrier carrier,
            NetworkItemReceiver receiver,
            NetworkInteractionContext context,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (carrier == null || receiver == null ||
                !carrier.IsSpawned || !receiver.IsSpawned ||
                !carrier.IsServer || !receiver.IsServer)
            {
                resultMessage = "Item receiver is not ready on the server.";
                return false;
            }

            if (!carrier.TryGetHeldItem(out NetworkWorldItem item) ||
                !ValidateServerPair(carrier, item, out resultMessage))
            {
                return false;
            }

            if (!item.IsHeld ||
                item.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and item state do not agree.";
                return false;
            }

            if (!receiver.TryBuildPlacementPlanServer(
                    item,
                    carrier,
                    context,
                    out NetworkItemPlacementPlan plan,
                    out resultMessage))
            {
                return false;
            }

            if (plan.Disposition ==
                NetworkItemDestinationDisposition.Consume)
            {
                string itemName = item.DisplayName;
                carrier.ClearHeldItemServer(item);
                receiver.CommitPlacementServer(item, plan);
                item.NetworkObject.Despawn(true);
                resultMessage = $"Discarded {itemName}.";
                return true;
            }

            NetworkItemMotionPresenter motion = CaptureMotionStart(
                item,
                out Vector3 visualStartPosition,
                out Quaternion visualStartRotation);
            uint revision = item.NextRevisionServer();

            item.PrepareParentPoseServer(
                plan.LocalPosition,
                plan.LocalRotation);

            if (!item.NetworkObject.TrySetParent(
                    receiver.NetworkObject,
                    false))
            {
                item.CancelPreparedParentPoseServer();
                resultMessage = "NGO could not parent the item to this receiver.";
                return false;
            }

            item.ApplyPreparedParentPoseServer();
            receiver.CommitPlacementServer(item, plan);
            item.SetLocationServer(NetworkItemLocationState.Placed(
                receiver.NetworkObject,
                plan.SlotIndex,
                revision));
            carrier.ClearHeldItemServer(item);
            PlayMotion(
                carrier,
                item,
                motion,
                visualStartPosition,
                visualStartRotation,
                revision);

            resultMessage = $"Placed {item.DisplayName}.";
            return true;
        }

        private static NetworkItemMotionPresenter CaptureMotionStart(
            NetworkWorldItem item,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (item == null ||
                !item.TryGetComponent(
                    out NetworkItemMotionPresenter motion) ||
                !motion.TryCaptureCurrentVisualPose(
                    out worldPosition,
                    out worldRotation))
            {
                return null;
            }

            return motion;
        }

        private static void PlayMotion(
            NetworkItemCarrier carrier,
            NetworkWorldItem item,
            NetworkItemMotionPresenter motion,
            Vector3 startPosition,
            Quaternion startRotation,
            uint targetRevision)
        {
            if (carrier != null && motion != null)
            {
                carrier.BroadcastItemMotionServer(
                    item,
                    startPosition,
                    startRotation,
                    targetRevision);
            }
        }

        private static bool ValidateServerPair(
            NetworkItemCarrier carrier,
            NetworkWorldItem item,
            out string rejectionMessage)
        {
            rejectionMessage = string.Empty;

            if (carrier == null || !carrier.IsSpawned || !carrier.IsServer)
            {
                rejectionMessage = "Carrier is not ready on the server.";
                return false;
            }

            if (item == null || !item.IsSpawned || !item.IsServer)
            {
                rejectionMessage = "Item is not ready on the server.";
                return false;
            }

            NetworkManager manager = carrier.NetworkManager;

            if (manager == null || manager != item.NetworkManager ||
                !manager.IsListening)
            {
                rejectionMessage =
                    "Carrier and item are not in the same session.";
                return false;
            }

            return true;
        }
    }
}
