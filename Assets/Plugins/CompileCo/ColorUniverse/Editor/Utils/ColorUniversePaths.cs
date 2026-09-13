using System.IO;
using UnityEditor;
using UnityEngine;

namespace CompileCo.ColorUniverse.Editor
{
	/// <summary>
	/// Provides dynamic path resolution for Color Universe data,
	/// ensuring that save/load locations adapt to project structure.
	/// </summary>
	public static class ColorUniversePaths
	{
		// Default fallback path if script location cannot be resolved
		private const string FallbackPath = "Assets/CompileCo/ColorUniverse";

		/// <summary>
		/// Resolves the root folder of Color Universe by locating the script path
		/// and traversing up to find the "Editor" folder, then one level above it.
		/// </summary>
		public static string RootFolder
		{
			get
			{
				string[] guids = AssetDatabase.FindAssets("t:Script ColorUniverseWindow");

				if (guids.Length > 0)
				{
					string scriptPath = AssetDatabase.GUIDToAssetPath(guids[0]);
					if (!string.IsNullOrEmpty(scriptPath))
					{
						string directory = Path.GetDirectoryName(scriptPath);

						// Traverse upward until we hit an "Editor" folder
						while (!string.IsNullOrEmpty(directory) && !directory.EndsWith("/Editor") && !directory.EndsWith("\\Editor"))
						{
							directory = Path.GetDirectoryName(directory);
						}

						// Go one level above the Editor folder
						string parentDir = Path.GetDirectoryName(directory);
						return Path.GetFullPath(parentDir).Replace("\\", "/");
					}
				}

				// Fallback: Assets/CompileCo/ColorUniverse
				return Path.GetFullPath(FallbackPath).Replace("\\", "/");
			}
		}

		/// <summary>
		/// Full path to the data folder (ex: /Plugins/ColorUniverse/Resources)
		/// </summary>
		public static string DataFolder => Path.Combine(RootFolder, "Resources").Replace("\\", "/");

		/// <summary>
		/// Full system path to JSON save file
		/// </summary>
		public static string JsonSavePath => Path.Combine(DataFolder, "ColorUniverse_Palettes.json").Replace("\\", "/");

		/// <summary>
		/// Relative Unity path to use with AssetDatabase API
		/// </summary>
		public static string RelativeJsonPath => "Assets" + JsonSavePath.Replace(Application.dataPath.Replace("\\", "/"), "");
	}
}