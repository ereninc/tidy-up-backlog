using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectUI : MonoBehaviour
{
    [SerializeField] private Button btnMainMenu;
    [SerializeField] private Button btnReady;
    
    [SerializeField] private TextMeshProUGUI  lobbyName;
    [SerializeField] private TextMeshProUGUI  lobbyCode;

    private void Awake()
    {
        btnMainMenu.onClick.AddListener(() =>
        {
            GameLobby.Instance.LeaveLobby();
            NetworkManager.Singleton.Shutdown();
            Loader.Load(Loader.Scene.MainMenuScene);
        });
        
        btnReady.onClick.AddListener(() =>
        {
            CharacterSelectController.Instance.SetPlayerReady();
        });
    }

    private void Start()
    {
        var lobby = GameLobby.Instance.GetLobby();
        if (lobby != null)
        {
            lobbyName.text = "Lobby Name : " + lobby.Name;
            lobbyCode.text = "Lobby Code : " + lobby.LobbyCode;
        }
    }
}