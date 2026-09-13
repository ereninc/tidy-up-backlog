using System;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Base class for an instant, server-authoritative network interaction.
    /// Looking at an object is local-only; only an explicit use request reaches
    /// the server.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public abstract class NetworkInteractable : NetworkBehaviour
    {
        [InfoBox(
            "Focus and prompt evaluation are local and packet-free. The actual " +
            "state change runs only after the server validates sender, origin, " +
            "aim, distance, line-of-sight, availability and cooldown.")]
        [TitleGroup("Interaction")]
        [Tooltip("Optional title shown above the interaction prompt.")]
        [SerializeField] private string interactionDisplayName;

        [TitleGroup("Interaction")]
        [SerializeField] private string interactionPrompt = "Interact";

        [TitleGroup("Interaction")]
        [MinValue(0.1f)]
        [SuffixLabel("m")]
        [SerializeField] private float maximumInteractionDistance = 3.25f;

        [TitleGroup("Interaction")]
        [MinValue(0f)]
        [SuffixLabel("s")]
        [Tooltip("Server-side cooldown shared by every player for this object.")]
        [SerializeField] private float serverCooldown = 0.1f;

        [TitleGroup("Presentation")]
        [Tooltip(
            "Optional local-only highlight object. Do not assign this component's " +
            "own GameObject or another NetworkObject root.")]
        [SerializeField] private GameObject localFocusVisual;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Spawned")]
        private bool RuntimeSpawned => IsSpawned;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Server")]
        private bool RuntimeServer => IsSpawned && IsServer;

        [ShowInInspector]
        [ReadOnly]
        [BoxGroup("Runtime")]
        [LabelText("Locally Focused")]
        private bool RuntimeLocallyFocused => _isLocallyFocused;

        public float MaximumInteractionDistance => maximumInteractionDistance;
        public float ServerCooldown => serverCooldown;
        public bool IsLocallyFocused => _isLocallyFocused;

        private bool _isLocallyFocused;
        private double _nextAllowedServerTime;

        protected virtual void Reset()
        {
            ValidateConfiguration();
        }

        protected virtual void Awake()
        {
            SetFocusVisual(false);
        }

        protected virtual void OnValidate()
        {
            ValidateConfiguration();

            if (!Application.isPlaying)
            {
                SetFocusVisual(false);
            }
        }

        protected virtual void OnDisable()
        {
            _isLocallyFocused = false;
            SetFocusVisual(false);
        }

        /// <summary>
        /// Called every frame only for the local owner while this is the nearest
        /// unobstructed candidate. Do not use this as an authority check.
        /// </summary>
        public virtual bool IsAvailableLocally(
            NetworkInteractionController interactor)
        {
            return isActiveAndEnabled && IsSpawned;
        }

        public virtual string GetInteractionPrompt(
            NetworkInteractionController interactor)
        {
            return string.IsNullOrWhiteSpace(interactionPrompt)
                ? "Interact"
                : interactionPrompt;
        }

        public virtual string GetInteractionDisplayName(
            NetworkInteractionController interactor)
        {
            return string.IsNullOrWhiteSpace(interactionDisplayName)
                ? gameObject.name
                : interactionDisplayName;
        }

        internal void SetLocallyFocused(
            bool focused,
            NetworkInteractionController interactor)
        {
            if (_isLocallyFocused == focused)
            {
                return;
            }

            _isLocallyFocused = focused;
            SetFocusVisual(focused);
            OnLocalFocusChanged(focused, interactor);
        }

        internal bool TryExecuteServer(
            NetworkInteractionContext context,
            out InteractionRejectReason rejectReason,
            out string message)
        {
            rejectReason = InteractionRejectReason.None;
            message = string.Empty;

            if (!IsSpawned || !IsServer || !isActiveAndEnabled)
            {
                rejectReason = InteractionRejectReason.TargetNotSpawned;
                message = "Target is not available on the server.";
                return false;
            }

            double now = Time.realtimeSinceStartupAsDouble;

            if (now < _nextAllowedServerTime)
            {
                rejectReason = InteractionRejectReason.RateLimited;
                message = "Target is cooling down.";
                return false;
            }

            if (!CanInteractServer(context, out string unavailableMessage))
            {
                rejectReason = InteractionRejectReason.Unavailable;
                message = string.IsNullOrWhiteSpace(unavailableMessage)
                    ? "Target is currently unavailable."
                    : unavailableMessage;
                return false;
            }

            try
            {
                if (!ExecuteInteractionServer(
                        context,
                        out string executionMessage))
                {
                    rejectReason = InteractionRejectReason.ExecutionFailed;
                    message = string.IsNullOrWhiteSpace(executionMessage)
                        ? "Interaction could not be completed."
                        : executionMessage;
                    return false;
                }

                _nextAllowedServerTime = now + serverCooldown;
                message = string.IsNullOrWhiteSpace(executionMessage)
                    ? "Interaction completed."
                    : executionMessage;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                rejectReason = InteractionRejectReason.ExecutionFailed;
                message = "Interaction threw a server exception.";
                return false;
            }
        }

        /// <summary>
        /// Override for game rules such as inventory capacity, locks, permissions
        /// or object state. This executes on the server only.
        /// </summary>
        protected virtual bool CanInteractServer(
            NetworkInteractionContext context,
            out string rejectionMessage)
        {
            rejectionMessage = string.Empty;
            return true;
        }

        /// <summary>
        /// Perform the authoritative state mutation here. Replicate its result
        /// through NetworkVariables or network-spawn/despawn operations.
        /// </summary>
        protected abstract bool ExecuteInteractionServer(
            NetworkInteractionContext context,
            out string resultMessage);

        protected virtual void OnLocalFocusChanged(
            bool focused,
            NetworkInteractionController interactor)
        {
        }

        private void ValidateConfiguration()
        {
            maximumInteractionDistance = Mathf.Max(
                0.1f,
                maximumInteractionDistance);
            serverCooldown = Mathf.Max(0f, serverCooldown);

            if (localFocusVisual == gameObject)
            {
                Debug.LogWarning(
                    "[Interaction] Focus visual cannot be the interactable root. " +
                    "The reference was cleared.",
                    this);
                localFocusVisual = null;
            }
        }

        private void SetFocusVisual(bool visible)
        {
            if (localFocusVisual != null &&
                localFocusVisual.activeSelf != visible)
            {
                localFocusVisual.SetActive(visible);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, maximumInteractionDistance);
        }
    }
}
