namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UI;

    public sealed class TerminalMotionPresetTests
    {
        private static string FindPackageRoot()
        {
            string current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "package.json")) && Directory.Exists(Path.Combine(current, "Styles")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new DirectoryNotFoundException("Could not locate package root containing Styles/BaseStyles.uss.");
        }

        [TestCase(TerminalMotionPreset.Off, 0f, 0f, 0f, "terminal-motion--none")]
        [TestCase(TerminalMotionPreset.Subtle, 0.12f, 0.10f, 0.10f, "terminal-motion--subtle")]
        [TestCase(TerminalMotionPreset.Snappy, 0.06f, 0.05f, 0.05f, "terminal-motion--snappy")]
        public void MetricsMatchPresetDurationsAndCssClasses(TerminalMotionPreset preset, float openDuration, float closeDuration, float cssTransitionDuration, string cssClass)
        {
            TerminalMotionPresetMetrics metrics = TerminalMotionPresetMetrics.For(preset);
            Assert.That(metrics.Preset, Is.EqualTo(preset));
            Assert.That(metrics.CssClass, Is.EqualTo(cssClass));
            Assert.That(metrics.OpenDurationSeconds, Is.EqualTo(openDuration));
            Assert.That(metrics.CloseDurationSeconds, Is.EqualTo(closeDuration));
            Assert.That(metrics.CssTransitionDurationSeconds, Is.EqualTo(cssTransitionDuration));
            Assert.That(metrics.OpenDurationSeconds <= 0.12f, Is.True, "All presets must stay at or under 120 ms.");
            Assert.That(metrics.CloseDurationSeconds <= 0.12f, Is.True, "All presets must stay at or under 120 ms.");
            Assert.That(metrics.CssTransitionDurationSeconds <= 0.12f, Is.True, "All CSS transitions must stay at or under 120 ms.");
        }

        [Test]
        public void InvalidValuesNormalizeToSubtleDefault()
        {
#pragma warning disable CS0618
            Assert.That(TerminalMotionPresetMetrics.Normalize(TerminalMotionPreset.Unknown), Is.EqualTo(TerminalMotionPreset.Subtle));
#pragma warning restore CS0618
            Assert.That(TerminalMotionPresetMetrics.Normalize((TerminalMotionPreset)(-1)), Is.EqualTo(TerminalMotionPreset.Subtle));
            Assert.That(TerminalMotionPresetMetrics.Normalize((TerminalMotionPreset)99), Is.EqualTo(TerminalMotionPreset.Subtle));
            Assert.That(TerminalMotionPresetMetrics.For((TerminalMotionPreset)99).OpenDurationSeconds, Is.EqualTo(0.12f));
        }

        [TestCase(0, TerminalMotionPreset.Off)]
        [TestCase(1, TerminalMotionPreset.Subtle)]
        [TestCase(2, TerminalMotionPreset.Snappy)]
        [TestCase(-1, TerminalMotionPreset.Subtle)]
        [TestCase(9, TerminalMotionPreset.Subtle)]
        public void OptionIndicesRoundTripThroughNoneSubtleSnappy(int index, TerminalMotionPreset expected)
        {
            Assert.That(TerminalMotionPresetMetrics.FromOptionIndex(index), Is.EqualTo(expected));
            if (0 <= index && index <= 2)
            {
                Assert.That(TerminalMotionPresetMetrics.ToOptionIndex(expected), Is.EqualTo(index));
            }
        }

        [TestCase(0f, 0f, TerminalMotionPreset.Off)]
        [TestCase(-1f, 0f, TerminalMotionPreset.Off)]
        [TestCase(float.NaN, 0f, TerminalMotionPreset.Off)]
        [TestCase(0.02f, 0.02f, TerminalMotionPreset.Off)]
        [TestCase(0.05f, 0.05f, TerminalMotionPreset.Snappy)]
        [TestCase(0.08f, 0.08f, TerminalMotionPreset.Snappy)]
        [TestCase(0.10f, 0.10f, TerminalMotionPreset.Subtle)]
        [TestCase(0.5f, 0.5f, TerminalMotionPreset.Subtle)]
        [TestCase(1.5f, 1.0f, TerminalMotionPreset.Subtle)]
        public void LegacyTimingMigratesToNearestPreset(float easeOut, float easeIn, TerminalMotionPreset expected)
        {
            Assert.That(TerminalMotionPresetMetrics.MigrateLegacyTiming(easeOut, easeIn), Is.EqualTo(expected));
        }

        [Test]
        public void EaseOutCurvesAreMonotonicAndSnappySettlesFasterThanSubtle()
        {
            TerminalMotionPresetMetrics none = TerminalMotionPresetMetrics.For(TerminalMotionPreset.Off);
            TerminalMotionPresetMetrics subtle = TerminalMotionPresetMetrics.For(TerminalMotionPreset.Subtle);
            TerminalMotionPresetMetrics snappy = TerminalMotionPresetMetrics.For(TerminalMotionPreset.Snappy);

            Assert.That(none.EvaluateEaseOut(0f), Is.EqualTo(1f));
            Assert.That(none.EvaluateEaseOut(1f), Is.EqualTo(1f));

            Assert.That(subtle.EvaluateEaseOut(0f), Is.EqualTo(0f));
            Assert.That(subtle.EvaluateEaseOut(1f), Is.EqualTo(1f));
            Assert.That(0.5f < subtle.EvaluateEaseOut(0.5f), Is.True, "Subtle ease-out must progress past midpoint at t=0.5.");

            Assert.That(snappy.EvaluateEaseOut(0f), Is.EqualTo(0f));
            Assert.That(snappy.EvaluateEaseOut(1f), Is.EqualTo(1f));
            Assert.That(subtle.EvaluateEaseOut(0.5f) < snappy.EvaluateEaseOut(0.5f), Is.True, "Snappy ease-out must progress faster than Subtle at t=0.5.");
        }

        [Test]
        public void BaseStylesZeroesEveryTransitionUnderMotionNone()
        {
            string ussPath = Path.Combine(FindPackageRoot(), "Styles", "BaseStyles.uss");
            string uss = File.ReadAllText(ussPath);

            Assert.That(uss.Contains(".terminal-motion--none .log-scroll-view .unity-scroller .unity-base-slider__tracker", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains(".terminal-motion--none .log-scroll-view .unity-scroller .unity-base-slider__dragger", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains(".terminal-motion--none .autocomplete-item", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains(".terminal-motion--none .palette-results .unity-scroller .unity-base-slider__tracker", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains(".terminal-motion--none .palette-results .unity-scroller .unity-base-slider__dragger", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains(".terminal-motion--none .palette-row", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains("transition-duration: 0s;", StringComparison.Ordinal), Is.True);
            Assert.That(uss.Contains("transition-duration: 0.05s;", StringComparison.Ordinal), Is.True);
        }
    }
}
