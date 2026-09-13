using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectModel : MonoBehaviour
{
    public virtual void Initialize() { }

    protected void SetActive()
    {
        gameObject.SetActive(true);
    }

    protected void SetDeactive()
    {
        gameObject.SetActive(false);
    }
}