using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectPlayer : MonoBehaviour
{
	[SerializeField] private int playerIndex;
	[SerializeField] private GameObject readyTextGameObject;
	[SerializeField] private PlayerVisual playerVisual;
	[SerializeField] private Button kickButton;

	[SerializeField] private TextMeshPro txtPlayerName;

	private void Awake()
	{
		kickButton.onClick.AddListener(() =>
		{
			var playerData = KitchenGameMultiplayer.Instance.GetPlayerDataFromIndex(playerIndex);
			GameLobby.Instance.LeaveLobbyOnKick(playerData.playerId.ToString());
			KitchenGameMultiplayer.Instance.KickPlayer(playerData.clientId);
		});
	}

	private void Start()
	{
		KitchenGameMultiplayer.Instance.OnPlayerDataNetworkListChanged += KitchenGameMultiplayer_OnPlayerDataNetworkListChanged;
		CharacterSelectController.Instance.OnReadyChanged += CharacterSelectController_OnReadyChanged;

		kickButton.gameObject.SetActive(NetworkManager.Singleton.IsServer);

		UpdatePlayer();
	}

	private void KitchenGameMultiplayer_OnPlayerDataNetworkListChanged(object sender, EventArgs e) { UpdatePlayer(); }

	private void CharacterSelectController_OnReadyChanged(object sender, EventArgs e) { UpdatePlayer(); }

	private void UpdatePlayer()
	{
		if (KitchenGameMultiplayer.Instance.IsPlayerIndexConnected(playerIndex))
		{
			Show();
			var playerData = KitchenGameMultiplayer.Instance.GetPlayerDataFromIndex(playerIndex);
			readyTextGameObject.SetActive(CharacterSelectController.Instance.IsPlayerReady(playerData.clientId));
			txtPlayerName.text = playerData.playerName.ToString();
			
			playerVisual.SetPlayerColor(KitchenGameMultiplayer.Instance.GetPlayerColor(playerData.colorId));
		}
		else
		{
			Hide();
		}
	}

	private void Show() { gameObject.SetActive(true); }

	private void Hide() { gameObject.SetActive(false); }

	private void OnDestroy()
	{
		if (KitchenGameMultiplayer.Instance != null)
		{
			KitchenGameMultiplayer.Instance.OnPlayerDataNetworkListChanged -= KitchenGameMultiplayer_OnPlayerDataNetworkListChanged;
		}
	}
}
