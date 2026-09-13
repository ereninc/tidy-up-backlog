using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateLobbyUI : MonoBehaviour
{
    [SerializeField] private TMP_InputField lobbyName;
    [SerializeField] private Button btnPublic;
    [SerializeField] private Button btnPrivate;
     
    [SerializeField] private Button btnClose;

    private void Awake()
    {
        btnPublic.onClick.AddListener(() =>
        {
            GameLobby.Instance.CreateLobby(lobbyName.text, false);
        });
        
        btnPrivate.onClick.AddListener(() =>
        {
            GameLobby.Instance.CreateLobby(lobbyName.text, true);
        });
        
        btnClose.onClick.AddListener(Hide);
    }

    private void Start() { Hide(); }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}