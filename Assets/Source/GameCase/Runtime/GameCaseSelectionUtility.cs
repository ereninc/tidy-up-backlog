using System;
using System.Collections.Generic;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Selects games fairly across lobby members and builds an exact-count,
    /// deterministic case assignment for every peer.
    /// </summary>
    public static class GameCaseSelectionUtility
    {
        public static List<uint> SelectDistinctFairly(
            IReadOnlyList<IReadOnlyList<uint>> playerLibraries,
            IReadOnlyList<uint> fallbackAppIds,
            IReadOnlyCollection<uint> excludedAppIds,
            int targetCount,
            uint seed)
        {
            if (targetCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetCount));
            }

            var excluded = excludedAppIds != null
                ? new HashSet<uint>(excludedAppIds)
                : new HashSet<uint>();
            var libraries = new List<List<uint>>();
            var random = new GameCaseDeterministicRandom(seed);

            if (playerLibraries != null)
            {
                for (int i = 0; i < playerLibraries.Count; i++)
                {
                    IReadOnlyList<uint> source = playerLibraries[i];
                    var localUnique = new HashSet<uint>();
                    var filtered = new List<uint>();

                    if (source != null)
                    {
                        for (int j = 0; j < source.Count; j++)
                        {
                            uint appId = source[j];

                            if (appId != 0 &&
                                !excluded.Contains(appId) &&
                                localUnique.Add(appId))
                            {
                                filtered.Add(appId);
                            }
                        }
                    }

                    if (filtered.Count > 0)
                    {
                        Shuffle(filtered, ref random);
                        libraries.Add(filtered);
                    }
                }
            }

            // Do not always let the lobby owner get the first pick.
            Shuffle(libraries, ref random);

            var selected = new List<uint>(targetCount);
            var selectedSet = new HashSet<uint>();
            var cursors = new int[libraries.Count];

            while (selected.Count < targetCount)
            {
                bool madeProgress = false;

                for (int i = 0;
                     i < libraries.Count && selected.Count < targetCount;
                     i++)
                {
                    List<uint> library = libraries[i];

                    while (cursors[i] < library.Count)
                    {
                        uint candidate = library[cursors[i]++];

                        if (!selectedSet.Add(candidate))
                        {
                            continue;
                        }

                        selected.Add(candidate);
                        madeProgress = true;
                        break;
                    }
                }

                if (!madeProgress)
                {
                    break;
                }
            }

            if (selected.Count < targetCount && fallbackAppIds != null)
            {
                var fallback = new List<uint>();

                for (int i = 0; i < fallbackAppIds.Count; i++)
                {
                    uint appId = fallbackAppIds[i];

                    if (appId != 0 &&
                        !excluded.Contains(appId) &&
                        !selectedSet.Contains(appId))
                    {
                        fallback.Add(appId);
                    }
                }

                Shuffle(fallback, ref random);

                for (int i = 0;
                     i < fallback.Count && selected.Count < targetCount;
                     i++)
                {
                    uint appId = fallback[i];

                    if (selectedSet.Add(appId))
                    {
                        selected.Add(appId);
                    }
                }
            }

            return selected;
        }

        public static int[] BuildExactAssignment(
            int distinctGameCount,
            int copiesPerGame,
            uint seed)
        {
            if (distinctGameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(distinctGameCount));
            }

            if (copiesPerGame <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(copiesPerGame));
            }

            int total = checked(distinctGameCount * copiesPerGame);
            var assignment = new int[total];
            int index = 0;

            for (int gameIndex = 0;
                 gameIndex < distinctGameCount;
                 gameIndex++)
            {
                for (int copy = 0; copy < copiesPerGame; copy++)
                {
                    assignment[index++] = gameIndex;
                }
            }

            var random = new GameCaseDeterministicRandom(seed);
            Shuffle(assignment, ref random);
            return assignment;
        }

        private static void Shuffle<T>(
            IList<T> values,
            ref GameCaseDeterministicRandom random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                T temporary = values[i];
                values[i] = values[other];
                values[other] = temporary;
            }
        }

        private struct GameCaseDeterministicRandom
        {
            private uint _state;

            public GameCaseDeterministicRandom(uint seed)
            {
                _state = seed == 0 ? 0xA341316Cu : seed;
            }

            public int Next(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 1)
                {
                    return 0;
                }

                uint value = NextUInt();
                return (int)(value % (uint)exclusiveMaximum);
            }

            private uint NextUInt()
            {
                uint value = _state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                _state = value;
                return value;
            }
        }
    }
}
