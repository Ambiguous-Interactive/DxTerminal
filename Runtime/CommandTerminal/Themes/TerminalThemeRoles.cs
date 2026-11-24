namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    [Serializable]
    internal sealed class TerminalThemeRoles
    {
        [SerializeField]
        private Color _surface = new(13f / 255f, 17f / 255f, 23f / 255f, 0.94f);

        [SerializeField]
        private Color _surfaceRaised = new(22f / 255f, 28f / 255f, 37f / 255f);

        [SerializeField]
        private Color _hover = new(29f / 255f, 37f / 255f, 48f / 255f);

        [SerializeField]
        private Color _input = new(10f / 255f, 14f / 255f, 19f / 255f);

        [SerializeField]
        private Color _line = new(35f / 255f, 44f / 255f, 56f / 255f);

        [SerializeField]
        private Color _ink = new(230f / 255f, 237f / 255f, 243f / 255f);

        [SerializeField]
        private Color _inkBody = new(201f / 255f, 210f / 255f, 220f / 255f);

        [SerializeField]
        private Color _inkMuted = new(154f / 255f, 163f / 255f, 175f / 255f);

        [SerializeField]
        private Color _accent = new(92f / 255f, 214f / 255f, 143f / 255f);

        [SerializeField]
        private Color _accentSoft = new(184f / 255f, 242f / 255f, 207f / 255f);

        [SerializeField]
        private Color _onAccent = new(13f / 255f, 17f / 255f, 23f / 255f);

        [SerializeField]
        private Color _selection = new(18f / 255f, 48f / 255f, 31f / 255f);

        [SerializeField]
        private Color _selectionLine = new(52f / 255f, 126f / 255f, 82f / 255f);

        [SerializeField]
        private Color _warning = new(244f / 255f, 168f / 255f, 54f / 255f);

        [SerializeField]
        private Color _error = new(236f / 255f, 70f / 255f, 97f / 255f);

        [SerializeField]
        private Color _errorLine = new(180f / 255f, 68f / 255f, 90f / 255f);

        [SerializeField]
        private Color _errorText = new(255f / 255f, 122f / 255f, 142f / 255f);

        [SerializeField]
        private Color _synNumber = new(108f / 255f, 182f / 255f, 255f / 255f);

        [SerializeField]
        private Color _synString = new(250f / 255f, 192f / 255f, 98f / 255f);

        [SerializeField]
        private Color _synChoice = new(194f / 255f, 245f / 255f, 216f / 255f);

        internal TerminalThemeRoles() { }

        internal TerminalThemeRoles(
            Color surface,
            Color surfaceRaised,
            Color hover,
            Color input,
            Color ink,
            Color inkBody,
            Color inkMuted,
            Color accent,
            Color accentSoft,
            Color selection,
            Color warning,
            Color errorText
        )
        {
            _surface = surface;
            _surfaceRaised = surfaceRaised;
            _hover = hover;
            _input = input;
            _ink = ink;
            _inkBody = inkBody;
            _inkMuted = inkMuted;
            _accent = accent;
            _accentSoft = accentSoft;
            _selection = selection;
            _warning = warning;
            _errorText = errorText;
        }

        internal static int QuantizeChannel(float channel)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(channel) * 255f);
        }

        private static Color Quantize(Color color)
        {
            return new Color(QuantizeChannel(color.r) / 255f, QuantizeChannel(color.g) / 255f, QuantizeChannel(color.b) / 255f, (float)Math.Round(Mathf.Clamp01(color.a), 3));
        }

        internal void RepairContrast()
        {
            if (!ThemeContrastRepair.TryRepair(ReadColors(), out IReadOnlyDictionary<string, Color> colors))
            {
                return;
            }

            _ink = colors["ink"];
            _inkBody = colors["ink-body"];
            _inkMuted = colors["ink-muted"];
            _accent = colors["accent"];
            _accentSoft = colors["accent-soft"];
            _onAccent = colors["on-accent"];
            _selectionLine = colors["selection-line"];
            _warning = colors["warning"];
            _error = colors["error"];
            _errorLine = colors["error-line"];
            _errorText = colors["error-text"];
            _synNumber = colors["syn-number"];
            _synString = colors["syn-string"];
            _synChoice = colors["syn-choice"];
        }

        internal IReadOnlyDictionary<string, Color> ReadColors()
        {
            return new SortedDictionary<string, Color>(StringComparer.Ordinal)
            {
                ["surface"] = Quantize(_surface),
                ["surface-raised"] = Quantize(_surfaceRaised),
                ["hover"] = Quantize(_hover),
                ["input"] = Quantize(_input),
                ["line"] = Quantize(_line),
                ["ink"] = Quantize(_ink),
                ["ink-body"] = Quantize(_inkBody),
                ["ink-muted"] = Quantize(_inkMuted),
                ["accent"] = Quantize(_accent),
                ["accent-soft"] = Quantize(_accentSoft),
                ["on-accent"] = Quantize(_onAccent),
                ["selection"] = Quantize(_selection),
                ["selection-line"] = Quantize(_selectionLine),
                ["warning"] = Quantize(_warning),
                ["error"] = Quantize(_error),
                ["error-line"] = Quantize(_errorLine),
                ["error-text"] = Quantize(_errorText),
                ["syn-number"] = Quantize(_synNumber),
                ["syn-string"] = Quantize(_synString),
                ["syn-choice"] = Quantize(_synChoice),
            };
        }
    }
}
