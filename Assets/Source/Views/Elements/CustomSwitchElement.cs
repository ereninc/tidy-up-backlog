using DG.Tweening;
using Sirenix.OdinInspector;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class CustomSwitchElement : UIElement
{
    [Header("SETTINGS")]
    public bool isOn = true;
    public bool invokeAtStart = true;

    public UnityEvent OnEvents;
    public UnityEvent OffEvents;

    [Header("UI References")]
    [SerializeField] private Image bg, handlerImage;
    [SerializeField] private RectTransform handler;
    [SerializeField] private Color activeColor, deactiveColor;
    [SerializeField] private Color handlerActiveColor;

    private bool isInitialized = false;

    public void Initialize(bool defaultState)
    {
        isOn = defaultState;
        SetVisualInstant();
        
        if (invokeAtStart)
        {
            if (isOn) OnEvents?.Invoke();
            else OffEvents?.Invoke();
        }

        isInitialized = true;
    }

    public void ToggleSwitch()
    {
        SetState(!isOn);
    }

    public void SetState(bool state, bool animate = true)
    {
        if (!isInitialized) Initialize(state);

        isOn = state;
        
        if (animate) AnimateSwitch();
        else SetVisualInstant();

        if (isOn) OnEvents?.Invoke();
        else OffEvents?.Invoke();
    }

    private void SetVisualInstant()
    {
        bg.color = isOn ? activeColor : deactiveColor;
        handlerImage.color = isOn ? handlerActiveColor : Color.white;
        handler.anchorMin = handler.anchorMax = isOn ? Vector2.one : Vector2.zero;
        handler.anchoredPosition = isOn ? new Vector2(-7.5f, -18f) : new Vector2(5f, 18f);
        handler.pivot = isOn ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
    }

    private void AnimateSwitch()
    {
        bg.DOColor(isOn ? activeColor : deactiveColor, 0.15f);
        handlerImage.DOColor(isOn ? handlerActiveColor : Color.white, 0.15f);
        handler.DOAnchorMax(isOn ? Vector2.one : Vector2.zero, 0.15f);
        handler.DOAnchorMin(isOn ? Vector2.one : Vector2.zero, 0.15f);
        handler.DOAnchorPos(isOn ? new Vector2(-7.5f, -18f) : new Vector2(5f, 18f), 0.15f);
        handler.DOPivot(isOn ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f), 0.15f);
    }
}