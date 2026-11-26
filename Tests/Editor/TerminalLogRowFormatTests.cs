namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using Backend;
    using NUnit.Framework;
    using UI;

    /*
        The text the C1 log rows and severity chips render (issue #241):
        timestamps with milliseconds, one glyph per log type, and the
        "N Type" chip label. These are pure text rules, so the
        engine-independent fixture pins them without a visual tree; the rows
        and chips that draw them are TerminalUIChromeTests.
     */
    public sealed class TerminalLogRowFormatTests
    {
        [Test]
        public void FormatTimestampShowsMillisecondsWithoutTimezoneMarker()
        {
            DateTime utc = new(2026, 11, 26, 12, 4, 31, 204, DateTimeKind.Utc);
            Assert.AreEqual("12:04:31.204", TerminalLogRowFormat.FormatTimestamp(utc));
        }

        [Test]
        public void FormatTimestampPadsSingleDigitFields()
        {
            DateTime utc = new(2026, 1, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            Assert.AreEqual("03:04:05.006", TerminalLogRowFormat.FormatTimestamp(utc));
        }

        [TestCase(TerminalLogType.Input, "›")]
        [TestCase(TerminalLogType.Message, "·")]
        [TestCase(TerminalLogType.ShellMessage, "·")]
        [TestCase(TerminalLogType.Warning, "▲")]
        [TestCase(TerminalLogType.Error, "■")]
        [TestCase(TerminalLogType.Assert, "■")]
        [TestCase(TerminalLogType.Exception, "■")]
        public void EveryLogTypeHasAGlyph(TerminalLogType type, string expected)
        {
            Assert.AreEqual(expected, TerminalLogRowFormat.GlyphOf(type));
        }

        [Test]
        public void UnknownLogTypeFallsBackToTheMessageGlyph()
        {
            Assert.AreEqual("·", TerminalLogRowFormat.GlyphOf((TerminalLogType)99));
        }

        [TestCase(TerminalLogType.Message, 199, "199 Messages")]
        [TestCase(TerminalLogType.Warning, 12, "12 Warnings")]
        [TestCase(TerminalLogType.Error, 3, "3 Errors")]
        [TestCase(TerminalLogType.Error, 0, "0 Errors")]
        public void SeverityChipsCountBeforeTheTypeWord(TerminalLogType severity, int count, string expected)
        {
            Assert.AreEqual(expected, TerminalLogRowFormat.ChipText(severity, count));
        }
    }
}
