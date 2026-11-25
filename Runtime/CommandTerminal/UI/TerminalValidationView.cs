namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Globalization;
    using System.Text;
    using Backend;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;

    internal sealed class TerminalValidationView
    {
        private const string EnterHintText = "Enter does not run it and logs the same message";
        private const string TabFixHintText = "Tab to fix";

        private static readonly CustomStyleProperty<Color> Ink = new("--dxt-ink");
        private static readonly CustomStyleProperty<Color> ErrorText = new("--dxt-error-text");
        private static readonly CustomStyleProperty<Color> LegacyInk = new("--input-text-color");
        private static readonly CustomStyleProperty<Color> LegacyError = new("--text-error");

        internal VisualElement Root { get; }

        internal bool HasFixSuggestion
        {
            get { return _visible && _validation.IsError && !string.IsNullOrEmpty(_validation.Suggestion); }
        }

        internal char? UnclosedQuote
        {
            get { return _validation.UnclosedQuote; }
        }

        internal string PlainMessage
        {
            get { return _visible ? _validation.Message : null; }
        }

        private readonly Label _glyph;
        private readonly Label _message;
        private readonly Button _suggestionButton;
        private readonly Label _hint;
        private CommandShell _shell;
        private string _line;
        private long _commandsVersion;
        private long _variablesVersion;
        private long _parserVersion;
        private CultureInfo _culture;
        private bool _visible;
        private bool _colorsDirty = true;
        private string _inkTag;
        private string _errorTag;
        private CommandShell.LineValidation _validation;

        internal TerminalValidationView(VisualElement root, VisualElement parent, Action onAcceptSuggestion)
        {
            Root = new VisualElement { name = "CommandValidation" };
            Root.AddToClassList("terminal-validation");
            Root.style.display = DisplayStyle.None;

            _glyph = CreateLabel("CommandValidationGlyph", "terminal-validation-glyph", richText: false);
            _message = CreateLabel("CommandValidationMessage", "terminal-validation-message", richText: true);
            _suggestionButton = new Button(onAcceptSuggestion) { name = "CommandValidationSuggestion", enableRichText = false };
            _suggestionButton.AddToClassList("terminal-button");
            _suggestionButton.AddToClassList("terminal-validation-chip");
            _suggestionButton.style.display = DisplayStyle.None;

            VisualElement spacer = new() { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList("terminal-validation-spacer");

            _hint = CreateLabel("CommandValidationHint", "terminal-validation-hint", richText: false);
            _hint.style.display = DisplayStyle.None;

            Root.Add(_glyph);
            Root.Add(_message);
            Root.Add(_suggestionButton);
            Root.Add(spacer);
            Root.Add(_hint);
            parent.Add(Root);
            root.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        private static Label CreateLabel(string name, string className, bool richText)
        {
            Label label = new()
            {
                name = name,
                enableRichText = richText,
                pickingMode = PickingMode.Ignore,
            };
#if UNITY_2022_2_OR_NEWER
            label.parseEscapeSequences = false;
#endif
            label.AddToClassList(className);
            return label;
        }

        private static void AppendSanitizedLiteral(StringBuilder builder, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string safe = LogTextSanitizer.Sanitize(text);
            CommandInputHighlight.AppendLiteral(builder, safe, 0, safe.Length);
        }

        private static void AppendColored(StringBuilder builder, string colorTag, string text, bool bold)
        {
            if (colorTag != null)
            {
                builder.Append(colorTag);
            }

            if (bold)
            {
                builder.Append("<b>");
            }

            AppendSanitizedLiteral(builder, text);
            if (bold)
            {
                builder.Append("</b>");
            }

            if (colorTag != null)
            {
                builder.Append("</color>");
            }
        }

        internal void Refresh(CommandShell shell, string line, bool open)
        {
            if (!open || shell == null || string.IsNullOrWhiteSpace(line))
            {
                if (_visible || _shell != null)
                {
                    _shell = null;
                    _line = string.Empty;
                    _validation = CommandShell.LineValidation.Valid();
                    _visible = false;
                    ApplyView();
                }

                return;
            }

            long commandsVersion = shell.CommandsVersion;
            long variablesVersion = shell.VariablesVersion;
            long parserVersion = CommandArg.ParserVersion;
            CultureInfo culture = CultureInfo.CurrentCulture;
            if (
                !_colorsDirty
                && _validation.CanCache
                && ReferenceEquals(_shell, shell)
                && _commandsVersion == commandsVersion
                && _variablesVersion == variablesVersion
                && _parserVersion == parserVersion
                && ReferenceEquals(_culture, culture)
                && string.Equals(_line, line, StringComparison.Ordinal)
            )
            {
                return;
            }

            _shell = shell;
            _line = line;
            _commandsVersion = commandsVersion;
            _variablesVersion = variablesVersion;
            _parserVersion = parserVersion;
            _culture = culture;

            CommandShell.LineValidation next = shell.ValidateLine(line, CommandExecutionContext.Current, allowIncomplete: true);
            int tokenEnd = next.TokenStart + next.TokenLength + (next.TokenQuoted && next.UnclosedQuote == null ? 1 : 0);
            bool typingPrefix = next.IsError && !string.IsNullOrEmpty(next.UnknownCommand) && tokenEnd == line.Length && shell.HasCommandPrefix(next.UnknownCommand);
            if (typingPrefix && next.UnclosedQuote != null)
            {
                next = CommandShell.LineValidation.Note(CommandShell.UnclosedQuoteMessage, next.UnclosedQuote, next.CanCache);
                typingPrefix = false;
            }

            bool nextVisible = !next.IsValid && !typingPrefix;
            if (!_colorsDirty && _visible == nextVisible && _validation.EqualsContent(next))
            {
                return;
            }

            _validation = next;
            _visible = nextVisible;
            _colorsDirty = false;
            ApplyView();
        }

        internal bool TryBuildFix(string line, out string fixedLine, out int caret)
        {
            if (
                !HasFixSuggestion
                || string.IsNullOrEmpty(line)
                || !CommandTokenizer.TryPrepareInsertion(
                    line,
                    _validation.Suggestion,
                    _validation.TokenStart,
                    _validation.TokenLength,
                    _validation.TokenQuoted,
                    out string insertion,
                    out int start,
                    out int length
                )
            )
            {
                fixedLine = null;
                caret = 0;
                return false;
            }

            fixedLine = line.Remove(start, length).Insert(start, insertion);
            caret = start + insertion.Length;
            return true;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle styles = evt.customStyle;
            Color ink =
                styles.TryGetValue(Ink, out Color c1) ? c1
                : styles.TryGetValue(LegacyInk, out Color c2) ? c2
                : Color.white;
            Color error =
                styles.TryGetValue(ErrorText, out Color c3) ? c3
                : styles.TryGetValue(LegacyError, out Color c4) ? c4
                : Color.red;
            string inkTag = "<color=#" + ColorUtility.ToHtmlStringRGBA(ink) + ">";
            string errorTag = "<color=#" + ColorUtility.ToHtmlStringRGBA(error) + ">";
            if (string.Equals(_inkTag, inkTag, StringComparison.Ordinal) && string.Equals(_errorTag, errorTag, StringComparison.Ordinal))
            {
                return;
            }

            _inkTag = inkTag;
            _errorTag = errorTag;
            _colorsDirty = true;
            if (_visible)
            {
                ApplyView();
                _colorsDirty = false;
            }
        }

        private void ApplyView()
        {
            if (!_visible)
            {
                Root.style.display = DisplayStyle.None;
                _suggestionButton.style.display = DisplayStyle.None;
                _hint.style.display = DisplayStyle.None;
                return;
            }

            bool isError = _validation.IsError;
            Root.style.display = DisplayStyle.Flex;
            Root.EnableInClassList("terminal-validation--error", isError);
            Root.EnableInClassList("terminal-validation--note", _validation.IsNote);
            _glyph.text = isError ? "■" : "·";
            _glyph.EnableInClassList("terminal-validation-glyph--error", isError);
            _glyph.EnableInClassList("terminal-validation-glyph--note", _validation.IsNote);

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent((_validation.Message?.Length ?? 0) + 64);
            StringBuilder builder = scope.Builder;
            if (!string.IsNullOrEmpty(_validation.UnknownCommand))
            {
                builder.Append("No command ");
                AppendColored(builder, _errorTag, _validation.UnknownCommand, bold: false);
                bool hasSuggestion = !string.IsNullOrEmpty(_validation.Suggestion);
                builder.Append(hasSuggestion ? ". Did you mean" : ".");
                _message.text = builder.ToString();
                _suggestionButton.text = hasSuggestion ? LogTextSanitizer.Sanitize(_validation.Suggestion) : string.Empty;
                _suggestionButton.style.display = hasSuggestion ? DisplayStyle.Flex : DisplayStyle.None;
                _hint.text = hasSuggestion ? TabFixHintText : EnterHintText;
                _hint.style.display = DisplayStyle.Flex;
                return;
            }

            _suggestionButton.style.display = DisplayStyle.None;
            string message = _validation.Message ?? string.Empty;
            string argName = _validation.ArgumentName;
            string typed = _validation.TypedValue;
            string suffix = typed != null ? $"You typed {typed}." : null;
            if (
                !string.IsNullOrEmpty(argName)
                && suffix != null
                && message.StartsWith(argName, StringComparison.Ordinal)
                && message.EndsWith(suffix, StringComparison.Ordinal)
                && argName.Length + suffix.Length <= message.Length
            )
            {
                AppendColored(builder, _inkTag, argName, bold: true);
                AppendSanitizedLiteral(builder, message.Substring(argName.Length, message.Length - argName.Length - suffix.Length));
                builder.Append("You typed ");
                AppendColored(builder, _errorTag, typed, bold: false);
                builder.Append('.');
            }
            else
            {
                AppendSanitizedLiteral(builder, message);
            }

            _message.text = builder.ToString();
            _hint.text = isError ? EnterHintText : string.Empty;
            _hint.style.display = isError ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
