// Asset Recall v1.0.0 - by Compile&Co.
// Centralized icon content definitions for Asset Recall editor window UI

using System.IO;
using UnityEditor;
using UnityEngine;
namespace CompileCo.AssetRecall.Editor
{
	/// <summary>
	/// Contains all icons used in the Asset Recall tool for reuse and consistency.
	/// </summary>
	public static class AssetRecallIcons
	{
		// General UI
		public static readonly GUIContent Trash = EditorGUIUtility.IconContent("TreeEditor.Trash");
		public static readonly GUIContent FoldoutOpen = EditorGUIUtility.IconContent("IN foldout on");
		public static readonly GUIContent FoldoutClosed = EditorGUIUtility.IconContent("IN foldout");

		// Pin Icons
		public static readonly GUIContent LockOn = EditorGUIUtility.IconContent("IN LockButton on");
		public static readonly GUIContent LockOff = EditorGUIUtility.IconContent("IN LockButton");

		// Section Headers
		public static readonly GUIContent Favorite = EditorGUIUtility.IconContent("d_Favorite");
		public static readonly GUIContent FolderOpened = EditorGUIUtility.IconContent("d_FolderOpened Icon");

		// Asset Types
		public static readonly GUIContent Prefab = EditorGUIUtility.IconContent("Prefab Icon");
		public static readonly GUIContent Script = EditorGUIUtility.IconContent("cs Script Icon");
		public static readonly GUIContent Scene = EditorGUIUtility.IconContent("SceneAsset Icon");
		public static readonly GUIContent Material = EditorGUIUtility.IconContent("Material Icon");
		public static readonly GUIContent Texture = EditorGUIUtility.IconContent("Texture Icon");
		public static readonly GUIContent Audio = EditorGUIUtility.IconContent("AudioClip Icon");
		public static readonly GUIContent Folder = EditorGUIUtility.IconContent("Folder Icon");
		public static readonly GUIContent GameObject = EditorGUIUtility.IconContent("GameObject Icon");
		
		// Branding logo
		#region [ LOGO ]

		private static Texture2D _logoDark;
		private static Texture2D _logoLight;

		public static Texture2D CompileAndCoLogoDarkTheme
		{
			get
			{
				if (_logoDark == null)
					_logoDark = LoadLogoByName(LogoWhiteFileName);
				return _logoDark;
			}
		}

		public static Texture2D CompileAndCoLogoLightTheme
		{
			get
			{
				if (_logoLight == null)
					_logoLight = LoadLogoByName(LogoBlackFileName);
				return _logoLight;
			}
		}
		
		private const string LogoWhiteFileName = "compileandco-white";
		private const string LogoBlackFileName = "compileandco-black";

		private static Texture2D LoadLogoByName(string fileNameWithoutExtension)
		{
			string[] guids = AssetDatabase.FindAssets(fileNameWithoutExtension);

			foreach (string guid in guids)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (path.EndsWith(".png") && Path.GetFileNameWithoutExtension(path) == fileNameWithoutExtension)
				{
					return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
				}
			}

			Debug.LogWarning($"Logo asset not found: {fileNameWithoutExtension}");
			return null;
		}

		#endregion

		#region [ HELPERS ]
		
		/// <summary>
		/// Gets the appropriate icon for a given asset.
		/// Fallbacks to GameObject icon if not matched.
		/// </summary>
		public static Texture GetIconForObject(Object asset)
		{
			if (asset == null) return GameObject.image;

			string assetPath = AssetDatabase.GetAssetPath(asset);
			if (string.IsNullOrEmpty(assetPath)) return GameObject.image;

			if (AssetDatabase.IsValidFolder(assetPath))
				return Folder.image;
			
			// ✅ First try preview
			Texture preview = AssetPreview.GetAssetPreview(asset);
			if (preview)
				return preview;

			// 🔄 Fallback to mini thumbnail
			Texture thumbnail = AssetPreview.GetMiniThumbnail(asset);
			if (thumbnail)
				return thumbnail;

			switch (asset)
			{
				case GameObject _ when PrefabUtility.IsPartOfAnyPrefab(asset):
					return Prefab.image;
				case MonoScript _:
					return Script.image;
				case SceneAsset _:
					return Scene.image;
				case Material _:
					return Material.image;
				case Texture _:
					return Texture.image;
				case AudioClip _:
					return Audio.image;
				default:
					return AssetPreview.GetMiniThumbnail(asset) ?? GameObject.image;
			}
		}
		
		//Only for foldout icon when toggle show-hide button
		public static GUIContent GetFoldoutIcon(bool isOpen)
		{
			return isOpen ? FoldoutOpen : FoldoutClosed;
		}
		
		//Only for lock icon when item pinned
		public static GUIContent GetLockIcon(bool isOpen)
		{
			return isOpen ? LockOn : LockOff;
		}
		
		#endregion
	}
}