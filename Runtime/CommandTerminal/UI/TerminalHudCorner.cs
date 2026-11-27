namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    /// <summary>The screen corner the Watches HUD anchors to (#244, board S3).</summary>
    public enum TerminalHudCorner
    {
        [Obsolete("Use a valid TerminalHudCorner value.")]
        Unknown = 0,

        BottomRight = 1,

        BottomLeft = 2,

        TopRight = 3,

        TopLeft = 4,
    }
}
