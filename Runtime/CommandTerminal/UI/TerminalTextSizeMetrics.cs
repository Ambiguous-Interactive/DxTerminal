namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    internal readonly struct TerminalTextSizeMetrics
    {
        internal const string SmallClass = "terminal-text-size--small";
        internal const string MediumClass = "terminal-text-size--medium";
        internal const string LargeClass = "terminal-text-size--large";

        private static readonly TerminalTextSizeMetrics SmallMetrics = new(
            TerminalTextSize.Small,
            SmallClass,
            logFontSize: 13f,
            logRowHeight: 22f,
            signatureFontSize: 12.5f,
            signatureHeight: 30f,
            inputFontSize: 13f,
            completionRowHeight: 24f,
            completionDetailsHeight: 36f,
            completionFooterHeight: 16f,
            paletteRowHeightDelta: 0f
        );

        private static readonly TerminalTextSizeMetrics MediumMetrics = new(
            TerminalTextSize.Medium,
            MediumClass,
            logFontSize: 15f,
            logRowHeight: 26f,
            signatureFontSize: 14f,
            signatureHeight: 34f,
            inputFontSize: 15f,
            completionRowHeight: 28f,
            completionDetailsHeight: 42f,
            completionFooterHeight: 19f,
            paletteRowHeightDelta: 6f
        );

        private static readonly TerminalTextSizeMetrics LargeMetrics = new(
            TerminalTextSize.Large,
            LargeClass,
            logFontSize: 18f,
            logRowHeight: 32f,
            signatureFontSize: 16.5f,
            signatureHeight: 40f,
            inputFontSize: 18f,
            completionRowHeight: 34f,
            completionDetailsHeight: 50f,
            completionFooterHeight: 22f,
            paletteRowHeightDelta: 14f
        );

        internal TerminalTextSize Size { get; }

        internal string CssClass { get; }

        internal float LogFontSize { get; }

        internal float LogRowHeight { get; }

        internal float SignatureFontSize { get; }

        internal float SignatureHeight { get; }

        internal float InputFontSize { get; }

        internal float CompletionRowHeight { get; }

        internal float CompletionDetailsHeight { get; }

        internal float CompletionFooterHeight { get; }

        internal float PaletteRowHeightDelta { get; }

        private TerminalTextSizeMetrics(
            TerminalTextSize size,
            string cssClass,
            float logFontSize,
            float logRowHeight,
            float signatureFontSize,
            float signatureHeight,
            float inputFontSize,
            float completionRowHeight,
            float completionDetailsHeight,
            float completionFooterHeight,
            float paletteRowHeightDelta
        )
        {
            Size = size;
            CssClass = cssClass;
            LogFontSize = logFontSize;
            LogRowHeight = logRowHeight;
            SignatureFontSize = signatureFontSize;
            SignatureHeight = signatureHeight;
            InputFontSize = inputFontSize;
            CompletionRowHeight = completionRowHeight;
            CompletionDetailsHeight = completionDetailsHeight;
            CompletionFooterHeight = completionFooterHeight;
            PaletteRowHeightDelta = paletteRowHeightDelta;
        }

        internal static TerminalTextSize Normalize(TerminalTextSize size)
        {
            return size switch
            {
                TerminalTextSize.Small => TerminalTextSize.Small,
                TerminalTextSize.Medium => TerminalTextSize.Medium,
                TerminalTextSize.Large => TerminalTextSize.Large,
                _ => TerminalTextSize.Small,
            };
        }

        internal static TerminalTextSizeMetrics For(TerminalTextSize size)
        {
            return Normalize(size) switch
            {
                TerminalTextSize.Medium => MediumMetrics,
                TerminalTextSize.Large => LargeMetrics,
                _ => SmallMetrics,
            };
        }

        internal static int ToOptionIndex(TerminalTextSize size)
        {
            return Normalize(size) switch
            {
                TerminalTextSize.Medium => 1,
                TerminalTextSize.Large => 2,
                _ => 0,
            };
        }

        internal static TerminalTextSize FromOptionIndex(int index)
        {
            return index switch
            {
                1 => TerminalTextSize.Medium,
                2 => TerminalTextSize.Large,
                _ => TerminalTextSize.Small,
            };
        }

        internal float EffectivePaletteRowHeight(float baseRowHeight)
        {
            float clampedBase = float.IsFinite(baseRowHeight) ? Math.Max(16f, baseRowHeight) : 30f;
            return clampedBase + PaletteRowHeightDelta;
        }

        internal int EstimateVisibleLogRows(float viewportHeight)
        {
            if (!float.IsFinite(viewportHeight) || viewportHeight <= 0f)
            {
                return 0;
            }

            return (int)Math.Floor(viewportHeight / LogRowHeight);
        }
    }
}
