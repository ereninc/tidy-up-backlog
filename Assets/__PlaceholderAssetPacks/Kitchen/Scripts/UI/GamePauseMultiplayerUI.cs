using System;
using UnityEngine;

public class GamePauseMultiplayerUI : MonoBehaviour
{
    private void Start()
    {
        KitchenGameManager.Instance.OnMultiplayerGamePaused += Instance_OnMultiplayerGamePaused;
        KitchenGameManager.Instance.OnMultiplayerGameUnpaused += Instance_OnMultiplayerGameUnpaused;
        Hide();
    }

    private void Instance_OnMultiplayerGamePaused(object sender, EventArgs e)
    {
        Show();
    }

    private void Instance_OnMultiplayerGameUnpaused(object sender, EventArgs e)
    {
        Hide();
    }

    private void Show()
    {
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        gameObject.SetActive(false);
    }
}