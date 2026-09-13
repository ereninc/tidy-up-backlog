using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TooltipController : Singleton<TooltipController>
{
	// [SerializeField] private TooltipModel tooltipPrefab;
	// private TooltipModel tooltip;
	//
	// public void ShowTooltip(string title, string description, Transform anchor, bool followMouse = false)
	// {
	// 	if (tooltip == null)
	// 	{
	// 		tooltip = Instantiate(tooltipPrefab, transform).GetComponent<TooltipModel>();
	// 	}
 //        
	// 	tooltip.SetTooltip(title, description, anchor, followMouse);
	// 	tooltip.gameObject.SetActive(true);
	// }
	//
	// public void HideTooltip()
	// {
	// 	if (tooltip != null)
	// 	{
	// 		tooltip.gameObject.SetActive(false);
	// 	}
	// }
}

// public class TooltipModel : UIElement
// {
// 	[SerializeField] private TextMeshProUGUI titleText;
// 	[SerializeField] private TextMeshProUGUI descriptionText;
// 	[SerializeField] private RectTransform backgroundRect;
// 	[SerializeField] private CanvasGroup canvasGroup;
// 	[SerializeField] private float maxWidth = 400f;
//     
// 	private bool followMouse;
// 	private Transform anchor;
//
// 	public void SetTooltip(string title, string description, Transform anchor, bool followMouse)
// 	{
// 		this.anchor = anchor;
// 		this.followMouse = followMouse;
//
// 		titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
// 		titleText.text = title;
// 		descriptionText.text = description;
//         
// 		LayoutRebuilder.ForceRebuildLayoutImmediate(backgroundRect);
// 		AdjustAnchor();
// 	}
//
// 	private void Update()
// 	{
// 		if (followMouse)
// 		{
// 			transform.position = Input.mousePosition;
// 		}
// 		else if (anchor != null)
// 		{
// 			transform.position = anchor.position;
// 		}
// 	}
//
// 	private void AdjustAnchor()
// 	{
// 		Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(null, anchor.position);
// 		Vector2 pivot = new Vector2(0.5f, 0.5f);
//         
// 		if (screenPos.x > Screen.width * 0.75f)
// 		{
// 			pivot.x = 1f;
// 		}
// 		else if (screenPos.x < Screen.width * 0.25f)
// 		{
// 			pivot.x = 0f;
// 		}
//
// 		if (screenPos.y > Screen.height * 0.75f)
// 		{
// 			pivot.y = 1f;
// 		}
// 		else if (screenPos.y < Screen.height * 0.25f)
// 		{
// 			pivot.y = 0f;
// 		}
//         
// 		backgroundRect.pivot = pivot;
// 	}
// }

// public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
// {
// 	[TextArea]
// 	public string tooltipText;
// 	public string tooltipTitle;
//  //USE INT'S TO GET LOCALIZED TEXTS NOT STRINGS
// 	public bool followMouse;
//
// 	public void OnPointerEnter(PointerEventData eventData)
// 	{
// 		TooltipManager.Instance.ShowTooltip(tooltipTitle, tooltipText, transform, followMouse);
// 	}
//
// 	public void OnPointerExit(PointerEventData eventData)
// 	{
// 		TooltipManager.Instance.HideTooltip();
// 	}
// }