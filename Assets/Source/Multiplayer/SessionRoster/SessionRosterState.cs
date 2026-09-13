using System;
using System.Collections.Generic;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Pure, host-authoritative roster state. It has no Steam, NGO or Unity API
    /// dependency, which keeps admission and retention rules deterministic and
    /// directly testable.
    /// </summary>
    public sealed class SessionRosterState
    {
        private sealed class Slot
        {
            public readonly int Index;
            public ulong SteamId;
            public ulong ClientId;
            public bool IsConnected;
            public bool IsHost;
            public double AssignedAt;
            public double DisconnectedAt;
            public bool HasDisconnectedAt;

            public Slot(int index)
            {
                Index = index;
            }
        }

        private readonly List<Slot> _slots = new List<Slot>();

        public int Capacity => _slots.Count;
        public int AssignedCount { get; private set; }
        public int ConnectedCount { get; private set; }
        public int OfflineCount => AssignedCount - ConnectedCount;
        public int UnassignedCount => Capacity - AssignedCount;

        public SessionRosterState(int capacity = 4)
        {
            Reset(capacity);
        }

        public void Reset(int capacity)
        {
            capacity = Math.Max(1, capacity);
            _slots.Clear();

            for (int i = 0; i < capacity; i++)
            {
                _slots.Add(new Slot(i));
            }

            AssignedCount = 0;
            ConnectedCount = 0;
        }

        public SessionRosterAdmissionResult TryAdmit(
            ulong steamId,
            ulong clientId,
            SteamLobbyState lobbyState,
            bool allowRejoin,
            bool allowLateJoin,
            SessionSlotRetentionPolicy retentionPolicy,
            double timedRetentionSeconds,
            double now)
        {
            if (steamId == 0)
            {
                return SessionRosterAdmissionResult.Deny(
                    SessionRosterAdmissionFailure.InvalidSteamId,
                    "SteamID is zero.");
            }

            PruneExpired(
                retentionPolicy,
                timedRetentionSeconds,
                now,
                null);

            Slot existing = FindBySteamId(steamId);

            if (existing != null)
            {
                if (existing.IsConnected)
                {
                    return existing.ClientId == clientId
                        ? SessionRosterAdmissionResult.Allow(
                            lobbyState == SteamLobbyState.Playing
                                ? MultiplayerJoinKind.Reconnect
                                : MultiplayerJoinKind.WaitingRoom,
                            existing.Index)
                        : SessionRosterAdmissionResult.Deny(
                            SessionRosterAdmissionFailure.AlreadyConnected,
                            "This Steam user already owns a connected session slot.");
                }

                if (lobbyState == SteamLobbyState.Waiting)
                {
                    Connect(existing, clientId);
                    return SessionRosterAdmissionResult.Allow(
                        MultiplayerJoinKind.WaitingRoom,
                        existing.Index);
                }

                if (lobbyState != SteamLobbyState.Playing)
                {
                    return SessionRosterAdmissionResult.Deny(
                        SessionRosterAdmissionFailure.InvalidLobbyState,
                        "The lobby is not accepting session participants.");
                }

                if (!allowRejoin)
                {
                    return SessionRosterAdmissionResult.Deny(
                        SessionRosterAdmissionFailure.RejoinDisabled,
                        "Rejoining this running session is disabled.");
                }

                Connect(existing, clientId);
                return SessionRosterAdmissionResult.Allow(
                    MultiplayerJoinKind.Reconnect,
                    existing.Index);
            }

            if (lobbyState != SteamLobbyState.Waiting &&
                lobbyState != SteamLobbyState.Playing)
            {
                return SessionRosterAdmissionResult.Deny(
                    SessionRosterAdmissionFailure.InvalidLobbyState,
                    "The lobby is not accepting session participants.");
            }

            MultiplayerJoinKind joinKind;

            if (lobbyState == SteamLobbyState.Playing)
            {
                if (!allowLateJoin)
                {
                    return SessionRosterAdmissionResult.Deny(
                        SessionRosterAdmissionFailure.LateJoinDisabled,
                        "This running session does not allow new players.");
                }

                joinKind = MultiplayerJoinKind.LateJoin;
            }
            else
            {
                joinKind = MultiplayerJoinKind.WaitingRoom;
            }

            Slot available = FindUnassigned();

            if (available == null)
            {
                return SessionRosterAdmissionResult.Deny(
                    SessionRosterAdmissionFailure.SessionRosterFull,
                    $"All {Capacity} session slots are assigned, including " +
                    "offline players. Ask the host to release an offline slot.");
            }

            Assign(
                available,
                steamId,
                clientId,
                clientId == 0UL,
                now);

            return SessionRosterAdmissionResult.Allow(
                joinKind,
                available.Index);
        }

        public bool MarkDisconnected(
            ulong clientId,
            SteamLobbyState lobbyState,
            bool gracefulExit,
            bool allowRejoin,
            SessionSlotRetentionPolicy retentionPolicy,
            GracefulExitSlotPolicy gracefulExitPolicy,
            double now,
            out SessionRosterSlotSnapshot changedSlot,
            out SessionSlotReleaseReason? releaseReason)
        {
            changedSlot = null;
            releaseReason = null;
            Slot slot = FindConnectedByClientId(clientId);

            if (slot == null)
            {
                return false;
            }

            changedSlot = Snapshot(slot);

            if (lobbyState != SteamLobbyState.Playing)
            {
                releaseReason = SessionSlotReleaseReason.WaitingRoomLeave;
                Release(slot);
                return true;
            }

            if (!allowRejoin ||
                retentionPolicy ==
                SessionSlotRetentionPolicy.ReleaseImmediately)
            {
                releaseReason = SessionSlotReleaseReason.RetentionDisabled;
                Release(slot);
                return true;
            }

            if (gracefulExit &&
                gracefulExitPolicy == GracefulExitSlotPolicy.ReleaseSlot)
            {
                releaseReason = SessionSlotReleaseReason.GracefulExitPolicy;
                Release(slot);
                return true;
            }

            slot.IsConnected = false;
            slot.ClientId = 0UL;
            slot.DisconnectedAt = now;
            slot.HasDisconnectedAt = true;
            ConnectedCount--;
            changedSlot = Snapshot(slot);
            return true;
        }

        public int PruneExpired(
            SessionSlotRetentionPolicy retentionPolicy,
            double timedRetentionSeconds,
            double now,
            Action<SessionRosterSlotSnapshot> released)
        {
            if (retentionPolicy ==
                SessionSlotRetentionPolicy.UntilSessionEnds)
            {
                return 0;
            }

            int count = 0;

            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];

                bool timedSlotExpired =
                    retentionPolicy == SessionSlotRetentionPolicy.Timed &&
                    timedRetentionSeconds > 0d &&
                    slot.HasDisconnectedAt &&
                    now - slot.DisconnectedAt >= timedRetentionSeconds;
                bool retentionDisabled =
                    retentionPolicy ==
                    SessionSlotRetentionPolicy.ReleaseImmediately;

                if (slot.SteamId == 0 ||
                    slot.IsConnected ||
                    (!timedSlotExpired && !retentionDisabled))
                {
                    continue;
                }

                released?.Invoke(Snapshot(slot));
                Release(slot);
                count++;
            }

            return count;
        }

        public bool ReleaseBySteamId(
            ulong steamId,
            out SessionRosterSlotSnapshot releasedSlot)
        {
            Slot slot = FindBySteamId(steamId);
            releasedSlot = slot != null ? Snapshot(slot) : null;

            if (slot == null)
            {
                return false;
            }

            Release(slot);
            return true;
        }

        public bool ReleaseBySlotIndex(
            int slotIndex,
            bool offlineOnly,
            out SessionRosterSlotSnapshot releasedSlot)
        {
            releasedSlot = null;

            if (slotIndex < 0 || slotIndex >= _slots.Count)
            {
                return false;
            }

            Slot slot = _slots[slotIndex];

            if (slot.SteamId == 0 || (offlineOnly && slot.IsConnected))
            {
                return false;
            }

            releasedSlot = Snapshot(slot);
            Release(slot);
            return true;
        }

        public bool TryGetSteamId(ulong clientId, out ulong steamId)
        {
            Slot slot = FindConnectedByClientId(clientId);
            steamId = slot != null ? slot.SteamId : 0UL;
            return steamId != 0;
        }

        public int GetSlotIndexForClient(ulong clientId)
        {
            Slot slot = FindConnectedByClientId(clientId);
            return slot != null ? slot.Index : -1;
        }

        public int GetSlotIndexForSteamId(ulong steamId)
        {
            Slot slot = FindBySteamId(steamId);
            return slot != null ? slot.Index : -1;
        }

        public bool ContainsSteamId(ulong steamId)
        {
            return steamId != 0 && FindBySteamId(steamId) != null;
        }

        public bool IsSteamIdConnected(ulong steamId)
        {
            Slot slot = FindBySteamId(steamId);
            return slot != null && slot.IsConnected;
        }

        public IReadOnlyList<SessionRosterSlotSnapshot> CreateSnapshot()
        {
            List<SessionRosterSlotSnapshot> snapshot =
                new List<SessionRosterSlotSnapshot>(AssignedCount);

            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].SteamId != 0)
                {
                    snapshot.Add(Snapshot(_slots[i]));
                }
            }

            return snapshot;
        }

        private Slot FindBySteamId(ulong steamId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].SteamId == steamId)
                {
                    return _slots[i];
                }
            }

            return null;
        }

        private Slot FindConnectedByClientId(ulong clientId)
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsConnected &&
                    _slots[i].ClientId == clientId)
                {
                    return _slots[i];
                }
            }

            return null;
        }

        private Slot FindUnassigned()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].SteamId == 0)
                {
                    return _slots[i];
                }
            }

            return null;
        }

        private void Assign(
            Slot slot,
            ulong steamId,
            ulong clientId,
            bool isHost,
            double now)
        {
            slot.SteamId = steamId;
            slot.ClientId = clientId;
            slot.IsConnected = true;
            slot.IsHost = isHost;
            slot.AssignedAt = now;
            slot.DisconnectedAt = 0d;
            slot.HasDisconnectedAt = false;
            AssignedCount++;
            ConnectedCount++;
        }

        private void Connect(Slot slot, ulong clientId)
        {
            slot.ClientId = clientId;
            slot.IsConnected = true;
            slot.DisconnectedAt = 0d;
            slot.HasDisconnectedAt = false;
            ConnectedCount++;
        }

        private void Release(Slot slot)
        {
            if (slot.SteamId == 0)
            {
                return;
            }

            if (slot.IsConnected)
            {
                ConnectedCount--;
            }

            AssignedCount--;
            slot.SteamId = 0UL;
            slot.ClientId = 0UL;
            slot.IsConnected = false;
            slot.IsHost = false;
            slot.AssignedAt = 0d;
            slot.DisconnectedAt = 0d;
            slot.HasDisconnectedAt = false;
        }

        private static SessionRosterSlotSnapshot Snapshot(Slot slot)
        {
            return new SessionRosterSlotSnapshot(
                slot.Index,
                slot.SteamId,
                slot.ClientId,
                slot.IsConnected,
                slot.IsHost,
                slot.AssignedAt,
                slot.DisconnectedAt,
                slot.HasDisconnectedAt);
        }
    }
}
