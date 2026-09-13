using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SoundEffect), true)]
public class AudioEventEditor : Editor
{

    [SerializeField] AudioSource previewer;

    public void OnEnable()
    {
        previewer = EditorUtility.CreateGameObjectWithHideFlags("Audio preview", HideFlags.HideAndDontSave, typeof(AudioSource)).GetComponent<AudioSource>();
    }

    public void OnDisable()
    {
        DestroyImmediate(previewer.gameObject);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUI.BeginDisabledGroup(serializedObject.isEditingMultipleObjects);
        if (GUILayout.Button("Preview"))
        {
            ((SoundEffect)target).Play(previewer);
        }
        EditorGUI.EndDisabledGroup();
    }
}
