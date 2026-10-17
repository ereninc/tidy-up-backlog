using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Computer/Desktop Render Texture Raycaster")]
    public sealed class ComputerDesktopRaycaster : GraphicRaycaster
    {
        [SerializeField] private StoreComputerDesktopPresenter presenter;

        public override int sortOrderPriority => 30000;

        public override void Raycast(
            PointerEventData eventData,
            List<RaycastResult> resultAppendList)
        {
            if (presenter == null || !presenter.CanRouteInput ||
                !TryMapPointer(
                    presenter.FullscreenImage,
                    eventData.position,
                    presenter.TextureResolution,
                    out Vector2 texturePoint))
            {
                return;
            }

            // The existing input module still owns hover, press, release and Submit.
            // Only this canvas's hit test uses coordinates in the rendered texture.
            Vector2 screenPoint = eventData.position;

            try
            {
                eventData.position = texturePoint;
                base.Raycast(eventData, resultAppendList);
            }
            finally
            {
                eventData.position = screenPoint;
            }
        }

        public static bool TryMapPointer(
            RawImage image,
            Vector2 screenPoint,
            Vector2Int textureResolution,
            out Vector2 texturePoint)
        {
            texturePoint = default;

            if (image == null || !image.gameObject.activeInHierarchy ||
                textureResolution.x <= 0 || textureResolution.y <= 0)
            {
                return false;
            }

            Canvas canvas = image.canvas;
            Camera eventCamera = canvas != null &&
                                 canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            RectTransform rectTransform = image.rectTransform;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    rectTransform, screenPoint, eventCamera, out Vector2 local))
            {
                return false;
            }

            Rect rect = rectTransform.rect;

            if (rect.width <= 0f || rect.height <= 0f || !rect.Contains(local))
            {
                return false;
            }

            Rect uv = image.uvRect;
            float u = uv.x + (local.x - rect.xMin) / rect.width * uv.width;
            float v = uv.y + (local.y - rect.yMin) / rect.height * uv.height;
            texturePoint = new Vector2(
                u * textureResolution.x,
                v * textureResolution.y);
            return true;
        }
    }
}
