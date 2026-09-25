using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Development-only server gateway for shared test commands. Add it to the
    /// existing SharedProgressionRoot NetworkObject.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Development/Network Shared Development Service")]
    public sealed class NetworkSharedDevelopmentService : NetworkBehaviour
    {
        [Header("Build Access")]
        [Tooltip(
            "Leave disabled for release. Editor and Development Builds are " +
            "always allowed.")]
        [SerializeField] private bool allowInNonDevelopmentBuild;

        [Tooltip(
            "When disabled, only the host may execute commands. Keep enabled " +
            "while testing client authority paths.")]
        [SerializeField] private bool allowRemoteClients = true;

        [Header("Safety Limits")]
        [SerializeField] private long maxMoneyDeltaPerCommand = 1000000;
        [SerializeField, Min(1)] private int maxCarryDeltaPerCommand = 10;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            SharedDevelopmentActions.MoneyDeltaRequested +=
                HandleMoneyDeltaRequested;
            SharedDevelopmentActions.CarryLimitDeltaRequested +=
                HandleCarryLimitDeltaRequested;
            SharedDevelopmentActions.CarryLimitResetRequested +=
                HandleCarryLimitResetRequested;
            SharedDevelopmentActions.SkillCooldownsResetRequested +=
                HandleSkillCooldownsResetRequested;
            SharedDevelopmentActions.SkillsUnlockRequested +=
                HandleSkillsUnlockRequested;
        }

        public override void OnNetworkDespawn()
        {
            SharedDevelopmentActions.MoneyDeltaRequested -=
                HandleMoneyDeltaRequested;
            SharedDevelopmentActions.CarryLimitDeltaRequested -=
                HandleCarryLimitDeltaRequested;
            SharedDevelopmentActions.CarryLimitResetRequested -=
                HandleCarryLimitResetRequested;
            SharedDevelopmentActions.SkillCooldownsResetRequested -=
                HandleSkillCooldownsResetRequested;
            SharedDevelopmentActions.SkillsUnlockRequested -=
                HandleSkillsUnlockRequested;

            base.OnNetworkDespawn();
        }

        private void HandleMoneyDeltaRequested(long delta)
        {
            RequestMoneyDeltaServerRpc(delta);
        }

        private void HandleCarryLimitDeltaRequested(int delta)
        {
            RequestCarryLimitDeltaServerRpc(delta);
        }

        private void HandleCarryLimitResetRequested()
        {
            RequestCarryLimitResetServerRpc();
        }

        private void HandleSkillCooldownsResetRequested()
        {
            RequestSkillCooldownsResetServerRpc();
        }

        private void HandleSkillsUnlockRequested()
        {
            RequestUnlockAllSkillsServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestMoneyDeltaServerRpc(
            long requestedDelta,
            ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!ValidateRequest(sender, out string rejection))
            {
                SendResult(sender, false, rejection);
                return;
            }

            long maxDelta = maxMoneyDeltaPerCommand < 1
                ? 1
                : maxMoneyDeltaPerCommand;
            long delta = requestedDelta;

            if (delta > maxDelta)
            {
                delta = maxDelta;
            }
            else if (delta < -maxDelta)
            {
                delta = -maxDelta;
            }

            NetworkSharedWallet wallet = NetworkSharedWallet.Instance;

            if (wallet == null || !wallet.IsSpawned)
            {
                SendResult(sender, false, "Shared wallet is not ready.");
                return;
            }

            bool succeeded;
            string message;

            if (delta > 0)
            {
                succeeded = wallet.GrantServer(
                    delta,
                    $"Development command from client {sender}");
                message = succeeded
                    ? $"Added {delta:N0}. Balance: {wallet.Balance:N0}"
                    : "Money grant was rejected.";
            }
            else
            {
                long spendAmount = -delta;
                succeeded = wallet.TrySpendServer(
                    spendAmount,
                    $"Development command from client {sender}");
                message = succeeded
                    ? $"Removed {spendAmount:N0}. Balance: " +
                      $"{wallet.Balance:N0}"
                    : "Not enough shared money.";
            }

            SendResult(sender, succeeded, message);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestCarryLimitDeltaServerRpc(
            int requestedDelta,
            ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!ValidateRequest(sender, out string rejection) ||
                !TryResolveCarrier(sender, out NetworkItemCarrier carrier))
            {
                SendResult(
                    sender,
                    false,
                    string.IsNullOrEmpty(rejection)
                        ? "Requesting player's carrier is not ready."
                        : rejection);
                return;
            }

            int maxDelta = Mathf.Max(1, maxCarryDeltaPerCommand);
            int delta = Mathf.Clamp(
                requestedDelta,
                -maxDelta,
                maxDelta);
            int minimumAllowed = Mathf.Max(1, carrier.HeldItemCount);
            int target = Mathf.Max(
                minimumAllowed,
                carrier.CarryLimit + delta);

            carrier.SetCarryLimit(target);
            SendResult(
                sender,
                true,
                $"Carry limit set to {carrier.CarryLimit}.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestCarryLimitResetServerRpc(
            ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!ValidateRequest(sender, out string rejection) ||
                !TryResolveCarrier(sender, out NetworkItemCarrier carrier))
            {
                SendResult(
                    sender,
                    false,
                    string.IsNullOrEmpty(rejection)
                        ? "Requesting player's carrier is not ready."
                        : rejection);
                return;
            }

            carrier.ResetCarryLimitToDefault();
            SendResult(
                sender,
                true,
                $"Carry limit reset to {carrier.CarryLimit}.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSkillCooldownsResetServerRpc(
            ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!ValidateRequest(sender, out string rejection))
            {
                SendResult(sender, false, rejection);
                return;
            }

            NetworkSharedSkillService skills =
                NetworkSharedSkillService.Instance;
            bool succeeded = skills != null &&
                             skills.ResetAllCooldownsServer();

            SendResult(
                sender,
                succeeded,
                succeeded
                    ? "Every shared skill cooldown was reset."
                    : "Shared skill service is not ready.");
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestUnlockAllSkillsServerRpc(
            ServerRpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;

            if (!ValidateRequest(sender, out string rejection))
            {
                SendResult(sender, false, rejection);
                return;
            }

            NetworkSharedSkillService skills =
                NetworkSharedSkillService.Instance;
            bool succeeded = skills != null &&
                             skills.UnlockAllSkillsServer();

            SendResult(
                sender,
                succeeded,
                succeeded
                    ? "Every shared skill was unlocked."
                    : "Shared skill service is not ready.");
        }

        private bool ValidateRequest(
            ulong senderClientId,
            out string rejectionMessage)
        {
            rejectionMessage = string.Empty;

            bool buildAllowed = Application.isEditor ||
                                Debug.isDebugBuild ||
                                allowInNonDevelopmentBuild;

            if (!buildAllowed)
            {
                rejectionMessage =
                    "Development commands are disabled in this build.";
                return false;
            }

            if (!allowRemoteClients &&
                senderClientId !=
                Unity.Netcode.NetworkManager.ServerClientId)
            {
                rejectionMessage =
                    "Only the host may use development commands.";
                return false;
            }

            if (NetworkManager == null ||
                !NetworkManager.ConnectedClients.ContainsKey(senderClientId))
            {
                rejectionMessage = "Requesting client is not connected.";
                return false;
            }

            return true;
        }

        private bool TryResolveCarrier(
            ulong clientId,
            out NetworkItemCarrier carrier)
        {
            carrier = null;

            if (NetworkManager == null ||
                !NetworkManager.ConnectedClients.TryGetValue(
                    clientId,
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

        private void SendResult(
            ulong targetClientId,
            bool succeeded,
            string message)
        {
            var sendParams = new ClientRpcSendParams
            {
                TargetClientIds = new[] { targetClientId }
            };

            DevelopmentCommandResultClientRpc(
                succeeded,
                new FixedString512Bytes(message ?? string.Empty),
                new ClientRpcParams { Send = sendParams });
        }

        [ClientRpc]
        private void DevelopmentCommandResultClientRpc(
            bool succeeded,
            FixedString512Bytes message,
            ClientRpcParams rpcParams = default)
        {
            SharedDevelopmentActions.RaiseCommandResult(
                succeeded,
                message.ToString());
        }
    }
}
