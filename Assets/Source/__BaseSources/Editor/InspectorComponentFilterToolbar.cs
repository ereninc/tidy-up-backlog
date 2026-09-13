#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class InspectorComponentFilterToolbar
{
	private const int Visible = 1;
	private const int Hidden = 0;

	private const float ToolbarTopSpace = 4f;
	private const float ToolbarBottomSpace = 4f;
	private const float ButtonHeight = 20f;
	private const float ButtonGap = 3f;
	private const float RowGap = 3f;
	private const float MinButtonWidth = 48f;
	private const float MaxButtonWidth = 210f;
	private const float HorizontalPadding = 8f;

	private static int _selectedGameObjectId;
	private static int _selectedComponentId;
	private static bool _showAll = true;

	static InspectorComponentFilterToolbar()
	{
		Editor.finishedDefaultHeaderGUI -= DrawHeaderToolbar;
		Editor.finishedDefaultHeaderGUI += DrawHeaderToolbar;

		Selection.selectionChanged -= OnSelectionChanged;
		Selection.selectionChanged += OnSelectionChanged;
	}

	private static void OnSelectionChanged()
	{
		GameObject selected = Selection.activeGameObject;
		int selectedId = selected ? selected.GetInstanceID() : 0;

		if (_selectedGameObjectId == selectedId) return;

		_selectedGameObjectId = selectedId;
		_selectedComponentId = 0;
		_showAll = true;

		DelayApplyFilter();
	}

	private static void DrawHeaderToolbar(Editor editor)
	{
		if (editor == null) return;
		if (editor.target is not GameObject gameObject) return;
		if (Selection.activeGameObject != gameObject) return;

		Component[] components = gameObject.GetComponents<Component>();
		if (components == null || components.Length == 0) return;

		if (_selectedGameObjectId != gameObject.GetInstanceID())
		{
			_selectedGameObjectId = gameObject.GetInstanceID();
			_selectedComponentId = 0;
			_showAll = true;
		}

		DrawComponentToolbar(gameObject, components);
	}

	private static void DrawComponentToolbar(GameObject gameObject, Component[] components)
	{
		List<ToolbarItem> items = BuildToolbarItems(components);
		if (items.Count == 0) return;

		float viewWidth = EditorGUIUtility.currentViewWidth;
		float contentWidth = Mathf.Max(100f, viewWidth - HorizontalPadding * 2f);

		List<List<ToolbarItem>> rows = BuildRows(items, contentWidth);

		GUILayout.Space(ToolbarTopSpace);

		Rect fullRect = GUILayoutUtility.GetRect(
			contentWidth,
			rows.Count * ButtonHeight + Mathf.Max(0, rows.Count - 1) * RowGap
		);

		fullRect.x += HorizontalPadding * 0.5f;
		fullRect.width -= HorizontalPadding;

		float y = fullRect.y;

		for (int r = 0; r < rows.Count; r++)
		{
			float x = fullRect.x;
			List<ToolbarItem> row = rows[r];

			for (int i = 0; i < row.Count; i++)
			{
				ToolbarItem item = row[i];

				Rect buttonRect = new Rect(x, y, item.Width, ButtonHeight);
				DrawButton(gameObject, item, buttonRect);

				x += item.Width + ButtonGap;
			}

			y += ButtonHeight + RowGap;
		}

		GUILayout.Space(ToolbarBottomSpace);
	}

	private static List<ToolbarItem> BuildToolbarItems(Component[] components)
	{
		List<ToolbarItem> items = new List<ToolbarItem>();

		items.Add(new ToolbarItem
		{
			Label = "All",
			Tooltip = "Show all components",
			Component = null,
			ComponentId = 0,
			Icon = EditorGUIUtility.IconContent("d_FilterByType").image,
			Width = 44f
		});

		for (int i = 0; i < components.Length; i++)
		{
			Component component = components[i];
			if (!component) continue;

			Type type = component.GetType();
			GUIContent content = EditorGUIUtility.ObjectContent(component, type);

			string label = GetNiceComponentName(type);
			float width = CalculateButtonWidth(label);

			items.Add(new ToolbarItem
			{
				Label = label,
				Tooltip = type.Name,
				Component = component,
				ComponentId = component.GetInstanceID(),
				Icon = content.image,
				Width = width
			});
		}

		return items;
	}

	private static List<List<ToolbarItem>> BuildRows(List<ToolbarItem> items, float contentWidth)
	{
		List<List<ToolbarItem>> rows = new List<List<ToolbarItem>>();
		List<ToolbarItem> currentRow = new List<ToolbarItem>();

		float currentWidth = 0f;

		for (int i = 0; i < items.Count; i++)
		{
			ToolbarItem item = items[i];
			float requiredWidth = currentRow.Count == 0 ? item.Width : item.Width + ButtonGap;

			if (currentRow.Count > 0 && currentWidth + requiredWidth > contentWidth)
			{
				rows.Add(currentRow);
				currentRow = new List<ToolbarItem>();
				currentWidth = 0f;
			}

			currentRow.Add(item);
			currentWidth += currentRow.Count == 1 ? item.Width : item.Width + ButtonGap;
		}

		if (currentRow.Count > 0)
		{
			rows.Add(currentRow);
		}

		return rows;
	}

	private static void DrawButton(GameObject gameObject, ToolbarItem item, Rect rect)
	{
		bool selected = item.Component == null
			? _showAll
			: !_showAll && _selectedComponentId == item.ComponentId;

		GUIContent content = new GUIContent(item.Label, item.Icon, item.Tooltip);

		GUIStyle style = new GUIStyle(EditorStyles.toolbarButton)
		{
			alignment = TextAnchor.MiddleLeft,
			imagePosition = ImagePosition.ImageLeft,
			fixedHeight = ButtonHeight,
			padding = new RectOffset(5, 7, 2, 2),
			margin = new RectOffset(0, 0, 0, 0)
		};

		Color oldColor = GUI.backgroundColor;

		if (selected)
		{
			GUI.backgroundColor = new Color(0.35f, 0.55f, 0.95f, 1f);
		}

		if (GUI.Button(rect, content, style))
		{
			_selectedGameObjectId = gameObject.GetInstanceID();

			if (item.Component == null)
			{
				_showAll = true;
				_selectedComponentId = 0;
			}
			else
			{
				_showAll = false;
				_selectedComponentId = item.ComponentId;
			}

			DelayApplyFilter();
		}

		GUI.backgroundColor = oldColor;
	}

	private static void DelayApplyFilter()
	{
		EditorApplication.delayCall -= ApplyFilter;
		EditorApplication.delayCall += ApplyFilter;
	}

	private static void ApplyFilter()
	{
		ActiveEditorTracker tracker = ActiveEditorTracker.sharedTracker;
		if (tracker == null) return;

		Editor[] editors = tracker.activeEditors;
		if (editors == null || editors.Length == 0) return;

		for (int i = 0; i < editors.Length; i++)
		{
			Editor editor = editors[i];
			if (editor == null || editor.target == null) continue;

			if (editor.target is GameObject)
			{
				tracker.SetVisible(i, Visible);
				continue;
			}

			if (_showAll)
			{
				tracker.SetVisible(i, Visible);
				continue;
			}

			if (editor.target is Component component)
			{
				bool shouldShow = component.GetInstanceID() == _selectedComponentId;
				tracker.SetVisible(i, shouldShow ? Visible : Hidden);
			}
		}

		RepaintInspectorWindows();
	}

	private static void RepaintInspectorWindows()
	{
		UnityEngine.Object[] inspectors = Resources.FindObjectsOfTypeAll(typeof(EditorWindow));

		for (int i = 0; i < inspectors.Length; i++)
		{
			if (inspectors[i] is not EditorWindow window) continue;
			if (window.GetType().Name != "InspectorWindow") continue;

			window.Repaint();
		}
	}

	private static string GetNiceComponentName(Type type)
	{
		string name = ObjectNames.NicifyVariableName(type.Name);

		name = name.Replace("Cinemachine ", "Cinemachine");
		name = name.Replace(" Controller", "Ctrl");
		name = name.Replace(" Component", "Comp");

		return name;
	}

	private static float CalculateButtonWidth(string label)
	{
		float textWidth = EditorStyles.toolbarButton.CalcSize(new GUIContent(label)).x;
		float width = textWidth + 24f;

		return Mathf.Clamp(width, MinButtonWidth, MaxButtonWidth);
	}

	private class ToolbarItem
	{
		public string Label;
		public string Tooltip;
		public Component Component;
		public int ComponentId;
		public Texture Icon;
		public float Width;
	}
}
#endif