namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal static class ThemeContrastRepair
    {
        private const int LightnessSteps = 2048;
        private const int RefinementSteps = 12;
        private const double GamutTolerance = 0.000001;

        internal static bool TryRepair(IReadOnlyDictionary<string, Color> original, out IReadOnlyDictionary<string, Color> repaired)
        {
            Dictionary<string, Color> colors = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Color> role in original)
            {
                colors.Add(role.Key, role.Value);
            }

            HashSet<string> handled = new(StringComparer.Ordinal);
            foreach ((string foreground, _, _) in ThemeContrast.Pairs)
            {
                // Accent is repaired before it becomes the background for on-accent.
                if (!handled.Add(foreground) || Passes(colors, foreground, colors[foreground]))
                {
                    continue;
                }

                Color source = colors[foreground];
                (double L, double A, double B) lab = ToLab(source);
                bool foundDark = TryFindPassingColor(colors, foreground, source.a, lab, -1, out (double Distance, Color Color) dark);
                bool foundLight = TryFindPassingColor(colors, foreground, source.a, lab, 1, out (double Distance, Color Color) light);
                if (!foundDark && !foundLight)
                {
                    repaired = original;
                    return false;
                }

                colors[foreground] = foundDark && (!foundLight || dark.Distance <= light.Distance) ? dark.Color : light.Color;
            }

            repaired = colors;
            return true;
        }

        internal static (double L, double A, double B) ToLab(Color color)
        {
            // sRGB matrices from Bjorn Ottosson's Oklab specification.
            double red = ThemeContrast.Linearize(color.r);
            double green = ThemeContrast.Linearize(color.g);
            double blue = ThemeContrast.Linearize(color.b);
            double l = Math.Pow(0.4122214708 * red + 0.5363325363 * green + 0.0514459929 * blue, 1.0 / 3.0);
            double m = Math.Pow(0.2119034982 * red + 0.6806995451 * green + 0.1073969566 * blue, 1.0 / 3.0);
            double s = Math.Pow(0.0883024619 * red + 0.2817188376 * green + 0.6299787005 * blue, 1.0 / 3.0);
            return (
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
            );
        }

        private static bool Passes(IReadOnlyDictionary<string, Color> colors, string role, Color color)
        {
            Color surface = colors["surface"];
            foreach ((string foreground, string background, double minimum) in ThemeContrast.Pairs)
            {
                if (!string.Equals(role, foreground, StringComparison.Ordinal))
                {
                    continue;
                }

                Color? layer = string.Equals(background, "surface", StringComparison.Ordinal) ? null : colors[background];
                if (ThemeContrast.Measure(color, surface, layer).Ratio < minimum)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryFindPassingColor(
            IReadOnlyDictionary<string, Color> colors,
            string role,
            float alpha,
            (double L, double A, double B) lab,
            int direction,
            out (double Distance, Color Color) result
        )
        {
            (double originalL, double a, double b) = lab;
            double previousL = originalL;
            double extent = direction < 0 ? originalL : 1.0 - originalL;
            for (int step = 1; step <= LightnessSteps; ++step)
            {
                double nextL = originalL + direction * Math.Min((double)step / LightnessSteps, extent);
                Color? candidate = FromLab(nextL, a, b, alpha);
                bool reachedGamutEdge = candidate == null;
                if (reachedGamutEdge)
                {
                    // A passing quantized color can lie between the last step and the gamut edge.
                    double outsideL = nextL;
                    nextL = previousL;
                    for (int i = 0; i < RefinementSteps; ++i)
                    {
                        double middle = (nextL + outsideL) * 0.5;
                        if (FromLab(middle, a, b, alpha).HasValue)
                        {
                            nextL = middle;
                        }
                        else
                        {
                            outsideL = middle;
                        }
                    }

                    candidate = FromLab(nextL, a, b, alpha);
                }

                if (candidate is Color passing && Passes(colors, role, passing))
                {
                    for (int i = 0; i < RefinementSteps; ++i)
                    {
                        double middle = (previousL + nextL) * 0.5;
                        if (FromLab(middle, a, b, alpha) is Color closer && Passes(colors, role, closer))
                        {
                            nextL = middle;
                            passing = closer;
                        }
                        else
                        {
                            previousL = middle;
                        }
                    }

                    result = (Math.Abs(nextL - originalL), passing);
                    return true;
                }

                if (reachedGamutEdge || nextL == 0 || nextL == 1)
                {
                    break;
                }

                previousL = nextL;
            }

            result = default;
            return false;
        }

        private static Color? FromLab(double lightness, double a, double b, float alpha)
        {
            // Fixed a and b keep OKLCH chroma and hue fixed.
            double l = Cube(lightness + 0.3963377774 * a + 0.2158037573 * b);
            double m = Cube(lightness - 0.1055613458 * a - 0.0638541728 * b);
            double s = Cube(lightness - 0.0894841775 * a - 1.2914855480 * b);
            double red = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            double green = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            double blue = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
            if (!InGamut(red) || !InGamut(green) || !InGamut(blue))
            {
                return null;
            }

            return TerminalThemeRoles.Quantize(new Color(Encode(red), Encode(green), Encode(blue), alpha));
        }

        private static double Cube(double value)
        {
            return value * value * value;
        }

        private static bool InGamut(double value)
        {
            return -GamutTolerance <= value && value <= 1.0 + GamutTolerance;
        }

        private static float Encode(double linear)
        {
            return (float)(linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055);
        }
    }
}
