namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public enum HintDisplayMode
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,
        Always = 1,

#if UNITY_EDITOR
        [UnityEngine.InspectorName("While typing")]
#endif
        AutoCompleteOnly = 2,

#if UNITY_EDITOR
        [UnityEngine.InspectorName("Off")]
#endif
        Never = 3,
    }
}
