namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal static class ThemeContrastRepair
    {
        private const int BelowSrgb = -1;
        private const int AboveSrgb = 256;
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

                if (!TryFindPassingColor(colors, foreground, colors[foreground], out Color passing))
                {
                    repaired = original;
                    return false;
                }

                colors[foreground] = passing;
            }

            repaired = colors;
            return true;
        }

        internal static (double L, double A, double B) ToLab(Color color)
        {
            // sRGB matrices from https://bottosson.github.io/posts/oklab/.
            double red = ThemeContrast.Linearize(color.r);
            if (color.r.Equals(color.g) && color.r.Equals(color.b))
            {
                return (Math.Pow(red, 1.0 / 3.0), 0, 0);
            }

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

        private static bool TryFindPassingColor(IReadOnlyDictionary<string, Color> colors, string role, Color source, out Color result)
        {
            (double lightness, double a, double b) = ToLab(source);
            // Fixed a and b preserve OKLCH chroma and hue along each channel's cubic.
            double l = 0.3963377774 * a + 0.2158037573 * b;
            double m = -0.1055613458 * a - 0.0638541728 * b;
            double s = -0.0894841775 * a - 1.2914855480 * b;
            SrgbChannel red = new(4.0767416621, -3.3077115913, 0.2309699292, l, m, s);
            SrgbChannel green = new(-1.2684380046, 2.6097574011, -0.3413193965, l, m, s);
            SrgbChannel blue = new(-0.0041960863, -0.7034186147, 1.7076147010, l, m, s);

            SortedSet<double> boundaries = new() { 0, lightness, 1 };
            red.AddBoundaries(boundaries);
            green.AddBoundaries(boundaries);
            blue.AddBoundaries(boundaries);

            double bestDistance = double.PositiveInfinity;
            Color best = source;
            double previous = 0;
            foreach (double boundary in boundaries)
            {
                // Every open interval has one emitted RGB value, even if it is very narrow.
                double nearest = Math.Clamp(lightness, previous, boundary);
                Consider((previous + boundary) * 0.5, Math.Abs(nearest - lightness));
                Consider(boundary, Math.Abs(boundary - lightness));
                previous = boundary;
            }

            result = best;
            return bestDistance < double.PositiveInfinity;

            void Consider(double at, double distance)
            {
                if (bestDistance <= distance)
                {
                    return;
                }

                int r = red.ReadCode(at);
                int g = green.ReadCode(at);
                int bCode = blue.ReadCode(at);
                if (r is BelowSrgb or AboveSrgb || g is BelowSrgb or AboveSrgb || bCode is BelowSrgb or AboveSrgb)
                {
                    return;
                }

                Color candidate = new(r / 255f, g / 255f, bCode / 255f, source.a);
                if (Passes(colors, role, candidate))
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }

        private readonly struct SrgbChannel
        {
            private readonly double _quadratic;
            private readonly double _linear;
            private readonly double _constant;

            internal SrgbChannel(double lWeight, double mWeight, double sWeight, double l, double m, double s)
            {
                // The inverse matrix rows sum to 1, so the cubic coefficient is 1.
                _quadratic = 3 * (lWeight * l + mWeight * m + sWeight * s);
                _linear = 3 * (lWeight * l * l + mWeight * m * m + sWeight * s * s);
                _constant = lWeight * l * l * l + mWeight * m * m * m + sWeight * s * s * s;
            }

            internal int ReadCode(double lightness)
            {
                double linear = ((lightness + _quadratic) * lightness + _linear) * lightness + _constant;
                if (linear < -GamutTolerance)
                {
                    return BelowSrgb;
                }

                if (1.0 + GamutTolerance < linear)
                {
                    return AboveSrgb;
                }

                float encoded = (float)(linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055);
                return TerminalThemeRoles.QuantizeChannel(encoded);
            }

            internal void AddBoundaries(SortedSet<double> boundaries)
            {
                // The derivative roots divide the cubic into monotone segments.
                double discriminant = _quadratic * _quadratic - 3 * _linear;
                if (discriminant <= 0)
                {
                    AddCrossings(boundaries, 0, 1);
                    return;
                }

                double root = Math.Sqrt(discriminant);
                double first = Math.Clamp((-_quadratic - root) / 3, 0, 1);
                double second = Math.Clamp((-_quadratic + root) / 3, 0, 1);
                boundaries.Add(first);
                boundaries.Add(second);
                AddCrossings(boundaries, 0, first);
                AddCrossings(boundaries, first, second);
                AddCrossings(boundaries, second, 1);
            }

            private void AddCrossings(SortedSet<double> boundaries, double from, double to)
            {
                int first = ReadCode(from);
                int last = ReadCode(to);
                bool rising = first < last;
                int upper = Math.Max(first, last);
                for (int code = Math.Min(first, last) + 1; code <= upper; ++code)
                {
                    double low = from;
                    double high = to;
                    while (true)
                    {
                        double middle = (low + high) * 0.5;
                        if (middle == low || middle == high)
                        {
                            break;
                        }

                        if (rising == (code <= ReadCode(middle)))
                        {
                            high = middle;
                        }
                        else
                        {
                            low = middle;
                        }
                    }

                    boundaries.Add(high);
                }
            }
        }
    }
}
