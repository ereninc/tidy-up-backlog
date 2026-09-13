using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class GamePauseUI : MonoBehaviour
{
	[SerializeField] private Button resumeButton;
	[SerializeField] private Button mainMenuButton;
	[SerializeField] private Button optionsButton;

	private void Awake()
	{
		resumeButton.onClick.AddListener(() =>
		{
			KitchenGameManager.Instance.TogglePauseGame();
		});
		mainMenuButton.onClick.AddListener(() =>
		{
			NetworkManager.Singleton.Shutdown();
			Loader.Load(Loader.Scene.MainMenuScene);
		});
		optionsButton.onClick.AddListener(() =>
		{
			Hide();
			OptionsUI.Instance.Show(Show);
		});
	}

	private void Start()
	{
		KitchenGameManager.Instance.OnLocalGamePaused += KitchenLocalGameManagerOnLocalGamePaused;
		KitchenGameManager.Instance.OnLocalGameUnpaused += KitchenLocalGameManagerOnLocalGameUnpaused;

		Hide();
	}

	private void KitchenLocalGameManagerOnLocalGameUnpaused(object sender, System.EventArgs e) { Hide(); }

	private void KitchenLocalGameManagerOnLocalGamePaused(object sender, System.EventArgs e) { Show(); }

	private void Show()
	{
		gameObject.SetActive(true);
		resumeButton.Select();
	}

	private void Hide() { gameObject.SetActive(false); }
	private void OnDestroy()
	{
		if (KitchenGameManager.Instance)
		{
			KitchenGameManager.Instance.OnLocalGamePaused -= KitchenLocalGameManagerOnLocalGamePaused;
			KitchenGameManager.Instance.OnLocalGameUnpaused -= KitchenLocalGameManagerOnLocalGameUnpaused;
		}
	}
}
