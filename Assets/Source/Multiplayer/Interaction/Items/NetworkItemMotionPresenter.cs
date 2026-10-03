using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Animates the visual child only. Transfers use one local tween; physical
    /// drops use one continuous, sequenced snapshot stream instead of restarting
    /// a tween for every packet. The NetworkObject root stays authoritative.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(NetworkWorldItem))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Motion Presenter")]
    public sealed class NetworkItemMotionPresenter : MonoBehaviour
    {
        [InfoBox("Assign the mesh-only child, not the NetworkObject root. " +
                 "Pickup/shelf use easing; physics snapshots do not.")]
        [TitleGroup("References"), Required, SerializeField]
        private NetworkWorldItem item;

        [TitleGroup("References"), Required, SerializeField]
        private Transform visualRoot;

        [TitleGroup("Motion"), MinValue(0.01f), SuffixLabel("seconds")]
        [SerializeField] private float duration = 0.18f;

        [TitleGroup("Motion"), SerializeField]
        private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [TitleGroup("Motion"), SerializeField] private bool animatePosition = true;
        [TitleGroup("Motion"), SerializeField] private bool animateRotation = true;

        // Preserve the existing serialized settings for non-physical world items.
        [TitleGroup("World Drop Settle"), SerializeField]
        private bool settleWorldDrops = true;
        [TitleGroup("World Drop Settle"), Range(0f, 0.95f), SerializeField]
        private float settleStartNormalized = 0.55f;
        [TitleGroup("World Drop Settle"), MinValue(0f), SerializeField]
        private float settleHeight = 0.018f;
        [TitleGroup("World Drop Settle"), MinValue(0f), SerializeField]
        private float settleAngle = 1.25f;
        [TitleGroup("World Drop Settle"), MinValue(0.25f), SerializeField]
        private float settleCycles = 1.25f;

        [TitleGroup("Reliability"), MinValue(0.25f), SerializeField]
        private float hierarchyReadyTimeout = 2f;

        [TitleGroup("Physics Snapshot Smoothing"), MinValue(0.01f)]
        [Tooltip("Interpolation delay, not a new tween duration per packet.")]
        [SerializeField] private float snapshotCatchUpDuration = 0.09f;

        [TitleGroup("Physics Snapshot Smoothing"), MinValue(0.01f)]
        [SerializeField] private float finalPoseCatchUpDuration = 0.12f;

        [ShowInInspector, ReadOnly, BoxGroup("Runtime")]
        public bool IsAnimating => _mode != MotionMode.Idle;
        [ShowInInspector, ReadOnly, BoxGroup("Runtime")]
        public bool IsPhysicsSettling => _mode == MotionMode.Physics;

        private enum MotionMode { Idle, AwaitingCue, Transfer, Physics }

        private struct PhysicsSample
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public double Time;
        }

        private const int MaximumBufferedSamples = 24;
        private MotionMode _mode;
        private bool _initialized;
        private bool _hasAuthoredPose;
        private Vector3 _authoredLocalPosition;
        private Quaternion _authoredLocalRotation;
        private Vector3 _authoredLocalScale;
        private Vector3 _visiblePosition;
        private Quaternion _visibleRotation;
        private bool _hasVisiblePose;
        private uint _revision;
        private bool _hasCue;
        private float _waitElapsed;

        private Vector3 _transferStartPosition;
        private Quaternion _transferStartRotation;
        private bool _transferStarted;
        private float _transferElapsed;

        // Allocated only for a client that actually observes a physical drop.
        private List<PhysicsSample> _samples;
        private uint _lastSequence;
        private bool _hasSample;
        private bool _hasFinalSample;
        private double _finalSampleTime;
        private bool _hasPlaybackTime;
        private double _playbackTime;
        private bool _serverSettleActive;
        private bool _releaseStarted;
        private float _releaseElapsed;
        private float _releaseDuration;
        private Vector3 _releasePositionOffset;
        private Quaternion _releaseRotationOffset;

        private void Reset()
        {
            AutoAssignReferences();
            CaptureAuthoredPose();
        }

        private void Awake()
        {
            AutoAssignReferences();
            CaptureAuthoredPose();
            enabled = false;
        }

        private void OnValidate()
        {
            duration = Mathf.Max(0.01f, duration);
            hierarchyReadyTimeout = Mathf.Max(0.25f, hierarchyReadyTimeout);
            snapshotCatchUpDuration = Mathf.Max(0.01f, snapshotCatchUpDuration);
            finalPoseCatchUpDuration = Mathf.Max(0.01f, finalPoseCatchUpDuration);
            settleStartNormalized = Mathf.Clamp(settleStartNormalized, 0f, 0.95f);
            settleHeight = Mathf.Max(0f, settleHeight);
            settleAngle = Mathf.Max(0f, settleAngle);
            settleCycles = Mathf.Max(0.25f, settleCycles);
            AutoAssignReferences();
            if (!Application.isPlaying)
            {
                CaptureAuthoredPose();
            }
        }

        private void OnDisable()
        {
            _mode = MotionMode.Idle;
            _transferStarted = false;
            _releaseStarted = false;
            SnapVisualToTarget();
        }

        internal void InitializePresentationLocal()
        {
            AutoAssignReferences();
            if (!_hasAuthoredPose)
            {
                CaptureAuthoredPose();
            }
            ClearMotionState();
            _initialized = true;
            _revision = item != null ? item.Revision : 0u;
            SnapVisualToTarget();
            RememberVisiblePose();
            enabled = item != null && item.IsHeld;
        }

        internal void ResetPresentationLocal()
        {
            ClearMotionState();
            _initialized = false;
            _hasVisiblePose = false;
            SnapVisualToTarget();
            enabled = false;
        }

        internal bool TryCaptureCurrentVisualPose(
            out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = visualRoot != null ? visualRoot.position : Vector3.zero;
            worldRotation = visualRoot != null ? visualRoot.rotation : Quaternion.identity;
            return HasVisual && IsFinite(worldPosition) && IsFinite(worldRotation);
        }

        internal void PreserveVisualForParentChangeLocal()
        {
            if (!_initialized || !HasVisual || !_hasVisiblePose)
            {
                return;
            }
            // NGO may still apply its serialized pose after this callback.
            // Pin again in LateUpdate, after all parent/pose messages and the
            // held-pose follower, so there is no one-frame rendered teleport.
            if (_mode == MotionMode.Idle)
            {
                _mode = MotionMode.AwaitingCue;
            }
            _waitElapsed = 0f;
            enabled = true;
            HoldVisiblePose();
        }

        internal void NotifyLocationChangedLocal(
            NetworkItemLocationState previous, NetworkItemLocationState current)
        {
            if (!_initialized || !HasVisual)
            {
                return;
            }
            if (IsNewer(current.Revision, _revision))
            {
                // State or hierarchy can lead its cue. Do not expose the
                // target pose and then travel backwards to a server hand pose.
                BeginRevision(current.Revision);
                _mode = MotionMode.AwaitingCue;
                enabled = true;
            }
            else if (current.Revision == _revision && _hasSample &&
                     _mode == MotionMode.Idle)
            {
                // Recover a buffered final pose if an unusually delayed state
                // update arrives after the hierarchy watchdog has expired.
                _mode = MotionMode.Physics;
                _waitElapsed = 0f;
                enabled = true;
            }
            else if (current.IsHeld)
            {
                // Only held/animated cases need LateUpdate. Static world cases
                // stay disabled, rather than all 4000 polling every frame.
                enabled = true;
            }
        }

        internal void PlayTransitionLocal(
            Vector3 startWorldPosition, Quaternion startWorldRotation,
            uint targetRevision, bool physicsSettle = false)
        {
            if (!EnsureInitialized() || !IsFinite(startWorldPosition) ||
                !IsFinite(startWorldRotation) || IsStaleRevision(targetRevision))
            {
                return;
            }
            if (targetRevision == _revision && (_hasCue || _hasSample))
            {
                // A sample (including final) can lead a reliable cue. Never
                // replay the hand-to-world transition for that same revision.
                _hasCue = true;
                return;
            }
            bool useDisplayedPose = _hasVisiblePose &&
                (_mode != MotionMode.Idle || item.IsHeld);
            Vector3 localStart = useDisplayedPose ? _visiblePosition : visualRoot.position;
            Quaternion localRotation = useDisplayedPose ? _visibleRotation : visualRoot.rotation;
            if (!IsFinite(localStart) || !IsFinite(localRotation))
            {
                localStart = startWorldPosition;
                localRotation = startWorldRotation;
            }
            BeginRevision(targetRevision);
            _hasCue = true;
            _visiblePosition = localStart;
            _visibleRotation = localRotation;
            _hasVisiblePose = true;
            _mode = physicsSettle ? MotionMode.Physics : MotionMode.Transfer;
            _serverSettleActive = physicsSettle;
            enabled = true;
            HoldVisiblePose();
        }

        internal void CancelTransitionLocal(uint targetRevision)
        {
            if (!_initialized || targetRevision != _revision)
            {
                return;
            }
            ClearMotionState();
            _revision = item != null ? item.Revision : 0u;
            FinishAtAuthoritativePose();
        }

        internal void SetPhysicsSettleActiveLocal(bool active, uint revision)
        {
            if (!EnsureInitialized() || IsStaleRevision(revision))
            {
                return;
            }
            if (revision != _revision)
            {
                BeginRevision(revision);
            }
            _serverSettleActive = active;
            if (active)
            {
                _mode = MotionMode.Physics;
                enabled = true;
            }
        }

        internal void QueuePhysicsPoseLocal(
            Vector3 worldPosition, Quaternion worldRotation, uint revision,
            uint sequence, double sampleTime, bool final)
        {
            if (!EnsureInitialized() || IsStaleRevision(revision) ||
                !IsFinite(worldPosition) || !IsFinite(worldRotation) ||
                double.IsNaN(sampleTime) || double.IsInfinity(sampleTime))
            {
                return;
            }
            if (revision != _revision)
            {
                BeginRevision(revision);
            }
            if (_hasFinalSample ||
                (_hasSample && !IsNewer(sequence, _lastSequence)))
            {
                return;
            }
            if (_hasSample && sampleTime < _samples[_samples.Count - 1].Time)
            {
                return;
            }
            if (_samples == null)
            {
                _samples = new List<PhysicsSample>(MaximumBufferedSamples);
            }
            PhysicsSample incoming = new PhysicsSample
            {
                Position = worldPosition, Rotation = worldRotation, Time = sampleTime
            };
            if (_samples.Count > 0 &&
                sampleTime == _samples[_samples.Count - 1].Time)
            {
                // A final reliable pose can share a server tick with the last
                // unreliable sample. Keep the newer pose for that exact time.
                _samples[_samples.Count - 1] = incoming;
            }
            else
            {
                if (_samples.Count == MaximumBufferedSamples)
                {
                    _samples.RemoveAt(0);
                }
                _samples.Add(incoming);
            }
            _hasSample = true;
            _lastSequence = sequence;
            if (final)
            {
                _hasFinalSample = true;
                _finalSampleTime = sampleTime;
            }
            _mode = MotionMode.Physics;
            enabled = true;
            // Do NOT set a transform or restart elapsed time in a network
            // callback. Even samples leading the Location update are retained.
        }

        // Compatibility for any local callers using the former presentation API.
        internal void ApplyReplicatedWorldPoseLocal(
            Vector3 worldPosition, Quaternion worldRotation, uint revision, bool final)
        {
            uint sequence = revision == _revision ? _lastSequence + 1u : 1u;
            double now = item != null && item.NetworkManager != null
                ? item.NetworkManager.ServerTime.Time : Time.timeAsDouble;
            QueuePhysicsPoseLocal(worldPosition, worldRotation, revision, sequence, now, final);
        }

        private void LateUpdate()
        {
            if (!_initialized || item == null || !item.IsSpawned || !HasVisual)
            {
                return;
            }
            if (_mode == MotionMode.Idle)
            {
                SnapVisualToTarget();
                RememberVisiblePose();
                enabled = item.IsHeld;
                return;
            }
            if (_mode == MotionMode.AwaitingCue ||
                item.Revision != _revision || !IsHierarchyReady(item.Location))
            {
                HoldVisiblePose();
                _waitElapsed += Time.deltaTime;
                if (_waitElapsed >= hierarchyReadyTimeout)
                {
                    Debug.LogWarning(
                        $"[ItemMotion] Waiting for item revision {_revision} timed out " +
                        $"on {name}; using the authoritative pose.", this);
                    FinishAtAuthoritativePose();
                }
                return;
            }
            _waitElapsed = 0f;
            if (_mode == MotionMode.Physics)
            {
                UpdatePhysicalDrop();
            }
            else
            {
                UpdateTransfer();
            }
        }

        private void UpdateTransfer()
        {
            if (!_transferStarted)
            {
                _transferStarted = true;
                _transferStartPosition = _visiblePosition;
                _transferStartRotation = _visibleRotation;
                _transferElapsed = 0f;
            }
            _transferElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_transferElapsed / duration);
            float eased = easing != null ? Mathf.Clamp01(easing.Evaluate(t))
                : Mathf.SmoothStep(0f, 1f, t);
            GetTargetWorldPose(out Vector3 targetPosition, out Quaternion targetRotation);
            Vector3 position = animatePosition
                ? Vector3.Lerp(_transferStartPosition, targetPosition, eased) : targetPosition;
            Quaternion rotation = animateRotation
                ? Quaternion.Slerp(_transferStartRotation, targetRotation, eased) : targetRotation;
            if (settleWorldDrops && item.Location.IsWorld && t > settleStartNormalized)
            {
                float settleT = Mathf.InverseLerp(settleStartNormalized, 1f, t);
                float wave = Mathf.Sin(settleT * Mathf.PI * 2f * settleCycles);
                float decay = 1f - settleT;
                position += Vector3.up * (Mathf.Abs(wave) * decay * settleHeight);
                rotation *= Quaternion.Euler(wave * decay * settleAngle * 0.35f,
                    0f, wave * decay * settleAngle);
            }
            SetVisiblePose(position, rotation);
            if (t >= 1f)
            {
                FinishAtAuthoritativePose();
            }
        }

        private void UpdatePhysicalDrop()
        {
            if (!item.Location.IsWorld || transform.parent != null)
            {
                HoldVisiblePose();
                return;
            }
            Vector3 targetPosition;
            Quaternion targetRotation;
            bool finished;
            if (item.IsServer)
            {
                // Host follows the real simulated body, not remote snapshots.
                GetTargetWorldPose(out targetPosition, out targetRotation);
                finished = !_serverSettleActive;
            }
            else
            {
                if (!_hasSample || _samples == null || _samples.Count == 0)
                {
                    HoldVisiblePose();
                    return;
                }
                PhysicsSample latest = _samples[_samples.Count - 1];
                transform.SetPositionAndRotation(latest.Position, latest.Rotation);
                double now = item.NetworkManager.ServerTime.Time;
                double requestedTime = Math.Min(
                    latest.Time, now - snapshotCatchUpDuration);
                if (!_hasPlaybackTime)
                {
                    _playbackTime = _samples[0].Time;
                    _hasPlaybackTime = true;
                }
                // Clock correction or a delayed packet must never rewind time.
                _playbackTime = Math.Max(_playbackTime, requestedTime);
                EvaluatePhysicsPose(_playbackTime,
                    out Vector3 rootPosition, out Quaternion rootRotation);
                RootPoseToVisualPose(rootPosition, rootRotation,
                    out targetPosition, out targetRotation);
                finished = _hasFinalSample && _playbackTime >= _finalSampleTime;
            }
            if (!_releaseStarted)
            {
                _releaseStarted = true;
                _releaseElapsed = 0f;
                _releaseDuration = _hasCue ? duration
                    : (_hasFinalSample ? finalPoseCatchUpDuration : snapshotCatchUpDuration);
                _releasePositionOffset = _visiblePosition - targetPosition;
                _releaseRotationOffset = _visibleRotation * Quaternion.Inverse(targetRotation);
            }
            _releaseElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_releaseElapsed / Mathf.Max(0.01f, _releaseDuration));
            float remaining = 1f - Mathf.SmoothStep(0f, 1f, t);
            // One decaying hand offset over a continuously moving physical
            // trajectory. No second ease, replay, bounce or elapsed-time reset.
            Vector3 position = animatePosition
                ? targetPosition + _releasePositionOffset * remaining : targetPosition;
            Quaternion rotation = animateRotation
                ? Quaternion.Slerp(Quaternion.identity, _releaseRotationOffset, remaining) * targetRotation
                : targetRotation;
            SetVisiblePose(position, rotation);
            if (finished && t >= 1f)
            {
                FinishAtAuthoritativePose();
            }
        }

        private void EvaluatePhysicsPose(double time,
            out Vector3 position, out Quaternion rotation)
        {
            PhysicsSample a = _samples[0];
            if (time <= a.Time)
            {
                position = a.Position;
                rotation = a.Rotation;
                return;
            }
            for (int i = 1; i < _samples.Count; i++)
            {
                PhysicsSample b = _samples[i];
                if (time <= b.Time)
                {
                    float t = (float)((time - a.Time) / Math.Max(0.000001d, b.Time - a.Time));
                    position = Vector3.Lerp(a.Position, b.Position, t);
                    rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
                    return;
                }
                a = b;
            }
            // Packet loss: hold the newest pose. Never invent another fall.
            position = a.Position;
            rotation = a.Rotation;
        }

        private void RootPoseToVisualPose(Vector3 rootPosition, Quaternion rootRotation,
            out Vector3 position, out Quaternion rotation)
        {
            GetTargetWorldPose(out Vector3 authoredPosition, out Quaternion authoredRotation);
            Quaternion delta = rootRotation * Quaternion.Inverse(transform.rotation);
            position = rootPosition + delta * (authoredPosition - transform.position);
            rotation = delta * authoredRotation;
        }

        private void BeginRevision(uint revision)
        {
            ClearMotionState();
            _revision = revision;
        }

        private void ClearMotionState()
        {
            _mode = MotionMode.Idle;
            _hasCue = false;
            _hasSample = false;
            _hasFinalSample = false;
            _hasPlaybackTime = false;
            _lastSequence = 0;
            _samples?.Clear();
            _transferStarted = false;
            _releaseStarted = false;
            _serverSettleActive = false;
            _waitElapsed = 0f;
        }

        private bool IsStaleRevision(uint revision)
        {
            return IsNewer(_revision, revision) || IsNewer(item.Revision, revision);
        }

        private static bool IsNewer(uint value, uint previous)
        {
            return unchecked((int)(value - previous)) > 0;
        }

        private bool IsHierarchyReady(NetworkItemLocationState state)
        {
            switch (state.Kind)
            {
                case NetworkItemLocationKind.World:
                    return transform.parent == null;
                case NetworkItemLocationKind.Held:
                    NetworkItemCarrier carrier = GetComponentInParent<NetworkItemCarrier>();
                    return carrier != null && carrier.OwnerClientId == state.HolderClientId &&
                           carrier.ContainsHeldItem(item);
                case NetworkItemLocationKind.Placed:
                    return item.TryResolveReceiver(out NetworkItemReceiver receiver) &&
                           transform.IsChildOf(receiver.transform);
                default:
                    return false;
            }
        }

        private bool HasVisual => visualRoot != null && visualRoot != transform && _hasAuthoredPose;

        private bool EnsureInitialized()
        {
            if (!_initialized && item != null && item.IsSpawned)
            {
                InitializePresentationLocal();
            }
            return _initialized && item != null && item.IsSpawned && HasVisual;
        }

        private void HoldVisiblePose()
        {
            if (_hasVisiblePose && HasVisual)
            {
                visualRoot.SetPositionAndRotation(_visiblePosition, _visibleRotation);
                visualRoot.localScale = _authoredLocalScale;
            }
        }

        private void SetVisiblePose(Vector3 position, Quaternion rotation)
        {
            visualRoot.SetPositionAndRotation(position, rotation);
            visualRoot.localScale = _authoredLocalScale;
            RememberVisiblePose();
        }

        private void RememberVisiblePose()
        {
            if (HasVisual)
            {
                _visiblePosition = visualRoot.position;
                _visibleRotation = visualRoot.rotation;
                _hasVisiblePose = true;
            }
        }

        private void FinishAtAuthoritativePose()
        {
            _mode = MotionMode.Idle;
            _transferStarted = false;
            _releaseStarted = false;
            SnapVisualToTarget();
            RememberVisiblePose();
            enabled = item != null && item.IsHeld;
        }

        private void GetTargetWorldPose(out Vector3 position, out Quaternion rotation)
        {
            Transform parent = visualRoot.parent;
            position = parent != null ? parent.TransformPoint(_authoredLocalPosition) : _authoredLocalPosition;
            rotation = parent != null ? parent.rotation * _authoredLocalRotation : _authoredLocalRotation;
        }

        private void SnapVisualToTarget()
        {
            if (!HasVisual)
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

        private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        private static bool IsFinite(Quaternion v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z) && IsFinite(v.w);
        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
