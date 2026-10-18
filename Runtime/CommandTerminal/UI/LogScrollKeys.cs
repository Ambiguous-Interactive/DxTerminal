namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine.UIElements;

    /* Routes configured log shortcuts without taking unbound keys from text editing. */
    internal static class LogScrollKeys
    {
        /*
            Where a scroll value wants to land. The value is clamped to the
            scroller's own range on the way, so a page that runs past either
            end stops there rather than leaving the view past its content.
         */
        public static float Target(LogScrollIntent intent, float value, float highValue, float page)
        {
            return intent switch
            {
                LogScrollIntent.ToStart => 0f,
                LogScrollIntent.ToEnd => highValue,
                LogScrollIntent.PageUp => Clamp(value - page, highValue),
                LogScrollIntent.PageDown => Clamp(value + page, highValue),
                _ => Clamp(value, highValue),
            };
        }

        public static bool TryResolve(KeyDownEvent evt, ConsoleKeyBindings bindings, out LogScrollIntent intent)
        {
            if (ConsoleKeyBindings.Matches(evt, bindings.pageUp))
            {
                intent = LogScrollIntent.PageUp;
                return true;
            }
            if (ConsoleKeyBindings.Matches(evt, bindings.pageDown))
            {
                intent = LogScrollIntent.PageDown;
                return true;
            }
            if (ConsoleKeyBindings.Matches(evt, bindings.logStart))
            {
                intent = LogScrollIntent.ToStart;
                return true;
            }
            if (ConsoleKeyBindings.Matches(evt, bindings.logEnd))
            {
                intent = LogScrollIntent.ToEnd;
                return true;
            }
            intent = default;
            return false;
        }

        /*
            Clamped on both sides. The scroller clamps a write to the extent it
            holds, but a value computed from a viewport height taken before a
            layout pass can land outside the range, and the tail follower reads
            whatever the scroller ended up holding as the developer's position.
         */
        private static float Clamp(float value, float highValue)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return highValue < value ? highValue : value;
        }
    }
}
