namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using NUnit.Framework;
    using UI;

    public sealed class TerminalTextSizeTests
    {
        [Test]
        public void TextSizeSettingExistsWithSmallMediumAndLargeOptions()
        {
            Type textSizeType = typeof(TerminalAnchor).Assembly.GetType("WallstopStudios.DxCommandTerminal.UI.TerminalTextSize");
            Assert.That(textSizeType, Is.Not.Null, "A4 (#332) requires a TerminalTextSize setting with S 13, M 15, and L 18 options.");
        }
    }
}
