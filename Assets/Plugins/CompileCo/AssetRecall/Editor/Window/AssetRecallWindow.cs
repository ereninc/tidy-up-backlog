// ─────────────────────────────────────────────────────────────
// Asset Recall v1.0.0 - by Compile&Co.
// Namespace: CompileCo.AssetRecall.Editor
// Description: Editor tool to track and revisit recently selected assets
// ─────────────────────────────────────────────────────────────

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;

namespace CompileCo.AssetRecall.Editor
{
	public enum FilterType { All, Prefabs, Scripts, ScriptableObject, HierarchyObject, Shaders, Scenes, Materials, Sprites, Textures, Animations, Audio, Folder }

	public class AssetRecallWindow : EditorWindow
	{
		private static List<AssetRecallRecentItem> _recentAssets = new List<AssetRecallRecentItem>();
		private FilterType _selectedFilter = FilterType.All;
		private int _maxAssets = 10; // Default maximum asset count.

		private Vector2 _scrollPosition = Vector2.zero;
		private bool _isScrollActive = false;

		private bool _showFavorites = true;
		private bool _showRecentItems = true;

		private double _nextValidationTime = 0f;

		[MenuItem("Tools/CompileCo/Asset Recall Window")]
		public static void ShowWindow()
		{
			var window = GetWindow<AssetRecallWindow>(AssetRecallText.EditorWindowTitle);
			var titleContent = new GUIContent(AssetRecallText.EditorWindowTitle, AssetRecallIcons.FolderOpened.image);

			window.titleContent = titleContent;
		}

		private void OnEnable()
		{
			Selection.selectionChanged += OnSelectionChanged;
			_recentAssets = AssetRecallUtility.LoadRecentAssets();

			AssetRecallUtility.ValidateRecentAssets(_recentAssets);
			AssetRecallUtility.SaveRecentAssets(_recentAssets);

			_maxAssets = EditorPrefs.GetInt(AssetRecallConstants.MaxAssetsKey, 10);

			EditorApplication.update += OnEditorUpdate;
		}

		private void OnDisable()
		{
			Selection.selectionChanged -= OnSelectionChanged;
			AssetRecallUtility.SaveRecentAssets(_recentAssets);

			EditorApplication.update -= OnEditorUpdate;
		}

		private void OnSelectionChanged()
		{
			var selectedObject = Selection.activeObject;
			if (selectedObject == null || _recentAssets.Exists(item => AssetRecallUtility.IsSameObject(item, selectedObject)))
				return;

			AssetRecallRecentItem newItem = new AssetRecallRecentItem();
			string assetPath = AssetDatabase.GetAssetPath(selectedObject);

			if (!string.IsNullOrEmpty(assetPath)) // Project asset
			{
				newItem.assetPath = assetPath;
			}
			else if (selectedObject is GameObject go && go.scene.IsValid()) // Scene object
			{
				newItem.scenePath = go.scene.path;
				newItem.objectPath = AssetRecallUtility.GetHierarchyPath(go);
			}

			_recentAssets.Insert(0, newItem);

			// Keep pinned items, remove others by maxAsset count
			AssetRecallUtility.TrimHistoryList(_recentAssets, _maxAssets);
			AssetRecallUtility.SaveRecentAssets(_recentAssets);
			Repaint();
		}

		private void OnGUI()
		{
			GUILayout.Space(12);
			
			// MaxHistoryItems slider
			GUILayout.BeginHorizontal();
			GUILayout.Label($"{AssetRecallText.MaxHistoryItems} {_maxAssets}", AssetRecallStyles.HeaderMaxHistoryLabel);
			
			GUILayout.Space(12);
			int newMaxAssets = EditorGUILayout.IntSlider(_maxAssets, AssetRecallConstants.MinAssets, AssetRecallConstants.MaxAssetsLimit);
			
			if (newMaxAssets != _maxAssets)
			{
				_maxAssets = newMaxAssets;
				EditorPrefs.SetInt(AssetRecallConstants.MaxAssetsKey, _maxAssets); // Save count
				AssetRecallUtility.TrimHistoryList(_recentAssets, _maxAssets); // Update list
			}
			GUILayout.EndHorizontal();
			
			DrawSectionLine();
			
			GUILayout.BeginHorizontal();

			// Filter dropdown and clear button layout
			GUILayout.BeginVertical();
			GUILayout.Space(6);
			GUILayout.BeginHorizontal();

			// Label (styled)
			GUILayout.Label(AssetRecallText.FilterByType, AssetRecallStyles.HeaderFilterType, GUILayout.Width(100));
			// Enum dropdown
			_selectedFilter = (FilterType)EditorGUILayout.EnumPopup(_selectedFilter);

			GUILayout.EndHorizontal();
			GUILayout.EndVertical();

			GUILayout.Space(5);

			// Clear button
			if (GUILayout.Button(AssetRecallIcons.Trash, GUILayout.Width(30), GUILayout.Height(25)))
			{
				if (EditorUtility.DisplayDialog(AssetRecallText.ClearConfirmTitle, AssetRecallText.ClearConfirmMessage, "Yes", "No"))
				{
					_recentAssets.RemoveAll(item => !item.isPinned);
					AssetRecallUtility.SaveRecentAssets(_recentAssets);
				}
			}

			GUILayout.EndHorizontal();
			
			DrawSectionLine();

			if (_recentAssets.Count == 0)
			{
				GUILayout.Label(AssetRecallText.NoAssetsMessage);
				return;
			}

			// Scroll view start
			_scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(position.height - 130));

