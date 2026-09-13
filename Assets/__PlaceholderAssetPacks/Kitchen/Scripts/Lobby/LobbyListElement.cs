using TMPro;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.UI;

public class LobbyListElement : MonoBehaviour
{
	[SerializeField] private TextMeshProUGUI titleText;
	[SerializeField] private TextMeshProUGUI availablePlayersCountText;
	
	[SerializeField] private Button lobbyJoinButton;
	
	private Lobby _lobby;
	
	public void SetLobby(Lobby lobby)
	{
		_lobby = lobby;

		titleText.text = _lobby.Name;
		availablePlayersCountText.text =  (_lobby.MaxPlayers - _lobby.AvailableSlots) + "/" + _lobby.MaxPlayers;
		
		lobbyJoinButton.onClick.RemoveAllListeners();
		lobbyJoinButton.onClick.AddListener(() =>
		{
			GameLobby.Instance.JoinWithId(_lobby.Id);
		});
	}
}