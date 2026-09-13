namespace EXW.Multiplayer
{
    /// <summary>
    /// Deterministic local identity used only by the offline NGO host. Keeping it
    /// non-zero lets the shared player identity/registry pipeline work unchanged.
    /// </summary>
    public static class SinglePlayerIdentity
    {
        public const ulong LocalPlayerId = 1UL;
        public const string DefaultPlayerName = "Player";
    }
}
