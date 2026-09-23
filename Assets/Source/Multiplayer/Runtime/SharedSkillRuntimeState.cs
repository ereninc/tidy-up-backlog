using System;
using Unity.Netcode;

namespace EXW.Multiplayer
{
    /// <summary>Small replicated state for one shared skill.</summary>
    [Serializable]
    public struct SharedSkillRuntimeState :
        INetworkSerializable,
        IEquatable<SharedSkillRuntimeState>
    {
        public int SkillId;
        public ushort Level;
        public bool IsUnlocked;
        public double CooldownEndsAt;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref SkillId);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref IsUnlocked);
            serializer.SerializeValue(ref CooldownEndsAt);
        }

        public bool Equals(SharedSkillRuntimeState other)
        {
            return SkillId == other.SkillId &&
                   Level == other.Level &&
                   IsUnlocked == other.IsUnlocked &&
                   CooldownEndsAt.Equals(other.CooldownEndsAt);
        }

        public override bool Equals(object obj)
        {
            return obj is SharedSkillRuntimeState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SkillId;
                hash = (hash * 397) ^ Level;
                hash = (hash * 397) ^ IsUnlocked.GetHashCode();
                hash = (hash * 397) ^ CooldownEndsAt.GetHashCode();
                return hash;
            }
        }
    }
}
