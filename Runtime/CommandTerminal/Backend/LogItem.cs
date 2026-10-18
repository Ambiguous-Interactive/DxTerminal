namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Threading;

    public readonly struct LogItem : IEquatable<LogItem>
    {
        private static long _sequenceCounter;

        public readonly TerminalLogType type;
        public readonly string message;
        public readonly string stackTrace;
        internal readonly long Sequence;

        public LogItem(TerminalLogType type, string message, string stackTrace)
        {
            Sequence = Interlocked.Increment(ref _sequenceCounter);
            this.type = type;
            this.message = message ?? string.Empty;
            this.stackTrace = stackTrace ?? string.Empty;
        }

        public static bool operator ==(LogItem left, LogItem right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LogItem left, LogItem right)
        {
            return !left.Equals(right);
        }

        public bool Equals(LogItem other)
        {
            return type == other.type && string.Equals(message, other.message, StringComparison.Ordinal) && string.Equals(stackTrace, other.stackTrace, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is LogItem other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                type,
                message != null ? StringComparer.Ordinal.GetHashCode(message) : 0,
                stackTrace != null ? StringComparer.Ordinal.GetHashCode(stackTrace) : 0
            );
        }
    }
}
