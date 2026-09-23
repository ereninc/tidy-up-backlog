using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Server-only transaction sequencer for one logical game-case shelf slot.
    /// One interaction places every matching case that was already in the
    /// carrier stack when the batch began, up to the shelf's remaining space.
    /// Each case still uses the ordinary transfer and motion pipeline.
    /// </summary>
    [RequireComponent(typeof(NetworkItemReceiver))]
    [RequireComponent(typeof(NetworkGameCaseShelfDestination))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Batch Placement")]
    public sealed class NetworkGameCaseShelfBatchPlacement : MonoBehaviour
    {
        private static readonly HashSet<ulong> BusyCarrierIds =
            new HashSet<ulong>();

        [TitleGroup("Batch UX")]
        [Tooltip(
            "Realtime delay between cases. Values around 0.08-0.12 seconds " +
            "produce a quick staggered 'pop-pop-pop' motion.")]
        [MinValue(0.02f)]
        [SerializeField]
        private float intervalSeconds = 0.09f;

        [TitleGroup("Batch UX")]
        [Tooltip(
            "Safety cap for one interaction. Shelf capacity still remains " +
            "the real placement limit.")]
        [MinValue(1)]
        [SerializeField]
        private int maximumCasesPerInteraction = 32;

        [ShowInInspector, ReadOnly, BoxGroup("Live Batch")]
        private bool IsRunning => _routine != null;

        private Coroutine _routine;
        private ulong _activeCarrierId;
        private bool _hasActiveCarrier;

        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            BusyCarrierIds.Clear();
        }

        private void OnValidate()
        {
            intervalSeconds = Mathf.Max(0.02f, intervalSeconds);
            maximumCasesPerInteraction =
                Mathf.Max(1, maximumCasesPerInteraction);
        }

        private void OnDisable()
        {
            CancelBatch();
        }

        internal bool TryBeginServer(
            NetworkItemCarrier carrier,
            NetworkItemReceiver receiver,
            NetworkInteractionContext context,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (carrier == null || receiver == null ||
                !carrier.IsSpawned || !carrier.IsServer ||
                !receiver.IsSpawned || !receiver.IsServer)
            {
                resultMessage =
                    "Shelf batch placement is not ready on the server.";
                return false;
            }

            NetworkGameCaseShelfDestination shelfDestination =
                receiver.Destination as NetworkGameCaseShelfDestination;

            if (shelfDestination == null ||
                shelfDestination.SlotState == null)
            {
                resultMessage =
                    "Batch placement requires a game-case shelf destination.";
                return false;
            }

            if (_routine != null)
            {
                resultMessage =
                    "This shelf slot is already receiving game cases.";
                return false;
            }

            ulong carrierId = carrier.NetworkObjectId;

            if (!BusyCarrierIds.Add(carrierId))
            {
                resultMessage =
                    "This carry stack is already being placed.";
                return false;
            }

            _activeCarrierId = carrierId;
            _hasActiveCarrier = true;

            if (!receiver.TryFindPlacementCandidateServer(
                    carrier,
                    context,
                    out NetworkWorldItem firstItem,
                    out _,
                    out resultMessage) ||
                firstItem == null ||
                !firstItem.TryGetComponent(
                    out NetworkGameCase firstGameCase) ||
                !firstGameCase.IsBound)
            {
                ReleaseCarrierLock();

                if (string.IsNullOrWhiteSpace(resultMessage))
                {
                    resultMessage =
                        "No carried game case fits this shelf slot.";
                }

                return false;
            }

            uint targetAppId = firstGameCase.AppId;
            int matchingCount = CountMatchingCases(carrier, targetAppId);
            int remainingCapacity = Mathf.Max(
                0,
                receiver.Capacity - receiver.OccupiedCount);

            int plannedCount = Mathf.Min(
                Mathf.Min(
                    matchingCount,
                    remainingCapacity),
                maximumCasesPerInteraction);

            if (plannedCount <= 0)
            {
                ReleaseCarrierLock();
                resultMessage = "This game shelf slot has no free space.";
                return false;
            }

            if (!NetworkItemTransferService.TryReceiveSingle(
                    carrier,
                    receiver,
                    context,
                    out string firstResult))
            {
                ReleaseCarrierLock();
                resultMessage = firstResult;
                return false;
            }

            if (plannedCount == 1)
            {
                ReleaseCarrierLock();
                resultMessage = firstResult;
                return true;
            }

            _routine = StartCoroutine(
                PlaceRemainingServer(
                    carrier,
                    receiver,
                    shelfDestination,
                    context,
                    targetAppId,
                    plannedCount - 1));

            resultMessage =
                $"Placing {plannedCount} matching game cases.";
            return true;
        }

        private IEnumerator PlaceRemainingServer(
            NetworkItemCarrier carrier,
            NetworkItemReceiver receiver,
            NetworkGameCaseShelfDestination shelfDestination,
            NetworkInteractionContext context,
            uint targetAppId,
            int remainingCount)
        {
            var delay =
                new WaitForSecondsRealtime(
                    Mathf.Max(0.02f, intervalSeconds));

            for (int i = 0; i < remainingCount; i++)
            {
                yield return delay;

                if (!CanContinueServer(
                        carrier,
                        receiver,
                        shelfDestination,
                        targetAppId))
                {
                    break;
                }

                if (!NetworkItemTransferService.TryReceiveSingle(
                        carrier,
                        receiver,
                        context,
                        out _))
                {
                    break;
                }
            }

            _routine = null;
            ReleaseCarrierLock();
        }

        private static bool CanContinueServer(
            NetworkItemCarrier carrier,
            NetworkItemReceiver receiver,
            NetworkGameCaseShelfDestination shelfDestination,
            uint targetAppId)
        {
            if (carrier == null || receiver == null ||
                shelfDestination == null ||
                !carrier.IsSpawned || !carrier.IsServer ||
                !receiver.IsSpawned || !receiver.IsServer ||
                !carrier.HasHeldItem)
            {
                return false;
            }

            NetworkGameCaseShelfSlotState slotState =
                shelfDestination.SlotState;

            if (slotState == null || !slotState.IsSpawned ||
                slotState.Snapshot.IsComplete)
            {
                return false;
            }

            // The first placement locks the slot. If another transaction
            // emptied or changed it between staggered steps, stop rather than
            // accidentally choosing a different game from the mixed stack.
            return slotState.Snapshot.LockedAppId == targetAppId;
        }

        private static int CountMatchingCases(
            NetworkItemCarrier carrier,
            uint appId)
        {
            int count = 0;

            for (int i = 0; i < carrier.HeldItemCount; i++)
            {
                if (carrier.TryGetHeldItemAt(
                        i,
                        out NetworkWorldItem item) &&
                    item.TryGetComponent(
                        out NetworkGameCase gameCase) &&
                    gameCase.IsBound &&
                    gameCase.AppId == appId)
                {
                    count++;
                }
            }

            return count;
        }

        private void CancelBatch()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            ReleaseCarrierLock();
        }

        private void ReleaseCarrierLock()
        {
            if (!_hasActiveCarrier)
            {
                return;
            }

            BusyCarrierIds.Remove(_activeCarrierId);
            _activeCarrierId = 0;
            _hasActiveCarrier = false;
        }
    }
}
