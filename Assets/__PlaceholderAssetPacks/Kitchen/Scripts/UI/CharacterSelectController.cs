using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class CharacterSelectController : NetworkBehaviour
{
	private Dictionary<ulong, bool> playerReadyDict;
	public static CharacterSelectController Instance { get ; private set; }

	public event EventHandler OnReadyChanged;
	
	private void Awake()
	{
		playerReadyDict = new Dictionary<ulong, bool>();
		Instance = this;
	}

	public void SetPlayerReady()
	{
		OnLocalPlayerReadyServerRpc();
	}
    
	[ServerRpc(RequireOwnership = false)]
	private void OnLocalPlayerReadyServerRpc(ServerRpcParams serverRpcParams = default)
	{
		// Debug.Log(serverRpcParams.Receive.SenderClientId); //host 0 - clientlar ilk girise gore 1-2-3-4...
		SetPlayerReadyClientRpc(serverRpcParams.Receive.SenderClientId);
		playerReadyDict[serverRpcParams.Receive.SenderClientId] = true;

		bool allClientsAreReady = true;
		foreach (ulong singletonConnectedClientsId in NetworkManager.Singleton.ConnectedClientsIds)
		{
			if (!playerReadyDict.ContainsKey(singletonConnectedClientsId) || !playerReadyDict[singletonConnectedClientsId])
			{
				allClientsAreReady = false;
				break;
			}
		}

		Debug.Log("AllClientsAreReady: " + allClientsAreReady);
		if (allClientsAreReady)
		{
			GameLobby.Instance.DeleteLobby();
			Loader.LoadNetwork(Loader.Scene.GameScene);
		}
	}

	[ClientRpc]
	private void SetPlayerReadyClientRpc(ulong clientId)
	{
		playerReadyDict[clientId] = true;
		OnReadyChanged?.Invoke(this, EventArgs.Empty);
	}

	public bool IsPlayerReady(ulong clientId)
	{
		return playerReadyDict.ContainsKey(clientId) && playerReadyDict.ContainsKey(clientId);
	}
}
