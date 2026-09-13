using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class LobbyMessageUI : MonoBehaviour
{
	[SerializeField] private TextMeshProUGUI messageText;
	[SerializeField] private Button btnClose;

	private void Awake()
	{
		btnClose.onClick.AddListener(Hide);
	}

	private void Start()
	{
		KitchenGameMultiplayer.Instance.OnFailedToJoinGame += KitchenGameMultiplayer_OnFailedToJoinGame;

		GameLobby.Instance.OnCreateLobbyStarted += GameLobby_OnCreateLobbyStarted;
		GameLobby.Instance.OnCreateLobbyFailed += GameLobby_OnCreateLobbyFailed;
		
		GameLobby.Instance.OnQuickJoinStarted += GameLobby_OnQuickJoinStarted;
		GameLobby.Instance.OnQuickJoinFailed += GameLobby_OnQuickJoinFailed;

		GameLobby.Instance.OnJoinStarted += GameLobby_OnJoinStarted;
		GameLobby.Instance.OnJoinFailed += GameLobby_OnJoinFailed;
		
		Hide();
	}
	
	private void GameLobby_OnCreateLobbyStarted(object sender, EventArgs e)
	{
		ShowMessage("Creating lobby...");	
	}
	
	private void GameLobby_OnCreateLobbyFailed(object sender, EventArgs e)
	{
		ShowMessage("Failed to create lobby");
	}
	
	private void GameLobby_OnQuickJoinStarted(object sender, EventArgs e)
	{
		ShowMessage("Could not find a Lobby to Quick Join!");	
	}
	
	private void GameLobby_OnQuickJoinFailed(object sender, EventArgs e)
	{
		ShowMessage("Quick join failed.");
	}
	
	private void GameLobby_OnJoinStarted(object sender, EventArgs e)
	{
		ShowMessage("Joining lobby...");	
	}
	
	private void GameLobby_OnJoinFailed(object sender, EventArgs e)
	{
		ShowMessage("Failed to join lobby.");
	}
	
	private void KitchenGameMultiplayer_OnFailedToJoinGame(object sender, EventArgs e)
	{
		if (NetworkManager.Singleton.DisconnectReason == "")
		{
			ShowMessage("Failed to connect.");
		}
		else
		{
			ShowMessage(NetworkManager.Singleton.DisconnectReason);
		}
	}

	private void ShowMessage(string message)
	{
		Show();
		messageText.text = message;
	}

	private void Show()
	{
		gameObject.SetActive(true);
	}

	private void Hide()
	{
		gameObject.SetActive(false);
	}

	private void OnDestroy()
	{
		if (KitchenGameMultiplayer.Instance != null)
		{
			KitchenGameMultiplayer.Instance.OnFailedToJoinGame -= KitchenGameMultiplayer_OnFailedToJoinGame;
		}

		if (GameLobby.Instance != null)
		{
			GameLobby.Instance.OnCreateLobbyStarted -= GameLobby_OnCreateLobbyStarted;
			GameLobby.Instance.OnCreateLobbyFailed -= GameLobby_OnCreateLobbyFailed;
		
			GameLobby.Instance.OnQuickJoinStarted -= GameLobby_OnQuickJoinStarted;
			GameLobby.Instance.OnQuickJoinFailed -= GameLobby_OnQuickJoinFailed;

			GameLobby.Instance.OnJoinStarted -= GameLobby_OnJoinStarted;
			GameLobby.Instance.OnJoinFailed -= GameLobby_OnJoinFailed;
		}
	}
}