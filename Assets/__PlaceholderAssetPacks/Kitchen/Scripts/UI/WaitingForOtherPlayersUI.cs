using System;
using UnityEngine;

public class WaitingForOtherPlayersUI : MonoBehaviour
{
    private void Start()
    {
        KitchenGameManager.Instance.OnLocalPlayerReadyChanged += InstanceOnOnLocalPlayerReadyChanged;
        KitchenGameManager.Instance.OnStateChanged += InstanceOnOnStateChanged;
        Hide();
    }
    
    private void InstanceOnOnStateChanged(object sender, EventArgs e)
    {
        if (KitchenGameManager.Instance.IsCountdownToStartActive())
        {
            Hide();
        }
    }

    private void InstanceOnOnLocalPlayerReadyChanged(object sender, EventArgs e)
    {
        if (KitchenGameManager.Instance.IsLocalPlayerReady())
        {
            Show();
        }
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