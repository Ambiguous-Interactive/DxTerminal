namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;

    public sealed class ThemeContrastTests
    {
        private const double TextMinimum = 4.5;
        private const double OutlineMinimum = 3.0;

        // Handoff DxThemes.uss supplies the proposed roles. DESIGN.md 3.2 adds error-line.
        private static readonly Dictionary<string, Dictionary<string, Rgb>> Themes = new(StringComparer.Ordinal)
        {
            ["Dx Dark"] = new Dictionary<string, Rgb>(StringComparer.Ordinal)
            {
                ["surface"] = new Rgb(0x0D1117, 0.94),
                ["surface-raised"] = new Rgb(0x161C25),
                ["hover"] = new Rgb(0x1D2530),
                ["input"] = new Rgb(0x0A0E13),
                ["ink"] = new Rgb(0xE6EDF3),
                ["ink-body"] = new Rgb(0xC9D2DC),
                ["ink-muted"] = new Rgb(0x8B949E),
                ["accent"] = new Rgb(0x5CD68F),
                ["accent-soft"] = new Rgb(0xB8F2CF),
                ["on-accent"] = new Rgb(0x0D1117),
                ["selection"] = new Rgb(0x12301F),
                ["selection-line"] = new Rgb(0x2B6B45),
                ["warning"] = new Rgb(0xF4A836),
                ["error"] = new Rgb(0xEC4661),
                ["error-line"] = new Rgb(0xB4445A),
            },
            ["Dx Paper"] = new Dictionary<string, Rgb>(StringComparer.Ordinal)
            {
                ["surface"] = new Rgb(0xF4F1EA, 0.97),
                ["surface-raised"] = new Rgb(0xFAF8F3),
                ["hover"] = new Rgb(0xEFEBE1),
                ["input"] = new Rgb(0xFFFFFF),
                ["ink"] = new Rgb(0x1B1813),
                ["ink-body"] = new Rgb(0x46423A),
                ["ink-muted"] = new Rgb(0x5F5A4F),
                ["accent"] = new Rgb(0x1F7A47),
                ["accent-soft"] = new Rgb(0x23704A),
                ["on-accent"] = new Rgb(0xFFFFFF),
                ["selection"] = new Rgb(0xDCEFE3),
                ["selection-line"] = new Rgb(0x8CC5A3),
                ["warning"] = new Rgb(0x9A5B00),
                ["error"] = new Rgb(0xC42847),
                ["error-line"] = new Rgb(0xC4677A),
            },
            ["Phosphor"] = new Dictionary<string, Rgb>(StringComparer.Ordinal)
            {
                ["surface"] = new Rgb(0x07110B, 0.96),
                ["surface-raised"] = new Rgb(0x0C1A11),
                ["hover"] = new Rgb(0x12251A),
                ["input"] = new Rgb(0x050C08),
                ["ink"] = new Rgb(0xC8F7D9),
                ["ink-body"] = new Rgb(0x8FE3B0),
                ["ink-muted"] = new Rgb(0x4A9A6C),
                ["accent"] = new Rgb(0x5CD68F),
                ["accent-soft"] = new Rgb(0xB8F2CF),
                ["on-accent"] = new Rgb(0x07110B),
                ["selection"] = new Rgb(0x143D26),
                ["selection-line"] = new Rgb(0x2F7A4F),
                ["warning"] = new Rgb(0xF4B04A),
                ["error"] = new Rgb(0xFF5C74),
                ["error-line"] = new Rgb(0xB4445A),
            },
        };

        private static readonly (string Foreground, string Background, double Minimum)[] Pairs =
        {
            ("ink", "surface", TextMinimum),
            ("ink", "input", TextMinimum),
            ("ink", "selection", TextMinimum),
            ("ink", "hover", TextMinimum),
            ("ink-body", "surface", TextMinimum),
            ("ink-body", "surface-raised", TextMinimum),
            ("ink-muted", "surface", TextMinimum),
            ("ink-muted", "input", TextMinimum),
            ("accent", "input", TextMinimum),
            ("accent-soft", "surface", TextMinimum),
            ("accent-soft", "selection", TextMinimum),
            ("on-accent", "accent", TextMinimum),
            ("warning", "surface", TextMinimum),
            ("warning", "surface-raised", TextMinimum),
            ("error", "surface", TextMinimum),
            ("error", "surface-raised", TextMinimum),
            ("accent", "surface", OutlineMinimum),
            ("selection-line", "surface", OutlineMinimum),
            ("error-line", "surface", OutlineMinimum),
            ("error-line", "input", OutlineMinimum),
        };

        private static readonly (string Name, Rgb Color)[] Backdrops = { ("black", new Rgb(0x000000)), ("mid gray", new Rgb(0x808080)), ("white", new Rgb(0xFFFFFF)) };

        // Remove these baselines when A2 switches the gate to shipped themes.
        private static readonly Dictionary<(string Theme, string Foreground, string Background), (string Backdrop, double Ratio)> ProposedFailures = new()
        {
            [("Dx Dark", "error", "surface")] = ("white", 4.409032),
            [("Dx Dark", "selection-line", "surface")] = ("white", 2.584630),
            [("Dx Paper", "selection-line", "surface")] = ("black", 1.640742),
        };

        private static IEnumerable<TestCaseData> ContrastCases()
        {
            // Unity's assembly filter can run NUnit explicit cases during a full suite.
            bool strict = string.Equals(Environment.GetEnvironmentVariable("DXT_STRICT_PROPOSED_THEME_CONTRAST"), "1", StringComparison.Ordinal);
            foreach (string theme in Themes.Keys)
            {
                foreach ((string foreground, string background, double minimum) in Pairs)
                {
                    if (!strict && ProposedFailures.ContainsKey((theme, foreground, background)))
                    {
                        continue;
                    }

                    yield return new TestCaseData(theme, foreground, background, minimum);
                }
            }
        }

        private static IEnumerable<TestCaseData> ProposedFailureCases()
        {
            foreach (KeyValuePair<(string Theme, string Foreground, string Background), (string Backdrop, double Ratio)> failure in ProposedFailures)
            {
                yield return new TestCaseData(failure.Key.Theme, failure.Key.Foreground, failure.Key.Background, failure.Value.Backdrop, failure.Value.Ratio);
            }
        }

        private static double Contrast(Rgb foreground, Rgb background)
        {
            double text = foreground.RelativeLuminance();
            double back = background.RelativeLuminance();
            return (Math.Max(text, back) + 0.05) / (Math.Min(text, back) + 0.05);
        }

        private static double Apca(Rgb foreground, Rgb background)
        {
            double text = foreground.ApcaLuminance();
            double back = background.ApcaLuminance();
            if (Math.Abs(back - text) < 0.0005)
            {
                return 0.0;
            }

            if (text < back)
            {
                double lighterBackground = (Math.Pow(back, 0.56) - Math.Pow(text, 0.57)) * 1.14;
                return lighterBackground < 0.1 ? 0.0 : (lighterBackground - 0.027) * 100;
            }

            double darkerBackground = (Math.Pow(back, 0.65) - Math.Pow(text, 0.62)) * 1.14;
            return -0.1 < darkerBackground ? 0.0 : (darkerBackground + 0.027) * 100;
        }

        private static (double Ratio, string Backdrop, string Result) MeasureContrast(string theme, string foregroundRole, string backgroundRole)
        {
            Dictionary<string, Rgb> colors = Themes[theme];
            double worstRatio = double.PositiveInfinity;
            string worstBackdrop = string.Empty;
            double weakestLc = double.PositiveInfinity;
            string weakestBackdrop = string.Empty;
            foreach ((string backdrop, Rgb game) in Backdrops)
            {
                Rgb surface = colors["surface"].Over(game);
                Rgb background = string.Equals(backgroundRole, "surface", StringComparison.Ordinal) ? surface : colors[backgroundRole].Over(surface);
                Rgb foreground = colors[foregroundRole].Over(background);
                double ratio = Contrast(foreground, background);
                if (ratio < worstRatio)
                {
                    worstRatio = ratio;
                    worstBackdrop = backdrop;
                }

                double lc = Apca(foreground, background);
                if (Math.Abs(lc) < Math.Abs(weakestLc))
                {
                    weakestLc = lc;
                    weakestBackdrop = backdrop;
                }
            }

            string pair = $"{theme} {foregroundRole} on {backgroundRole}";
            TestContext.WriteLine(FormattableString.Invariant($"{pair} on {weakestBackdrop}: APCA Lc {weakestLc:F1} (advisory)"));
            string result = FormattableString.Invariant($"{pair} on {worstBackdrop}: {worstRatio:F2}:1");
            TestContext.WriteLine(result);
            return (worstRatio, worstBackdrop, result);
        }

        [TestCaseSource(nameof(ContrastCases))]
        public void RolesMeetContrastMinimum(string theme, string foregroundRole, string backgroundRole, double minimum)
        {
            (double ratio, _, string result) = MeasureContrast(theme, foregroundRole, backgroundRole);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(minimum), result);
        }

        [TestCaseSource(nameof(ProposedFailureCases))]
        public void ProposedFailuresMatchHandoff(string theme, string foregroundRole, string backgroundRole, string expectedBackdrop, double expectedRatio)
        {
            (double ratio, string backdrop, string result) = MeasureContrast(theme, foregroundRole, backgroundRole);
            Assert.That(backdrop, Is.EqualTo(expectedBackdrop), result);
            Assert.That(ratio, Is.EqualTo(expectedRatio).Within(0.000001), result);
        }

        private readonly struct Rgb
        {
            private readonly double _red;
            private readonly double _green;
            private readonly double _blue;
            private readonly double _alpha;

            public Rgb(int hex, double alpha = 1)
                : this((hex >> 16) / 255.0, ((hex >> 8) & 255) / 255.0, (hex & 255) / 255.0, alpha) { }

            private Rgb(double red, double green, double blue, double alpha)
            {
                _red = red;
                _green = green;
                _blue = blue;
                _alpha = alpha;
            }

            private static double Linearize(double channel)
            {
                return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
            }

            public Rgb Over(Rgb opaqueBackground)
            {
                double remaining = 1.0 - _alpha;
                return new Rgb(
                    _red * _alpha + opaqueBackground._red * remaining,
                    _green * _alpha + opaqueBackground._green * remaining,
                    _blue * _alpha + opaqueBackground._blue * remaining,
                    1
                );
            }

            public double RelativeLuminance()
            {
                return 0.2126 * Linearize(_red) + 0.7152 * Linearize(_green) + 0.0722 * Linearize(_blue);
            }

            public double ApcaLuminance()
            {
                double luminance = 0.2126729 * Math.Pow(_red, 2.4) + 0.7151522 * Math.Pow(_green, 2.4) + 0.0721750 * Math.Pow(_blue, 2.4);
                return luminance < 0.022 ? luminance + Math.Pow(0.022 - luminance, 1.414) : luminance;
            }
        }
    }
}
