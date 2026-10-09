using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Game Cases/Shelf/Game Case Shelf Row")]
    public sealed class NetworkGameCaseShelfRow : NetworkBehaviour
    {
        private readonly NetworkVariable<uint> _lockedAppId =
            new NetworkVariable<uint>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "Use a separate root NetworkObject for this state. Reference the " +
            "existing slots without moving them under this object.")]
        [TitleGroup("References")]
        [SerializeField]
        private NetworkGameCaseShelfDestination[] slots =
            Array.Empty<NetworkGameCaseShelfDestination>();

        [ShowInInspector, ReadOnly, BoxGroup("Live Row")]
        public uint LockedAppId => _lockedAppId.Value;

        [ShowInInspector, ReadOnly, BoxGroup("Validation")]
        private string _configurationError = string.Empty;

        public event Action<uint, uint> LockedAppIdChanged;

        private readonly List<NetworkGameCaseShelfSlotState> _slotStates =
            new List<NetworkGameCaseShelfSlotState>();

        private bool _bindingsInitialized;
        private bool _subscribed;
        private uint _slotAppId;
        private bool _conflictingSlotGames;

        private void Awake()
        {
            BindSlots();
        }

        private void OnEnable()
        {
            BindSlots();
            Subscribe();
            RefreshFromSlots();
            LockedAppIdChanged?.Invoke(LockedAppId, LockedAppId);
        }

        private void OnDisable()
        {
            // Existing slots can still release cases while this component is
            // disabled. Keep spawned authority and replicated labels current.
            if (!IsSpawned)
            {
                Unsubscribe();
            }
        }

        public override void OnDestroy()
        {
            Unsubscribe();

            if (slots != null)
            {
                foreach (NetworkGameCaseShelfDestination slot in slots)
                {
                    if (slot != null)
                    {
                        slot.UnassignRow(this);
                    }
                }
            }

            base.OnDestroy();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ValidateConfiguration();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            BindSlots();

            Subscribe();
            RefreshFromSlots();
            LockedAppIdChanged?.Invoke(LockedAppId, LockedAppId);
        }

        public override void OnNetworkDespawn()
        {
            Unsubscribe();
            base.OnNetworkDespawn();
        }

        public bool CanAcceptAppId(uint appId)
        {
            return appId != 0 && IsSpawned && isActiveAndEnabled &&
                   _bindingsInitialized &&
                   string.IsNullOrEmpty(_configurationError) &&
                   !_conflictingSlotGames &&
                   (LockedAppId == 0 || LockedAppId == appId) &&
                   (_slotAppId == 0 || _slotAppId == appId);
        }

        // Resolves even initially inactive row objects once, during slot Awake.
        internal static void BindConfiguredRowsFor(
            NetworkGameCaseShelfDestination slot)
        {
            NetworkGameCaseShelfRow[] rows = FindObjectsByType<
                NetworkGameCaseShelfRow>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (NetworkGameCaseShelfRow row in rows)
            {
                if (row.ContainsSlot(slot))
                {
                    row.BindSlots();
                }
            }
        }

        internal void ReportMembershipConflict(
            NetworkGameCaseShelfDestination slot,
            NetworkGameCaseShelfRow otherRow)
        {
            _configurationError =
                $"Slot '{slot.name}' is assigned to both '{name}' and " +
                $"'{otherRow.name}'. A slot can belong to only one row.";
            Debug.LogError("[GameCaseShelfRow] " + _configurationError, this);
        }

        [Button("VALIDATE ROW SETUP")]
        private void ValidateConfiguration()
        {
            _configurationError = GetConfigurationError();

            if (!string.IsNullOrEmpty(_configurationError))
            {
                Debug.LogError("[GameCaseShelfRow] " + _configurationError, this);
            }
        }

        private string GetConfigurationError()
        {
            if (transform.parent != null &&
                transform.parent.GetComponentInParent<NetworkObject>() != null)
            {
                return "Row state must not be nested under another NetworkObject.";
            }

            NetworkObject[] children =
                GetComponentsInChildren<NetworkObject>(true);

            if (children.Length != 1 || children[0].gameObject != gameObject)
            {
                return "Use a separate row state NetworkObject without slot " +
                       "NetworkObjects beneath it.";
            }

            if (slots == null || slots.Length == 0)
            {
                return "Assign at least one shelf slot to this row.";
            }

            var uniqueSlots = new HashSet<NetworkGameCaseShelfDestination>();

            for (int i = 0; i < slots.Length; i++)
            {
                NetworkGameCaseShelfDestination slot = slots[i];

                if (slot == null)
                {
                    return $"Slot reference at index {i} is missing.";
                }

                if (!uniqueSlots.Add(slot))
                {
                    return $"Slot '{slot.name}' occurs more than once in this row.";
                }

                if (slot.SlotState == null &&
                    slot.GetComponent<NetworkGameCaseShelfSlotState>() == null)
                {
                    return $"Slot '{slot.name}' has no shelf slot state component.";
                }
            }

            NetworkGameCaseShelfRow[] rows = FindObjectsByType<
                NetworkGameCaseShelfRow>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (NetworkGameCaseShelfRow otherRow in rows)
            {
                if (otherRow == this)
                {
                    continue;
                }

                foreach (NetworkGameCaseShelfDestination slot in slots)
                {
                    if (otherRow.ContainsSlot(slot))
                    {
                        return $"Slot '{slot.name}' is also assigned to row " +
                               $"'{otherRow.name}'. A slot can belong to only one row.";
                    }
                }
            }

            return string.Empty;
        }

        private bool ContainsSlot(NetworkGameCaseShelfDestination slot)
        {
            return slots != null && Array.IndexOf(slots, slot) >= 0;
        }

        private void BindSlots()
        {
            if (_bindingsInitialized)
            {
                return;
            }

            _bindingsInitialized = true;
            ValidateConfiguration();

            if (slots == null)
            {
                return;
            }

            foreach (NetworkGameCaseShelfDestination slot in slots)
            {
                if (slot == null || !slot.TryAssignRow(this))
                {
                    continue;
                }

                NetworkGameCaseShelfSlotState state =
                    slot.SlotState != null
                        ? slot.SlotState
                        : slot.GetComponent<NetworkGameCaseShelfSlotState>();

                if (state != null && !_slotStates.Contains(state))
                {
                    _slotStates.Add(state);
                }
            }
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            _lockedAppId.OnValueChanged += HandleLockChanged;

            foreach (NetworkGameCaseShelfSlotState state in _slotStates)
            {
                state.SnapshotChanged += HandleSlotSnapshotChanged;
            }
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            _subscribed = false;
            _lockedAppId.OnValueChanged -= HandleLockChanged;

            foreach (NetworkGameCaseShelfSlotState state in _slotStates)
            {
                if (state != null)
                {
                    state.SnapshotChanged -= HandleSlotSnapshotChanged;
                }
            }
        }

        private void HandleLockChanged(uint previous, uint current)
        {
            LockedAppIdChanged?.Invoke(previous, current);
        }

        private void HandleSlotSnapshotChanged(
            GameCaseShelfSlotSnapshot previous,
            GameCaseShelfSlotSnapshot current)
        {
            RefreshFromSlots();
        }

        private void RefreshFromSlots()
        {
            _slotAppId = 0;
            bool previouslyConflicting = _conflictingSlotGames;
            _conflictingSlotGames = false;

            foreach (NetworkGameCaseShelfSlotState state in _slotStates)
            {
                if (state == null)
                {
                    continue;
                }

                GameCaseShelfSlotSnapshot snapshot = state.Snapshot;

                if (snapshot.OccupiedCount == 0 && !snapshot.IsComplete)
                {
                    continue;
                }

                if (snapshot.LockedAppId == 0 ||
                    (_slotAppId != 0 && _slotAppId != snapshot.LockedAppId))
                {
                    _conflictingSlotGames = true;
                }
                else
                {
                    _slotAppId = snapshot.LockedAppId;
                }
            }

            if (IsServer && _conflictingSlotGames && !previouslyConflicting)
            {
                Debug.LogError(
                    "[GameCaseShelfRow] Occupied slots have conflicting or " +
                    "missing AppIds. Placement is blocked until they agree.",
                    this);
            }

            if (IsServer && IsSpawned && !_conflictingSlotGames &&
                _lockedAppId.Value != _slotAppId)
            {
                _lockedAppId.Value = _slotAppId;
            }
        }
    }
}
