using System;
using Unity.Netcode;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Compact replicated state for one logical game-case shelf slot.
    /// The individual cases keep using NetworkWorldItem location state; this
    /// snapshot only carries the information clients need for rules and UI.
    /// </summary>
    [Serializable]
    public struct GameCaseShelfSlotSnapshot :
        INetworkSerializable,
        IEquatable<GameCaseShelfSlotSnapshot>
    {
        public uint LockedAppId;
        public ushort OccupiedCount;
        public short NextFreeIndex;
        public bool IsComplete;
        public uint Revision;

        public static GameCaseShelfSlotSnapshot Empty(uint revision)
        {
            return new GameCaseShelfSlotSnapshot
            {
                LockedAppId = 0,
                OccupiedCount = 0,
                NextFreeIndex = 0,
                IsComplete = false,
                Revision = revision
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref LockedAppId);
            serializer.SerializeValue(ref OccupiedCount);
            serializer.SerializeValue(ref NextFreeIndex);
            serializer.SerializeValue(ref IsComplete);
            serializer.SerializeValue(ref Revision);
        }

        public bool Equals(GameCaseShelfSlotSnapshot other)
        {
            return LockedAppId == other.LockedAppId &&
                   OccupiedCount == other.OccupiedCount &&
                   NextFreeIndex == other.NextFreeIndex &&
                   IsComplete == other.IsComplete &&
                   Revision == other.Revision;
        }

        public override bool Equals(object obj)
        {
            return obj is GameCaseShelfSlotSnapshot other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)LockedAppId;
                hash = (hash * 397) ^ OccupiedCount;
                hash = (hash * 397) ^ NextFreeIndex;
                hash = (hash * 397) ^ IsComplete.GetHashCode();
                hash = (hash * 397) ^ (int)Revision;
                return hash;
            }
        }
    }
}
