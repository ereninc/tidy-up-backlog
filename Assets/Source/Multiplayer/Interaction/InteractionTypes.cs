using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Stable, UI-friendly reason codes returned by the authoritative server.
    /// Game-specific interactables can add detail through the accompanying message.
    /// </summary>
    public enum InteractionRejectReason : byte
    {
        None = 0,
        NotReady = 1,
        NotOwner = 2,
        NoFocusedTarget = 3,
        RateLimited = 4,
        InvalidSender = 5,
        InvalidTarget = 6,
        TargetNotSpawned = 7,
        InvalidOrigin = 8,
        InvalidAim = 9,
        OutOfRange = 10,
        Obstructed = 11,
        Unavailable = 12,
        ExecutionFailed = 13,
        TimedOut = 14
    }

    public enum InteractionResultState : byte
    {
        None = 0,
        Pending = 1,
        Succeeded = 2,
        Rejected = 3
    }

    /// <summary>
    /// Local representation of the latest request/result. This is intentionally
    /// not network-serialized; it feeds HUD, audio and presentation code.
    /// </summary>
    public readonly struct InteractionResult
    {
        public InteractionResult(
            uint sequence,
            InteractionResultState state,
            InteractionRejectReason reason,
            string message)
        {
            Sequence = sequence;
            State = state;
            Reason = reason;
            Message = message ?? string.Empty;
        }

        public uint Sequence { get; }
        public InteractionResultState State { get; }
        public InteractionRejectReason Reason { get; }
        public string Message { get; }
        public bool IsFinal =>
            State == InteractionResultState.Succeeded ||
            State == InteractionResultState.Rejected;

        public static InteractionResult Empty =>
            new InteractionResult(
                0,
                InteractionResultState.None,
                InteractionRejectReason.None,
                string.Empty);
    }

    /// <summary>
    /// Server-only execution context supplied to concrete interactables. Never
    /// trust client-owned state inside an interactable; use these server-validated
    /// values instead.
    /// </summary>
    public readonly struct NetworkInteractionContext
    {
        public NetworkInteractionContext(
            ulong senderClientId,
            NetworkInteractionController playerController,
            NetworkInteractable target,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 hitPoint,
            Vector3 hitNormal,
            double serverTime)
        {
            SenderClientId = senderClientId;
            PlayerController = playerController;
            Target = target;
            RayOrigin = rayOrigin;
            RayDirection = rayDirection;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            ServerTime = serverTime;
        }

        public ulong SenderClientId { get; }
        public NetworkInteractionController PlayerController { get; }
        public NetworkInteractable Target { get; }
        public Vector3 RayOrigin { get; }
        public Vector3 RayDirection { get; }
        public Vector3 HitPoint { get; }
        public Vector3 HitNormal { get; }
        public double ServerTime { get; }
    }
}
