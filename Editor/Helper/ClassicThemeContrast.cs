namespace WallstopStudios.DxCommandTerminal.Editor.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.RegularExpressions;
    using Themes;
    using UnityEditor;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal static class ClassicThemeContrast
    {
        private const string PackDirectory = "Packages/com.wallstop-studios.dxcommandterminal/Packs/Themes/";
        private const string SheetDirectory = "Styles/Themes/";
        private const string Surface = "terminal-bg";

        private static readonly Regex Rule = new(@"^\.([a-z0-9-]+)\s*\{([^}]*)\}", RegexOptions.Multiline);
        private static readonly Regex ColorDeclaration = new(@"--([a-z-]+)\s*:\s*rgba?\(([^)]+)\)\s*;");

        private static readonly (string Foreground, string Background)[] TextPairs =
        {
            ("text-message", Surface),
            ("text-warning", Surface),
            ("text-error", Surface),
            ("text-input-echo", Surface),
            ("text-shell", Surface),
            ("input-text-color", "input-field-bg"),
            ("button-text", "button-bg"),
            ("button-selected-text", "button-selected-bg"),
            ("button-hover-text", "button-hover-bg"),
        };

        internal static Dictionary<string, double> ReadRatios(TerminalThemePack pack)
        {
            Dictionary<string, double> ratios = new(StringComparer.OrdinalIgnoreCase);
            string packPath = AssetDatabase.GetAssetPath(pack);
            if (!packPath.StartsWith(PackDirectory, StringComparison.Ordinal))
            {
                return ratios;
            }

            PackageInfo package = PackageInfo.FindForAssetPath(packPath);
            if (package == null)
            {
                return ratios;
            }

            string directory = $"{package.assetPath}/{SheetDirectory}";
            foreach (StyleSheet sheet in pack.Themes)
            {
                if (sheet == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(sheet);
                if (!path.StartsWith(directory, StringComparison.Ordinal))
                {
                    continue;
                }

                string source = TerminalThemeAsset.TryReadText(Path.Combine(package.resolvedPath, SheetDirectory, Path.GetFileName(path)));
                if (source != null && TryMeasure(source, out string theme, out double ratio))
                {
                    ratios[theme] = ratio;
                }
            }

            return ratios;
        }

        private static bool TryMeasure(string source, out string theme, out double ratio)
        {
            Match rule = Rule.Match(source);
            Dictionary<string, Color> colors = new(StringComparer.Ordinal);
            foreach (Match declaration in ColorDeclaration.Matches(rule.Groups[2].Value))
            {
                if (TryReadColor(declaration.Groups[2].Value, out Color color))
                {
                    colors[declaration.Groups[1].Value] = color;
                }
            }

            if (colors.ContainsKey("dxt-surface") || !colors.TryGetValue(Surface, out Color surface))
            {
                theme = default;
                ratio = default;
                return false;
            }

            double worst = double.PositiveInfinity;
            foreach ((string foreground, string background) in TextPairs)
            {
                if (!colors.TryGetValue(foreground, out Color text) || !colors.TryGetValue(background, out Color layer))
                {
                    theme = default;
                    ratio = default;
                    return false;
                }

                Color? backdrop = string.Equals(background, Surface, StringComparison.Ordinal) ? null : layer;
                worst = Math.Min(worst, ThemeContrast.Measure(text, surface, backdrop).Ratio);
            }

            theme = rule.Groups[1].Value;
            ratio = worst;
            return true;
        }

        private static bool TryReadColor(string color, out Color parsed)
        {
            string[] channels = color.Split(',');
            if (
                channels.Length is 3 or 4
                && byte.TryParse(channels[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte red)
                && byte.TryParse(channels[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte green)
                && byte.TryParse(channels[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte blue)
            )
            {
                float alpha = 1f;
                if (channels.Length == 3 || float.TryParse(channels[3], NumberStyles.Float, CultureInfo.InvariantCulture, out alpha) && 0f <= alpha && alpha <= 1f)
                {
                    parsed = new Color(red / 255f, green / 255f, blue / 255f, alpha);
                    return true;
                }
            }

            parsed = default;
            return false;
        }
    }
#endif
}
