namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using System;

    [Serializable]
    public struct TerminalThemeConfiguration : IEquatable<TerminalThemeConfiguration>
    {
        public const int HashBase = 397;

        public string terminalId;
        public string font;
        public string theme;

        public static bool operator ==(TerminalThemeConfiguration left, TerminalThemeConfiguration right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(TerminalThemeConfiguration left, TerminalThemeConfiguration right)
        {
            return !left.Equals(right);
        }

        public bool Equals(TerminalThemeConfiguration other)
        {
            return string.Equals(terminalId, other.terminalId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(font, other.font, StringComparison.OrdinalIgnoreCase)
                && string.Equals(theme, other.theme, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (obj is TerminalThemeConfiguration config)
            {
                return Equals(config);
            }

            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (terminalId != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(terminalId) : 0);
                hashCode = (hashCode * HashBase) ^ (font != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(font) : 0);
                hashCode = (hashCode * HashBase) ^ (theme != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(theme) : 0);
                return hashCode;
            }
        }
    }
}
