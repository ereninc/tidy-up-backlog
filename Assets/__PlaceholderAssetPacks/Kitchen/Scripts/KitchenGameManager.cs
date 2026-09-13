using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class KitchenGameManager : NetworkBehaviour
{
	public static KitchenGameManager Instance { get; private set; }

	public event EventHandler OnStateChanged;

	public event EventHandler OnLocalGamePaused;
	public event EventHandler OnLocalGameUnpaused;

	public event EventHandler OnMultiplayerGamePaused;
	public event EventHandler OnMultiplayerGameUnpaused;

	public event EventHandler OnLocalPlayerReadyChanged;

	private enum State
	{
		WaitingToStart,
		CountdownToStart,
		GamePlaying,
		GameOver,
	}

	[SerializeField] private Transform playerPrefab;
	
	private NetworkVariable<State> state = new NetworkVariable<State>(State.WaitingToStart);
	private bool isLocalPlayerReady = false;

	private NetworkVariable<float> countdownToStartTimer = new NetworkVariable<float>(3f);
	private NetworkVariable<float> gamePlayingTimer = new NetworkVariable<float>(0f);

	private bool isLocalGamePaused = false;
	private NetworkVariable<bool> isGamePaused = new NetworkVariable<bool>(false);

	private Dictionary<ulong, bool> playerReadyDict;
	private Dictionary<ulong, bool> playerPausedDict;

	private bool autoCheckGamePausedState = false;

	private const float GamePlayingTimerMax = 90f;

	private void Awake()
	{
		Instance = this;

		state.Value = State.WaitingToStart;
		playerReadyDict = new Dictionary<ulong, bool>();
		playerPausedDict = new Dictionary<ulong, bool>();
	}

	private void Start()
	{
		GameInput.Instance.OnPauseAction += GameInput_OnPauseAction;
		GameInput.Instance.OnInteractAction += GameInput_OnInteractAction;

		//TRIGGER GAME TO START AUTOMATICALLY
		// state = State.CountdownToStart;
		// OnStateChanged?.Invoke(this, EventArgs.Empty);
		//TO HERE
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();
		state.OnValueChanged += State_OnValueChanged;
		isGamePaused.OnValueChanged += GamePaused_OnValueChanged;

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;
			NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += SceneManager_OnLoadEventCompleted;
		}
	}

	private void SceneManager_OnLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
	{
		foreach (var clientId in clientsCompleted)
		{
			Transform playerTransform = Instantiate(playerPrefab);
			NetworkObject networkObject = playerTransform.GetComponent<NetworkObject>();
			networkObject.SpawnAsPlayerObject(clientId, true);
		}
	}

	private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
	{
		if (IsServer)
		{
			autoCheckGamePausedState = true;
			playerReadyDict.Remove(clientId);
			playerPausedDict.Remove(clientId);
		}
	}

	public override void OnNetworkDespawn()
	{
		state.OnValueChanged -= State_OnValueChanged;
		isGamePaused.OnValueChanged -= GamePaused_OnValueChanged;
		base.OnNetworkDespawn();
	}

	private void GamePaused_OnValueChanged(bool previousValue, bool newValue)
	{
		if (newValue)
		{
			Time.timeScale = 0f;
			OnMultiplayerGamePaused?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			Time.timeScale = 1f;
			OnMultiplayerGameUnpaused?.Invoke(this, EventArgs.Empty);
		}
	}

	private void State_OnValueChanged(State previousValue, State newValue) { OnStateChanged?.Invoke(this, EventArgs.Empty); }

	private void GameInput_OnInteractAction(object sender, EventArgs e)
	{
		if (state.Value == State.WaitingToStart)
		{
			isLocalPlayerReady = true;
			OnLocalPlayerReadyChanged?.Invoke(this, EventArgs.Empty);
			OnLocalPlayerReadyServerRpc();
		}
	}

	[ServerRpc(RequireOwnership = false)]
	private void OnLocalPlayerReadyServerRpc(ServerRpcParams serverRpcParams = default)
	{
		// Debug.Log(serverRpcParams.Receive.SenderClientId); //host 0 - clientlar ilk girise gore 1-2-3-4...
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
		if (allClientsAreReady) state.Value = State.CountdownToStart;
	}

	private void GameInput_OnPauseAction(object sender, EventArgs e) { TogglePauseGame(); }

	private void Update()
	{
		if (!IsServer) return;

		switch (state.Value)
		{
		case State.WaitingToStart:
		break;
		case State.CountdownToStart:
			countdownToStartTimer.Value -= Time.deltaTime;
			if (countdownToStartTimer.Value < 0f)
			{
				state.Value = State.GamePlaying;
				gamePlayingTimer.Value = GamePlayingTimerMax;
			}
		break;
		case State.GamePlaying:
			gamePlayingTimer.Value -= Time.deltaTime;
			if (gamePlayingTimer.Value < 0f)
			{
				state.Value = State.GameOver;
			}
		break;
		case State.GameOver:
		break;
		}
	}

	private void LateUpdate()
	{
		if (autoCheckGamePausedState)
		{
			autoCheckGamePausedState = false;
			CheckGamePausedState();
		}
	}

	public bool IsGamePlaying() { return state.Value == State.GamePlaying; }

	public bool IsCountdownToStartActive() { return state.Value == State.CountdownToStart; }

	public float GetCountdownToStartTimer() { return countdownToStartTimer.Value; }

	public bool IsGameOver() { return state.Value == State.GameOver; }

	public bool IsLocalPlayerReady() { return isLocalPlayerReady; }

	public float GetGamePlayingTimerNormalized() { return 1 - (gamePlayingTimer.Value / GamePlayingTimerMax); }

	public bool IsWaitingToStart() { return state.Value == State.WaitingToStart; }

	public void TogglePauseGame()
	{
		isLocalGamePaused = !isLocalGamePaused;
		if (isLocalGamePaused)
		{
			PauseGameServerRpc();
			OnLocalGamePaused?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			UnPauseGameServerRpc();
			OnLocalGameUnpaused?.Invoke(this, EventArgs.Empty);
		}
	}
	
	public void ForceUnpauseLocalGame()
	{
		isLocalGamePaused = false;
		Time.timeScale = 1f;

		OnLocalGameUnpaused?.Invoke(this, EventArgs.Empty);
	}

	[ServerRpc(RequireOwnership = false)]
	private void PauseGameServerRpc(ServerRpcParams serverRpcParams = default)
	{
		playerPausedDict[serverRpcParams.Receive.SenderClientId] = true;
		CheckGamePausedState();
	}

	[ServerRpc(RequireOwnership = false)]
	private void UnPauseGameServerRpc(ServerRpcParams serverRpcParams = default)
	{
		playerPausedDict[serverRpcParams.Receive.SenderClientId] = false;
		CheckGamePausedState();
	}

	private void CheckGamePausedState()
	{
		foreach (ulong singletonConnectedClientsId in NetworkManager.Singleton.ConnectedClientsIds)
		{
			if (playerPausedDict.TryGetValue(singletonConnectedClientsId, out bool isPaused) && isPaused)
			{
				isGamePaused.Value = true;
				return;
			}
		}
		//all players unpaused
		isGamePaused.Value = false;
	}
}
