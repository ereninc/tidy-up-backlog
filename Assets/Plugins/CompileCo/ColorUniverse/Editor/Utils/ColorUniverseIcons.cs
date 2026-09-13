using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR
namespace CompileCo.ColorUniverse.Editor
{
	public static class ColorUniverseIcons
	{
		public static readonly GUIContent Folder = EditorGUIUtility.IconContent("ColorPicker.CycleColor");
		public static readonly GUIContent Save = EditorGUIUtility.IconContent("d_SaveAs");
		public static readonly GUIContent Add = EditorGUIUtility.IconContent("CreateAddNew");
		public static readonly GUIContent Remove = EditorGUIUtility.IconContent("P4_DeletedLocal@2x");
	}
}
#endif