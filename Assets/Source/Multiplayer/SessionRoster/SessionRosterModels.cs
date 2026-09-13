using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EXW.Multiplayer
{
    public enum SessionSlotRetentionPolicy
    {
        UntilSessionEnds = 0,
        Timed = 1,
        ReleaseImmediately = 2
    }

    public enum GracefulExitSlotPolicy
    {
        PreserveSlot = 0,
        ReleaseSlot = 1
    }

    public enum LastSessionRejoinMode
    {
        Prompt = 0,
        BrowserOnly = 1,
        Disabled = 2
    }

    public enum SessionRosterAdmissionFailure
    {
        None,
        InvalidSteamId,
        InvalidLobbyState,
        AlreadyConnected,
        RejoinDisabled,
        LateJoinDisabled,
        SessionRosterFull
    }

    public enum SessionSlotReleaseReason
    {
        WaitingRoomLeave,
        GracefulExitPolicy,
        RetentionDisabled,
        RetentionExpired,
        HostReleased,
        SessionEnded
    }

    public sealed class SessionRosterSlotSnapshot
    {
        public int SlotIndex { get; }
        public ulong SteamId { get; }
        public ulong ClientId { get; }
        public bool IsConnected { get; }
        public bool IsHost { get; }
        public double AssignedAt { get; }
        public double DisconnectedAt { get; }
        public bool HasDisconnectedAt { get; }

        public SessionRosterSlotSnapshot(
            int slotIndex,
            ulong steamId,
            ulong clientId,
            bool isConnected,
            bool isHost,
            double assignedAt,
            double disconnectedAt,
            bool hasDisconnectedAt)
        {
            SlotIndex = slotIndex;
            SteamId = steamId;
            ClientId = clientId;
            IsConnected = isConnected;
            IsHost = isHost;
            AssignedAt = assignedAt;
            DisconnectedAt = disconnectedAt;
            HasDisconnectedAt = hasDisconnectedAt;
        }
    }

    public sealed class SessionRosterAdmissionResult
    {
        public bool Approved { get; }
        public MultiplayerJoinKind JoinKind { get; }
        public SessionRosterAdmissionFailure Failure { get; }
        public int SlotIndex { get; }
        public string Message { get; }

        private SessionRosterAdmissionResult(
            bool approved,
            MultiplayerJoinKind joinKind,
            SessionRosterAdmissionFailure failure,
            int slotIndex,
            string message)
        {
            Approved = approved;
            JoinKind = joinKind;
            Failure = failure;
            SlotIndex = slotIndex;
            Message = message ?? string.Empty;
        }

        public static SessionRosterAdmissionResult Allow(
            MultiplayerJoinKind joinKind,
            int slotIndex)
        {
            return new SessionRosterAdmissionResult(
                true,
                joinKind,
                SessionRosterAdmissionFailure.None,
                slotIndex,
                string.Empty);
        }

        public static SessionRosterAdmissionResult Deny(
            SessionRosterAdmissionFailure failure,
            string message)
        {
            return new SessionRosterAdmissionResult(
                false,
                MultiplayerJoinKind.Denied,
                failure,
                -1,
                message);
        }
    }

    /// <summary>
    /// Compact Steam lobby metadata format. Only slot ownership is published;
    /// online state remains authoritative on the host and can be inferred by a
    /// browser from current Steam lobby membership.
    /// </summary>
    public static class SessionRosterCodec
    {
        private const string Prefix = "1|";

        public static string Encode(
            IReadOnlyList<SessionRosterSlotSnapshot> slots)
        {
            StringBuilder builder = new StringBuilder(Prefix);
            bool wroteEntry = false;

            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    SessionRosterSlotSnapshot slot = slots[i];

                    if (slot == null || slot.SteamId == 0)
                    {
                        continue;
                    }

                    if (wroteEntry)
                    {
                        builder.Append(';');
                    }

                    builder.Append(slot.SlotIndex.ToString(
                        CultureInfo.InvariantCulture));
                    builder.Append(':');
                    builder.Append(slot.SteamId.ToString(
                        CultureInfo.InvariantCulture));
                    wroteEntry = true;
                }
            }

            return builder.ToString();
        }

        public static bool TryDecode(
            string value,
            int capacity,
            out List<SessionRosterSlotSnapshot> slots)
        {
            slots = new List<SessionRosterSlotSnapshot>();

            if (string.IsNullOrWhiteSpace(value) ||
                !value.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string payload = value.Substring(Prefix.Length);

            if (string.IsNullOrEmpty(payload))
            {
                return true;
            }

            HashSet<int> usedSlots = new HashSet<int>();
            HashSet<ulong> usedSteamIds = new HashSet<ulong>();
            string[] entries = payload.Split(';');

            for (int i = 0; i < entries.Length; i++)
            {
                string[] pair = entries[i].Split(':');

                if (pair.Length != 2 ||
                    !int.TryParse(
                        pair[0],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int slotIndex) ||
                    !ulong.TryParse(
                        pair[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong steamId) ||
                    slotIndex < 0 ||
                    slotIndex >= capacity ||
                    steamId == 0 ||
                    !usedSlots.Add(slotIndex) ||
                    !usedSteamIds.Add(steamId))
                {
                    slots.Clear();
                    return false;
                }

                slots.Add(new SessionRosterSlotSnapshot(
                    slotIndex,
                    steamId,
                    0UL,
                    false,
                    slotIndex == 0,
                    0d,
                    0d,
                    false));
            }

            slots.Sort((left, right) =>
                left.SlotIndex.CompareTo(right.SlotIndex));
            return true;
        }

        public static string EncodeSteamIds(
            IReadOnlyList<SessionRosterSlotSnapshot> slots)
        {
            StringBuilder builder = new StringBuilder();

            if (slots == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                SessionRosterSlotSnapshot slot = slots[i];

                if (slot == null || slot.SteamId == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(',');
                }

                builder.Append(slot.SteamId.ToString(
                    CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }
}
