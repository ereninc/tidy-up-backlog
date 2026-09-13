using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformObject : ObjectModel
{
    private Transform _transform;
    public Transform Transform { get {
            if (!_transform)
                _transform = transform;
            return _transform;
        }
    }
    
    protected virtual void Awake()
    {
        _transform = transform;
    }
}