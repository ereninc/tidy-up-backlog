using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Server-authoritative shared skills. One player's use starts the same
    /// cooldown for every player. Skill targets are always resolved by server.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Progression/Network Shared Skill Service")]
    public sealed class NetworkSharedSkillService : NetworkBehaviour
    {
        [SerializeField] private SharedProgressionCatalog catalog;

        private NetworkList<SharedSkillRuntimeState> _skillStates;

        private readonly NetworkVariable<GameCaseRevealState> _revealState =
            new NetworkVariable<GameCaseRevealState>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public static NetworkSharedSkillService Instance { get; private set; }
        public SharedProgressionCatalog Catalog => catalog;
        public GameCaseRevealState RevealState => _revealState.Value;

        public double ServerTimeNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : 0d;

        public event Action SkillsChanged;
        public event Action<GameCaseRevealState, GameCaseRevealState>
            RevealStateChanged;
        public event Action<bool, string> LocalUseResult;

        private void Awake()
        {
            _skillStates = new NetworkList<SharedSkillRuntimeState>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;

            _skillStates.OnListChanged += HandleSkillListChanged;
            _revealState.OnValueChanged += HandleRevealStateChanged;

            if (IsServer && _skillStates.Count == 0)
            {
                InitializeSkillsServer();
            }

            SkillsChanged?.Invoke();
            RevealStateChanged?.Invoke(
                _revealState.Value,
                _revealState.Value);
        }

        public override void OnNetworkDespawn()
        {
            _skillStates.OnListChanged -= HandleSkillListChanged;
            _revealState.OnValueChanged -= HandleRevealStateChanged;

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        public bool RequestUseSkillSlot(int slotIndex)
        {
            if (!IsSpawned || catalog == null ||
                !catalog.TryGetSkillAtSlot(
                    slotIndex,
                    out SharedSkillDefinition definition))
            {
                LocalUseResult?.Invoke(false, "Skill slot is not configured.");
                return false;
            }

            RequestUseSkillServerRpc(definition.SkillId);
            return true;
        }

        public bool TryGetRuntimeState(
            int skillId,
            out SharedSkillRuntimeState state)
        {
            for (int i = 0; i < _skillStates.Count; i++)
            {
                if (_skillStates[i].SkillId == skillId)
                {
                    state = _skillStates[i];
                    return true;
                }
            }

            state = default;
            return false;
        }

        public float GetRemainingCooldownSeconds(int skillId)
        {
            return TryGetRuntimeState(skillId, out var state)
                ? Mathf.Max(0f, (float)(state.CooldownEndsAt - ServerTimeNow))
                : 0f;
        }

        /// <summary>Used later by the shared upgrade service.</summary>
        public bool SetSkillUnlockedServer(int skillId, bool unlocked)
        {
            if (!IsServer || !IsSpawned ||
                !TryFindStateIndex(skillId, out int index))
            {
                return false;
            }

            SharedSkillRuntimeState state = _skillStates[index];
            state.IsUnlocked = unlocked;
            _skillStates[index] = state;
            return true;
        }

        /// <summary>Used later by the shared upgrade service.</summary>
        public bool SetSkillLevelServer(int skillId, int level)
        {
            if (!IsServer || !IsSpawned ||
                !TryFindStateIndex(skillId, out int index))
            {
                return false;
            }

            SharedSkillRuntimeState state = _skillStates[index];
            state.Level = (ushort)Mathf.Clamp(level, 0, ushort.MaxValue);
            _skillStates[index] = state;
            return true;
        }

        /// <summary>Development and future upgrade-system server API.</summary>
        public bool ResetAllCooldownsServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            for (int i = 0; i < _skillStates.Count; i++)
            {
                SharedSkillRuntimeState state = _skillStates[i];

                if (state.CooldownEndsAt <= 0d)
                {
                    continue;
                }

                state.CooldownEndsAt = 0d;
                _skillStates[i] = state;
            }

            return true;
        }

        /// <summary>Development and future upgrade-system server API.</summary>
        public bool UnlockAllSkillsServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return false;
            }

            for (int i = 0; i < _skillStates.Count; i++)
            {
                SharedSkillRuntimeState state = _skillStates[i];

                if (state.IsUnlocked)
                {
                    continue;
                }

                state.IsUnlocked = true;

                if (state.Level == 0)
                {
                    state.Level = 1;
                }

                _skillStates[i] = state;
            }

            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestUseSkillServerRpc(
            int skillId,
            ServerRpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            if (!TryUseSkillServer(
                    senderClientId,
                    skillId,
                    out string resultMessage))
            {
                SendUseResult(senderClientId, false, resultMessage);
                return;
            }

            SendUseResult(senderClientId, true, resultMessage);
        }

        private bool TryUseSkillServer(
            ulong senderClientId,
            int skillId,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!IsServer || !IsSpawned || catalog == null)
            {
                resultMessage = "Shared skill service is not ready.";
                return false;
            }

            if (!catalog.TryGetSkill(skillId, out var definition) ||
                !TryFindStateIndex(skillId, out int stateIndex))
            {
                resultMessage = "Unknown shared skill.";
                return false;
            }

            SharedSkillRuntimeState state = _skillStates[stateIndex];

            if (!state.IsUnlocked)
            {
                resultMessage = "This skill is locked.";
                return false;
            }

            double now = ServerTimeNow;

            if (state.CooldownEndsAt > now)
            {
                resultMessage =
                    $"Skill cooldown: {state.CooldownEndsAt - now:0.0}s";
                return false;
            }

            if (!TryResolveRequestingCarrier(
                    senderClientId,
                    out NetworkItemCarrier carrier))
            {
                resultMessage = "The requesting player has no carrier.";
                return false;
            }

            switch (definition.EffectType)
            {
                case SharedSkillEffectType.RevealMatchingGameCases:
                    if (!TryActivateRevealServer(
                            carrier,
                            definition,
                            now,
                            out resultMessage))
                    {
                        return false;
                    }
                    break;

                default:
                    resultMessage = "The skill effect is not implemented.";
                    return false;
            }

            state.CooldownEndsAt = now + definition.CooldownSeconds;
            _skillStates[stateIndex] = state;
            return true;
        }

        private bool TryActivateRevealServer(
            NetworkItemCarrier carrier,
            SharedSkillDefinition definition,
            double now,
            out string resultMessage)
        {
            resultMessage = string.Empty;

            if (!carrier.TryGetHeldItem(out NetworkWorldItem topItem) ||
                topItem == null ||
                !topItem.TryGetComponent(out NetworkGameCase gameCase) ||
                gameCase.AppId == 0)
            {
                resultMessage =
                    "Hold at least one bound game case to use Case Finder.";
                return false;
            }

            uint sequence = _revealState.Value.Sequence + 1;
            if (sequence == 0)
            {
                sequence = 1;
            }

            _revealState.Value = new GameCaseRevealState
            {
                SkillId = definition.SkillId,
                TargetAppId = gameCase.AppId,
                ExpiresAt = now + definition.EffectDurationSeconds,
                Sequence = sequence
            };

            resultMessage =
                $"Revealed every instance of " +
                GameCaseSessionPlan.GetGameName(gameCase.AppId) + ".";
            return true;
        }

        private bool TryResolveRequestingCarrier(
            ulong senderClientId,
            out NetworkItemCarrier carrier)
        {
            carrier = null;

            if (NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(
                    senderClientId,
                    out NetworkClient client) ||
                client.PlayerObject == null)
            {
                return false;
            }

            carrier = client.PlayerObject.GetComponent<NetworkItemCarrier>();

            if (carrier == null)
            {
                carrier = client.PlayerObject
                    .GetComponentInChildren<NetworkItemCarrier>(true);
            }

            return carrier != null && carrier.IsSpawned;
        }

        private void InitializeSkillsServer()
        {
            if (catalog == null)
            {
                Debug.LogError(
                    "Shared Progression Catalog is missing.",
                    this);
                return;
            }

            for (int slotIndex = 0;
                 slotIndex < catalog.SkillCount;
                 slotIndex++)
            {
                if (!catalog.TryGetSkillAtSlot(
                        slotIndex,
                        out SharedSkillDefinition definition))
                {
                    continue;
                }

                _skillStates.Add(new SharedSkillRuntimeState
                {
                    SkillId = definition.SkillId,
                    Level = (ushort)(definition.UnlockedByDefault ? 1 : 0),
                    IsUnlocked = definition.UnlockedByDefault,
                    CooldownEndsAt = 0d
                });
            }
        }

        private bool TryFindStateIndex(int skillId, out int stateIndex)
        {
            for (int i = 0; i < _skillStates.Count; i++)
            {
                if (_skillStates[i].SkillId == skillId)
                {
                    stateIndex = i;
                    return true;
                }
            }

            stateIndex = -1;
            return false;
        }

        private void SendUseResult(
            ulong targetClientId,
            bool succeeded,
            string message)
        {
            var sendParams = new ClientRpcSendParams
            {
                TargetClientIds = new[] { targetClientId }
            };

            SkillUseResultClientRpc(
                succeeded,
                new FixedString512Bytes(message ?? string.Empty),
                new ClientRpcParams { Send = sendParams });
        }

        [ClientRpc]
        private void SkillUseResultClientRpc(
            bool succeeded,
            FixedString512Bytes message,
            ClientRpcParams rpcParams = default)
        {
            string result = message.ToString();
            LocalUseResult?.Invoke(succeeded, result);

            if (succeeded)
            {
                Debug.Log($"[SharedSkill] {result}", this);
            }
            else
            {
                Debug.LogWarning($"[SharedSkill] {result}", this);
            }
        }

        private void HandleSkillListChanged(
            NetworkListEvent<SharedSkillRuntimeState> changeEvent)
        {
            SkillsChanged?.Invoke();
        }

        private void HandleRevealStateChanged(
            GameCaseRevealState previous,
            GameCaseRevealState current)
        {
            RevealStateChanged?.Invoke(previous, current);
        }
    }
}
