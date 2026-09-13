// using System;
// using System.Collections;
// using System.Collections.Generic;
// using DG.Tweening;
// using UnityEngine;
// using UnityEngine.UI;
// using UnityEngine.EventSystems;
// using UnityEngine.InputSystem;
//
// public class MenuEventSystemHandler : MonoBehaviour
// {
// 	[Header("References")]
// 	public List<Selectable> selectables = new List<Selectable>();
//
// 	[Header("Animations")]
// 	[SerializeField] private float selectedAnimationScale = 1.2f;
// 	[SerializeField] private float animationDuration = 0.3f;
// 	[SerializeField] private List<GameObject> animationExclusions;
//
// 	private Dictionary<Selectable, Vector3> _scales = new Dictionary<Selectable, Vector3>();
// 	private Tween _scaleUpTween;
// 	private Tween _scaleDownTween;
//
// 	[SerializeField] private Selectable firstSelected;
// 	[SerializeField] private Selectable lastSelected;
//
// 	[Header("Controls")]
// 	[SerializeField] private InputActionReference navigateReference;
//
// 	public virtual void Awake()
// 	{
// 		foreach (var selectable in selectables)
// 		{
// 			AddSelectionListeners(selectable);
// 			_scales.Add(selectable, selectable.transform.localScale);
// 		}
// 	}
//
// 	public virtual void OnEnable()
// 	{
// 		SetFirstSelected();
// 		navigateReference.action.performed += OnNavigate;
//
// 		//reset all the selectables to default scale
// 		foreach (var selectable in selectables)
// 		{
// 			selectable.transform.localScale = _scales[selectable];
// 		}
//
// 		StartCoroutine(SelectAfterDelay());
// 	}
//
// 	public virtual void OnDisable()
// 	{
// 		navigateReference.action.performed -= OnNavigate;
//
// 		_scaleUpTween?.Kill(true);
// 		_scaleDownTween?.Kill(true);
// 	}
//
// 	protected virtual IEnumerator SelectAfterDelay()
// 	{
// 		yield return null;
// 		EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
// 	}
//
// 	private void AddSelectionListeners(Selectable selectable)
// 	{
// 		if (!selectable.gameObject.TryGetComponent<EventTrigger>(out var trigger))
// 		{
// 			trigger = selectable.gameObject.AddComponent<EventTrigger>();
// 		}
//
// 		AddTriggerEvent(trigger, EventTriggerType.Select, OnSelect);
// 		AddTriggerEvent(trigger, EventTriggerType.Deselect, OnDeselect);
// 		AddTriggerEvent(trigger, EventTriggerType.PointerEnter, OnPointerEnter);
// 		AddTriggerEvent(trigger, EventTriggerType.PointerExit, OnPointerExit);
// 	}
//
// 	private void AddTriggerEvent(EventTrigger trigger, EventTriggerType eventType, Action<BaseEventData> callback)
// 	{
// 		EventTrigger.Entry entry = new EventTrigger.Entry
// 		{
// 			eventID = eventType
// 		};
// 		entry.callback.AddListener(eventData => callback(eventData));
// 		trigger.triggers.Add(entry);
// 	}
//
// 	private void SetFirstSelected()
// 	{
// 		if (firstSelected == null && selectables.Count > 0)
// 		{
// 			firstSelected = selectables[0];
// 		}
// 	}
//
// 	#region [ OnSelect / OnDeselect ]
//
// 	private void OnSelect(BaseEventData eventData)
// 	{
// 		if (eventData.selectedObject == null) return;
// 		lastSelected = eventData.selectedObject.GetComponent<Selectable>();
//
// 		if (animationExclusions.Contains(eventData.selectedObject.gameObject)) return;
//
// 		Vector3 newScale = eventData.selectedObject.transform.localScale * selectedAnimationScale;
// 		_scaleUpTween = eventData.selectedObject.transform.DOScale(newScale, animationDuration);
// 	}
//
// 	private void OnDeselect(BaseEventData eventData)
// 	{
// 		if (animationExclusions.Contains(eventData.selectedObject.gameObject)) return;
//
// 		Selectable selectable = eventData.selectedObject.GetComponent<Selectable>();
// 		_scaleDownTween = eventData.selectedObject.transform.DOScale(_scales[selectable], animationDuration);
// 	}
//
// 	#endregion
//
// 	#region [ OnPointerEnter / OnPointerExit ]
//
// 	private void OnPointerEnter(BaseEventData eventData)
// 	{
// 		if (InputController.Instance.activeGameDevice == InputController.GameDevice.Gamepad) return;
//
// 		if (!(eventData is PointerEventData pointerEventData)) return;
//
// 		Selectable selectable = pointerEventData.pointerEnter?.GetComponentInParent<Selectable>() ??
// 			pointerEventData.pointerEnter?.GetComponentInChildren<Selectable>();
//
// 		if (selectable != null)
// 		{
// 			EventSystem.current.SetSelectedGameObject(selectable.gameObject);
// 		}
// 	}
//
// 	private void OnPointerExit(BaseEventData eventData)
// 	{
// 		if (InputController.Instance.activeGameDevice == InputController.GameDevice.Gamepad) return;
// 		
// 		PointerEventData pointerEventData = eventData as PointerEventData;
// 		if (pointerEventData == null) return;
//
// 		pointerEventData.selectedObject = null;
// 	}
//
// 	#endregion
//
// 	protected virtual void OnNavigate(InputAction.CallbackContext context)
// 	{
// 		if (EventSystem.current.currentSelectedGameObject == null && lastSelected != null)
// 		{
// 			EventSystem.current.SetSelectedGameObject(lastSelected.gameObject);
// 		}
// 	}
// }