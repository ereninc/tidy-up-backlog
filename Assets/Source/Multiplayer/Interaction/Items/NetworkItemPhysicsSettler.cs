using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Temporarily enables Rigidbody simulation for one freshly dropped item on
    /// the server. It emits low-rate pose samples while active, freezes the body
    /// after it actually settles, then sends one reliable final pose.
    ///
    /// While settling, the dropped item ignores the carrier's CURRENT held stack.
    /// This also covers swap operations where a new item enters the stack after
    /// the dropped item has already started settling.
    /// </summary>
    [RequireComponent(typeof(NetworkWorldItem))]
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Multiplayer/Items/Network Item Physics Settler")]
    public sealed class NetworkItemPhysicsSettler : MonoBehaviour
    {
        private static readonly WaitForFixedUpdate FixedUpdateWait =
            new WaitForFixedUpdate();

        [Header("Lifetime")]
        [SerializeField, Min(0.05f)]
        private float minimumSettleTime = 0.28f;

        [SerializeField, Min(0.2f)]
        private float maximumSettleTime = 1.9f;

        [SerializeField, Min(0.03f)]
        private float snapshotInterval = 0.08f;

        [Header("Rest Detection")]
        [SerializeField, Min(0.001f)]
        private float restingLinearSpeed = 0.065f;

        [SerializeField, Min(0.001f)]
        private float restingAngularSpeed = 0.32f;

        [SerializeField, Range(1, 12)]
        private int requiredStableSteps = 4;

        [SerializeField, Range(-1f, 1f)]
        private float minimumSupportNormalUpDot = 0.12f;

        [Header("Temporary Physics")]
        [SerializeField, Min(0f)]
        private float settlingLinearDamping = 1.35f;

        [SerializeField, Min(0f)]
        private float settlingAngularDamping = 2.1f;

        [Tooltip("Small forward push in the player's facing direction.")]
        [SerializeField, Min(0f)]
        private float initialForwardSpeed = 0.85f;

        [SerializeField, Min(0f)]
        private float initialDownwardSpeed = 0.24f;

        [Tooltip("Small deterministic sideways variation.")]
        [SerializeField, Min(0f)]
        private float initialPlanarSpeed = 0.035f;

        [SerializeField, Min(0f)]
        private float initialAngularSpeed = 0.55f;

        public bool IsSettling => _isSettling;

        private NetworkWorldItem _item;
        private Rigidbody _body;
        private NetworkItemMotionPresenter _motionPresenter;
        private Coroutine _routine;

        private uint _revision;
        private bool _isSettling;

        private float _elapsed;
        private float _snapshotElapsed;
        private float _lastSupportedAt = float.NegativeInfinity;

        private int _stableSteps;

        private bool _savedUseGravity;
        private bool _savedDetectCollisions;

        private CollisionDetectionMode _savedCollisionDetectionMode;
        private RigidbodyInterpolation _savedInterpolation;
        private RigidbodyConstraints _savedConstraints;

        private float _savedLinearDamping;
        private float _savedAngularDamping;

        private bool _hasPreparedDropContext;

        private Vector3 _preparedReleaseForward =
            Vector3.forward;

        private NetworkItemCarrier _preparedCarrier;
        private NetworkItemCarrier _activeCarrier;

        private readonly List<IgnoredCollisionPair> _activeIgnoredPairs =
            new List<IgnoredCollisionPair>();

        private readonly HashSet<Collider> _currentHeldColliders =
            new HashSet<Collider>();

        private readonly struct IgnoredCollisionPair
        {
            public readonly Collider A;
            public readonly Collider B;

            public IgnoredCollisionPair(
                Collider a,
                Collider b)
            {
                A = a;
                B = b;
            }
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        #region Drop Context

        internal static void PrepareDropContextServer(
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            Vector3 releaseForward)
        {
            if (item == null ||
                carrier == null ||
                !item.IsSpawned ||
                !item.IsServer)
            {
                return;
            }

            NetworkItemPhysicsSettler settler =
                item.GetComponent<NetworkItemPhysicsSettler>();

            if (settler == null)
            {
                settler =
                    item.gameObject.AddComponent<
                        NetworkItemPhysicsSettler>();
            }

            settler.PrepareDropContext(
                item,
                carrier,
                releaseForward);
        }

        private void PrepareDropContext(
            NetworkWorldItem item,
            NetworkItemCarrier carrier,
            Vector3 releaseForward)
        {
            Vector3 planarForward =
                Vector3.ProjectOnPlane(
                    releaseForward,
                    Vector3.up);

            if (planarForward.sqrMagnitude < 0.0001f)
            {
                planarForward =
                    Vector3.ProjectOnPlane(
                        carrier.transform.forward,
                        Vector3.up);
            }

            if (planarForward.sqrMagnitude < 0.0001f)
            {
                planarForward =
                    Vector3.forward;
            }

            _preparedReleaseForward =
                planarForward.normalized;

            /*
             * Important:
             *
             * We intentionally keep the carrier reference instead of taking
             * a collider snapshot here.
             *
             * During a full-stack swap:
             *
             * 1. Old top item starts dropping.
             * 2. It begins settling.
             * 3. New world item gets picked up.
             * 4. New item enters the held stack.
             *
             * A collider snapshot taken at step 1 would miss the new item.
             */
            _preparedCarrier =
                carrier;

            _hasPreparedDropContext =
                true;
        }

        private void ClearPreparedDropContext()
        {
            _preparedCarrier =
                null;

            _preparedReleaseForward =
                Vector3.forward;

            _hasPreparedDropContext =
                false;
        }

        #endregion

        #region Public Server API

        internal static bool CanSettleServer(
            NetworkWorldItem item)
        {
            return item != null &&
                   item.IsSpawned &&
                   item.IsServer &&
                   item.TryGetComponent(
                       out Rigidbody body) &&
                   body != null;
        }

        internal static bool IsSettlingServer(
            NetworkWorldItem item)
        {
            return item != null &&
                   item.TryGetComponent(
                       out NetworkItemPhysicsSettler settler) &&
                   settler != null &&
                   settler._isSettling;
        }

        internal static bool TryBeginServer(
            NetworkWorldItem item,
            uint revision,
            out string error)
        {
            error =
                string.Empty;

            if (!CanSettleServer(item) ||
                !item.Location.IsWorld ||
                item.Revision != revision ||
                item.transform.parent != null)
            {
                error =
                    "Item is not ready for server physics settle.";

                return false;
            }

            NetworkItemPhysicsSettler settler =
                item.GetComponent<
                    NetworkItemPhysicsSettler>();

            if (settler == null)
            {
                settler =
                    item.gameObject.AddComponent<
                        NetworkItemPhysicsSettler>();
            }

            return settler.BeginServer(
                item,
                revision,
                out error);
        }

        internal static bool CancelServer(
            NetworkWorldItem item)
        {
            if (item == null ||
                !item.IsServer ||
                !item.TryGetComponent(
                    out NetworkItemPhysicsSettler settler) ||
                settler == null ||
                !settler._isSettling)
            {
                return false;
            }

            settler.CancelInternal(
                true);

            return true;
        }

        #endregion

        #region Settle

        private bool BeginServer(
            NetworkWorldItem item,
            uint revision,
            out string error)
        {
            error =
                string.Empty;

            ValidateSettings();

            _item =
                item;

            _body =
                GetComponent<Rigidbody>();

            _motionPresenter =
                GetComponent<
                    NetworkItemMotionPresenter>();

            if (_body == null)
            {
                error =
                    "Dropped item has no Rigidbody.";

                return false;
            }

            if (_isSettling)
            {
                CancelInternal(
                    true);
            }

            enabled =
                true;

            SaveBodyConfiguration();

            _revision =
                revision;

            _elapsed =
                0f;

            _snapshotElapsed =
                0f;

            _lastSupportedAt =
                float.NegativeInfinity;

            _stableSteps =
                0;

            _isSettling =
                true;

            _motionPresenter
                ?.SetPhysicsSettleActiveLocal(
                    true,
                    revision);

            BeginIgnoringCarrierStack();

            ConfigureDynamicBody();

            _routine =
                StartCoroutine(
                    SettleRoutine());

            return true;
        }

        private IEnumerator SettleRoutine()
        {
            while (_isSettling)
            {
                yield return FixedUpdateWait;

                if (!_isSettling)
                {
                    yield break;
                }

                if (!IsStillTheSameWorldDrop())
                {
                    CancelInternal(
                        true);

                    yield break;
                }

                /*
                 * Another capability may temporarily own the Rigidbody.
                 */
                if (_body.isKinematic)
                {
                    continue;
                }

                float step =
                    Mathf.Max(
                        0.0001f,
                        Time.fixedDeltaTime);

                _elapsed +=
                    step;

                _snapshotElapsed +=
                    step;

                if (_snapshotElapsed >=
                    snapshotInterval)
                {
                    _snapshotElapsed =
                        0f;

                    BroadcastPose(
                        false);
                }

                bool supportedRecently =
                    _body.IsSleeping() ||
                    _elapsed -
                    _lastSupportedAt <=
                    step *
                    (requiredStableSteps + 2f);

                Vector3 linearVelocity =
                    GetLinearVelocity(
                        _body);

                bool slowEnough =
                    linearVelocity.sqrMagnitude <=
                    restingLinearSpeed *
                    restingLinearSpeed &&
                    _body.angularVelocity.sqrMagnitude <=
                    restingAngularSpeed *
                    restingAngularSpeed;

                bool canFreeze =
                    supportedRecently &&
                    (_body.IsSleeping() ||
                     slowEnough);

                if (_elapsed >=
                        minimumSettleTime &&
                    canFreeze)
                {
                    _stableSteps++;
                }
                else
                {
                    _stableSteps =
                        0;
                }

                /*
                 * maximumSettleTime is NOT allowed to freeze an unsupported
                 * airborne item.
                 */
                bool timedOutButSafeToFreeze =
                    _elapsed >=
                    maximumSettleTime &&
                    canFreeze;

                if (_stableSteps >=
                        requiredStableSteps ||
                    timedOutButSafeToFreeze)
                {
                    FinalizeServer(
                        false);

                    yield break;
                }
            }
        }

        private bool IsStillTheSameWorldDrop()
        {
            return _item != null &&
                   _body != null &&
                   _item.IsSpawned &&
                   _item.IsServer &&
                   _item.Location.IsWorld &&
                   _item.Revision ==
                   _revision &&
                   _item.transform.parent ==
                   null;
        }

        #endregion

        #region Carrier Collision Ignore

        private void BeginIgnoringCarrierStack()
        {
            /*
             * Defensive cleanup in case this component is reused.
             */
            EndIgnoringCarrierStack(
                true);

            if (!_hasPreparedDropContext ||
                _preparedCarrier == null)
            {
                return;
            }

            _activeCarrier =
                _preparedCarrier;

            _activeCarrier.HeldStackChanged +=
                HandleCarrierStackChanged;

            RefreshCarrierCollisionIgnores();
        }

        private void HandleCarrierStackChanged()
        {
            if (!_isSettling)
            {
                return;
            }

            RefreshCarrierCollisionIgnores();
        }

        private void RefreshCarrierCollisionIgnores()
        {
            if (_activeCarrier == null ||
                _item == null)
            {
                return;
            }

            _currentHeldColliders.Clear();

            /*
             * Read the LIVE held stack.
             *
             * This is what fixes rapid full-stack swapping:
             * anything picked up after this case started falling is added here.
             */
            for (int i = 0;
                 i < _activeCarrier.HeldItemCount;
                 i++)
            {
                if (!_activeCarrier.TryGetHeldItemAt(
                        i,
                        out NetworkWorldItem heldItem) ||
                    heldItem == null ||
                    heldItem == _item)
                {
                    continue;
                }

                Collider[] colliders =
                    heldItem.GetComponentsInChildren<
                        Collider>(true);

                for (int j = 0;
                     j < colliders.Length;
                     j++)
                {
                    Collider candidate =
                        colliders[j];

                    if (candidate == null ||
                        !candidate.enabled ||
                        candidate.isTrigger ||
                        !candidate.gameObject
                            .activeInHierarchy)
                    {
                        continue;
                    }

                    _currentHeldColliders.Add(
                        candidate);
                }
            }

            /*
             * Restore collisions against objects which have LEFT the hand.
             *
             * Example:
             * A is falling and ignores B because B is held.
             * B gets dropped too.
             *
             * A and B should now interact normally as world objects.
             */
            for (int i =
                     _activeIgnoredPairs.Count - 1;
                 i >= 0;
                 i--)
            {
                IgnoredCollisionPair pair =
                    _activeIgnoredPairs[i];

                if (pair.A == null ||
                    pair.B == null)
                {
                    _activeIgnoredPairs
                        .RemoveAt(i);

                    continue;
                }

                if (_currentHeldColliders.Contains(
                        pair.B))
                {
                    continue;
                }

                Physics.IgnoreCollision(
                    pair.A,
                    pair.B,
                    false);

                _activeIgnoredPairs
                    .RemoveAt(i);
            }

            Collider[] ownColliders =
                GetComponentsInChildren<
                    Collider>(true);

            foreach (Collider heldCollider
                     in _currentHeldColliders)
            {
                if (heldCollider == null)
                {
                    continue;
                }

                for (int i = 0;
                     i < ownColliders.Length;
                     i++)
                {
                    Collider own =
                        ownColliders[i];

                    if (own == null ||
                        !own.enabled ||
                        own.isTrigger ||
                        own == heldCollider ||
                        !own.gameObject
                            .activeInHierarchy)
                    {
                        continue;
                    }

                    /*
                     * If it is already ignored, either we already handled it or
                     * another system owns that ignore. Do not duplicate it.
                     */
                    if (Physics.GetIgnoreCollision(
                            own,
                            heldCollider))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(
                        own,
                        heldCollider,
                        true);

                    _activeIgnoredPairs.Add(
                        new IgnoredCollisionPair(
                            own,
                            heldCollider));
                }
            }
        }

        private void EndIgnoringCarrierStack(
            bool restoreCollisions)
        {
            if (_activeCarrier != null)
            {
                _activeCarrier.HeldStackChanged -=
                    HandleCarrierStackChanged;
            }

            _activeCarrier =
                null;

            _currentHeldColliders.Clear();

            if (restoreCollisions)
            {
                RestoreIgnoredCollisions();
            }
        }

        private void RestoreIgnoredCollisions()
        {
            for (int i = 0;
                 i < _activeIgnoredPairs.Count;
                 i++)
            {
                IgnoredCollisionPair pair =
                    _activeIgnoredPairs[i];

                if (pair.A == null ||
                    pair.B == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(
                    pair.A,
                    pair.B,
                    false);
            }

            _activeIgnoredPairs.Clear();
        }

        #endregion

        #region Rigidbody

        private void ConfigureDynamicBody()
        {
            _body.isKinematic =
                true;

            _body.useGravity =
                true;

            _body.detectCollisions =
                true;

            _body.constraints =
                RigidbodyConstraints.None;

            _body.collisionDetectionMode =
                CollisionDetectionMode
                    .ContinuousSpeculative;

            _body.interpolation =
                RigidbodyInterpolation
                    .Interpolate;

            SetDamping(
                _body,
                settlingLinearDamping,
                settlingAngularDamping);

            _body.isKinematic =
                false;

            CalculateInitialMotion(
                out Vector3 linearVelocity,
                out Vector3 angularVelocity);

            SetLinearVelocity(
                _body,
                linearVelocity);

            _body.angularVelocity =
                angularVelocity;

            _body.WakeUp();
        }

        private void CalculateInitialMotion(
            out Vector3 linearVelocity,
            out Vector3 angularVelocity)
        {
            ulong id =
                _item != null &&
                _item.IsSpawned
                    ? _item.NetworkObjectId
                    : 1UL;

            uint hash =
                unchecked(
                    (uint)(id ^
                           (id >> 32)));

            hash ^=
                hash << 13;

            hash ^=
                hash >> 17;

            hash ^=
                hash << 5;

            float angle =
                (hash & 0xffffu) /
                65535f *
                Mathf.PI *
                2f;

            Vector3 randomPlanarDirection =
                new Vector3(
                    Mathf.Cos(angle),
                    0f,
                    Mathf.Sin(angle));

            Vector3 forward =
                _hasPreparedDropContext
                    ? _preparedReleaseForward
                    : Vector3.ProjectOnPlane(
                        transform.forward,
                        Vector3.up);

            if (forward.sqrMagnitude <
                0.0001f)
            {
                forward =
                    Vector3.forward;
            }

            forward.Normalize();

            linearVelocity =
                forward *
                initialForwardSpeed +
                Vector3.down *
                initialDownwardSpeed +
                randomPlanarDirection *
                initialPlanarSpeed;

            Vector3 rotationAxis =
                new Vector3(
                    -randomPlanarDirection.z,
                    0.18f,
                    randomPlanarDirection.x)
                .normalized;

            float signedSpeed =
                ((hash & 1u) == 0u
                    ? -1f
                    : 1f) *
                initialAngularSpeed;

            angularVelocity =
                rotationAxis *
                signedSpeed;
        }

        private void SaveBodyConfiguration()
        {
            _savedUseGravity =
                _body.useGravity;

            _savedDetectCollisions =
                _body.detectCollisions;

            _savedCollisionDetectionMode =
                _body.collisionDetectionMode;

            _savedInterpolation =
                _body.interpolation;

            _savedConstraints =
                _body.constraints;

            GetDamping(
                _body,
                out _savedLinearDamping,
                out _savedAngularDamping);
        }

        private void RestoreAndFreezeBody()
        {
            if (_body == null)
            {
                return;
            }

            if (!_body.isKinematic)
            {
                SetLinearVelocity(
                    _body,
                    Vector3.zero);

                _body.angularVelocity =
                    Vector3.zero;
            }

            _body.isKinematic =
                true;

            _body.useGravity =
                _savedUseGravity;

            _body.detectCollisions =
                _savedDetectCollisions;

            _body.constraints =
                _savedConstraints;

            _body.collisionDetectionMode =
                _savedCollisionDetectionMode;

            _body.interpolation =
                _savedInterpolation;

            SetDamping(
                _body,
                _savedLinearDamping,
                _savedAngularDamping);

            _body.Sleep();
        }

        #endregion

        #region Finish / Cancel

        private void FinalizeServer(
            bool preserveExternalBodyState)
        {
            if (!_isSettling)
            {
                return;
            }

            _isSettling =
                false;

            _routine =
                null;

            EndIgnoringCarrierStack(
                true);

            if (!preserveExternalBodyState)
            {
                RestoreAndFreezeBody();
            }

            _motionPresenter
                ?.SetPhysicsSettleActiveLocal(
                    false,
                    _revision);

            ClearPreparedDropContext();

            Physics.SyncTransforms();

            BroadcastPose(
                true);

            enabled =
                false;
        }

        private void CancelInternal(
            bool restoreBody)
        {
            if (!_isSettling)
            {
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(
                    _routine);

                _routine =
                    null;
            }

            EndIgnoringCarrierStack(
                true);

            if (restoreBody &&
                _body != null)
            {
                RestoreAndFreezeBody();
            }

            _motionPresenter
                ?.SetPhysicsSettleActiveLocal(
                    false,
                    _revision);

            ClearPreparedDropContext();

            _isSettling =
                false;

            enabled =
                false;
        }

        #endregion

        #region Networking

        private void BroadcastPose(
            bool final)
        {
            if (_item == null ||
                !_item.IsSpawned ||
                !_item.IsServer ||
                !_item.Location.IsWorld ||
                _item.Revision !=
                _revision ||
                !IsFinite(
                    transform.position) ||
                !IsFinite(
                    transform.rotation))
            {
                return;
            }

            _item.BroadcastPhysicsPoseServer(
                transform.position,
                transform.rotation,
                _revision,
                final);
        }

        #endregion

        #region Support Detection

        private void OnCollisionEnter(
            Collision collision)
        {
            RegisterSupport(
                collision);
        }

        private void OnCollisionStay(
            Collision collision)
        {
            RegisterSupport(
                collision);
        }

        private void RegisterSupport(
            Collision collision)
        {
            if (!_isSettling ||
                collision == null ||
                collision.collider == null)
            {
                return;
            }

            /*
             * The player / carry stack can NEVER be considered legitimate
             * physical support for a dropped item.
             */
            if (collision.collider
                    .GetComponentInParent<
                        NetworkItemCarrier>() !=
                null)
            {
                return;
            }

            /*
             * Extra safety:
             * Even if hierarchy / collider setup changes later, a held item
             * still cannot count as ground support.
             */
            NetworkWorldItem supportItem =
                collision.collider
                    .GetComponentInParent<
                        NetworkWorldItem>();

            if (supportItem != null &&
                supportItem.IsHeld)
            {
                return;
            }

            for (int i = 0;
                 i < collision.contactCount;
                 i++)
            {
                ContactPoint contact =
                    collision.GetContact(i);

                if (Vector3.Dot(
                        contact.normal,
                        Vector3.up) <
                    minimumSupportNormalUpDot)
                {
                    continue;
                }

                _lastSupportedAt =
                    _elapsed;

                return;
            }
        }

        #endregion

        private void OnDisable()
        {
            if (_isSettling)
            {
                CancelInternal(
                    true);
            }
        }

        #region Unity Version Helpers

        private static void GetDamping(
            Rigidbody body,
            out float linear,
            out float angular)
        {
#if UNITY_6000_0_OR_NEWER
            linear =
                body.linearDamping;

            angular =
                body.angularDamping;
#else
            linear = body.drag;
            angular = body.angularDrag;
#endif
        }

        private static void SetDamping(
            Rigidbody body,
            float linear,
            float angular)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearDamping =
                linear;

            body.angularDamping =
                angular;
#else
            body.drag = linear;
            body.angularDrag = angular;
#endif
        }

        private static Vector3 GetLinearVelocity(
            Rigidbody body)
        {
#if UNITY_6000_0_OR_NEWER
            return body.linearVelocity;
#else
            return body.velocity;
#endif
        }

        private static void SetLinearVelocity(
            Rigidbody body,
            Vector3 value)
        {
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity =
                value;
#else
            body.velocity = value;
#endif
        }

        #endregion

        #region Validation

        private static bool IsFinite(
            Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(
            Quaternion value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z) &&
                   IsFinite(value.w);
        }

        private static bool IsFinite(
            float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }

        private void ValidateSettings()
        {
            minimumSettleTime =
                Mathf.Max(
                    0.05f,
                    minimumSettleTime);

            maximumSettleTime =
                Mathf.Max(
                    minimumSettleTime,
                    maximumSettleTime);

            snapshotInterval =
                Mathf.Max(
                    0.03f,
                    snapshotInterval);

            restingLinearSpeed =
                Mathf.Max(
                    0.001f,
                    restingLinearSpeed);

            restingAngularSpeed =
                Mathf.Max(
                    0.001f,
                    restingAngularSpeed);

            requiredStableSteps =
                Mathf.Clamp(
                    requiredStableSteps,
                    1,
                    12);

            minimumSupportNormalUpDot =
                Mathf.Clamp(
                    minimumSupportNormalUpDot,
                    -1f,
                    1f);

            settlingLinearDamping =
                Mathf.Max(
                    0f,
                    settlingLinearDamping);

            settlingAngularDamping =
                Mathf.Max(
                    0f,
                    settlingAngularDamping);

            initialForwardSpeed =
                Mathf.Max(
                    0f,
                    initialForwardSpeed);

            initialDownwardSpeed =
                Mathf.Max(
                    0f,
                    initialDownwardSpeed);

            initialPlanarSpeed =
                Mathf.Max(
                    0f,
                    initialPlanarSpeed);

            initialAngularSpeed =
                Mathf.Max(
                    0f,
                    initialAngularSpeed);
        }

        #endregion
    }
}