#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EXW.Multiplayer.Editor
{
	/// <summary>
	/// Starts multiplayer cleanup at ExitingPlayMode, before Unity destroys the
	/// runtime components. The coordinator remains the single shutdown owner.
	/// </summary>
	[InitializeOnLoad]
	internal static class MultiplayerShutdownCoordinatorEditorBridge
	{
		static MultiplayerShutdownCoordinatorEditorBridge()
		{
			EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
			EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
		}

		private static void HandlePlayModeStateChanged(PlayModeStateChange state)
		{
			if (state != PlayModeStateChange.ExitingPlayMode)
			{
				return;
			}

			bool found = global::EXW.Multiplayer.MultiplayerShutdownCoordinator
			                   .TryShutdownActiveCoordinator("Unity Editor exiting Play Mode");

			if (!found)
			{
				Debug.LogWarning(
					"[MultiplayerShutdown] No active coordinator was found while " +
					"exiting Play Mode. SteamBootstrap fallback shutdown will run.");
			}
		}
	}
}
#endif