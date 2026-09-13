using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using MEC;
using UnityEngine;
using Newtonsoft.Json;
using Sirenix.OdinInspector;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class LevelController : Singleton<LevelController>
{
    // [Header("Folders/Prefixes")] private readonly string _levelsFolder = "Levels";
    // [ShowInInspector] private int _levelCount = 0;

    // [SerializeField] private int loopLevelStartIndex;
    // [SerializeField] private MasterLevelDataSO masterLevelData;
    // [SerializeField] private LevelModel levelPrefab;
    // [ShowInInspector] private int _currentIndex = 0;

    // [ShowInInspector] public LevelModel ActiveLevel { get; private set; }

    // public override void Initialize()
    // {
    //     base.Initialize();
    //     LevelModel spawnedLevel = Instantiate(levelPrefab);
    //     ActiveLevel = spawnedLevel;
    //     ActiveLevel.SetActiveGameObject(true);
    //     LoadLevel();
    // }

    // private void LoadLevel()
    // {
    //     _levelCount = masterLevelData.levelData.Count;
    //     _currentIndex = UserPrefs.GetCurrentLevel();
    //     ActiveLevel.Initialize(masterLevelData.levelData[_currentIndex]);
    // }

    // public void NextLevel()
    // {
    //     int level = UserPrefs.GetCurrentLevel();
    //     if (level < _levelCount)
    //     {
    //         level++;
    //         UserPrefs.SetLevel(level);
    //     }
    //     else
    //     {
    //         level = loopLevelStartIndex;
    //         UserPrefs.SetLevel(level);
    //     }

    //     GameController.SetGameState(GameStates.Game);
    //     LoadLevel();
    //     EventController.Invoke_OnLevelCompleted();
    //     // TargetProductController.Instance.SetTargetSlots();
    // }
}