namespace WallstopStudios.DxCommandTerminal.Themes
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal static class LegacyThemeStyles
    {
        private const string LegacyClass = "dxt-legacy";

        private static readonly CustomStyleProperty<Color> SemanticSurface = new("--dxt-surface");
        private static readonly CustomStyleProperty<Color>[] LegacyColors =
        {
            new("--terminal-bg"),
            new("--button-bg"),
            new("--input-field-bg"),
            new("--button-selected-bg"),
            new("--button-hover-bg"),
            new("--scroll-bg"),
            new("--scroll-inverse-bg"),
            new("--scroll-active-bg"),
            new("--button-text"),
            new("--button-selected-text"),
            new("--button-hover-text"),
            new("--input-text-color"),
            new("--text-message"),
            new("--text-warning"),
            new("--text-input-echo"),
            new("--text-shell"),
            new("--text-error"),
            new("--scroll-color"),
            new("--caret-color"),
        };

        private static bool _reportedLegacyTheme;

        internal static void Register(VisualElement root)
        {
            root.AddToClassList(LegacyClass);
            root.RegisterCallback<CustomStyleResolvedEvent>(Apply);
        }

        private static void Apply(CustomStyleResolvedEvent evt)
        {
            VisualElement root = (VisualElement)evt.currentTarget;
            ICustomStyle styles = evt.customStyle;
            bool semantic = styles.TryGetValue(SemanticSurface, out _);
            root.EnableInClassList(LegacyClass, !semantic);
            if (semantic || _reportedLegacyTheme)
            {
                return;
            }

            foreach (CustomStyleProperty<Color> color in LegacyColors)
            {
                if (styles.TryGetValue(color, out _))
                {
                    _reportedLegacyTheme = true;
                    Debug.LogWarning("A legacy terminal theme is in use. Convert its colors to semantic roles.");
                    return;
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _reportedLegacyTheme = false;
        }
    }
}
