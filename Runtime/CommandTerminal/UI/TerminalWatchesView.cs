namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using UnityEngine.UIElements;
    using WatchEntry = Backend.CommandWatchRegistry.WatchEntry;
    using WatchValueRole = Backend.CommandWatchRegistry.WatchValueRole;

    /*
        UI Toolkit view for the Watches milestone (#244, board S3): the tab
        grid (name, value, source, HUD pin toggle) and the corner HUD panel
        that keeps pinned values on screen while the terminal is closed.
        Rows rebuild only when the registry's structural version changes;
        value cells compare-and-write, so idle frames allocate nothing.
     */
    internal sealed class TerminalWatchesView
    {
        private const string ValueClassPrefix = "terminal-watches-value--";

        internal VisualElement Root
        {
            get { return _root; }
        }

        internal VisualElement HudRoot
        {
            get { return _hudRoot; }
        }

        private readonly VisualElement _root;
        private readonly VisualElement _hudRoot;
        private readonly VisualElement _hudRows;
        private readonly ScrollView _rowsScroll;
        private readonly Label _emptyLabel;
        private readonly Action<string, bool> _onPinToggled;
        private readonly List<WatchEntry> _entries = new();
        private readonly List<WatchEntry> _hudEntries = new();
        private readonly List<WatchRowBinding> _rowBindings = new();
        private readonly List<HudRowBinding> _hudBindings = new();

        private CommandWatchRegistry _lastRegistry;
        private long _lastVersion = -1;
        private CommandTier _lastTier;
        private bool _lastHudEnabled;
        private int _lastHudMaxRows;
        private TerminalHudCorner _lastHudCorner = TerminalHudCorner.BottomRight;
        private bool _visible;

        internal TerminalWatchesView(VisualElement tabParent, VisualElement hudParent, Action<string, bool> onPinToggled)
        {
            _onPinToggled = onPinToggled;

            _root = new VisualElement { name = "TerminalWatchesView" };
            _root.AddToClassList("terminal-watches");
            _root.style.display = DisplayStyle.None;

            VisualElement header = new() { name = "TerminalWatchesGridHeader" };
            header.AddToClassList("terminal-watches-header");
            header.Add(CreateHeaderLabel("TerminalWatchesHeaderName", "Name"));
            header.Add(CreateHeaderLabel("TerminalWatchesHeaderValue", "Value"));
            header.Add(CreateHeaderLabel("TerminalWatchesHeaderSource", "Source"));
            header.Add(CreateHeaderLabel("TerminalWatchesHeaderHud", "HUD"));
            _root.Add(header);

            _rowsScroll = new ScrollView { name = "TerminalWatchesScroll" };
            _rowsScroll.AddToClassList("terminal-watches-scroll");
            _rowsScroll.focusable = false;
            _root.Add(_rowsScroll);

            _emptyLabel = new Label("No watches visible at this tier.") { name = "TerminalWatchesEmpty", pickingMode = PickingMode.Ignore };
            _emptyLabel.AddToClassList("terminal-watches-empty");
            _emptyLabel.style.display = DisplayStyle.None;
            _root.Add(_emptyLabel);

            tabParent.Add(_root);

            /*
                The HUD mounts in its own document root so the terminal's
                height animation cannot move or clip it while closed.
             */
            _hudRoot = new VisualElement { name = "TerminalWatchesHud", pickingMode = PickingMode.Position };
            _hudRoot.AddToClassList("terminal-watches-hud");
            _hudRoot.style.display = DisplayStyle.None;

            Label hudHeader = new("Watches") { name = "TerminalWatchesHudHeader", pickingMode = PickingMode.Ignore };
            hudHeader.AddToClassList("terminal-watches-hud-header");
            _hudRoot.Add(hudHeader);

            _hudRows = new VisualElement { name = "TerminalWatchesHudRows" };
            _hudRows.AddToClassList("terminal-watches-hud-rows");
            _hudRoot.Add(_hudRows);

            if (hudParent != null)
            {
                hudParent.Add(_hudRoot);
            }
        }

        private static Label CreateHeaderLabel(string elementName, string text)
        {
            Label label = new(text) { name = elementName, pickingMode = PickingMode.Ignore };
            label.AddToClassList("terminal-watches-header-label");
            return label;
        }

        internal void SetVisible(bool visible)
        {
            _visible = visible;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        ///     Rebuilds the tab rows and the HUD row set when the registry's
        ///     version, tier, or HUD settings changed since the last pass;
        ///     otherwise this is one version compare.
        /// </summary>
        internal void RefreshIfNeeded(CommandWatchRegistry watches, CommandTier tier, bool hudEnabled, int hudMaxRows, TerminalHudCorner hudCorner)
        {
            if (watches == null)
            {
                return;
            }

            bool hudSettingsChanged = _lastHudEnabled != hudEnabled || _lastHudMaxRows != hudMaxRows || _lastHudCorner != hudCorner;
            bool registryChanged = !ReferenceEquals(_lastRegistry, watches) || _lastVersion != watches.Version || _lastTier != tier;
            if (!registryChanged && !hudSettingsChanged)
            {
                return;
            }

            _lastRegistry = watches;
            _lastVersion = watches.Version;
            _lastTier = tier;
            _lastHudEnabled = hudEnabled;
            _lastHudMaxRows = hudMaxRows;
            _lastHudCorner = hudCorner;

            watches.CollectVisibleEntries(tier, _entries);
            TerminalWatchesModel.BuildHudRows(_entries, hudMaxRows, _hudEntries);

            if (registryChanged)
            {
                RebuildRows();
            }

            ApplyHudLayout(hudCorner, hudEnabled && _hudEntries.Count != 0);
            RebuildHudRows();
        }

        /// <summary>
        ///     Compares and writes every visible value cell (tab and HUD).
        ///     Unchanged values write nothing and allocate nothing, so the
        ///     4 Hz sampler can run against an idle HUD for free.
        /// </summary>
        internal void RefreshValues()
        {
            int bindingCount = _rowBindings.Count;
            for (int index = 0; index < bindingCount; ++index)
            {
                _rowBindings[index].Refresh();
            }

            int hudBindingCount = _hudBindings.Count;
            for (int index = 0; index < hudBindingCount; ++index)
            {
                _hudBindings[index].Refresh();
            }
        }

        private void RebuildRows()
        {
            _rowsScroll.Clear();
            _rowBindings.Clear();

            int entryCount = _entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                WatchEntry entry = _entries[index];
                VisualElement row = new() { name = "TerminalWatchesRow" };
                row.AddToClassList("terminal-watches-row");

                Label name = new(entry.Name) { name = "TerminalWatchesName", pickingMode = PickingMode.Ignore };
                name.AddToClassList("terminal-watches-name");
                row.Add(name);

                Label value = new() { name = "TerminalWatchesValue", pickingMode = PickingMode.Ignore };
                value.AddToClassList("terminal-watches-value");
                row.Add(value);

                Label source = new(entry.Source) { name = "TerminalWatchesSource", pickingMode = PickingMode.Ignore };
                source.AddToClassList("terminal-watches-source");
                row.Add(source);

                Button pin = new(() =>
                {
                    _onPinToggled?.Invoke(entry.Name, !entry.Pinned);
                })
                {
                    name = "TerminalWatchesPin",
                    text = entry.Pinned ? TerminalWatchesModel.PinnedLabel : TerminalWatchesModel.PinLabel,
                };
                pin.AddToClassList("terminal-watches-pin");
                pin.EnableInClassList("terminal-watches-pin--pinned", entry.Pinned);
                row.Add(pin);

                _rowsScroll.Add(row);
                _rowBindings.Add(new WatchRowBinding(entry, value));
            }

            _emptyLabel.style.display = entryCount == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshValues();
        }

        private void RebuildHudRows()
        {
            _hudRows.Clear();
            _hudBindings.Clear();

            int hudCount = _hudEntries.Count;
            for (int index = 0; index < hudCount; ++index)
            {
                WatchEntry entry = _hudEntries[index];
                VisualElement row = new() { name = "TerminalWatchesHudRow" };
                row.AddToClassList("terminal-watches-hud-row");

                Label name = new(entry.Name) { name = "TerminalWatchesHudName", pickingMode = PickingMode.Ignore };
                name.AddToClassList("terminal-watches-hud-name");
                row.Add(name);

                Label value = new() { name = "TerminalWatchesHudValue", pickingMode = PickingMode.Ignore };
                value.AddToClassList("terminal-watches-hud-value");
                row.Add(value);

                _hudRows.Add(row);
                _hudBindings.Add(new HudRowBinding(entry, value));
            }

            RefreshValues();
        }

        private void ApplyHudLayout(TerminalHudCorner corner, bool show)
        {
            _hudRoot.EnableInClassList("terminal-watches-hud--bottom-right", corner == TerminalHudCorner.BottomRight);
            _hudRoot.EnableInClassList("terminal-watches-hud--bottom-left", corner == TerminalHudCorner.BottomLeft);
            _hudRoot.EnableInClassList("terminal-watches-hud--top-right", corner == TerminalHudCorner.TopRight);
            _hudRoot.EnableInClassList("terminal-watches-hud--top-left", corner == TerminalHudCorner.TopLeft);
            _hudRoot.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /*
            One binding per rendered row. Refresh compares the entry's live
            value text and role against what the labels carry, so a steady
            value rewrites nothing (the zero-allocation idle HUD pin) and a
            changed value is the only text write. Pin state never syncs
            here: every pin change bumps the registry version, which
            rebuilds the rows.
         */
        private sealed class WatchRowBinding
        {
            private readonly WatchEntry _entry;
            private readonly Label _value;
            private WatchValueRole _role = WatchValueRole.Body;

            internal WatchRowBinding(WatchEntry entry, Label value)
            {
                _entry = entry;
                _value = value;
            }

            internal void Refresh()
            {
                string expected = _entry.HasValue ? _entry.ValueText : TerminalWatchesModel.UnsampledValueText;
                if (!string.Equals(_value.text, expected, StringComparison.Ordinal))
                {
                    _value.text = expected;
                }

                if (_role != _entry.Role)
                {
                    _value.EnableInClassList($"{ValueClassPrefix}{TerminalWatchesModel.ValueClassName(_role)}", false);
                    _role = _entry.Role;
                    _value.EnableInClassList($"{ValueClassPrefix}{TerminalWatchesModel.ValueClassName(_role)}", true);
                }
            }
        }

        private sealed class HudRowBinding
        {
            private readonly WatchEntry _entry;
            private readonly Label _value;
            private WatchValueRole _role = WatchValueRole.Body;

            internal HudRowBinding(WatchEntry entry, Label value)
            {
                _entry = entry;
                _value = value;
            }

            internal void Refresh()
            {
                string expected = _entry.HasValue ? _entry.ValueText : TerminalWatchesModel.UnsampledValueText;
                if (!string.Equals(_value.text, expected, StringComparison.Ordinal))
                {
                    _value.text = expected;
                }

                if (_role != _entry.Role)
                {
                    _value.EnableInClassList($"{ValueClassPrefix}{TerminalWatchesModel.ValueClassName(_role)}", false);
                    _role = _entry.Role;
                    _value.EnableInClassList($"{ValueClassPrefix}{TerminalWatchesModel.ValueClassName(_role)}", true);
                }
            }
        }
    }
}