			// If scroll view active
			float itemHeight = 30f;
			float totalItemHeight = _recentAssets.Count * itemHeight;
			_isScrollActive = totalItemHeight > (position.height - 160);

			// Pinned Items (Favorites)
			var pinnedItems = _recentAssets.Where(i => i.isPinned && AssetRecallUtility.IsItemMatchingFilter(i, _selectedFilter)).ToList();
			if (pinnedItems.Count > 0)
			{
				GUILayout.BeginHorizontal();

				GUILayout.Label(AssetRecallIcons.Favorite.image, AssetRecallStyles.HeaderIconFavouriteStyle);
				GUILayout.Label($"{AssetRecallText.FavoritesLabel} [{pinnedItems.Count}]", AssetRecallStyles.HeaderLabel);

				// Collapse/Expand button
				if (GUILayout.Button(AssetRecallIcons.GetFoldoutIcon(_showFavorites), GUILayout.Width(25), GUILayout.Height(25)))
				{
					_showFavorites = !_showFavorites; // Toggle
				}

				GUILayout.EndHorizontal();

				if (_showFavorites) // Draw if enabled
				{
					foreach (var item in pinnedItems)
					{
						DrawRecentItem(item, true);
					}
					GUILayout.Space(5);
				}
			}
			
			DrawSectionLine();
			
			// Recent Items
			var recentItems = _recentAssets.Where(i => !i.isPinned && AssetRecallUtility.IsItemMatchingFilter(i, _selectedFilter)).ToList();
			if (recentItems.Count > 0)
			{
				GUILayout.BeginHorizontal();

				GUILayout.Label(AssetRecallIcons.FolderOpened.image, AssetRecallStyles.HeaderIconRecentItemsStyle);
				GUILayout.Label($"{AssetRecallText.RecentItemsLabel} [{recentItems.Count}]", AssetRecallStyles.HeaderLabel);

				// Collapse/Expand button
				if (GUILayout.Button(AssetRecallIcons.GetFoldoutIcon(_showRecentItems), GUILayout.Width(25), GUILayout.Height(25)))
				{
					_showRecentItems = !_showRecentItems; // Toggle
				}

				GUILayout.EndHorizontal();

				if (_showRecentItems) // Draw if enabled
				{
					foreach (var item in recentItems)
					{
						DrawRecentItem(item, false);
					}
				}
			}

			//Scroll view end
			GUILayout.EndScrollView();
			
			// Footer logo (Compile & Co.)
			GUILayout.Space(8);
			DrawFooterLogo();
		}
		
		private static void DrawFooterLogo()
		{
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();

			Texture footerLogo = EditorGUIUtility.isProSkin ? AssetRecallIcons.CompileAndCoLogoDarkTheme : AssetRecallIcons.CompileAndCoLogoLightTheme;
			if (footerLogo)
			{
				Rect logoRect = GUILayoutUtility.GetRect(footerLogo.width, footerLogo.height, GUILayout.Width(80f), GUILayout.Height(13f));
				GUI.DrawTexture(logoRect, footerLogo, ScaleMode.ScaleToFit);
			}

			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();

			GUILayout.Space(6); // bottom margin
		}

