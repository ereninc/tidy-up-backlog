// TimeTweaker v1.0.0 - by Compile&Co.
// Centralized styling constants for visual elements used in the TimeTweaker toolbar UI.

using UnityEngine;

namespace CompileCo.TimeTweaker.Editor
{
	/// <summary>
	/// Contains shared UI styling values for the toolbar elements.
	/// </summary>
	public static class TimeTweakerToolbarStyle
	{
		public static readonly Color ResetButtonBackground = Color.gray;
		public static readonly Color ResetButtonTextColor = Color.white;
		public static readonly Color ResetButtonBorderColor = Color.white;

		public const int ContainerHeight = 22;
		public const int ResetButtonMinWidth = 50;
		public const int ResetButtonMinHeight = 22;
		public const float SliderWidth = 100;
		public const int MarginLeft = 10;
		public const int LabelMarginRight = 5;
		public const int LabelMarginLeft = 5;
	}
}