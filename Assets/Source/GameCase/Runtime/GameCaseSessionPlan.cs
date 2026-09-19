using System;
using System.Collections.Generic;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local hand-off between the NGO-loaded LoadingScene and GameScene.
    /// Every peer receives the same manifest in LoadingScene before the host
    /// changes scene, so GameScene does not need per-case replicated metadata.
    /// </summary>
    public static class GameCaseSessionPlan
    {
        private static uint[] _appIds = Array.Empty<uint>();
        private static string[] _gameNames = Array.Empty<string>();

        private static readonly Dictionary<uint, string>
            GameNameByAppId = new Dictionary<uint, string>();

        public static bool IsReady { get; private set; }
        public static IReadOnlyList<uint> AppIds => _appIds;
        public static IReadOnlyList<string> GameNames => _gameNames;
        public static int CopiesPerGame { get; private set; }
        public static uint AssignmentSeed { get; private set; }
        public static int TotalCaseCount =>
            IsReady ? _appIds.Length * CopiesPerGame : 0;

        public static event Action Changed;

        /// <summary>
        /// Compatibility overload for direct-scene tests and older callers.
        /// Games without supplied metadata use "App {id}" as their label.
        /// </summary>
        public static void Set(
            IReadOnlyList<uint> appIds,
            int copiesPerGame,
            uint assignmentSeed)
        {
            Set(
                appIds,
                null,
                copiesPerGame,
                assignmentSeed);
        }

        public static void Set(
            IReadOnlyList<uint> appIds,
            IReadOnlyList<string> gameNames,
            int copiesPerGame,
            uint assignmentSeed)
        {
            if (appIds == null || appIds.Count == 0)
            {
                throw new ArgumentException(
                    "At least one Steam AppId is required.",
                    nameof(appIds));
            }

            if (gameNames != null &&
                gameNames.Count != appIds.Count)
            {
                throw new ArgumentException(
                    "Game names must use the same order and count as AppIds.",
                    nameof(gameNames));
            }

            if (copiesPerGame <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(copiesPerGame));
            }

            var unique = new HashSet<uint>();
            var copiedAppIds = new uint[appIds.Count];
            var copiedNames = new string[appIds.Count];
            var nameLookup = new Dictionary<uint, string>(appIds.Count);

            for (int i = 0; i < appIds.Count; i++)
            {
                uint appId = appIds[i];

                if (appId == 0 || !unique.Add(appId))
                {
                    throw new ArgumentException(
                        "The manifest must contain unique, non-zero AppIds.",
                        nameof(appIds));
                }

                string gameName =
                    gameNames != null &&
                    !string.IsNullOrWhiteSpace(gameNames[i])
                        ? gameNames[i].Trim()
                            .Replace('\r', ' ')
                            .Replace('\n', ' ')
                        : $"App {appId}";

                copiedAppIds[i] = appId;
                copiedNames[i] = gameName;
                nameLookup.Add(appId, gameName);
            }

            _appIds = copiedAppIds;
            _gameNames = copiedNames;

            GameNameByAppId.Clear();

            foreach (KeyValuePair<uint, string> pair in nameLookup)
            {
                GameNameByAppId.Add(pair.Key, pair.Value);
            }

            CopiesPerGame = copiesPerGame;
            AssignmentSeed = assignmentSeed == 0 ? 1u : assignmentSeed;
            IsReady = true;
            Changed?.Invoke();
        }

        public static bool TryGetGameName(
            uint appId,
            out string gameName)
        {
            return GameNameByAppId.TryGetValue(
                appId,
                out gameName);
        }

        public static string GetGameName(uint appId)
        {
            return TryGetGameName(appId, out string gameName)
                ? gameName
                : $"App {appId}";
        }

        public static void Clear()
        {
            _appIds = Array.Empty<uint>();
            _gameNames = Array.Empty<string>();
            GameNameByAppId.Clear();
            CopiesPerGame = 0;
            AssignmentSeed = 0;
            IsReady = false;
            Changed?.Invoke();
        }
    }
}
