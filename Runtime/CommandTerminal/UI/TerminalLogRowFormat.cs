namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Globalization;
    using Backend;

    /*
        The text the terminal header chips and the log rows draw (issue #241,
        direction 1a). Kept free of UI Toolkit so the engine-independent
        fixtures pin the rules without a visual tree.

        Timestamps render capture time as `HH:mm:ss.fff`; log captures are
        UTC (LogItem), and the row's tooltip says so, because Unity's own
        Console shows local time and the two must not be mistaken for each
        other. Glyphs carry the type so the state survives a grayscale or
        colorblind read (never color alone); the shell reply shares the
        message glyph, and an unknown serialized value falls back to it too.
     */
    internal static class TerminalLogRowFormat
    {
        public static string FormatTimestamp(DateTime timestampUtc)
        {
            return timestampUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        public static string GlyphOf(TerminalLogType type)
        {
            return type switch
            {
                TerminalLogType.Error => "■",
                TerminalLogType.Assert => "■",
                TerminalLogType.Exception => "■",
                TerminalLogType.Warning => "▲",
                TerminalLogType.Input => "›",
                _ => "·",
            };
        }

        public static string ChipText(TerminalLogType severity, int count)
        {
            string word = severity switch
            {
                TerminalLogType.Error => "Errors",
                TerminalLogType.Warning => "Warnings",
                _ => "Messages",
            };
            return $"{count.ToString(CultureInfo.InvariantCulture)} {word}";
        }
    }
}
