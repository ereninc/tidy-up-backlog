using UnityEngine;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
	[SerializeField] private Button playMultiplayerButton;
	[SerializeField] private Button playSingleplayerButton;
	[SerializeField] private Button quitButton;

	private void Awake()
	{
		playMultiplayerButton.onClick.RemoveAllListeners();
		playSingleplayerButton.onClick.RemoveAllListeners();
		quitButton.onClick.RemoveAllListeners();
		
		playMultiplayerButton.onClick.AddListener(() =>
		{
			KitchenGameMultiplayer.PlayMultiplayer = true;
			Loader.Load(Loader.Scene.LobbyScene);
		});
		playSingleplayerButton.onClick.AddListener(() =>
		{
			KitchenGameMultiplayer.PlayMultiplayer = false;
			Loader.Load(Loader.Scene.LobbyScene);
		});
		
		quitButton.onClick.AddListener(Application.Quit);
		Time.timeScale = 1f;
	}
}