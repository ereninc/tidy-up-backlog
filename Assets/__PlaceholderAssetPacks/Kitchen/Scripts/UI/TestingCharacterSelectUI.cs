using System;
using UnityEngine;
using UnityEngine.UI;

public class TestingCharacterSelectUI : MonoBehaviour
{
    [SerializeField] private Button btnReady;

    private void Awake()
    {
        btnReady.onClick.AddListener(OnReadyButtonClicked);
    }

    private void OnReadyButtonClicked()
    {
        CharacterSelectController.Instance.SetPlayerReady();
    }

    private void OnDestroy()
    {
        btnReady.onClick.RemoveAllListeners();
    }
}