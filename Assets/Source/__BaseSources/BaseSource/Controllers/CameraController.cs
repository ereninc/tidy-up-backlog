using System;
using UnityEngine;
using Cinemachine;
using Sirenix.OdinInspector;

public class CameraController : Singleton<CameraController>, IGameStateObserver
{
    [SerializeField] private CinemachineVirtualCamera startCamera, gameplayCamera, finalCamera;
    [SerializeField] private CinemachineVirtualCamera activeCamera;

    public Camera main;
    public Camera uiCamera;

    public override void Initialize()
    {
        base.Initialize();
        AddToGameObserverList();
        ChangeCamera(CameraType.Gameplay);
    }

    [Button]
    public void ChangeCamera(CameraType type)
    {
        if (activeCamera != null)
        {
            activeCamera.SetActiveGameObject(false);
        }

        switch (type)
        {
            case CameraType.Start:
                startCamera.SetActiveGameObject(true);
                activeCamera = startCamera;
                break;
            case CameraType.Gameplay:
                gameplayCamera.SetActiveGameObject(true);
                activeCamera = gameplayCamera;
                break;
            case CameraType.Final:
                finalCamera.SetActiveGameObject(true);
                activeCamera = finalCamera;
                break;
        }
    }

    public void AddToGameObserverList()
    {
        GameController.AddListener(this);
    }

    public void OnGameStateChanged()
    {
        switch (GameController.currentGameState)
        {
            case GameStates.Game:
                ChangeCamera(CameraType.Gameplay);
                break;
            case GameStates.Win:
                ChangeCamera(CameraType.Final);
                break;
            case GameStates.Lose:
                ChangeCamera(CameraType.Final);
                break;
        }
    }
}

public enum CameraType
{
    Start,
    Gameplay,
    Final
}