		private void DrawRecentItem(AssetRecallRecentItem item, bool isPinned)
		{
			Object asset = AssetRecallUtility.GetObjectFromItem(item);
			if (asset == null) return;

			Texture icon = AssetRecallIcons.GetIconForObject(asset);
			if (icon == null) icon = AssetRecallIcons.GameObject.image;

			string tooltip = AssetDatabase.GetAssetPath(asset);
			GUIContent content = new GUIContent(asset.name, /*icon,*/ string.IsNullOrEmpty(tooltip) ? AssetRecallText.SceneObjectTooltip : tooltip);

			GUILayout.BeginHorizontal();

			#region [ PinButton Element ]

			float pinButtonWidth = 30;
			float removeButtonWidth = 25;
			float extraPadding = _isScrollActive ? 12f : 0f; // Add padding if scrollView active
			float availableWidth = position.width - pinButtonWidth - removeButtonWidth - 12 - extraPadding;
			availableWidth = isPinned ? availableWidth + pinButtonWidth - 2 : availableWidth;

			if (GUILayout.Button(AssetRecallIcons.GetLockIcon(item.isPinned), GUILayout.Width(pinButtonWidth), GUILayout.Height(25)))
			{
				item.isPinned = !item.isPinned;

				// To add first index
				if (item.isPinned)
				{
					_recentAssets.Remove(item);
					_recentAssets.Insert(0, item);
				}
				AssetRecallUtility.SaveRecentAssets(_recentAssets);
				Repaint();
			}

			#endregion

			#region [ RecentItem Element ]

			// Get button rect first
			Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent(asset.name), AssetRecallStyles.AssetButton, GUILayout.Height(25), GUILayout.Width(availableWidth));

			// Handle button click
			if (GUI.Button(buttonRect, GUIContent.none, AssetRecallStyles.AssetButton))
			{
				Selection.activeObject = asset;
				EditorGUIUtility.PingObject(asset);
			}

			// Draw icon (left side)
			if (icon)
			{
				Rect iconRect = new Rect(buttonRect.x + 4, buttonRect.y + 4, 18, 18);
				GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
			}

			// Draw asset name with tooltip (right to icon)
			Rect labelRect = new Rect(buttonRect.x + 26, buttonRect.y + 4, buttonRect.width - 30, 18);
			GUI.Label(labelRect, content);

			#endregion

			#region [ RemoveButton Element ]

			if (!isPinned)
			{
				// Remove button("X")
				if (GUILayout.Button(AssetRecallText.RemoveItemButtonText, GUILayout.Width(removeButtonWidth), GUILayout.Height(25)))
				{
					// Unnecessary but fail-safe
					if (!item.isPinned)
					{
						_recentAssets.Remove(item);
						AssetRecallUtility.SaveRecentAssets(_recentAssets);
						Repaint();
					}
				}
			}

			#endregion

			GUILayout.EndHorizontal();
		}

		private void DrawSectionLine()
		{
			GUILayout.Space(10);

			Rect lineRect = EditorGUILayout.GetControlRect(false, 2);
			EditorGUI.DrawRect(lineRect, new Color(1f, 1f, 1f, 0.2f)); // White with 20% opacity

			GUILayout.Space(10);
		}

		private void OnEditorUpdate()
		{
			if (EditorApplication.timeSinceStartup < _nextValidationTime) return;

			_nextValidationTime = EditorApplication.timeSinceStartup + AssetRecallConstants.ValidationInterval;

			bool cleaned = AssetRecallUtility.ValidateRecentAssets(_recentAssets);
			if (!cleaned) return;

			Debug.LogWarning(AssetRecallText.RemovedUnavailable);
			AssetRecallUtility.SaveRecentAssets(_recentAssets);
			Repaint();
		}

		[MenuItem("Assets/Pin to Asset Recall", false, 0)]
		private static void PinAssetFromProject(MenuCommand command)
		{
			Object obj = command.context ?? Selection.activeObject;
			if (obj == null)
			{
				Debug.LogWarning("Object is null. Cannot pin.");
				return;
			}

			var recentAssets = AssetRecallUtility.LoadRecentAssets();
			AssetRecallUtility.PinObjectToRecent(obj, recentAssets, EditorPrefs.GetInt(AssetRecallConstants.MaxAssetsKey, 10));

			Debug.Log($"Pinned from Project: {obj.name}");
			StaticRefresh();
		}

		[MenuItem("GameObject/Pin to Asset Recall", false, 0)]
		private static void PinFromHierarchy(MenuCommand command)
		{
			Object obj = command.context ?? Selection.activeObject;
			if (obj == null)
			{
				Debug.LogWarning("PinFromHierarchy: Selected object is null");
				return;
			}

			var recentAssets = AssetRecallUtility.LoadRecentAssets();
			AssetRecallUtility.PinObjectToRecent(obj, recentAssets, EditorPrefs.GetInt(AssetRecallConstants.MaxAssetsKey, 10));

			Debug.Log($"Pinned from Hierarchy: {obj.name}");
			StaticRefresh();
		}

		public static void StaticRefresh()
		{
			var window = GetWindow<AssetRecallWindow>();
			_recentAssets = AssetRecallUtility.LoadRecentAssets();
			window.Repaint();
		}
	}
}
#endif