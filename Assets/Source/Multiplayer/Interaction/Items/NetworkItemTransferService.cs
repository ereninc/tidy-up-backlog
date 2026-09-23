using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Synchronous, server-only stack transactions. The last stack item is the
    /// default drop/swap target, while receivers may choose a matching item from
    /// anywhere in the stack.
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

            return carrier.IsCarryLimitReached()
                ? TrySwapTop(carrier, item, out resultMessage)
                : TryPush(carrier, item, out resultMessage);
        }

        private static bool TrySwapTop(
            NetworkItemCarrier carrier,
            NetworkWorldItem targetItem,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!carrier.TryGetHeldItem(out NetworkWorldItem previousItem) ||
                previousItem == null)
            {
                resultMessage = "The top stack item could not be resolved.";
                return false;
            }

            if (previousItem == targetItem ||
                carrier.ContainsHeldItem(targetItem))
            {
                resultMessage = "You are already holding this item.";
                return false;
            }

            if (!previousItem.IsHeld ||
                previousItem.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and top item state do not agree.";
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
                resultMessage = "Swap could not drop the top item: " +
                                dropMessage;
                return false;
            }

            if (TryPush(carrier, targetItem, out string pickupMessage))
            {
                resultMessage = $"Swapped {previousName} for {targetName}.";
                return true;
            }

            bool restoredPrevious = TryPush(
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

        private static bool TryPush(
            NetworkItemCarrier carrier,
            NetworkWorldItem item,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!ValidateServerPair(carrier, item, out resultMessage))
            {
                return false;
            }

            if (carrier.IsCarryLimitReached())
            {
                resultMessage = "The carry stack is full.";
                return false;
            }

            if (carrier.ContainsHeldItem(item))
            {
                resultMessage = "This item is already in the carry stack.";
                return false;
            }

            NetworkCarryable carryable = item.GetComponent<NetworkCarryable>();

            if (carryable == null ||
                !carryable.CanPickUpServer(item, out resultMessage))
            {
                return false;
            }

            int stackIndex = carrier.HeldItemCount;
            if (!carrier.TryGetCarryLocalPose(
                    carryable,
                    stackIndex,
                    out Vector3 localPosition,
                    out Quaternion localRotation))
            {
                resultMessage = "Carry stack produced an invalid pose.";
                return false;
            }

            NetworkItemReceiver previousReceiver = null;
            if (item.IsPlaced && !item.TryResolveReceiver(out previousReceiver))
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

            if (!carrier.AddHeldItemServer(item, out int addedIndex))
            {
                resultMessage = "Carrier rejected the new stack entry.";
                return false;
            }

            PlayMotion(
                carrier,
                item,
                motion,
                visualStartPosition,
                visualStartRotation,
                revision);

            resultMessage =
                $"Picked up {item.DisplayName} ({addedIndex + 1}/" +
                $"{carrier.CarryLimit}).";
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
                carrier.ClearHeldItemServer(null);
                resultMessage = "No spawned top item is being carried.";
                return false;
            }

            if (!item.IsHeld ||
                item.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and top item state do not agree.";
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
            carrier.RemoveHeldItemServer(item);

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

        public static bool TryDropAll(
            NetworkItemCarrier carrier,
            out string resultMessage,
            bool disconnectCleanup = false)
        {
            resultMessage = string.Empty;

            if (carrier == null)
            {
                resultMessage = "Carrier is missing.";
                return false;
            }

            int droppedCount = 0;
            int safety = carrier.HeldItemCount + 1;

            while (carrier.HeldItemCount > 0 && safety-- > 0)
            {
                int countBeforeDrop = carrier.HeldItemCount;

                if (!TryDrop(
                        carrier,
                        out string dropMessage,
                        disconnectCleanup))
                {
                    // TryDrop removes an unresolved top reference so cleanup
                    // can continue with the remaining valid items.
                    if (carrier.HeldItemCount < countBeforeDrop)
                    {
                        continue;
                    }

                    resultMessage = dropMessage;
                    return false;
                }

                droppedCount++;
            }

            resultMessage = $"Dropped {droppedCount} carried item(s).";
            return droppedCount > 0;
        }

        public static bool TryReceive(
            NetworkItemCarrier carrier,
            NetworkItemReceiver receiver,
            NetworkInteractionContext context,
            out string resultMessage)
        {
            if (receiver != null &&
                receiver.TryGetComponent(
                    out NetworkGameCaseShelfBatchPlacement batchPlacement))
            {
                return batchPlacement.TryBeginServer(
                    carrier,
                    receiver,
                    context,
                    out resultMessage);
            }

            return TryReceiveSingle(
                carrier,
                receiver,
                context,
                out resultMessage);
        }

        /// <summary>
        /// Performs exactly one server-authoritative receiver transaction.
        /// Batch shelf placement reuses this method once per animated case.
        /// </summary>
        internal static bool TryReceiveSingle(
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

            if (!receiver.TryFindPlacementCandidateServer(
                    carrier,
                    context,
                    out NetworkWorldItem item,
                    out NetworkItemPlacementPlan plan,
                    out resultMessage) ||
                !ValidateServerPair(carrier, item, out resultMessage))
            {
                return false;
            }

            if (!item.IsHeld ||
                item.Location.HolderClientId != carrier.OwnerClientId)
            {
                resultMessage = "Carrier and selected item state do not agree.";
                return false;
            }

            if (plan.Disposition ==
                NetworkItemDestinationDisposition.Consume)
            {
                string itemName = item.DisplayName;
                carrier.RemoveHeldItemServer(item);
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
                resultMessage =
                    "NGO could not parent the item to this receiver.";
                return false;
            }

            item.ApplyPreparedParentPoseServer();
            receiver.CommitPlacementServer(item, plan);
            item.SetLocationServer(NetworkItemLocationState.Placed(
                receiver.NetworkObject,
                plan.SlotIndex,
                revision));
            carrier.RemoveHeldItemServer(item);

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