using System;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    public enum NetworkItemLocationKind : byte
    {
        World = 0,
        Held = 1,
        Placed = 2
    }

    public enum NetworkItemDestinationDisposition : byte
    {
        Place = 0,
        Consume = 1
    }

    /// <summary>
    /// One replicated snapshot describes where an item belongs. A single struct
    /// avoids exposing partially-applied holder/receiver/slot combinations.
    /// </summary>
    [Serializable]
    public struct NetworkItemLocationState :
        INetworkSerializable,
        IEquatable<NetworkItemLocationState>
    {
        public const ulong NoClient = ulong.MaxValue;
        public const int NoSlot = -1;

        public NetworkItemLocationKind Kind;
        public ulong HolderClientId;
        public NetworkObjectReference Receiver;
        public int SlotIndex;
        public uint Revision;

        public bool IsWorld => Kind == NetworkItemLocationKind.World;
        public bool IsHeld => Kind == NetworkItemLocationKind.Held;
        public bool IsPlaced => Kind == NetworkItemLocationKind.Placed;

        public static NetworkItemLocationState World(uint revision)
        {
            return new NetworkItemLocationState
            {
                Kind = NetworkItemLocationKind.World,
                HolderClientId = NoClient,
                Receiver = default,
                SlotIndex = NoSlot,
                Revision = revision
            };
        }

        public static NetworkItemLocationState Held(
            ulong holderClientId,
            uint revision)
        {
            return new NetworkItemLocationState
            {
                Kind = NetworkItemLocationKind.Held,
                HolderClientId = holderClientId,
                Receiver = default,
                SlotIndex = NoSlot,
                Revision = revision
            };
        }

        public static NetworkItemLocationState Placed(
            NetworkObject receiver,
            int slotIndex,
            uint revision)
        {
            return new NetworkItemLocationState
            {
                Kind = NetworkItemLocationKind.Placed,
                HolderClientId = NoClient,
                Receiver = receiver != null
                    ? new NetworkObjectReference(receiver)
                    : default,
                SlotIndex = slotIndex,
                Revision = revision
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref HolderClientId);
            serializer.SerializeValue(ref Receiver);
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(NetworkItemLocationState other)
        {
            return Kind == other.Kind &&
                   HolderClientId == other.HolderClientId &&
                   Receiver.Equals(other.Receiver) &&
                   SlotIndex == other.SlotIndex &&
                   Revision == other.Revision;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkItemLocationState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ HolderClientId.GetHashCode();
                hash = (hash * 397) ^ Receiver.GetHashCode();
                hash = (hash * 397) ^ SlotIndex;
                hash = (hash * 397) ^ (int)Revision;
                return hash;
            }
        }
    }

    /// <summary>
    /// Server-only destination decision. It is deliberately not serialized;
    /// only the resulting item state and NGO parent relationship are replicated.
    /// </summary>
    public readonly struct NetworkItemPlacementPlan
    {
        public NetworkItemPlacementPlan(
            NetworkItemDestinationDisposition disposition,
            Vector3 localPosition,
            Quaternion localRotation,
            int slotIndex)
        {
            Disposition = disposition;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            SlotIndex = slotIndex;
        }

        public NetworkItemDestinationDisposition Disposition { get; }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
        public int SlotIndex { get; }

        public static NetworkItemPlacementPlan Consume()
        {
            return new NetworkItemPlacementPlan(
                NetworkItemDestinationDisposition.Consume,
                Vector3.zero,
                Quaternion.identity,
                NetworkItemLocationState.NoSlot);
        }
    }
}
