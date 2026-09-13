#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EditorSceneGameViewSwitcher
{
	private sealed class SwitchChordContext : IShortcutContext
	{
		public bool active => _switchChordHeld;
	}

	private static readonly SwitchChordContext SwitchContext = new();

	private static bool _switchChordHeld;

	static EditorSceneGameViewSwitcher()
	{
		ShortcutManager.RegisterContext(SwitchContext);
	}

	[ClutchShortcut(
		"EXW/Scene Game View/Hold Switch",
		KeyCode.Tab)]
	private static void HandleSwitchChord(
		ShortcutArguments args)
	{
		_switchChordHeld =
			args.stage == ShortcutStage.Begin;
	}
	
	// Image #3 is roughly 1/3 Scene View and 2/3 Game View.
	// Use 0.5f if you want both views to have equal width.
	private const float SceneViewWidthRatio = 0.33f;
	private const float MinimumViewWidth = 100f;

	private const BindingFlags InstanceFlags =
		BindingFlags.Instance |
		BindingFlags.Public |
		BindingFlags.NonPublic;

	private const BindingFlags StaticFlags =
		BindingFlags.Static |
		BindingFlags.Public |
		BindingFlags.NonPublic;

	private static readonly Type GameViewType =
		FindEditorType("UnityEditor.GameView");

	private static readonly Type PlayModeViewType =
		FindEditorType("UnityEditor.PlayModeView");

	private static readonly Type DockAreaType =
		FindEditorType("UnityEditor.DockArea");

	private static readonly Type SplitViewType =
		FindEditorType("UnityEditor.SplitView");

	private static readonly Type ViewType =
		FindEditorType("UnityEditor.View");

	private static readonly FieldInfo EditorWindowParentField =
		typeof(EditorWindow).GetField("m_Parent", InstanceFlags);

	private static readonly FieldInfo DockAreaPanesField =
		DockAreaType?.GetField("m_Panes", InstanceFlags);

	private static readonly FieldInfo OriginalDragSourceField =
		DockAreaType?.GetField("s_OriginalDragSource", StaticFlags);

	private static readonly PropertyInfo ViewParentProperty =
		ViewType?.GetProperty("parent", InstanceFlags);

	private static readonly PropertyInfo ViewScreenPositionProperty =
		ViewType?.GetProperty("screenPosition", InstanceFlags);

	private static readonly PropertyInfo DockAreaSelectedProperty =
		DockAreaType?.GetProperty("selected", InstanceFlags);

	private static readonly MethodInfo DockAreaAddTabMethod =
		DockAreaType?.GetMethod(
			"AddTab",
			InstanceFlags,
			null,
			new[] { typeof(EditorWindow), typeof(bool) },
			null);

	private static readonly MethodInfo DockAreaRemoveTabMethod =
		DockAreaType?.GetMethod(
			"RemoveTab",
			InstanceFlags,
			null,
			new[] { typeof(EditorWindow), typeof(bool), typeof(bool) },
			null);

	private static readonly MethodInfo SplitViewDragOverMethod =
		SplitViewType?.GetMethod(
			"DragOver",
			InstanceFlags,
			null,
			new[] { typeof(EditorWindow), typeof(Vector2) },
			null);

	private static readonly MethodInfo SplitViewPerformDropMethod =
		FindPerformDropMethod();

	private static readonly MethodInfo GetMainPlayModeViewMethod =
		PlayModeViewType?.GetMethod(
			"GetMainPlayModeView",
			StaticFlags);

	private static Action _pendingAction;

	[Shortcut(
		"EXW/Scene Game View/Scene Only",
		typeof(SwitchChordContext),
		KeyCode.Alpha1)]
	[MenuItem("EW-ToolBar/Scene/View Switcher/Scene Only")]
	private static void QueueSceneOnly()
	{
		Schedule(SetSceneOnly);
	}

	[Shortcut(
		"EXW/Scene Game View/Game Only",
		typeof(SwitchChordContext),
		KeyCode.Alpha2)]
	[MenuItem("EW-ToolBar/Scene/View Switcher/Game Only")]
	private static void QueueGameOnly()
	{
		Schedule(SetGameOnly);
	}

	[Shortcut(
		"EXW/Scene Game View/Side By Side",
		typeof(SwitchChordContext),
		KeyCode.Alpha3)]
	[MenuItem("EW-ToolBar/Scene/View Switcher/Side By Side")]
	private static void QueueSideBySide()
	{
		Schedule(SetSideBySide);
	}

	private static void SetSceneOnly()
	{
		if (!TryGetViews(
			    out SceneView sceneView,
			    out EditorWindow gameView))
		{
			return;
		}

		MergeAndSelect(
			sceneView,
			gameView);
	}

	private static void SetGameOnly()
	{
		if (!TryGetViews(
			    out SceneView sceneView,
			    out EditorWindow gameView))
		{
			return;
		}

		MergeAndSelect(
			gameView,
			sceneView);
	}

	private static void SetSideBySide()
	{
		if (!TryGetViews(
			    out SceneView sceneView,
			    out EditorWindow gameView))
		{
			return;
		}

		object sceneDock = GetDockArea(sceneView);
		object gameDock = GetDockArea(gameView);

		if (sceneDock == null || gameDock == null)
		{
			LogError(
				"Scene View and Game View must both be docked inside the main Unity window.");
			return;
		}

		if (!ReferenceEquals(sceneDock, gameDock))
		{
			// They are already in separate dock areas.
			sceneView.Repaint();
			gameView.Repaint();
			return;
		}

		SplitGameViewToTheRight(
			sceneDock,
			gameView);
	}

	private static void MergeAndSelect(
		EditorWindow targetView,
		EditorWindow otherView)
	{
		object targetDock = GetDockArea(targetView);
		object otherDock = GetDockArea(otherView);

		if (targetDock == null || otherDock == null)
		{
			LogError(
				"Scene View and Game View must both be docked inside the main Unity window.");
			return;
		}

		if (!ReferenceEquals(targetDock, otherDock))
		{
			DockAreaRemoveTabMethod.Invoke(
				otherDock,
				new object[]
				{
					otherView,
					true,
					false
				});

			if (!IsAlive(targetDock))
			{
				LogError(
					"The target dock was destroyed while the layout was being rearranged.");
				return;
			}

			DockAreaAddTabMethod.Invoke(
				targetDock,
				new object[]
				{
					otherView,
					false
				});
		}

		SelectTab(
			targetDock,
			targetView);

		targetView.Focus();
		targetView.Repaint();
	}

	private static void SplitGameViewToTheRight(
		object sourceDock,
		EditorWindow gameView)
	{
		object parentSplitView =
			ViewParentProperty.GetValue(sourceDock);

		if (parentSplitView == null ||
		    !SplitViewType.IsInstanceOfType(parentSplitView))
		{
			LogError(
				"The Scene/Game dock is not inside a splittable Unity editor area.");
			return;
		}

		Rect dockScreenRect =
			(Rect)ViewScreenPositionProperty.GetValue(sourceDock);

		Vector2 rightEdgePoint = new(
			dockScreenRect.xMax - 8f,
			dockScreenRect.center.y);

		object dropInfo = SplitViewDragOverMethod.Invoke(
			parentSplitView,
			new object[]
			{
				gameView,
				rightEdgePoint
			});

		if (dropInfo == null)
		{
			LogError(
				"Unity could not create a right-side drop area for the Game View.");
			return;
		}

		float sceneWidth = Mathf.Clamp(
			dockScreenRect.width * SceneViewWidthRatio,
			MinimumViewWidth,
			dockScreenRect.width - MinimumViewWidth);

		Rect gameViewRect = new(
			dockScreenRect.x + sceneWidth,
			dockScreenRect.y,
			dockScreenRect.width - sceneWidth,
			dockScreenRect.height);

		SetDropInfoRect(
			dropInfo,
			gameViewRect);

		OriginalDragSourceField.SetValue(
			null,
			sourceDock);

		try
		{
			object result = SplitViewPerformDropMethod.Invoke(
				parentSplitView,
				new[]
				{
					(object)gameView,
					dropInfo,
					rightEdgePoint
				});

			if (result is bool success && !success)
			{
				LogError(
					"Unity rejected the Scene/Game split operation.");
			}
		}
		finally
		{
			OriginalDragSourceField.SetValue(
				null,
				null);
		}

		gameView.Repaint();
	}

	private static void SelectTab(
		object dockArea,
		EditorWindow window)
	{
		IList panes =
			DockAreaPanesField.GetValue(dockArea) as IList;

		if (panes == null)
		{
			LogError(
				"Unity's dock pane list could not be read.");
			return;
		}

		int targetIndex = panes.IndexOf(window);

		if (targetIndex < 0)
		{
			LogError(
				$"{window.titleContent.text} is not inside the expected dock area.");
			return;
		}

		DockAreaSelectedProperty.SetValue(
			dockArea,
			targetIndex);
	}

	private static void SetDropInfoRect(
		object dropInfo,
		Rect rect)
	{
		Type dropInfoType = dropInfo.GetType();

		FieldInfo rectField =
			dropInfoType.GetField(
				"rect",
				InstanceFlags);

		if (rectField != null)
		{
			rectField.SetValue(
				dropInfo,
				rect);
			return;
		}

		PropertyInfo rectProperty =
			dropInfoType.GetProperty(
				"rect",
				InstanceFlags);

		if (rectProperty?.CanWrite == true)
		{
			rectProperty.SetValue(
				dropInfo,
				rect);
			return;
		}

		throw new MissingMemberException(
			dropInfoType.FullName,
			"rect");
	}

	private static bool TryGetViews(
		out SceneView sceneView,
		out EditorWindow gameView)
	{
		sceneView = FindSceneView();
		gameView = FindGameView();

		if (sceneView && gameView)
		{
			return ValidateReflectionApi();
		}

		LogError(
			"Scene View or Game View could not be found.");
		return false;
	}

	private static SceneView FindSceneView()
	{
		if (EditorWindow.focusedWindow is SceneView focusedSceneView &&
		    focusedSceneView.docked)
		{
			return focusedSceneView;
		}

		SceneView lastActiveSceneView =
			SceneView.lastActiveSceneView;

		if (lastActiveSceneView &&
		    lastActiveSceneView.docked)
		{
			return lastActiveSceneView;
		}

		SceneView[] sceneViews =
			Resources.FindObjectsOfTypeAll<SceneView>();

		foreach (SceneView sceneView in sceneViews)
		{
			if (sceneView && sceneView.docked)
			{
				return sceneView;
			}
		}

		return EditorWindow.GetWindow<SceneView>();
	}

	private static EditorWindow FindGameView()
	{
		if (GameViewType == null)
		{
			return null;
		}

		if (EditorWindow.focusedWindow &&
		    GameViewType.IsInstanceOfType(
			    EditorWindow.focusedWindow))
		{
			return EditorWindow.focusedWindow;
		}

		EditorWindow mainPlayModeView =
			GetMainPlayModeViewMethod?.Invoke(
				null,
				null) as EditorWindow;

		if (mainPlayModeView &&
		    GameViewType.IsInstanceOfType(mainPlayModeView))
		{
			return mainPlayModeView;
		}

		Object[] gameViews =
			Resources.FindObjectsOfTypeAll(GameViewType);

		EditorWindow firstGameView = null;

		foreach (Object candidate in gameViews)
		{
			if (candidate is not EditorWindow editorWindow)
			{
				continue;
			}

			firstGameView ??= editorWindow;

			if (editorWindow.docked)
			{
				return editorWindow;
			}
		}

		return firstGameView ??
		       EditorWindow.GetWindow(GameViewType);
	}

	private static object GetDockArea(
		EditorWindow window)
	{
		object parent =
			EditorWindowParentField?.GetValue(window);

		return parent != null &&
		       DockAreaType?.IsInstanceOfType(parent) == true
			? parent
			: null;
	}

	private static void Schedule(
		Action action)
	{
		_pendingAction = action;

		EditorApplication.delayCall -=
			ExecutePendingAction;

		EditorApplication.delayCall +=
			ExecutePendingAction;
	}

	private static void ExecutePendingAction()
	{
		Action action = _pendingAction;
		_pendingAction = null;

		try
		{
			action?.Invoke();
		}
		catch (TargetInvocationException exception)
		{
			Debug.LogException(
				exception.InnerException ?? exception);
		}
		catch (Exception exception)
		{
			Debug.LogException(exception);
		}
	}

	private static bool ValidateReflectionApi()
	{
		bool isValid =
			GameViewType != null &&
			DockAreaType != null &&
			SplitViewType != null &&
			ViewType != null &&
			EditorWindowParentField != null &&
			DockAreaPanesField != null &&
			OriginalDragSourceField != null &&
			ViewParentProperty != null &&
			ViewScreenPositionProperty != null &&
			DockAreaSelectedProperty != null &&
			DockAreaAddTabMethod != null &&
			DockAreaRemoveTabMethod != null &&
			SplitViewDragOverMethod != null &&
			SplitViewPerformDropMethod != null;

		if (!isValid)
		{
			LogError(
				"The required Unity dock API could not be found. " +
				"Unity may have changed its internal editor layout API.");
		}

		return isValid;
	}

	private static MethodInfo FindPerformDropMethod()
	{
		if (SplitViewType == null)
		{
			return null;
		}

		MethodInfo[] methods =
			SplitViewType.GetMethods(InstanceFlags);

		foreach (MethodInfo method in methods)
		{
			if (method.Name != "PerformDrop")
			{
				continue;
			}

			ParameterInfo[] parameters =
				method.GetParameters();

			if (parameters.Length == 3 &&
			    parameters[0].ParameterType == typeof(EditorWindow) &&
			    parameters[2].ParameterType == typeof(Vector2))
			{
				return method;
			}
		}

		return null;
	}

	private static Type FindEditorType(
		string fullName)
	{
		Assembly[] assemblies =
			AppDomain.CurrentDomain.GetAssemblies();

		foreach (Assembly assembly in assemblies)
		{
			Type type = assembly.GetType(fullName);

			if (type != null)
			{
				return type;
			}
		}

		return null;
	}

	private static bool IsAlive(
		object value)
	{
		return value is Object unityObject &&
		       unityObject;
	}

	private static void LogError(
		string message)
	{
		Debug.LogError(
			$"[{nameof(EditorSceneGameViewSwitcher)}] {message}");
	}
}
#endif
