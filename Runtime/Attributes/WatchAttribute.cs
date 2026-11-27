namespace WallstopStudios.DxCommandTerminal.Attributes
{
    using System;
    using System.Reflection;
    using Backend;

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class WatchAttribute : Attribute
    {
        public string Name { get; private set; }

        /// <summary>
        ///     Optional .NET format string applied to the sampled value with
        ///     the invariant culture, for example "F1" or "0". Values whose
        ///     type is not <see cref="IFormattable"/> ignore it.
        /// </summary>
        public string Format { get; set; }

        public CommandTier Tier { get; set; } = CommandTier.Developer;

        /// <summary>
        ///     Declares a watch over a static member: a property, a field,
        ///     or a parameterless method whose return value is sampled. The
        ///     watch name defaults to the member name.
        /// </summary>
        public WatchAttribute(string name = null)
        {
            if (0 <= name?.IndexOf(' ', StringComparison.Ordinal))
            {
                name = name.Replace(" ", string.Empty, StringComparison.Ordinal);
            }

            Name = name?.Trim();
        }

        public void NormalizeName(MemberInfo member)
        {
            if (member == null)
            {
                throw new ArgumentNullException(nameof(member));
            }

            if (!string.IsNullOrWhiteSpace(Name))
            {
                return;
            }

            Name = member.Name;
        }
    }
}
