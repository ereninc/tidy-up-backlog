using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIRumble : ObjectModel, IPointerEnterHandler, IPointerExitHandler
{
    [Space(12)]
    [Header("GAMEPAD RUMBLE")]
    [SerializeField] private bool hasVibrationEffect;

    [ShowIf("hasVibrationEffect")] public bool vibrationOnEnter;
    [ShowIf("vibrationOnEnter")] public RumbleType vibrationOnEnterType;
    [ShowIf("hasVibrationEffect")] public bool vibrationOnExit;
    [ShowIf("vibrationOnExit")] public RumbleType vibrationOnExitType;

    public void OnPointerEnter(PointerEventData eventData)
    {
        //RUMBLE ON ENTER
        RumbleOnEnter();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        //RUMBLE ON EXIT
        RumbleOnExit();
    }

    #region [ GAMEPAD RUMBLE FUNCTIONS ]

    private void RumbleOnEnter()
    {
        if (vibrationOnEnter)
        {
            RumbleController.Instance.RumbleFeedback(vibrationOnEnterType);
        }
    }
    private void RumbleOnExit()
    {
        if (vibrationOnExit)
        {
            RumbleController.Instance.RumbleFeedback(vibrationOnEnterType);
        }
    }

    #endregion
}
