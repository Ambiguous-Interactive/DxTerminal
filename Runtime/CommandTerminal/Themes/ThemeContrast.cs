namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    internal static class ThemeContrast
    {
        private const double TextMinimum = 4.5;
        private const double OutlineMinimum = 3.0;

        internal static readonly IReadOnlyList<(string Foreground, string Background, double Minimum)> Pairs = Array.AsReadOnly(
            new (string, string, double)[]
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
                ("error-text", "surface", TextMinimum),
                ("error-text", "surface-raised", TextMinimum),
                ("error-text", "input", TextMinimum),
                ("syn-number", "surface", TextMinimum),
                ("syn-number", "input", TextMinimum),
                ("syn-string", "surface", TextMinimum),
                ("syn-string", "input", TextMinimum),
                ("syn-choice", "surface", TextMinimum),
                ("syn-choice", "input", TextMinimum),
                ("error", "surface", OutlineMinimum),
                ("error", "surface-raised", OutlineMinimum),
                ("accent", "surface", OutlineMinimum),
                ("selection-line", "surface", OutlineMinimum),
                ("error-line", "surface", OutlineMinimum),
                ("error-line", "input", OutlineMinimum),
            }
        );

        private static readonly (string Name, Color Color)[] Backdrops =
        {
            ("black", Color.black),
            ("mid gray", new Color(128f / 255f, 128f / 255f, 128f / 255f)),
            ("white", Color.white),
        };

        internal static (double Ratio, string Backdrop) Measure(Color foreground, Color surface, Color? backdropLayer)
        {
            double worstRatio = double.PositiveInfinity;
            string worstBackdrop = string.Empty;
            foreach ((string name, Color game) in Backdrops)
            {
                Color baseColor = Over(surface, game);
                Color background = backdropLayer is Color layer ? Over(layer, baseColor) : baseColor;
                double front = Luminance(Over(foreground, background));
                double back = Luminance(background);
                double ratio = (Math.Max(front, back) + 0.05) / (Math.Min(front, back) + 0.05);
                if (ratio < worstRatio)
                {
                    worstRatio = ratio;
                    worstBackdrop = name;
                }
            }

            return (worstRatio, worstBackdrop);
        }

        private static Color Over(Color foreground, Color opaqueBackground)
        {
            float remaining = 1f - foreground.a;
            return new Color(
                foreground.r * foreground.a + opaqueBackground.r * remaining,
                foreground.g * foreground.a + opaqueBackground.g * remaining,
                foreground.b * foreground.a + opaqueBackground.b * remaining
            );
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * Linearize(color.r) + 0.7152 * Linearize(color.g) + 0.0722 * Linearize(color.b);
        }

        private static double Linearize(double channel)
        {
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }
}
