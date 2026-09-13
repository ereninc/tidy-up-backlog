#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	public class AddPalettePopup : PopupWindowContent
	{
		private string _newName = "New Palette";
		private int _newHeight = 5;
		private readonly Action<string, int> _onCreate;

		private const int MinGridHeight = 1;
		private const int MaxGridHeight = 20;

		public AddPalettePopup(Action<string, int> onCreate)
		{
			_onCreate = onCreate;
		}

		public override Vector2 GetWindowSize() => new Vector2(260, 100);

		public override void OnGUI(Rect rect)
		{
			GUILayout.Label("Add New Palette", EditorStyles.boldLabel);
			_newName = EditorGUILayout.TextField("Name", _newName);
			_newHeight = EditorGUILayout.IntSlider("Grid Height", _newHeight, MinGridHeight, MaxGridHeight);

			GUILayout.Space(5);
			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Add"))
			{
				if (!string.IsNullOrEmpty(_newName))
				{
					_onCreate?.Invoke(_newName, _newHeight);
					editorWindow.Close();
				}
			}

			if (GUILayout.Button("Cancel"))
			{
				editorWindow.Close();
			}

			GUILayout.EndHorizontal();
		}
	}
}
#endif