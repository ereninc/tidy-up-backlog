using DG.Tweening;
using System.Text;
using TMPro;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local-only UI presenter. It follows the exact stack entry selected by
    /// the shelf ghost, converts carrier order to top-first text order, and
    /// sizes one background to the rendered TMP line width.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(
        "Multiplayer/Game Cases/UI/Game Case Stack Selection Indicator")]
    public sealed class GameCaseStackSelectionIndicator : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Text stackNameText;
        [SerializeField] private RectTransform lineIndicatorBackground;
        [SerializeField] private CanvasGroup indicatorCanvasGroup;
        
        [SerializeField] private TMP_Text stackCountText;

        [Header("Layout")]
        [Min(0f)] [SerializeField] private float horizontalPadding = 28f;
        [SerializeField] private float verticalOffset;

        [Header("Animation")]
        [Min(0f)] [SerializeField] private float animationDuration = 0.12f;
        [SerializeField] private Ease animationEase = Ease.OutCubic;

        private GameCaseShelfGhostPresenter _ghost;
        private NetworkItemCarrier _carrier;
        private Sequence _animation;
        private bool _refreshQueued = true;

        private readonly StringBuilder _stackTextBuilder =
            new StringBuilder(256);

        private void Reset()
        {
            ResolveReferences();
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureCanvasGroup();

            if (stackNameText != null)
            {
                stackNameText.richText = false;
            }

            SetVisible(false);
        }

        private void OnEnable()
        {
            GameCaseSessionPlan.Changed +=
                HandleSessionPlanChanged;

            RebindSources();
            QueueRefresh();
        }

        private void LateUpdate()
        {
            if (_ghost != GameCaseShelfGhostPresenter.Instance ||
                _carrier != NetworkItemCarrier.Local)
            {
                RebindSources();
            }

            if (!_refreshQueued)
            {
                return;
            }

            _refreshQueued = false;

            RefreshStackText();
            RefreshStackCount();
            RefreshIndicator();
        }

        private void OnDisable()
        {
            GameCaseSessionPlan.Changed -=
                HandleSessionPlanChanged;

            UnbindSources();
            KillAnimation();

            if (stackNameText != null)
            {
                stackNameText.text = string.Empty;
            }
            
            if (stackCountText != null)
            {
                stackCountText.text = string.Empty;
            }

            SetVisible(false);
        }

        private void OnDestroy()
        {
            KillAnimation();
        }

        /// <summary>
        /// Optional compatibility hook for another local UI system that wants
        /// to force the stack text and indicator to refresh.
        /// </summary>
        public void NotifyStackTextChanged()
        {
            QueueRefresh();
        }

        private void RebindSources()
        {
            UnbindSources();

            _ghost = GameCaseShelfGhostPresenter.Instance;
            _carrier = NetworkItemCarrier.Local;

            if (_ghost != null)
            {
                _ghost.PreviewSelectionChanged +=
                    HandlePreviewSelectionChanged;
            }

            if (_carrier != null)
            {
                _carrier.HeldStackChanged += HandleHeldStackChanged;
                _carrier.CarryLimitChanged += HandleCarryLimitChanged;
            }

            QueueRefresh();
        }

        private void UnbindSources()
        {
            if (_ghost != null)
            {
                _ghost.PreviewSelectionChanged -=
                    HandlePreviewSelectionChanged;
            }

            if (_carrier != null)
            {
                _carrier.HeldStackChanged -= HandleHeldStackChanged;
                _carrier.CarryLimitChanged -= HandleCarryLimitChanged;
            }

            _ghost = null;
            _carrier = null;
        }

        private void HandlePreviewSelectionChanged(
            int stackIndex,
            NetworkWorldItem selectedItem)
        {
            QueueRefresh();
        }

        private void HandleHeldStackChanged()
        {
            QueueRefresh();
        }
        
        private void HandleCarryLimitChanged(
            int previousLimit,
            int currentLimit)
        {
            QueueRefresh();
        }

        private void HandleSessionPlanChanged()
        {
            QueueRefresh();
        }

        private void QueueRefresh()
        {
            _refreshQueued = true;
        }

        private void RefreshStackText()
        {
            if (stackNameText == null)
            {
                return;
            }

            _stackTextBuilder.Clear();

            if (_carrier != null)
            {
                for (int i = _carrier.HeldItemCount - 1;
                     i >= 0;
                     i--)
                {
                    if (_stackTextBuilder.Length > 0)
                    {
                        _stackTextBuilder.Append('\n');
                    }

                    if (!_carrier.TryGetHeldItemAt(
                            i,
                            out NetworkWorldItem item))
                    {
                        // Keep one line per replicated stack index so the
                        // selection highlight cannot shift while an object
                        // reference resolves on this client.
                        _stackTextBuilder.Append("Loading...");
                        continue;
                    }

                    if (item.TryGetComponent(
                            out NetworkGameCase gameCase) &&
                        gameCase.AppId != 0)
                    {
                        _stackTextBuilder.Append(
                            GameCaseSessionPlan.GetGameName(
                                gameCase.AppId));
                    }
                    else
                    {
                        _stackTextBuilder.Append(
                            string.IsNullOrWhiteSpace(item.DisplayName)
                                ? "Item"
                                : item.DisplayName);
                    }
                }
            }

            stackNameText.text =
                _stackTextBuilder.ToString();
        }
        
        private void RefreshStackCount()
        {
            if (stackCountText == null)
            {
                return;
            }

            if (_carrier == null)
            {
                stackCountText.text = string.Empty;
                return;
            }

            stackCountText.SetText(
                "{0}/{1}",
                _carrier.HeldItemCount,
                _carrier.CarryLimit);
        }

        private void RefreshIndicator()
        {
            if (stackNameText == null ||
                lineIndicatorBackground == null ||
                _ghost == null ||
                _carrier == null ||
                _ghost.PreviewStackIndex < 0)
            {
                SetVisible(false);
                return;
            }

            int displayLineIndex =
                _carrier.HeldItemCount - 1 -
                _ghost.PreviewStackIndex;

            stackNameText.ForceMeshUpdate();
            TMP_TextInfo textInfo = stackNameText.textInfo;

            if (displayLineIndex < 0 ||
                displayLineIndex >= textInfo.lineCount)
            {
                SetVisible(false);
                return;
            }

            TMP_LineInfo line = textInfo.lineInfo[displayLineIndex];

            if (line.characterCount <= 0 ||
                lineIndicatorBackground.parent == null)
            {
                SetVisible(false);
                return;
            }

            RectTransform indicatorParent =
                lineIndicatorBackground.parent as RectTransform;

            if (indicatorParent == null)
            {
                SetVisible(false);
                return;
            }

            float lineCenterY =
                (line.ascender + line.descender) * 0.5f;

            Vector3 textLeftWorld =
                stackNameText.rectTransform.TransformPoint(
                    new Vector3(
                        line.lineExtents.min.x,
                        lineCenterY,
                        0f));

            Vector3 textRightWorld =
                stackNameText.rectTransform.TransformPoint(
                    new Vector3(
                        line.lineExtents.max.x,
                        lineCenterY,
                        0f));

            Vector3 textLeftLocal =
                indicatorParent.InverseTransformPoint(textLeftWorld);

            Vector3 textRightLocal =
                indicatorParent.InverseTransformPoint(textRightWorld);

            float targetY =
                (textLeftLocal.y + textRightLocal.y) * 0.5f +
                verticalOffset;

            float targetWidth = Mathf.Max(
                1f,
                Mathf.Abs(textRightLocal.x - textLeftLocal.x) +
                horizontalPadding * 2f);

            SetVisible(true);
            AnimateTo(targetY, targetWidth);
        }

        private void AnimateTo(float targetLocalY, float targetWidth)
        {
            KillAnimation();

            Vector2 targetSize = lineIndicatorBackground.sizeDelta;
            targetSize.x = targetWidth;

            if (!Application.isPlaying || animationDuration <= 0f)
            {
                Vector3 localPosition =
                    lineIndicatorBackground.localPosition;

                localPosition.y = targetLocalY;
                lineIndicatorBackground.localPosition = localPosition;
                lineIndicatorBackground.sizeDelta = targetSize;
                return;
            }

            _animation = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(this);

            _animation.Join(
                lineIndicatorBackground.DOLocalMoveY(
                        targetLocalY,
                        animationDuration)
                    .SetEase(animationEase));

            _animation.Join(
                lineIndicatorBackground.DOSizeDelta(
                        targetSize,
                        animationDuration)
                    .SetEase(animationEase));
        }

        private void SetVisible(bool visible)
        {
            EnsureCanvasGroup();

            if (indicatorCanvasGroup == null)
            {
                return;
            }

            indicatorCanvasGroup.alpha = visible ? 1f : 0f;
            indicatorCanvasGroup.interactable = false;
            indicatorCanvasGroup.blocksRaycasts = false;
        }

        private void KillAnimation()
        {
            if (_animation != null && _animation.IsActive())
            {
                _animation.Kill();
            }

            _animation = null;
        }

        private void ResolveReferences()
        {
            if (stackNameText == null)
            {
                stackNameText =
                    GetComponentInChildren<TMP_Text>(true);
            }

            if (lineIndicatorBackground == null)
            {
                Transform candidate = transform.Find("LineIndicatorBG");

                if (candidate != null)
                {
                    lineIndicatorBackground =
                        candidate as RectTransform;
                }
            }
        }

        private void EnsureCanvasGroup()
        {
            if (indicatorCanvasGroup != null ||
                lineIndicatorBackground == null)
            {
                return;
            }

            indicatorCanvasGroup =
                lineIndicatorBackground.GetComponent<CanvasGroup>();

            if (indicatorCanvasGroup == null && Application.isPlaying)
            {
                indicatorCanvasGroup =
                    lineIndicatorBackground.gameObject
                        .AddComponent<CanvasGroup>();
            }
        }
    }
}
