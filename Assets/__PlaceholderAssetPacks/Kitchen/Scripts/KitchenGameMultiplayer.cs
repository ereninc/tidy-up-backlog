using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode.Transports.UTP;

public class KitchenGameMultiplayer : NetworkBehaviour
{
	public const int MaxPlayerAccepted = 4;
	private const string PlayerPrefsPlayerNameMultiplayer = "PlayerNameMultiplePlayer";
	public static bool PlayMultiplayer;
	
	public static KitchenGameMultiplayer Instance { get; private set; }

	[SerializeField] private KitchenObjectListSO kitchenObjectListSO;
	[SerializeField] private List<Color> playerColors;

	public event EventHandler OnTryingToJoinGame;
	public event EventHandler OnFailedToJoinGame;
	public event EventHandler OnPlayerDataNetworkListChanged;
	
	private NetworkList<PlayerData> playerDataNetworkList;
	private string _playerName;
	
	private void Awake()
	{
		Instance = this;
		DontDestroyOnLoad(gameObject);

		_playerName = PlayerPrefs.GetString(PlayerPrefsPlayerNameMultiplayer, "Player_" + UnityEngine.Random.Range(100, 9999));
		
		playerDataNetworkList = new NetworkList<PlayerData>();
		playerDataNetworkList.OnListChanged += NetworkList_OnPlayerDataNetworkListChanged;
	}

	private void Start()
	{
		if (!PlayMultiplayer)
		{
			StartSingleplayer();
		}
	}

	private void StartSingleplayer()
	{
		UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

		// Relay modundan çık ve local/direct transport kullan.
		transport.SetConnectionData(
			"127.0.0.1",
			7777,
			"127.0.0.1"
		);

		if (!StartHost())
		{
			Debug.LogError("Failed to start singleplayer host.");
			return;
		}

		Loader.LoadNetwork(Loader.Scene.GameScene);
	}

	private void NetworkList_OnPlayerDataNetworkListChanged(NetworkListEvent<PlayerData> changeEvent) { OnPlayerDataNetworkListChanged?.Invoke(this, EventArgs.Empty); }

	#region [ PUBLIC API ]
	public bool StartHost()
	{
		NetworkManager.Singleton.ConnectionApprovalCallback -= NetworkManager_ConnectionApprovalCallback;
		NetworkManager.Singleton.OnClientConnectedCallback -= NetworkManager_Client_OnClientConnectedCallback;
		NetworkManager.Singleton.OnClientDisconnectCallback -= NetworkManager_Server_OnClientDisconnectCallback;

		NetworkManager.Singleton.ConnectionApprovalCallback += NetworkManager_ConnectionApprovalCallback;
		NetworkManager.Singleton.OnClientConnectedCallback += NetworkManager_Client_OnClientConnectedCallback;
		NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_Server_OnClientDisconnectCallback;

		return NetworkManager.Singleton.StartHost();
	}
	
	private void NetworkManager_Server_OnClientDisconnectCallback(ulong clientId)
	{
		for (int i = 0; i < playerDataNetworkList.Count; i++)
		{
			PlayerData playerData = playerDataNetworkList[i];
			if (playerData.clientId == clientId)
			{
				//disconnected
				playerDataNetworkList.RemoveAt(i);
			}
		}
	}

