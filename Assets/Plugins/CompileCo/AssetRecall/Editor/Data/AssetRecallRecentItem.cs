// Asset Recall v1.0.0 - by Compile&Co.
// Represents a tracked item in the Asset Recall history.

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Represents a single tracked asset in the recall history.
	/// Contains references to either a project asset or a scene object.
	/// </summary>
	[System.Serializable]
	public class AssetRecallRecentItem
	{
		public string assetPath;    // File path for project assets
		public string scenePath;    // Scene path for scene objects
		public string objectPath;   // Hierarchy path for scene objects
		public bool isPinned;       // Whether this item is pinned (Favorites)
	}
}