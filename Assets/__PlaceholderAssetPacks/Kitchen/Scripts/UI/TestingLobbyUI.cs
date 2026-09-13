using System;
using UnityEngine;
using UnityEngine.UI;

public class TestingLobbyUI : MonoBehaviour
{
	[SerializeField] private Button btnCreateGame;
	[SerializeField] private Button btnJoinGame;

	private void Awake()
	{
		btnCreateGame.onClick.AddListener(() =>
		{
			KitchenGameMultiplayer.Instance.StartHost();
			Loader.LoadNetwork(Loader.Scene.CharacterSelectScene); 
		});

		btnJoinGame.onClick.AddListener(() =>
		{
			KitchenGameMultiplayer.Instance.StartClient();
			// Loader.LoadNetwork(Loader.Scene.CharacterSelectionScene);
		});
	}

	private void OnDisable()
	{
		btnCreateGame.onClick.RemoveAllListeners();
		btnJoinGame.onClick.RemoveAllListeners();
	}
}