using System;
using System.Globalization;
using UnityEngine;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local pointer to a running Steam session. It contains no authoritative
    /// gameplay state; it only lets the same Steam user find the previous lobby
    /// again after a restart. The host roster still decides whether rejoin is
    /// allowed.
    /// </summary>
    public sealed class MultiplayerLastSessionRecord
    {
        public ulong LobbyId { get; }
        public string SessionId { get; }
        public ulong HostSteamId { get; }
        public ulong LocalSteamId { get; }
        public int SlotIndex { get; }
        public string LobbyName { get; }
        public string ProductId { get; }
        public int ProtocolVersion { get; }
        public string BuildId { get; }
        public long LastSeenUnixSeconds { get; }

        public MultiplayerLastSessionRecord(
            ulong lobbyId,
            string sessionId,
            ulong hostSteamId,
            ulong localSteamId,
            int slotIndex,
            string lobbyName,
            string productId,
            int protocolVersion,
            string buildId,
            long lastSeenUnixSeconds)
        {
            LobbyId = lobbyId;
            SessionId = sessionId ?? string.Empty;
            HostSteamId = hostSteamId;
            LocalSteamId = localSteamId;
            SlotIndex = slotIndex;
            LobbyName = lobbyName ?? string.Empty;
            ProductId = productId ?? string.Empty;
            ProtocolVersion = protocolVersion;
            BuildId = buildId ?? string.Empty;
            LastSeenUnixSeconds = lastSeenUnixSeconds;
        }

        public bool IsCompatible(
            MultiplayerSettings settings,
            ulong localSteamId)
        {
            return settings != null &&
                   LobbyId != 0 &&
                   !string.IsNullOrWhiteSpace(SessionId) &&
                   LocalSteamId != 0 &&
                   LocalSteamId == localSteamId &&
                   string.Equals(
                       ProductId,
                       settings.ProductId,
                       StringComparison.Ordinal) &&
                   ProtocolVersion == settings.ProtocolVersion &&
                   string.Equals(
                       BuildId,
                       settings.BuildId,
                       StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// PlayerPrefs-backed, device-local last-session pointer. The record is
    /// retained across Return To Main Menu, application quit and crashes when the
    /// preserve-slot profile is active. It is replaced when another running
    /// session is observed and can be removed by an explicit Forget action.
    /// </summary>
    public static class MultiplayerLastSessionStore
    {
        private const string PlayerPrefsKey =
            "EXW.Multiplayer.LastSession.v1";

        [Serializable]
        private sealed class SerializedRecord
        {
            public string lobbyId;
            public string sessionId;
            public string hostSteamId;
            public string localSteamId;
            public int slotIndex;
            public string lobbyName;
            public string productId;
            public int protocolVersion;
            public string buildId;
            public long lastSeenUnixSeconds;
        }

        public static bool HasRecord => PlayerPrefs.HasKey(PlayerPrefsKey);

        public static bool Remember(
            SteamLobbySummary lobby,
            MultiplayerSettings settings,
            ulong localSteamId)
        {
            if (lobby == null ||
                settings == null ||
                settings.LastSessionRejoinBehaviour ==
                LastSessionRejoinMode.Disabled ||
                !settings.SupportsSessionRejoin ||
                lobby.State != SteamLobbyState.Playing ||
                lobby.LobbyId == 0 ||
                string.IsNullOrWhiteSpace(lobby.SessionId) ||
                localSteamId == 0 ||
                localSteamId == lobby.HostSteamId)
            {
                return false;
            }

            SessionRosterSlotSnapshot slot =
                lobby.FindSessionSlot(localSteamId);

            if (slot == null)
            {
                return false;
            }

            if (TryLoad(out MultiplayerLastSessionRecord existing, out _) &&
                existing.LobbyId == lobby.LobbyId &&
                string.Equals(
                    existing.SessionId,
                    lobby.SessionId,
                    StringComparison.Ordinal) &&
                existing.LocalSteamId == localSteamId &&
                existing.HostSteamId == lobby.HostSteamId &&
                existing.SlotIndex == slot.SlotIndex &&
                string.Equals(
                    existing.LobbyName,
                    lobby.Name,
                    StringComparison.Ordinal) &&
                existing.IsCompatible(settings, localSteamId))
            {
                return true;
            }

            try
            {
                SerializedRecord record = new SerializedRecord
                {
                    lobbyId = lobby.LobbyId.ToString(
                        CultureInfo.InvariantCulture),
                    sessionId = lobby.SessionId,
                    hostSteamId = lobby.HostSteamId.ToString(
                        CultureInfo.InvariantCulture),
                    localSteamId = localSteamId.ToString(
                        CultureInfo.InvariantCulture),
                    slotIndex = slot.SlotIndex,
                    lobbyName = lobby.Name,
                    productId = settings.ProductId,
                    protocolVersion = settings.ProtocolVersion,
                    buildId = settings.BuildId,
                    lastSeenUnixSeconds =
                        DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                PlayerPrefs.SetString(
                    PlayerPrefsKey,
                    JsonUtility.ToJson(record));
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[LastSession] Could not save the session pointer: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        public static bool TryLoad(
            out MultiplayerLastSessionRecord record,
            out string error)
        {
            record = null;
            error = string.Empty;

            if (!PlayerPrefs.HasKey(PlayerPrefsKey))
            {
                error = "No previous running session was saved.";
                return false;
            }

            try
            {
                string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
                SerializedRecord serialized =
                    JsonUtility.FromJson<SerializedRecord>(json);

                if (serialized == null ||
                    !ulong.TryParse(
                        serialized.lobbyId,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong lobbyId) ||
                    !ulong.TryParse(
                        serialized.hostSteamId,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong hostSteamId) ||
                    !ulong.TryParse(
                        serialized.localSteamId,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out ulong localSteamId) ||
                    lobbyId == 0 ||
                    localSteamId == 0 ||
                    string.IsNullOrWhiteSpace(serialized.sessionId))
                {
                    error = "The saved session record is invalid.";
                    return false;
                }

                record = new MultiplayerLastSessionRecord(
                    lobbyId,
                    serialized.sessionId,
                    hostSteamId,
                    localSteamId,
                    serialized.slotIndex,
                    serialized.lobbyName,
                    serialized.productId,
                    serialized.protocolVersion,
                    serialized.buildId,
                    serialized.lastSeenUnixSeconds);
                return true;
            }
            catch (Exception exception)
            {
                error =
                    $"Could not read the saved session: " +
                    $"{exception.GetType().Name}.";
                return false;
            }
        }

        public static bool TryLoadCompatible(
            MultiplayerSettings settings,
            ulong localSteamId,
            out MultiplayerLastSessionRecord record,
            out string error)
        {
            if (!TryLoad(out record, out error))
            {
                return false;
            }

            if (settings == null ||
                settings.LastSessionRejoinBehaviour ==
                LastSessionRejoinMode.Disabled ||
                !settings.SupportsSessionRejoin)
            {
                record = null;
                error = "Previous-session rejoin is disabled in multiplayer settings.";
                return false;
            }

            if (!record.IsCompatible(settings, localSteamId))
            {
                record = null;
                error =
                    "The saved session belongs to another Steam user or an " +
                    "incompatible product, protocol or build.";
                return false;
            }

            return true;
        }

        public static void Forget()
        {
            try
            {
                PlayerPrefs.DeleteKey(PlayerPrefsKey);
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[LastSession] Could not remove the session pointer: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
