namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using NUnit.Framework;
    using UI;

    public sealed class TerminalTextSizeTests
    {
        [TestCase(TerminalTextSize.Small, 13f, 22f, 12.5f, 30f, 13f, 24f, 30f, "terminal-text-size--small")]
        [TestCase(TerminalTextSize.Medium, 15f, 26f, 14f, 34f, 15f, 28f, 36f, "terminal-text-size--medium")]
        [TestCase(TerminalTextSize.Large, 18f, 32f, 16.5f, 40f, 18f, 34f, 44f, "terminal-text-size--large")]
        public void MetricsMatchSpecifiedTypographyAndFixedRowHeights(
            TerminalTextSize size,
            float logFont,
            float logRowHeight,
            float signatureFont,
            float signatureHeight,
            float inputFont,
            float completionRowHeight,
            float paletteRowHeight,
            string cssClass
        )
        {
            TerminalTextSizeMetrics metrics = TerminalTextSizeMetrics.For(size);
            Assert.That(metrics.Size, Is.EqualTo(size));
            Assert.That(metrics.CssClass, Is.EqualTo(cssClass));
            Assert.That(metrics.LogFontSize, Is.EqualTo(logFont));
            Assert.That(metrics.LogRowHeight, Is.EqualTo(logRowHeight));
            Assert.That(metrics.SignatureFontSize, Is.EqualTo(signatureFont));
            Assert.That(metrics.SignatureHeight, Is.EqualTo(signatureHeight));
            Assert.That(metrics.InputFontSize, Is.EqualTo(inputFont));
            Assert.That(metrics.CompletionRowHeight, Is.EqualTo(completionRowHeight));
            Assert.That(metrics.EffectivePaletteRowHeight(30f), Is.EqualTo(paletteRowHeight));
        }

        [Test]
        public void InvalidAndObsoleteValuesNormalizeToSmall()
        {
#pragma warning disable CS0618
            Assert.That(TerminalTextSizeMetrics.Normalize(TerminalTextSize.Unknown), Is.EqualTo(TerminalTextSize.Small));
#pragma warning restore CS0618
            Assert.That(TerminalTextSizeMetrics.Normalize((TerminalTextSize)(-1)), Is.EqualTo(TerminalTextSize.Small));
            Assert.That(TerminalTextSizeMetrics.Normalize((TerminalTextSize)99), Is.EqualTo(TerminalTextSize.Small));
            Assert.That(TerminalTextSizeMetrics.For((TerminalTextSize)99).LogFontSize, Is.EqualTo(13f));
        }

        [TestCase(0, TerminalTextSize.Small)]
        [TestCase(1, TerminalTextSize.Medium)]
        [TestCase(2, TerminalTextSize.Large)]
        [TestCase(-1, TerminalTextSize.Small)]
        [TestCase(9, TerminalTextSize.Small)]
        public void OptionIndicesRoundTripThroughSmallMediumLarge(int index, TerminalTextSize expected)
        {
            Assert.That(TerminalTextSizeMetrics.FromOptionIndex(index), Is.EqualTo(expected));
            if (0 <= index && index <= 2)
            {
                Assert.That(TerminalTextSizeMetrics.ToOptionIndex(expected), Is.EqualTo(index));
            }
        }

        [Test]
        public void VisibleLogRowEstimatesDecreaseMonotonicallyAt720p()
        {
            const float fullLogViewportAt720p = 440f;
            int smallRows = TerminalTextSizeMetrics.For(TerminalTextSize.Small).EstimateVisibleLogRows(fullLogViewportAt720p);
            int mediumRows = TerminalTextSizeMetrics.For(TerminalTextSize.Medium).EstimateVisibleLogRows(fullLogViewportAt720p);
            int largeRows = TerminalTextSizeMetrics.For(TerminalTextSize.Large).EstimateVisibleLogRows(fullLogViewportAt720p);

            Assert.That(smallRows, Is.EqualTo(20));
            Assert.That(mediumRows, Is.EqualTo(16));
            Assert.That(largeRows, Is.EqualTo(13));
            Assert.That(largeRows < mediumRows && mediumRows < smallRows, Is.True);
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Small).EstimateVisibleLogRows(0f), Is.EqualTo(0));
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Small).EstimateVisibleLogRows(float.NaN), Is.EqualTo(0));
        }

        [Test]
        public void PaletteRowHeightPreservesCustomBaseFloor()
        {
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Small).EffectivePaletteRowHeight(40f), Is.EqualTo(40f));
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Medium).EffectivePaletteRowHeight(40f), Is.EqualTo(46f));
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Large).EffectivePaletteRowHeight(40f), Is.EqualTo(54f));
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Small).EffectivePaletteRowHeight(8f), Is.EqualTo(16f));
        }
    }
}
