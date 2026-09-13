using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class HostDisconnectedUI : MonoBehaviour
{
	[SerializeField] private Button btnPlayAgain;

	private NetworkManager _networkManager;

	// Bu instance oyuna client olarak mı girdi?
	private bool _isClientOnlyInstance;

	private void Awake() { btnPlayAgain.onClick.AddListener(HandlePlayAgainClicked); }

	private void Start()
	{
		_networkManager = NetworkManager.Singleton;

		if (!_networkManager)
		{
			Debug.LogError("NetworkManager bulunamadı.", this);
			Hide();
			return;
		}

		/*
		 * Bunu callback içinde kontrol etmiyoruz.
		 * Disconnect gerçekleştiğinde NetworkManager'ın IsClient / IsHost
		 * değerleri değişmiş olabilir.
		 */
		_isClientOnlyInstance = _networkManager.IsClient && !_networkManager.IsServer;

		_networkManager.OnClientDisconnectCallback += NetworkManager_OnClientDisconnectCallback;

		Hide();
	}

	private void NetworkManager_OnClientDisconnectCallback(ulong clientId)
	{
		/*
		 * Client tarafında bu callback yalnızca local client bağlantısını
		 * kaybettiğinde çalışır.
		 *
		 * Gelen clientId, host ID'si değildir.
		 * Local client'ın kendi ID'sidir.
		 */
		if (!_isClientOnlyInstance) return;

		HandleHostDisconnected();
	}

	private void HandleHostDisconnected()
	{
		if (KitchenGameManager.Instance)
		{
			KitchenGameManager.Instance.ForceUnpauseLocalGame();
		}
		else
		{
			Time.timeScale = 1f;
		}

		Show();
		btnPlayAgain.Select();
	}

	private void HandlePlayAgainClicked()
	{
		Time.timeScale = 1f;

		if (_networkManager && _networkManager.IsListening)
		{
			_networkManager.Shutdown();
		}

		Loader.Load(Loader.Scene.MainMenuScene);
	}

	private void Show() { gameObject.SetActive(true); }

	private void Hide() { gameObject.SetActive(false); }

	private void OnDestroy()
	{
		if (_networkManager)
		{
			_networkManager.OnClientDisconnectCallback -= NetworkManager_OnClientDisconnectCallback;
		}
	}
}