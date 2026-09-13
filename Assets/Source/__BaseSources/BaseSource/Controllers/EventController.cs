using System;
using UnityEngine;

public static class EventController
{
    #region [ Collections ]

    public static Action OnCoinUpdated;
    public static void Invoke_OnCoinUpdated()
    {
        OnCoinUpdated?.Invoke();
    }

    #endregion
    
    #region [ Level ]
    
    public static Action OnLevelCompleted;
    public static void Invoke_OnLevelCompleted()
    {
        OnLevelCompleted?.Invoke();
    }
    
    #endregion
}