// TimeTweaker v1.0.0 - by Compile&Co.
// Scriptable configuration asset for customizing the timeScale slider range and default value.
// Automatically triggers toolbar UI refresh when values are modified in the inspector.

using UnityEditor;
using UnityEngine;

namespace CompileCo.TimeTweaker.Editor
{
	[CreateAssetMenu(fileName = "TimeTweakerSettings", menuName = "CompileCo/TimeTweaker/Settings", order = 0)]
	public class TimeTweakerSettings : ScriptableObject
	{
		[Header("Slider Range")]
		[Min(0)] public float minTimeScale = 0f;
		public float maxTimeScale = 2f;
		
		[Header("Slider Default")]
		public float defaultTimeScale = 1f;
		
		[HideInInspector] public float lastKnownTimeScale = 1f;
		
		private void OnValidate()
		{
			EditorApplication.delayCall += TimeTweakerToolbar.ReloadToolbar;
		}
	}
}