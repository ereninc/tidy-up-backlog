using System;
using System.Collections.Generic;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local hand-off between the NGO-loaded LoadingScene and GameScene.
    /// Every peer receives the same manifest in LoadingScene before the host
    /// changes scene, so GameScene does not need 1500 replicated AppIds.
    /// </summary>
    public static class GameCaseSessionPlan
    {
        private static uint[] _appIds = Array.Empty<uint>();

        public static bool IsReady { get; private set; }
        public static IReadOnlyList<uint> AppIds => _appIds;
        public static int CopiesPerGame { get; private set; }
        public static uint AssignmentSeed { get; private set; }
        public static int TotalCaseCount =>
            IsReady ? _appIds.Length * CopiesPerGame : 0;

        public static event Action Changed;

        public static void Set(
            IReadOnlyList<uint> appIds,
            int copiesPerGame,
            uint assignmentSeed)
        {
            if (appIds == null || appIds.Count == 0)
            {
                throw new ArgumentException(
                    "At least one Steam AppId is required.",
                    nameof(appIds));
            }

            if (copiesPerGame <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(copiesPerGame));
            }

            var unique = new HashSet<uint>();
            var copied = new uint[appIds.Count];

            for (int i = 0; i < appIds.Count; i++)
            {
                uint appId = appIds[i];

                if (appId == 0 || !unique.Add(appId))
                {
                    throw new ArgumentException(
                        "The manifest must contain unique, non-zero AppIds.",
                        nameof(appIds));
                }

                copied[i] = appId;
            }

            _appIds = copied;
            CopiesPerGame = copiesPerGame;
            AssignmentSeed = assignmentSeed == 0 ? 1u : assignmentSeed;
            IsReady = true;
            Changed?.Invoke();
        }

        public static void Clear()
        {
            _appIds = Array.Empty<uint>();
            CopiesPerGame = 0;
            AssignmentSeed = 0;
            IsReady = false;
            Changed?.Invoke();
        }
    }
}
