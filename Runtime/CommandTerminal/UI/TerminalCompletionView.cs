namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class TerminalCompletionView
    {
        private const int RowHeight = 24;
        private const int VisibleRows = 6;

        private static readonly CustomStyleProperty<Color> AccentSoft = new("--dxt-accent-soft");

        internal ListView List { get; }

        private readonly List<string> _displayed = new();
        private readonly Action<int> _accept;
        private CommandShell _shell;
        private long _commandVersion;
        private int? _selectedIndex;
        private bool _clickable;
        private string _query;
        private Color? _accent;
        private string _matchOpen = "<u>";
        private string _matchClose = "</u>";

        internal TerminalCompletionView(VisualElement root, Action<int> accept)
        {
            _accept = accept;
            root.RegisterCallback<CustomStyleResolvedEvent>(OnThemeResolved);
            List = new ListView
            {
                name = "AutoCompletePopup",
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.None,
                focusable = false,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _displayed,
            };
            List.AddToClassList("autocomplete-popup");
            List.style.display = DisplayStyle.None;
        }

        private static void AddLabel(VisualElement row, string name, string className, bool richText = false)
        {
            Label label = new()
            {
                name = name,
                enableRichText = richText,
                pickingMode = PickingMode.Ignore,
            };
            label.AddToClassList(className);
#if UNITY_2022_2_OR_NEWER
            label.parseEscapeSequences = false;
#endif
            row.Add(label);
        }

        internal void Refresh(List<string> candidates, string query, int? selectedIndex, bool visible, bool clickable, CommandShell shell)
        {
            _clickable = clickable;
            int count = visible ? candidates.Count : 0;
            long commandVersion = shell?.CommandsVersion ?? 0;
            bool contentsChanged =
                count != _displayed.Count || !string.Equals(_query, query, StringComparison.Ordinal) || !ReferenceEquals(_shell, shell) || _commandVersion != commandVersion;
            if (!contentsChanged)
            {
                for (int index = 0; index < count; ++index)
                {
                    if (!string.Equals(_displayed[index], candidates[index], StringComparison.Ordinal))
                    {
                        contentsChanged = true;
                        break;
                    }
                }
            }

            if (!contentsChanged && _selectedIndex == selectedIndex)
            {
                return;
            }

            _shell = shell;
            _query = query;
            _commandVersion = commandVersion;
            _selectedIndex = selectedIndex;
            if (contentsChanged)
            {
                _displayed.Clear();
                if (0 < count)
                {
                    _displayed.AddRange(candidates);
                }

                List.style.display = count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                List.style.height = Math.Min(count, VisibleRows) * RowHeight;
            }

            List.RefreshItems();
            int selected = selectedIndex ?? 0;
            if (0 <= selected && selected < count)
            {
                List.ScrollToItem(selected);
            }
        }

        internal bool TryGetCandidate(int index, out string candidate)
        {
            if (index < 0 || _displayed.Count <= index)
            {
                candidate = null;
                return false;
            }

            candidate = _displayed[index];
            return true;
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList("completion-row");
            AddLabel(row, "CompletionName", "completion-name", richText: true);
            AddLabel(row, "CompletionDescription", "completion-description");
            AddLabel(row, "CompletionGroup", "completion-group");
            row.RegisterCallback<ClickEvent, TerminalCompletionView>(
                static (evt, view) =>
                {
                    if (evt.button == 0 && view._clickable && evt.currentTarget is VisualElement { userData: int index })
                    {
                        view._accept(index);
                        evt.StopPropagation();
                    }
                },
                this
            );
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            string candidate = _displayed[index];
            element.userData = index;
            bool selected = index == _selectedIndex;
            element.EnableInClassList("autocomplete-item-selected", selected);
            element.EnableInClassList("autocomplete-item", !selected);
            ((Label)element[0]).text = FormatName(candidate);
            CommandInfo command = default;
            _shell?.TryGetCommand(candidate, out command);
            ((Label)element[1]).text = LogTextSanitizer.Sanitize(command.help ?? string.Empty);
            ((Label)element[2]).text = LogTextSanitizer.Sanitize(command.Group ?? string.Empty);
        }

        private string FormatName(string candidate)
        {
            string safe = LogTextSanitizer.Sanitize(candidate);
            int prefix = !string.IsNullOrEmpty(_query) && safe.StartsWith(_query, StringComparison.OrdinalIgnoreCase) ? _query.Length : 0;
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(safe.Length + 32);
            StringBuilder builder = scope.Builder;
            if (0 < prefix)
            {
                builder.Append(_matchOpen);
                CommandInputHighlight.AppendLiteral(builder, safe, 0, prefix);
                builder.Append(_matchClose);
            }

            CommandInputHighlight.AppendLiteral(builder, safe, prefix, safe.Length);
            return builder.ToString();
        }

        private void OnThemeResolved(CustomStyleResolvedEvent evt)
        {
            Color? accent = evt.customStyle.TryGetValue(AccentSoft, out Color color) ? color : null;
            if (accent == _accent)
            {
                return;
            }

            _accent = accent;
            _matchOpen = accent.HasValue ? "<color=#" + ColorUtility.ToHtmlStringRGBA(accent.Value) + "><u>" : "<u>";
            _matchClose = accent.HasValue ? "</u></color>" : "</u>";
            List.RefreshItems();
        }
    }
}
