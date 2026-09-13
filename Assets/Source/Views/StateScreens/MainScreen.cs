using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MainScreen : ScreenElement
{
    public void StartGame()
    {
        GameStateController.Instance.SetGameState(GameStates.Game);
    }
}
