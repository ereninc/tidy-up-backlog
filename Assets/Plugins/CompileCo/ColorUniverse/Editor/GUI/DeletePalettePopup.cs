#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	public class DeletePalettePopup : PopupWindowContent
	{
		private readonly string _paletteName;
		private readonly Action _onConfirm;

		public DeletePalettePopup(string paletteName, Action onConfirm)
		{
			_paletteName = paletteName;
			_onConfirm = onConfirm;
		}

		public override Vector2 GetWindowSize() => new Vector2(250, 70);

		public override void OnGUI(Rect rect)
		{
			EditorGUILayout.LabelField($"Delete '{_paletteName}'?", EditorStyles.boldLabel);
			GUILayout.Space(5);

			GUILayout.BeginHorizontal();

			if (GUILayout.Button("Confirm"))
			{
				_onConfirm?.Invoke();
				editorWindow.Close();
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