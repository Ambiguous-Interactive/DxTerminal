namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

    /*
        Which key steps a search, and which key it is not.

        The command line owns panel focus for as long as the console is open,
        so F3 arrives at the command field rather than at the log view, and
        routing it is the terminal's job. This is that decision; pressing it is
        TerminalUILogFilterTests.

        One table for the whole rule, because the failures are the same class:
        a key the log answers that is not its own, and a key of its own that
        it leaves to the field.
     */
    public sealed class LogFindKeysTests
    {
        [TestCase(KeyCode.F3, false, true, false)]
        [TestCase(KeyCode.F3, true, false, true)]
        [TestCase(KeyCode.F4, false, false, false)]
        [TestCase(KeyCode.F4, true, false, false)]
        [TestCase(KeyCode.PageDown, false, false, false)]
        [TestCase(KeyCode.LeftArrow, false, false, false)]
        [TestCase(KeyCode.A, false, false, false)]
        [TestCase(KeyCode.Return, false, false, false)]
        [TestCase(KeyCode.Escape, false, false, false)]
        [TestCase(KeyCode.Tab, false, false, false)]
        public void OnlyF3StepsASearch(KeyCode keyCode, bool shiftKey, bool forward, bool backward)
        {
            using KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, shiftKey ? EventModifiers.Shift : EventModifiers.None);
            ConsoleKeyBindings bindings = new();
            Assert.That(LogFindKeys.IsStepForward(key, bindings), Is.EqualTo(forward));
            Assert.That(LogFindKeys.IsStepBackward(key, bindings), Is.EqualTo(backward));
        }
    }
}
