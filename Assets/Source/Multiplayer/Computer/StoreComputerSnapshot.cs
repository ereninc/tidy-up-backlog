using System;
using Unity.Netcode;

namespace EXW.Multiplayer
{
    public enum StoreComputerApp : byte
    {
        Desktop = 0,
        Upgrades = 1,
        StoreInfo = 2
    }

    [Serializable]
    public struct StoreComputerSnapshot :
        INetworkSerializable,
        IEquatable<StoreComputerSnapshot>
    {
        public bool IsInUse;
        public ulong UserClientId;
        public uint SessionToken;
        public StoreComputerApp ActiveApp;

        public bool CanControl(ulong senderClientId, uint sessionToken)
        {
            return IsInUse && SessionToken != 0 &&
                   UserClientId == senderClientId &&
                   SessionToken == sessionToken;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref IsInUse);
            serializer.SerializeValue(ref UserClientId);
            serializer.SerializeValue(ref SessionToken);
            serializer.SerializeValue(ref ActiveApp);
        }

        public bool Equals(StoreComputerSnapshot other)
        {
            return IsInUse == other.IsInUse &&
                   UserClientId == other.UserClientId &&
                   SessionToken == other.SessionToken &&
                   ActiveApp == other.ActiveApp;
        }

        public override bool Equals(object obj)
        {
            return obj is StoreComputerSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = IsInUse.GetHashCode();
                hash = (hash * 397) ^ UserClientId.GetHashCode();
                hash = (hash * 397) ^ SessionToken.GetHashCode();
                hash = (hash * 397) ^ (int)ActiveApp;
                return hash;
            }
        }
    }
}
