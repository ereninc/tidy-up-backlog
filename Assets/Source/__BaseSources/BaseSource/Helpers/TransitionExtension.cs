using UnityEngine;

public static class TransitionExtension
{
    public static Vector3 UIToWorldPosition(RectTransform targetRect, Camera uiCamera, Vector3 offset)
    {
        Vector3 worldPosition = uiCamera.ScreenToWorldPoint(targetRect.position);
        return worldPosition + offset;
    }
}