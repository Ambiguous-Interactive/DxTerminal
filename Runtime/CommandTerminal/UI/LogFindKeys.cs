namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine.UIElements;

    internal static class LogFindKeys
    {
        public static bool IsStepForward(KeyDownEvent evt, ConsoleKeyBindings bindings)
        {
            return ConsoleKeyBindings.Matches(evt, bindings.findNext);
        }

        public static bool IsStepBackward(KeyDownEvent evt, ConsoleKeyBindings bindings)
        {
            return ConsoleKeyBindings.Matches(evt, bindings.findPrevious);
        }
    }
}
