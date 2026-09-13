using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Controls the local gameplay body, camera and input. With deferred player
    /// spawning this component starts in GameplayScene; it also safely supports
    /// legacy PlayerObjects that already exist while the lobby UI is open.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Session/Network Player Presence Controller")]
    public sealed class NetworkPlayerPresenceController : NetworkBehaviour
    {
        private readonly NetworkVariable<bool> _gameplayActive =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [Header("References")]
        [SerializeField] private NetworkFpsPlayerController fpsController;

        [Tooltip("Visual roots, name tag roots or gameplay-only children. Never assign the NetworkObject root itself.")]
        [SerializeField] private GameObject[] gameplayOnlyObjects;

        [Header("Debug")]
        [SerializeField] private bool verboseLogging;

        public bool IsGameplayActive => IsSpawned && _gameplayActive.Value;

        public event Action<bool> GameplayPresenceChanged;

        private void Reset()
        {
            AutoAssignReferences();
        }

        private void OnValidate()
        {
            AutoAssignReferences();
        }

        public override void OnNetworkSpawn()
        {
            AutoAssignReferences();
            _gameplayActive.OnValueChanged += HandleGameplayActiveChanged;
            ApplyGameplayPresence(_gameplayActive.Value);
        }

        public override void OnNetworkDespawn()
        {
            _gameplayActive.OnValueChanged -= HandleGameplayActiveChanged;
            ApplyGameplayPresence(false);
            GameplayInputGate.Clear(this);
            base.OnNetworkDespawn();
        }

        private void OnDestroy()
        {
            GameplayInputGate.Clear(this);
        }

        /// <summary>Server-only activation called by MultiplayerSessionCoordinator.</summary>
        public void ActivateGameplayServer(Vector3 position, Quaternion rotation)
        {
            if (!IsServer || !IsSpawned)
            {
                Debug.LogWarning(
                    "[PlayerPresence] Only the spawned server PlayerObject can be activated.",
                    this);
                return;
            }

            ApplyPose(position, rotation);

            ClientRpcParams targetOwner = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { OwnerClientId }
                }
            };

            ApplyOwnerGameplayPoseClientRpc(position, rotation, targetOwner);
            _gameplayActive.Value = true;
            ApplyGameplayPresence(true);
        }

        public void DeactivateGameplayServer()
        {
            if (!IsServer || !IsSpawned)
            {
                return;
            }

            _gameplayActive.Value = false;
            ApplyGameplayPresence(false);
        }

        public void AutoAssignReferences()
        {
            if (fpsController == null)
            {
                fpsController = GetComponent<NetworkFpsPlayerController>();
            }
        }

        public bool ValidateSetup(bool logResult = true)
        {
            AutoAssignReferences();
            List<string> problems = new List<string>();

            if (fpsController == null)
            {
                problems.Add("NetworkFpsPlayerController is missing.");
            }

            if (gameplayOnlyObjects != null)
            {
                for (int i = 0; i < gameplayOnlyObjects.Length; i++)
                {
                    GameObject target = gameplayOnlyObjects[i];

                    if (target == gameObject)
                    {
                        problems.Add(
                            $"gameplayOnlyObjects[{i}] is the NetworkObject root; use a child instead.");
                    }
                }
            }

            bool valid = problems.Count == 0;

            if (logResult)
            {
                if (valid)
                {
                    Debug.Log("[PlayerPresence] PASS: setup is valid.", this);
                }
                else
                {
                    Debug.LogError(
                        "[PlayerPresence] Setup problems:\n- " +
                        string.Join("\n- ", problems),
                        this);
                }
            }

            return valid;
        }

        [ClientRpc]
        private void ApplyOwnerGameplayPoseClientRpc(
            Vector3 position,
            Quaternion rotation,
            ClientRpcParams clientRpcParams = default)
        {
            if (IsOwner)
            {
                ApplyPose(position, rotation);
            }
        }

        private void HandleGameplayActiveChanged(bool previous, bool current)
        {
            ApplyGameplayPresence(current);
        }

        private void ApplyGameplayPresence(bool active)
        {
            if (fpsController != null)
            {
                fpsController.SetGameplayEnabled(active);
            }

            if (gameplayOnlyObjects != null)
            {
                for (int i = 0; i < gameplayOnlyObjects.Length; i++)
                {
                    GameObject target = gameplayOnlyObjects[i];

                    if (target != null && target != gameObject)
                    {
                        target.SetActive(active);
                    }
                }
            }

            if (IsOwner)
            {
                GameplayInputGate.SetBlocked(this, !active);

                if (!active)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            if (verboseLogging && IsSpawned)
            {
                Debug.Log(
                    $"[PlayerPresence] ClientID={OwnerClientId}, Active={active}, Owner={IsOwner}",
                    this);
            }

            GameplayPresenceChanged?.Invoke(active);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            CharacterController controller = GetComponent<CharacterController>();
            bool restoreController = controller != null && controller.enabled;

            if (restoreController)
            {
                controller.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);

            if (restoreController)
            {
                controller.enabled = true;
            }
        }
    }
}
