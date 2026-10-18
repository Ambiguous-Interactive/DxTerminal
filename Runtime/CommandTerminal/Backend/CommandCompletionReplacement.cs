namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     An explicit replacement range for one completion. Both values are
    ///     required and validated at construction; a completion without an
    ///     override carries <c>null</c> and the completion context's range
    ///     applies.
    /// </summary>
    public readonly struct CommandCompletionReplacement : IEquatable<CommandCompletionReplacement>
    {
        /// <summary>Start index of the replaced range within the input line.</summary>
        public int Start { get; }

        /// <summary>Length of the replaced range within the input line.</summary>
        public int Length { get; }

        public CommandCompletionReplacement(int start, int length)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, "Start must not be negative.");
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), length, "Length must not be negative.");
            }

            Start = start;
            Length = length;
        }

        public static bool operator ==(CommandCompletionReplacement left, CommandCompletionReplacement right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CommandCompletionReplacement left, CommandCompletionReplacement right)
        {
            return !left.Equals(right);
        }

        public bool Equals(CommandCompletionReplacement other)
        {
            return Start == other.Start && Length == other.Length;
        }

        public override bool Equals(object obj)
        {
            return obj is CommandCompletionReplacement other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Start, Length);
        }

        public override string ToString()
        {
            return $"[{Start}, {Length})";
        }
    }
}
