using System.Collections.Generic;
using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class GameLobbyUI : MonoBehaviour
{
	[SerializeField] private Button btnMainMenu;
	[SerializeField] private Button btnCreateLobby;
	[SerializeField] private Button btnJoinGame;

	[Space(2)] 
	[SerializeField] private CreateLobbyUI createLobbyUI;

	[Space(2)] 
	[SerializeField] private Button btnJoinCode;
	[SerializeField] private TMP_InputField lobbyCodeInput;

	[Space(2)] 
	[SerializeField] private TMP_InputField playerNameInput;

	[Header("Lobby List")]
	[SerializeField] private Transform lobbyContainer;
	[SerializeField] private LobbyListElement lobbyTemplate;

	private void Awake()
	{
		btnMainMenu.onClick.AddListener(() =>
		{
			GameLobby.Instance.LeaveLobby();
			Loader.Load(Loader.Scene.MainMenuScene);
		});
		btnCreateLobby.onClick.AddListener(() =>
		{
			createLobbyUI.Show();
		});
		btnJoinGame.onClick.AddListener(() =>
		{
			GameLobby.Instance.QuickJoin();
		});
		btnJoinCode.onClick.AddListener(() =>
		{
			GameLobby.Instance.JoinWithCode(lobbyCodeInput.text);
		});
		
		lobbyTemplate.SetActiveGameObject(false);
	}

	private void Start()
	{
		playerNameInput.text = KitchenGameMultiplayer.Instance.GetPlayerName();
		playerNameInput.onValueChanged.AddListener((string newPlayerName) =>
		{
			KitchenGameMultiplayer.Instance.SetPlayerName(newPlayerName);
		});

		GameLobby.Instance.OnLobbyListChanged += GameLobby_OnLobbyListChanged;
		UpdateLobbyList(new List<Lobby>());
	}
	
	private void GameLobby_OnLobbyListChanged(object sender, GameLobby.OnLobbyListChangedEventArgs e)
	{
		UpdateLobbyList(e.Lobbies);	
	}

	private void UpdateLobbyList(List<Lobby> lobbies)
	{
		foreach (Transform child in lobbyContainer)
		{
			if (child == lobbyTemplate.transform) continue;
			Destroy(child.gameObject);
		}

		foreach (Lobby lobby in lobbies)
		{
			LobbyListElement lobbyListElement = Instantiate(lobbyTemplate, lobbyContainer);
			lobbyListElement.SetActiveGameObject(true);
			lobbyListElement.SetLobby(lobby);
		}
	}

	private void OnDestroy()
	{
		btnMainMenu.onClick.RemoveAllListeners();
		btnCreateLobby.onClick.RemoveAllListeners();
		btnJoinGame.onClick.RemoveAllListeners();
		btnJoinCode.onClick.RemoveAllListeners();

		if (GameLobby.Instance)
		{
			GameLobby.Instance.OnLobbyListChanged -= GameLobby_OnLobbyListChanged;
		}
	}
}