using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Synchronous server-only item transactions. It validates before mutation,
    /// changes an NGO parent once, then publishes item/carrier/receiver state.
    /// Unity's main thread makes competing pickup requests deterministic: first
    /// valid transaction wins.
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

            if (carrier.HasHeldItem)
            {
                resultMessage = "Your hands are full.";
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

            if (item.IsPlaced)
            {
                if (!item.TryResolveReceiver(out previousReceiver))
                {
                    resultMessage =
                        "Item placement receiver could not be resolved.";
                    return false;
                }
            }

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
                item.NextRevisionServer()));
            carrier.SetHeldItemServer(item);

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

            if (!resolvedItem ||
                item == null ||
                !item.IsSpawned)
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
            item.SetLocationServer(NetworkItemLocationState.World(
                item.NextRevisionServer()));

            if (carrier.IsSpawned)
            {
                carrier.ClearHeldItemServer(item);
            }

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
                item.NextRevisionServer()));
            carrier.ClearHeldItemServer(item);

            resultMessage = $"Placed {item.DisplayName}.";
            return true;
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
                rejectionMessage = "Carrier and item are not in the same session.";
                return false;
            }

            return true;
        }
    }
}
