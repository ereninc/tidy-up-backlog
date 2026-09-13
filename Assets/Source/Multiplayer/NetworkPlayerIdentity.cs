using System;
using System.Collections.Generic;
using System.Globalization;
using Sirenix.OdinInspector;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Replicates the host-approved Steam identity with the NGO player object.
    /// Clients never choose their own identity here; only the server can write the
    /// NetworkVariables.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Network Player Identity")]
    public sealed class NetworkPlayerIdentity : NetworkBehaviour
    {
        private readonly NetworkVariable<ulong> _steamId =
            new NetworkVariable<ulong>(
                0UL,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString128Bytes> _personaName =
            new NetworkVariable<FixedString128Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public event Action<NetworkPlayerIdentity> IdentityChanged;

        [InfoBox(
            "SteamID and persona name are written by the host from connection " +
            "approval data, then replicated to every client.")]
        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("Ready")]
        public bool IsIdentityReady => IsSpawned && _steamId.Value != 0;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("NGO Client ID")]
        public ulong ClientId => IsSpawned ? OwnerClientId : 0UL;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("Steam ID")]
        public ulong SteamId => _steamId.Value;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("Persona")]
        public string PersonaName => _personaName.Value.ToString();

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("Local Owner")]
        public bool IsLocalOwner => IsSpawned && IsOwner;

        [ShowInInspector]
        [Sirenix.OdinInspector.ReadOnly]
        [BoxGroup("Live Identity")]
        [LabelText("Host Player")]
        public bool IsHostPlayer =>
            IsSpawned &&
            OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId;

        private bool IsPlayMode => Application.isPlaying;

        private NetworkPlayerRegistry _registry;

        public override void OnNetworkSpawn()
        {
            _steamId.OnValueChanged += HandleSteamIdChanged;
            _personaName.OnValueChanged += HandlePersonaNameChanged;

            ResolveRegistry();

            if (IsServer)
            {
                InitializeServerIdentity();
            }

            ApplyReplicatedIdentity();
        }

        public override void OnNetworkDespawn()
        {
            _steamId.OnValueChanged -= HandleSteamIdChanged;
            _personaName.OnValueChanged -= HandlePersonaNameChanged;

            if (_registry != null)
            {
                _registry.RemoveReplicatedIdentity(OwnerClientId);
            }
        }

        [Button("LOG IDENTITY")]
        [EnableIf(nameof(IsPlayMode))]
        private void LogIdentity()
        {
            Debug.Log(
                $"[PlayerIdentity] Spawned={IsSpawned}, ClientID={ClientId}, " +
                $"SteamID={SteamId}, Persona={PersonaName}, Owner={IsOwner}, " +
                $"HostPlayer={IsHostPlayer}",
                this);
        }

        /// <summary>
        /// Best-effort graceful-leave notice. The session coordinator gives the
        /// reliable ServerRpc a short unscaled-time window before shutting down
        /// the local transport.
        /// </summary>
        public bool NotifyIntentionalSessionLeave()
        {
            if (!IsSpawned || !IsOwner || !IsClient)
            {
                return false;
            }

            NotifyGracefulExitServerRpc();
            return true;
        }

        [ServerRpc]
        private void NotifyGracefulExitServerRpc()
        {
            SteamConnectionApproval approval = SteamConnectionApproval.Instance;

            if (approval != null)
            {
                approval.MarkClientIntentionalLeave(OwnerClientId);
            }
        }

        private void InitializeServerIdentity()
        {
            ulong resolvedSteamId = ResolveApprovedSteamId();

            if (resolvedSteamId == 0)
            {
                Debug.LogError(
                    $"[PlayerIdentity] Host could not resolve SteamID for " +
                    $"ClientID={OwnerClientId}.",
                    this);
                return;
            }

            string resolvedPersonaName = ResolvePersonaName(resolvedSteamId);
            _steamId.Value = resolvedSteamId;
            _personaName.Value = new FixedString128Bytes(resolvedPersonaName);
        }

        private ulong ResolveApprovedSteamId()
        {
            SteamConnectionApproval approval = SteamConnectionApproval.Instance;

            if (approval != null &&
                approval.TryGetApprovedSteamId(
                    OwnerClientId,
                    out ulong approvedSteamId))
            {
                return approvedSteamId;
            }

            if (OwnerClientId == Unity.Netcode.NetworkManager.ServerClientId)
            {
                return SteamBootstrap.LocalSteamId;
            }

            if (NetworkManager != null && NetworkManager.IsServer)
            {
                return NetworkManager.GetTransportIdFromClientId(OwnerClientId);
            }

            return 0UL;
        }

        private string ResolvePersonaName(ulong steamId)
        {
            if (steamId == SinglePlayerIdentity.LocalPlayerId &&
                MultiplayerFlowController.Instance != null &&
                MultiplayerFlowController.Instance.IsSinglePlayer)
            {
                return SinglePlayerIdentity.DefaultPlayerName;
            }

            SteamLobbyService lobbyService = SteamLobbyService.Instance;

            if (lobbyService != null)
            {
                IReadOnlyList<SteamLobbyMember> members =
                    lobbyService.CurrentMembers;

                for (int i = 0; i < members.Count; i++)
                {
                    SteamLobbyMember member = members[i];

                    if (member.SteamId == steamId &&
                        !string.IsNullOrWhiteSpace(member.PersonaName))
                    {
                        return member.PersonaName;
                    }
                }
            }

            if (steamId == SteamBootstrap.LocalSteamId &&
                !string.IsNullOrWhiteSpace(SteamBootstrap.LocalPersonaName))
            {
                return SteamBootstrap.LocalPersonaName;
            }

            if (SteamBootstrap.IsSteamAvailable)
            {
                try
                {
                    string steamName = SteamFriends.GetFriendPersonaName(
                        new CSteamID(steamId));

                    if (!string.IsNullOrWhiteSpace(steamName))
                    {
                        return steamName;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[PlayerIdentity] Could not read persona name for " +
                        $"SteamID={steamId}: {exception.Message}",
                        this);
                }
            }

            return steamId.ToString(CultureInfo.InvariantCulture);
        }

        private void HandleSteamIdChanged(ulong previous, ulong current)
        {
            ApplyReplicatedIdentity();
        }

        private void HandlePersonaNameChanged(
            FixedString128Bytes previous,
            FixedString128Bytes current)
        {
            ApplyReplicatedIdentity();
        }

        private void ApplyReplicatedIdentity()
        {
            if (!IsSpawned || _steamId.Value == 0)
            {
                return;
            }

            ResolveRegistry();

            string resolvedName = _personaName.Value.ToString();

            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                resolvedName = _steamId.Value.ToString(
                    CultureInfo.InvariantCulture);
            }

            if (_registry != null)
            {
                _registry.RegisterReplicatedIdentity(
                    OwnerClientId,
                    _steamId.Value,
                    resolvedName);
            }

            gameObject.name =
                $"NetworkPlayer [{resolvedName}] [Client {OwnerClientId}]";

            IdentityChanged?.Invoke(this);
        }

        private void ResolveRegistry()
        {
            if (_registry == null)
            {
                _registry = NetworkPlayerRegistry.Instance;
            }
        }
    }
}
