// Asset Recall v1.0.0 - by Compile&Co.
// Centralized styles for Asset Recall editor window UI

using UnityEditor;
using UnityEngine;

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Contains centralized GUIStyles for Asset Recall editor UI elements.
	/// </summary>
	public static class AssetRecallStyles
	{
		/// <summary>
		/// Header label style used for section titles like "Favourites".
		/// </summary>
		public static readonly GUIStyle HeaderIconFavouriteStyle = new GUIStyle()
		{
			fixedWidth = 20,
			fixedHeight = 20,
			margin = new RectOffset(10, 4, 5, 0)
		};
		
		/// <summary>
		/// Header label style used for section titles like "Recent Items".
		/// </summary>
		public static readonly GUIStyle HeaderIconRecentItemsStyle = new GUIStyle()
		{
			fixedWidth = 20,
			fixedHeight = 16,
			margin = new RectOffset(10, 4, 5, 0)
		};
		
		/// <summary>
		/// Header label style used for section titles like "Favorites" or "Recent Items".
		/// </summary>
		public static readonly GUIStyle HeaderLabel = new GUIStyle(EditorStyles.boldLabel)
		{
			padding = new RectOffset(4, 0, 4, 0)
		};
		
		/// <summary>
		/// Header MaxHistory label style used for section "MaxHistory : Count".
		/// </summary>
		public static readonly GUIStyle HeaderMaxHistoryLabel = new GUIStyle(EditorStyles.boldLabel)
		{
			padding = new RectOffset(4, 0, 1, 0)
		};
		
		/// <summary>
		/// Header FilterType label style used for section "Filter by Type:".
		/// </summary>
		public static readonly GUIStyle HeaderFilterType = new GUIStyle(EditorStyles.boldLabel)
		{
			padding = new RectOffset(4, 0, 0, 0)
		};
		
		/// <summary>
		/// Style for the main asset button with icon and clipped label.
		/// </summary>
		public static readonly GUIStyle AssetButton = new GUIStyle(GUI.skin.button)
		{
			fixedHeight = 25,
			alignment = TextAnchor.MiddleLeft,
			imagePosition = ImagePosition.ImageLeft,
			clipping = TextClipping.Clip, // Clip text if too long
			wordWrap = false
		};
	}
}