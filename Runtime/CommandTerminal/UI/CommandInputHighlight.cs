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
#endif

    internal sealed class CommandInputHighlight
    {
        private const string NarrowGlyphs = "iiiiiiiiiiiiiiiiiiiiiiiiiiiiiiii";
        private const string WideGlyphs = "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW";

        private static readonly CustomStyleProperty<Color> InkBody = new("--dxt-ink-body");
        private static readonly CustomStyleProperty<Color> Ink = new("--dxt-ink");
        private static readonly CustomStyleProperty<Color> Muted = new("--dxt-ink-muted");
        private static readonly CustomStyleProperty<Color> Number = new("--dxt-syn-number");
        private static readonly CustomStyleProperty<Color> String = new("--dxt-syn-string");
        private static readonly CustomStyleProperty<Color> Choice = new("--dxt-syn-choice");
        private static readonly CustomStyleProperty<Color> Error = new("--dxt-error-text");
        private static readonly CustomStyleProperty<Color> LegacyInk = new("--input-text-color");
        private static readonly CustomStyleProperty<Color> LegacyError = new("--text-error");

        private readonly Label _label;
        private readonly VisualElement _nativeText;
        private readonly bool _nativeScrollFollowsLabel;
        private readonly CommandSyntaxClassifier _classifier = new();
        private readonly List<CommandSyntaxSpan> _spans = new();
        private readonly string[] _tags = new string[(int)CommandSyntaxRole.Invalid + 1];
        private string _line;
        private CommandShell _shell;
        private Color _error;
        private string _monoTag;
        private float _advance;
        private float _fontSize;
        private FontDefinition _font;
        private bool _paletteChanged = true;
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
            _nativeScrollFollowsLabel = _nativeText is TextElement;
            _label = new Label
            {
                name = "CommandInputHighlight",
                pickingMode = PickingMode.Ignore,
                enableRichText = true,
            };
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

        private static Color Read(ICustomStyle styles, CustomStyleProperty<Color> property, Color fallback)
        {
            return styles.TryGetValue(property, out Color color) ? color : fallback;
        }

        private static void AppendLiteral(StringBuilder builder, string line, int start, int end)
        {
            for (int index = start; index < end; ++index)
            {
                char value = line[index];
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

        internal void Refresh(string line, CommandShell shell)
        {
            if (!ReferenceEquals(_shell, shell) || !string.Equals(_line, line, StringComparison.Ordinal))
            {
                _shell = shell;
                _line = line;
                _spans.Clear();
                if (shell != null && !string.IsNullOrEmpty(line))
                {
                    _classifier.Classify(line, shell, _spans);
                }

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

                _paletteChanged = true;
            }

            IResolvedStyle nativeStyle = _nativeText.resolvedStyle;
            if (_geometryChanged || !_fontSize.Equals(nativeStyle.fontSize) || !_font.Equals(nativeStyle.unityFontDefinition))
            {
                RefreshGeometry(nativeStyle);
            }

            if (_paletteChanged)
            {
                WriteLabel();
            }

            bool visible = !IsComposing() && !string.IsNullOrEmpty(line) && !_hasLineBreak;

            if (visible && !_nativeScrollFollowsLabel)
            {
                // Unity 2021 keeps the single-line scroll offset inside its text editor.
                visible = line.Length == _regularColumns && 0 < _advance && _advance * line.Length < _nativeText.contentRect.width;
            }

            if (_visible != visible)
            {
                _visible = visible;
                _nativeText.style.color = visible ? new StyleColor(Color.clear) : new StyleColor(StyleKeyword.Null);
                _label.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private bool IsComposing()
        {
#if ENABLE_LEGACY_INPUT_MANAGER || ENABLE_INPUT_SYSTEM
            bool focused = _nativeText.focusController?.focusedElement == _nativeText;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return focused && !string.IsNullOrEmpty(UnityEngine.Input.compositionString);
#elif ENABLE_INPUT_SYSTEM
            SetKeyboard(focused ? Keyboard.current : null);
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
            ICustomStyle styles = evt.customStyle;
            Color ink = Read(styles, Ink, Read(styles, LegacyInk, Color.white));
            Color muted = Read(styles, Muted, ink);
            Color choice = Read(styles, Choice, ink);
            _error = Read(styles, Error, Read(styles, LegacyError, ink));
            _label.style.color = ink;
            SetTag(CommandSyntaxRole.Command, ink);
            SetTag(CommandSyntaxRole.UnknownCommand, _error);
            SetTag(CommandSyntaxRole.Choice, choice);
            SetTag(CommandSyntaxRole.Number, Read(styles, Number, ink));
            SetTag(CommandSyntaxRole.String, Read(styles, String, ink));
            SetTag(CommandSyntaxRole.Variable, Read(styles, InkBody, ink));
            SetTag(CommandSyntaxRole.Punctuation, muted);
            SetTag(CommandSyntaxRole.Invalid, _error);
            _paletteChanged = true;
            _geometryChanged = true;
        }

        private void SetTag(CommandSyntaxRole role, Color color)
        {
            _tags[(int)role] = "<color=#" + ColorUtility.ToHtmlStringRGBA(color) + ">";
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
                _paletteChanged = true;
            }

            _label.MarkDirtyRepaint();
        }

        private float MeasureWidth(string sample)
        {
            return _label.MeasureTextSize(sample, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
        }

        private void WriteLabel()
        {
            _paletteChanged = false;
            if (string.IsNullOrEmpty(_line))
            {
                _label.text = string.Empty;
                return;
            }

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(_line.Length + (_spans.Count * 32));
            StringBuilder builder = scope.Builder;
            int start = 0;
            foreach (CommandSyntaxSpan span in _spans)
            {
                AppendLiteral(builder, _line, start, span.Start);
                int textStart = span.Start;
                bool italic = span.Role == CommandSyntaxRole.Variable && _line[textStart] == '$';
                if (italic)
                {
                    builder.Append(_tags[(int)CommandSyntaxRole.Punctuation]);
                    builder.Append("$</color>");
                    ++textStart;
                }

                string tag = _tags[(int)span.Role];
                builder.Append(tag);
                bool bold = span.End <= _regularColumns && 0 < _advance && span.Role is CommandSyntaxRole.Command or CommandSyntaxRole.Choice;
                if (bold)
                {
                    // Synthetic bold adds advance in TextCore unless the span has fixed-width cells.
                    builder.Append(_monoTag);
                    builder.Append("<b>");
                }

                if (italic)
                {
                    builder.Append("<i>");
                }

                AppendLiteral(builder, _line, textStart, span.End);
                if (italic)
                {
                    builder.Append("</i>");
                }

                if (bold)
                {
                    builder.Append("</b></mspace>");
                }

                if (tag != null)
                {
                    builder.Append("</color>");
                }

                start = span.End;
            }

            AppendLiteral(builder, _line, start, _line.Length);
            _label.text = builder.ToString();
        }

        private void DrawWaves(MeshGenerationContext context)
        {
            if (!_hasWaves || _advance <= 0)
            {
                return;
            }

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
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, wave, Vertex.nearZ), tint = _error });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, wave + 1f, Vertex.nearZ), tint = _error });
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
    }
}
