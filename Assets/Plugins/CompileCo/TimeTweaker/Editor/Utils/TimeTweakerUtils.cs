// TimeTweaker v1.0.0 - by Compile&Co.
// Provides utility functions for locating and accessing Unity's internal toolbar containers via reflection.

using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace CompileCo.TimeTweaker.Editor
{
	public static class TimeTweakerUtils
	{
		public static VisualElement GetPlayModeContainer()
		{
			return GetToolbarRoot()?.Q<VisualElement>("ToolbarZonePlayMode");
		}
		
		private static VisualElement GetToolbarRoot()
		{
			var toolbarType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Toolbar");
			var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
			if (toolbars.Length == 0) return null;

			var rootField = toolbarType.GetField("m_Root", BindingFlags.Instance | BindingFlags.NonPublic);
			return rootField?.GetValue(toolbars[0]) as VisualElement;
		}
	}
}