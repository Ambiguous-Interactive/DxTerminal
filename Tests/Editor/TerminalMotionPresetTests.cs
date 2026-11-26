namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UI;

    public sealed class TerminalMotionPresetTests
    {
        private static string FindPackageRoot()
        {
            string current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "package.json")) && Directory.Exists(Path.Combine(current, "Styles")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new DirectoryNotFoundException("Could not locate package root containing Styles/BaseStyles.uss.");
        }

        [Test]
        public void MotionPresetTypeAndNoneTransitionOverrideExist()
        {
            Type presetType = typeof(TerminalTextSize).Assembly.GetType("WallstopStudios.DxCommandTerminal.UI.TerminalMotionPreset");
            Assert.That(presetType != null, Is.True, "TerminalMotionPreset enum must exist with None, Subtle, and Snappy.");

            string ussPath = Path.Combine(FindPackageRoot(), "Styles", "BaseStyles.uss");
            string uss = File.ReadAllText(ussPath);
            Assert.That(
                uss.Contains(".terminal-motion--none", StringComparison.Ordinal),
                Is.True,
                "BaseStyles.uss must define .terminal-motion--none to resolve USS transition-duration to 0s."
            );
        }
    }
}
