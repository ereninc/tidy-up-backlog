// Asset Recall v1.0.0 - by Compile&Co.
// A lightweight Unity Editor extension to track and revisit recently selected assets.

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Contains all constant values used throughout the Asset Recall tool.
	/// </summary>
	public static class AssetRecallConstants
	{
		public const string MaxAssetsKey = "AssetRecall_MaxAssets_Key";

		public const int MinAssets = 5;
		public const int MaxAssetsLimit = 50;

		public const double ValidationInterval = 2.0f; // Seconds
	}
}