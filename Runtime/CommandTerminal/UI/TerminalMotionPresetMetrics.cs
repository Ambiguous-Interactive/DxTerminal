namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    internal readonly struct TerminalMotionPresetMetrics
    {
        internal const string NoneClass = "terminal-motion--none";
        internal const string SubtleClass = "terminal-motion--subtle";
        internal const string SnappyClass = "terminal-motion--snappy";
        internal const float DefaultLegacyDurationSeconds = 0.5f;

        private const float LegacyNoneThresholdSeconds = 0.0275f;
        private const float LegacySnappyThresholdSeconds = 0.0825f;

        private static readonly TerminalMotionPresetMetrics NoneMetrics = new(
            TerminalMotionPreset.Off,
            NoneClass,
            openDurationSeconds: 0f,
            closeDurationSeconds: 0f,
            cssTransitionDurationSeconds: 0f
        );

        private static readonly TerminalMotionPresetMetrics SubtleMetrics = new(
            TerminalMotionPreset.Subtle,
            SubtleClass,
            openDurationSeconds: 0.12f,
            closeDurationSeconds: 0.10f,
            cssTransitionDurationSeconds: 0.10f
        );

        private static readonly TerminalMotionPresetMetrics SnappyMetrics = new(
            TerminalMotionPreset.Snappy,
            SnappyClass,
            openDurationSeconds: 0.06f,
            closeDurationSeconds: 0.05f,
            cssTransitionDurationSeconds: 0.05f
        );

        internal TerminalMotionPreset Preset { get; }

        internal string CssClass { get; }

        internal float OpenDurationSeconds { get; }

        internal float CloseDurationSeconds { get; }

        internal float CssTransitionDurationSeconds { get; }

        private TerminalMotionPresetMetrics(TerminalMotionPreset preset, string cssClass, float openDurationSeconds, float closeDurationSeconds, float cssTransitionDurationSeconds)
        {
            Preset = preset;
            CssClass = cssClass;
            OpenDurationSeconds = openDurationSeconds;
            CloseDurationSeconds = closeDurationSeconds;
            CssTransitionDurationSeconds = cssTransitionDurationSeconds;
        }

        internal static TerminalMotionPreset Normalize(TerminalMotionPreset preset)
        {
            return preset switch
            {
                TerminalMotionPreset.Off => TerminalMotionPreset.Off,
                TerminalMotionPreset.Subtle => TerminalMotionPreset.Subtle,
                TerminalMotionPreset.Snappy => TerminalMotionPreset.Snappy,
                _ => TerminalMotionPreset.Subtle,
            };
        }

        internal static TerminalMotionPresetMetrics For(TerminalMotionPreset preset)
        {
            return Normalize(preset) switch
            {
                TerminalMotionPreset.Off => NoneMetrics,
                TerminalMotionPreset.Snappy => SnappyMetrics,
                _ => SubtleMetrics,
            };
        }

        internal static int ToOptionIndex(TerminalMotionPreset preset)
        {
            return Normalize(preset) switch
            {
                TerminalMotionPreset.Off => 0,
                TerminalMotionPreset.Snappy => 2,
                _ => 1,
            };
        }

        internal static TerminalMotionPreset FromOptionIndex(int index)
        {
            return index switch
            {
                0 => TerminalMotionPreset.Off,
                2 => TerminalMotionPreset.Snappy,
                _ => TerminalMotionPreset.Subtle,
            };
        }

        internal static TerminalMotionPreset MigrateLegacyTiming(float easeOutSeconds, float easeInSeconds)
        {
            float cleanOut = SanitizeDuration(easeOutSeconds);
            float cleanIn = SanitizeDuration(easeInSeconds);
            if (cleanOut <= 0f && cleanIn <= 0f)
            {
                return TerminalMotionPreset.Off;
            }

            float average = 0.5f * (cleanOut + cleanIn);
            if (average <= LegacyNoneThresholdSeconds)
            {
                return TerminalMotionPreset.Off;
            }

            if (average <= LegacySnappyThresholdSeconds)
            {
                return TerminalMotionPreset.Snappy;
            }

            return TerminalMotionPreset.Subtle;
        }

        private static float SanitizeDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
            {
                return 0f;
            }

            return seconds;
        }

        internal float EvaluateEaseOut(float normalizedTime)
        {
            if (Preset == TerminalMotionPreset.Off)
            {
                return 1f;
            }

            float clamped = Math.Min(1f, Math.Max(0f, normalizedTime));
            float inverse = 1f - clamped;
            if (Preset == TerminalMotionPreset.Snappy)
            {
                float sq = inverse * inverse;
                return 1f - (sq * sq);
            }

            return 1f - (inverse * inverse * inverse);
        }
    }
}
