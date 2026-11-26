namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Audience tier for a registered command. Build profiles and UI surfaces
    ///     filter commands by tier; execution contexts control where a command
    ///     can run.
    /// </summary>
    public enum CommandTier
    {
        [Obsolete("Use a valid CommandTier value.")]
        Unknown = 0,

        Player = 1,

        Tester = 2,

        Developer = 3,
    }
}
