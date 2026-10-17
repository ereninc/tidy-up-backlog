#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using EXW.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Used only by the explicit isolated Play Mode fixture; never registers itself.
internal static class StoreComputerUiInputValidation
{
    internal static IEnumerator Run(NetworkStoreComputer pc,
        StoreComputerDesktopPresenter desktop, Action<bool, string> check)
    {
        Gamepad gamepad = null;
        Mouse mouse = null;
        Keyboard keyboard = null;
        NetworkPlayerInputReader reader = NetworkPlayerInputReader.Local;
        int pauseEvents = 0;
        Action countPause = () => pauseEvents++;

        try
        {
            check(pc.IsLocalUser && pc.ActiveApp == StoreComputerApp.Desktop &&
                  desktop.CanRouteInput && reader != null,
                "UI input fixture begins with the reserved local desktop");
            reader.PausePressed += countPause;
            gamepad = InputSystem.AddDevice<Gamepad>("PCValidation_VirtualGamepad");
            mouse = InputSystem.AddDevice<Mouse>("PCValidation_VirtualMouse");
            keyboard = InputSystem.AddDevice<Keyboard>("PCValidation_VirtualKeyboard");
            var references = new SerializedObject(desktop);
            var upgrades = (Button)references.FindProperty("upgradesButton").objectReferenceValue;
            var info = (Button)references.FindProperty("storeInfoButton").objectReferenceValue;
            var uiCamera = (Camera)references.FindProperty("uiCamera").objectReferenceValue;
            var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            int nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            check(EventSystem.current.currentSelectedGameObject == upgrades.gameObject,
                "Desktop has the Upgrades default selection for gamepad Submit");

            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
            nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            nextFrame = Time.frameCount + 2;
            while (Time.frameCount < nextFrame) yield return null;
            check(pc.ActiveApp == StoreComputerApp.Upgrades && pc.IsLocalUser,
                "Generated gamepad South reaches the existing UI module and opens Upgrades");

            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.East));
            nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            nextFrame = Time.frameCount + 2;
            while (Time.frameCount < nextFrame) yield return null;
            check(pc.ActiveApp == StoreComputerApp.Desktop && pc.IsLocalUser,
                "Generated gamepad East closes the app without leaving the computer");

            Canvas.ForceUpdateCanvases();
            Vector2 texturePoint = RectTransformUtility.WorldToScreenPoint(uiCamera,
                info.transform.TransformPoint(((RectTransform)info.transform).rect.center));
            RawImage image = desktop.FullscreenImage;
            Rect rect = image.rectTransform.rect;
            Vector2 uv = texturePoint / (Vector2)desktop.TextureResolution;
            Vector2 localPoint = rect.min + Vector2.Scale(uv, rect.size);
            Camera fullscreenCamera = image.canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : image.canvas.worldCamera;
            Vector2 pointer = RectTransformUtility.WorldToScreenPoint(fullscreenCamera,
                image.rectTransform.TransformPoint(localPoint));
            check(ComputerDesktopRaycaster.TryMapPointer(image, pointer,
                      desktop.TextureResolution, out Vector2 mapped) &&
                  Vector2.Distance(mapped, texturePoint) < 1f,
                "Store Info button center maps through the real fullscreen RectTransform to RT pixels");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            int currentPointer = (int)typeof(InputSystemUIInputModule)
                .GetField("m_CurrentPointerId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            GameObject hovered = module.GetLastRaycastResult(mouse.deviceId).gameObject;
            GameObject unifiedHovered = module.GetLastRaycastResult(currentPointer).gameObject;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
                { position = pointer, pointerId = mouse.deviceId }, hits);
            var debug = new StringBuilder("DEBUG PC pointer: generated=").Append(pointer)
                .Append(" mapped=").Append(mapped).Append(" textureTarget=").Append(texturePoint)
                .Append(" mouseId=").Append(mouse.deviceId).Append(" currentId=").Append(currentPointer)
                .Append(" lastVirtual=").Append(hovered != null ? hovered.name : "null")
                .Append(" lastUnified=").Append(unifiedHovered != null ? unifiedHovered.name : "null")
                .Append(" actionPoint=").Append(module.point.action.ReadValue<Vector2>())
                .Append(" activeControl=").Append(module.point.action.activeControl?.path)
                .Append(" uiPixelRect=").Append(uiCamera.pixelRect).Append(" uiAspect=").Append(uiCamera.aspect)
                .Append(" canvasRect=").Append(((RectTransform)info.GetComponentInParent<Canvas>().transform).rect)
                .Append(" canvasScale=").Append(info.GetComponentInParent<Canvas>().scaleFactor)
                .Append(" graphicDepth=").Append(info.image.depth).Append(" cull=").Append(info.image.canvasRenderer.cull)
                .Append(" route=").Append(desktop.CanRouteInput).Append(" cursor=").Append(Cursor.lockState)
                .Append(" visible=").Append(Cursor.visible).Append(" appFocus=").Append(Application.isFocused)
                .Append(" editorFocus=").Append(EditorWindow.focusedWindow != null ? EditorWindow.focusedWindow.GetType().Name : "null")
                .Append(" directHits=");
            foreach (RaycastResult hit in hits)
                debug.Append(hit.gameObject.name).Append('@').Append(hit.module.GetType().Name).Append(';');
            foreach (InputDevice device in InputSystem.devices)
                if (device is Mouse)
                    debug.Append(" mouse[").Append(device.deviceId).Append("]=")
                        .Append(module.GetLastRaycastResult(device.deviceId).gameObject?.name ?? "null");
            check(true, debug.ToString());
            check((hovered != null && hovered.GetComponentInParent<Button>() == info) ||
                  (unifiedHovered != null && unifiedHovered.GetComponentInParent<Button>() == info),
                "Generated mouse movement produces Store Info hover through the real UI module");

            InputSystem.QueueStateEvent(mouse,
                new MouseState { position = pointer }.WithButton(MouseButton.Left));
            nextFrame = Time.frameCount + 2;
            while (Time.frameCount < nextFrame) yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = pointer });
            nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            check(pc.ActiveApp == StoreComputerApp.StoreInfo && pc.IsLocalUser,
                "Generated mouse press/release opens Store Info through RenderTexture pointer routing");

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape));
            nextFrame = Time.frameCount + 3;
            while (Time.frameCount < nextFrame) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            nextFrame = Time.frameCount + 2;
            while (Time.frameCount < nextFrame) yield return null;
            check(pc.ActiveApp == StoreComputerApp.Desktop && pc.IsLocalUser,
                "Generated Escape returns from Store Info to desktop without releasing the session");
            check(pauseEvents == 0,
                "The same generated Escape never reaches the gameplay PausePressed event");
        }
        finally
        {
            if (reader != null) reader.PausePressed -= countPause;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
        }
    }
}
#endif
