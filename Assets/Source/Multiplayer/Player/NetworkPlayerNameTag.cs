using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EXW.Multiplayer
{
    /// <summary>
    /// Displays the already replicated Steam persona name above a network player.
    /// This component owns no network state and never calls the Steam API.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkPlayerIdentity))]
    [AddComponentMenu("Multiplayer/Network Player Name Tag")]
    public sealed class NetworkPlayerNameTag : MonoBehaviour
    {
        private const string CanvasObjectName = "NameTagCanvas";
        private const string TextObjectName = "NameText";

        [InfoBox(
            "Reads the host-approved persona name from NetworkPlayerIdentity. " +
            "It does not add another NetworkBehaviour or contact Steam directly.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField] private NetworkPlayerIdentity playerIdentity;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private Transform nameTagRoot;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private Canvas nameTagCanvas;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private CanvasGroup canvasGroup;

        [TitleGroup("References")]
        [Required]
        [SerializeField] private TMP_Text nameText;

        [TitleGroup("References")]
        [SerializeField] private Image background;

        [TitleGroup("Visibility")]
        [Tooltip("The first-person owner normally does not need to see their own tag.")]
        [SerializeField] private bool hideForLocalOwner = true;

        [TitleGroup("Visibility")]
        [Tooltip("Rotate the tag to match the active MainCamera each frame.")]
        [SerializeField] private bool faceCamera = true;

        [TitleGroup("Visibility")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float fadeStartDistance = 14f;

        [TitleGroup("Visibility")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float maximumVisibleDistance = 20f;

        [TitleGroup("Style")]
        [SerializeField] private Color textColor = Color.white;

        [TitleGroup("Style")]
        [SerializeField] private Color backgroundColor =
            new Color(0.03f, 0.03f, 0.03f, 0.72f);

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Displayed Name")]
        private string DisplayedName =>
            nameText != null ? nameText.text : string.Empty;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Local Owner")]
        private bool RuntimeLocalOwner =>
            playerIdentity != null && playerIdentity.IsLocalOwner;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Visible")]
        private bool RuntimeVisible =>
            nameTagCanvas != null && nameTagCanvas.enabled;

        private Camera _targetCamera;
        private bool _subscribed;

        private void Reset()
        {
            ResolveReferences();
            ApplyStyle();
        }

        private void OnValidate()
        {
            fadeStartDistance = Mathf.Max(0.1f, fadeStartDistance);
            maximumVisibleDistance = Mathf.Max(
                fadeStartDistance + 0.1f,
                maximumVisibleDistance);

            ResolveReferences();
            ApplyStyle();
        }

        private void Awake()
        {
            ResolveReferences();
            ApplyStyle();

            if (nameText != null)
            {
                // Steam persona names are untrusted display strings. Do not let
                // names such as <size=200> alter the TMP layout.
                nameText.richText = false;
            }

            ConfigureCanvasGroup();
            SetRendered(false);
        }

        private void OnEnable()
        {
            Subscribe();
            RefreshIdentity();
        }

        private void OnDisable()
        {
            Unsubscribe();
            SetRendered(false);
        }

        private void LateUpdate()
        {
            if (!CanRenderForPlayer())
            {
                SetRendered(false);
                return;
            }

            if (!TryResolveTargetCamera())
            {
                SetRendered(false);
                return;
            }

            if (faceCamera && nameTagRoot != null)
            {
                // Matching the camera's world rotation keeps the UI parallel to
                // the screen while cancelling the owning player's yaw rotation.
                nameTagRoot.rotation = _targetCamera.transform.rotation;
            }

            if (nameTagCanvas != null && nameTagCanvas.worldCamera != _targetCamera)
            {
                nameTagCanvas.worldCamera = _targetCamera;
            }

            float distance = Vector3.Distance(
                _targetCamera.transform.position,
                nameTagRoot.position);

            if (distance >= maximumVisibleDistance)
            {
                SetRendered(false);
                return;
            }

            float alpha = distance <= fadeStartDistance
                ? 1f
                : 1f - Mathf.InverseLerp(
                    fadeStartDistance,
                    maximumVisibleDistance,
                    distance);

            SetRendered(true);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
            }
        }

        private void Subscribe()
        {
            if (_subscribed || playerIdentity == null)
            {
                return;
            }

            playerIdentity.IdentityChanged += HandleIdentityChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            if (playerIdentity != null)
            {
                playerIdentity.IdentityChanged -= HandleIdentityChanged;
            }

            _subscribed = false;
        }

        private void HandleIdentityChanged(NetworkPlayerIdentity identity)
        {
            if (identity == playerIdentity)
            {
                RefreshIdentity();
            }
        }

        private void RefreshIdentity()
        {
            if (nameText == null)
            {
                return;
            }

            string personaName = playerIdentity != null
                ? playerIdentity.PersonaName
                : string.Empty;

            nameText.SetText(
                string.IsNullOrWhiteSpace(personaName)
                    ? "Player"
                    : personaName);
        }

        private bool CanRenderForPlayer()
        {
            if (playerIdentity == null ||
                !playerIdentity.IsSpawned ||
                !playerIdentity.IsIdentityReady ||
                nameTagRoot == null ||
                nameTagCanvas == null ||
                nameText == null)
            {
                return false;
            }

            return !hideForLocalOwner || !playerIdentity.IsLocalOwner;
        }

        private bool TryResolveTargetCamera()
        {
            if (_targetCamera != null && _targetCamera.isActiveAndEnabled)
            {
                return true;
            }

            _targetCamera = Camera.main;
            return _targetCamera != null && _targetCamera.isActiveAndEnabled;
        }

        private void SetRendered(bool visible)
        {
            if (nameTagCanvas != null)
            {
                nameTagCanvas.enabled = visible;
            }

            if (!visible && canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private void ConfigureCanvasGroup()
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.ignoreParentGroups = true;
        }

        private void ApplyStyle()
        {
            if (nameText != null)
            {
                nameText.color = textColor;
                nameText.richText = false;
            }

            if (background != null)
            {
                background.color = backgroundColor;
                background.raycastTarget = false;
            }
        }

        [Button("AUTO ASSIGN REFERENCES")]
        private void ResolveReferences()
        {
            if (playerIdentity == null)
            {
                playerIdentity = GetComponent<NetworkPlayerIdentity>();
            }

            if (nameTagCanvas == null)
            {
                Transform canvasTransform = transform.Find(CanvasObjectName);

                if (canvasTransform != null)
                {
                    nameTagCanvas = canvasTransform.GetComponent<Canvas>();
                }
            }

            if (nameTagCanvas == null)
            {
                Canvas[] childCanvases = GetComponentsInChildren<Canvas>(true);

                for (int i = 0; i < childCanvases.Length; i++)
                {
                    if (childCanvases[i].name == CanvasObjectName)
                    {
                        nameTagCanvas = childCanvases[i];
                        break;
                    }
                }
            }

            if (nameTagCanvas != null)
            {
                if (nameTagRoot == null)
                {
                    nameTagRoot = nameTagCanvas.transform;
                }

                if (canvasGroup == null)
                {
                    canvasGroup = nameTagCanvas.GetComponent<CanvasGroup>();
                }

                if (background == null)
                {
                    background = nameTagCanvas.GetComponent<Image>();
                }

                if (nameText == null)
                {
                    nameText = nameTagCanvas.GetComponentInChildren<TMP_Text>(true);
                }
            }
        }

#if UNITY_EDITOR
        [Button("CREATE / REPAIR DEFAULT NAME TAG", ButtonSizes.Large)]
        [GUIColor(0.35f, 0.8f, 1f)]
        public void CreateOrRepairDefaultNameTag()
        {
            Undo.RegisterFullObjectHierarchyUndo(
                gameObject,
                "Create Network Player Name Tag");

            playerIdentity = GetComponent<NetworkPlayerIdentity>();

            Transform existingCanvas = transform.Find(CanvasObjectName);
            GameObject canvasObject;

            if (existingCanvas == null)
            {
                canvasObject = new GameObject(
                    CanvasObjectName,
                    typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(
                    canvasObject,
                    "Create Name Tag Canvas");
                canvasObject.transform.SetParent(transform, false);
            }
            else
            {
                canvasObject = existingCanvas.gameObject;
            }

            RectTransform canvasRect =
                GetOrAddComponent<RectTransform>(canvasObject);
            nameTagCanvas = GetOrAddComponent<Canvas>(canvasObject);
            CanvasScaler scaler = GetOrAddComponent<CanvasScaler>(canvasObject);
            canvasGroup = GetOrAddComponent<CanvasGroup>(canvasObject);
            GetOrAddComponent<CanvasRenderer>(canvasObject);
            background = GetOrAddComponent<Image>(canvasObject);

            nameTagRoot = canvasRect;
            canvasRect.localPosition = new Vector3(0f, 2.15f, 0f);
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * 0.01f;
            canvasRect.sizeDelta = new Vector2(240f, 48f);

            nameTagCanvas.renderMode = RenderMode.WorldSpace;
            nameTagCanvas.overrideSorting = true;
            nameTagCanvas.sortingOrder = 100;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            ConfigureCanvasGroup();

            Transform existingText = canvasRect.Find(TextObjectName);
            GameObject textObject;

            if (existingText == null)
            {
                textObject = new GameObject(
                    TextObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer));
                Undo.RegisterCreatedObjectUndo(
                    textObject,
                    "Create Name Tag Text");
                textObject.transform.SetParent(canvasRect, false);
            }
            else
            {
                textObject = existingText.gameObject;
            }

            nameText = GetOrAddComponent<TextMeshProUGUI>(textObject);
            RectTransform textRect = nameText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.offsetMin = new Vector2(10f, 3f);
            textRect.offsetMax = new Vector2(-10f, -3f);

            nameText.SetText("Steam Player");
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 18f;
            nameText.fontSizeMax = 30f;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.raycastTarget = false;
            nameText.richText = false;

            ApplyStyle();
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(canvasObject);
            EditorUtility.SetDirty(textObject);

            PrefabUtility.RecordPrefabInstancePropertyModifications(this);

            Debug.Log(
                "[PlayerNameTag] Default world-space name tag created/repaired.",
                this);
        }

        private static T GetOrAddComponent<T>(GameObject target)
            where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null
                ? component
                : Undo.AddComponent<T>(target);
        }
#endif
    }
}