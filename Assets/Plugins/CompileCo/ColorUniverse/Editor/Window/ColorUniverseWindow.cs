#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	public class ColorUniverseWindow : EditorWindow
	{
		private const int GridWidth = 5;
		private const int CellSize = 50;
		private const int MinGridHeight = 1;
		private const int MaxGridHeight = 20;

		private int _gridHeight = 5;
		private Color[,] _colorGrid;
		private Vector2 _scrollPosition;

		// Multi-palette support
		private string _currentPaletteName = "Default";
		private ColorUniversePaletteList _paletteList;
		private int _selectedPaletteIndex = 0;

		[MenuItem("Tools/CompileCo/Color Universe Window")]
		public static void OpenWindow()
		{
			var window = GetWindow<ColorUniverseWindow>("Color Universe");
			var titleContent = new GUIContent("Color Universe", ColorUniverseIcons.Folder.image);

			window.titleContent = titleContent;

			window.minSize = new Vector2(GridWidth * CellSize + 40, 500);
			window.maxSize = new Vector2(GridWidth * CellSize + 60, 1000);
			window.Show();
		}

		private void OnEnable()
		{
			_paletteList = ColorUniverseUtility.LoadAllPalettes();

			if (_paletteList.palettes.Count == 0)
			{
				CreateDefaultPalette();

				EditorApplication.delayCall += () =>
				{
					_paletteList = ColorUniverseUtility.LoadAllPalettes();
					LoadSelectedPaletteFromPrefs();
				};

				return;
			}

			LoadSelectedPaletteFromPrefs();
		}
		
		private void LoadSelectedPaletteFromPrefs()
		{
			string savedName = EditorPrefs.GetString("ColorUniverse_SelectedPaletteName", "");

			int foundIndex = _paletteList.palettes.FindIndex(p => p.paletteName == savedName);

			if (foundIndex == -1)
			{
				foundIndex = 0;
			}

			_selectedPaletteIndex = foundIndex;
			_currentPaletteName = _paletteList.palettes[foundIndex].paletteName;

			LoadColors(_currentPaletteName);
		}
		
		private void OnDisable()
		{
			if (!string.IsNullOrEmpty(_currentPaletteName) && _colorGrid != null)
			{
				SaveColors();
			}
		}

		private void CreateDefaultPalette()
		{
			_currentPaletteName = "New Palette 1";
			_gridHeight = 3;
			_colorGrid = new Color[GridWidth, _gridHeight];

			for (int x = 0; x < GridWidth; x++)
			{
				for (int y = 0; y < _gridHeight; y++)
				{
					_colorGrid[x, y] = Random.ColorHSV();
				}
			}

			var defaultPalette = new ColorUniverseGridData(_currentPaletteName, _colorGrid, _gridHeight);
			ColorUniverseUtility.SaveAllPalettes(new ColorUniversePaletteList
			{
				palettes = new List<ColorUniverseGridData>
				{
					defaultPalette
				}
			});
		}

		private void OnGUI()
		{
			GUILayout.Space(12);

			GUILayout.BeginHorizontal();

			// Palette Selection
			GUILayout.Label("Current Palette", EditorStyles.boldLabel, GUILayout.Width(120));

			GUI.enabled = (_colorGrid != null);

			string[] paletteNames = _paletteList.palettes.ConvertAll(p => p.paletteName).ToArray();

			bool hasPalettes = _paletteList.palettes.Count > 0;

			if (hasPalettes)
			{
				paletteNames = _paletteList.palettes.ConvertAll(p => p.paletteName).ToArray();
			}
			else
			{
				paletteNames = new[]
				{
					"[ Create New Palette ]"
				};
			}

			//fdsafdsafdassddsadsads
			int newIndex = EditorGUILayout.Popup(_selectedPaletteIndex, paletteNames);

			if (newIndex != _selectedPaletteIndex)
			{
				_selectedPaletteIndex = newIndex;
				_currentPaletteName = paletteNames[newIndex];
				EditorPrefs.SetString("ColorUniverse_SelectedPaletteName", _currentPaletteName);
				// var savedValue = EditorPrefs.GetString("ColorUniverse_SelectedPaletteName");
				// Debug.Log("Saved Value : " + savedValue);
				LoadColors(_currentPaletteName);
			}

			GUI.enabled = true;
			GUILayout.EndHorizontal();

			DrawSectionLine();

			if (_colorGrid == null || _paletteList.palettes.Count == 0)
			{
				GUILayout.Space(30);
				EditorGUILayout.HelpBox("No palette to show. Create one using the Add button.", MessageType.Info);
			}
			else
			{
				_scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(Mathf.Max(position.height - 200, 40)));

				// Scroll alan?n?n içine kendi çizim alan?m?z? tan?ml?yoruz
				float spacing = 1;
				float totalSpacing = (GridWidth - 1) * spacing;
				float cellSize = (position.width - 20 - totalSpacing) / GridWidth;
				float contentHeight = _gridHeight * (cellSize + spacing);

				Rect scrollContentRect = GUILayoutUtility.GetRect(
					GUIContent.none,
					GUIStyle.none,
					GUILayout.ExpandWidth(true),
					GUILayout.Height(contentHeight)
				);

				for (int y = 0; y < _gridHeight; y++)
				{
					for (int x = 0; x < GridWidth; x++)
					{
						DrawCellAbsolute(x, y, scrollContentRect.position);
					}
				}

				GUILayout.EndScrollView();
				GUILayout.Space(24);
			}

			GUILayout.Space(-58);

			GUILayout.FlexibleSpace(); // it pushes content upward

			DrawBottomButtonGroup();

			GUILayout.Space(4);
			GUILayout.Label("v0.1 - Color Universe", EditorStyles.centeredGreyMiniLabel);
		}

		private void DrawCellAbsolute(int x, int y, Vector2 offset)
		{
			int spacing = 2;
			float totalSpacing = (GridWidth - 1) * spacing;
			float cellSize = (position.width - 20 - totalSpacing) / GridWidth;

			Rect cellRect = new Rect(
				offset.x + x * (cellSize + spacing),
				offset.y + y * (cellSize + spacing),
				cellSize,
				cellSize
			);

			EditorGUI.DrawRect(cellRect, _colorGrid[x, y]);

			string hexColor = ColorUtility.ToHtmlStringRGB(_colorGrid[x, y]);

			Event evt = Event.current;
			if (cellRect.Contains(evt.mousePosition))
			{
				if (evt.type == EventType.MouseDown && evt.button == 0)
				{
					GUIUtility.systemCopyBuffer = $"#{hexColor}";
					Debug.Log($"Color #{hexColor} copied to clipboard!");
				}
				else if (evt.type == EventType.MouseDown && evt.button == 1)
				{
					OpenColorPicker(x, y);
					evt.Use();
				}
			}

			GUIStyle style = new GUIStyle(EditorStyles.label)
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = 12,
				normal =
				{
					textColor = ColorUniverseUtility.GetContrastingTextColor(_colorGrid[x, y])
				}
			};

			GUI.Label(cellRect, $"#{hexColor}", style);
		}

		private void DrawBottomButtonGroup()
		{
			GUI.enabled = (_colorGrid != null);

			if (GUILayout.Button("Save Colors", GUILayout.Height(30)))
			{
				SaveColors();
				Debug.Log("Colors saved successfully!");
			}

			GUILayout.Space(10);

			// Geni?li?i pencereye göre hesapla
			float fullWidth = position.width - 10f; // margin
			float halfWidth = (fullWidth - 4f) / 2f; // 4px spacing

			// --- Row 1 ---
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Clear Colors", GUILayout.Width(halfWidth), GUILayout.Height(30)))
			{
				ClearColors();
				Debug.Log("Colors cleared successfully!");
			}
			GUILayout.Space(4);
			if (GUILayout.Button("Random Colors", GUILayout.Width(halfWidth), GUILayout.Height(30)))
			{
				InitializeDefaultColors();
				Debug.Log("Random colors generated successfully!");
			}
			GUILayout.EndHorizontal();

			GUILayout.Space(6);

			// --- Row 2 ---
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Edit or Delete Palette", GUILayout.Width(halfWidth), GUILayout.Height(30)))
			{
				ShowEditPopup();
			}
			GUILayout.Space(4);
			GUI.enabled = true;
			if (GUILayout.Button("Add Palette", GUILayout.Width(halfWidth), GUILayout.Height(30)))
			{
				ShowAddPalettePopup();
			}
			GUILayout.EndHorizontal();
		}

		private void ShowAddPalettePopup()
		{
			PopupWindow.Show(new Rect(Event.current.mousePosition, Vector2.zero),
				new AddPalettePopup((newName, newHeight) =>
				{
					if (_paletteList.palettes.Exists(p => p.paletteName == newName))
					{
						Debug.LogWarning($"Palette with name '{newName}' already exists.");
						return;
					}

					_currentPaletteName = newName;
					_selectedPaletteIndex = _paletteList.palettes.Count;
					_gridHeight = newHeight;

					_colorGrid = new Color[GridWidth, _gridHeight];
					for (int x = 0; x < GridWidth; x++)
					{
						for (int y = 0; y < _gridHeight; y++)
						{
							_colorGrid[x, y] = Random.ColorHSV();
						}
					}
					
					EditorPrefs.SetString("ColorUniverse_SelectedPaletteName", _currentPaletteName);
					
					SaveColors();
					_paletteList = ColorUniverseUtility.LoadAllPalettes();
					Repaint();

					Debug.Log("New palette created successfully.");
				}));
		}

		private void ShowEditPopup()
		{
			var palette = _paletteList.palettes.Find(p => p.paletteName == _currentPaletteName);

			if (palette != null)
			{
				PopupWindow.Show(new Rect(Event.current.mousePosition, Vector2.zero),
					new RenamePalettePopup(_currentPaletteName, _gridHeight, (newName, newHeight) =>
						{
							palette.paletteName = newName;
							palette.gridHeight = newHeight;

							_currentPaletteName = newName;
							EditorPrefs.SetString("ColorUniverse_SelectedPaletteName", _currentPaletteName);
							_gridHeight = newHeight;

							_colorGrid = palette.ToColorArray(GridWidth, _gridHeight);

							_paletteList = ColorUniverseUtility.LoadAllPalettes();
							ColorUniverseUtility.SaveAllPalettes(_paletteList);

							Repaint();
							Debug.Log("Palette updated successfully.");
						},
						() =>
						{
							_paletteList.palettes.RemoveAll(p => p.paletteName == _currentPaletteName);
							ColorUniverseUtility.SaveAllPalettes(_paletteList);

							if (_paletteList.palettes.Count > 0)
							{
								_selectedPaletteIndex = 0;
								_currentPaletteName = _paletteList.palettes[0].paletteName;
								LoadColors(_currentPaletteName);
							}
							else
							{
								_colorGrid = null;
								_gridHeight = MinGridHeight;
								_currentPaletteName = "";
								_selectedPaletteIndex = 0;
								Repaint();
							}
							
							Debug.Log("Palette deleted successfully.");
						}));
			}
		}

		#region [ SAVE & LOAD & CLEAR ]

		private void SaveColors()
		{
			if (string.IsNullOrEmpty(_currentPaletteName) || _colorGrid == null)
			{
				Debug.LogWarning("Cannot save: palette name or color grid is invalid.");
				return;
			}

			var data = new ColorUniverseGridData(_currentPaletteName, _colorGrid, _gridHeight);
			ColorUniverseUtility.SavePalette(data);
			
			_paletteList = ColorUniverseUtility.LoadAllPalettes();

			_selectedPaletteIndex = _paletteList.palettes.FindIndex(p => p.paletteName == _currentPaletteName);
			EditorPrefs.SetString("ColorUniverse_SelectedPaletteName", _currentPaletteName);
		}

		private void LoadColors(string paletteName)
		{
			var data = ColorUniverseUtility.GetPaletteByName(paletteName);
			if (data != null)
			{
				_gridHeight = Mathf.Clamp(data.gridHeight, MinGridHeight, MaxGridHeight);
				_colorGrid = data.ToColorArray(GridWidth, _gridHeight);
			}
			else
			{
				InitializeDefaultColors();
			}
		}

		private void ClearColors()
		{
			_colorGrid = new Color[GridWidth, _gridHeight];
			for (int x = 0; x < GridWidth; x++)
			{
				for (int y = 0; y < _gridHeight; y++)
				{
					_colorGrid[x, y] = Color.black;
				}
			}
		}

		private void InitializeDefaultColors()
		{
			_colorGrid = new Color[GridWidth, _gridHeight];
			for (int x = 0; x < GridWidth; x++)
			{
				for (int y = 0; y < _gridHeight; y++)
				{
					_colorGrid[x, y] = Random.ColorHSV();
				}
			}
		}

		#endregion

		private void OpenColorPicker(int x, int y)
		{
			Color currentColor = _colorGrid[x, y];

			PopupWindow.Show(new Rect(Event.current.mousePosition, Vector2.zero), new ColorUniversePickerPopup(currentColor, (newColor) =>
			{
				_colorGrid[x, y] = newColor;
				Repaint();
				SaveColors();
			}));
		}

		private void DrawSectionLine()
		{
			GUILayout.Space(10);

			Rect lineRect = EditorGUILayout.GetControlRect(false, 2);
			EditorGUI.DrawRect(lineRect, new Color(1f, 1f, 1f, 0.2f)); // White with 20% opacity

			GUILayout.Space(10);
		}
	}
}
#endif