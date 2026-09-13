// Asset Recall v1.0.0 - by Compile&Co.
// Centralized text content used throughout the Asset Recall editor tool.

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Contains all user-facing text and string constants used in the Asset Recall editor window and related utilities.
	/// Centralizing text improves maintainability, localization, and consistency.
	/// </summary>
	public static class AssetRecallText
	{
		// ───── EDITOR WINDOW ─────
		
		/// <summary>Title of the Asset Recall editor window.</summary>
		public const string EditorWindowTitle = "Asset Recall";
		
		/// <summary>Label for the max history items slider.</summary>
		public const string MaxHistoryItems = "Max History Items:";
		
		/// <summary>Label for the filter dropdown.</summary>
		public const string FilterByType = "Filter by Type:";

		/// <summary>Header label for the favorites section.</summary>
		public const string FavoritesLabel = "Favorites";

		/// <summary>Header label for the recent items section.</summary>
		public const string RecentItemsLabel = "Recent Items";

		/// <summary>Confirmation title shown when clearing history.</summary>
		public const string ClearConfirmTitle = "Clear Selection History";

		/// <summary>Confirmation message shown when clearing history.</summary>
		public const string ClearConfirmMessage = "Are you sure you want to clear the selection history? Pinned items will not be removed.";

		/// <summary>Message displayed when no assets have been selected yet.</summary>
		public const string NoAssetsMessage = "No assets selected yet.";

		/// <summary>Tooltip for scene objects with no asset path.</summary>
		public const string SceneObjectTooltip = "Scene Object";

		/// <summary>Text for the remove (X) button.</summary>
		public const string RemoveItemButtonText = "X";

		// ───── LOGS ─────

		/// <summary>Message logged when a scene object is not found and removed from the list.</summary>
		public const string RemovedUnavailable = "[AssetRecall] Removed unavailable scene object";
	}
}
