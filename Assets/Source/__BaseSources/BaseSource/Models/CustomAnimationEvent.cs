using UnityEngine;
using UnityEngine.Events;

public class CustomAnimationEvent : MonoBehaviour
{
    [System.Serializable]
    public class TrigEvent : UnityEvent { }

    public TrigEvent OnTrig;

    public void Trigger()
    {
        OnTrig.Invoke();
    }
}