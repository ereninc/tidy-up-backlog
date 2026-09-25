using System;

namespace EXW.Multiplayer
{
    /// <summary>
    /// Local hand-off between development UI and the spawned network gateway.
    /// The UI knows no NGO transaction details; the gateway owns validation.
    /// </summary>
    public static class SharedDevelopmentActions
    {
        public static event Action<long> MoneyDeltaRequested;
        public static event Action<int> CarryLimitDeltaRequested;
        public static event Action CarryLimitResetRequested;
        public static event Action SkillCooldownsResetRequested;
        public static event Action SkillsUnlockRequested;

        public static event Action<bool, string> CommandResult;

        public static void RequestMoneyDelta(long delta)
        {
            if (delta == 0)
            {
                RaiseCommandResult(false, "Money delta cannot be zero.");
                return;
            }

            if (MoneyDeltaRequested == null)
            {
                RaiseGatewayMissing();
                return;
            }

            MoneyDeltaRequested.Invoke(delta);
        }

        public static void RequestCarryLimitDelta(int delta)
        {
            if (delta == 0)
            {
                RaiseCommandResult(false, "Carry limit delta cannot be zero.");
                return;
            }

            if (CarryLimitDeltaRequested == null)
            {
                RaiseGatewayMissing();
                return;
            }

            CarryLimitDeltaRequested.Invoke(delta);
        }

        public static void RequestCarryLimitReset()
        {
            if (CarryLimitResetRequested == null)
            {
                RaiseGatewayMissing();
                return;
            }

            CarryLimitResetRequested.Invoke();
        }

        public static void RequestSkillCooldownsReset()
        {
            if (SkillCooldownsResetRequested == null)
            {
                RaiseGatewayMissing();
                return;
            }

            SkillCooldownsResetRequested.Invoke();
        }

        public static void RequestUnlockAllSkills()
        {
            if (SkillsUnlockRequested == null)
            {
                RaiseGatewayMissing();
                return;
            }

            SkillsUnlockRequested.Invoke();
        }

        internal static void RaiseCommandResult(
            bool succeeded,
            string message)
        {
            CommandResult?.Invoke(succeeded, message ?? string.Empty);
        }

        private static void RaiseGatewayMissing()
        {
            RaiseCommandResult(
                false,
                "Development network gateway is not spawned.");
        }
    }
}
