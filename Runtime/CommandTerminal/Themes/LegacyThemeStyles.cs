namespace WallstopStudios.DxCommandTerminal.Themes
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal static class LegacyThemeStyles
    {
        private static readonly CustomStyleProperty<Color> SemanticSurface = new("--dxt-surface");
        private static readonly CustomStyleProperty<Color> LegacySurface = new("--terminal-bg");

        private static bool _reportedLegacyTheme;

        internal static void Apply(CustomStyleResolvedEvent evt)
        {
            VisualElement root = (VisualElement)evt.currentTarget;
            bool legacy = !evt.customStyle.TryGetValue(SemanticSurface, out _) && evt.customStyle.TryGetValue(LegacySurface, out _);
            root.EnableInClassList("dxt-legacy", legacy);
            if (legacy && !_reportedLegacyTheme)
            {
                _reportedLegacyTheme = true;
                Debug.LogWarning("A legacy terminal theme is in use. Convert its colors to semantic roles.");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _reportedLegacyTheme = false;
        }
    }
}
