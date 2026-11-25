namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using Extensions;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class TerminalCompletionView
    {
        private const int RowHeight = 24;
        private const int VisibleRows = 6;

        private static readonly CustomStyleProperty<Color> AccentSoft = new("--dxt-accent-soft");
        private static readonly Comparison<Row> CompareRows = CompareRank;

        internal VisualElement Panel { get; }

        internal ListView List
        {
            get { return _list; }
        }

        internal string Ghost
        {
            get { return _ghost; }
        }

        internal bool HasSelection
        {
            get { return !_suppressed && 0 < _rows.Count; }
        }

        internal int SelectedIndex
        {
            get { return _selection; }
        }

        private readonly List<Row> _rows = new();
        private readonly List<CommandCompletion> _providerRows = new();
        private readonly List<string> _history = new();
        private List<string> _prefixCandidates;
        private readonly List<CommandToken> _tokens = new();
        private readonly ListView _list;
        private readonly Label _keys;
        private readonly Action<int> _accept;
        private CommandShell _shell;
        private long _commandsVersion;
        private long _variablesVersion;
        private string _line;
        private string _query;
        private int _caret;
        private int _start;
        private int _length;
        private bool _quoted;
        private bool _mixedKinds;
        private bool _suppressed;
        private bool _requested;
        private HintDisplayMode _mode;
        private string _ghost;
        private string _matchTag = "<u>";
        private string _matchEnd = "</u>";
        private Color? _matchColor;
        private int _selection;
        private bool _clickable;
        private VisualElement _focusedRow;
        private bool _previewAllowed;

        internal TerminalCompletionView(VisualElement root, VisualElement parent, Action<int> accept)
        {
            _accept = accept;
            root.RegisterCallback<CustomStyleResolvedEvent>(ApplyColors);
            Panel = new VisualElement { name = "CompletionPanel" };
            Panel.AddToClassList("completion-panel");
            _list = new ListView
            {
                name = "AutoCompletePopup",
                fixedItemHeight = RowHeight,
                selectionType = SelectionType.None,
                focusable = false,
            };
            _list.style.minHeight = RowHeight;
            _list.makeItem = MakeRow;
            _list.bindItem = BindRow;
            _list.itemsSource = _rows;
            _list.AddToClassList("autocomplete-popup");
            _keys = MakeLabel("CompletionKeys", "completion-keys");
            Panel.Add(_list);
            Panel.Add(_keys);
            parent.Add(Panel);
            Panel.style.display = DisplayStyle.None;
        }

        private static int CompareRank(Row left, Row right)
        {
            return left.Rank == right.Rank ? left.Order.CompareTo(right.Order) : left.Rank.CompareTo(right.Rank);
        }

        private static Label MakeLabel(string name, string className)
        {
            Label label = new()
            {
                name = name,
                enableRichText = false,
                pickingMode = PickingMode.Ignore,
            };
            label.AddToClassList(className);
#if UNITY_2022_2_OR_NEWER
            label.parseEscapeSequences = false;
#endif
            return label;
        }

        internal void Refresh(CommandShell shell, string line, int? fieldCaret, HintDisplayMode mode, bool clickable, bool previewAllowed, List<string> prefixCandidates)
        {
            if (shell == null || mode is not (HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly))
            {
                if (_shell != null)
                {
                    Panel.style.display = DisplayStyle.None;
                    _rows.Clear();
                    _ghost = null;
                    _shell = null;
                    _requested = false;
                    _suppressed = false;
                }
                return;
            }

            bool sameLine = string.Equals(line, _line, StringComparison.Ordinal);
            int caret = line.SnapToTextBoundary(fieldCaret ?? (sameLine ? _caret : line.Length));
            bool sameInput = sameLine && caret == _caret;
            if (
                sameInput
                && ReferenceEquals(shell, _shell)
                && _mode == mode
                && _clickable == clickable
                && _previewAllowed == previewAllowed
                && _commandsVersion == shell.CommandsVersion
                && _variablesVersion == shell.VariablesVersion
            )
            {
                return;
            }

            if (!sameInput || _mode != mode)
            {
                _suppressed = false;
                _requested = false;
            }

            _line = line;
            _caret = caret;
            _shell = shell;
            _mode = mode;
            _clickable = clickable;
            _list.focusable = clickable;
            _previewAllowed = previewAllowed;
            _prefixCandidates = prefixCandidates;
            Rebuild();
        }

        internal bool Request()
        {
            if (_shell == null)
            {
                return false;
            }

            _requested = true;
            _suppressed = false;
            Rebuild();
            return HasSelection;
        }

        internal bool Dismiss()
        {
            if (!HasSelection)
            {
                return false;
            }

            _suppressed = true;
            RefreshSelection();
            return true;
        }

        internal bool Move(int direction)
        {
            if (!HasSelection)
            {
                return false;
            }

            int count = _rows.Count;
            if (1 < count)
            {
                _selection = (_selection + direction + count) % count;
                RefreshSelection();
                _list.ScrollToItem(_selection);
            }
            return true;
        }

        internal bool TryAccept(string line, int index, bool oneWord, out string text, out int caret)
        {
            if (
                !HasSelection
                || !string.Equals(line, _line, StringComparison.Ordinal)
                || index < 0
                || _rows.Count <= index
                || !TryInsertion(_rows[index], out string insertion, out int start, out int length)
            )
            {
                text = null;
                caret = 0;
                return false;
            }

            if (oneWord)
            {
                if (index != _selection || string.IsNullOrEmpty(_ghost))
                {
                    text = null;
                    caret = 0;
                    return false;
                }

                int end = 0;
                while (end < _ghost.Length && char.IsWhiteSpace(_ghost[end]))
                {
                    ++end;
                }
                while (end < _ghost.Length && !char.IsWhiteSpace(_ghost[end]))
                {
                    ++end;
                }
                text = line.Insert(_caret, _ghost.Substring(0, end));
                caret = _caret + end;
                return true;
            }

            text = line.Remove(start, length).Insert(start, insertion);
            caret = start + insertion.Length;
            return true;
        }

        internal bool TryAcceptFocusedRow(VisualElement target)
        {
            return (target == _list || _list.Contains(target)) && TryAcceptRow(_focusedRow);
        }

        private void AppendMatch(StringBuilder builder, string text)
        {
            int match = 0;
            bool insideMatch = false;
            for (int index = 0; index < text.Length; )
            {
                int width = char.IsSurrogatePair(text, index) ? 2 : 1;
                ReadOnlySpan<char> character = text.AsSpan(index, width);
                bool hit = width <= _query.Length - match && character.Equals(_query.AsSpan(match, width), StringComparison.OrdinalIgnoreCase);
                if (hit)
                {
                    match += width;
                }

                if (hit != insideMatch)
                {
                    builder.Append(hit ? _matchTag : _matchEnd);
                    insideMatch = hit;
                }
                CommandInputHighlight.AppendLiteral(builder, text, index, index + width);
                index += width;
            }

            if (insideMatch)
            {
                builder.Append(_matchEnd);
            }
        }

        private void Rebuild()
        {
            _rows.Clear();
            _tokens.Clear();
            _query = string.Empty;
            _selection = 0;
            _quoted = false;
            _mixedKinds = false;
            CommandTokenizer.Tokenize(_line, _tokens);
            bool found = CommandTokenizer.TryFindActiveToken(_line, _caret, _tokens, out int active, out _start, out _length, out _);
            _shell.EnsureAutoCommandsRegistered();
            bool showSuggestions = _requested || _mode == HintDisplayMode.Always || _line.Length != 0;
            if (showSuggestions && !_suppressed)
            {
                if (found && _shell.TryComplete(CommandExecutionContext.Current, _line, _caret, _providerRows, out CommandCompletionContext context))
                {
                    _query = context.Token;
                    _quoted = context.IsQuoted;
                    foreach (CommandCompletion candidate in _providerRows)
                    {
                        _rows.Add(new Row(candidate, string.Empty, candidate.Hint, rank: 0, order: _rows.Count, wholeLine: false));
                    }
                }
                else
                {
                    RefreshNames(!found || active == 0);
                    _rows.Sort(CompareRows);
                }
            }

            _commandsVersion = _shell.CommandsVersion;
            _variablesVersion = _shell.VariablesVersion;
            RefreshSelection();
            if (HasSelection)
            {
                _list.ScrollToItem(0);
            }
        }

        private void RefreshNames(bool commandSlot)
        {
            List<string> history = _prefixCandidates;
            if (history == null)
            {
                _history.Clear();
                Terminal.AutoComplete?.Complete(_line, _history);
                history = _history;
            }

            _query = _line;
            if (commandSlot)
            {
                if (0 <= _start && _tokens.Count != 0)
                {
                    _query = _line.Substring(_start, _caret - _start);
                    _quoted = _tokens[0].StartQuote != null;
                }
                else
                {
                    _query = string.Empty;
                    _start = _caret;
                    _length = 0;
                }

                foreach (KeyValuePair<string, CommandInfo> pair in _shell.CommandsSorted)
                {
                    int rank = CommandPaletteSearch.Rank(_query, pair.Key);
                    if (_query.Length == 0 || rank != CommandPaletteSearch.NoMatch)
                    {
                        AddRow(new CommandCompletion(pair.Key, description: pair.Value.help), "cmd", pair.Value.Group, rank);
                    }
                }

                int commandCount = _rows.Count;
                string query = _query.StartsWith('$') ? _query[1..] : _query;
                foreach (KeyValuePair<string, CommandArg> pair in _shell.Variables)
                {
                    int rank = CommandPaletteSearch.Rank(query, pair.Key);
                    if (query.Length == 0 || rank != CommandPaletteSearch.NoMatch)
                    {
                        AddRow(new CommandCompletion("$" + pair.Key, description: pair.Value.contents), "var", null, rank);
                    }
                }

                _mixedKinds = commandCount != 0 && commandCount < _rows.Count;
            }

            foreach (string entry in history)
            {
                if (!commandSlot || !_shell.TryGetCommand(entry, out _))
                {
                    AddRow(new CommandCompletion(entry), string.Empty, null, CommandPaletteSearch.Rank(_query, entry), wholeLine: true);
                }
            }
        }

        private void AddRow(CommandCompletion candidate, string badge, string hint, int rank, bool wholeLine = false)
        {
            if (rank == CommandPaletteSearch.NoMatch)
            {
                rank = CommandPaletteSearch.SubsequenceMatch;
            }

            _rows.Add(new Row(candidate, badge, hint, rank, _rows.Count, wholeLine));
        }

        private bool TryInsertion(Row row, out string insertion, out int start, out int length)
        {
            if (row.WholeLine)
            {
                insertion = row.Value.InsertionText;
                start = 0;
                length = _line.Length;
                return true;
            }

            CommandCompletionReplacement? replacement = row.Value.Replacement;
            int rawStart = replacement?.Start ?? _start;
            int rawLength = replacement?.Length ?? _length;
            return CommandTokenizer.TryPrepareInsertion(
                _line,
                row.Value.InsertionText,
                rawStart,
                rawLength,
                _quoted,
                out insertion,
                out start,
                out length,
                wholeToken: rawStart == _start && rawLength == _length
            );
        }

        private void RefreshSelection()
        {
            int count = HasSelection ? _rows.Count : 0;
            _ghost = null;
            if (
                0 < count
                && _previewAllowed
                && 0 < _caret
                && TryInsertion(_rows[_selection], out string insertion, out int start, out int length)
                && _caret == _line.Length
                && start <= _caret
                && start + length == _caret
                && _caret - start < insertion.Length
                && _line.AsSpan(start, _caret - start).Equals(insertion.AsSpan(0, _caret - start), StringComparison.Ordinal)
            )
            {
                _ghost = insertion.Substring(_caret - start);
            }

            _list.style.height = Math.Min(count, VisibleRows) * RowHeight;
            _keys.text = (1 < count, _ghost != null) switch
            {
                (true, true) => "↑↓ select   Tab accept   → accept word   Esc dismiss",
                (true, false) => "↑↓ select   Tab accept   Esc dismiss",
                (false, true) => "Tab accept   → accept word   Esc dismiss",
                _ => "Tab accept   Esc dismiss",
            };
            Panel.style.display = count != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.RefreshItems();
        }

        private void ApplyColors(CustomStyleResolvedEvent evt)
        {
            Color? color = evt.customStyle.TryGetValue(AccentSoft, out Color resolved) ? resolved : null;
            if (_matchColor == color)
            {
                return;
            }
            _matchColor = color;
            _matchTag = color.HasValue ? "<color=#" + ColorUtility.ToHtmlStringRGBA(color.Value) + "><u>" : "<u>";
            _matchEnd = color.HasValue ? "</u></color>" : "</u>";

            if (HasSelection)
            {
                _list.RefreshItems();
            }
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList("completion-row");
            row.Add(MakeLabel("CompletionKind", "completion-kind"));
            Label name = MakeLabel("CompletionName", "completion-name");
            name.enableRichText = true;
            row.Add(name);
            row.Add(MakeLabel("CompletionDescription", "completion-description"));
            row.Add(MakeLabel("CompletionHint", "completion-hint"));
            row.RegisterCallback<ClickEvent, TerminalCompletionView>(
                static (evt, view) =>
                {
                    if (evt.button == 0 && view.TryAcceptRow(evt.currentTarget as VisualElement))
                    {
                        evt.StopPropagation();
                    }
                },
                this
            );
            row.RegisterCallback<NavigationSubmitEvent, TerminalCompletionView>(
                static (evt, view) =>
                {
                    if (view.TryAcceptRow(evt.currentTarget as VisualElement))
                    {
                        evt.StopPropagation();
                    }
                },
                this
            );
            row.RegisterCallback<FocusEvent, TerminalCompletionView>(
                static (evt, view) =>
                {
                    view._focusedRow = evt.currentTarget as VisualElement;
                },
                this
            );
            row.RegisterCallback<BlurEvent, TerminalCompletionView>(
                static (evt, view) =>
                {
                    if (ReferenceEquals(view._focusedRow, evt.currentTarget))
                    {
                        view._focusedRow = null;
                    }
                },
                this
            );
            return row;
        }

        private bool TryAcceptRow(VisualElement row)
        {
            if (!HasSelection || !_clickable || row?.panel == null || row.userData is not int index)
            {
                return false;
            }

            _accept(index);
            return true;
        }

        private void BindRow(VisualElement element, int index)
        {
            Row row = _rows[index];
            element.userData = index;
            element.focusable = _clickable;
            element.EnableInClassList("terminal-button", _clickable);
            element.EnableInClassList("autocomplete-item-selected", index == _selection);
            element.EnableInClassList("autocomplete-item", index != _selection);
            Label kind = (Label)element[0];
            kind.text = row.Badge;
            kind.style.display = _mixedKinds ? DisplayStyle.Flex : DisplayStyle.None;
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            AppendMatch(scope.Builder, LogTextSanitizer.Sanitize(row.Value.EffectiveDisplayLabel));
            ((Label)element[1]).text = scope.Builder.ToString();
            ((Label)element[2]).text = LogTextSanitizer.Sanitize(row.Value.Description ?? string.Empty);
            ((Label)element[3]).text = LogTextSanitizer.Sanitize(row.Hint ?? string.Empty);
        }

        private readonly struct Row
        {
            internal readonly CommandCompletion Value;
            internal readonly string Badge;
            internal readonly string Hint;
            internal readonly bool WholeLine;
            internal readonly int Rank;
            internal readonly int Order;

            internal Row(CommandCompletion value, string badge, string hint, int rank, int order, bool wholeLine)
            {
                Value = value;
                Badge = badge;
                Hint = hint;
                WholeLine = wholeLine;
                Rank = rank;
                Order = order;
            }
        }
    }
}
