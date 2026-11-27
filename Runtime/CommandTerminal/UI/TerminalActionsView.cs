namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Backend;
    using UnityEngine;
    using UnityEngine.UIElements;
    using ActionCard = TerminalActionsModel.ActionCard;
    using ActionControlKind = TerminalActionsModel.ActionControlKind;
    using ActionField = TerminalActionsModel.ActionField;
    using ActionGroup = TerminalActionsModel.ActionGroup;
    using RecentEntry = TerminalActionsModel.RecentEntry;

    /*
        UI Toolkit view for the Actions tab (#243): left group sidebar (Recent
        plus command groups) and right card list with typed controls, syntax-
        colored preview line, unavailability reason, and Run button.
     */
    internal sealed class TerminalActionsView
    {
        internal VisualElement Root
        {
            get { return _root; }
        }

        private readonly VisualElement _root;
        private readonly ScrollView _sidebar;
        private readonly ScrollView _cardsScroll;
        private readonly Action<string, string, string[]> _onRun;
        private readonly Func<string, string> _formatPreview;
        private readonly List<ActionGroup> _groups = new();
        private readonly List<RecentEntry> _recent = new();
        private readonly List<Button> _groupButtons = new();
        private readonly List<CardBinding> _cardBindings = new();
        private readonly List<CommandCompletion> _dynamicBuffer = new();
        private readonly List<CommandArg> _precedingArgs = new();

        private string _selectedGroup;
        private CommandShell _lastShell;
        private long _lastCommandsVersion = -1;
        private CommandTier _lastTier;
        private CommandExecutionContexts _lastEnvironment;
        private int _recentVersion;
        private int _lastRecentVersion = -1;
        private long _lastPaletteVersion = -1;
        private bool _visible;

        internal TerminalActionsView(VisualElement parent, Action<string, string, string[]> onRun, Func<string, string> formatPreview)
        {
            _onRun = onRun;
            _formatPreview = formatPreview;

            _root = new VisualElement { name = "TerminalActionsView" };
            _root.AddToClassList("terminal-actions");
            _root.style.display = DisplayStyle.None;

            _sidebar = new ScrollView { name = "ActionsGroupSidebar" };
            _sidebar.AddToClassList("terminal-actions-sidebar");
            DisableScrollerFocus(_sidebar);
            _root.Add(_sidebar);

            _cardsScroll = new ScrollView { name = "ActionsCardsScroll" };
            _cardsScroll.AddToClassList("terminal-actions-cards");
            DisableScrollerFocus(_cardsScroll);
            _root.Add(_cardsScroll);

            parent.Add(_root);
        }

        private static void DisableScrollerFocus(ScrollView scrollView)
        {
            scrollView.focusable = false;
            DisableScroller(scrollView.verticalScroller);
            DisableScroller(scrollView.horizontalScroller);
        }

        private static void DisableScroller(Scroller scroller)
        {
            if (scroller == null)
            {
                return;
            }

            scroller.focusable = false;
            if (scroller.slider != null)
            {
                scroller.slider.focusable = false;
            }

            if (scroller.lowButton != null)
            {
                scroller.lowButton.focusable = false;
            }

            if (scroller.highButton != null)
            {
                scroller.highButton.focusable = false;
            }
        }

        private static string FormatMenuLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(select) ▾" : $"{value} ▾";
        }

        private static string[] SplitComponents(string value, int count)
        {
            string[] result = new string[count];
            for (int i = 0; i < count; ++i)
            {
                result[i] = "0";
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            string trimmed = value.Trim();
            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal) && 2 <= trimmed.Length)
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2);
            }

            string[] tokens = trimmed.Split(',');
            int limit = Math.Min(count, tokens.Length);
            for (int i = 0; i < limit; ++i)
            {
                result[i] = tokens[i].Trim();
            }

            return result;
        }

        private static string JoinComponents(TextField[] inputs)
        {
            int count = inputs.Length;
            string[] parts = new string[count];
            for (int i = 0; i < count; ++i)
            {
                string text = inputs[i].value;
                parts[i] = string.IsNullOrWhiteSpace(text) ? "0" : text.Trim();
            }

            return $"({string.Join(",", parts)})";
        }

        internal void SetVisible(bool visible)
        {
            _visible = visible;
            _root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        internal void RecordRecent(string commandPath, string commandLine, string[] fieldValues)
        {
            TerminalActionsModel.RecordRecent(_recent, commandPath, commandLine, fieldValues);
            ++_recentVersion;
        }

        internal void RefreshIfNeeded(CommandShell shell, CommandExecutionContext context, long paletteVersion)
        {
            if (!_visible || shell == null)
            {
                return;
            }

            long commandsVersion = shell.CommandsVersion;
            CommandTier tier = shell.ActiveTier;
            CommandExecutionContexts env = context.Environment;
            bool registryChanged = !ReferenceEquals(_lastShell, shell) || _lastCommandsVersion != commandsVersion || _lastTier != tier || _lastEnvironment != env;
            bool recentChanged = _lastRecentVersion != _recentVersion;

            if (registryChanged || recentChanged)
            {
                _lastShell = shell;
                _lastCommandsVersion = commandsVersion;
                _lastTier = tier;
                _lastEnvironment = env;
                _lastRecentVersion = _recentVersion;
                _lastPaletteVersion = paletteVersion;
                bool preserveCards = !registryChanged && !string.Equals(_selectedGroup, TerminalActionsModel.RecentGroupName, StringComparison.OrdinalIgnoreCase);
                Rebuild(shell, context, tier, preserveCards);
                return;
            }

            if (_lastPaletteVersion != paletteVersion)
            {
                _lastPaletteVersion = paletteVersion;
                RefreshPreviews();
            }
        }

        internal void FocusInitialControl()
        {
            int count = _groupButtons.Count;
            for (int i = 0; i < count; ++i)
            {
                if (_groupButtons[i].ClassListContains("terminal-actions-group--active"))
                {
                    _groupButtons[i].Focus();
                    return;
                }
            }

            if (0 < count)
            {
                _groupButtons[0].Focus();
            }
        }

        internal bool FocusActiveCardFirstField()
        {
            VisualElement focused = _root.panel?.focusController?.focusedElement as VisualElement;
            int bindingCount = _cardBindings.Count;
            CardBinding target = null;
            if (focused != null)
            {
                for (int i = 0; i < bindingCount; ++i)
                {
                    if (_cardBindings[i].CardElement.Contains(focused))
                    {
                        target = _cardBindings[i];
                        break;
                    }
                }
            }

            if (target == null && 0 < bindingCount)
            {
                target = _cardBindings[0];
            }

            if (target == null)
            {
                return false;
            }

            VisualElement control = target.FirstFocusableControl ?? target.RunButton;
            if (control != null && control.enabledSelf)
            {
                control.Focus();
                return true;
            }

            return false;
        }

        internal bool TryHandleCancel(VisualElement focused)
        {
            if (focused == null || !_cardsScroll.Contains(focused))
            {
                return false;
            }

            /*
                Only trap Cancel when the user is actively editing inside a
                TextField; pressing Cancel on a button, toggle, or sidebar
                item lets TerminalUI close the console in one press (T7).
             */
            VisualElement cursor = focused;
            while (cursor != null && cursor != _cardsScroll)
            {
                if (cursor is TextField)
                {
                    FocusInitialControl();
                    return true;
                }

                cursor = cursor.parent;
            }

            return false;
        }

        private void Rebuild(CommandShell shell, CommandExecutionContext context, CommandTier tier, bool preserveCards = false)
        {
            TerminalActionsModel.BuildGroups(shell, context, tier, _recent, _groups);
            _sidebar.Clear();
            _groupButtons.Clear();

            int groupCount = _groups.Count;
            if (groupCount == 0)
            {
                _selectedGroup = null;
                _cardsScroll.Clear();
                _cardBindings.Clear();
                return;
            }

            bool hasSelected = false;
            for (int i = 0; i < groupCount; ++i)
            {
                if (string.Equals(_groups[i].Name, _selectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    hasSelected = true;
                    break;
                }
            }

            if (!hasSelected)
            {
                _selectedGroup = _groups[0].Name;
                preserveCards = false;
            }

            for (int i = 0; i < groupCount; ++i)
            {
                ActionGroup group = _groups[i];
                string groupName = group.Name;
                Button button = new Button(() =>
                {
                    SelectGroup(groupName, shell, context);
                })
                {
                    name = $"ActionGroup-{groupName}",
                    text = $"{groupName} ({group.Cards.Count})",
                };
                button.AddToClassList("terminal-actions-group");
                if (string.Equals(groupName, _selectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    button.AddToClassList("terminal-actions-group--active");
                }

                _sidebar.Add(button);
                _groupButtons.Add(button);
            }

            if (!preserveCards)
            {
                RebuildCards(shell, context);
            }
        }

        private void SelectGroup(string groupName, CommandShell shell, CommandExecutionContext context)
        {
            if (string.Equals(_selectedGroup, groupName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedGroup = groupName;
            int count = _groupButtons.Count;
            for (int i = 0; i < count; ++i)
            {
                bool active = string.Equals(_groups[i].Name, _selectedGroup, StringComparison.OrdinalIgnoreCase);
                _groupButtons[i].EnableInClassList("terminal-actions-group--active", active);
            }

            RebuildCards(shell, context);
        }

        private void RebuildCards(CommandShell shell, CommandExecutionContext context)
        {
            _cardsScroll.Clear();
            _cardBindings.Clear();

            ActionGroup activeGroup = null;
            int groupCount = _groups.Count;
            for (int i = 0; i < groupCount; ++i)
            {
                if (string.Equals(_groups[i].Name, _selectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    activeGroup = _groups[i];
                    break;
                }
            }

            if (activeGroup == null)
            {
                return;
            }

            List<ActionCard> cards = activeGroup.Cards;
            int cardCount = cards.Count;
            for (int i = 0; i < cardCount; ++i)
            {
                CardBinding binding = BuildCard(cards[i], shell, context);
                _cardBindings.Add(binding);
                _cardsScroll.Add(binding.CardElement);
            }
        }

        private CardBinding BuildCard(ActionCard card, CommandShell shell, CommandExecutionContext context)
        {
            VisualElement cardElement = new() { name = $"ActionCard-{card.CommandPath}" };
            cardElement.AddToClassList("terminal-actions-card");
            if (card.IsUnavailable)
            {
                cardElement.AddToClassList("terminal-actions-card--unavailable");
            }

            VisualElement header = new();
            header.AddToClassList("terminal-actions-card-header");
            Label title = new(card.CommandPath) { name = "ActionCardTitle" };
            title.AddToClassList("terminal-actions-card-title");
            header.Add(title);

            if (!string.IsNullOrWhiteSpace(card.Description))
            {
                Label desc = new(card.Description) { name = "ActionCardDescription" };
                desc.AddToClassList("terminal-actions-card-desc");
                header.Add(desc);
            }

            cardElement.Add(header);

            if (card.IsUnavailable)
            {
                Label reason = new(card.UnavailableReason) { name = "ActionUnavailableReason" };
                reason.AddToClassList("terminal-actions-unavailable-reason");
                cardElement.Add(reason);
            }

            int fieldCount = card.Fields.Length;
            string[] values = new string[fieldCount];
            for (int i = 0; i < fieldCount; ++i)
            {
                values[i] = card.Fields[i].InitialValue;
            }

            Label previewLabel = new() { name = "ActionPreviewLine", enableRichText = true };
            previewLabel.AddToClassList("terminal-actions-preview");

            Button runButton = new() { name = "ActionRunButton", text = "Run" };
            runButton.AddToClassList("terminal-button");
            runButton.AddToClassList("terminal-actions-run");

            CardBinding binding = new(card, cardElement, values, previewLabel, runButton);
            runButton.clicked += () =>
            {
                if (TerminalActionsModel.CanRun(binding.Card, binding.Values))
                {
                    string line = TerminalActionsModel.BuildCommandLine(binding.Card, binding.Values);
                    _onRun(binding.Card.CommandPath, line, binding.Values);
                }
            };

            if (0 < fieldCount)
            {
                VisualElement fieldsRoot = new();
                fieldsRoot.AddToClassList("terminal-actions-fields");
                for (int i = 0; i < fieldCount; ++i)
                {
                    VisualElement fieldRow = BuildFieldRow(binding, i, shell, context, out VisualElement focusableControl);
                    if (binding.FirstFocusableControl == null && focusableControl != null)
                    {
                        binding.FirstFocusableControl = focusableControl;
                    }

                    fieldsRoot.Add(fieldRow);
                }

                cardElement.Add(fieldsRoot);
            }

            VisualElement footer = new();
            footer.AddToClassList("terminal-actions-card-footer");
            footer.Add(previewLabel);
            footer.Add(runButton);
            cardElement.Add(footer);

            UpdateCardPreviewAndRun(binding);
            return binding;
        }

        private VisualElement BuildFieldRow(CardBinding binding, int index, CommandShell shell, CommandExecutionContext context, out VisualElement focusableControl)
        {
            ActionField field = binding.Card.Fields[index];
            VisualElement row = new();
            row.AddToClassList("terminal-actions-field");

            switch (field.Kind)
            {
                case ActionControlKind.Toggle:
                    focusableControl = BuildToggleControl(binding, index, field, row);
                    break;
                case ActionControlKind.Segmented:
                    focusableControl = BuildSegmentedControl(binding, index, field, row);
                    break;
                case ActionControlKind.Menu:
                    focusableControl = BuildMenuControl(binding, index, field, shell, context, row);
                    break;
                case ActionControlKind.RangeNumber:
                    focusableControl = BuildRangeControl(binding, index, field, row);
                    break;
                case ActionControlKind.Vector2:
                    focusableControl = BuildComponentControl(binding, index, field, row, new[] { "X", "Y" });
                    break;
                case ActionControlKind.Vector3:
                    focusableControl = BuildComponentControl(binding, index, field, row, new[] { "X", "Y", "Z" });
                    break;
                case ActionControlKind.Vector4:
                    focusableControl = BuildComponentControl(binding, index, field, row, new[] { "X", "Y", "Z", "W" });
                    break;
                case ActionControlKind.Color:
                    focusableControl = BuildComponentControl(binding, index, field, row, new[] { "R", "G", "B", "A" });
                    break;
                case ActionControlKind.SceneObject:
                    focusableControl = BuildSceneObjectControl(binding, index, field, shell, context, row);
                    break;
                default:
                    focusableControl = BuildTextControl(binding, index, field, row);
                    break;
            }

            if (!string.IsNullOrWhiteSpace(field.Description))
            {
                Label desc = new(field.Description);
                desc.AddToClassList("terminal-actions-field-desc");
                row.Add(desc);
            }

            if (binding.Card.IsUnavailable)
            {
                row.SetEnabled(false);
            }

            return row;
        }

        private VisualElement BuildToggleControl(CardBinding binding, int index, ActionField field, VisualElement row)
        {
            Toggle toggle = new(field.Name)
            {
                name = $"ActionField-{field.Name}",
                value = string.Equals(binding.Values[index], "true", StringComparison.OrdinalIgnoreCase),
                tooltip = field.Description ?? field.Name,
            };
            toggle.AddToClassList("terminal-actions-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                binding.Values[index] = evt.newValue ? "true" : "false";
                UpdateCardPreviewAndRun(binding);
            });
            row.Add(toggle);
            return toggle;
        }

        private VisualElement BuildSegmentedControl(CardBinding binding, int index, ActionField field, VisualElement row)
        {
            Label label = new(field.Name);
            label.AddToClassList("terminal-actions-field-label");
            row.Add(label);

            VisualElement segmentRow = new();
            segmentRow.AddToClassList("terminal-actions-segmented");
            string[] choices = field.StaticChoices;
            int choiceCount = choices.Length;
            Button[] buttons = new Button[choiceCount];
            for (int i = 0; i < choiceCount; ++i)
            {
                string choice = choices[i];
                Button segment = new() { name = $"ActionField-{field.Name}-{choice}", text = choice };
                segment.AddToClassList("terminal-button");
                segment.AddToClassList("terminal-actions-segment");
                segment.EnableInClassList("terminal-actions-segment--selected", string.Equals(binding.Values[index], choice, StringComparison.OrdinalIgnoreCase));
                segment.clicked += () =>
                {
                    binding.Values[index] = choice;
                    for (int j = 0; j < choiceCount; ++j)
                    {
                        buttons[j].EnableInClassList("terminal-actions-segment--selected", string.Equals(choices[j], choice, StringComparison.OrdinalIgnoreCase));
                    }

                    UpdateCardPreviewAndRun(binding);
                };
                buttons[i] = segment;
                segmentRow.Add(segment);
            }

            row.Add(segmentRow);
            return 0 < choiceCount ? buttons[0] : null;
        }

        private VisualElement BuildMenuControl(CardBinding binding, int index, ActionField field, CommandShell shell, CommandExecutionContext context, VisualElement row)
        {
            Label label = new(field.Name);
            label.AddToClassList("terminal-actions-field-label");
            row.Add(label);

            string[] choices = ResolveMenuChoices(binding, index, field, shell, context);
            if (string.IsNullOrWhiteSpace(binding.Values[index]) && 0 < choices.Length)
            {
                binding.Values[index] = choices[0];
            }

            Button menuButton = new() { name = $"ActionField-{field.Name}", text = FormatMenuLabel(binding.Values[index]) };
            menuButton.AddToClassList("terminal-button");
            menuButton.AddToClassList("terminal-actions-menu");
            menuButton.clicked += () =>
            {
                string[] liveChoices = field.Spec != null && field.Spec.HasDynamicChoices ? ResolveMenuChoices(binding, index, field, shell, context) : choices;
                if (liveChoices.Length == 0)
                {
                    return;
                }

                int nextIndex = 0;
                int count = liveChoices.Length;
                for (int i = 0; i < count; ++i)
                {
                    if (string.Equals(liveChoices[i], binding.Values[index], StringComparison.OrdinalIgnoreCase))
                    {
                        nextIndex = (i + 1) % count;
                        break;
                    }
                }

                binding.Values[index] = liveChoices[nextIndex];
                menuButton.text = FormatMenuLabel(binding.Values[index]);
                UpdateCardPreviewAndRun(binding);
            };
            row.Add(menuButton);
            return menuButton;
        }

        private VisualElement BuildRangeControl(CardBinding binding, int index, ActionField field, VisualElement row)
        {
            if (field.IsIntegral && (field.RangeMin < int.MinValue || int.MaxValue < field.RangeMax))
            {
                return BuildTextControl(binding, index, field, row);
            }

            Label label = new(field.Name);
            label.AddToClassList("terminal-actions-field-label");
            row.Add(label);

            VisualElement rangeRow = new();
            rangeRow.AddToClassList("terminal-actions-range");

            TextField numberField = new() { name = $"ActionField-{field.Name}", value = binding.Values[index] };
            numberField.AddToClassList("terminal-actions-number");

            if (field.IsIntegral)
            {
                int min = (int)Math.Round(field.RangeMin);
                int max = (int)Math.Round(field.RangeMax);
                int initial = int.TryParse(binding.Values[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? Math.Clamp(parsed, min, max) : min;
                SliderInt slider = new(min, max) { name = $"ActionSlider-{field.Name}", value = initial };
                slider.AddToClassList("terminal-actions-slider");
                slider.RegisterValueChangedCallback(evt =>
                {
                    string text = evt.newValue.ToString(CultureInfo.InvariantCulture);
                    binding.Values[index] = text;
                    numberField.SetValueWithoutNotify(text);
                    UpdateCardPreviewAndRun(binding);
                });
                numberField.RegisterValueChangedCallback(evt =>
                {
                    binding.Values[index] = evt.newValue;
                    if (int.TryParse(evt.newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int typed))
                    {
                        slider.SetValueWithoutNotify(Math.Clamp(typed, min, max));
                    }

                    UpdateCardPreviewAndRun(binding);
                });
                rangeRow.Add(slider);
                rangeRow.Add(numberField);
                row.Add(rangeRow);
                return slider;
            }
            else
            {
                float min = (float)field.RangeMin;
                float max = (float)field.RangeMax;
                float initial = float.TryParse(binding.Values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? Math.Clamp(parsed, min, max) : min;
                Slider slider = new(min, max) { name = $"ActionSlider-{field.Name}", value = initial };
                slider.AddToClassList("terminal-actions-slider");
                slider.RegisterValueChangedCallback(evt =>
                {
                    string text = evt.newValue.ToString("G", CultureInfo.InvariantCulture);
                    binding.Values[index] = text;
                    numberField.SetValueWithoutNotify(text);
                    UpdateCardPreviewAndRun(binding);
                });
                numberField.RegisterValueChangedCallback(evt =>
                {
                    binding.Values[index] = evt.newValue;
                    if (float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
                    {
                        slider.SetValueWithoutNotify(Math.Clamp(typed, min, max));
                    }

                    UpdateCardPreviewAndRun(binding);
                });
                rangeRow.Add(slider);
                rangeRow.Add(numberField);
                row.Add(rangeRow);
                return slider;
            }
        }

        private VisualElement BuildComponentControl(CardBinding binding, int index, ActionField field, VisualElement row, string[] labels)
        {
            Label title = new(field.Name);
            title.AddToClassList("terminal-actions-field-label");
            row.Add(title);

            VisualElement components = new();
            components.AddToClassList("terminal-actions-components");
            int count = labels.Length;
            TextField[] inputs = new TextField[count];
            string[] parts = SplitComponents(binding.Values[index], count);

            for (int i = 0; i < count; ++i)
            {
                TextField partField = new(labels[i]) { name = $"ActionField-{field.Name}-{labels[i]}", value = parts[i] };
                partField.AddToClassList("terminal-actions-component-input");
                partField.RegisterValueChangedCallback(_ =>
                {
                    binding.Values[index] = JoinComponents(inputs);
                    UpdateCardPreviewAndRun(binding);
                });
                inputs[i] = partField;
                components.Add(partField);
            }

            row.Add(components);
            return inputs[0];
        }

        private VisualElement BuildSceneObjectControl(CardBinding binding, int index, ActionField field, CommandShell shell, CommandExecutionContext context, VisualElement row)
        {
            VisualElement pickerRow = new();
            pickerRow.AddToClassList("terminal-actions-range");

            TextField searchField = new(field.Name) { name = $"ActionField-{field.Name}", value = binding.Values[index] };
            searchField.AddToClassList("terminal-actions-text");
            searchField.RegisterValueChangedCallback(evt =>
            {
                binding.Values[index] = evt.newValue;
                UpdateCardPreviewAndRun(binding);
            });

            Button pickButton = new() { name = $"ActionPick-{field.Name}", text = "Pick" };
            pickButton.AddToClassList("terminal-button");
            pickButton.AddToClassList("terminal-actions-menu");
            pickButton.clicked += () =>
            {
                string[] choices = ResolveMenuChoices(binding, index, field, shell, context);
                if (choices.Length == 0)
                {
                    return;
                }

                int nextIndex = 0;
                int count = choices.Length;
                for (int i = 0; i < count; ++i)
                {
                    if (string.Equals(choices[i], binding.Values[index], StringComparison.OrdinalIgnoreCase))
                    {
                        nextIndex = (i + 1) % count;
                        break;
                    }
                }

                binding.Values[index] = choices[nextIndex];
                searchField.SetValueWithoutNotify(choices[nextIndex]);
                UpdateCardPreviewAndRun(binding);
            };

            pickerRow.Add(searchField);
            pickerRow.Add(pickButton);
            row.Add(pickerRow);
            return searchField;
        }

        private VisualElement BuildTextControl(CardBinding binding, int index, ActionField field, VisualElement row)
        {
            TextField textField = new(field.Name) { name = $"ActionField-{field.Name}", value = binding.Values[index] };
            textField.AddToClassList("terminal-actions-text");
            textField.RegisterValueChangedCallback(evt =>
            {
                binding.Values[index] = evt.newValue;
                UpdateCardPreviewAndRun(binding);
            });
            row.Add(textField);
            return textField;
        }

        private string[] ResolveMenuChoices(CardBinding binding, int fieldIndex, ActionField field, CommandShell shell, CommandExecutionContext context)
        {
            if (field.StaticChoices != null && 0 < field.StaticChoices.Length)
            {
                return field.StaticChoices;
            }

            if (field.Spec == null || shell == null)
            {
                return Array.Empty<string>();
            }

            _precedingArgs.Clear();
            int precedingCount = Math.Min(fieldIndex, binding.Values.Length);
            for (int i = 0; i < precedingCount; ++i)
            {
                _precedingArgs.Add(new CommandArg(binding.Values[i] ?? string.Empty));
            }

            string line = TerminalActionsModel.BuildCommandLine(binding.Card, binding.Values);
            _dynamicBuffer.Clear();
            CommandCompletionContext completionContext = new(
                context,
                line,
                line.Length,
                fieldIndex,
                _precedingArgs,
                string.Empty,
                line.Length,
                0,
                isQuoted: false,
                quoteCharacter: null
            );
            field.Spec.AppendCompletions(in completionContext, _dynamicBuffer);
            int count = _dynamicBuffer.Count;
            if (count == 0)
            {
                return Array.Empty<string>();
            }

            string[] results = new string[count];
            for (int i = 0; i < count; ++i)
            {
                results[i] = _dynamicBuffer[i].InsertionText;
            }

            return results;
        }

        private void UpdateCardPreviewAndRun(CardBinding binding)
        {
            string line = TerminalActionsModel.BuildCommandLine(binding.Card, binding.Values);
            binding.PreviewLabel.text = _formatPreview != null ? _formatPreview(line) : line;
            binding.RunButton.SetEnabled(TerminalActionsModel.CanRun(binding.Card, binding.Values));
        }

        private void RefreshPreviews()
        {
            int count = _cardBindings.Count;
            for (int i = 0; i < count; ++i)
            {
                UpdateCardPreviewAndRun(_cardBindings[i]);
            }
        }

        private sealed class CardBinding
        {
            public ActionCard Card { get; }
            public VisualElement CardElement { get; }
            public string[] Values { get; }
            public Label PreviewLabel { get; }
            public Button RunButton { get; }
            public VisualElement FirstFocusableControl { get; set; }

            public CardBinding(ActionCard card, VisualElement cardElement, string[] values, Label previewLabel, Button runButton)
            {
                Card = card;
                CardElement = cardElement;
                Values = values;
                PreviewLabel = previewLabel;
                RunButton = runButton;
            }
        }
    }
}
