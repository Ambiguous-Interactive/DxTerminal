namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using Extensions;
    using Helper;
    using Input;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class TerminalCompletionView
    {
        private const int VisibleRows = 6;
        private const int BodyPadding = 3;
        private const KeyHints DefaultKeyHints = KeyHints.Previous | KeyHints.Next | KeyHints.Accept | KeyHints.Dismiss;

        private static readonly CustomStyleProperty<Color> AccentSoft = new("--dxt-accent-soft");
        private static readonly Comparison<Row> CompareRows = CompareRank;
        private static readonly string[] FooterLabels = BuildFooterLabels();

        internal VisualElement Panel { get; }

        internal float MinimumHeight
        {
            get { return Panel.style.minHeight.value.value; }
        }

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
        private readonly VisualElement _body;
        private readonly VisualElement _suggestions;
        private readonly VisualElement _signature;
        private readonly VisualElement _details;
        private readonly Label _argument;
        private readonly Label _argumentDescription;
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
        private CommandArgument _activeArgument;
        private CommandBuilder.SubcommandRoute _signatureRoute;
        private int _signatureStage;
        private string _matchTag = "<u>";
        private string _matchEnd = "</u>";
        private Color? _matchColor;
        private int _selection;
        private bool _clickable;
        private VisualElement _focusedRow;
        private bool _previewAllowed;
        private KeyHints _boundHints;
        private KeyHints _footerHints;
        private int _rowHeight;
        private int _footerHeight;
        private int _detailsHeight;
        private int _signatureHeight;

        internal TerminalCompletionView(VisualElement root, VisualElement parent, Action<int> accept, TerminalTextSize textSize = TerminalTextSize.Small)
        {
            _accept = accept;
            TerminalTextSizeMetrics metrics = TerminalTextSizeMetrics.For(textSize);
            _rowHeight = (int)metrics.CompletionRowHeight;
            _footerHeight = (int)metrics.CompletionFooterHeight;
            _detailsHeight = (int)metrics.CompletionDetailsHeight;
            _signatureHeight = (int)metrics.SignatureHeight;
            root.RegisterCallback<CustomStyleResolvedEvent>(ApplyColors);
            Panel = new VisualElement { name = "CompletionPanel" };
            Panel.AddToClassList("completion-panel");
            _details = new VisualElement();
            _details.AddToClassList("completion-details");
            _details.style.minHeight = _detailsHeight;
            _argument = MakeLabel("CompletionArgument", "completion-detail-title");
            _argumentDescription = MakeLabel("CompletionArgumentDescription", "completion-detail-description");
            _details.Add(_argument);
            _details.Add(_argumentDescription);
            _list = new ListView
            {
                name = "AutoCompletePopup",
                fixedItemHeight = _rowHeight,
                selectionType = SelectionType.None,
                focusable = false,
            };
            _list.style.minHeight = _rowHeight;
            _list.makeItem = MakeRow;
            _list.bindItem = BindRow;
            _list.itemsSource = _rows;
            _list.AddToClassList("autocomplete-popup");
            _keys = MakeLabel("CompletionKeys", "completion-keys");
            _keys.style.height = _footerHeight;
            _keys.style.flexShrink = 0f;
            _suggestions = new VisualElement();
            _suggestions.AddToClassList("completion-suggestions");
            _suggestions.Add(_list);
            _suggestions.Add(_keys);
            _body = new VisualElement();
            _body.AddToClassList("completion-body");
            _body.style.paddingTop = BodyPadding;
            _body.style.paddingBottom = BodyPadding;
            _body.Add(_suggestions);
            _body.Add(_details);
            Panel.Add(_body);
            _signature = new VisualElement { name = "CommandSignature" };
            _signature.AddToClassList("completion-signature");
            _signature.style.height = _signatureHeight;
            Panel.Add(_signature);
            parent.Add(Panel);
            Panel.style.display = DisplayStyle.None;
        }

        internal static void AppendHighlightedMatch(StringBuilder builder, string text, string query, string matchTag, string matchEnd)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.IsNullOrEmpty(query))
            {
                CommandInputHighlight.AppendLiteral(builder, text, 0, text.Length);
                return;
            }

            int match = 0;
            bool insideMatch = false;
            for (int index = 0; index < text.Length; )
            {
                int width = char.IsSurrogatePair(text, index) ? 2 : 1;
                ReadOnlySpan<char> character = text.AsSpan(index, width);
                bool hit = width <= query.Length - match && character.Equals(query.AsSpan(match, width), StringComparison.OrdinalIgnoreCase);
                if (hit)
                {
                    match += width;
                }

                if (hit != insideMatch)
                {
                    builder.Append(hit ? matchTag : matchEnd);
                    insideMatch = hit;
                }
                CommandInputHighlight.AppendLiteral(builder, text, index, index + width);
                index += width;
            }

            if (insideMatch)
            {
                builder.Append(matchEnd);
            }
        }

        internal static bool PopulateSignature(
            VisualElement signature,
            CommandShell shell,
            List<CommandToken> tokens,
            int active,
            int caret,
            ref CommandBuilder.SubcommandRoute cachedRoute,
            ref int cachedStage,
            out CommandArgument activeArgument,
            out int activeIndex,
            out int totalArguments
        )
        {
            CommandBuilder.SubcommandRoute route = null;
            if (
                shell != null
                && 0 < tokens.Count
                && shell.TryResolveToken(tokens[0], tokens[0].GetVariableName(), out CommandArg nameToken)
                && !string.IsNullOrWhiteSpace(nameToken.contents)
                && shell.TryGetCommand(CommandShell.NormalizeCommandName(nameToken.contents), out CommandInfo command)
            )
            {
                route = command.SyntaxRoute;
            }

            int routed = 1;
            while (route is { IsRouter: true } && routed <= active && routed < tokens.Count)
            {
                CommandToken selector = tokens[routed];
                if (routed == active && caret < selector.End)
                {
                    break;
                }

                if (
                    !shell.TryResolveToken(selector, selector.GetVariableName(), out CommandArg resolved)
                    || !route.Lookup.TryGetValue(resolved.contents ?? string.Empty, out CommandBuilder.SubcommandRoute child)
                )
                {
                    break;
                }

                route = child;
                ++routed;
            }

            CommandArgument[] specs = route?.Specs ?? Array.Empty<CommandArgument>();
            int stage = active - routed;
            if (route?.HasRemaining == true && specs.Length <= stage)
            {
                stage = specs.Length - 1;
            }

            if (ReferenceEquals(cachedRoute, route) && cachedStage == stage)
            {
                activeArgument = 0 <= stage && stage < specs.Length ? specs[stage] : null;
                activeIndex = stage;
                totalArguments = specs.Length;
                return false;
            }

            cachedRoute = route;
            cachedStage = stage;
            signature.Clear();
            if (route == null)
            {
                activeArgument = null;
                activeIndex = -1;
                totalArguments = specs.Length;
                return true;
            }

            CommandArgument foundArgument = null;
            int foundIndex = -1;
            Label name = MakeLabel("SignatureCommand", "completion-signature-name");
            name.text = LogTextSanitizer.Sanitize(route.FullPath);
            signature.Add(name);
            for (int i = 0; i < specs.Length; ++i)
            {
                CommandArgument spec = specs[i];
                VisualElement argument = new();
                argument.AddToClassList("completion-signature-argument");
                argument.EnableInClassList("completion-argument-active", i == stage);
                Label argumentName = MakeLabel(null, "completion-signature-part");
                argumentName.text = spec.IsRequired ? spec.Name + ": " : "[" + spec.Name + ": ";
                Label type = MakeLabel(null, "completion-signature-part");
                type.text = LogTextSanitizer.Sanitize(spec.SignatureType);
                type.AddToClassList("completion-type-" + (spec.HasChoices ? CommandSyntaxClassifier.CommandSyntaxRole.Choice : spec.SyntaxRole));
                Label suffix = MakeLabel(null, "completion-signature-part");
                suffix.text = LogTextSanitizer.Sanitize(spec.SignatureSuffix);
                argument.Add(argumentName);
                argument.Add(type);
                argument.Add(suffix);
                signature.Add(argument);
                if (i == stage)
                {
                    foundArgument = spec;
                    foundIndex = i;
                }
            }

            Label description = MakeLabel("SignatureDescription", "completion-signature-description");
            description.text = LogTextSanitizer.Sanitize(route.Help ?? string.Empty);
            signature.Add(description);
            activeArgument = foundArgument;
            activeIndex = foundIndex;
            totalArguments = specs.Length;
            return true;
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

        private static KeyHints ReadBindings(TerminalKeyboardController keyboard)
        {
            if (keyboard == null || !keyboard.isActiveAndEnabled)
            {
                return DefaultKeyHints;
            }

            KeyHints keys = default;
            if (keyboard.UsesCompletionBinding(KeyCode.UpArrow))
            {
                keys |= KeyHints.Previous;
            }
            if (keyboard.UsesCompletionBinding(KeyCode.DownArrow))
            {
                keys |= KeyHints.Next;
            }
            if (keyboard.UsesCompletionBinding(KeyCode.Tab))
            {
                keys |= KeyHints.Accept;
            }
            if (keyboard.UsesCompletionBinding(KeyCode.Escape))
            {
                keys |= KeyHints.Dismiss;
            }
            return keys;
        }

        private static string[] BuildFooterLabels()
        {
            string[] labels = new string[(int)KeyHints.Word << 1];
            StringBuilder text = new(64);
            for (int index = 0; index < labels.Length; ++index)
            {
                KeyHints visible = (KeyHints)index;
                text.Clear();
                switch (visible & (KeyHints.Previous | KeyHints.Next))
                {
                    case KeyHints.Previous | KeyHints.Next:
                        AppendHint(text, "↑↓ select");
                        break;
                    case KeyHints.Previous:
                        AppendHint(text, "↑ select");
                        break;
                    case KeyHints.Next:
                        AppendHint(text, "↓ select");
                        break;
                }
                if ((visible & KeyHints.Accept) != 0)
                {
                    AppendHint(text, "Tab accept");
                }
                if ((visible & KeyHints.Word) != 0)
                {
                    AppendHint(text, "→ accept word");
                }
                if ((visible & KeyHints.Dismiss) != 0)
                {
                    AppendHint(text, "Esc dismiss");
                }
                labels[index] = text.ToString();
            }
            return labels;
        }

        private static void AppendHint(StringBuilder builder, string hint)
        {
            if (0 < builder.Length)
            {
                builder.Append("   ");
            }
            builder.Append(hint);
        }

        internal void SetTextSize(TerminalTextSize textSize)
        {
            TerminalTextSizeMetrics metrics = TerminalTextSizeMetrics.For(textSize);
            int rowHeight = (int)metrics.CompletionRowHeight;
            int footerHeight = (int)metrics.CompletionFooterHeight;
            int detailsHeight = (int)metrics.CompletionDetailsHeight;
            int signatureHeight = (int)metrics.SignatureHeight;
            if (_rowHeight == rowHeight && _footerHeight == footerHeight && _detailsHeight == detailsHeight && _signatureHeight == signatureHeight)
            {
                return;
            }

            _rowHeight = rowHeight;
            _footerHeight = footerHeight;
            _detailsHeight = detailsHeight;
            _signatureHeight = signatureHeight;
            _list.fixedItemHeight = _rowHeight;
            _list.style.minHeight = _rowHeight;
            _details.style.minHeight = _detailsHeight;
            _keys.style.height = _footerHeight;
            _signature.style.height = _signatureHeight;
            if (_list.panel != null)
            {
                _list.Rebuild();
            }

            if (_shell != null)
            {
                RefreshSelection();
            }
        }

        internal void Refresh(
            CommandShell shell,
            string line,
            int? fieldCaret,
            HintDisplayMode mode,
            bool clickable,
            bool previewAllowed,
            List<string> prefixCandidates,
            TerminalKeyboardController keyboard
        )
        {
            if (shell == null || mode is not (HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly))
            {
                if (_shell != null)
                {
                    Panel.style.display = DisplayStyle.None;
                    Panel.style.minHeight = 0f;
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
            KeyHints boundHints = ReadBindings(keyboard);
            bool changedBindings = _boundHints != boundHints;
            _boundHints = boundHints;
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
                if (changedBindings)
                {
                    RefreshSelection();
                }
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
            AppendHighlightedMatch(builder, text, _query, _matchTag, _matchEnd);
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
            RefreshSignature(active);
            bool showSuggestions = _requested || _mode == HintDisplayMode.Always || _line.Length != 0;
            if (showSuggestions && !_suppressed)
            {
                if (found && _shell.TryComplete(CommandExecutionContext.Current, _line, _caret, _providerRows, out CommandCompletionContext context))
                {
                    _query = context.Token;
                    _quoted = context.IsQuoted;
                    string badge = _activeArgument?.DeclaredType.IsEnum == true ? "enum" : string.Empty;
                    foreach (CommandCompletion candidate in _providerRows)
                    {
                        _rows.Add(new Row(candidate, badge, candidate.Hint, rank: 0, order: _rows.Count, wholeLine: false));
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

            bool showSuggestions = 0 < count;
            bool showDetails =
                !_suppressed && _activeArgument != null && (showSuggestions || _activeArgument.HasChoices || !string.IsNullOrWhiteSpace(_activeArgument.Description));
            _body.style.display = showSuggestions || showDetails ? DisplayStyle.Flex : DisplayStyle.None;
            _suggestions.style.display = showSuggestions ? DisplayStyle.Flex : DisplayStyle.None;
            _details.style.display = showDetails ? DisplayStyle.Flex : DisplayStyle.None;
            _list.style.height = Math.Min(count, VisibleRows) * _rowHeight;
            int footerHeight = RefreshFooter(count);
            int bodyHeight = 0;
            if (showSuggestions)
            {
                bodyHeight = _rowHeight + footerHeight;
            }
            if (showDetails)
            {
                bodyHeight = Math.Max(bodyHeight, _detailsHeight);
            }
            if (0 < bodyHeight)
            {
                bodyHeight += 2 * BodyPadding;
            }
            int signatureHeight = _signature.childCount == 0 ? 0 : _signatureHeight;
            _signature.style.display = signatureHeight != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            Panel.style.minHeight = bodyHeight + signatureHeight;
            Panel.style.display = bodyHeight + signatureHeight != 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _list.RefreshItems();
        }

        private int RefreshFooter(int count)
        {
            KeyHints visible = default;
            if (0 < count)
            {
                visible = _boundHints;
                if (count == 1)
                {
                    visible &= ~(KeyHints.Previous | KeyHints.Next);
                }
                if (_ghost != null)
                {
                    visible |= KeyHints.Word;
                }
            }

            _keys.style.display = visible == default ? DisplayStyle.None : DisplayStyle.Flex;
            if (visible != _footerHints)
            {
                _footerHints = visible;
                _keys.text = FooterLabels[(int)visible];
            }
            return visible == default ? 0 : _footerHeight;
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

        private void RefreshSignature(int active)
        {
            if (
                !PopulateSignature(
                    _signature,
                    _shell,
                    _tokens,
                    active,
                    _caret,
                    ref _signatureRoute,
                    ref _signatureStage,
                    out CommandArgument activeArgument,
                    out int activeIndex,
                    out int totalArguments
                )
            )
            {
                return;
            }

            _activeArgument = activeArgument;
            if (_activeArgument != null)
            {
                _argument.text = $"{_activeArgument.Name} · {activeIndex + 1} of {totalArguments} arguments";
                _argumentDescription.text = LogTextSanitizer.Sanitize(_activeArgument.Description ?? string.Empty);
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
            string description = row.Value.Description;
            ((Label)element[2]).text = string.Equals(description, _activeArgument?.Description, StringComparison.Ordinal)
                ? string.Empty
                : LogTextSanitizer.Sanitize(description ?? string.Empty);
            ((Label)element[3]).text = LogTextSanitizer.Sanitize(row.Hint ?? string.Empty);
        }

        [Flags]
        private enum KeyHints
        {
            [Obsolete("Use default or a valid key hint")]
            None = 0,
            Previous = 1 << 0,
            Next = 1 << 1,
            Accept = 1 << 2,
            Dismiss = 1 << 3,
            Word = 1 << 4,
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
