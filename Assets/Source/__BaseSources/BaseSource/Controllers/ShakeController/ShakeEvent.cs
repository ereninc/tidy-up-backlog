using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ShakeEvent : MonoBehaviour
{
    public UnityEvent shock;
    private void Start()
    {
        //  InvokeRepeating("ShockwaveEvent", 3f,4f);
       // ShockwaveEvent();
    }
    void ShockwaveEvent()
    {
        shock.Invoke();
    }
}
