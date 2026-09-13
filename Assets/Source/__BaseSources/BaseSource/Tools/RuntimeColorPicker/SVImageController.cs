using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SVImageController : ControllerBaseModel, IDragHandler, IPointerClickHandler
{
    [SerializeField] private Image picker;

    [SerializeField] private RawImage svImage;
    [SerializeField] private RectTransform rectTransform, pickerTransform;

    [Button]
    public override void Initialize()
    {
        base.Initialize();

        pickerTransform.anchoredPosition = new Vector2(-(rectTransform.sizeDelta.x * 0.5f), -(rectTransform.sizeDelta.y * 0.5f));
    }

    public void OnDrag(PointerEventData eventData)
    {
        UpdateColor(eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        UpdateColor(eventData);
    }

    void UpdateColor(PointerEventData eventData)
    {
        Vector3 pos = rectTransform.InverseTransformPoint(eventData.position);

        float deltaX = rectTransform.sizeDelta.x * 0.5f;
        float deltaY = rectTransform.sizeDelta.y * 0.5f;

        if (pos.x < -deltaX)
        {
            pos.x = -deltaX;
        }
        else if (pos.x > deltaX)
        {
            pos.x = deltaX;
        }

        if (pos.y < -deltaY)
        {
            pos.y = -deltaY;
        }
        else if (pos.y > deltaY)
        {
            pos.y = deltaY;
        }

        float x = pos.x + deltaX;
        float y = pos.y + deltaY;

        float xNorm = x / rectTransform.sizeDelta.x;
        float yNorm = y / rectTransform.sizeDelta.y;

        pickerTransform.localPosition = pos;
        picker.color = Color.HSVToRGB(0, 0, 1 - yNorm);

        ColorPickerController.Instance.SetSaturationValue(xNorm, yNorm);
    }
}
