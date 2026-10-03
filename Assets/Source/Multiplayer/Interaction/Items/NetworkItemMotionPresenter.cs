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

		[InfoBox("Visual Root must be a child that owns the renderers only. " + "Keep NetworkObject, colliders, Rigidbody and item logic on the " + "authoritative root.")]
		[TitleGroup("References")]
		[Required]
		[SerializeField]
		private NetworkWorldItem item;

		[TitleGroup("References")] [Required] [Tooltip("Child containing the case mesh/renderers. Never assign the " + "NetworkObject root itself.")] [SerializeField] private Transform visualRoot;

		[TitleGroup("Motion")] [MinValue(0.01f)] [SuffixLabel("seconds")] [SerializeField] private float duration = 0.18f;

		[TitleGroup("Motion")] [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		[TitleGroup("Motion")] [SerializeField] private bool animatePosition = true;

		[TitleGroup("Motion")] [SerializeField] private bool animateRotation = true;

		[TitleGroup("World Drop Settle")] [Tooltip("Adds a tiny visual-only bounce to world drops. The authoritative " + "root and collider remain at the server-resolved safe pose.")] [SerializeField]
		private bool settleWorldDrops = true;

		[TitleGroup("World Drop Settle")] [Range(0f, 0.95f)] [SerializeField] private float settleStartNormalized = 0.55f;

		[TitleGroup("World Drop Settle")] [MinValue(0f)] [SuffixLabel("m")] [SerializeField] private float settleHeight = 0.018f;

		[TitleGroup("World Drop Settle")] [MinValue(0f)] [SuffixLabel("degrees")] [SerializeField] private float settleAngle = 1.25f;

		[TitleGroup("World Drop Settle")] [MinValue(0.25f)] [SerializeField] private float settleCycles = 1.25f;

		[TitleGroup("Reliability")]
		[MinValue(0.25f)]
		[SuffixLabel("seconds")]
		[Tooltip("Maximum wait for the matching NGO parent/location update before " + "the visual safely snaps to its authoritative pose.")]
		[SerializeField]
		private float hierarchyReadyTimeout = 2f;

		[TitleGroup("Physics Snapshot Smoothing")] [MinValue(0.01f)] [SuffixLabel("seconds")] [SerializeField] private float snapshotCatchUpDuration = 0.09f;

		[TitleGroup("Physics Snapshot Smoothing")] [MinValue(0.01f)] [SuffixLabel("seconds")] [SerializeField] private float finalPoseCatchUpDuration = 0.12f;

		[ShowInInspector]
		[ReadOnly]
		[BoxGroup("Runtime")]

		public bool IsAnimating => _isPending || _isAnimating;

		[ShowInInspector]
		[ReadOnly]
		[BoxGroup("Runtime")]

		public bool IsPhysicsSettling => _physicsSettleActive;

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

		private float _activeDuration;

		private bool _settleCurrentWorldDrop;

		private bool _physicsSettleActive;

		private bool _hasPhysicsPoseSample;

		private uint _physicsRevision;

		private bool _hasFinalizedPhysicsRevision;

		private uint _finalizedPhysicsRevision;

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
			settleStartNormalized = Mathf.Clamp(settleStartNormalized, 0f, 0.95f);
			settleHeight = Mathf.Max(0f, settleHeight);
			settleAngle = Mathf.Max(0f, settleAngle);
			settleCycles = Mathf.Max(0.25f, settleCycles);
			hierarchyReadyTimeout = Mathf.Max(0.25f, hierarchyReadyTimeout);
			snapshotCatchUpDuration = Mathf.Max(0.01f, snapshotCatchUpDuration);
			finalPoseCatchUpDuration = Mathf.Max(0.01f, finalPoseCatchUpDuration);
			AutoAssignReferences();
			CaptureAuthoredPose();
		}

		private void OnDisable() { CancelMotion(true); }

		private void LateUpdate()

		{
			if (item == null || !item.IsSpawned || visualRoot == null || !_hasAuthoredPose)
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

		internal bool TryCaptureCurrentVisualPose(out Vector3 worldPosition, out Quaternion worldRotation)

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

		internal void PlayTransitionLocal(Vector3 startWorldPosition, Quaternion startWorldRotation, uint targetRevision, bool physicsSettle = false)

		{
			AutoAssignReferences();
			if (!_hasAuthoredPose)
			{
				CaptureAuthoredPose();
			}
			if (visualRoot == null || visualRoot == transform || !IsFinite(startWorldPosition) || !IsFinite(startWorldRotation))
			{
				return;
			}

			// An unreliable physics sample may overtake the initial reliable

			// motion cue on another behaviour. Never rewind that newer visual.
			if (physicsSettle &&
			    ((_hasPhysicsPoseSample && _physicsSettleActive && _physicsRevision == targetRevision) || (_hasFinalizedPhysicsRevision && _finalizedPhysicsRevision == targetRevision)))
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
			_activeDuration = duration;
			_settleCurrentWorldDrop = false;
			_physicsSettleActive = physicsSettle;
			_hasPhysicsPoseSample = false;
			_physicsRevision = targetRevision;
			HoldVisualAtPendingStart();
		}

		internal void SetPhysicsSettleActiveLocal(bool active, uint revision)

		{
			if (active && _hasFinalizedPhysicsRevision && _finalizedPhysicsRevision == revision)
			{
				return;
			}
			if (active || _physicsRevision == revision)
			{
				_physicsSettleActive = active;
				_physicsRevision = revision;
			}
		}

		internal void ApplyReplicatedWorldPoseLocal(Vector3 worldPosition, Quaternion worldRotation, uint revision, bool final)

		{
			AutoAssignReferences();
			if (item == null || !item.IsSpawned || !item.Location.IsWorld || item.Revision != revision || !IsFinite(worldPosition) || !IsFinite(worldRotation) ||
			    (!final && _hasFinalizedPhysicsRevision && _finalizedPhysicsRevision == revision))
			{
				return;
			}
			if (!_hasAuthoredPose)
			{
				CaptureAuthoredPose();
			}
			bool hasVisual = visualRoot != null && visualRoot != transform && _hasAuthoredPose;
			Vector3 visiblePosition = hasVisual ? visualRoot.position : worldPosition;
			Quaternion visibleRotation = hasVisual ? visualRoot.rotation : worldRotation;
			transform.SetPositionAndRotation(worldPosition, worldRotation);
			_physicsRevision = revision;
			_physicsSettleActive = !final;
			_hasPhysicsPoseSample = true;
			if (final)
			{
				_hasFinalizedPhysicsRevision = true;
				_finalizedPhysicsRevision = revision;
			}
			if (!hasVisual)
			{
				return;
			}

			// Keep what the client was displaying in place, move only the

			// authoritative root, then let the visual child catch up locally.
			visualRoot.SetPositionAndRotation(visiblePosition, visibleRotation);
			visualRoot.localScale = _authoredLocalScale;
			_isPending = false;
			_isAnimating = true;
			_animationStartPosition = visiblePosition;
			_animationStartRotation = visibleRotation;
			_animationElapsed = 0f;
			_activeDuration = final ? finalPoseCatchUpDuration : snapshotCatchUpDuration;
			_settleCurrentWorldDrop = false;
			enabled = true;
		}

		private void UpdatePendingTransition()

		{
			HoldVisualAtPendingStart();
			_pendingElapsed += Time.deltaTime;
			if (HasReachedRevision(item.Revision, _pendingRevision) && IsHierarchyReady(item.Location))
			{
				_animationStartPosition = _pendingStartPosition;
				_animationStartRotation = _pendingStartRotation;
				_animationElapsed = 0f;
				_activeDuration = duration;
				_settleCurrentWorldDrop = settleWorldDrops && item.Location.IsWorld && !_physicsSettleActive;
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
			float normalized = Mathf.Clamp01(_animationElapsed / Mathf.Max(0.01f, _activeDuration));
			float eased = easing != null ? easing.Evaluate(normalized) : Mathf.SmoothStep(0f, 1f, normalized);
			GetTargetWorldPose(out Vector3 targetPosition, out Quaternion targetRotation);
			Vector3 position = animatePosition ? Vector3.LerpUnclamped(_animationStartPosition, targetPosition, eased) : targetPosition;
			Quaternion rotation = animateRotation ? Quaternion.SlerpUnclamped(_animationStartRotation, targetRotation, eased) : targetRotation;
			if (_settleCurrentWorldDrop && normalized > settleStartNormalized)
			{
				float settleT = Mathf.InverseLerp(settleStartNormalized, 1f, normalized);
				float wave = Mathf.Sin(settleT * Mathf.PI * 2f * settleCycles);
				float decay = 1f - settleT;
				float bounce = Mathf.Abs(wave) * decay * settleHeight;
				float wobble = wave * decay * settleAngle;
				position += Vector3.up * bounce;
				rotation *= Quaternion.Euler(wobble * 0.35f, 0f, wobble);
			}
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
			visualRoot.SetPositionAndRotation(_pendingStartPosition, _pendingStartRotation);
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
				NetworkItemCarrier carrier = GetComponentInParent<NetworkItemCarrier>();
				return carrier != null && carrier.OwnerClientId == state.HolderClientId;
			}
			case NetworkItemLocationKind.Placed:
				return item.TryResolveReceiver(out NetworkItemReceiver receiver) && transform.IsChildOf(receiver.transform);
			default:
				return false;
			}
		}

		private void GetTargetWorldPose(out Vector3 worldPosition, out Quaternion worldRotation)

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
			_settleCurrentWorldDrop = false;
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

		private static bool HasReachedRevision(uint current, uint target) { return current == target || unchecked(current - target) < 0x80000000u; }

		private static bool IsFinite(Vector3 value) { return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z); }

		private static bool IsFinite(Quaternion value) { return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w); }

		private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

	}

}