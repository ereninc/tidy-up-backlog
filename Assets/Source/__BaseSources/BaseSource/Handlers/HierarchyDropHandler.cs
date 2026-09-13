using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;

[InitializeOnLoad]
public class HierarchyDropHandler
{
    static HierarchyDropHandler()
    {
        DragAndDrop.AddDropHandler(OnScriptHierarchyDrop);
    }

    private static DragAndDropVisualMode OnScriptHierarchyDrop(int dragInstanceId, HierarchyDropFlags dropMode,
        Transform parentForDraggedObjects, bool perform)
    {
        MonoScript monoScript = GetScriptBeingDragged();
        if (monoScript != null)
        {
            if (perform)
            {
                GameObject gameObject = CreateAndRename(monoScript.name);
                Component component = gameObject.AddComponent(monoScript.GetClass());
            }

            return DragAndDropVisualMode.Copy;
        }
        return DragAndDropVisualMode.None;
    }

    /// <summary>
    /// Get script that will be dropped in hierarchy. It should be inherited from MonoBehavior (It needs Transform to be in Scene)
    /// </summary>
    /// <returns>Script that dropped in Hierarchy</returns>
    static MonoScript GetScriptBeingDragged()
    {
        foreach (Object draggedObject in DragAndDrop.objectReferences)
        {
            if (draggedObject is MonoScript monoScript)
            {
                System.Type scriptType = monoScript.GetClass();
                if (scriptType != null && scriptType.IsSubclassOf(typeof(MonoBehaviour)))
                {
                    return monoScript;
                }
            }
        }

        return null;
    }

    public static GameObject CreateAndRename(string startingName)
    {
        GameObject gameObject = new GameObject(startingName);
        if (Selection.activeGameObject)
        {
            gameObject.transform.parent = Selection.activeGameObject.transform;
            gameObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        Selection.activeGameObject = gameObject;
        Undo.RegisterCreatedObjectUndo(gameObject, "Created GameObject");
        EditorApplication.delayCall += () =>
        {
            System.Type sceneHierarchyType = System.Type.GetType("UnityEditor.SceneHierarchyWindow,UnityEditor");
            EditorWindow.GetWindow(sceneHierarchyType).SendEvent(EditorGUIUtility.CommandEvent("Rename"));
        };

        return gameObject;
    }
}
#endif