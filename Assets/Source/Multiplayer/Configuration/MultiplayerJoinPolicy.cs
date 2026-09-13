namespace EXW.Multiplayer
{
    public enum MultiplayerJoinKind
    {
        Denied,
        WaitingRoom,
        Reconnect,
        LateJoin
    }

    /// <summary>
    /// Pure policy evaluator shared by the browser, approval and UI layers.
    /// Keeping this decision free of Steam/NGO calls makes it deterministic and
    /// easy to validate when more lobby options are added later.
    /// </summary>
    public static class MultiplayerJoinPolicy
    {
        public static MultiplayerJoinKind Classify(
            SteamLobbyState state,
            bool allowsLateJoin,
            bool reconnectEligible,
            bool hasUnassignedSessionSlot = true)
        {
            if (state == SteamLobbyState.Waiting)
            {
                return MultiplayerJoinKind.WaitingRoom;
            }

            if (state != SteamLobbyState.Playing)
            {
                return MultiplayerJoinKind.Denied;
            }

            if (reconnectEligible)
            {
                return MultiplayerJoinKind.Reconnect;
            }

            return allowsLateJoin && hasUnassignedSessionSlot
                ? MultiplayerJoinKind.LateJoin
                : MultiplayerJoinKind.Denied;
        }
    }
}