	private void NetworkManager_ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest connectionApprovalRequest, NetworkManager.ConnectionApprovalResponse connectionApprovalResponse)
	{
		if (SceneManager.GetActiveScene().name != nameof(Loader.Scene.CharacterSelectScene))
		{
			connectionApprovalResponse.Approved = false;
			connectionApprovalResponse.Reason = "Game has already started!";
			return;
		}

		if (NetworkManager.Singleton.ConnectedClientsList.Count >= MaxPlayerAccepted)
		{
			connectionApprovalResponse.Approved = false;
			connectionApprovalResponse.Reason = "Game is full!";
			return;
		}
		connectionApprovalResponse.Approved = true;
	}

	private bool _isTryingToJoinGame;

	public void StartClient()
	{
		NetworkManager networkManager = NetworkManager.Singleton;

		_isTryingToJoinGame = true;

		OnTryingToJoinGame?.Invoke(this, EventArgs.Empty);

		networkManager.OnClientConnectedCallback -= NetworkManager_Client_OnClientConnectedCallback;
		networkManager.OnClientDisconnectCallback -= NetworkManager_Client_OnClientDisconnectCallback;

		networkManager.OnClientConnectedCallback += NetworkManager_Client_OnClientConnectedCallback;
		networkManager.OnClientDisconnectCallback += NetworkManager_Client_OnClientDisconnectCallback;

		networkManager.StartClient();
	}

	public string GetPlayerName() => _playerName;
	public void SetPlayerName(string playerName)
	{
		_playerName = playerName; 
		PlayerPrefs.SetString(PlayerPrefsPlayerNameMultiplayer, _playerName);
	}

	#endregion

	private void NetworkManager_Client_OnClientConnectedCallback(ulong clientId)
	{
		if (NetworkManager.Singleton.IsServer)
		{
			playerDataNetworkList.Add(new PlayerData
			{
				clientId = clientId,
				colorId =  GetFirstUnusedColorId(),
			});
		}

		if (clientId == NetworkManager.Singleton.LocalClientId)
		{
			_isTryingToJoinGame = false;
		}
		
		SetPlayerNameServerRpc(GetPlayerName());
		SetPlayerIdServerRpc(AuthenticationService.Instance.PlayerId);
	}

	private void NetworkManager_Client_OnClientDisconnectCallback(ulong clientId)
	{
		if (NetworkManager.Singleton.IsServer)
		{
			for (int i = playerDataNetworkList.Count - 1; i >= 0; i--)
			{
				if (playerDataNetworkList[i].clientId != clientId) continue;

				playerDataNetworkList.RemoveAt(i);
				break;
			}

			return;
		}

		if (!_isTryingToJoinGame) return;

		_isTryingToJoinGame = false;
		OnFailedToJoinGame?.Invoke(this, EventArgs.Empty);
	}

	#region [ SPAWN ]
	public void SpawnKitchenObject(KitchenObjectSO kitchenObjectSO, IKitchenObjectParent kitchenObjectParent) { SpawnKitchenObjectServerRpc(GetKitchenObjectSOIndex(kitchenObjectSO), kitchenObjectParent.GetNetworkObject()); }

	[ServerRpc(RequireOwnership = false)]
	private void SpawnKitchenObjectServerRpc(int kitchenObjectSOListIndex, NetworkObjectReference kitchenObjectParentNetworkObjectReference)
	{
		KitchenObjectSO kitchenObjectSO = GetKitchenObjectSOByIndex(kitchenObjectSOListIndex);
		
		kitchenObjectParentNetworkObjectReference.TryGet(out NetworkObject kitchenObjectParentNetworkObject);
		IKitchenObjectParent kitchenObjectParent = kitchenObjectParentNetworkObject.GetComponent<IKitchenObjectParent>();

		if (kitchenObjectParent.HasKitchenObject()) return; //zaten varsa return -> better for laggy players
		
		Transform kitchenObjectTransform = Instantiate(kitchenObjectSO.prefab); //spawn

		//network object olarak spawn et
		NetworkObject kitchenNetworkObject = kitchenObjectTransform.GetComponent<NetworkObject>();
		kitchenNetworkObject.Spawn(true);

		//transform islemleri
		KitchenObject kitchenObject = kitchenObjectTransform.GetComponent<KitchenObject>();

		kitchenObject.SetKitchenObjectParent(kitchenObjectParent);
	}
	#endregion

	#region [ DESTROY ]
	public void DestroyKitchenObject(KitchenObject kitchenObject) { DespawnKitchenObjectServerRpc(kitchenObject.NetworkObject); }

	[ServerRpc(RequireOwnership = false)]
	private void DespawnKitchenObjectServerRpc(NetworkObjectReference kitchenObjectNetworkObjectReference)
	{
		kitchenObjectNetworkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject);

		if (!kitchenObjectNetworkObject) return; //already destroyed -> better for laggy players
		KitchenObject kitchenObject = kitchenObjectNetworkObject.GetComponent<KitchenObject>();

		ClearKitchenObjectOnParentClientRpc(kitchenObjectNetworkObjectReference);
		kitchenObject.DestroySelf();
	}

	[ClientRpc]
	private void ClearKitchenObjectOnParentClientRpc(NetworkObjectReference networkObjectReference)
	{
		networkObjectReference.TryGet(out NetworkObject kitchenObjectNetworkObject);
		KitchenObject kitchenObject = kitchenObjectNetworkObject.GetComponent<KitchenObject>();

		kitchenObject.ClearKitchenObjectOnParent();
	}
	#endregion

	#region [ HELPERS ]
	public int GetKitchenObjectSOIndex(KitchenObjectSO kitchenObjectSO) { return kitchenObjectListSO.kitchenObjectSOList.IndexOf(kitchenObjectSO); }
	public KitchenObjectSO GetKitchenObjectSOByIndex(int kitchenObjectSOIndex) { return kitchenObjectListSO.kitchenObjectSOList[kitchenObjectSOIndex]; }
	public bool IsPlayerIndexConnected(int playerIndex) { return playerIndex < playerDataNetworkList.Count; }
	
	public int GetPlayerDataIndexFromClientId(ulong clientId)
	{
		for (int i = 0; i< playerDataNetworkList.Count; i++)
		{
			if (playerDataNetworkList[i].clientId == clientId)
			{
				return i;
			}
		}
		return -1;
	}
	
	public PlayerData GetPlayerDataFromClientId(ulong clientId)
	{
		foreach (var playerData in playerDataNetworkList)
		{
			if (playerData.clientId == clientId)
			{
				return playerData;
			}
		}
		return default(PlayerData);
	}
	public PlayerData GetPlayerData() { return GetPlayerDataFromClientId(NetworkManager.Singleton.LocalClientId); }
	public PlayerData GetPlayerDataFromIndex(int playerIndex) { return playerDataNetworkList[playerIndex]; }

	public Color GetPlayerColor(int colorId) { return playerColors[colorId]; }
	#endregion

	public void ChangePlayerColor(int colorId) { ChangePlayerColorServerRpc(colorId); }

	[ServerRpc(RequireOwnership = false)]
	private void ChangePlayerColorServerRpc(int colorId, ServerRpcParams serverRpcParams = default)
	{
		if (!IsColorAvailable(colorId)) return;
		int playerDataIndex = GetPlayerDataIndexFromClientId(serverRpcParams.Receive.SenderClientId);
		PlayerData playerData =  playerDataNetworkList[playerDataIndex];
		playerData.colorId = colorId;
		
		playerDataNetworkList[playerDataIndex] = playerData;
	}

	private bool IsColorAvailable(int colorId)
	{
		foreach (PlayerData playerData in playerDataNetworkList)
		{
			if (playerData.colorId == colorId)
			{
				return false;
			}
		}
		return true;
	}

	private int GetFirstUnusedColorId()
	{
		for (int i = 0; i < playerColors.Count; i++)
		{
			if (IsColorAvailable(i)) return i;
		}

		return 0;
	}
		
	[ServerRpc(RequireOwnership = false)]
	private void SetPlayerNameServerRpc(string playerName, ServerRpcParams serverRpcParams = default)
	{
		int playerDataIndex = GetPlayerDataIndexFromClientId(serverRpcParams.Receive.SenderClientId);
		PlayerData playerData =  playerDataNetworkList[playerDataIndex];
		playerData.playerName = playerName;
		
		playerDataNetworkList[playerDataIndex] = playerData;
	}		
	
	[ServerRpc(RequireOwnership = false)]
	private void SetPlayerIdServerRpc(string playerId, ServerRpcParams serverRpcParams = default)
	{
		int playerDataIndex = GetPlayerDataIndexFromClientId(serverRpcParams.Receive.SenderClientId);
		PlayerData playerData =  playerDataNetworkList[playerDataIndex];
		playerData.playerId = playerId;
		
		playerDataNetworkList[playerDataIndex] = playerData;
	}

	public void KickPlayer(ulong clientId)
	{
		NetworkManager.Singleton.DisconnectClient(clientId);
		NetworkManager_Server_OnClientDisconnectCallback(clientId);
	}
}
