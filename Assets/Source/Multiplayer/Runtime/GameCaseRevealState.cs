using System;
using Unity.Netcode;

namespace EXW.Multiplayer
{
    /// <summary>
    /// One replicated reveal command replaces one NetworkObject per indicator.
    /// Sequence makes repeated activation of the same AppId observable.
    /// </summary>
    [Serializable]
    public struct GameCaseRevealState :
        INetworkSerializable,
        IEquatable<GameCaseRevealState>
    {
        public int SkillId;
        public uint TargetAppId;
        public double ExpiresAt;
        public uint Sequence;

        public bool IsValid => TargetAppId != 0 && Sequence != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref SkillId);
            serializer.SerializeValue(ref TargetAppId);
            serializer.SerializeValue(ref ExpiresAt);
            serializer.SerializeValue(ref Sequence);
        }

        public bool Equals(GameCaseRevealState other)
        {
            return SkillId == other.SkillId &&
                   TargetAppId == other.TargetAppId &&
                   ExpiresAt.Equals(other.ExpiresAt) &&
                   Sequence == other.Sequence;
        }

        public override bool Equals(object obj)
        {
            return obj is GameCaseRevealState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SkillId;
                hash = (hash * 397) ^ (int)TargetAppId;
                hash = (hash * 397) ^ ExpiresAt.GetHashCode();
                hash = (hash * 397) ^ (int)Sequence;
                return hash;
            }
        }
    }
}
