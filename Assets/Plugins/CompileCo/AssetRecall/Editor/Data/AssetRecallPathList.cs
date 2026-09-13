// Asset Recall v1.0.0 - by Compile&Co.
// Serializable container used to store recent asset selection history in JSON format.

using System.Collections.Generic;

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Serializable container class used for saving and loading the list of recently selected assets.
	/// This is used by Unity's JsonUtility for persisting data to disk.
	/// </summary>
	[System.Serializable]
	public class AssetRecallPathList
	{
		/// <summary>
		/// The list of recent asset items to be serialized.
		/// </summary>
		public List<AssetRecallRecentItem> items;
	}
}