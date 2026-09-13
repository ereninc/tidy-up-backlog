using System;
using Sirenix.OdinInspector;
using UnityEngine;

public class CollectionController : ControllerBaseModel
{
    private void OnIncreaseCollection(int increaseAmount, Vector3 worldPosition, int moneyAmount = 1)
    {
        EventController.Invoke_OnCoinUpdated();
    }
}