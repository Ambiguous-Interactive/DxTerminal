namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Extensions;
    using Helper;
    using CommandSyntaxRole = CommandSyntaxClassifier.CommandSyntaxRole;
    using CommandSyntaxSpan = CommandSyntaxClassifier.CommandSyntaxSpan;

    internal sealed class CommandHelpCard
    {
        private const int MaxRouterExampleDepth = 4;
        private const int InitialSignatureCapacity = 64;
        private const int InitialDescriptionCapacity = 128;
        private const int InitialPlainTextCapacity = 256;
        private const int AverageArgumentTokenCapacity = 12;
        private const int ColumnPaddingSpaces = 2;

        internal string CommandName { get; }
        internal string SignaturePlainText { get; }
        internal SignaturePart[] SignatureParts { get; }
        internal string DescriptionLine { get; }
        internal ArgumentRow[] Arguments { get; }
        internal string ExampleLine { get; }
        internal CommandSyntaxSpan[] ExampleSpans { get; }
        internal string PlainText { get; }

        private CommandHelpCard(
            string commandName,
            string signaturePlainText,
            SignaturePart[] signatureParts,
            string descriptionLine,
            ArgumentRow[] arguments,
            string exampleLine,
            CommandSyntaxSpan[] exampleSpans,
            string plainText
        )
        {
            CommandName = commandName;
            SignaturePlainText = signaturePlainText;
            SignatureParts = signatureParts;
            DescriptionLine = descriptionLine;
            Arguments = arguments;
            ExampleLine = exampleLine;
            ExampleSpans = exampleSpans;
            PlainText = plainText;
        }

        internal static CommandHelpCard Create(string commandName, CommandInfo info, CommandShell shell)
        {
            string canonicalName = ResolveCanonicalName(commandName, info, shell);
            CommandBuilder.SubcommandRoute route = info.SyntaxRoute;

            SignaturePart[] signatureParts = BuildSignatureParts(canonicalName, info, route);
            string signaturePlainText = FormatSignaturePlainText(signatureParts);
            string descriptionLine = BuildDescriptionLine(canonicalName, info);
            ArgumentRow[] arguments = BuildArgumentRows(route);
            string exampleLine = route != null ? BuildRouteExample(route, 0) : (info.minArgCount == 0 ? canonicalName : null);
            if (!string.IsNullOrWhiteSpace(exampleLine))
            {
                exampleLine = LogTextSanitizer.Sanitize(exampleLine);
            }

            CommandSyntaxSpan[] exampleSpans = ClassifyExample(exampleLine, shell);
            string plainText = BuildPlainText(signaturePlainText, descriptionLine, arguments, exampleLine);

            return new CommandHelpCard(canonicalName, signaturePlainText, signatureParts, descriptionLine, arguments, exampleLine, exampleSpans, plainText);
        }

        private static string ResolveCanonicalName(string commandName, CommandInfo info, CommandShell shell)
        {
            if (info.SyntaxRoute != null && !string.IsNullOrWhiteSpace(info.SyntaxRoute.FullPath))
            {
                return info.SyntaxRoute.FullPath;
            }

            if (shell != null)
            {
                foreach (KeyValuePair<string, CommandInfo> entry in shell.CommandsSorted)
                {
                    if (string.Equals(entry.Key, commandName, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.Key;
                    }
                }
            }

            return commandName ?? string.Empty;
        }

        private static SignaturePart[] BuildSignatureParts(string canonicalName, CommandInfo info, CommandBuilder.SubcommandRoute route)
        {
            if (route != null && !route.IsRouter)
            {
                CommandArgument[] specs = route.Specs ?? Array.Empty<CommandArgument>();
                SignaturePart[] parts = new SignaturePart[specs.Length + 1];
                parts[0] = new SignaturePart(route.FullPath, SignaturePartKind.Command);
                for (int i = 0; i < specs.Length; ++i)
                {
                    CommandArgument spec = specs[i];
                    string suffix = spec.IsRemaining ? "..." : string.Empty;
                    string partText = spec.IsRequired ? spec.Name + suffix : "[" + spec.Name + suffix + "]";
                    SignaturePartKind partKind = spec.IsRequired ? SignaturePartKind.Required : SignaturePartKind.Optional;
                    parts[i + 1] = new SignaturePart(partText, partKind);
                }

                return parts;
            }

            if (route != null)
            {
                string subcommandToken = route.Fallback != null ? "[subcommand]" : "<subcommand>";
                return new[] { new SignaturePart(route.FullPath, SignaturePartKind.Command), new SignaturePart(subcommandToken, SignaturePartKind.Subcommand) };
            }

            string hint = info.hint?.Trim();
            if (!string.IsNullOrWhiteSpace(hint) && !string.Equals(hint, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                bool hasCommandPrefix =
                    hint.StartsWith(canonicalName, StringComparison.OrdinalIgnoreCase) && canonicalName.Length < hint.Length && char.IsWhiteSpace(hint[canonicalName.Length]);
                string rest = hasCommandPrefix ? hint.Substring(canonicalName.Length).TrimStart() : hint;
                if (!string.IsNullOrWhiteSpace(rest))
                {
                    return new[] { new SignaturePart(canonicalName, SignaturePartKind.Command), new SignaturePart(rest, SignaturePartKind.Optional) };
                }
            }
            else if (string.IsNullOrWhiteSpace(hint) && (0 < info.minArgCount || info.maxArgCount != 0))
            {
                string argsToken = 0 < info.minArgCount ? "<args>" : "[args]";
                return new[] { new SignaturePart(canonicalName, SignaturePartKind.Command), new SignaturePart(argsToken, SignaturePartKind.Optional) };
            }

            return new[] { new SignaturePart(canonicalName, SignaturePartKind.Command) };
        }

        private static string FormatSignaturePlainText(SignaturePart[] parts)
        {
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(InitialSignatureCapacity);
            StringBuilder builder = scope.Builder;
            bool first = true;
            foreach (SignaturePart part in parts)
            {
                if (!first)
                {
                    builder.Append(' ');
                }

                builder.Append(part.Text);
                first = false;
            }

            return builder.ToString();
        }

        private static string BuildDescriptionLine(string canonicalName, CommandInfo info)
        {
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(InitialDescriptionCapacity);
            StringBuilder builder = scope.Builder;
            string description = string.IsNullOrWhiteSpace(info.help) ? $"{canonicalName} does not provide any help documentation." : info.help.Trim();
            builder.Append(description);
            char lastCharacter = description[description.Length - 1];
            if (lastCharacter is not ('.' or '!' or '?'))
            {
                builder.Append('.');
            }

            if (!string.IsNullOrWhiteSpace(info.Group))
            {
                string trimmedGroup = info.Group.Trim();
                builder.Append(" Group: ").Append(trimmedGroup).Append('.');
            }

            string contextsText = FormatExecutionContexts(info.executionContexts);
            builder.Append(' ').Append(contextsText);
            return builder.ToString();
        }

        private static string FormatExecutionContexts(CommandExecutionContexts contexts)
        {
            bool editMode = contexts.HasFlagNoAlloc(CommandExecutionContexts.EditorEditMode);
            bool playMode = contexts.HasFlagNoAlloc(CommandExecutionContexts.EditorPlayMode);
            bool player = contexts.HasFlagNoAlloc(CommandExecutionContexts.Player);
            return (editMode, playMode, player) switch
            {
                (true, true, true) => "All contexts.",
                (false, true, true) => "Play Mode and Player.",
                (true, true, false) => "Editor only.",
                (true, false, false) => "Edit Mode only.",
                (false, true, false) => "Play Mode only.",
                (false, false, true) => "Player only.",
                (true, false, true) => "Edit Mode and Player.",
                _ => "Disabled.",
            };
        }

        private static ArgumentRow[] BuildArgumentRows(CommandBuilder.SubcommandRoute route)
        {
            if (route == null)
            {
                return Array.Empty<ArgumentRow>();
            }

            if (route.IsRouter)
            {
                CommandBuilder.SubcommandRoute[] children = route.Children ?? Array.Empty<CommandBuilder.SubcommandRoute>();
                ArgumentRow[] childRows = children.Length == 0 ? Array.Empty<ArgumentRow>() : new ArgumentRow[children.Length];
                for (int i = 0; i < children.Length; ++i)
                {
                    CommandBuilder.SubcommandRoute child = children[i];
                    string childHelp = child.Help ?? string.Empty;
                    childRows[i] = new ArgumentRow(child.Name, "subcommand", CommandSyntaxRole.Choice, childHelp);
                }

                return childRows;
            }

            CommandArgument[] specs = route.Specs ?? Array.Empty<CommandArgument>();
            ArgumentRow[] rows = specs.Length == 0 ? Array.Empty<ArgumentRow>() : new ArgumentRow[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                CommandArgument spec = specs[i];
                CommandSyntaxRole role = spec.HasChoices ? CommandSyntaxRole.Choice : spec.SyntaxRole;
                string requirement =
                    spec.IsRequired ? "required"
                    : spec.IsRemaining ? "optional"
                    : "= " + spec.DefaultDisplayText;
                rows[i] = new ArgumentRow(spec.Name, spec.HelpType, role, requirement);
            }

            return rows;
        }

        private static string BuildRouteExample(CommandBuilder.SubcommandRoute route, int depth)
        {
            if (!string.IsNullOrWhiteSpace(route.Example))
            {
                return route.Example.Trim();
            }

            if (route.IsRouter)
            {
                if (depth < MaxRouterExampleDepth && route.Children is { Length: > 0 })
                {
                    int nextDepth = depth + 1;
                    foreach (CommandBuilder.SubcommandRoute child in route.Children)
                    {
                        string childExample = BuildRouteExample(child, nextDepth);
                        if (!string.IsNullOrWhiteSpace(childExample))
                        {
                            return childExample;
                        }
                    }
                }

                return route.Fallback != null ? route.FullPath : null;
            }

            CommandArgument[] specs = route.Specs ?? Array.Empty<CommandArgument>();
            if (specs.Length == 0)
            {
                return route.FullPath;
            }

            int initialCapacity = route.FullPath.Length + (specs.Length * AverageArgumentTokenCapacity);
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(initialCapacity);
            StringBuilder builder = scope.Builder;
            builder.Append(route.FullPath);
            foreach (CommandArgument spec in specs)
            {
                if (!spec.TryGetExampleToken(out string token))
                {
                    return null;
                }

                builder.Append(' ').Append(token);
            }

            return builder.ToString();
        }

        private static CommandSyntaxSpan[] ClassifyExample(string exampleLine, CommandShell shell)
        {
            if (string.IsNullOrWhiteSpace(exampleLine) || shell == null)
            {
                return Array.Empty<CommandSyntaxSpan>();
            }

            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            classifier.Classify(exampleLine, shell, spans);
            return spans.ToArray();
        }

        private static string BuildPlainText(string signaturePlainText, string descriptionLine, ArgumentRow[] arguments, string exampleLine)
        {
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(InitialPlainTextCapacity);
            StringBuilder builder = scope.Builder;
            builder.Append(signaturePlainText).Append('\n').Append(descriptionLine);
            if (0 < arguments.Length)
            {
                int maxName = 0;
                int maxType = 0;
                foreach (ArgumentRow row in arguments)
                {
                    int nameLength = row.Name.Length;
                    int typeLength = row.TypeText.Length;
                    maxName = Math.Max(maxName, nameLength);
                    maxType = Math.Max(maxType, typeLength);
                }

                foreach (ArgumentRow row in arguments)
                {
                    int namePadding = maxName - row.Name.Length + ColumnPaddingSpaces;
                    builder.Append('\n').Append(row.Name).Append(' ', namePadding).Append(row.TypeText);
                    if (!string.IsNullOrWhiteSpace(row.RequirementText))
                    {
                        int typePadding = maxType - row.TypeText.Length + ColumnPaddingSpaces;
                        builder.Append(' ', typePadding).Append(row.RequirementText);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(exampleLine))
            {
                builder.Append("\ne.g. ").Append(exampleLine);
            }

            return builder.ToString();
        }

        internal readonly struct SignaturePart
        {
            public string Text { get; }
            public SignaturePartKind Kind { get; }

            public SignaturePart(string text, SignaturePartKind kind)
            {
                Text = text;
                Kind = kind;
            }
        }

        internal readonly struct ArgumentRow
        {
            public string Name { get; }
            public string TypeText { get; }
            public CommandSyntaxRole TypeRole { get; }
            public string RequirementText { get; }

            public ArgumentRow(string name, string typeText, CommandSyntaxRole typeRole, string requirementText)
            {
                Name = name;
                TypeText = typeText;
                TypeRole = typeRole;
                RequirementText = requirementText;
            }
        }

        internal enum SignaturePartKind
        {
            [Obsolete("Specify a signature part kind.")]
            Unknown = 0,
            Command = 1,
            Required = 2,
            Optional = 3,
            Subcommand = 4,
        }
    }
}
