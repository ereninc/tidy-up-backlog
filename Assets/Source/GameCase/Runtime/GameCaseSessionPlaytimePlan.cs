using System.Collections.Generic;

namespace EXW.Multiplayer
{
	/// <summary>
	/// Local process-wide playtime metadata for the current
	/// game-case session.
	///
	/// Populated in LoadingScene from the replicated manifest
	/// and consumed in GameScene.
	/// </summary>
	public static class GameCaseSessionPlaytimePlan
	{
		private static readonly Dictionary<uint, uint>
			PlaytimeByAppId =
				new Dictionary<uint, uint>();

		public static void Clear()
		{
			PlaytimeByAppId.Clear();
		}

		public static void Set(
			IReadOnlyList<uint> appIds,
			IReadOnlyList<uint> playtimeMinutes)
		{
			PlaytimeByAppId.Clear();

			if (appIds == null ||
			    playtimeMinutes == null)
			{
				return;
			}

			int count =
				System.Math.Min(
					appIds.Count,
					playtimeMinutes.Count);

			for (int i = 0;
				i < count;
				i++)
			{
				uint appId =
					appIds[i];

				if (appId == 0)
				{
					continue;
				}

				PlaytimeByAppId[appId] =
					playtimeMinutes[i];
			}
		}

		public static bool TryGet(
			uint appId,
			out uint playtimeMinutes)
		{
			return PlaytimeByAppId.TryGetValue(
				appId,
				out playtimeMinutes);
		}

		public static uint GetOrDefault(
			uint appId)
		{
			return PlaytimeByAppId.TryGetValue(
				appId,
				out uint playtime)
				? playtime
				: 0u;
		}
	}
}