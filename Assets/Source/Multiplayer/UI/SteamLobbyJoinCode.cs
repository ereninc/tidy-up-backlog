using System;
using System.Globalization;
using System.Text;

namespace EXW.Multiplayer.UI
{
    /// <summary>
    /// Converts Steam's 64-bit lobby ID into a shorter, human-friendly code.
    /// This is an encoding, not a backend reservation system: the decoded value
    /// is still passed to Steam Matchmaking as the real lobby ID.
    /// </summary>
    public static class SteamLobbyJoinCode
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        private const int DataCharacterCount = 13;
        private const int TotalCharacterCount = 14;

        public static string Encode(ulong lobbyId)
        {
            if (lobbyId == 0)
            {
                return string.Empty;
            }

            char[] raw = new char[TotalCharacterCount];
            ulong remaining = lobbyId;

            for (int i = DataCharacterCount - 1; i >= 0; i--)
            {
                raw[i] = Alphabet[(int)(remaining & 31UL)];
                remaining >>= 5;
            }

            raw[DataCharacterCount] = Alphabet[ComputeChecksum(lobbyId)];

            return new string(raw, 0, 5) + "-" +
                   new string(raw, 5, 5) + "-" +
                   new string(raw, 10, 4);
        }

        public static bool TryDecode(string input, out ulong lobbyId)
        {
            return TryDecode(input, out lobbyId, out _);
        }

        public static bool TryDecode(
            string input,
            out ulong lobbyId,
            out string error)
        {
            lobbyId = 0;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = "Enter a lobby code.";
                return false;
            }

            string normalized = Normalize(input);

            // Development/debug convenience: the raw decimal LobbyID shown in
            // Steam logs can also be pasted into the same field.
            if (normalized.Length != TotalCharacterCount &&
                IsDecimal(normalized) &&
                ulong.TryParse(
                    normalized,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out lobbyId) &&
                lobbyId != 0)
            {
                return true;
            }

            if (normalized.Length != TotalCharacterCount)
            {
                error = "Lobby code must contain 14 characters.";
                return false;
            }

            ulong value = 0;

            for (int i = 0; i < DataCharacterCount; i++)
            {
                int digit = DecodeCharacter(normalized[i]);

                if (digit < 0)
                {
                    error = $"Lobby code contains an invalid character: {normalized[i]}";
                    return false;
                }

                if (value > (ulong.MaxValue - (ulong)digit) / 32UL)
                {
                    error = "Lobby code is outside the valid Steam lobby ID range.";
                    return false;
                }

                value = value * 32UL + (ulong)digit;
            }

            int suppliedChecksum = DecodeCharacter(normalized[DataCharacterCount]);

            if (suppliedChecksum < 0)
            {
                error = "Lobby code contains an invalid checksum character.";
                return false;
            }

            if (value == 0)
            {
                error = "Lobby code resolves to LobbyID 0.";
                return false;
            }

            if (suppliedChecksum != ComputeChecksum(value))
            {
                error = "Lobby code is invalid or was typed incorrectly.";
                return false;
            }

            lobbyId = value;
            return true;
        }

        private static string Normalize(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                char character = char.ToUpperInvariant(value[i]);

                if (character == '#' || character == '-' || character == '_' ||
                    char.IsWhiteSpace(character))
                {
                    continue;
                }

                // Crockford Base32 aliases make spoken/typed codes friendlier.
                if (character == 'O') character = '0';
                if (character == 'I' || character == 'L') character = '1';

                builder.Append(character);
            }

            return builder.ToString();
        }

        private static bool IsDecimal(string value)
        {
            if (value.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static int DecodeCharacter(char character)
        {
            return Alphabet.IndexOf(character);
        }

        // Five-bit CRC over the full 64-bit LobbyID. It catches the common typo
        // case before an unnecessary Steam JoinLobby request is started.
        private static int ComputeChecksum(ulong value)
        {
            int crc = 0x1F;

            for (int bit = 63; bit >= 0; bit--)
            {
                int incoming = (int)((value >> bit) & 1UL);
                int highBit = (crc >> 4) & 1;
                crc = (crc << 1) & 0x1F;

                if ((highBit ^ incoming) != 0)
                {
                    crc ^= 0x05;
                }
            }

            return crc & 0x1F;
        }
    }
}
