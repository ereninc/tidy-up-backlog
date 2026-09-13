#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SceneTool : MonoBehaviour
{
    private const string Prefix = "Assets/Scene/";

    private const string StartingScene = "01_LoadingScene";
    private const string MainScene = "02_MainMenuScene";
    private const string GameScene = "03_GameplayScene";

    [MenuItem("EW-ToolBar/Scene/Scene List/01_LoadingScene %1")]
    static void ToLoadingScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Prefix + StartingScene + ".unity", OpenSceneMode.Single);
    }
    
    [MenuItem("EW-ToolBar/Scene/Scene List/02_MainMenuScene %2")]
    static void ToMainScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Prefix + MainScene + ".unity", OpenSceneMode.Single);
    }
    
    [MenuItem("EW-ToolBar/Scene/Scene List/03_GameplayScene %3")]
    static void ToGameScene()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Prefix + GameScene + ".unity", OpenSceneMode.Single);
    }
}
#endif