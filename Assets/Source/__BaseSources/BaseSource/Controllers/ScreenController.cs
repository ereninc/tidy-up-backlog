using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class ScreenController : Singleton<ScreenController>, IGameStateObserver
{
    [SerializeField] private List<ScreenElement> screens;
    [SerializeField] private ScreenElement activeScreen;

    public override void Initialize()
    {
        base.Initialize();
        AddToGameObserverList();

        foreach (var item in screens)
        {
            item.Initialize();
            item.SetActiveGameObject(false);
        }
        activeScreen.Show();
    }

    [Button]
    public void ChangeScreen(bool showAfterHide, int index)
    {
        ScreenElement nextScreen = GetScreen<ScreenElement>(index);

        if (showAfterHide)
        {
            if (activeScreen != null)
            {
                activeScreen.Hide(nextScreen.Show);
                activeScreen = nextScreen;
            }
            else
            {
                activeScreen = nextScreen;
                activeScreen.Show();
                activeScreen.Initialize();
            }
        }
        else
        {
            activeScreen.Hide();
            activeScreen = nextScreen;
            activeScreen.Show();
            activeScreen.Initialize();
        }
    }

    private T GetScreen<T>(int index)
    {
        return (T)(object)screens[index];
    }

    public void AddToGameObserverList()
    {
        GameController.AddListener(this);
        Debug.Log("ScreenController is now GameStateListener");
    }

    public void OnGameStateChanged()
    {
        switch (GameController.currentGameState)
        {
            case GameStates.Loading:
                ChangeScreen(false, 0);
                break;
            case GameStates.Main:
                ChangeScreen(true, 1);
                break;
            case GameStates.Game:
                ChangeScreen(true, 2);
                break;
            case GameStates.Win:
                ChangeScreen(true, 3);
                break;
            case GameStates.Lose:
                ChangeScreen(true, 4);
                break;
        }
    }
}
