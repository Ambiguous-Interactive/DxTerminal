namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
#if UNITY_EDITOR
    using UnityEditor.PackageManager;
#endif

    public sealed class ThemeContrastTests
    {
        private const double TextMinimum = 4.5;
        private const double OutlineMinimum = 3.0;

        private const string ThemeSheet = "Styles/Themes/DxThemes.uss";
        private static readonly string[] ThemeClasses = { "dx-dark-theme", "dx-paper-theme", "phosphor-theme" };
        private static readonly Lazy<Dictionary<string, Dictionary<string, Rgb>>> Themes = new(LoadThemes);

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

        private static Dictionary<string, Dictionary<string, Rgb>> LoadThemes()
        {
#if UNITY_EDITOR
            PackageInfo package = PackageInfo.FindForAssetPath("Packages/com.wallstop-studios.dxcommandterminal/" + ThemeSheet);
            string sheet = File.ReadAllText(Path.Combine(package.resolvedPath, ThemeSheet));
#else
            using StreamReader reader = new(typeof(ThemeContrastTests).Assembly.GetManifestResourceStream(ThemeSheet));
            string sheet = reader.ReadToEnd();
#endif
            Dictionary<string, Dictionary<string, Rgb>> themes = new(StringComparer.Ordinal);
            foreach (string themeClass in ThemeClasses)
            {
                Match block = Regex.Match(sheet, @"^\." + themeClass + @"\s*\{([^}]*)\}", RegexOptions.Multiline);
                Assert.That(block.Success, Is.True, $"Missing theme .{themeClass}");
                Dictionary<string, Rgb> roles = new(StringComparer.Ordinal);
                foreach (Match declaration in Regex.Matches(block.Groups[1].Value, @"^\s*--dxt-([a-z-]+):\s*rgba\(([^)]*)\);", RegexOptions.Multiline))
                {
                    string[] components = declaration.Groups[2].Value.Split(',');
                    roles.Add(
                        declaration.Groups[1].Value,
                        new Rgb(
                            byte.Parse(components[0], CultureInfo.InvariantCulture) / 255.0,
                            byte.Parse(components[1], CultureInfo.InvariantCulture) / 255.0,
                            byte.Parse(components[2], CultureInfo.InvariantCulture) / 255.0,
                            double.Parse(components[3], CultureInfo.InvariantCulture)
                        )
                    );
                }

                themes.Add(themeClass, roles);
            }

            return themes;
        }

        private static IEnumerable<TestCaseData> ContrastCases()
        {
            foreach (string theme in ThemeClasses)
            {
                foreach ((string foreground, string background, double minimum) in Pairs)
                {
                    yield return new TestCaseData(theme, foreground, background, minimum);
                }
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

        private static (double Ratio, string Result) MeasureContrast(string theme, string foregroundRole, string backgroundRole)
        {
            Dictionary<string, Rgb> colors = Themes.Value[theme];
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
            return (worstRatio, result);
        }

        [TestCaseSource(nameof(ContrastCases))]
        public void RolesMeetContrastMinimum(string theme, string foregroundRole, string backgroundRole, double minimum)
        {
            (double ratio, string result) = MeasureContrast(theme, foregroundRole, backgroundRole);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(minimum), result);
        }

        private readonly struct Rgb
        {
            private readonly double _red;
            private readonly double _green;
            private readonly double _blue;
            private readonly double _alpha;

            public Rgb(int hex, double alpha = 1)
                : this((hex >> 16) / 255.0, ((hex >> 8) & 255) / 255.0, (hex & 255) / 255.0, alpha) { }

            public Rgb(double red, double green, double blue, double alpha)
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
