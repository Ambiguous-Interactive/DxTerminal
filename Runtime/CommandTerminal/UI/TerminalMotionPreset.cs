namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    public enum TerminalMotionPreset
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,

#if UNITY_EDITOR
        [UnityEngine.InspectorName("None")]
#endif
        Off = 1,
        Subtle = 2,
        Snappy = 3,
    }
}
