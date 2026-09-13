using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Authoritative identity and persistent location state for one world item.
    /// Capabilities such as carrying and using remain separate components.
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

        private void Awake()
        {
            _authoredLocalScale = transform.localScale;
        }

        public override void OnNetworkSpawn()
        {
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
    }
}
