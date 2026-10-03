using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Authoritative identity and persistent location state for one world item.
    /// Capabilities such as carrying and using remain separate components.
    /// Physics settle poses are transient RPCs, not per-item NetworkVariables.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network World Item")]
    public sealed class NetworkWorldItem : NetworkBehaviour
    {
        private readonly NetworkVariable<NetworkItemLocationState> _location =
            new NetworkVariable<NetworkItemLocationState>(
                NetworkItemLocationState.World(0),
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<ulong> _sessionInstanceId =
            new NetworkVariable<ulong>(
                0,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        [InfoBox(
            "Definition is optional for prototypes. Without it, Display Name " +
            "is used. Location is one atomic NetworkVariable and is safe for " +
            "late joiners.")]
        [TitleGroup("Identity")]
        [SerializeField] private NetworkItemDefinition definition;

        [TitleGroup("Identity")]
        [SerializeField] private string fallbackDisplayName = "World Item";

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Item")]
        public string DisplayName => definition != null
            ? definition.DisplayName
            : string.IsNullOrWhiteSpace(fallbackDisplayName)
                ? gameObject.name
                : fallbackDisplayName;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Item")]
        public NetworkItemLocationKind LocationKind => Location.Kind;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Item")]
        public ulong SessionInstanceId => _sessionInstanceId.Value;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Live Item")]
        public uint Revision => Location.Revision;

        public NetworkItemDefinition Definition => definition;
        public NetworkItemLocationState Location => _location.Value;
        public bool IsHeld => Location.IsHeld;
        public bool IsPlaced => Location.IsPlaced;

        public event Action<
            NetworkItemLocationState,
            NetworkItemLocationState> LocationChanged;

        private bool _hasPreparedParentPose;
        private Vector3 _preparedLocalPosition;
        private Quaternion _preparedLocalRotation;
        private Vector3 _authoredLocalScale;
        private bool _hasFinalPhysicsRevision;
        private uint _finalPhysicsRevision;

        private void Awake()
        {
            _authoredLocalScale = transform.localScale;
        }

        public override void OnNetworkSpawn()
        {
            _hasFinalPhysicsRevision = false;
            _location.OnValueChanged += HandleLocationChanged;

            if (IsServer)
            {
                if (_sessionInstanceId.Value == 0)
                {
                    _sessionInstanceId.Value = NetworkObjectId;
                }

                if (_location.Value.Revision == 0)
                {
                    _location.Value = NetworkItemLocationState.World(1);
                }
            }

            LocationChanged?.Invoke(_location.Value, _location.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                ReleaseExternalBookkeepingServer();
            }

            _hasFinalPhysicsRevision = false;
            _location.OnValueChanged -= HandleLocationChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// NGO invokes this during its server-side parent transaction. Applying
        /// the prepared local pose here includes that pose in the ParentSyncMessage.
        /// </summary>
        public override void OnNetworkObjectParentChanged(
            NetworkObject parentNetworkObject)
        {
            if (!IsServer || !_hasPreparedParentPose)
            {
                return;
            }

            ApplyPreparedParentPoseServer();
        }

        internal void PrepareParentPoseServer(
            Vector3 localPosition,
            Quaternion localRotation)
        {
            if (!IsServer)
            {
                return;
            }

            _preparedLocalPosition = localPosition;
            _preparedLocalRotation = localRotation;
            _hasPreparedParentPose = true;
        }

        internal void ApplyPreparedParentPoseServer()
        {
            if (!IsServer || !_hasPreparedParentPose)
            {
                return;
            }

            transform.localPosition = _preparedLocalPosition;
            transform.localRotation = _preparedLocalRotation;
            transform.localScale = _authoredLocalScale;
            _hasPreparedParentPose = false;
        }

        internal void CancelPreparedParentPoseServer()
        {
            _hasPreparedParentPose = false;
        }

        internal void SetLocationServer(NetworkItemLocationState state)
        {
            if (!IsServer)
            {
                return;
            }

            _location.Value = state;
        }

        internal uint NextRevisionServer()
        {
            uint next = _location.Value.Revision + 1;
            return next == 0 ? 1u : next;
        }

        internal bool TryResolveReceiver(out NetworkItemReceiver receiver)
        {
            receiver = null;

            if (!Location.IsPlaced || NetworkManager == null ||
                !Location.Receiver.TryGet(
                    out NetworkObject receiverObject,
                    NetworkManager))
            {
                return false;
            }

            return receiverObject.TryGetComponent(out receiver);
        }

        internal void BroadcastPhysicsPoseServer(
            Vector3 worldPosition,
            Quaternion worldRotation,
            uint revision,
            bool final)
        {
            if (!IsServer || !IsSpawned || !Location.IsWorld ||
                Revision != revision ||
                !IsFinite(worldPosition) || !IsFinite(worldRotation))
            {
                return;
            }

            if (final)
            {
                ApplyPhysicsFinalPoseClientRpc(
                    worldPosition,
                    worldRotation,
                    revision);
            }
            else
            {
                ApplyPhysicsPoseSnapshotClientRpc(
                    worldPosition,
                    worldRotation,
                    revision);
            }
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void ApplyPhysicsPoseSnapshotClientRpc(
            Vector3 worldPosition,
            Quaternion worldRotation,
            uint revision)
        {
            if (IsServer ||
                (_hasFinalPhysicsRevision &&
                 _finalPhysicsRevision == revision))
            {
                return;
            }

            ApplyPhysicsPoseLocal(
                worldPosition,
                worldRotation,
                revision,
                false);
        }

        [ClientRpc]
        private void ApplyPhysicsFinalPoseClientRpc(
            Vector3 worldPosition,
            Quaternion worldRotation,
            uint revision)
        {
            if (IsServer)
            {
                return;
            }

            ApplyPhysicsPoseLocal(
                worldPosition,
                worldRotation,
                revision,
                true);
        }

        private void ApplyPhysicsPoseLocal(
            Vector3 worldPosition,
            Quaternion worldRotation,
            uint revision,
            bool final)
        {
            if (!IsSpawned || !Location.IsWorld || Revision != revision ||
                !IsFinite(worldPosition) || !IsFinite(worldRotation))
            {
                return;
            }

            if (final)
            {
                _hasFinalPhysicsRevision = true;
                _finalPhysicsRevision = revision;
            }

            if (TryGetComponent(
                    out NetworkItemMotionPresenter presenter))
            {
                presenter.ApplyReplicatedWorldPoseLocal(
                    worldPosition,
                    worldRotation,
                    revision,
                    final);
                return;
            }

            transform.SetPositionAndRotation(
                worldPosition,
                worldRotation);
        }

        [Button("AUTO NAME FROM GAMEOBJECT")]
        private void AutoNameFromGameObject()
        {
            fallbackDisplayName = gameObject.name;
        }

#if UNITY_EDITOR
        public void EditorConfigure(string displayName)
        {
            fallbackDisplayName = displayName;
        }
#endif

        private void HandleLocationChanged(
            NetworkItemLocationState previous,
            NetworkItemLocationState current)
        {
            LocationChanged?.Invoke(previous, current);
        }

        private void ReleaseExternalBookkeepingServer()
        {
            if (_location.Value.IsPlaced && TryResolveReceiver(out var receiver))
            {
                receiver.ReleaseItemServer(this);
            }

            NetworkItemCarrier carrier =
                GetComponentInParent<NetworkItemCarrier>();

            if (carrier != null)
            {
                carrier.ClearHeldItemServer(this);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
