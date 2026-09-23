using System;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One server-written wallet shared by the whole co-op session. Gameplay
    /// systems call GrantServer/TrySpendServer; clients never submit amounts.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Progression/Network Shared Wallet")]
    public sealed class NetworkSharedWallet : NetworkBehaviour
    {
        private readonly NetworkVariable<long> _balance =
            new NetworkVariable<long>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [SerializeField, Min(0)] private long startingBalance;

        public static NetworkSharedWallet Instance { get; private set; }
        public long Balance => _balance.Value;

        public event Action<long, long> BalanceChanged;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            _balance.OnValueChanged += HandleBalanceChanged;

            if (IsServer && _balance.Value == 0 && startingBalance > 0)
            {
                _balance.Value = startingBalance;
            }

            BalanceChanged?.Invoke(_balance.Value, _balance.Value);
        }

        public override void OnNetworkDespawn()
        {
            _balance.OnValueChanged -= HandleBalanceChanged;

            if (Instance == this)
            {
                Instance = null;
            }

            base.OnNetworkDespawn();
        }

        public bool GrantServer(long amount, string reason = null)
        {
            if (!IsServer || !IsSpawned || amount <= 0)
            {
                return false;
            }

            long current = _balance.Value;
            long next = current > long.MaxValue - amount
                ? long.MaxValue
                : current + amount;
            _balance.Value = next;

            Debug.Log(
                $"[SharedWallet] +{amount} ({reason ?? "No reason"}). " +
                $"Balance={next}",
                this);
            return true;
        }

        public bool TrySpendServer(long amount, string reason = null)
        {
            if (!IsServer || !IsSpawned || amount <= 0 ||
                _balance.Value < amount)
            {
                return false;
            }

            _balance.Value -= amount;
            Debug.Log(
                $"[SharedWallet] -{amount} ({reason ?? "No reason"}). " +
                $"Balance={_balance.Value}",
                this);
            return true;
        }

        [ContextMenu("DEV/Grant 100")]
        private void DevelopmentGrant100()
        {
            if (!Application.isPlaying || !IsServer)
            {
                Debug.LogWarning(
                    "Development wallet grants only run on the active server.",
                    this);
                return;
            }

            GrantServer(100, "Development grant");
        }

        private void HandleBalanceChanged(long previous, long current)
        {
            BalanceChanged?.Invoke(previous, current);
        }
    }
}
