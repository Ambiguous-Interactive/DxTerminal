namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    /// <summary>Active content tab displayed inside <see cref="TerminalUI"/>.</summary>
    public enum TerminalTab
    {
        [Obsolete("Use a valid TerminalTab value.")]
        Unknown = 0,

        Log = 1,

        Actions = 2,

        Watches = 3,
    }
}
