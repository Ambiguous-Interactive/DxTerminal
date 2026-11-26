namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Backend;
    using NUnit.Framework;
    using UI;
#if UNITY_EDITOR
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#endif

    public sealed class CommandPaletteQuickLaunchTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal/";
        private const string BaseStylesPath = "Styles/BaseStyles.uss";

        private static string LoadBaseStyles()
        {
#if UNITY_EDITOR
            PackageInfo package = PackageInfo.FindForAssetPath(PackageRoot + BaseStylesPath);
            return File.ReadAllText(Path.Combine(package.resolvedPath, BaseStylesPath));
#else
            DirectoryInfo directory = new(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, BaseStylesPath);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }

                directory = directory.Parent;
            }

            Assert.Fail($"Could not locate {BaseStylesPath} from {TestContext.CurrentContext.TestDirectory}");
            return string.Empty;
#endif
        }

        [Test]
        public void DefaultPaletteRowHeightFallbackIsFortyPixels()
        {
            Assert.That(TerminalTextSizeMetrics.For(TerminalTextSize.Small).EffectivePaletteRowHeight(float.NaN), Is.EqualTo(40f));
        }

        [Test]
        public void BaseStylesDefineS4QuickLaunchGeometryAndDefectFixes()
        {
            string uss = LoadBaseStyles();
            Assert.That(uss, Does.Contain(".palette-input-container"), "Input container hosts the prompt and 44 px input");
            Assert.That(uss, Does.Contain(".palette-prompt"), "Prompt glyph sits inside the input container");
            Assert.That(uss, Does.Contain("--unity-selection-color: var(--dxt-selection);"), "Selected input text uses ink on selection");
            Assert.That(uss, Does.Contain(".palette-feedback--error"), "Failed run uses one styled error block");
            Assert.That(uss, Does.Contain("border-color: var(--dxt-error-line);"), "Failed run error block uses the error-line border token");
            Assert.That(uss, Does.Contain(".palette-root--narrow"), "Narrow/touch viewports expand Quick launch to full width");
            Assert.That(uss, Does.Contain("text-overflow: ellipsis;"), "Help column truncates long descriptions with an ellipsis");
        }

        [Test]
        public void CollectRecentReturnsLastThreeUniqueLinesWithArgumentsNewestFirst()
        {
            CommandHistory history = new(16);
            history.Push("heal 10", success: true, errorFree: true);
            history.Push("spawn goblin 3", success: true, errorFree: true);
            history.Push("teleport 1 2 3", success: true, errorFree: true);
            history.Push("spawn goblin 3", success: true, errorFree: true);
            history.Push("give sword 2", success: true, errorFree: true);

            List<string> scratch = new();
            List<string> recent = new();
            CommandPaletteSearch.CollectRecent(history, scratch, recent);

            Assert.That(recent, Is.EqualTo(new[] { "give sword 2", "spawn goblin 3", "teleport 1 2 3" }));
        }

        [Test]
        public void FooterHintsFollowStateWithoutUnusableKeyHints()
        {
            string empty = CommandPaletteSearch.FormatFooterHint(rowCount: 0, hasGhost: false, failedRun: false);
            Assert.That(empty, Does.Not.Contain("↑↓"));
            Assert.That(empty, Does.Not.Contain("Tab"));

            string single = CommandPaletteSearch.FormatFooterHint(rowCount: 1, hasGhost: false, failedRun: false);
            Assert.That(single, Does.Not.Contain("↑↓"));
            Assert.That(single, Does.Contain("Tab"));

            string multi = CommandPaletteSearch.FormatFooterHint(rowCount: 3, hasGhost: true, failedRun: false);
            Assert.That(multi, Does.Contain("↑↓"));
            Assert.That(multi, Does.Contain("Tab"));
            Assert.That(multi, Does.Contain("→"));

            string failed = CommandPaletteSearch.FormatFooterHint(rowCount: 2, hasGhost: true, failedRun: true);
            Assert.That(failed, Is.EqualTo("Edit the line to retry"));
        }
    }
}
