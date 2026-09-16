using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Keeps the NetworkObject root authoritative and animates only a visual
    /// child. The server sends one transition cue per transfer; every peer then
    /// performs the interpolation locally without a NetworkTransform stream.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Motion Presenter")]
    public sealed class NetworkItemMotionPresenter : MonoBehaviour
    {
        [InfoBox(
            "Visual Root must be a child that owns the renderers only. " +
            "Keep NetworkObject, colliders, Rigidbody and item logic on the " +
            "authoritative root.")]
        [TitleGroup("References")]
        [Required]
        [SerializeField]
        private NetworkWorldItem item;

        [TitleGroup("References")]
        [Required]
        [Tooltip(
            "Child containing the case mesh/renderers. Never assign the " +
            "NetworkObject root itself.")]
        [SerializeField]
        private Transform visualRoot;

        [TitleGroup("Motion")]
        [MinValue(0.01f)]
        [SuffixLabel("seconds")]
        [SerializeField]
        private float duration = 0.18f;

        [TitleGroup("Motion")]
        [SerializeField]
        private AnimationCurve easing = AnimationCurve.EaseInOut(
            0f,
            0f,
            1f,
            1f);

        [TitleGroup("Motion")]
        [SerializeField]
        private bool animatePosition = true;

        [TitleGroup("Motion")]
        [SerializeField]
        private bool animateRotation = true;

        [TitleGroup("Reliability")]
        [MinValue(0.25f)]
        [SuffixLabel("seconds")]
        [Tooltip(
            "Maximum wait for the matching NGO parent/location update before " +
            "the visual safely snaps to its authoritative pose.")]
        [SerializeField]
        private float hierarchyReadyTimeout = 2f;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        public bool IsAnimating => _isPending || _isAnimating;

        private Vector3 _authoredLocalPosition;
        private Quaternion _authoredLocalRotation;
        private Vector3 _authoredLocalScale;
        private bool _hasAuthoredPose;

        private bool _isPending;
        private uint _pendingRevision;
        private Vector3 _pendingStartPosition;
        private Quaternion _pendingStartRotation;
        private float _pendingElapsed;

        private bool _isAnimating;
        private Vector3 _animationStartPosition;
        private Quaternion _animationStartRotation;
        private float _animationElapsed;

        private void Reset()
        {
            AutoAssignReferences();
            CaptureAuthoredPose();
        }

        private void Awake()
        {
            AutoAssignReferences();
            CaptureAuthoredPose();

            // Thousands of world cases should not receive LateUpdate while
            // idle. A transition enables this component temporarily.
            enabled = false;
        }

        private void OnValidate()
        {
            duration = Mathf.Max(0.01f, duration);
            hierarchyReadyTimeout = Mathf.Max(
                0.25f,
                hierarchyReadyTimeout);
            AutoAssignReferences();
            CaptureAuthoredPose();
        }

        private void OnDisable()
        {
            CancelMotion(true);
        }

        private void LateUpdate()
        {
            if (item == null || !item.IsSpawned ||
                visualRoot == null || !_hasAuthoredPose)
            {
                return;
            }

            if (_isPending)
            {
                UpdatePendingTransition();
            }

            if (_isAnimating)
            {
                UpdateAnimation();
            }
        }

        internal bool TryCaptureCurrentVisualPose(
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (visualRoot == null || visualRoot == transform)
            {
                return false;
            }

            worldPosition = visualRoot.position;
            worldRotation = visualRoot.rotation;
            return IsFinite(worldPosition) && IsFinite(worldRotation);
        }

        internal void PlayTransitionLocal(
            Vector3 startWorldPosition,
            Quaternion startWorldRotation,
            uint targetRevision)
        {
            AutoAssignReferences();

            if (!_hasAuthoredPose)
            {
                CaptureAuthoredPose();
            }

            if (visualRoot == null || visualRoot == transform ||
                !IsFinite(startWorldPosition) ||
                !IsFinite(startWorldRotation))
            {
                return;
            }

            enabled = true;

            // A rapid second transfer continues from what this peer currently
            // displays, preventing a pop back to the server's earlier frame.
            if (_isPending || _isAnimating)
            {
                startWorldPosition = visualRoot.position;
                startWorldRotation = visualRoot.rotation;
            }

            _pendingStartPosition = startWorldPosition;
            _pendingStartRotation = startWorldRotation;
            _pendingRevision = targetRevision;
            _pendingElapsed = 0f;
            _isPending = true;
            _isAnimating = false;

            HoldVisualAtPendingStart();
        }

        private void UpdatePendingTransition()
        {
            HoldVisualAtPendingStart();
            _pendingElapsed += Time.deltaTime;

            if (HasReachedRevision(item.Revision, _pendingRevision) &&
                IsHierarchyReady(item.Location))
            {
                _animationStartPosition = _pendingStartPosition;
                _animationStartRotation = _pendingStartRotation;
                _animationElapsed = 0f;
                _isPending = false;
                _isAnimating = true;
                return;
            }

            if (_pendingElapsed >= hierarchyReadyTimeout)
            {
                FinishMotion();
            }
        }

        private void UpdateAnimation()
        {
            _animationElapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(_animationElapsed / duration);
            float eased = easing != null
                ? easing.Evaluate(normalized)
                : Mathf.SmoothStep(0f, 1f, normalized);

            GetTargetWorldPose(
                out Vector3 targetPosition,
                out Quaternion targetRotation);

            Vector3 position = animatePosition
                ? Vector3.LerpUnclamped(
                    _animationStartPosition,
                    targetPosition,
                    eased)
                : targetPosition;
            Quaternion rotation = animateRotation
                ? Quaternion.SlerpUnclamped(
                    _animationStartRotation,
                    targetRotation,
                    eased)
                : targetRotation;

            visualRoot.SetPositionAndRotation(position, rotation);
            visualRoot.localScale = _authoredLocalScale;

            if (normalized >= 1f)
            {
                FinishMotion();
            }
        }

        private void FinishMotion()
        {
            CancelMotion(true);
            enabled = false;
        }

        private void HoldVisualAtPendingStart()
        {
            if (visualRoot == null)
            {
                return;
            }

            visualRoot.SetPositionAndRotation(
                _pendingStartPosition,
                _pendingStartRotation);
            visualRoot.localScale = _authoredLocalScale;
        }

        private bool IsHierarchyReady(NetworkItemLocationState state)
        {
            switch (state.Kind)
            {
                case NetworkItemLocationKind.World:
                    return transform.parent == null;

                case NetworkItemLocationKind.Held:
                {
                    NetworkItemCarrier carrier =
                        GetComponentInParent<NetworkItemCarrier>();
                    return carrier != null &&
                           carrier.OwnerClientId == state.HolderClientId;
                }

                case NetworkItemLocationKind.Placed:
                    return item.TryResolveReceiver(
                               out NetworkItemReceiver receiver) &&
                           transform.IsChildOf(receiver.transform);

                default:
                    return false;
            }
        }

        private void GetTargetWorldPose(
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            Transform parent = visualRoot.parent;

            if (parent == null)
            {
                worldPosition = _authoredLocalPosition;
                worldRotation = _authoredLocalRotation;
                return;
            }

            worldPosition = parent.TransformPoint(_authoredLocalPosition);
            worldRotation = parent.rotation * _authoredLocalRotation;
        }

        private void CancelMotion(bool snapToTarget)
        {
            _isPending = false;
            _isAnimating = false;
            _pendingElapsed = 0f;
            _animationElapsed = 0f;

            if (snapToTarget)
            {
                SnapVisualToTarget();
            }
        }

        private void SnapVisualToTarget()
        {
            if (visualRoot == null || !_hasAuthoredPose)
            {
                return;
            }

            visualRoot.localPosition = _authoredLocalPosition;
            visualRoot.localRotation = _authoredLocalRotation;
            visualRoot.localScale = _authoredLocalScale;
        }

        private void CaptureAuthoredPose()
        {
            if (visualRoot == null || visualRoot == transform)
            {
                _hasAuthoredPose = false;
                return;
            }

            _authoredLocalPosition = visualRoot.localPosition;
            _authoredLocalRotation = visualRoot.localRotation;
            _authoredLocalScale = visualRoot.localScale;
            _hasAuthoredPose = true;
        }

        [Button("AUTO ASSIGN REFERENCES")]
        public void AutoAssignReferences()
        {
            if (item == null)
            {
                item = GetComponent<NetworkWorldItem>();
            }

            if (visualRoot != null && visualRoot != transform)
            {
                return;
            }

            Transform namedVisual = transform.Find("VisualRoot");

            if (namedVisual != null)
            {
                visualRoot = namedVisual;
                return;
            }

            Renderer firstRenderer = GetComponentInChildren<Renderer>(true);

            if (firstRenderer != null && firstRenderer.transform != transform)
            {
                visualRoot = firstRenderer.transform;
            }
        }

        private static bool HasReachedRevision(uint current, uint target)
        {
            return current == target ||
                   unchecked(current - target) < 0x80000000u;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
