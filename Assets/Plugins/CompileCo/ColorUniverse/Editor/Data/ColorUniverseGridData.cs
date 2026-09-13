using System;
using Unity.VisualScripting;
using UnityEngine;
using ColorUtility = UnityEngine.ColorUtility;

namespace CompileCo.ColorUniverse.Editor
{
	[System.Serializable]
	public class ColorUniverseGridData
	{
		public string paletteName;
		public string[] colors;
		public int gridHeight;

		public ColorUniverseGridData(string name, Color[,] grid, int height)
		{
			paletteName = name;
			gridHeight = height;

			if (grid == null)
			{
				colors = Array.Empty<string>();
				return;
			}
			
			colors = new string[grid.Length];
			int index = 0;
			for (int y = 0; y < grid.GetLength(1); y++)
			{
				for (int x = 0; x < grid.GetLength(0); x++)
				{
					colors[index++] = ColorUtility.ToHtmlStringRGBA(grid[x, y]);
				}
			}
		}

		public Color[,] ToColorArray(int width, int height)
		{
			Color[,] grid = new Color[width, height];
			int index = 0;
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					if (index < colors.Length && ColorUtility.TryParseHtmlString($"#{colors[index++]}", out Color color))
					{
						grid[x, y] = color;
					}
				}
			}
			return grid;
		}
	}

}