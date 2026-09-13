using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Singleton<T> : ControllerBaseModel where T : MonoBehaviour
{
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                instance = (T)FindObjectOfType(typeof(T));

                if (instance == null)
                {
                    Debug.Log("Instance = null");
                }
            }

            return instance;
        }
    }

    private static T instance;
}