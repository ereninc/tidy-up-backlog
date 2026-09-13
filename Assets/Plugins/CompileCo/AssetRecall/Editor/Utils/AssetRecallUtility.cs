// Asset Recall v1.0.0 - by Compile&Co.
// A lightweight Unity Editor extension to track and revisit recently selected assets.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Contains utility functions for object path resolution, filtering, and serialization for Asset Recall.
	/// </summary>
	public static class AssetRecallUtility
	{
		/// <summary>
		/// Gets the full hierarchy path of a GameObject in the scene.
		/// </summary>
		public static string GetHierarchyPath(GameObject obj)
		{
			string path = obj.name;
			while (obj.transform.parent != null)
			{
				obj = obj.transform.parent.gameObject;
				path = obj.name + "/" + path;
			}
			return path;
		}

		/// <summary>
		/// Attempts to find a GameObject in a scene by its hierarchy path.
		/// </summary>
		private static GameObject FindGameObjectByPath(GameObject root, string path)
		{
			string[] parts = path.Split('/');
			Transform current = root.transform;

			if (current.name != parts[0])
				return null;

			for (int i = 1; i < parts.Length; i++)
			{
				current = current.Find(parts[i]);
				if (current == null)
				{
					//Debug.LogWarning($"Hierarchy path not found: {path}");
					return null;
				}
			}

			return current.gameObject;
		}
		
		public static Object GetObjectFromItem(AssetRecallRecentItem item)
		{
			if (!string.IsNullOrEmpty(item.assetPath)) // If it's a project asset -> not hierarchy object
			{
				return AssetDatabase.LoadAssetAtPath<Object>(item.assetPath);
			}
			else if (!string.IsNullOrEmpty(item.scenePath) && !string.IsNullOrEmpty(item.objectPath)) // If it's a scene object -> so its on hierarchy
			{
				Scene scene = EditorSceneManager.GetSceneByPath(item.scenePath);

				if (!scene.isLoaded)
				{
					if (SceneManager.GetActiveScene().path != item.scenePath)
					{
						//Debug.LogWarning($"Object '{item.ObjectPath}' belongs to scene '{item.ScenePath}', but the current active scene is '{SceneManager.GetActiveScene().name}'. The object cannot be added unless its scene is loaded.");
					}
					else
					{
						//Debug.LogWarning($"Object '{item.ObjectPath}' cannot be added because its scene '{item.ScenePath}' is not loaded.");
					}
					return null;
				}

				foreach (GameObject root in scene.GetRootGameObjects())
				{
					var obj = FindGameObjectByPath(root, item.objectPath);
					if (obj != null)
					{
						//Debug.Log($"Scene object found: {item.ObjectPath}");
						return obj;
					}
				}

				//Debug.LogWarning($"Scene object not found: {item.ObjectPath}");
			}

			return null;
		}
		
		/// <summary>
		/// Removes missing or deleted assets/objects from the recent list.
		/// Returns true if any item was removed.
		/// </summary>
		public static bool ValidateRecentAssets(List<AssetRecallRecentItem> recentAssets)
		{
			bool modified = false;

			for (int i = recentAssets.Count - 1; i >= 0; i--)
			{
				Object obj = GetObjectFromItem(recentAssets[i]);
				if (obj == null)
				{
					recentAssets.RemoveAt(i);
					modified = true;
				}
			}

			return modified;
		}

		#region [ ADD ]

		public static void PinObjectToRecent(Object obj, List<AssetRecallRecentItem> recentAssets, int maxCount)
		{
			if (obj == null)
			{
				Debug.LogWarning("OBJ NULL — obj is actually null reference");
				return;
			}
			var existingItem = recentAssets.FirstOrDefault(item => IsSameObject(item, obj));

			if (existingItem != null)
			{
				existingItem.isPinned = true;
				recentAssets.Remove(existingItem);
				recentAssets.Insert(0, existingItem);
			}
			else
			{
				var newItem = new AssetRecallRecentItem { isPinned = true };

				string assetPath = AssetDatabase.GetAssetPath(obj);

				if (!string.IsNullOrEmpty(assetPath)) // Project asset
				{
					newItem.assetPath = assetPath;
				}
				else if (obj is GameObject go && go.scene.IsValid()) // Scene object
				{
					newItem.scenePath = go.scene.path;
					newItem.objectPath = GetHierarchyPath(go);
				}
				else
				{
					Debug.LogWarning("Could not determine object type to pin.");
					return;
				}

				recentAssets.Insert(0, newItem);
			}

			TrimHistoryList(recentAssets, maxCount);
			SaveRecentAssets(recentAssets);
		}
		
		#endregion

		#region [ SAVE & LOAD ]

		/// <summary>
		/// Save recent assets to json file.
		/// </summary>
		public static void SaveRecentAssets(List<AssetRecallRecentItem> recentAssets)
		{
			string json = JsonUtility.ToJson(new AssetRecallPathList
			{
				items = recentAssets
			}, true);

			string path = AssetRecallPaths.RelativeJsonPath;

			// Ensure the directory exists
			Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

			File.WriteAllText(path, json);
		}

		/// <summary>
		/// Loads recent assets from json file.
		/// </summary>
		public static List<AssetRecallRecentItem> LoadRecentAssets()
		{
			string path = AssetRecallPaths.RelativeJsonPath;

			if (!File.Exists(path))
				return new List<AssetRecallRecentItem>();

			string json = File.ReadAllText(path);
			AssetRecallPathList assetPathList = JsonUtility.FromJson<AssetRecallPathList>(json);

			return assetPathList?.items ?? new List<AssetRecallRecentItem>();
		}

		#endregion
		
		#region [ OBJECT COMPARISONS ]

		public static bool IsSameObject(AssetRecallRecentItem item, Object obj)
		{
			if (item == null || obj == null) return false;

			string assetPath = AssetDatabase.GetAssetPath(obj);

			if (!string.IsNullOrEmpty(item.assetPath) && item.assetPath == assetPath)
			{
				// Check for project asset
				return true;
			}
			else if (!string.IsNullOrEmpty(item.scenePath) && obj is GameObject go)
			{
				// Check for scene object
				if (go.scene.path == item.scenePath)
				{
					string objPath = AssetRecallUtility.GetHierarchyPath(go);
					return objPath == item.objectPath;
				}
			}

			return false;
		}

		#endregion

		#region [ FILTERING ]

		public static bool IsItemMatchingFilter(AssetRecallRecentItem item, FilterType selectedFilter)
		{
			if (selectedFilter == FilterType.All)
				return true;

			if (!string.IsNullOrEmpty(item.assetPath))
			{
				if (selectedFilter == FilterType.Prefabs && item.assetPath.EndsWith(".prefab")) return true;
				if (selectedFilter == FilterType.Scripts && item.assetPath.EndsWith(".cs")) return true;
				if (selectedFilter == FilterType.Scenes && item.assetPath.EndsWith(".unity")) return true;
				if (selectedFilter == FilterType.Materials && item.assetPath.EndsWith(".mat")) return true;
				
				if (selectedFilter == FilterType.Textures)
				{
					var importer = AssetImporter.GetAtPath(item.assetPath) as TextureImporter;
					return importer && importer.textureType == TextureImporterType.Default;
				}
				if (selectedFilter == FilterType.Sprites)
				{
					var importer = AssetImporter.GetAtPath(item.assetPath) as TextureImporter;
					return importer && importer.textureType == TextureImporterType.Sprite;
				}
				if (selectedFilter == FilterType.Audio && item.assetPath.EndsWith(".wav") || item.assetPath.EndsWith(".mp3")) return true;
				if (selectedFilter == FilterType.Folder && AssetDatabase.IsValidFolder(item.assetPath)) return true;
				if (selectedFilter == FilterType.Shaders) return item.assetPath.EndsWith(".shader") || item.assetPath.EndsWith(".shadergraph");
				if (selectedFilter == FilterType.Animations) return item.assetPath.EndsWith(".anim") || item.assetPath.EndsWith(".controller") || item.assetPath.EndsWith(".mask") || item.assetPath.EndsWith(".overrideController") || AssetDatabase.LoadAssetAtPath<Object>(item.assetPath) is Avatar;
				if (selectedFilter == FilterType.ScriptableObject)
				{
					Object obj = AssetDatabase.LoadAssetAtPath<Object>(item.assetPath);
					if (obj is ScriptableObject) return true;
				}
			}
			else if (!string.IsNullOrEmpty(item.scenePath) && !string.IsNullOrEmpty(item.objectPath))
			{
				if (selectedFilter == FilterType.HierarchyObject)
					return true;
			}

			return false;
		}
		
		public static void TrimHistoryList(List<AssetRecallRecentItem> recentAssets, int currentMaxAssets)
		{
			// Remain pinned items
			while (recentAssets.Count > currentMaxAssets)
			{
				var lastUnpinned = recentAssets.FindLast(item => !item.isPinned);
				if (lastUnpinned != null)
					recentAssets.Remove(lastUnpinned);
				else
					break; // If all pinned, break out
			}
		}

		#endregion
	}
}