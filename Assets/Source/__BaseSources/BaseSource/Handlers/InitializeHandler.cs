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
}