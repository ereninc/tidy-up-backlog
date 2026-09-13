// Asset Recall v1.0.0 - by Compile&Co.
// Dynamically resolves paths for saving and loading persistent data

using System.IO;
using UnityEditor;
using UnityEngine;

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Provides dynamic path resolution for Asset Recall data,
	/// ensuring that save/load locations adapt to project structure.
	/// </summary>
	public static class AssetRecallPaths
	{
		// Default fallback path if script location cannot be resolved
		private const string FallbackPath = "Assets/CompileCo/AssetRecall";

		/// <summary>
		/// Resolves the root folder of Asset Recall by locating the script path
		/// and traversing up to find the "Editor" folder, then one level above it.
		/// </summary>
		public static string RootFolder
		{
			get
			{
				string[] guids = AssetDatabase.FindAssets("t:Script AssetRecallWindow");

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

				// Fallback: Assets/AssetRecall
				return Path.GetFullPath(FallbackPath).Replace("\\", "/");
			}
		}

		/// <summary>
		/// Full path to the data folder (ex: /Plugins/AssetRecall/Resources)
		/// </summary>
		public static string DataFolder => Path.Combine(RootFolder, "Resources").Replace("\\", "/");

		/// <summary>
		/// Full system path to JSON save file
		/// </summary>
		public static string JsonSavePath => Path.Combine(DataFolder, "AssetRecall_History.json").Replace("\\", "/");

		/// <summary>
		/// Relative Unity path to use with AssetDatabase API
		/// </summary>
		public static string RelativeJsonPath => "Assets" + JsonSavePath.Replace(Application.dataPath.Replace("\\", "/"), "");
	}
}
