using UnityEngine;
using DG.Tweening;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public static class UIHelper
{
    public static bool IsPointerOverUIObject
    {
        get
        {
            PointerEventData eventDataCurrentPosition = new PointerEventData(EventSystem.current);
            eventDataCurrentPosition.position = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventDataCurrentPosition, results);
            return results.Count > 0;
        }
    }

    //Bottom but like small 12.5% of the screen
    public static bool IsPointerOverTheBottom
    {
        get
        {
            var y = Input.mousePosition.y;
            var screenHeight = Screen.height;
            return y > screenHeight / 8f;
        }
    }
    
    //Bottom half 50% of the screen
    public static bool IsPointerOnTheBottomHalf
    {
        get
        {
            var y = Input.mousePosition.y;
            var screenHeight = Screen.height;
            return y < screenHeight / 2f;
        }
    }
    
    //Upper half 50% of the screen
    public static bool IsPointerOnTheUpperHalf
    {
        get
        {
            var y = Input.mousePosition.y;
            var screenHeight = Screen.height;
            return y > screenHeight / 2f;
        }
    }
}