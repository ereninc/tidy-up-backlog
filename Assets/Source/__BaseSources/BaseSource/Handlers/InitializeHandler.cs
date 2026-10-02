using UnityEngine;

public class InitializeHandler : ObjectModel
{
	[SerializeField] private bool initializeOnAwake;
	[SerializeField] private ObjectModel[] initializeElement;
	[SerializeField] private ObjectModel persistentAudioSource;

	private void Awake()
	{
		if (initializeOnAwake) Initialize();
	}

	public override void Initialize()
	{
		if (persistentAudioSource) persistentAudioSource.Initialize();
		
		foreach (var item in initializeElement)
		{
			item.Initialize();
		}
	}
	
	private void Update()
	{
		#if UNITY_EDITOR
		if (Input.GetKey(KeyCode.LeftControl))
		{
			if (Input.GetKeyDown(KeyCode.Space)) UnityEditor.EditorWindow.focusedWindow.maximized = !UnityEditor.EditorWindow.focusedWindow.maximized;
		}
		#endif
	}
}