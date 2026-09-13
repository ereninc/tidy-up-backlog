using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Converts completed shelf cases into renderer-only presentation without
    /// changing the generic item classes. NetworkObject and NetworkWorldItem
    /// remain alive for replicated identity and future save integration.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/Shelf/Game Case Shelf Completion Presenter")]
    public sealed class GameCaseShelfCompletionPresenter : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private NetworkGameCaseShelfSlotState slotState;

        [Header("Freeze On Complete")]
        [SerializeField]
        private bool disableItemInteractable = true;

        [SerializeField]
        private bool disableCarryable = true;

        [SerializeField]
        private bool disableHeldPoseFollower = true;

        [SerializeField]
        private bool disableItemPresentation = true;

        [SerializeField]
        private bool disableWorldColliders = true;

        [SerializeField]
        private bool disableRigidbodyCollisions = true;

        [Tooltip(
            "Retries while NGO parent messages finish on remote/late clients.")]
        [SerializeField, Min(1)]
        private int hierarchySyncFrames = 120;

        private readonly HashSet<NetworkWorldItem> _frozenItems =
            new HashSet<NetworkWorldItem>();

        private Coroutine _applyRoutine;

        public int FrozenItemCount => _frozenItems.Count;

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();

            if (slotState != null)
            {
                slotState.SnapshotChanged += HandleSnapshotChanged;

                if (slotState.Snapshot.IsComplete)
                {
                    ScheduleApply();
                }
            }
        }

        private void OnDisable()
        {
            if (slotState != null)
            {
                slotState.SnapshotChanged -= HandleSnapshotChanged;
            }

            if (_applyRoutine != null)
            {
                StopCoroutine(_applyRoutine);
                _applyRoutine = null;
            }
        }

        private void OnTransformChildrenChanged()
        {
            // A late join can receive the completed snapshot before all NGO
            // parent messages. A newly parented case gets another freeze pass
            // even if the timed retry window already ended.
            if (slotState != null && slotState.Snapshot.IsComplete)
            {
                ScheduleApply();
            }
        }

        private void HandleSnapshotChanged(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            // Also runs for an already-complete initial snapshot on a late
            // client (OnNetworkSpawn publishes current/current locally).
            if (current.IsComplete)
            {
                ScheduleApply();
            }
        }

        private void ScheduleApply()
        {
            if (_applyRoutine == null && isActiveAndEnabled)
            {
                _applyRoutine = StartCoroutine(ApplyWhenHierarchyIsReady());
            }
        }

        private IEnumerator ApplyWhenHierarchyIsReady()
        {
            int expectedCount = slotState != null
                ? slotState.Snapshot.OccupiedCount
                : 0;
            int attempts = Mathf.Max(1, hierarchySyncFrames);

            for (int frame = 0; frame < attempts; frame++)
            {
                FreezeCurrentChildren();

                if (_frozenItems.Count >= expectedCount)
                {
                    _applyRoutine = null;
                    yield break;
                }

                yield return null;
            }

            Debug.LogWarning(
                $"[GameCaseShelf] Completion expected {expectedCount} cases " +
                $"but froze {_frozenItems.Count} after {attempts} frames.",
                this);
            _applyRoutine = null;
        }

        private void FreezeCurrentChildren()
        {
            NetworkWorldItem[] items =
                GetComponentsInChildren<NetworkWorldItem>(true);

            for (int i = 0; i < items.Length; i++)
            {
                NetworkWorldItem item = items[i];

                if (item == null || _frozenItems.Contains(item))
                {
                    continue;
                }

                FreezeItem(item);
                _frozenItems.Add(item);
            }
        }

        private void FreezeItem(NetworkWorldItem item)
        {
            if (disableItemInteractable &&
                item.TryGetComponent(out NetworkItemInteractable interactable))
            {
                interactable.enabled = false;
            }

            if (disableCarryable &&
                item.TryGetComponent(out NetworkCarryable carryable))
            {
                carryable.enabled = false;
            }

            if (disableHeldPoseFollower &&
                item.TryGetComponent(
                    out NetworkHeldItemPoseFollower poseFollower))
            {
                poseFollower.enabled = false;
            }

            if (disableItemPresentation &&
                item.TryGetComponent(
                    out NetworkItemPresentation presentation))
            {
                presentation.enabled = false;
            }

            if (disableWorldColliders)
            {
                Collider[] colliders =
                    item.GetComponentsInChildren<Collider>(true);

                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        colliders[i].enabled = false;
                    }
                }
            }

            if (item.TryGetComponent(out Rigidbody targetRigidbody))
            {
                targetRigidbody.isKinematic = true;

                if (disableRigidbodyCollisions)
                {
                    targetRigidbody.detectCollisions = false;
                }

                targetRigidbody.Sleep();
            }
        }

        private void ResolveReferences()
        {
            if (slotState == null)
            {
                slotState = GetComponent<NetworkGameCaseShelfSlotState>();
            }
        }
    }
}
