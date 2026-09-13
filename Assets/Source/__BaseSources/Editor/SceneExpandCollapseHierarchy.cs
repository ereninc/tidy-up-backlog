#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class SceneExpandCollapseHierarchy
{
    private static object[] m_ParametersExpand = new object[] { null, true };
    private static object[] m_ParametersCollapse = new object[] { null, false };
    private static System.Type m_SceneHierarchyWindowType = null;
    private static System.Type SceneHierarchyWindowType
    {
        get
        {
            if (m_SceneHierarchyWindowType == null)
            {
                var assembly = typeof(EditorWindow).Assembly;
                m_SceneHierarchyWindowType = assembly.GetType("UnityEditor.SceneHierarchyWindow");
            }
            return m_SceneHierarchyWindowType;
        }
    }
    private static MethodInfo m_SetSceneExpanded = null;
    private static MethodInfo SetSceneExpandedImpl
    {
        get
        {
            if (m_SetSceneExpanded == null)
                m_SetSceneExpanded = SceneHierarchyWindowType.GetMethod("SetExpanded", BindingFlags.NonPublic | BindingFlags.Instance);
            return m_SetSceneExpanded;
        }
    }

    static SceneExpandCollapseHierarchy()
    {
        EditorSceneManager.sceneOpened += OnSceneOpened;
        
        //for runtime expand
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
    {
        ExpandScene(scene);
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ExpandScene(scene);
    }

    private static void ExpandScene(Scene scene)
    {
        var hierarchyWindow = EditorWindow.GetWindow(SceneHierarchyWindowType);
        if (hierarchyWindow == null)
            return;

        var sceneHandle = scene.handle;
        SetSceneExpanded(sceneHandle, true);
    }

    private static void SetSceneExpanded(int sceneHandle, bool expand)
    {
        var hierarchyWindow = EditorWindow.GetWindow(SceneHierarchyWindowType);
        if (expand)
        {
            m_ParametersExpand[0] = sceneHandle;
            SetSceneExpandedImpl.Invoke(hierarchyWindow, m_ParametersExpand);
        }
        else
        {
            m_ParametersCollapse[0] = sceneHandle;
            SetSceneExpandedImpl.Invoke(hierarchyWindow, m_ParametersCollapse);
        }
    }

    [MenuItem("EW-ToolBar/Expand Scene")]
    private static void ExpandCurrentScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        ExpandScene(activeScene);
    }

    [MenuItem("EW-ToolBar/Collapse Scene")]
    private static void CollapseCurrentScene()
    {
        var activeScene = SceneManager.GetActiveScene();
        var hierarchyWindow = EditorWindow.GetWindow(SceneHierarchyWindowType);
        if (hierarchyWindow == null) return;

        var sceneHandle = activeScene.handle;
        SetSceneExpanded(sceneHandle, false);
    }
}
#endif
