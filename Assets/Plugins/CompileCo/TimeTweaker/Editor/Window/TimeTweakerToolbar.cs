// TimeTweaker v1.0.0 - by Compile&Co.
// Entry point for injecting the time scale UI into Unity's toolbar using InitializeOnLoad.

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CompileCo.TimeTweaker.Editor
{
	[InitializeOnLoad]
	public static class TimeTweakerToolbar
	{
		private static VisualElement _currentToolbar;
		
		static TimeTweakerToolbar()
		{
			EditorApplication.delayCall += ReloadToolbar;
			
			EditorApplication.playModeStateChanged += state =>
			{
				if (state == PlayModeStateChange.EnteredEditMode)
				{
					var settings = AssetDatabase.FindAssets("t:TimeTweakerSettings")
						.Select(guid => AssetDatabase.LoadAssetAtPath<TimeTweakerSettings>(AssetDatabase.GUIDToAssetPath(guid)))
						.FirstOrDefault();

					if (settings && Mathf.Abs(Time.timeScale - settings.lastKnownTimeScale) > 0.01f)
					{
						Time.timeScale = settings.lastKnownTimeScale;
					}
				}
			};
		}

		public static void ReloadToolbar()
		{
			var container = TimeTweakerUtils.GetPlayModeContainer();
			if (container == null) return;

			var existing = container.Q("TimeScaleSliderContainer");
			if (existing != null)
				container.Remove(existing);

			_currentToolbar = TimeTweakerToolbarUI.Create();
			container.Add(_currentToolbar);
		}
	}
}