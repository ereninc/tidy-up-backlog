#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class SceneEditor
{
	private const string Prefix = "Assets/Scene/";

	private const string StartingScene = "01_LoadingScene";
	private const string MainScene = "02_MainMenuScene";
	private const string StudioScene = "StudioScene";

	private const string PlayModeToggleKey = "StartFromFirstScene";

	static SceneEditor() { EditorApplication.playModeStateChanged += OnPlayModeStateChanged; }

	[MenuItem("EW-ToolBar/Scene/Start From First Scene")]
	private static void ToggleStartFromFirstScene()
	{
		bool currentState = EditorPrefs.GetBool(PlayModeToggleKey, true);
		EditorPrefs.SetBool(PlayModeToggleKey, !currentState);

		Menu.SetChecked("EW-ToolBar/Scene/Start From First Scene", !currentState);
	}

	[MenuItem("EW-ToolBar/Scene/Start From First Scene", true)]
	private static bool ToggleStartFromFirstSceneValidate()
	{
		Menu.SetChecked("EW-ToolBar/Scene/Start From First Scene", EditorPrefs.GetBool(PlayModeToggleKey, true));
		return true;
	}
	
	private static void OnPlayModeStateChanged(PlayModeStateChange state)
	{
		if (!EditorPrefs.GetBool(PlayModeToggleKey, true)) return;
		switch (state)
		{
		case PlayModeStateChange.ExitingEditMode:
			if (SceneManager.GetActiveScene().name != StartingScene && SceneManager.GetActiveScene().name != StudioScene)
			{
				EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
				EditorSceneManager.OpenScene(Prefix + StartingScene + ".unity");
			}
		break;

		case PlayModeStateChange.EnteredEditMode:
			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
			EditorSceneManager.OpenScene(Prefix + MainScene + ".unity");
		break;
		}
	}
}

#endif
