using UnityEngine;

public class FollowTransform : TransformObject
{
    [SerializeField] private Transform targetTransform;

    public void SetTargetTransform(Transform target)
    {
        targetTransform = target;
    }

    private void LateUpdate()
    {
        if (!targetTransform) return;
        
        Transform.position = targetTransform.position;
        Transform.rotation = targetTransform.rotation;
    }
}