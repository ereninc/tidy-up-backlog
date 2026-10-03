using UnityEngine;

namespace EXW.Multiplayer
{
	/// <summary>
	/// Local settings captured by the menu before loading singleplayer.
	/// CO-OP continues to read its settings from the Steam lobby flow.
	/// </summary>
	public static class GameSessionLaunchOptions
	{
		public static bool SinglePlayerHideNsfwGames { get; private set; } = true;

		public static void ConfigureSinglePlayer(bool hideNsfwGames)
		{
			SinglePlayerHideNsfwGames = hideNsfwGames;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetLocalOptions()
		{
			SinglePlayerHideNsfwGames = true;
		}
	}
}