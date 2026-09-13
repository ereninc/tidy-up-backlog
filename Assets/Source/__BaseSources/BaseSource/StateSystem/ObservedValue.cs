using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObservedValue<T>
{
    private T currentValue;
    public event Action OnValueChange;

    public ObservedValue(T initialValue)
    {
        currentValue = initialValue;
    }

    public T Value
    {
        get => currentValue;
        set
        {
            if (currentValue.Equals(value)) return;
            currentValue = value;
            OnValueChange?.Invoke();
        }
    }
}