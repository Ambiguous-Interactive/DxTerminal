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
            if (!string.IsNullOrEmpty(exampleLine))
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
                    parts[i + 1] = spec.IsRequired
                        ? new SignaturePart(spec.Name + suffix, SignaturePartKind.Required)
                        : new SignaturePart("[" + spec.Name + suffix + "]", SignaturePartKind.Optional);
                }

                return parts;
            }

            if (route != null)
            {
                return new[]
                {
                    new SignaturePart(route.FullPath, SignaturePartKind.Command),
                    new SignaturePart(route.Fallback != null ? "[subcommand]" : "<subcommand>", SignaturePartKind.Subcommand),
                };
            }

            string hint = info.hint?.Trim();
            if (!string.IsNullOrEmpty(hint) && !string.Equals(hint, canonicalName, StringComparison.OrdinalIgnoreCase))
            {
                string rest =
                    hint.StartsWith(canonicalName, StringComparison.OrdinalIgnoreCase) && canonicalName.Length < hint.Length && char.IsWhiteSpace(hint[canonicalName.Length])
                        ? hint.Substring(canonicalName.Length).TrimStart()
                        : hint;
                if (!string.IsNullOrEmpty(rest))
                {
                    return new[] { new SignaturePart(canonicalName, SignaturePartKind.Command), new SignaturePart(rest, SignaturePartKind.Optional) };
                }
            }
            else if (string.IsNullOrEmpty(hint) && (0 < info.minArgCount || info.maxArgCount != 0))
            {
                return new[]
                {
                    new SignaturePart(canonicalName, SignaturePartKind.Command),
                    new SignaturePart(0 < info.minArgCount ? "<args>" : "[args]", SignaturePartKind.Optional),
                };
            }

            return new[] { new SignaturePart(canonicalName, SignaturePartKind.Command) };
        }

        private static string FormatSignaturePlainText(SignaturePart[] parts)
        {
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            StringBuilder builder = scope.Builder;
            for (int i = 0; i < parts.Length; ++i)
            {
                if (0 < i)
                {
                    builder.Append(' ');
                }

                builder.Append(parts[i].Text);
            }

            return builder.ToString();
        }

        private static string BuildDescriptionLine(string canonicalName, CommandInfo info)
        {
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(128);
            StringBuilder builder = scope.Builder;
            string description = string.IsNullOrWhiteSpace(info.help) ? $"{canonicalName} does not provide any help documentation." : info.help.Trim();
            builder.Append(description);
            if (description[description.Length - 1] is not ('.' or '!' or '?'))
            {
                builder.Append('.');
            }

            if (!string.IsNullOrWhiteSpace(info.Group))
            {
                builder.Append(" Group: ").Append(info.Group.Trim()).Append('.');
            }

            builder.Append(' ').Append(FormatExecutionContexts(info.executionContexts));
            return builder.ToString();
        }

        private static string FormatExecutionContexts(CommandExecutionContexts contexts)
        {
            return (
                contexts.HasFlagNoAlloc(CommandExecutionContexts.EditorEditMode),
                contexts.HasFlagNoAlloc(CommandExecutionContexts.EditorPlayMode),
                contexts.HasFlagNoAlloc(CommandExecutionContexts.Player)
            ) switch
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
                    string childHelp = children[i].Help ?? string.Empty;
                    childRows[i] = new ArgumentRow(children[i].Name, "subcommand", CommandSyntaxRole.Choice, childHelp);
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
                return depth < MaxRouterExampleDepth && route.Children is { Length: > 0 }
                    ? BuildRouteExample(route.Children[0], depth + 1)
                    : (route.Fallback != null ? route.FullPath : null);
            }

            CommandArgument[] specs = route.Specs ?? Array.Empty<CommandArgument>();
            if (specs.Length == 0)
            {
                return route.FullPath;
            }

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(route.FullPath.Length + (specs.Length * 12));
            StringBuilder builder = scope.Builder;
            builder.Append(route.FullPath);
            for (int i = 0; i < specs.Length; ++i)
            {
                builder.Append(' ').Append(specs[i].GetExampleToken());
            }

            return builder.ToString();
        }

        private static CommandSyntaxSpan[] ClassifyExample(string exampleLine, CommandShell shell)
        {
            if (string.IsNullOrEmpty(exampleLine) || shell == null)
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
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(256);
            StringBuilder builder = scope.Builder;
            builder.Append(signaturePlainText).Append('\n').Append(descriptionLine);
            if (0 < arguments.Length)
            {
                int maxName = 0;
                int maxType = 0;
                for (int i = 0; i < arguments.Length; ++i)
                {
                    maxName = Math.Max(maxName, arguments[i].Name.Length);
                    maxType = Math.Max(maxType, arguments[i].TypeText.Length);
                }

                for (int i = 0; i < arguments.Length; ++i)
                {
                    ArgumentRow row = arguments[i];
                    builder.Append('\n').Append(row.Name).Append(' ', maxName - row.Name.Length + 2).Append(row.TypeText);
                    if (!string.IsNullOrEmpty(row.RequirementText))
                    {
                        builder.Append(' ', maxType - row.TypeText.Length + 2).Append(row.RequirementText);
                    }
                }
            }

            if (!string.IsNullOrEmpty(exampleLine))
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
