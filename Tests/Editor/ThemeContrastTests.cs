namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using Themes;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;
    using UnityEngine.UIElements;
#endif

    public sealed class ThemeContrastTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal/";
        private const string ThemeSheet = "Styles/Themes/DxThemes.uss";
        private static readonly string[] ThemeClasses = { "dx-dark-theme", "dx-paper-theme", "phosphor-theme" };
        private static readonly Lazy<Dictionary<string, Dictionary<string, Rgb>>> Themes = new(LoadThemes);

        private static readonly (string Name, Rgb Color)[] Backdrops = { ("black", new Rgb(0x000000)), ("mid gray", new Rgb(0x808080)), ("white", new Rgb(0xFFFFFF)) };

        private static readonly (string Mode, double[,] Matrix)[] VisionModels =
        {
            (
                "normal",
                new double[,]
                {
                    { 1, 0, 0 },
                    { 0, 1, 0 },
                    { 0, 0, 1 },
                }
            ),
            // Machado et al. (2009), full severity, https://doi.org/10.1109/TVCG.2009.113
            (
                "protan",
                new double[,]
                {
                    { 0.152286, 1.052583, -0.204868 },
                    { 0.114503, 0.786281, 0.099216 },
                    { -0.003882, -0.048116, 1.051998 },
                }
            ),
            (
                "deutan",
                new double[,]
                {
                    { 0.367322, 0.860646, -0.227968 },
                    { 0.280085, 0.672501, 0.047413 },
                    { -0.011820, 0.042940, 0.968881 },
                }
            ),
            (
                "tritan",
                new double[,]
                {
                    { 1.255528, -0.076749, -0.178779 },
                    { -0.078411, 0.930809, 0.147602 },
                    { 0.004733, 0.691367, 0.303900 },
                }
            ),
        };

        private static Dictionary<string, Dictionary<string, Rgb>> LoadThemes()
        {
#if UNITY_EDITOR
            PackageInfo package = PackageInfo.FindForAssetPath(PackageRoot + ThemeSheet);
            string sheet = File.ReadAllText(Path.Combine(package.resolvedPath, ThemeSheet));
#else
            using StreamReader reader = new(typeof(ThemeContrastTests).Assembly.GetManifestResourceStream(ThemeSheet));
            string sheet = reader.ReadToEnd();
#endif
            Dictionary<string, Dictionary<string, Rgb>> themes = ParseThemes(sheet);
            Assert.That(themes.Keys, Is.EquivalentTo(ThemeClasses));
            return themes;
        }

        private static Dictionary<string, Dictionary<string, Rgb>> ParseThemes(string sheet)
        {
            sheet = Regex.Replace(sheet, @"/\*[\s\S]*?\*/", string.Empty);
            Dictionary<string, Dictionary<string, Rgb>> themes = new(StringComparer.Ordinal);
            foreach (Match block in Regex.Matches(sheet, @"^\.([a-z-]+)\s*\{([^}]*)\}", RegexOptions.Multiline))
            {
                string themeClass = block.Groups[1].Value;
                Dictionary<string, Rgb> roles = new(StringComparer.Ordinal);
                foreach (string declaration in block.Groups[2].Value.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(declaration))
                    {
                        continue;
                    }

                    Match literal = Regex.Match(declaration.Trim(), @"^--([a-z-]+):\s*rgba\(([^)]*)\)$");
                    Assert.That(literal.Success, Is.True, $"{themeClass}: {declaration}");
                    string token = literal.Groups[1].Value;
                    string[] components = literal.Groups[2].Value.Split(',');
                    Assert.That(components.Length, Is.EqualTo(4), $"{themeClass} --{token}");
                    double alpha = double.Parse(components[3], CultureInfo.InvariantCulture);
                    Assert.That(alpha, Is.InRange(0.0, 1.0), $"{themeClass} --{token} alpha");
                    roles.Add(
                        token,
                        new Rgb(
                            byte.Parse(components[0], CultureInfo.InvariantCulture) / 255.0,
                            byte.Parse(components[1], CultureInfo.InvariantCulture) / 255.0,
                            byte.Parse(components[2], CultureInfo.InvariantCulture) / 255.0,
                            alpha
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
                foreach ((string foreground, string background, double minimum) in ThemeContrast.Pairs)
                {
                    yield return new TestCaseData(theme, foreground, background, minimum);
                }
            }
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
            Color? layer = string.Equals(backgroundRole, "surface", StringComparison.Ordinal) ? null : colors["dxt-" + backgroundRole].ToColor();
            (double worstRatio, string worstBackdrop) = ThemeContrast.Measure(colors["dxt-" + foregroundRole].ToColor(), colors["dxt-surface"].ToColor(), layer);
            double weakestLc = double.PositiveInfinity;
            string weakestBackdrop = string.Empty;
            foreach ((string backdrop, Rgb game) in Backdrops)
            {
                Rgb surface = colors["dxt-surface"].Over(game);
                Rgb background = string.Equals(backgroundRole, "surface", StringComparison.Ordinal) ? surface : colors["dxt-" + backgroundRole].Over(surface);
                Rgb foreground = colors["dxt-" + foregroundRole].Over(background);
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

        [Test]
        public void NewThemeAssetGeneratesEveryDxDarkColor()
        {
            TerminalThemeAsset asset = TerminalThemeAsset.CreateSemantic();
            try
            {
                asset.name = "sample";
                Dictionary<string, Rgb> roles = ParseThemes(asset.BuildUss())["sample-theme"];
                Assert.That(roles, Is.EquivalentTo(Themes.Value["dx-dark-theme"]));
            }
            finally
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(asset);
#endif
            }
        }

        [Test]
        public void ContrastRejectsTextThatOnlyPassesOnDarkBackdrops()
        {
            Color surface = new(13f / 255f, 17f / 255f, 23f / 255f, 0.94f);
            Color error = new(236f / 255f, 70f / 255f, 97f / 255f);
            (double ratio, string backdrop) = ThemeContrast.Measure(error, surface, null);
            Assert.That(backdrop, Is.EqualTo("white"));
            Assert.That(ratio, Is.EqualTo(4.41).Within(0.01));
        }

        [TestCaseSource(nameof(ContrastCases))]
        public void RolesMeetContrastMinimum(string theme, string foregroundRole, string backgroundRole, double minimum)
        {
            (double ratio, string result) = MeasureContrast(theme, foregroundRole, backgroundRole);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(minimum), result);
        }

        [TestCase("dx-dark-theme")]
        [TestCase("dx-paper-theme")]
        [TestCase("phosphor-theme")]
        public void WidgetVariablesMatchSemanticRoles(string theme)
        {
            Dictionary<string, Rgb> colors = Themes.Value[theme];
            (string Widget, string Role)[] mappings =
            {
                ("terminal-bg", "surface"),
                ("button-bg", "surface-raised"),
                ("input-field-bg", "input"),
                ("button-selected-bg", "selection"),
                ("button-hover-bg", "hover"),
                ("scroll-bg", "surface"),
                ("scroll-color", "ink-muted"),
                ("scroll-inverse-bg", "ink-muted"),
                ("scroll-active-bg", "ink-body"),
                ("button-text", "ink-body"),
                ("button-selected-text", "ink"),
                ("button-hover-text", "ink"),
                ("input-text-color", "ink"),
                ("text-message", "ink-body"),
                ("text-warning", "warning"),
                ("text-input-echo", "accent-soft"),
                ("text-shell", "ink-muted"),
                ("text-error", "error-text"),
                ("caret-color", "accent"),
            };
            foreach ((string widget, string role) in mappings)
            {
                Assert.That(colors[widget], Is.EqualTo(colors["dxt-" + role]), $"{theme} --{widget}");
            }

            Assert.That(colors.ContainsKey("dxt-line"), Is.True, $"{theme} needs a separator color");
        }

        [TestCase("dx-dark-theme", "syn-number", "syn-string", false)]
        [TestCase("dx-dark-theme", "syn-number", "syn-choice", false)]
        [TestCase("dx-dark-theme", "syn-number", "error-text", false)]
        [TestCase("dx-dark-theme", "syn-string", "syn-choice", false)]
        [TestCase("dx-dark-theme", "syn-string", "error-text", false)]
        [TestCase("dx-dark-theme", "syn-choice", "error-text", false)]
        [TestCase("dx-paper-theme", "syn-number", "syn-string", false)]
        [TestCase("dx-paper-theme", "syn-number", "syn-choice", false)]
        [TestCase("dx-paper-theme", "syn-string", "syn-choice", false)]
        [TestCase("dx-paper-theme", "syn-choice", "error-text", false)]
        [TestCase("dx-paper-theme", "syn-number", "error-text", false)]
        // The Paper error keeps its glyph and underline below the usual CVD minimum.
        [TestCase("dx-paper-theme", "syn-string", "error-text", true)]
        [TestCase("phosphor-theme", "syn-number", "error-text", false)]
        [TestCase("phosphor-theme", "syn-choice", "error-text", false)]
        public void SyntaxHuesMeetDistanceMinimum(string theme, string first, string second, bool errorUnderline)
        {
            Dictionary<string, Rgb> colors = Themes.Value[theme];
            foreach ((string backdrop, Rgb game) in Backdrops)
            {
                Rgb surface = colors["dxt-surface"].Over(game);
                Rgb input = colors["dxt-input"].Over(surface);
                foreach ((string background, Rgb color) in new[] { ("input", input), ("surface", surface) })
                {
                    Rgb firstColor = colors["dxt-" + first].Over(color);
                    Rgb secondColor = colors["dxt-" + second].Over(color);
                    foreach ((string mode, double[,] matrix) in VisionModels)
                    {
                        double minimum =
                            string.Equals(mode, "normal", StringComparison.Ordinal) ? 15.0
                            : errorUnderline ? 6.0
                            : 8.0;
                        double distance = firstColor.OkLabDistance(secondColor, matrix);
                        string result = FormattableString.Invariant($"{theme} {first}/{second} on {background} over {backdrop}, {mode}: {distance:F2}");
                        TestContext.WriteLine(result);
                        Assert.That(distance, Is.GreaterThanOrEqualTo(minimum), result);
                    }
                }
            }
        }

        // The renderer milestones own the weight and italic cues.
        [TestCase("dx-paper-theme", "syn-choice", "ink")]
        [TestCase("phosphor-theme", "syn-choice", "ink")]
        [TestCase("phosphor-theme", "syn-number", "ink-body")]
        [TestCase("phosphor-theme", "syn-string", "ink-body")]
        public void CueBasedSyntaxUsesTheSharedInk(string theme, string syntax, string ink)
        {
            Dictionary<string, Rgb> colors = Themes.Value[theme];
            Assert.That(colors["dxt-" + syntax], Is.EqualTo(colors["dxt-" + ink]));
        }

#if UNITY_EDITOR
        [TestCase("ClassicThemes", 126)]
        [TestCase("Large", 63)]
        [TestCase("Medium", 26)]
        public void ClassicThemePacksExposeOneChoicePerStylesheet(string packName, int count)
        {
            string packPath = PackageRoot + $"Packs/Themes/{packName}.asset";
            TerminalThemePack pack = AssetDatabase.LoadAssetAtPath<TerminalThemePack>(packPath);
            Assert.That(pack != null, Is.True, packPath);
            Assert.That(pack.Themes.Count, Is.EqualTo(count));
            Assert.That(pack.ThemeNames.Count, Is.EqualTo(count));
            Assert.That(pack.ThemeNames, Is.Unique);
            PackageInfo package = PackageInfo.FindForAssetPath(packPath);
            for (int index = 0; index < count; ++index)
            {
                StyleSheet sheet = pack.Themes[index];
                Assert.That(sheet != null, Is.True, $"Missing stylesheet {index}");
                string sheetPath = AssetDatabase.GetAssetPath(sheet);
                string filePath = Path.Combine(package.resolvedPath, sheetPath.Substring(PackageRoot.Length));
                string css = File.ReadAllText(filePath);
                Match rule = Regex.Match(css, @"^\.([a-z0-9-]+)\s*\{", RegexOptions.Multiline);
                Assert.That(rule.Success, Is.True, sheetPath);
                Assert.That(pack.ThemeNames[index], Is.EqualTo(rule.Groups[1].Value), sheetPath);
            }

            Assert.That(pack.ThemeNames, Does.Contain("dracula-theme"));
            Assert.That(pack.ThemeNames, Does.Contain("one-dark-pro-theme"));
        }

        [Test]
        public void ClassicPackKeepsTheExistingPackGuid()
        {
            const string existingPackGuid = "5c84fcacc1618bf44aecf333887191e1";
            string path = AssetDatabase.GUIDToAssetPath(existingPackGuid);
            Assert.That(path, Is.EqualTo(PackageRoot + "Packs/Themes/ClassicThemes.asset"));
            TerminalThemePack pack = AssetDatabase.LoadAssetAtPath<TerminalThemePack>(path);
            Assert.That(pack != null, Is.True);
            Assert.That(pack.name, Is.EqualTo("Classic Themes"));
        }

        [Test]
        public void DxPackReferencesTheMeasuredStylesheet()
        {
            TerminalThemePack pack = AssetDatabase.LoadAssetAtPath<TerminalThemePack>(PackageRoot + "Packs/Themes/Dx.asset");
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(PackageRoot + ThemeSheet);
            Assert.That(pack != null, Is.True);
            Assert.That(sheet != null, Is.True);
            Assert.That(pack._themes, Is.EqualTo(new[] { sheet }));
            Assert.That(pack._themeNames, Is.EqualTo(ThemeClasses));
        }
#endif

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

            private static double SimulateChannel(double[,] matrix, int row, double red, double green, double blue)
            {
                return Math.Clamp(matrix[row, 0] * red + matrix[row, 1] * green + matrix[row, 2] * blue, 0, 1);
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

            public double OkLabDistance(Rgb other, double[,] matrix)
            {
                (double firstL, double firstA, double firstB) = ToOkLab(matrix);
                (double secondL, double secondA, double secondB) = other.ToOkLab(matrix);
                double deltaL = firstL - secondL;
                double deltaA = firstA - secondA;
                double deltaB = firstB - secondB;
                return 100 * Math.Sqrt(deltaL * deltaL + deltaA * deltaA + deltaB * deltaB);
            }

            public Color ToColor()
            {
                return new Color((float)_red, (float)_green, (float)_blue, (float)_alpha);
            }

            public double ApcaLuminance()
            {
                double luminance = 0.2126729 * Math.Pow(_red, 2.4) + 0.7151522 * Math.Pow(_green, 2.4) + 0.0721750 * Math.Pow(_blue, 2.4);
                return luminance < 0.022 ? luminance + Math.Pow(0.022 - luminance, 1.414) : luminance;
            }

            // Linear sRGB to Oklab, https://bottosson.github.io/posts/oklab/
            private (double L, double A, double B) ToOkLab(double[,] matrix)
            {
                double linearRed = Linearize(_red);
                double linearGreen = Linearize(_green);
                double linearBlue = Linearize(_blue);
                double red = SimulateChannel(matrix, 0, linearRed, linearGreen, linearBlue);
                double green = SimulateChannel(matrix, 1, linearRed, linearGreen, linearBlue);
                double blue = SimulateChannel(matrix, 2, linearRed, linearGreen, linearBlue);
                double l = Math.Pow(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue, 1.0 / 3.0);
                double m = Math.Pow(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue, 1.0 / 3.0);
                double s = Math.Pow(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue, 1.0 / 3.0);
                return (
                    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
                );
            }
        }
    }
}
