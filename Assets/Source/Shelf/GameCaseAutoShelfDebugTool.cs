using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(GameCaseShelfCompletionGroup))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Debug/Auto Shelf Debug Tool")]
    public sealed class GameCaseAutoShelfDebugTool : NetworkBehaviour
    {
        [SerializeField, Min(1)] private int batchSize = 50;
        [SerializeField, Min(0f)] private float placementInterval = .1f;
        [SerializeField] private GameCaseInstanceRegistry caseRegistry;
        [SerializeField] private GameCaseShelfCompletionGroup targetShelves;

        public bool IsBusy { get; private set; }
        public int NextCaseIndex => _nextCaseIndex;
        public bool CanStartBatch => Application.isPlaying && isActiveAndEnabled && IsSpawned &&
            IsServer && NetworkManager != null && NetworkManager.IsListening &&
            !NetworkManager.ShutdownInProgress && !IsBusy;

        private struct Candidate
        {
            public NetworkWorldItem Item;
            public int SourceIndex;
        }

        private struct Target
        {
            public NetworkGameCaseShelfDestination Shelf;
            public NetworkItemReceiver Receiver;
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<Target> _targets = new List<Target>();
        private GameCaseInstanceRegistry _boundRegistry;
        private NetworkManager _manager;
        private Coroutine _routine;
        private int _nextCaseIndex;
        private int _scanEnd;
        private int _examined;
        private int _placed;
        private int _noSlot;
        private int _invalid;

        private void Reset() => targetShelves = GetComponent<GameCaseShelfCompletionGroup>();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.shiftKey.isPressed && keyboard.gKey.wasPressedThisFrame)
                HandleShortcut();
        }

        private void HandleShortcut()
        {
            if (IsBusy) CancelCurrentBatch();
            else PlaceNextBatch();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            _manager = NetworkManager;
            if (_manager != null) _manager.OnServerStopped += HandleServerStopped;
        }

        public override void OnNetworkDespawn()
        {
            StopBatch("despawn");
            UnbindRegistry();
            if (_manager != null) _manager.OnServerStopped -= HandleServerStopped;
            _manager = null;
            base.OnNetworkDespawn();
        }

        private void OnDisable()
        {
            StopBatch("disabled");
            UnbindRegistry();
        }

        private void HandleServerStopped(bool wasHost) => StopBatch("server stopped");

        [Button("Place Next Batch")]
        public void PlaceNextBatch()
        {
            if (!CheckAuthority()) return;
            if (IsBusy)
            {
                Debug.LogWarning("[AutoShelf] A batch is already running.", this);
                return;
            }
            if (!isActiveAndEnabled) return;
            var registry = caseRegistry != null ? caseRegistry : GameCaseInstanceRegistry.Instance;
            if (registry == null || !registry.IsReady)
            {
                Debug.LogWarning("[AutoShelf] GameCaseInstanceRegistry is not ready. Wait for case binding.", this);
                return;
            }
            if (targetShelves == null) targetShelves = GetComponent<GameCaseShelfCompletionGroup>();
            if (targetShelves == null || !targetShelves.IsSpawned || !targetShelves.IsServer ||
                targetShelves.NetworkManager != NetworkManager)
            {
                Debug.LogWarning("[AutoShelf] Assign a spawned server Shelf Completion Group in this session.", this);
                return;
            }
            if (_boundRegistry != registry)
            {
                UnbindRegistry();
                _boundRegistry = registry;
                _boundRegistry.Rebuilt += HandleRegistryRebuilt;
                _nextCaseIndex = 0;
            }

            targetShelves.RebuildTargetsServer();
            _targets.Clear();
            foreach (var state in targetShelves.Slots)
            {
                if (state != null && state.TryGetComponent(out NetworkGameCaseShelfDestination shelf) &&
                    state.TryGetComponent(out NetworkItemReceiver receiver) && receiver.Destination == shelf)
                    _targets.Add(new Target { Shelf = shelf, Receiver = receiver });
            }
            // Copy references without sorting or altering the registry's list.
            IReadOnlyList<NetworkGameCase> source = registry.Cases;
            if (_nextCaseIndex >= source.Count) _nextCaseIndex = 0;
            _candidates.Clear();
            int index = _nextCaseIndex;
            for (; index < source.Count && _candidates.Count < Mathf.Max(1, batchSize); index++)
            {
                var gameCase = source[index];
                if (gameCase != null && gameCase.TryGetComponent(out NetworkWorldItem item) &&
                    item.NetworkManager == NetworkManager && NetworkItemTransferService.CanPlaceWorldGameCaseServer(item))
                    _candidates.Add(new Candidate { Item = item, SourceIndex = index });
            }
            _scanEnd = index;
            _examined = _placed = _noSlot = _invalid = 0;
            IsBusy = true;
            _routine = StartCoroutine(RunBatch());
        }

        [Button("Cancel Current Batch")]
        public void CancelCurrentBatch()
        {
            if (CheckAuthority()) StopBatch("cancelled");
        }

        private bool CheckAuthority()
        {
            if (Application.isPlaying && IsSpawned && IsServer && NetworkManager != null &&
                NetworkManager.IsListening && !NetworkManager.ShutdownInProgress) return true;
            Debug.LogWarning("[AutoShelf] Play Mode and a spawned server/host object are required; clients cannot place cases.", this);
            return false;
        }

        private bool CanContinue => isActiveAndEnabled && IsSpawned && IsServer && _manager != null &&
            _manager.IsListening && !_manager.ShutdownInProgress && targetShelves != null && targetShelves.IsSpawned &&
            _boundRegistry != null && _boundRegistry.IsReady;

        private IEnumerator RunBatch()
        {
            // StartCoroutine must return its handle before any completion/cancel path.
            yield return null;
            try
            {
                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (!CanContinue) yield break;
                    Candidate candidate = _candidates[i];
                    _nextCaseIndex = candidate.SourceIndex + 1;
                    _examined++;
                    bool placed = false;
                    if (!NetworkItemTransferService.CanPlaceWorldGameCaseServer(candidate.Item)) _invalid++;
                    else if (!TryFindReceiver(candidate.Item, out var receiver)) _noSlot++;
                    else if (NetworkItemTransferService.TryPlaceWorldGameCaseServer(candidate.Item, receiver, out _))
                    {
                        _placed++;
                        placed = true;
                    }
                    else _invalid++;

                    if (placed && placementInterval > 0f)
                    {
                        double until = Time.realtimeSinceStartupAsDouble + placementInterval;
                        while (CanContinue && Time.realtimeSinceStartupAsDouble < until) yield return null;
                    }
                    else yield return null;
                }
                _nextCaseIndex = _scanEnd;
            }
            finally { FinishBatch("finished"); }
        }

        private bool TryFindReceiver(NetworkWorldItem item, out NetworkItemReceiver receiver)
        {
            receiver = null;
            uint appId = item.GetComponent<NetworkGameCase>().AppId;
            for (int priority = 0; priority < 3; priority++)
            foreach (var target in _targets)
            {
                var shelf = target.Shelf;
                if (shelf == null || !shelf.isActiveAndEnabled || target.Receiver == null ||
                    !target.Receiver.isActiveAndEnabled || !target.Receiver.IsSpawned ||
                    shelf.SlotState == null || !shelf.SlotState.IsSpawned || shelf.SlotState.IsComplete ||
                    shelf.OccupiedCount >= shelf.Capacity) continue;
                uint slotApp = shelf.SlotState.LockedAppId;
                uint rowApp = shelf.Row != null ? shelf.Row.LockedAppId : 0;
                int rank = slotApp == appId ? 0 :
                    shelf.OccupiedCount == 0 && slotApp == 0 && rowApp == appId ? 1 :
                    shelf.OccupiedCount == 0 && slotApp == 0 && rowApp == 0 ? 2 : -1;
                if (rank != priority || !target.Receiver.TryBuildPlacementPlanServer(item, null, default, out _, out _)) continue;
                receiver = target.Receiver;
                return true;
            }
            return false;
        }

        private void StopBatch(string reason)
        {
            if (!IsBusy) return;
            var routine = _routine;
            _routine = null;
            // Clear busy/log before StopCoroutine disposes the iterator's finally.
            FinishBatch(reason);
            if (routine != null) StopCoroutine(routine);
        }

        private void FinishBatch(string reason)
        {
            if (!IsBusy) return;
            IsBusy = false;
            _routine = null;
            Debug.Log($"[AutoShelf] Batch {reason}: Examined={_examined}, Placed={_placed}, NoSlot={_noSlot}, Invalidated={_invalid}.", this);
            _candidates.Clear();
            _targets.Clear();
        }

        private void HandleRegistryRebuilt()
        {
            StopBatch("registry rebuilt");
            _nextCaseIndex = 0;
        }

        private void UnbindRegistry()
        {
            if (_boundRegistry != null) _boundRegistry.Rebuilt -= HandleRegistryRebuilt;
            _boundRegistry = null;
        }
    }
}
