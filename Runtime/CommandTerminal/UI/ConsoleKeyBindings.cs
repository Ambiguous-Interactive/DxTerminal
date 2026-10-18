namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;

    [Serializable]
    public sealed class ConsoleKeyBindings
    {
        internal static readonly ConsoleKeyBindings Default = new();

        public Shortcut[] copy = { new(KeyCode.C, controlOrCommand: true) };
        public Shortcut[] cut = { new(KeyCode.X, controlOrCommand: true) };
        public Shortcut[] paste = { new(KeyCode.V, controlOrCommand: true) };
        public Shortcut[] undo = { new(KeyCode.Z, controlOrCommand: true) };
        public Shortcut[] redo = { new(KeyCode.Z, EventModifiers.Shift, controlOrCommand: true) };
        public Shortcut[] pageUp = { new(KeyCode.PageUp) };
        public Shortcut[] pageDown = { new(KeyCode.PageDown) };
        public Shortcut[] logStart = { new(KeyCode.Home, controlOrCommand: true) };
        public Shortcut[] logEnd = { new(KeyCode.End, controlOrCommand: true) };
        public Shortcut[] findNext = { new(KeyCode.F3) };
        public Shortcut[] findPrevious = { new(KeyCode.F3, EventModifiers.Shift) };
        public Shortcut[] paletteNext = { new(KeyCode.DownArrow) };
        public Shortcut[] palettePrevious = { new(KeyCode.UpArrow) };
        public Shortcut[] paletteSubmit = { new(KeyCode.Return), new(KeyCode.KeypadEnter) };
        public Shortcut[] paletteClose = { new(KeyCode.Escape) };
        public Shortcut[] paletteComplete = { new(KeyCode.Tab) };

        internal static bool Matches(KeyDownEvent evt, Shortcut[] bindings)
        {
            if (evt == null || bindings == null)
            {
                return false;
            }

            foreach (Shortcut binding in bindings)
            {
                if (
                    binding.key == KeyCode.None
                    || binding.key != evt.keyCode
                    || HasFlagNoAlloc(binding.modifiers, EventModifiers.Shift) != evt.shiftKey
                    || HasFlagNoAlloc(binding.modifiers, EventModifiers.Alt) != evt.altKey
                )
                {
                    continue;
                }

                bool control = HasFlagNoAlloc(binding.modifiers, EventModifiers.Control);
                bool command = HasFlagNoAlloc(binding.modifiers, EventModifiers.Command);
                if (binding.controlOrCommand)
                {
                    if ((evt.ctrlKey || evt.commandKey) && (!control || evt.ctrlKey) && (!command || evt.commandKey))
                    {
                        return true;
                    }
                }
                else if (control == evt.ctrlKey && command == evt.commandKey)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasFlagNoAlloc(EventModifiers value, EventModifiers flag)
        {
            return (value & flag) == flag;
        }

        [Serializable]
        public struct Shortcut : IEquatable<Shortcut>
        {
            public KeyCode key;
            public EventModifiers modifiers;

            [Tooltip("Accept either Control or Command in addition to the listed modifiers")]
            public bool controlOrCommand;

            public Shortcut(KeyCode key, EventModifiers modifiers = EventModifiers.None, bool controlOrCommand = false)
            {
                this.key = key;
                this.modifiers = modifiers;
                this.controlOrCommand = controlOrCommand;
            }

            public static bool operator ==(Shortcut left, Shortcut right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(Shortcut left, Shortcut right)
            {
                return !left.Equals(right);
            }

            public bool Equals(Shortcut other)
            {
                return key == other.key && modifiers == other.modifiers && controlOrCommand == other.controlOrCommand;
            }

            public override bool Equals(object obj)
            {
                return obj is Shortcut other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(key, modifiers, controlOrCommand);
            }
        }
    }
}
