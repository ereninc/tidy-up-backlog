using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	/// <summary>
	/// Utility functions for saving and loading Color Universe grid data.
	/// </summary>
	public static class ColorUniverseUtility
	{
		private static ColorUniversePaletteList _cachedPaletteList;

		/// Loads all palettes (from file or creates empty).
		public static ColorUniversePaletteList LoadAllPalettes()
		{
			if (_cachedPaletteList != null)
				return _cachedPaletteList;

			string path = ColorUniversePaths.JsonSavePath;
			if (!File.Exists(path))
			{
				_cachedPaletteList = new ColorUniversePaletteList();
				return _cachedPaletteList;
			}

			string json = File.ReadAllText(path);
			_cachedPaletteList = JsonUtility.FromJson<ColorUniversePaletteList>(json);
			return _cachedPaletteList ?? new ColorUniversePaletteList();
		}

		public static void SaveAllPalettes(ColorUniversePaletteList paletteList)
		{
			string json = JsonUtility.ToJson(paletteList, true);
			string path = ColorUniversePaths.JsonSavePath;

			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
			File.WriteAllText(path, json);
			
			_cachedPaletteList = null;
		}

		public static ColorUniverseGridData GetPaletteByName(string name)
		{
			var list = LoadAllPalettes();
			return list.palettes.Find(p => p.paletteName == name);
		}

		public static void SavePalette(ColorUniverseGridData newPalette)
		{
			var list = LoadAllPalettes();
			var existing = list.palettes.Find(p => p.paletteName == newPalette.paletteName);

			if (existing != null)
				list.palettes.Remove(existing); // Replace
			list.palettes.Add(newPalette);

			SaveAllPalettes(list);
		}
		
		public static List<Color> ParseColorList(string[] hexArray)
		{
			var result = new List<Color>();

			foreach (string hex in hexArray)
			{
				if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
					result.Add(color);
				else
					result.Add(Color.black); // fallback olarak siyah
			}

			return result;
		}

		//OLD ONES HERE!!!!!!
		
		//VVVVVVVVVVVVVVVVVVV
		
		/// <summary>
		/// Saves the current grid data (colors and grid height) into JSON.
		/// </summary>
		public static void SaveGridData(ColorUniverseGridData data)
		{
			string json = JsonUtility.ToJson(data, true);
			string path = ColorUniversePaths.RelativeJsonPath;

			// Ensure the directory exists
			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

			File.WriteAllText(path, json);
		}

		/// <summary>
		/// Loads the grid data (colors and grid height) from JSON.
		/// </summary>
		public static ColorUniverseGridData LoadGridData()
		{
			string path = ColorUniversePaths.RelativeJsonPath;

			if (!File.Exists(path))
				return null;

			string json = File.ReadAllText(path);
			return JsonUtility.FromJson<ColorUniverseGridData>(json);
		}
		
		/// <summary>
		/// Deletes the saved grid data file, if it exists.
		/// </summary>
		public static void ClearGridData()
		{
			string path = ColorUniversePaths.JsonSavePath;

			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		
		//GET CONTRAST TEXT COLOR
		public static Color GetContrastingTextColor(Color backgroundColor)
		{
			float luminance = 0.299f * backgroundColor.r + 0.587f * backgroundColor.g + 0.114f * backgroundColor.b;
			return luminance > 0.5f ? Color.black : Color.white;
		}
	}
}
