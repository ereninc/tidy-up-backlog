using System;
using System.Collections;
using EXW.UI.Navigation;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace EXW.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Computer/Desktop Presenter")]
    public sealed class StoreComputerDesktopPresenter : MonoBehaviour
    {
        [BoxGroup("Computer"), Required]
        [SerializeField] private NetworkStoreComputer computer;
        [BoxGroup("World Screen"), Required]
        [SerializeField] private Renderer screenRenderer;
        [BoxGroup("World Screen"), Min(0)]
        [SerializeField] private int screenMaterialIndex;
        [BoxGroup("Desktop Render"), Required]
        [SerializeField] private Camera uiCamera;
        [BoxGroup("Desktop Render"), Required]
        [SerializeField] private Canvas desktopCanvas;
        [BoxGroup("Desktop Render"), Required]
        [SerializeField] private ComputerDesktopRaycaster desktopRaycaster;
        [BoxGroup("Desktop Render")]
        [SerializeField] private Vector2Int textureResolution = new Vector2Int(1280, 720);
        [BoxGroup("Desktop Render"), Required]
        [SerializeField] private UIPanelFocus panelFocus;
        [BoxGroup("Desktop Render")]
        [SerializeField] private UIFocusCoordinator focusCoordinator;
        [BoxGroup("Panels"), Required]
        [SerializeField] private GameObject desktopPanel;
        [BoxGroup("Panels"), Required]
        [SerializeField] private GameObject upgradesPanel;
        [BoxGroup("Panels"), Required]
        [SerializeField] private GameObject storeInfoPanel;
        [BoxGroup("Buttons"), Required]
        [SerializeField] private Button upgradesButton;
        [BoxGroup("Buttons"), Required]
        [SerializeField] private Button storeInfoButton;
        [BoxGroup("Buttons"), Required]
        [SerializeField] private Button upgradesCloseButton;
        [BoxGroup("Buttons"), Required]
        [SerializeField] private Button storeInfoCloseButton;
        [BoxGroup("Buttons"), Required]
        [SerializeField] private Button leaveButton;
        [BoxGroup("Fullscreen"), Required]
        [SerializeField] private GameObject fullscreenRoot;
        [BoxGroup("Fullscreen"), Required]
        [SerializeField] private RawImage fullscreenImage;
        [BoxGroup("Fullscreen"), Required]
        [SerializeField] private CanvasGroup fullscreenMatte;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int SobelUiRectId = Shader.PropertyToID("_EXW_SobelUiRect");
        private static readonly int SobelUiOpacityId = Shader.PropertyToID("_EXW_SobelUiOpacity");
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();
        private readonly UniversalRenderPipeline.SingleCameraRequest _renderRequest =
            new UniversalRenderPipeline.SingleCameraRequest();

        private RenderTexture _texture;
        private MaterialPropertyBlock _previousScreenProperties;
        private MaterialPropertyBlock _screenProperties;
        private Coroutine _renderRoutine;
        private StoreComputerApp _shownApp;
        private bool _hasShownApp;
        private bool _localUse;
        private bool _inputEnabled;
        private bool _dirty;
        private bool _isRendering;
        private bool _bound;
        private bool _textureBound;
        private int _textureSettleFrames;
        private uint _localSessionToken;
        private NetworkPlayerInputReader _inputReader;
        private InputSystemUIInputModule _inputModule;
        private InputActionReference[] _previousActions;
        private InputActionReference[] _desktopActions;
        private Canvas _fullscreenCanvas;
        private RenderMode _previousFullscreenMode;
        private Camera _previousFullscreenCamera;
        private float _previousFullscreenDistance;
        private float _presentationBlend;
        private Rect _worldScreenRect;
        private Rect _displayedScreenRect;
        private bool _ownsSobelUiMask;
        private Vector2Int _displaySize;

        public event Action LeaveRequested;

        public RawImage FullscreenImage => fullscreenImage;
        public Vector2Int TextureResolution => _texture != null
            ? new Vector2Int(_texture.width, _texture.height) : textureResolution;
        public RenderTexture DesktopTexture => _texture;
        public bool CanRouteInput => _localUse && _inputEnabled &&
            computer != null && computer.IsLocalUser &&
            computer.SessionToken == _localSessionToken &&
            panelFocus != null && panelFocus.IsReady &&
            (focusCoordinator == null || focusCoordinator.IsTop(panelFocus));

        private void OnEnable()
        {
            if (!ValidateSetup())
            {
                return;
            }

            uiCamera.enabled = false;
            uiCamera.rect = new Rect(0f, 0f, 1f, 1f);
            uiCamera.allowHDR = false;
            uiCamera.allowMSAA = false;
            if (uiCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            {
                cameraData.renderPostProcessing = false;
            }
            desktopRaycaster.enabled = false;
            fullscreenImage.raycastTarget = false;
            fullscreenRoot.SetActive(false);
            panelFocus.NotifyHidden();
            BindEvents();
            CaptureScreenProperties();
            EnsureTexture();
            _hasShownApp = false;
            ApplyApplication(computer.ActiveApp);
            RequestRedraw();
        }

        private void OnDisable()
        {
            bool wasInUse = _localUse;
            EndLocalUse();
            if (wasInUse) LeaveRequested?.Invoke();
            UnbindEvents();

            if (_renderRoutine != null)
            {
                StopCoroutine(_renderRoutine);
                _renderRoutine = null;
            }

            ReleaseTexture();
        }

        public bool BeginLocalUse(NetworkPlayerInputReader inputReader)
        {
            if (_localUse || !isActiveAndEnabled || !_bound || inputReader == null)
            {
                return false;
            }

            if (focusCoordinator == null)
            {
                focusCoordinator = FindFirstObjectByType<UIFocusCoordinator>();
            }

            if (!BindLocalUiActions(inputReader))
            {
                return false;
            }

            _localUse = true;
            _inputEnabled = false;
            _localSessionToken = computer.SessionToken;
            panelFocus.Bind(focusCoordinator);
            panelFocus.NotifyHidden();
            if (focusCoordinator != null && inputReader.IsGamepad && Gamepad.current != null)
            {
                focusCoordinator.ReportGameplayDevice(Gamepad.current);
            }
            SetPresentationBlend(0f);
            RequestRedraw();
            return true;
        }

        public void SetLocalInputEnabled(bool enabled)
        {
            bool acceptInput = enabled && _localUse;

            if (_inputEnabled == acceptInput)
            {
                return;
            }

            _inputEnabled = acceptInput;
            desktopRaycaster.enabled = acceptInput;

            if (acceptInput)
            {
                ResetPanelFocus();
            }
            else
            {
                panelFocus.NotifyHidden();
                ClearDesktopSelection();
            }

            RequestRedraw();
        }

        public void BeginFullscreenTransition(Camera playerCamera)
        {
            _worldScreenRect = GetProjectedScreenRect(playerCamera);
            _displaySize = new Vector2Int(Screen.width, Screen.height);
            BindFullscreenCamera(playerCamera);
            fullscreenRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            SetPresentationBlend(0f);
        }

        public void SetPresentationBlend(float blend)
        {
            _presentationBlend = Mathf.Clamp01(blend);

            if (fullscreenImage == null || fullscreenRoot == null)
            {
                return;
            }

            if (_localUse && _presentationBlend > 0f)
            {
                fullscreenRoot.SetActive(true);
            }

            Rect fit = CalculateFitRect(
                new Rect(0f, 0f, Screen.width, Screen.height),
                (float)TextureResolution.x / TextureResolution.y);
            Rect start = _worldScreenRect.width > 0f ? _worldScreenRect : fit;
            Rect displayed = new Rect(
                Vector2.Lerp(start.position, fit.position, _presentationBlend),
                Vector2.Lerp(start.size, fit.size, _presentationBlend));
            _displayedScreenRect = displayed;
            SetImageScreenRect(displayed);
            Color color = fullscreenImage.color;
            color.a = Mathf.Clamp01(_presentationBlend * 5f);
            fullscreenImage.color = color;

            if (fullscreenMatte != null)
            {
                fullscreenMatte.alpha = _presentationBlend;
            }
        }

        public void EndLocalUse()
        {
            if (desktopRaycaster != null)
            {
                desktopRaycaster.enabled = false;
            }

            _inputEnabled = false;
            _localUse = false;
            _localSessionToken = 0;

            if (panelFocus != null)
            {
                panelFocus.NotifyHidden();
            }

            ClearDesktopSelection();
            RestoreLocalUiActions();

            if (fullscreenRoot != null)
            {
                fullscreenRoot.SetActive(false);
            }

            RestoreFullscreenCamera();

            if (isActiveAndEnabled && _bound)
            {
                RequestRedraw();
            }
        }

        public void HandleBack()
        {
            if (!CanRouteInput)
            {
                return;
            }

            if (computer.ActiveApp == StoreComputerApp.Desktop)
            {
                LeaveComputer();
            }
            else
            {
                CloseApplication();
            }
        }

        public void RequestRedraw()
        {
            _dirty = true;

            if (isActiveAndEnabled && _renderRoutine == null && uiCamera != null)
            {
                _renderRoutine = StartCoroutine(RenderDesktop());
            }
        }

        private void BindEvents()
        {
            if (_bound)
            {
                return;
            }

            computer.StateChanged += HandleStateChanged;
            upgradesButton.onClick.AddListener(OpenUpgrades);
            storeInfoButton.onClick.AddListener(OpenStoreInfo);
            upgradesCloseButton.onClick.AddListener(CloseApplication);
            storeInfoCloseButton.onClick.AddListener(CloseApplication);
            leaveButton.onClick.AddListener(LeaveComputer);
            RenderPipelineManager.beginCameraRendering += HandleCameraRendering;
            _bound = true;
        }

        private void UnbindEvents()
        {
            if (!_bound)
            {
                return;
            }

            if (computer != null) computer.StateChanged -= HandleStateChanged;
            if (upgradesButton != null) upgradesButton.onClick.RemoveListener(OpenUpgrades);
            if (storeInfoButton != null) storeInfoButton.onClick.RemoveListener(OpenStoreInfo);
            if (upgradesCloseButton != null) upgradesCloseButton.onClick.RemoveListener(CloseApplication);
            if (storeInfoCloseButton != null) storeInfoCloseButton.onClick.RemoveListener(CloseApplication);
            if (leaveButton != null) leaveButton.onClick.RemoveListener(LeaveComputer);
            RenderPipelineManager.beginCameraRendering -= HandleCameraRendering;
            _bound = false;
        }

        private void HandleStateChanged(StoreComputerSnapshot snapshot)
        {
            ApplyApplication(snapshot.ActiveApp);
        }

        private void ApplyApplication(StoreComputerApp app)
        {
            if (_hasShownApp && _shownApp == app)
            {
                return;
            }

            _shownApp = app;
            _hasShownApp = true;
            desktopPanel.SetActive(app == StoreComputerApp.Desktop);
            upgradesPanel.SetActive(app == StoreComputerApp.Upgrades);
            storeInfoPanel.SetActive(app == StoreComputerApp.StoreInfo);

            if (_inputEnabled)
            {
                ResetPanelFocus();
            }

            RequestRedraw();
        }

        private void ResetPanelFocus()
        {
            ClearDesktopSelection();
            panelFocus.NotifyHidden();
            Button initial = _shownApp == StoreComputerApp.Upgrades
                ? upgradesCloseButton
                : _shownApp == StoreComputerApp.StoreInfo
                    ? storeInfoCloseButton
                    : upgradesButton;
            panelFocus.SetDefaultSelection(initial);
            panelFocus.NotifyShown();
            panelFocus.NotifyReady();
        }

        private void ClearDesktopSelection()
        {
            EventSystem events = EventSystem.current;
            GameObject selected = events != null
                ? events.currentSelectedGameObject
                : null;

            if (selected != null && desktopCanvas != null &&
                selected.transform.IsChildOf(desktopCanvas.transform))
            {
                events.SetSelectedGameObject(null);
            }
        }

        private void OpenUpgrades() => RequestApplication(StoreComputerApp.Upgrades);
        private void OpenStoreInfo() => RequestApplication(StoreComputerApp.StoreInfo);
        private void CloseApplication() => RequestApplication(StoreComputerApp.Desktop);

        private void RequestApplication(StoreComputerApp app)
        {
            if (CanRouteInput)
            {
                computer.RequestApp(app, _localSessionToken);
            }
        }

        private void LeaveComputer()
        {
            if (CanRouteInput)
            {
                LeaveRequested?.Invoke();
            }
        }

        private IEnumerator RenderDesktop()
        {
            try
            {
                while (_dirty || _localUse)
                {
                    yield return EndOfFrame;

                    if (!EnsureTexture())
                    {
                        break;
                    }

                    if (_localUse &&
                        (_displaySize.x != Screen.width || _displaySize.y != Screen.height))
                    {
                        _displaySize = new Vector2Int(Screen.width, Screen.height);
                        SetPresentationBlend(_presentationBlend);
                    }

                    // CanvasScaler observes a new target size during its next Update.
                    // A newly sized texture gets one additional frame before going idle.
                    _textureSettleFrames = Mathf.Max(0, _textureSettleFrames - 1);
                    _dirty = _textureSettleFrames > 0;
                    Canvas.ForceUpdateCanvases();
                    _renderRequest.destination = _texture;

                    if (!RenderPipeline.SupportsRenderRequest(uiCamera, _renderRequest))
                    {
                        Debug.LogError(
                            "[StoreComputer] The UI camera requires URP SingleCameraRequest support.",
                            this);
                        break;
                    }

                    _isRendering = true;
                    RenderPipeline.SubmitRenderRequest(uiCamera, _renderRequest);
                    _isRendering = false;
                }
            }
            finally
            {
                _isRendering = false;
                _renderRoutine = null;
            }
        }

        private void HandleCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_ownsSobelUiMask)
            {
                UpdateSobelUiMask(camera);
            }

            // Render textures have no content-lost event. Recover on the next real
            // camera render without polling the desktop state or redrawing idle PCs.
            if (!_isRendering && camera != uiCamera &&
                (_texture == null || !_texture.IsCreated() ||
                 _texture.width != textureResolution.x || _texture.height != textureResolution.y))
            {
                RequestRedraw();
            }
        }

        private void UpdateSobelUiMask(Camera camera)
        {
            bool visible = _fullscreenCanvas != null &&
                           camera == _fullscreenCanvas.worldCamera &&
                           fullscreenRoot != null && fullscreenRoot.activeInHierarchy;
            if (!visible)
            {
                Shader.SetGlobalVector(SobelUiOpacityId, Vector4.zero);
                return;
            }

            Rect viewport = camera.pixelRect;
            if (viewport.width <= 0f || viewport.height <= 0f) return;
            Shader.SetGlobalVector(SobelUiRectId, new Vector4(
                (_displayedScreenRect.xMin - viewport.xMin) / viewport.width,
                (_displayedScreenRect.yMin - viewport.yMin) / viewport.height,
                (_displayedScreenRect.xMax - viewport.xMin) / viewport.width,
                (_displayedScreenRect.yMax - viewport.yMin) / viewport.height));
            Shader.SetGlobalVector(SobelUiOpacityId, new Vector4(
                fullscreenImage.color.a,
                fullscreenMatte.alpha,
                0f, 0f));
        }

        private bool EnsureTexture()
        {
            textureResolution = new Vector2Int(
                Mathf.Max(1, textureResolution.x), Mathf.Max(1, textureResolution.y));

            if (_texture != null &&
                (_texture.width != textureResolution.x || _texture.height != textureResolution.y))
            {
                ReleaseTexture();
            }

            if (_texture == null)
            {
                _texture = new RenderTexture(
                    textureResolution.x, textureResolution.y, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "StoreComputerDesktop_" + GetInstanceID(),
                    hideFlags = HideFlags.DontSave,
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                _textureBound = false;
                _textureSettleFrames = 2;
            }

            if (!_texture.IsCreated() && !_texture.Create())
            {
                return false;
            }

            if (!_textureBound)
            {
                uiCamera.targetTexture = _texture;
                uiCamera.aspect = (float)textureResolution.x / textureResolution.y;
                fullscreenImage.texture = _texture;
                _screenProperties.SetTexture(BaseMapId, _texture);
                _screenProperties.SetTexture(MainTexId, _texture);
                screenRenderer.SetPropertyBlock(_screenProperties, screenMaterialIndex);
                _textureBound = true;
                if (_localUse && _fullscreenCanvas != null)
                {
                    SetPresentationBlend(_presentationBlend);
                }
            }
            return true;
        }

        private void CaptureScreenProperties()
        {
            _previousScreenProperties = new MaterialPropertyBlock();
            _screenProperties = new MaterialPropertyBlock();
            screenRenderer.GetPropertyBlock(_previousScreenProperties, screenMaterialIndex);
            screenRenderer.GetPropertyBlock(_screenProperties, screenMaterialIndex);
        }

        private void ReleaseTexture()
        {
            if (screenRenderer != null && _previousScreenProperties != null)
            {
                screenRenderer.SetPropertyBlock(_previousScreenProperties, screenMaterialIndex);
            }

            if (uiCamera != null)
            {
                uiCamera.enabled = false;
                uiCamera.targetTexture = null;
            }

            if (fullscreenImage != null)
            {
                fullscreenImage.texture = null;
            }

            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
                _texture = null;
                _textureBound = false;
            }

            _renderRequest.destination = null;
        }

        private bool BindLocalUiActions(NetworkPlayerInputReader reader)
        {
            _inputModule = EventSystem.current != null
                ? EventSystem.current.GetComponent<InputSystemUIInputModule>() : null;

            if (_inputModule == null || !_inputModule.isActiveAndEnabled ||
                reader.ActionsAsset == null || focusCoordinator == null ||
                !focusCoordinator.isActiveAndEnabled)
            {
                Debug.LogError("[StoreComputer] Assign the scene UI focus coordinator and use its existing EventSystem/InputSystemUIInputModule with the local input reader.", this);
                return false;
            }

            bool coordinatorEnabled = focusCoordinator != null && focusCoordinator.enabled;
            bool moduleEnabled = _inputModule.enabled;
            _previousActions = ReadModuleActions(_inputModule);
            if (coordinatorEnabled) focusCoordinator.enabled = false;
            _inputModule.enabled = false;
            _desktopActions = new InputActionReference[6];
            string[] names = { "Point", "Click", "Navigate", "Submit", "Cancel", "ScrollWheel" };

            for (int i = 0; i < names.Length; i++)
            {
                _desktopActions[i] = InputActionReference.Create(
                    reader.ActionsAsset.FindAction("UI/" + names[i], true));
            }

            _inputReader = reader;
            reader.SetUIInputEnabled(this, true);
            WriteModuleActions(_inputModule, _desktopActions);
            _inputModule.enabled = moduleEnabled;
            if (coordinatorEnabled) focusCoordinator.enabled = true;
            return true;
        }

        private void BindFullscreenCamera(Camera playerCamera)
        {
            if (playerCamera == null || _fullscreenCanvas != null)
            {
                return;
            }

            _fullscreenCanvas = fullscreenImage.GetComponentInParent<Canvas>(true);
            if (_fullscreenCanvas == null) return;
            _previousFullscreenMode = _fullscreenCanvas.renderMode;
            _previousFullscreenCamera = _fullscreenCanvas.worldCamera;
            _previousFullscreenDistance = _fullscreenCanvas.planeDistance;
            _ownsSobelUiMask = true;

            // Both copies of the same texture pass through the user's existing
            // camera post-processing, avoiding an exposure jump at the handoff.
            _fullscreenCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            _fullscreenCanvas.worldCamera = playerCamera;
            _fullscreenCanvas.planeDistance = Mathf.Max(0.1f, playerCamera.nearClipPlane + 0.02f);
        }

        private void RestoreFullscreenCamera()
        {
            if (_ownsSobelUiMask)
            {
                Shader.SetGlobalVector(SobelUiOpacityId, Vector4.zero);
                _ownsSobelUiMask = false;
            }
            if (_fullscreenCanvas == null) return;
            _fullscreenCanvas.renderMode = _previousFullscreenMode;
            _fullscreenCanvas.worldCamera = _previousFullscreenCamera;
            _fullscreenCanvas.planeDistance = _previousFullscreenDistance;
            _fullscreenCanvas = null;
        }

        private void RestoreLocalUiActions()
        {
            if (_previousActions == null)
            {
                return;
            }

            bool coordinatorEnabled = focusCoordinator != null && focusCoordinator.enabled;
            bool moduleEnabled = _inputModule != null && _inputModule.enabled;
            if (coordinatorEnabled) focusCoordinator.enabled = false;
            if (_inputModule != null)
            {
                _inputModule.enabled = false;
                WriteModuleActions(_inputModule, _previousActions);
            }

            if (_inputReader != null) _inputReader.SetUIInputEnabled(this, false);
            if (_inputModule != null) _inputModule.enabled = moduleEnabled;
            if (coordinatorEnabled) focusCoordinator.enabled = true;

            for (int i = 0; i < _desktopActions.Length; i++)
            {
                if (_desktopActions[i] != null) Destroy(_desktopActions[i]);
            }

            _previousActions = null;
            _desktopActions = null;
            _inputReader = null;
            _inputModule = null;
        }

        private static InputActionReference[] ReadModuleActions(InputSystemUIInputModule module)
        {
            return new[]
            {
                module.point, module.leftClick, module.move,
                module.submit, module.cancel, module.scrollWheel
            };
        }

        private static void WriteModuleActions(
            InputSystemUIInputModule module, InputActionReference[] actions)
        {
            module.point = actions[0];
            module.leftClick = actions[1];
            module.move = actions[2];
            module.submit = actions[3];
            module.cancel = actions[4];
            module.scrollWheel = actions[5];
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused && isActiveAndEnabled && _bound)
            {
                RequestRedraw();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused && isActiveAndEnabled && _bound)
            {
                RequestRedraw();
            }
        }

        public static Rect CalculateFitRect(Rect bounds, float imageAspect)
        {
            float width = Mathf.Min(bounds.width, bounds.height * imageAspect);
            float height = width / imageAspect;
            return new Rect(
                bounds.center.x - width * 0.5f,
                bounds.center.y - height * 0.5f,
                width, height);
        }

        private Rect GetProjectedScreenRect(Camera playerCamera)
        {
            if (playerCamera == null)
            {
                return default;
            }

            MeshFilter filter = screenRenderer.GetComponent<MeshFilter>();
            bool localBounds = filter != null && filter.sharedMesh != null;
            Bounds bounds = localBounds ? filter.sharedMesh.bounds : screenRenderer.bounds;
            Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < 8; i++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f,
                        (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f));

                if (localBounds) point = screenRenderer.transform.TransformPoint(point);
                Vector3 projected = playerCamera.WorldToScreenPoint(point);
                if (projected.z <= 0f) return default;
                minimum = Vector2.Min(minimum, projected);
                maximum = Vector2.Max(maximum, projected);
            }

            return CalculateFitRect(Rect.MinMaxRect(
                minimum.x, minimum.y, maximum.x, maximum.y),
                (float)textureResolution.x / textureResolution.y);
        }

        private void SetImageScreenRect(Rect screenRect)
        {
            RectTransform image = fullscreenImage.rectTransform;
            RectTransform parent = image.parent as RectTransform;
            if (parent == null) return;
            Canvas canvas = fullscreenImage.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screenRect.min, camera, out Vector2 minimum);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screenRect.max, camera, out Vector2 maximum);
            image.anchorMin = image.anchorMax = image.pivot = new Vector2(0.5f, 0.5f);
            image.anchoredPosition = (minimum + maximum) * 0.5f - parent.rect.center;
            image.sizeDelta = maximum - minimum;
        }

        [Button("Validate Desktop Setup")]
        public bool ValidateSetup()
        {
            bool valid = computer != null && screenRenderer != null &&
                         uiCamera != null && desktopCanvas != null &&
                         desktopRaycaster != null && panelFocus != null &&
                         desktopPanel != null && upgradesPanel != null && storeInfoPanel != null &&
                         upgradesButton != null && storeInfoButton != null &&
                         upgradesCloseButton != null && storeInfoCloseButton != null && leaveButton != null &&
                         fullscreenRoot != null && fullscreenImage != null && fullscreenMatte != null &&
                         textureResolution.x > 0 && textureResolution.y > 0;

            if (valid)
            {
                valid = screenMaterialIndex >= 0 &&
                        screenMaterialIndex < screenRenderer.sharedMaterials.Length &&
                        desktopCanvas.renderMode == RenderMode.ScreenSpaceCamera &&
                        desktopCanvas.worldCamera == uiCamera;
            }

            if (!valid)
            {
                Debug.LogError(
                    "[StoreComputer] Desktop setup is incomplete. Assign the computer, screen/material slot, " +
                    "UI camera/canvas/raycaster/focus, all panels/buttons and fullscreen references. " +
                    "The desktop canvas must use Screen Space Camera with the assigned UI camera.",
                    this);
            }

            return valid;
        }
    }
}
