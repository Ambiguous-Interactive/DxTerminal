namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class ConsoleKeyBindingsTests
    {
        private static void AssertMatch(ConsoleKeyBindings.Shortcut[] bindings, KeyCode key, EventModifiers modifiers, bool expected)
        {
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                Assert.That(ConsoleKeyBindings.Matches(evt, bindings), Is.EqualTo(expected));
            }
        }

        private static ConsoleKeyBindings.Shortcut[] Bindings(ConsoleKeyBindings bindings, string action)
        {
            return action switch
            {
                nameof(ConsoleKeyBindings.copy) => bindings.copy,
                nameof(ConsoleKeyBindings.cut) => bindings.cut,
                nameof(ConsoleKeyBindings.paste) => bindings.paste,
                nameof(ConsoleKeyBindings.undo) => bindings.undo,
                nameof(ConsoleKeyBindings.redo) => bindings.redo,
                nameof(ConsoleKeyBindings.pageUp) => bindings.pageUp,
                nameof(ConsoleKeyBindings.pageDown) => bindings.pageDown,
                nameof(ConsoleKeyBindings.logStart) => bindings.logStart,
                nameof(ConsoleKeyBindings.logEnd) => bindings.logEnd,
                nameof(ConsoleKeyBindings.findNext) => bindings.findNext,
                nameof(ConsoleKeyBindings.findPrevious) => bindings.findPrevious,
                nameof(ConsoleKeyBindings.paletteNext) => bindings.paletteNext,
                nameof(ConsoleKeyBindings.palettePrevious) => bindings.palettePrevious,
                nameof(ConsoleKeyBindings.paletteSubmit) => bindings.paletteSubmit,
                nameof(ConsoleKeyBindings.paletteClose) => bindings.paletteClose,
                nameof(ConsoleKeyBindings.paletteComplete) => bindings.paletteComplete,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
        }

        [TestCase(nameof(ConsoleKeyBindings.copy), KeyCode.C, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.copy), KeyCode.C, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.cut), KeyCode.X, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.cut), KeyCode.X, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.paste), KeyCode.V, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.paste), KeyCode.V, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.undo), KeyCode.Z, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.undo), KeyCode.Z, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.redo), KeyCode.Z, EventModifiers.Control | EventModifiers.Shift)]
        [TestCase(nameof(ConsoleKeyBindings.redo), KeyCode.Z, EventModifiers.Command | EventModifiers.Shift)]
        [TestCase(nameof(ConsoleKeyBindings.pageUp), KeyCode.PageUp, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.pageDown), KeyCode.PageDown, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.logStart), KeyCode.Home, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.logStart), KeyCode.Home, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.logEnd), KeyCode.End, EventModifiers.Control)]
        [TestCase(nameof(ConsoleKeyBindings.logEnd), KeyCode.End, EventModifiers.Command)]
        [TestCase(nameof(ConsoleKeyBindings.findNext), KeyCode.F3, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.findPrevious), KeyCode.F3, EventModifiers.Shift)]
        [TestCase(nameof(ConsoleKeyBindings.paletteNext), KeyCode.DownArrow, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.palettePrevious), KeyCode.UpArrow, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.paletteSubmit), KeyCode.Return, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.paletteSubmit), KeyCode.KeypadEnter, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.paletteClose), KeyCode.Escape, EventModifiers.None)]
        [TestCase(nameof(ConsoleKeyBindings.paletteComplete), KeyCode.Tab, EventModifiers.None)]
        public void DefaultsAcceptExpectedShortcuts(string action, KeyCode key, EventModifiers modifiers)
        {
            ConsoleKeyBindings defaults = new();
            using (KeyDownEvent evt = KeyDownEvent.GetPooled('\0', key, modifiers))
            {
                Assert.That(ConsoleKeyBindings.Matches(evt, Bindings(defaults, action)), Is.True);
            }
        }

        [Test]
        public void RemappingReplacesTheDefaultShortcut()
        {
            ConsoleKeyBindings bindings = new() { copy = new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Shift) } };
            AssertMatch(bindings.copy, KeyCode.F8, EventModifiers.Shift, true);
            AssertMatch(bindings.copy, KeyCode.C, EventModifiers.Control, false);
            AssertMatch(bindings.copy, KeyCode.C, EventModifiers.Command, false);
        }

        [Test]
        public void MultipleShortcutsCanInvokeTheSameAction()
        {
            ConsoleKeyBindings.Shortcut[] bindings = { new(KeyCode.F8), new(KeyCode.F9, EventModifiers.Shift) };
            AssertMatch(bindings, KeyCode.F8, EventModifiers.None, true);
            AssertMatch(bindings, KeyCode.F9, EventModifiers.Shift, true);
            AssertMatch(bindings, KeyCode.F10, EventModifiers.None, false);
        }

        [Test]
        public void NullEmptyAndNoneBindingsDisableTheAction()
        {
            AssertMatch(null, KeyCode.C, EventModifiers.Control, false);
            AssertMatch(Array.Empty<ConsoleKeyBindings.Shortcut>(), KeyCode.C, EventModifiers.Control, false);
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.None) }, KeyCode.None, EventModifiers.None, false);
            AssertMatch(new[] { default(ConsoleKeyBindings.Shortcut) }, KeyCode.C, EventModifiers.Control, false);
            Assert.That(ConsoleKeyBindings.Matches(null, new ConsoleKeyBindings().copy), Is.False);
        }

        [TestCase(EventModifiers.None, false)]
        [TestCase(EventModifiers.Shift, true)]
        [TestCase(EventModifiers.Alt, false)]
        [TestCase(EventModifiers.Shift | EventModifiers.Alt, false)]
        [TestCase(EventModifiers.Shift | EventModifiers.Control, false)]
        [TestCase(EventModifiers.Shift | EventModifiers.Command, false)]
        public void ExtraOrMissingModifiersDoNotMatch(EventModifiers modifiers, bool expected)
        {
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Shift) }, KeyCode.F8, modifiers, expected);
        }

        [TestCase(EventModifiers.Alt, true)]
        [TestCase(EventModifiers.None, false)]
        [TestCase(EventModifiers.Alt | EventModifiers.Shift, false)]
        public void AltCanBeRequired(EventModifiers modifiers, bool expected)
        {
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Alt) }, KeyCode.F8, modifiers, expected);
        }

        [TestCase(EventModifiers.Control, true)]
        [TestCase(EventModifiers.Command, false)]
        [TestCase(EventModifiers.Control | EventModifiers.Command, false)]
        [TestCase(EventModifiers.None, false)]
        public void ExactControlDoesNotAcceptCommand(EventModifiers modifiers, bool expected)
        {
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Control) }, KeyCode.F8, modifiers, expected);
        }

        [TestCase(EventModifiers.Control | EventModifiers.Shift, true)]
        [TestCase(EventModifiers.Command | EventModifiers.Shift, true)]
        [TestCase(EventModifiers.Shift, false)]
        [TestCase(EventModifiers.Control, false)]
        [TestCase(EventModifiers.Command | EventModifiers.Shift | EventModifiers.Alt, false)]
        public void ControlOrCommandStillRequiresExactShiftAndAlt(EventModifiers modifiers, bool expected)
        {
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Shift, controlOrCommand: true) }, KeyCode.F8, modifiers, expected);
        }

        [TestCase(EventModifiers.Command, true)]
        [TestCase(EventModifiers.Control, false)]
        [TestCase(EventModifiers.Control | EventModifiers.Command, false)]
        [TestCase(EventModifiers.None, false)]
        public void ExactCommandDoesNotAcceptControl(EventModifiers modifiers, bool expected)
        {
            AssertMatch(new[] { new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Command) }, KeyCode.F8, modifiers, expected);
        }

        [Test]
        public void EditingOneSurfaceBindingsDoesNotChangeAnother()
        {
            ConsoleKeyBindings first = new();
            ConsoleKeyBindings second = new();
            first.copy[0] = new ConsoleKeyBindings.Shortcut(KeyCode.F8);
            AssertMatch(first.copy, KeyCode.F8, EventModifiers.None, true);
            AssertMatch(second.copy, KeyCode.C, EventModifiers.Control, true);
            AssertMatch(second.copy, KeyCode.F8, EventModifiers.None, false);
        }
    }
}
