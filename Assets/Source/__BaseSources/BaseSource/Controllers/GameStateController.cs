using System.Collections.Generic;
using MEC;
using Sirenix.OdinInspector;
using UnityEngine;

public class GameStateController : Singleton<GameStateController>
{
    private readonly ObservedValue<GameStates> gameStatus = new ObservedValue<GameStates>(GameStates.Loading);
    private IList<IGameStateObserver> _gameStatObservers;

    public GameStates currentGameState;

    public override void Initialize()
    {
        base.Initialize();
        SetGameState(GameStates.Loading);
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = -1;
    }

    [Button]
    public void SetGameState(GameStates targetState)
    {
        if (gameStatus.Value == targetState) return;
        ChangeState(targetState);
    }

    public void AddListener(IGameStateObserver gameStateObserver)
    {
        _gameStatObservers ??= new List<IGameStateObserver>();
        if (!_gameStatObservers.Contains(gameStateObserver))
        {
            _gameStatObservers.Add(gameStateObserver);
        }
    }

    private void OnGameStateChanged()
    {
        currentGameState = gameStatus.Value;
        if (_gameStatObservers == null) return;

        for (int i = 0; i < _gameStatObservers.Count; i++)
        {
            if (_gameStatObservers[i] != null) _gameStatObservers[i].OnGameStateChanged();
        }
    }

    private void ChangeState(GameStates targetState)
    {
        if (targetState == GameStates.Win)
        {
            Timing.CallDelayed(2f, () => gameStatus.Value = targetState);
            return;
        }
        gameStatus.Value = targetState;
    }

    #region [ Subscriptions ]

    private void OnEnable()
    {
        gameStatus.OnValueChange += OnGameStateChanged;
    }

    private void OnDisable()
    {
        gameStatus.OnValueChange -= OnGameStateChanged;
    }

    #endregion

    public void OnQuit()
    {
        Application.Quit();
    }
}