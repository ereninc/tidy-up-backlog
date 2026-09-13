using System;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Shared local-input lock used by gameplay systems. UI, loading screens and
    /// player-presence code can each own a lock without accidentally unlocking
    /// another system's lock.
    /// </summary>
    public static class GameplayInputGate
    {
        private static readonly HashSet<int> Blockers = new HashSet<int>();

        public static event Action<bool> BlockStateChanged;

        public static bool IsBlocked => Blockers.Count > 0;
        public static int BlockerCount => Blockers.Count;

        /// <summary>
        /// Adds or removes this owner's input lock. Returns true when the global
        /// blocked state changed.
        /// </summary>
        public static bool SetBlocked(UnityEngine.Object owner, bool blocked)
        {
            if (owner == null)
            {
                Debug.LogWarning(
                    "[GameplayInputGate] Ignored a lock request with no owner.");
                return false;
            }

            bool wasBlocked = IsBlocked;
            int ownerId = owner.GetInstanceID();

            if (blocked)
            {
                Blockers.Add(ownerId);
            }
            else
            {
                Blockers.Remove(ownerId);
            }

            bool isBlocked = IsBlocked;

            if (wasBlocked != isBlocked)
            {
                BlockStateChanged?.Invoke(isBlocked);
                return true;
            }

            return false;
        }

        public static bool Clear(UnityEngine.Object owner)
        {
            return SetBlocked(owner, false);
        }

        /// <summary>
        /// Emergency/session-boundary reset. Normal UI code should release only
        /// its own lock with Clear(owner).
        /// </summary>
        public static void ClearAll()
        {
            bool wasBlocked = IsBlocked;
            Blockers.Clear();

            if (wasBlocked)
            {
                BlockStateChanged?.Invoke(false);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Blockers.Clear();
            BlockStateChanged = null;
        }
    }
}
