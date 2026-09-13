#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	public class ColorUniversePickerPopup : PopupWindowContent
	{
		private Color color;
		private readonly System.Action<Color> onColorChanged;

		public ColorUniversePickerPopup(Color initialColor, System.Action<Color> onColorChanged)
		{
			this.color = initialColor;
			this.onColorChanged = onColorChanged;
		}

		public override Vector2 GetWindowSize()
		{
			return new Vector2(200, 100);
		}

		public override void OnGUI(Rect rect)
		{
			GUILayout.Label("Pick a Color", EditorStyles.boldLabel);
			color = EditorGUILayout.ColorField(color);

			if (GUILayout.Button("Apply"))
			{
				onColorChanged?.Invoke(color);
				editorWindow.Close();
			}

			GUILayout.Space(10);


			if (GUILayout.Button("Reset"))
			{
				onColorChanged?.Invoke(Color.black);
				editorWindow.Close();
			}
		}
	}

}
#endif