namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Watches milestone C4 (#244): the watch/unwatch/watches commands,
        the Watches tab grid, the pinned HUD that survives a closed
        terminal, pin persistence across play sessions, and the zero-
        allocation idle HUD pin. Committed red first before the feature
        lands.
     */
    public sealed class TerminalWatchesTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;
        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static T LoadAsset<T>(string relativePath)
            where T : Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        [SetUp]
        public void SetUp()
        {
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
        }

        [TearDown]
        public void TearDown()
        {
            Terminal.Buffer = _originalBuffer;
            Terminal.Shell = _originalShell;
            Terminal.History = _originalHistory;
            Terminal.AutoComplete = _originalAutoComplete;

            if (_terminalObject != null)
            {
                Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                Object.Destroy(_panelSettings);
            }

            Terminal.Shell?.ClearCustomCommands();
        }

        [UnityTest]
        public IEnumerator WatchCommandsAreRegistered()
        {
            yield return SpawnTerminal(open: false);

            Assert.That(Terminal.Shell.Commands.ContainsKey("watch"), "The watch command pins a registered watch by name");
            Assert.That(Terminal.Shell.Commands.ContainsKey("unwatch"), "The unwatch command unpins a pinned watch");
            Assert.That(Terminal.Shell.Commands.ContainsKey("watches"), "The watches command lists the registered watches");
        }

        private IEnumerator SpawnTerminal(bool open)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.themeStyleSheet = LoadAsset<ThemeStyleSheet>("Styles/TerminalThemeSettings-Base.tss");
            _terminalObject = new GameObject("WatchesTerminal");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            _terminalObject.SetActive(true);
            yield return new WaitForSeconds(0.1f);
            if (open)
            {
                _terminal.SetState(TerminalState.OpenFull);
                yield return new WaitForSeconds(0.1f);
            }
#else
            Assert.Ignore("Watches UI coverage runs in the editor Play Mode suite.");
            yield break;
#endif
        }
    }
}
