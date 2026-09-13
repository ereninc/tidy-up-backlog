using System;

namespace EXW.SaveSystem
{
    internal static class SaveKeyValidator
    {
        private const int MaximumSlotIdLength = 48;
        private const int MaximumProviderKeyLength = 96;

        public static bool IsValidSlotId(string value)
        {
            return IsValid(value, MaximumSlotIdLength, allowDot: false);
        }

        public static void RequireProviderKey(string value)
        {
            if (!IsValid(value, MaximumProviderKeyLength, allowDot: true))
            {
                throw new InvalidOperationException(
                    $"Invalid save provider key '{value}'. Use lowercase letters, numbers, '.', '_' or '-'.");
            }
        }

        private static bool IsValid(string value, int maximumLength, bool allowDot)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool valid = character is >= 'a' and <= 'z' ||
                             character is >= '0' and <= '9' ||
                             character == '_' ||
                             character == '-' ||
                             allowDot && character == '.';

                if (!valid)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
