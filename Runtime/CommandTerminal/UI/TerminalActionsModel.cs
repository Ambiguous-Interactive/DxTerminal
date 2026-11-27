namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Backend;
    using Extensions;
    using Helper;
    using UnityEngine;

    /*
        Pure-logic model for the Actions tab (#243): projects the shell's
        command registry into grouped cards, maps each CommandArgument spec
        to its control kind, formats tier/context unavailability reasons,
        and builds quoted command lines matching typed terminal input.
     */
    internal static class TerminalActionsModel
    {
        internal const string RecentGroupName = "Recent";
        internal const string DefaultBuilderGroupName = "Commands";
        internal const int MaxRecentEntries = 4;
        internal const int MaxSegmentedChoices = 4;

        private static readonly Comparison<ActionGroup> GroupComparer = CompareGroups;
        private static readonly Comparison<ActionCard> CardComparer = CompareCards;

        internal static void BuildGroups(CommandShell shell, CommandExecutionContext context, CommandTier activeTier, IReadOnlyList<RecentEntry> recent, List<ActionGroup> output)
        {
            output.Clear();
            if (shell == null)
            {
                return;
            }

            shell.EnsureAutoCommandsRegistered();
            Dictionary<string, List<ActionCard>> byGroup = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ActionCard> cardsByPath = new(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, CommandInfo> pair in shell.CommandsSorted)
            {
                CommandInfo info = pair.Value;
                if (info.hideFromActions)
                {
                    continue;
                }

                string groupName = ResolveGroupName(info);
                string unavailableReason = FormatUnavailableReason(info, context, activeTier);

                if (!byGroup.TryGetValue(groupName, out List<ActionCard> groupCards))
                {
                    groupCards = new List<ActionCard>();
                    byGroup.Add(groupName, groupCards);
                }

                if (info.SyntaxRoute != null)
                {
                    CollectRouteCards(info.SyntaxRoute, groupName, unavailableReason, groupCards, cardsByPath);
                }
                else
                {
                    ActionCard card = BuildLegacyCard(pair.Key, info, groupName, unavailableReason);
                    groupCards.Add(card);
                    cardsByPath[card.CommandPath] = card;
                }
            }

            if (recent != null && 0 < recent.Count)
            {
                List<ActionCard> recentCards = new(recent.Count);
                int recentCount = recent.Count;
                for (int i = 0; i < recentCount; ++i)
                {
                    RecentEntry entry = recent[i];
                    if (cardsByPath.TryGetValue(entry.CommandPath, out ActionCard match))
                    {
                        recentCards.Add(match);
                    }
                }

                if (0 < recentCards.Count)
                {
                    output.Add(new ActionGroup(RecentGroupName, recentCards, isRecent: true));
                }
            }

            List<ActionGroup> namedGroups = new(byGroup.Count);
            foreach (KeyValuePair<string, List<ActionCard>> pair in byGroup)
            {
                if (pair.Value.Count == 0)
                {
                    continue;
                }

                pair.Value.Sort(CardComparer);
                namedGroups.Add(new ActionGroup(pair.Key, pair.Value, isRecent: false));
            }

            namedGroups.Sort(GroupComparer);
            int namedCount = namedGroups.Count;
            for (int i = 0; i < namedCount; ++i)
            {
                output.Add(namedGroups[i]);
            }
        }

        internal static ActionControlKind ClassifyField(CommandArgument spec)
        {
            if (spec == null)
            {
                return ActionControlKind.Text;
            }

            if (spec.IsRemaining)
            {
                return ActionControlKind.Remaining;
            }

            Type type = spec.DeclaredType;
            if (type == typeof(bool))
            {
                return ActionControlKind.Toggle;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return ActionControlKind.SceneObject;
            }

            if (spec.HasDynamicChoices)
            {
                return ActionControlKind.Menu;
            }

            string[] staticChoices = spec.StaticChoiceTexts;
            if (staticChoices != null && 0 < staticChoices.Length)
            {
                if (type.IsEnum || MaxSegmentedChoices < staticChoices.Length)
                {
                    return ActionControlKind.Menu;
                }

                return ActionControlKind.Segmented;
            }

            if (type.IsEnum)
            {
                return ActionControlKind.Menu;
            }

            if (type == typeof(Vector2))
            {
                return ActionControlKind.Vector2;
            }

            if (type == typeof(Vector3))
            {
                return ActionControlKind.Vector3;
            }

            if (type == typeof(Vector4))
            {
                return ActionControlKind.Vector4;
            }

            if (type == typeof(Color))
            {
                return ActionControlKind.Color;
            }

            if (IsNumericType(type))
            {
                return spec.TryGetNumericRange(out _, out _) ? ActionControlKind.RangeNumber : ActionControlKind.Number;
            }

            return ActionControlKind.Text;
        }

        internal static bool IsIntegralType(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(sbyte)
                || type == typeof(byte)
                || type == typeof(ushort)
                || type == typeof(uint)
                || type == typeof(ulong);
        }

        internal static bool IsNumericType(Type type)
        {
            return IsIntegralType(type) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        }

        internal static string FormatUnavailableReason(CommandInfo info, CommandExecutionContext context, CommandTier activeTier)
        {
            CommandTier normalizedActive = NormalizeTier(activeTier);
            if ((int)normalizedActive < (int)info.tier)
            {
                string requiredTiers = info.tier == CommandTier.Tester ? "Tester and Developer" : "Developer";
                return $"Unavailable in {normalizedActive} tier builds. {requiredTiers} only.";
            }

            if (!context.IsEligibleFor(info.executionContexts))
            {
                string currentEnv = FormatEnvironmentName(context.Environment);
                string allowedEnvs = FormatAllowedEnvironments(info.executionContexts);
                return $"Unavailable in {currentEnv}. {allowedEnvs} only.";
            }

            return null;
        }

        internal static bool CanRun(ActionCard card, IReadOnlyList<string> fieldValues)
        {
            if (card == null || card.IsUnavailable)
            {
                return false;
            }

            ActionField[] fields = card.Fields;
            int fieldCount = fields.Length;
            for (int i = 0; i < fieldCount; ++i)
            {
                if (!fields[i].IsRequired)
                {
                    continue;
                }

                string value = fieldValues != null && i < fieldValues.Count ? fieldValues[i] : fields[i].InitialValue;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }
            }

            return true;
        }

        internal static string BuildCommandLine(ActionCard card, IReadOnlyList<string> fieldValues)
        {
            if (card == null)
            {
                return string.Empty;
            }

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            StringBuilder builder = scope.Builder;
            builder.Append(card.CommandPath);

            ActionField[] fields = card.Fields;
            int fieldCount = fields.Length;
            if (card.IsRawArguments)
            {
                string raw = fieldValues != null && 0 < fieldValues.Count ? fieldValues[0] : string.Empty;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    builder.Append(' ').Append(raw.Trim());
                }

                return builder.ToString();
            }

            int lastIncluded = -1;
            for (int i = 0; i < fieldCount; ++i)
            {
                string value = fieldValues != null && i < fieldValues.Count ? fieldValues[i] : fields[i].InitialValue;
                if (fields[i].IsRequired || !string.IsNullOrWhiteSpace(value))
                {
                    lastIncluded = i;
                }
            }

            for (int i = 0; i <= lastIncluded; ++i)
            {
                ActionField field = fields[i];
                string value = fieldValues != null && i < fieldValues.Count ? fieldValues[i] : field.InitialValue;
                value ??= string.Empty;

                if (field.Kind == ActionControlKind.Remaining)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        builder.Append(' ').Append(value.Trim());
                    }

                    continue;
                }

                builder.Append(' ');
                AppendToken(builder, value);
            }

            return builder.ToString();
        }

        internal static void RecordRecent(List<RecentEntry> recent, string commandPath, string commandLine)
        {
            if (recent == null || string.IsNullOrWhiteSpace(commandPath))
            {
                return;
            }

            for (int i = recent.Count - 1; 0 <= i; --i)
            {
                if (string.Equals(recent[i].CommandPath, commandPath, StringComparison.OrdinalIgnoreCase))
                {
                    recent.RemoveAt(i);
                }
            }

            recent.Insert(0, new RecentEntry(commandPath, commandLine ?? commandPath));
            while (MaxRecentEntries < recent.Count)
            {
                recent.RemoveAt(recent.Count - 1);
            }
        }

        internal static string GetInitialFieldValue(CommandArgument spec, ActionControlKind kind)
        {
            if (spec == null)
            {
                return string.Empty;
            }

            if (spec.HasExplicitDefault)
            {
                return spec.DefaultDisplayText ?? string.Empty;
            }

            if (kind == ActionControlKind.Toggle)
            {
                return "false";
            }

            if (kind == ActionControlKind.RangeNumber && spec.TryGetNumericRange(out double min, out _))
            {
                return IsIntegralType(spec.DeclaredType) ? ((long)Math.Round(min)).ToString(CultureInfo.InvariantCulture) : min.ToString("0.##", CultureInfo.InvariantCulture);
            }

            if (kind is ActionControlKind.Segmented or ActionControlKind.Menu)
            {
                string[] choices = spec.StaticChoiceTexts;
                if (choices != null && 0 < choices.Length)
                {
                    return choices[0];
                }

                if (spec.DeclaredType != null && spec.DeclaredType.IsEnum)
                {
                    string[] names = Enum.GetNames(spec.DeclaredType);
                    if (0 < names.Length)
                    {
                        return names[0];
                    }
                }
            }

            if (kind == ActionControlKind.Vector2)
            {
                return "(0,0)";
            }

            if (kind == ActionControlKind.Vector3)
            {
                return "(0,0,0)";
            }

            if (kind == ActionControlKind.Vector4)
            {
                return "(0,0,0,0)";
            }

            if (kind == ActionControlKind.Color)
            {
                return "(1,1,1,1)";
            }

            return string.Empty;
        }

        private static void CollectRouteCards(
            CommandBuilder.SubcommandRoute route,
            string groupName,
            string unavailableReason,
            List<ActionCard> output,
            Dictionary<string, ActionCard> cardsByPath
        )
        {
            if (route.IsRouter)
            {
                CommandBuilder.SubcommandRoute[] children = route.Children;
                int childCount = children.Length;
                for (int i = 0; i < childCount; ++i)
                {
                    CollectRouteCards(children[i], groupName, unavailableReason, output, cardsByPath);
                }

                return;
            }

            CommandArgument[] specs = route.Specs ?? Array.Empty<CommandArgument>();
            int specCount = specs.Length;
            ActionField[] fields = new ActionField[specCount];
            for (int i = 0; i < specCount; ++i)
            {
                CommandArgument spec = specs[i];
                ActionControlKind kind = ClassifyField(spec);
                double min = 0d;
                double max = 0d;
                bool hasRange = spec.TryGetNumericRange(out min, out max);
                string initialValue = GetInitialFieldValue(spec, kind);
                string[] choices = ResolveStaticChoices(spec);
                fields[i] = new ActionField(spec.Name, spec.Description, kind, spec.IsRequired, initialValue, spec, choices, hasRange, min, max, IsIntegralType(spec.DeclaredType));
            }

            ActionCard card = new(route.FullPath, route.Help, groupName, unavailableReason, fields, isRawArguments: false);
            output.Add(card);
            cardsByPath[card.CommandPath] = card;
        }

        private static ActionCard BuildLegacyCard(string commandName, CommandInfo info, string groupName, string unavailableReason)
        {
            bool acceptsArgs = info.maxArgCount == null || 0 < info.maxArgCount.Value;
            ActionField[] fields;
            if (acceptsArgs)
            {
                string description = !string.IsNullOrWhiteSpace(info.hint) ? info.hint : "Arguments";
                fields = new[]
                {
                    new ActionField(
                        "arguments",
                        description,
                        ActionControlKind.Text,
                        isRequired: 0 < info.minArgCount,
                        initialValue: string.Empty,
                        spec: null,
                        staticChoices: null,
                        hasRange: false,
                        rangeMin: 0d,
                        rangeMax: 0d,
                        isIntegral: false
                    ),
                };
            }
            else
            {
                fields = Array.Empty<ActionField>();
            }

            return new ActionCard(commandName, info.help, groupName, unavailableReason, fields, isRawArguments: acceptsArgs);
        }

        private static string[] ResolveStaticChoices(CommandArgument spec)
        {
            if (spec.StaticChoiceTexts != null && 0 < spec.StaticChoiceTexts.Length)
            {
                return spec.StaticChoiceTexts;
            }

            if (spec.DeclaredType != null && spec.DeclaredType.IsEnum)
            {
                return Enum.GetNames(spec.DeclaredType);
            }

            return Array.Empty<string>();
        }

        private static string ResolveGroupName(CommandInfo info)
        {
            if (!string.IsNullOrWhiteSpace(info.group))
            {
                return info.group.Trim();
            }

            if (!string.IsNullOrWhiteSpace(info.defaultGroup))
            {
                return info.defaultGroup.Trim();
            }

            return DefaultBuilderGroupName;
        }

        private static CommandTier NormalizeTier(CommandTier tier)
        {
            return tier is CommandTier.Player or CommandTier.Tester or CommandTier.Developer ? tier : CommandTier.Developer;
        }

        private static string FormatEnvironmentName(CommandExecutionContexts environment)
        {
            return environment switch
            {
                CommandExecutionContexts.EditorEditMode => "Edit Mode",
                CommandExecutionContexts.EditorPlayMode => "Play Mode",
                CommandExecutionContexts.Player => "Player builds",
                _ => "this context",
            };
        }

        private static string FormatAllowedEnvironments(CommandExecutionContexts allowed)
        {
            List<string> names = new(3);
            if (allowed.HasFlagNoAlloc(CommandExecutionContexts.EditorEditMode))
            {
                names.Add("Edit Mode");
            }

            if (allowed.HasFlagNoAlloc(CommandExecutionContexts.EditorPlayMode))
            {
                names.Add("Play Mode");
            }

            if (allowed.HasFlagNoAlloc(CommandExecutionContexts.Player))
            {
                names.Add("Player");
            }

            if (names.Count == 0)
            {
                return "No context";
            }

            if (names.Count == 1)
            {
                return names[0];
            }

            if (names.Count == 2)
            {
                return $"{names[0]} and {names[1]}";
            }

            return $"{names[0]}, {names[1]}, and {names[2]}";
        }

        private static void AppendToken(StringBuilder builder, string value)
        {
            if (value.Length == 0)
            {
                builder.Append("\"\"");
                return;
            }

            bool needsQuotes = false;
            int length = value.Length;
            for (int i = 0; i < length; ++i)
            {
                char ch = value[i];
                if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\'')
                {
                    needsQuotes = true;
                    break;
                }
            }

            if (!needsQuotes)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            for (int i = 0; i < length; ++i)
            {
                char ch = value[i];
                if (ch is '"' or '\\')
                {
                    builder.Append('\\');
                }

                builder.Append(ch);
            }

            builder.Append('"');
        }

        private static int CompareGroups(ActionGroup left, ActionGroup right)
        {
            return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareCards(ActionCard left, ActionCard right)
        {
            return string.Compare(left.CommandPath, right.CommandPath, StringComparison.OrdinalIgnoreCase);
        }

        internal enum ActionControlKind
        {
            [Obsolete("Use a valid ActionControlKind value.")]
            Unknown = 0,
            Text = 1,
            Toggle = 2,
            Segmented = 3,
            Menu = 4,
            RangeNumber = 5,
            Number = 6,
            Vector2 = 7,
            Vector3 = 8,
            Vector4 = 9,
            Color = 10,
            SceneObject = 11,
            Remaining = 12,
        }

        internal sealed class ActionField
        {
            public string Name { get; }
            public string Description { get; }
            public ActionControlKind Kind { get; }
            public bool IsRequired { get; }
            public string InitialValue { get; }
            public CommandArgument Spec { get; }
            public string[] StaticChoices { get; }
            public bool HasRange { get; }
            public double RangeMin { get; }
            public double RangeMax { get; }
            public bool IsIntegral { get; }

            public ActionField(
                string name,
                string description,
                ActionControlKind kind,
                bool isRequired,
                string initialValue,
                CommandArgument spec,
                string[] staticChoices,
                bool hasRange,
                double rangeMin,
                double rangeMax,
                bool isIntegral
            )
            {
                Name = name;
                Description = description;
                Kind = kind;
                IsRequired = isRequired;
                InitialValue = initialValue ?? string.Empty;
                Spec = spec;
                StaticChoices = staticChoices ?? Array.Empty<string>();
                HasRange = hasRange;
                RangeMin = rangeMin;
                RangeMax = rangeMax;
                IsIntegral = isIntegral;
            }
        }

        internal sealed class ActionCard
        {
            public string CommandPath { get; }
            public string Description { get; }
            public string Group { get; }
            public string UnavailableReason { get; }
            public bool IsUnavailable
            {
                get { return !string.IsNullOrEmpty(UnavailableReason); }
            }
            public ActionField[] Fields { get; }
            public bool IsRawArguments { get; }

            public ActionCard(string commandPath, string description, string group, string unavailableReason, ActionField[] fields, bool isRawArguments)
            {
                CommandPath = commandPath;
                Description = description ?? string.Empty;
                Group = group;
                UnavailableReason = unavailableReason;
                Fields = fields ?? Array.Empty<ActionField>();
                IsRawArguments = isRawArguments;
            }
        }

        internal sealed class ActionGroup
        {
            public string Name { get; }
            public List<ActionCard> Cards { get; }
            public bool IsRecent { get; }

            public ActionGroup(string name, List<ActionCard> cards, bool isRecent)
            {
                Name = name;
                Cards = cards;
                IsRecent = isRecent;
            }
        }

        internal readonly struct RecentEntry
        {
            public readonly string CommandPath;
            public readonly string CommandLine;

            public RecentEntry(string commandPath, string commandLine)
            {
                CommandPath = commandPath;
                CommandLine = commandLine;
            }
        }
    }
}
