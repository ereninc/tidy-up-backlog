using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class GameLobby : Singleton<GameLobby>
{
	private const string KeyRelayJoinCode = "RelayJoinCode";

	private Lobby _joinedLobby;

	private float _heartbeatTimer;
	private const float HeartbeatTimerMax = 15f;

	private float _listLobbiesTimer;
	private const float ListLobbiesTimerMax = 3f;

	public Lobby GetLobby() => _joinedLobby;

	public event EventHandler OnCreateLobbyStarted;
	public event EventHandler OnCreateLobbyFailed;

	public event EventHandler OnQuickJoinStarted;
	public event EventHandler OnQuickJoinFailed;

	public event EventHandler OnJoinStarted;
	public event EventHandler OnJoinFailed;

	public event EventHandler<OnLobbyListChangedEventArgs> OnLobbyListChanged;

	public class OnLobbyListChangedEventArgs : EventArgs
	{
		public List<Lobby> Lobbies;
	}

	private void Awake()
	{
		DontDestroyOnLoad(gameObject);
		InitializeUnityAuthentication();
	}

	private void Update()
	{
		HandleHeartbeat();
		HandlePeriodicUpdateLobbiesList();
	}

	private async void InitializeUnityAuthentication()
	{
		try
		{
			if (UnityServices.State != ServicesInitializationState.Initialized)
			{
				var options = new InitializationOptions();
				// options.SetProfile(Random.Range(0, 10000).ToString());
				await UnityServices.InitializeAsync(options);
			}
			if (!AuthenticationService.Instance.IsSignedIn)
			{
				await AuthenticationService.Instance.SignInAnonymouslyAsync();
			}
		}
		catch (Exception e)
		{
			Debug.LogError(e);
		}
	}

	#region [ RELAY ]

	private async Task<Allocation> AllocateRelay() { return await RelayService.Instance.CreateAllocationAsync(KitchenGameMultiplayer.MaxPlayerAccepted - 1); }

	private async Task<string> GetRelayJoinCode(Allocation allocation) { return await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId); }

	private async Task<JoinAllocation> JoinRelay(string relayJoinCode) { return await RelayService.Instance.JoinAllocationAsync(relayJoinCode); }

	private void SetHostRelayData(Allocation allocation)
	{
		var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
		RelayServerData relayServerData = allocation.ToRelayServerData("dtls");
		transport.SetRelayServerData(relayServerData);
	}

	private void SetClientRelayData(JoinAllocation allocation)
	{
		var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
		RelayServerData relayServerData = allocation.ToRelayServerData("dtls");
		transport.SetRelayServerData(relayServerData);
	}

	#endregion

	public async void CreateLobby(string lobbyName, bool isPrivate)
	{
		OnCreateLobbyStarted?.Invoke(this, EventArgs.Empty);
		try
		{
			var options = new CreateLobbyOptions
			{
				IsPrivate = isPrivate
			};
			_joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, KitchenGameMultiplayer.MaxPlayerAccepted, options);

			#region [ RELAY ]

			// Host için Relay allocation oluştur.
			Allocation allocation = await AllocateRelay();

			// Allocation üzerinden client'ların kullanacağı Relay join code'u al.
			string relayJoinCode = await GetRelayJoinCode(allocation);

			// Join code'u Lobby Data içine koy.
			var updateLobbyOptions = new UpdateLobbyOptions
			{
				Data = new Dictionary<string, DataObject>
				{
					{
						KeyRelayJoinCode, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode)
					}
				}
			};
			_joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(_joinedLobby.Id, updateLobbyOptions);

			// Host başlamadan önce UnityTransport'a Relay datasını ver.
			SetHostRelayData(allocation);

			#endregion

			KitchenGameMultiplayer.Instance.StartHost();
			Loader.LoadNetwork(Loader.Scene.CharacterSelectScene);
		}
		catch (Exception e)
		{
			Debug.LogError($"CreateLobby failed:\n{e}");
			OnCreateLobbyFailed?.Invoke(this, EventArgs.Empty);
		}
	}

	public async void QuickJoin()
	{
		OnQuickJoinStarted?.Invoke(this, EventArgs.Empty);
		try
		{
			_joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync();
			await ConnectToJoinedLobbyRelay();
			KitchenGameMultiplayer.Instance.StartClient();
		}
		catch (Exception e)
		{
			Debug.LogError($"QuickJoin failed:\n{e}");
			OnQuickJoinFailed?.Invoke(this, EventArgs.Empty);
		}
	}

	public async void JoinWithCode(string lobbyCode)
	{
		OnJoinStarted?.Invoke(this, EventArgs.Empty);
		try
		{
			var options = new JoinLobbyByCodeOptions();
			_joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, options);
			await ConnectToJoinedLobbyRelay();
			KitchenGameMultiplayer.Instance.StartClient();
		}
		catch (Exception e)
		{
			Debug.LogError($"JoinWithCode failed:\n{e}");
			OnJoinFailed?.Invoke(this, EventArgs.Empty);
		}
	}

	public async void JoinWithId(string lobbyId)
	{
		OnJoinStarted?.Invoke(this, EventArgs.Empty);
		try
		{
			var options = new JoinLobbyByIdOptions();
			_joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, options);
			await ConnectToJoinedLobbyRelay();
			KitchenGameMultiplayer.Instance.StartClient();
		}
		catch (Exception e)
		{
			Debug.LogError($"JoinWithId failed:\n{e}");
			OnJoinFailed?.Invoke(this, EventArgs.Empty);
		}
	}

	private async Task ConnectToJoinedLobbyRelay()
	{
		if (_joinedLobby == null)
		{
			throw new InvalidOperationException("Cannot connect to Relay because joined lobby is null.");
		}
		if (_joinedLobby.Data == null || !_joinedLobby.Data.TryGetValue(KeyRelayJoinCode, out DataObject relayData))
		{
			throw new InvalidOperationException("Lobby does not contain a Relay join code.");
		}
		string relayJoinCode = relayData.Value;
		if (string.IsNullOrWhiteSpace(relayJoinCode))
		{
			throw new InvalidOperationException("Relay join code is empty.");
		}
		JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);
		SetClientRelayData(joinAllocation);
	}

	public async void DeleteLobby()
	{
		try
		{
			if (_joinedLobby == null) return;
			await LobbyService.Instance.DeleteLobbyAsync(_joinedLobby.Id);
			_joinedLobby = null;
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError(e);
		}
	}

	public async void LeaveLobby()
	{
		try
		{
			if (_joinedLobby == null) return;
			await LobbyService.Instance.RemovePlayerAsync(_joinedLobby.Id, AuthenticationService.Instance.PlayerId);
			_joinedLobby = null;
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError(e);
		}
	}

	public async void LeaveLobbyOnKick(string playerId)
	{
		if (!IsLobbyHost()) return;
		try
		{
			await LobbyService.Instance.RemovePlayerAsync(_joinedLobby.Id, playerId);
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError(e);
		}
	}

	private async void ListLobbies()
	{
		try
		{
			var options = new QueryLobbiesOptions
			{
				Filters = new List<QueryFilter>
				{
					new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT)
				}
			};
			QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
			OnLobbyListChanged?.Invoke(this, new OnLobbyListChangedEventArgs
			{
				Lobbies = response.Results
			});
		}
		catch (LobbyServiceException e)
		{
			Debug.LogError(e);
		}
	}

	#region [ HELPERS ]

	private void HandleHeartbeat()
	{
		if (!IsLobbyHost()) return;
		_heartbeatTimer -= Time.deltaTime;
		if (_heartbeatTimer > 0f) return;
		_heartbeatTimer = HeartbeatTimerMax;
		LobbyService.Instance.SendHeartbeatPingAsync(_joinedLobby.Id);
	}

	private void HandlePeriodicUpdateLobbiesList()
	{
		if (!AuthenticationService.Instance.IsSignedIn) return;
		if (SceneManager.GetActiveScene().name != nameof(Loader.Scene.LobbyScene)) return;
		_listLobbiesTimer -= Time.deltaTime;
		if (_listLobbiesTimer > 0f) return;
		_listLobbiesTimer = ListLobbiesTimerMax;
		ListLobbies();
	}

	private bool IsLobbyHost() { return _joinedLobby != null && _joinedLobby.HostId == AuthenticationService.Instance.PlayerId; }

	#endregion
}