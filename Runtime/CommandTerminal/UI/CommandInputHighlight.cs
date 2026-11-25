namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Backend;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;
    using CommandSyntaxRole = Backend.CommandSyntaxClassifier.CommandSyntaxRole;
    using CommandSyntaxSpan = Backend.CommandSyntaxClassifier.CommandSyntaxSpan;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;
#endif

    internal sealed class CommandInputHighlight
    {
        private const string NarrowGlyphs = "iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii";
        private const string WideGlyphs = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW";

        private readonly Label _label;
        private readonly VisualElement _nativeText;
        private readonly bool _labelFollowsNativeScroll;
        private readonly CommandSyntaxClassifier _classifier = new();
        private readonly Palette _palette = new();
        private List<CommandSyntaxSpan> _spans = new();
        private List<CommandSyntaxSpan> _nextSpans = new();
        private string _line;
        private string _ghost;
        private char? _unclosedQuote;
        private CommandShell _shell;
        private long _commandsVersion;
        private long _variablesVersion;
        private string _monoTag;
        private float _advance;
        private float _fontSize;
        private float _legacyTextWidth;
        private FontDefinition _font;
        private bool _labelDirty = true;
        private bool _geometryChanged = true;
        private bool _visible;
        private int _regularColumns;
        private bool _hasWaves;
        private bool _hasLineBreak;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private Keyboard _keyboard;
        private bool _composing;
#endif

        internal CommandInputHighlight(VisualElement root, VisualElement input)
        {
            _nativeText = input.Q<TextElement>(className: "unity-text-element") ?? input;
            _labelFollowsNativeScroll = _nativeText is TextElement;
            _label = new Label
            {
                name = "CommandInputHighlight",
                pickingMode = PickingMode.Ignore,
                enableRichText = true,
            };
#if UNITY_2022_2_OR_NEWER
            _label.parseEscapeSequences = false;
#endif
#if UNITY_6000_0_OR_NEWER
            // Advanced Text's worker stack overflows when it lays out a long line of rich-text spans.
            _label.style.unityTextGenerator = TextGeneratorType.Standard;
#endif
            _label.style.position = Position.Absolute;
            _label.style.whiteSpace = WhiteSpace.NoWrap;
            _label.style.marginLeft = 0;
            _label.style.marginTop = 0;
            _label.style.marginRight = 0;
            _label.style.marginBottom = 0;
            _label.style.paddingLeft = 0;
            _label.style.paddingTop = 0;
            _label.style.paddingRight = 0;
            _label.style.paddingBottom = 0;
            _label.style.display = DisplayStyle.None;
            _nativeText.Add(_label);
            _nativeText.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            _nativeText.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
#endif
            root.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            _label.generateVisualContent += DrawWaves;
        }

        internal static void AppendLiteral(StringBuilder builder, string line, int start, int end)
        {
            foreach (char value in line.AsSpan(start, end - start))
            {
                switch (value)
                {
                    case '<':
                        builder.Append("<noparse><</noparse>");
                        break;
                    case '>':
                        builder.Append("<noparse>></noparse>");
                        break;
                    default:
                        builder.Append(value);
                        break;
                }
            }
        }

        private static bool SpansMatch(List<CommandSyntaxSpan> previous, List<CommandSyntaxSpan> next)
        {
            int count = previous.Count;
            if (count != next.Count)
            {
                return false;
            }

            for (int index = 0; index < count; ++index)
            {
                CommandSyntaxSpan oldSpan = previous[index];
                CommandSyntaxSpan newSpan = next[index];
                if (oldSpan.Start != newSpan.Start || oldSpan.End != newSpan.End || oldSpan.Role != newSpan.Role)
                {
                    return false;
                }
            }

            return true;
        }

        internal void Refresh(string line, CommandShell shell, bool inputOwnsFocus, string ghost = null, char? unclosedQuote = null)
        {
            if (!string.Equals(_ghost, ghost, StringComparison.Ordinal))
            {
                _ghost = ghost;
                _labelDirty = true;
            }

            if (_unclosedQuote != unclosedQuote)
            {
                _unclosedQuote = unclosedQuote;
                _labelDirty = true;
            }

            long commandsVersion = shell?.CommandsVersion ?? 0;
            long variablesVersion = shell?.VariablesVersion ?? 0;
            bool lineChanged = !string.Equals(_line, line, StringComparison.Ordinal);
            bool hasInput = shell != null && !string.IsNullOrEmpty(line);
            if (
                !ReferenceEquals(_shell, shell)
                || _commandsVersion != commandsVersion
                || _variablesVersion != variablesVersion
                || lineChanged
                || (hasInput && _classifier.RefreshConfiguration())
            )
            {
                _shell = shell;
                _line = line;
                if (!hasInput)
                {
                    _labelDirty |= lineChanged || 0 < _spans.Count;
                    _spans.Clear();
                }
                else if (lineChanged)
                {
                    _classifier.Classify(line, shell, _spans);
                    _labelDirty = true;
                }
                else
                {
                    _classifier.Classify(line, shell, _nextSpans);
                    if (!SpansMatch(_spans, _nextSpans))
                    {
                        (_spans, _nextSpans) = (_nextSpans, _spans);
                        _labelDirty = true;
                    }
                }

                _commandsVersion = shell?.CommandsVersion ?? 0;
                _variablesVersion = shell?.VariablesVersion ?? 0;
                _regularColumns = 0;
                _hasLineBreak = false;
                bool regular = true;
                foreach (char character in line.AsSpan())
                {
                    _hasLineBreak |= character is '\r' or '\n';
                    regular &= ' ' <= character && character <= '~';
                    if (regular)
                    {
                        ++_regularColumns;
                    }
                }

                _hasWaves = false;
                foreach (CommandSyntaxSpan span in _spans)
                {
                    if (span.Start < _regularColumns && span.Role is CommandSyntaxRole.Invalid or CommandSyntaxRole.UnknownCommand)
                    {
                        _hasWaves = true;
                        break;
                    }
                }
            }

            IResolvedStyle nativeStyle = _nativeText.resolvedStyle;
            if (_geometryChanged || !_fontSize.Equals(nativeStyle.fontSize) || !_font.Equals(nativeStyle.unityFontDefinition))
            {
                RefreshGeometry(nativeStyle);
            }

            if (_labelDirty)
            {
                WriteLabel();
                _label.MarkDirtyRepaint();
                if (!_labelFollowsNativeScroll && !string.IsNullOrEmpty(line) && !_hasLineBreak)
                {
                    _legacyTextWidth = 0 < _advance && line.Length == _regularColumns ? _advance * line.Length : MeasureWidth(_label.text);
                }
            }

            bool visible = !IsComposing(inputOwnsFocus) && !string.IsNullOrEmpty(line) && !_hasLineBreak;

            if (visible && !_labelFollowsNativeScroll)
            {
                // Unity 2021 keeps the single-line scroll offset inside its text editor.
                visible = 0 < _legacyTextWidth && _legacyTextWidth < _nativeText.contentRect.width;
            }

            if (_visible != visible)
            {
                _visible = visible;
                _nativeText.style.color = visible ? new StyleColor(Color.clear) : new StyleColor(StyleKeyword.Null);
                _label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool IsComposing(bool inputOwnsFocus)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return inputOwnsFocus && !string.IsNullOrEmpty(UnityEngine.Input.compositionString);
#elif ENABLE_INPUT_SYSTEM
            SetKeyboard(inputOwnsFocus ? Keyboard.current : null);
            return _composing;
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            SetKeyboard(null);
        }

        private void SetKeyboard(Keyboard keyboard)
        {
            if (ReferenceEquals(_keyboard, keyboard))
            {
                return;
            }

            if (_keyboard != null)
            {
                _keyboard.onIMECompositionChange -= OnCompositionChanged;
            }

            _composing = false;
            _keyboard = keyboard;
            if (_keyboard != null)
            {
                _keyboard.onIMECompositionChange += OnCompositionChanged;
            }
        }

        private void OnCompositionChanged(IMECompositionString composition)
        {
            _composing = 0 < composition.Count;
        }
#endif

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            _palette.Apply(evt.customStyle);
            _label.style.color = _palette.Ink;
            _labelDirty = true;
            _geometryChanged = true;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            _geometryChanged = true;
        }

        private void RefreshGeometry(IResolvedStyle nativeStyle)
        {
            _geometryChanged = false;
            Rect rect = _nativeText.contentRect;
            _label.style.left = rect.x;
            _label.style.top = rect.y;
            _label.style.width = rect.width;
            _label.style.height = rect.height;
            _label.style.unityTextAlign = nativeStyle.unityTextAlign;

            FontDefinition font = nativeStyle.unityFontDefinition;
            float size = nativeStyle.fontSize;
            if (_monoTag == null || !_fontSize.Equals(size) || !_font.Equals(font))
            {
                _font = font;
                _fontSize = size;
                _label.style.unityFontDefinition = font;
                _label.style.fontSize = size;
                float narrow = MeasureWidth(NarrowGlyphs);
                float wide = MeasureWidth(WideGlyphs);
                _advance = 0 < narrow && Mathf.Approximately(narrow, wide) ? wide / WideGlyphs.Length : 0;
                _monoTag = 0 < _advance ? "<mspace=" + _advance.ToString("0.####", CultureInfo.InvariantCulture) + ">" : string.Empty;
                _labelDirty = true;
            }

            _labelDirty |= !_labelFollowsNativeScroll && !float.IsFinite(_legacyTextWidth);
            _label.MarkDirtyRepaint();
        }

        private float MeasureWidth(string sample)
        {
            return _label.MeasureTextSize(sample, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
        }

        private void WriteLabel()
        {
            _labelDirty = false;
            // A Label change event would reach the command field as another edit.
            INotifyValueChanged<string> display = _label;
            if (string.IsNullOrEmpty(_line))
            {
                display.SetValueWithoutNotify(string.Empty);
                return;
            }

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(_line.Length + (_spans.Count * 32));
            StringBuilder builder = scope.Builder;
            _palette.AppendHighlighted(builder, _line, _spans, forceBold: false, monoTag: _monoTag, regularColumns: _regularColumns);
            bool hasGhost = !string.IsNullOrEmpty(_ghost);
            if (hasGhost || _unclosedQuote.HasValue)
            {
                string ghostTag = _palette.GhostTag;
                builder.Append(ghostTag);
                if (hasGhost)
                {
                    string safeGhost = LogTextSanitizer.Sanitize(_ghost);
                    AppendLiteral(builder, safeGhost, 0, safeGhost.Length);
                }

                if (_unclosedQuote is char closingQuote)
                {
                    switch (closingQuote)
                    {
                        case '<':
                            builder.Append("<noparse><</noparse>");
                            break;
                        case '>':
                            builder.Append("<noparse>></noparse>");
                            break;
                        default:
                            builder.Append(closingQuote);
                            break;
                    }
                }

                if (ghostTag != null)
                {
                    builder.Append("</color></i>");
                }
            }

            display.SetValueWithoutNotify(builder.ToString());
        }

        private void DrawWaves(MeshGenerationContext context)
        {
            if (!_hasWaves || _advance <= 0)
            {
                return;
            }

            Color errorColor = _palette.Error;
            foreach (CommandSyntaxSpan span in _spans)
            {
                if (span.Role is not (CommandSyntaxRole.Invalid or CommandSyntaxRole.UnknownCommand))
                {
                    continue;
                }

                float left = span.Start * _advance;
                float right = Mathf.Min(_label.contentRect.width, Math.Min(_regularColumns, span.End) * _advance);
                if (right <= left)
                {
                    continue;
                }

                int segments = Mathf.Clamp(Mathf.CeilToInt((right - left) / 2f), 1, 16000);
                float step = (right - left) / segments;
                float y = _label.contentRect.yMax - 2f;
                MeshWriteData mesh = context.Allocate((segments + 1) * 2, segments * 6);
                for (int index = 0; index <= segments; ++index)
                {
                    float x = left + index * step;
                    float wave = (index & 1) == 0 ? y : y + 1f;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, wave, Vertex.nearZ), tint = errorColor });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, wave + 1f, Vertex.nearZ), tint = errorColor });
                    if (index < segments)
                    {
                        ushort top = (ushort)(index * 2);
                        mesh.SetNextIndex(top);
                        mesh.SetNextIndex((ushort)(top + 2));
                        mesh.SetNextIndex((ushort)(top + 1));
                        mesh.SetNextIndex((ushort)(top + 2));
                        mesh.SetNextIndex((ushort)(top + 3));
                        mesh.SetNextIndex((ushort)(top + 1));
                    }
                }
            }
        }

        internal sealed class Palette
        {
            private static readonly CustomStyleProperty<Color> InkBodyProperty = new("--dxt-ink-body");
            private static readonly CustomStyleProperty<Color> InkProperty = new("--dxt-ink");
            private static readonly CustomStyleProperty<Color> MutedProperty = new("--dxt-ink-muted");
            private static readonly CustomStyleProperty<Color> NumberProperty = new("--dxt-syn-number");
            private static readonly CustomStyleProperty<Color> StringProperty = new("--dxt-syn-string");
            private static readonly CustomStyleProperty<Color> ChoiceProperty = new("--dxt-syn-choice");
            private static readonly CustomStyleProperty<Color> ErrorProperty = new("--dxt-error-text");
            private static readonly CustomStyleProperty<Color> LegacyInkProperty = new("--input-text-color");
            private static readonly CustomStyleProperty<Color> LegacyErrorProperty = new("--text-error");

            internal long Version
            {
                get { return _version; }
            }

            internal Color Ink
            {
                get { return _colors[(int)CommandSyntaxRole.Command]; }
            }

            internal Color Error
            {
                get { return _colors[(int)CommandSyntaxRole.Invalid]; }
            }

            internal string GhostTag
            {
                get { return _ghostTag; }
            }

            private readonly Color[] _colors = new Color[(int)CommandSyntaxRole.Invalid + 1];
            private readonly string[] _tags = new string[(int)CommandSyntaxRole.Invalid + 1];
            private string _ghostTag;
            private long _version;

            internal Palette()
            {
                ApplyColors(
                    new Color(230f / 255f, 237f / 255f, 243f / 255f),
                    new Color(201f / 255f, 210f / 255f, 220f / 255f),
                    new Color(154f / 255f, 163f / 255f, 175f / 255f),
                    new Color(108f / 255f, 182f / 255f, 255f / 255f),
                    new Color(250f / 255f, 192f / 255f, 98f / 255f),
                    new Color(194f / 255f, 245f / 255f, 216f / 255f),
                    new Color(255f / 255f, 122f / 255f, 142f / 255f)
                );
            }

            private static Color Read(ICustomStyle styles, CustomStyleProperty<Color> property, Color fallback)
            {
                return styles.TryGetValue(property, out Color color) ? color : fallback;
            }

            internal bool Apply(ICustomStyle styles)
            {
                Color ink = Read(styles, InkProperty, Read(styles, LegacyInkProperty, Color.white));
                Color error = Read(styles, ErrorProperty, Read(styles, LegacyErrorProperty, ink));
                return ApplyColors(
                    ink,
                    Read(styles, InkBodyProperty, ink),
                    Read(styles, MutedProperty, ink),
                    Read(styles, NumberProperty, ink),
                    Read(styles, StringProperty, ink),
                    Read(styles, ChoiceProperty, ink),
                    error
                );
            }

            internal void AppendHighlighted(StringBuilder builder, string line, List<CommandSyntaxSpan> spans, bool forceBold, string monoTag, int regularColumns)
            {
                int start = 0;
                int count = spans.Count;
                for (int i = 0; i < count; ++i)
                {
                    start = AppendSpan(builder, line, spans[i], start, forceBold, monoTag, regularColumns);
                }

                AppendLiteral(builder, line, start, line.Length);
            }

            internal string FormatLine(string line, List<CommandSyntaxSpan> spans)
            {
                if (string.IsNullOrEmpty(line))
                {
                    return string.Empty;
                }

                using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(line.Length + (spans.Count * 32));
                AppendHighlighted(scope.Builder, line, spans, forceBold: true, monoTag: null, regularColumns: int.MaxValue);
                return scope.Builder.ToString();
            }

            internal string FormatHelpSignature(CommandHelpCard card)
            {
                CommandHelpCard.SignaturePart[] parts = card.SignatureParts;
                using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(card.SignaturePlainText.Length + (parts.Length * 28));
                StringBuilder builder = scope.Builder;
                string commandTag = _tags[(int)CommandSyntaxRole.Command];
                string mutedTag = _tags[(int)CommandSyntaxRole.Punctuation];
                for (int i = 0; i < parts.Length; ++i)
                {
                    if (0 < i)
                    {
                        builder.Append(' ');
                    }

                    CommandHelpCard.SignaturePart part = parts[i];
                    string safeText = LogTextSanitizer.Sanitize(part.Text);
                    bool isCommand = part.Kind == CommandHelpCard.SignaturePartKind.Command;
                    builder.Append(part.Kind is CommandHelpCard.SignaturePartKind.Command or CommandHelpCard.SignaturePartKind.Required ? commandTag : mutedTag);
                    if (isCommand)
                    {
                        builder.Append("<b>");
                    }

                    AppendLiteral(builder, safeText, 0, safeText.Length);
                    builder.Append(isCommand ? "</b></color>" : "</color>");
                }

                return builder.ToString();
            }

            internal string FormatHelpExample(CommandHelpCard card)
            {
                string example = card.ExampleLine;
                if (string.IsNullOrEmpty(example))
                {
                    return string.Empty;
                }

                CommandSyntaxSpan[] spans = card.ExampleSpans ?? Array.Empty<CommandSyntaxSpan>();
                using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(example.Length + (spans.Length * 32) + 32);
                StringBuilder builder = scope.Builder;
                builder.Append(_tags[(int)CommandSyntaxRole.Punctuation]).Append("e.g. </color>");
                int start = 0;
                for (int i = 0; i < spans.Length; ++i)
                {
                    start = AppendSpan(builder, example, spans[i], start, forceBold: true, monoTag: null, regularColumns: int.MaxValue);
                }

                AppendLiteral(builder, example, start, example.Length);
                return builder.ToString();
            }

            private bool ApplyColors(Color ink, Color inkBody, Color muted, Color number, Color text, Color choice, Color error)
            {
                bool changed = SetRole(CommandSyntaxRole.Command, ink);
                changed |= SetRole(CommandSyntaxRole.UnknownCommand, error);
                changed |= SetRole(CommandSyntaxRole.Choice, choice);
                changed |= SetRole(CommandSyntaxRole.Number, number);
                changed |= SetRole(CommandSyntaxRole.String, text);
                changed |= SetRole(CommandSyntaxRole.Variable, inkBody);
                changed |= SetRole(CommandSyntaxRole.Punctuation, muted);
                changed |= SetRole(CommandSyntaxRole.Invalid, error);
                if (!changed)
                {
                    return false;
                }

                _ghostTag = "<i>" + _tags[(int)CommandSyntaxRole.Punctuation];
                ++_version;
                return true;
            }

            private bool SetRole(CommandSyntaxRole role, Color color)
            {
                int slot = (int)role;
                if (_tags[slot] != null && _colors[slot] == color)
                {
                    return false;
                }

                _colors[slot] = color;
                _tags[slot] = "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">";
                return true;
            }

            private int AppendSpan(StringBuilder builder, string line, in CommandSyntaxSpan span, int start, bool forceBold, string monoTag, int regularColumns)
            {
                AppendLiteral(builder, line, start, span.Start);
                int textStart = span.Start;
                bool italic = span.Role == CommandSyntaxRole.Variable && textStart < line.Length && line[textStart] == '$';
                if (italic)
                {
                    builder.Append(_tags[(int)CommandSyntaxRole.Punctuation]).Append("$</color>");
                    ++textStart;
                }

                string tag = _tags[(int)span.Role];
                builder.Append(tag);
                bool useMspace = !forceBold && !string.IsNullOrEmpty(monoTag);
                bool bold = span.Role is CommandSyntaxRole.Command or CommandSyntaxRole.Choice && (forceBold || (span.End <= regularColumns && useMspace));
                if (bold)
                {
                    if (useMspace)
                    {
                        // Synthetic bold adds advance in TextCore unless the span has fixed-width cells.
                        builder.Append(monoTag);
                    }

                    builder.Append("<b>");
                }

                if (italic)
                {
                    builder.Append("<i>");
                }

                AppendLiteral(builder, line, textStart, span.End);
                if (italic)
                {
                    builder.Append("</i>");
                }

                if (bold)
                {
                    builder.Append(useMspace ? "</b></mspace>" : "</b>");
                }

                if (tag != null)
                {
                    builder.Append("</color>");
                }

                return span.End;
            }
        }
    }
}
