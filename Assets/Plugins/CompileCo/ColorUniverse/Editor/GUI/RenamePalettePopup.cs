#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	public class RenamePalettePopup : PopupWindowContent
	{
		private string _newName;
		private int _newHeight;
		private readonly string _originalName;
		private readonly int _originalHeight;
		private readonly Action<string, int> _onApply;
		private readonly Action _onDelete;

		private const int MinGridHeight = 1;
		private const int MaxGridHeight = 20;

		public RenamePalettePopup(string currentName, int currentHeight, Action<string, int> onApply, Action onDelete)
		{
			_originalName = currentName;
			_newName = currentName;
			_originalHeight = currentHeight;
			_newHeight = currentHeight;
			_onApply = onApply;
			_onDelete = onDelete;
		}

		public override Vector2 GetWindowSize() => new Vector2(260, 130);

		public override void OnGUI(Rect rect)
		{
			GUILayout.Label("Edit Palette", EditorStyles.boldLabel);
			_newName = EditorGUILayout.TextField("Name", _newName);
			_newHeight = EditorGUILayout.IntSlider("Grid Height", _newHeight, MinGridHeight, MaxGridHeight);

			GUILayout.Space(6);
			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Apply"))
			{
				if (!string.IsNullOrEmpty(_newName))
				{
					_onApply?.Invoke(_newName, _newHeight);
					editorWindow.Close();
				}
			}

			if (GUILayout.Button("Cancel"))
			{
				editorWindow.Close();
			}

			GUILayout.EndHorizontal();

			GUILayout.Space(4);

			GUIStyle deleteStyle = new GUIStyle(GUI.skin.button);
			deleteStyle.normal.textColor = Color.red;
			deleteStyle.fontStyle = FontStyle.Bold;

			if (GUILayout.Button("Delete Palette", deleteStyle))
			{
				if (EditorUtility.DisplayDialog("Delete Palette", $"Are you sure you want to delete '{_originalName}'?", "Yes", "No"))
				{
					_onDelete?.Invoke();
					editorWindow.Close();
				}
			}
		}
	}

}
#endif