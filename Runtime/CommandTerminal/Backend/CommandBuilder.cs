namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Helper;

    /// <summary>
    ///     Fluent authoring for one typed command: names, help, hints, history
    ///     policy, execution eligibility, typed arguments, handler, and
    ///     completion all come from one definition, registered into the same
    ///     shell through <see cref="CommandShell.AddCommand(CommandBuilder, out CommandRegistrationHandle)"/>.
    ///     Subcommands route the first argument to nested definitions, so
    ///     <c>inventory add pickaxe</c> and <c>inventory remove pickaxe</c>
    ///     share one registered command.
    /// </summary>
    /// <remarks>
    ///     Argument bounds, the usage hint, and the completion provider derive
    ///     from the declared arguments, so help, validation, and completion
    ///     cannot drift apart. Configuration errors (missing handler,
    ///     duplicate argument names, required-after-optional ordering,
    ///     unparseable argument types, defaults failing their own validation,
    ///     arguments after the remaining argument, or a default on the
    ///     remaining argument) throw at definition time with a diagnostic
    ///     naming the command; user-input mistakes at execution become
    ///     controlled shell errors and never run the handler.
    /// </remarks>
    public sealed class CommandBuilder
    {
        private static readonly CommandArgument[] NoArguments = Array.Empty<CommandArgument>();
        private static readonly object[] NoValues = Array.Empty<object>();

        /// <summary>The command name, matched case-insensitively; spaces are stripped at registration.</summary>
        public string Name { get; }

        private readonly List<CommandArgument> _arguments = new();
        private readonly List<CommandBuilder> _subcommands = new();

        private string _group;
        private string _help = string.Empty;
        private string _hint;
        private string _example;
        private CommandExecutionContexts _contexts = CommandExecutionContextSets.Gameplay;
        private CommandTier _tier = CommandTier.Developer;
        private bool _hideFromActions;
        private bool _addToHistory = true;
        private TypedCommandHandler _handler;

        private CommandBuilder(string name)
        {
            Name = name;
        }

        /// <summary>Creates a builder for the named command.</summary>
        public static CommandBuilder Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandConfigurationException(CommandConfigurationFailure.EmptyName, "A command name is required; pass a non-empty name to CommandBuilder.Create.");
            }

            return new CommandBuilder(name);
        }

        /// <summary>Creates a builder for the named command with help text.</summary>
        public static CommandBuilder Create(string name, string help)
        {
            return Create(name).Help(help);
        }

        internal static CommandShell.LineValidation ValidateRoute(
            SubcommandRoute route,
            IReadOnlyList<CommandArg> arguments,
            int offset,
            bool allowIncomplete,
            bool catchExceptions
        )
        {
            if (route == null)
            {
                return CommandShell.LineValidation.Valid();
            }

            BorrowedCommandArguments borrowed = new(arguments);
            if (!TryResolveRoute(route, borrowed, offset, allowIncomplete, out SubcommandRoute leaf, out int leafOffset, out string routeError))
            {
                return CommandShell.LineValidation.Error(routeError);
            }

            if (leaf == null)
            {
                return CommandShell.LineValidation.Valid();
            }

            return ValidateSpecs(leaf.Specs, borrowed, leafOffset, parsed: null, allowIncomplete: allowIncomplete, catchExceptions: catchExceptions);
        }

        private static bool CanCacheSpecs(CommandArgument[] specs, int evaluatedCount)
        {
            int limit = Math.Min(specs.Length, evaluatedCount);
            for (int index = 0; index < limit; ++index)
            {
                if (!specs[index].CanCacheSyntax)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryResolveRoute(
            SubcommandRoute route,
            BorrowedCommandArguments arguments,
            int offset,
            bool allowIncomplete,
            out SubcommandRoute leaf,
            out int leafOffset,
            out string error
        )
        {
            while (route.IsRouter)
            {
                if (arguments.Count <= offset)
                {
                    if (route.Fallback != null || allowIncomplete)
                    {
                        leaf = null;
                        leafOffset = offset;
                        error = null;
                        return true;
                    }

                    leaf = null;
                    leafOffset = offset;
                    error = $"'{route.FullPath}': expected a subcommand. Expected one of: {FormatRouteList(route.Lookup)}";
                    return false;
                }

                string tokenText = arguments[offset].contents ?? string.Empty;
                if (!route.Lookup.TryGetValue(tokenText, out SubcommandRoute child))
                {
                    leaf = null;
                    leafOffset = offset;
                    error = $"'{route.FullPath}': unknown subcommand '{tokenText}'. Expected one of: {FormatRouteList(route.Lookup)}";
                    return false;
                }

                route = child;
                ++offset;
            }

            int available = arguments.Count - offset;
            if (!allowIncomplete && available < route.RequiredCount)
            {
                leaf = null;
                leafOffset = offset;
                error =
                    $"'{route.FullPath}': requires at least {route.RequiredCount} argument"
                    + (route.RequiredCount == 1 ? string.Empty : "s")
                    + $"\n    -> Usage: {route.PathUsage}";
                return false;
            }

            if (!route.HasRemaining && route.Specs.Length < available)
            {
                leaf = null;
                leafOffset = offset;
                error =
                    $"'{route.FullPath}': expects at most {route.Specs.Length} argument" + (route.Specs.Length == 1 ? string.Empty : "s") + $"\n    -> Usage: {route.PathUsage}";
                return false;
            }

            leaf = route;
            leafOffset = offset;
            error = null;
            return true;
        }

        private static CommandShell.LineValidation ValidateSpecs(
            CommandArgument[] specs,
            BorrowedCommandArguments arguments,
            int offset,
            object[] parsed,
            bool allowIncomplete,
            bool catchExceptions
        )
        {
            bool canCache = CanCacheSpecs(specs, Math.Max(0, arguments.Count - offset));
            int specCount = specs.Length;
            for (int i = 0; i < specCount; ++i)
            {
                CommandArgument spec = specs[i];
                if (spec.IsRemaining)
                {
                    if (parsed != null)
                    {
                        if (!spec.TryParseAll(arguments, offset + i, parsed, i, out CommandArg failedToken, out string validationError))
                        {
                            string message = validationError ?? spec.FormatParseError(failedToken);
                            return CommandShell.LineValidation.Error(message, spec.Name, failedToken.contents, canCache: canCache);
                        }

                        continue;
                    }

                    int argCount = arguments.Count;
                    for (int argIndex = offset + i; argIndex < argCount; ++argIndex)
                    {
                        CommandArg remainingInput = arguments[argIndex];
                        CommandShell.LineValidation remainingValidation = ValidateSingleSpec(spec, remainingInput, catchExceptions, canCache, out _);
                        if (remainingValidation.IsError)
                        {
                            return remainingValidation;
                        }
                    }

                    continue;
                }

                if (arguments.Count <= offset + i)
                {
                    if (parsed != null)
                    {
                        parsed[i] = spec.GetDefault();
                    }

                    if (allowIncomplete)
                    {
                        break;
                    }

                    continue;
                }

                CommandArg input = arguments[offset + i];
                CommandShell.LineValidation validation = ValidateSingleSpec(spec, input, catchExceptions, canCache, out object value);
                if (validation.IsError)
                {
                    return validation;
                }

                if (parsed != null)
                {
                    parsed[i] = value;
                }
            }

            return CommandShell.LineValidation.Valid(canCache: canCache);
        }

        private static CommandShell.LineValidation ValidateSingleSpec(CommandArgument spec, CommandArg input, bool catchExceptions, bool canCache, out object value)
        {
            try
            {
                if (!spec.TryValidateArgument(input, out value, out string errorMessage))
                {
                    return CommandShell.LineValidation.Error(errorMessage, spec.Name, input.contents, canCache: canCache);
                }

                return CommandShell.LineValidation.Valid(canCache: canCache);
            }
            catch (Exception) when (catchExceptions)
            {
                value = null;
                return CommandShell.LineValidation.Error(spec.FormatParseError(input), spec.Name, input.contents, canCache: canCache);
            }
        }

        private static string NormalizeName(string owner, string name, bool isSubcommandName = true)
        {
            string normalized = name;
            if (normalized != null && 0 <= normalized.IndexOf(' ', StringComparison.Ordinal))
            {
                normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.EmptyName,
                    $"Command '{owner ?? string.Empty}': subcommand names must not be empty.",
                    owner,
                    subcommandName: isSubcommandName ? name : null
                );
            }

            return normalized;
        }

        private static void RunTyped(
            CommandShell owner,
            CommandArgument[] specs,
            string name,
            TypedCommandHandler handler,
            CommandExecutionContext context,
            BorrowedCommandArguments arguments,
            int offset
        )
        {
            int specCount = specs.Length;
            object[] parsed = specCount == 0 ? NoValues : new object[specCount];
            CommandShell.LineValidation validation = ValidateSpecs(specs, arguments, offset, parsed, allowIncomplete: false, catchExceptions: false);
            if (validation.IsError)
            {
                owner.IssueErrorMessage(validation.Message);
                return;
            }

            handler(context, new CommandArguments(specs, parsed, arguments.Slice(offset), context));
        }

        private static string BuildUsageHint(string name, CommandArgument[] specs)
        {
            int specCount = specs.Length;
            if (specCount == 0)
            {
                return name;
            }

            StringBuilder builder = new(name.Length + 16 * specCount);
            builder.Append(name);
            for (int i = 0; i < specCount; ++i)
            {
                builder.Append(' ');
                specs[i].AppendUsage(builder);
            }

            return builder.ToString();
        }

        private static CommandCompletionProvider BuildCompletionProvider(ReadOnlyMemory<CommandCompletionProvider> stages, bool hasRemaining)
        {
            foreach (CommandCompletionProvider stage in stages.Span)
            {
                if (stage == null)
                {
                    continue;
                }

                return (in CommandCompletionContext context, List<CommandCompletion> results) =>
                {
                    ReadOnlySpan<CommandCompletionProvider> available = stages.Span;
                    int index = context.ActiveArgumentIndex;
                    if (available.Length <= index)
                    {
                        if (!hasRemaining)
                        {
                            return;
                        }

                        index = available.Length - 1;
                    }

                    available[index]?.Invoke(context, results);
                };
            }

            // Leave commands without choices on the history-based path.
            return null;
        }

        /*
            One stage per declared argument, in declaration order; stages for
            arguments without choices stay null. The array is always sized to
            specs.Length so route completion can index it directly.
        */
        private static CommandCompletionProvider[] BuildSpecCompletionStages(CommandArgument[] specs)
        {
            CommandCompletionProvider[] stages = new CommandCompletionProvider[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                CommandArgument spec = specs[i];
                if (!spec.HasChoices)
                {
                    continue;
                }

                stages[i] = (in CommandCompletionContext context, List<CommandCompletion> results) => spec.AppendCompletions(context, results);
            }

            return stages;
        }

        /*
            Router dispatch and completion: one registered command routes the
            first argument to nested definitions. offset counts how many
            leading arguments belong to the routing levels already matched, so
            the same routine serves every nesting depth.
        */
        private static void RouteInvocation(CommandShell owner, SubcommandRoute root, CommandExecutionContext context, BorrowedCommandArguments arguments)
        {
            if (!TryResolveRoute(root, arguments, offset: 0, allowIncomplete: false, out SubcommandRoute leaf, out int leafOffset, out string error))
            {
                owner.IssueErrorMessage(error);
                return;
            }

            if (leaf == null)
            {
                SubcommandRoute current = root;
                for (int index = 0; index < leafOffset; ++index)
                {
                    current = current.Lookup[arguments[index].contents ?? string.Empty];
                }

                RunTyped(owner, NoArguments, current.FullPath, current.Fallback, context, arguments, leafOffset);
                return;
            }

            RunTyped(owner, leaf.Specs, leaf.FullPath, leaf.Handler, context, arguments, leafOffset);
        }

        private static void RouteCompletion(Dictionary<string, SubcommandRoute> lookup, in CommandCompletionContext context, int baseStage, List<CommandCompletion> results)
        {
            int stage = context.ActiveArgumentIndex - baseStage;
            if (stage == 0)
            {
                string token = context.Token;
                foreach (SubcommandRoute candidate in lookup.Values)
                {
                    if (candidate.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new CommandCompletion(candidate.Name, description: candidate.Help));
                    }
                }

                return;
            }

            if (context.PrecedingArguments.Count <= baseStage)
            {
                return;
            }

            CommandArg selector = context.PrecedingArguments[baseStage];
            if (!lookup.TryGetValue(selector.contents ?? string.Empty, out SubcommandRoute route))
            {
                return;
            }

            if (route.IsRouter)
            {
                RouteCompletion(route.Lookup, context, baseStage + 1, results);
                return;
            }

            int childStage = stage - 1;
            if (route.Specs.Length <= childStage)
            {
                if (!route.HasRemaining)
                {
                    return;
                }

                /*
                    The remaining argument owns every trailing stage, so later
                    stages clamp onto its choices.
                 */
                childStage = route.Specs.Length - 1;
            }

            /*
                The routed subcommand owns its argument window: hand the stage
                a subcommand-relative context so dynamic choice providers read
                the same shape as on a top-level command.
            */
            route.CompletionStages[childStage]?.Invoke(context.ForSubcommand(baseStage + 1), results);
        }

        private static string FormatRouteList(Dictionary<string, SubcommandRoute> lookup)
        {
            using CachedStringBuilder.Scope scope = new(64);
            StringBuilder builder = scope.Builder;
            bool first = true;
            foreach (SubcommandRoute route in lookup.Values)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(route.Usage);
                first = false;
            }

            return builder.ToString();
        }

        private static int RequiredCount(CommandArgument[] specs)
        {
            int count = 0;
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired)
                {
                    ++count;
                }
            }

            return count;
        }

        private static bool HasRemaining(CommandArgument[] specs)
        {
            return 0 < specs.Length && specs[specs.Length - 1].IsRemaining;
        }

        /// <summary>Sets the help text shown by the built-in <c>help</c> command.</summary>
        public CommandBuilder Help(string help)
        {
            _help = help ?? string.Empty;
            return this;
        }

        /// <summary>
        ///     Overrides the derived usage hint. Without this, the hint derives
        ///     from the declared arguments, e.g. <c>heal &lt;amount:int&gt; [target:string]</c>.
        /// </summary>
        public CommandBuilder Hint(string hint)
        {
            _hint = hint;
            return this;
        }

        /// <summary>
        ///     Sets the environments the command is eligible to run in. Defaults
        ///     to gameplay contexts; Edit Mode execution requires explicit opt-in.
        /// </summary>
        public CommandBuilder Contexts(CommandExecutionContexts contexts)
        {
            _contexts = contexts;
            return this;
        }

        /// <summary>Sets the group shown beside the command in completion lists.</summary>
        public CommandBuilder Group(string group)
        {
            _group = group;
            return this;
        }

        /// <summary>Sets the audience tier used by build profiles and UI surfaces.</summary>
        public CommandBuilder Tier(CommandTier tier)
        {
            if (tier is not (CommandTier.Player or CommandTier.Tester or CommandTier.Developer))
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Tier must be Player, Tester, or Developer.");
            }

            _tier = tier;
            return this;
        }

        /// <summary>Hides the command from the Actions surface when true.</summary>
        public CommandBuilder HideFromActions(bool hideFromActions = true)
        {
            _hideFromActions = hideFromActions;
            return this;
        }

        /// <summary>Sets the example command line shown in the help card.</summary>
        public CommandBuilder Example(string example)
        {
            _example = example;
            return this;
        }

        public CommandBuilder AddToHistory(bool addToHistory)
        {
            _addToHistory = addToHistory;
            return this;
        }

        /// <summary>
        ///     Declares one typed argument. The optional configuration callback
        ///     marks it required, gives it a default, and attaches validation
        ///     and choices. Argument order is declaration order; declaring an
        ///     argument after the command's
        ///     <see cref="Remaining{T}"/> argument throws.
        /// </summary>
        public CommandBuilder Arg<T>(string name, Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure = null)
        {
            EnsureArgumentNameAvailable(name);
            int argumentCount = _arguments.Count;
            if (0 < argumentCount && _arguments[argumentCount - 1].IsRemaining)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidRemainingArgument,
                    $"Command '{Name}': argument '{name}' cannot follow the remaining "
                        + $"argument '{_arguments[argumentCount - 1].Name}'; the remaining "
                        + "argument consumes every trailing token.",
                    Name,
                    argumentName: name
                );
            }

            _arguments.Add(ConfigureArgument(name, configure, isRemaining: false));
            return this;
        }

        /// <summary>
        ///     Declares the command's unbounded trailing argument: every token
        ///     after the declared arguments parses and validates as
        ///     <typeparamref name="T"/> and collects, in input order, into one
        ///     array the handler reads with
        ///     <c>arguments.Get&lt;T[]&gt;(name)</c>. An invocation without
        ///     trailing tokens reads as an empty array; mark the argument
        ///     <see cref="CommandArgumentSpec{T}.Required"/> to demand at
        ///     least one token instead.
        /// </summary>
        /// <remarks>
        ///     The remaining argument must be the final declaration: later
        ///     <see cref="Arg{T}"/> calls, a second
        ///     <see cref="Remaining{T}"/> call, and
        ///     <see cref="CommandArgumentSpec{T}.Default"/> on the remaining
        ///     argument all throw at definition time.
        /// </remarks>
        public CommandBuilder Remaining<T>(string name, Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure = null)
        {
            EnsureArgumentNameAvailable(name);
            foreach (CommandArgument argument in _arguments)
            {
                if (argument.IsRemaining)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.InvalidRemainingArgument,
                        $"Command '{Name}': only one remaining argument is allowed; " + $"'{argument.Name}' already consumes every trailing token.",
                        Name,
                        argumentName: name
                    );
                }
            }

            CommandArgumentSpec<T> spec = ConfigureArgument(name, configure, isRemaining: true);
            if (spec.HasExplicitDefault)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidRemainingArgument,
                    $"Command '{Name}': the remaining argument '{name}' cannot declare a " + "default; an invocation without trailing tokens reads as an empty array.",
                    Name,
                    argumentName: name
                );
            }

            _arguments.Add(spec);
            return this;
        }

        /// <summary>
        ///     Declares a subcommand routed on this command's first argument.
        ///     The configured builder may itself declare arguments, a handler,
        ///     and further subcommands; it registers with the parent, never
        ///     separately. The parent's execution contexts and history policy
        ///     govern every subcommand, so setting
        ///     <see cref="Contexts"/> or <see cref="AddToHistory"/> inside the
        ///     callback throws, as does declaring parent arguments on a
        ///     command that has subcommands.
        /// </summary>
        public CommandBuilder Subcommand(string name, Action<CommandBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            string normalized = NormalizeName(Name, name);
            foreach (CommandBuilder existing in _subcommands)
            {
                if (string.Equals(existing.Name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{Name}': duplicate subcommand name '{normalized}'.",
                        Name,
                        subcommandName: normalized
                    );
                }
            }

            CommandBuilder subcommand = Create(normalized);
            try
            {
                configure(subcommand);
            }
            catch (CommandConfigurationException e)
            {
                throw WithComposedScope(e, normalized);
            }

            if (subcommand._contexts != CommandExecutionContextSets.Gameplay)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands run inside the parent's " + "execution contexts; do not set Contexts on a subcommand.",
                    Name,
                    subcommandName: normalized
                );
            }

            if (!subcommand._addToHistory)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands are recorded under the " + "parent's history policy; do not set AddToHistory on a subcommand.",
                    Name,
                    subcommandName: normalized
                );
            }

            if (subcommand._tier != CommandTier.Developer)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands inherit the parent's tier; do not set Tier on a subcommand.",
                    Name,
                    subcommandName: normalized
                );
            }

            if (subcommand._hideFromActions)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands inherit the parent's Actions visibility; do not set HideFromActions on a subcommand.",
                    Name,
                    subcommandName: normalized
                );
            }

            _subcommands.Add(subcommand);
            return this;
        }

        /// <summary>
        ///     Sets the handler. It runs only when every argument parsed and
        ///     validated; otherwise a controlled error reports the first
        ///     problem and the invocation ends.
        /// </summary>
        public CommandBuilder Handler(TypedCommandHandler handler)
        {
            _handler = handler;
            return this;
        }

        /// <summary>
        ///     Snapshots this definition into a
        ///     <see cref="CommandDefinition"/> registered against
        ///     <paramref name="owner"/>. Throws
        ///     <see cref="CommandConfigurationException"/> for configuration
        ///     errors; duplicate names against the live shell stay runtime
        ///     registration failures handled by the shell.
        /// </summary>
        internal CommandDefinition Build(CommandShell owner)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new CommandConfigurationException(CommandConfigurationFailure.EmptyName, "A command name is required; pass a non-empty name to CommandBuilder.Create.");
            }

            string name = NormalizeName(Name, Name, isSubcommandName: false);
            if (0 < _subcommands.Count)
            {
                return BuildRouterDefinition(owner, name);
            }

            return BuildLeafDefinition(owner, name);
        }

        private CommandDefinition BuildLeafDefinition(CommandShell owner, string name)
        {
            SubcommandRoute route = BuildLeafRoute(name, name);
            CommandArgument[] specs = route.Specs;
            CommandCompletionProvider provider = BuildCompletionProvider(route.CompletionStages, route.HasRemaining);
            TypedCommandHandler handler = route.Handler;

            CommandHandler dispatch = (context, arguments) => RunTyped(owner, specs, name, handler, context, arguments, 0);

            return new CommandDefinition
            {
                Name = name,
                Help = _help,
                Group = _group,
                Tier = _tier,
                HideFromActions = _hideFromActions,
                Hint = route.Usage,
                MinArgCount = route.RequiredCount,
                /*
                    A remaining argument consumes every trailing token, so the
                    command is unbounded at the shell level and the typed
                    dispatch reports per-element problems itself.
                 */
                MaxArgCount = route.HasRemaining ? (int?)null : specs.Length,
                AddToHistory = _addToHistory,
                Contexts = _contexts,
                Handler = dispatch,
                CompletionProvider = provider,
                SyntaxRoute = route,
            };
        }

        private CommandDefinition BuildRouterDefinition(CommandShell owner, string name)
        {
            if (0 < _arguments.Count)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{name}': a command with subcommands cannot declare its own " + "arguments; declare them on the subcommands.",
                    name
                );
            }

            SubcommandRoute self = BuildRouterRoute(name, name);
            TypedCommandHandler fallback = _handler;

            return new CommandDefinition
            {
                Name = name,
                Help = _help,
                Group = _group,
                Tier = _tier,
                HideFromActions = _hideFromActions,
                Hint = _hint ?? $"{name} <subcommand>",
                MinArgCount = 0,
                /*
                    Unbounded at the shell level: the router knows which leaf
                    owns the arguments only after routing, so it issues the
                    precise composed bounds errors itself.
                */
                MaxArgCount = null,
                AddToHistory = _addToHistory,
                Contexts = _contexts,
                Handler = (context, arguments) => RouteInvocation(owner, self, context, arguments),
                CompletionProvider = (in CommandCompletionContext context, List<CommandCompletion> results) => RouteCompletion(self.Lookup, context, 0, results),
                SyntaxRoute = self,
            };
        }

        /// <summary>
        ///     Builds this builder as one route of a parent's subcommand
        ///     router. Children never register on their own; the returned
        ///     route is consumed by the parent's dispatch and completion.
        /// </summary>
        private SubcommandRoute BuildRoute(string path)
        {
            string name = NormalizeName(path, Name);
            if (0 < _subcommands.Count)
            {
                return BuildRouterRoute(path, name);
            }

            return BuildLeafRoute(path, name);
        }

        private SubcommandRoute BuildLeafRoute(string path, string name)
        {
            CommandArgument[] specs = ValidateLeafConfiguration(path);
            string usage = _hint ?? BuildUsageHint(name, specs);
            return new SubcommandRoute
            {
                Name = name,
                FullPath = path,
                Help = _help,
                Example = _example,
                Usage = usage,
                PathUsage = _hint ?? (string.Equals(path, name, StringComparison.Ordinal) ? usage : BuildUsageHint(path, specs)),
                Specs = specs,
                RequiredCount = RequiredCount(specs),
                HasRemaining = HasRemaining(specs),
                Handler = _handler,
                CompletionStages = BuildSpecCompletionStages(specs),
            };
        }

        private SubcommandRoute BuildRouterRoute(string path, string name)
        {
            if (0 < _arguments.Count)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{path}': a command with subcommands cannot declare its own " + "arguments; declare them on the subcommands.",
                    path
                );
            }

            int childCount = _subcommands.Count;
            SubcommandRoute[] children = new SubcommandRoute[childCount];
            Dictionary<string, SubcommandRoute> lookup = new(childCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < childCount; ++i)
            {
                CommandBuilder child = _subcommands[i];
                SubcommandRoute route = child.BuildRoute($"{path} {child.Name}");
                if (!lookup.TryAdd(route.Name, route))
                {
                    /*
                        The Subcommand-time duplicate scan runs before each
                        configure callback, so re-entrant callbacks can slip a
                        second name past it; Build is the authoritative gate.
                    */
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{path}': duplicate subcommand name '{route.Name}'.",
                        path,
                        subcommandName: route.Name
                    );
                }

                children[i] = route;
            }

            return new SubcommandRoute
            {
                Name = name,
                FullPath = path,
                Help = _help,
                Example = _example,
                Usage = _hint ?? $"{name} <subcommand>",
                PathUsage = _hint ?? $"{path} <subcommand>",
                Fallback = _handler,
                Children = children,
                Lookup = lookup,
            };
        }

        private void EnsureArgumentNameAvailable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandConfigurationException(CommandConfigurationFailure.EmptyName, $"Command '{Name}': argument names must not be empty.", Name, argumentName: name);
            }

            foreach (CommandArgument argument in _arguments)
            {
                if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{Name}': duplicate argument name '{name}'.",
                        Name,
                        argumentName: name
                    );
                }
            }
        }

        private CommandArgumentSpec<T> ConfigureArgument<T>(string name, Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure, bool isRemaining)
        {
            CommandArgumentSpec<T> spec = new(name, isRemaining);
            if (configure != null)
            {
                try
                {
                    spec = configure(spec);
                }
                catch (CommandConfigurationException e)
                {
                    throw WithCommandScope(e, name);
                }

                if (spec == null)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.NullConfigurationResult,
                        $"Command '{Name}': the configuration callback for argument '{name}' returned null.",
                        Name,
                        argumentName: name
                    );
                }
            }

            try
            {
                spec.EnsureParseable();
            }
            catch (CommandConfigurationException e)
            {
                throw WithCommandScope(e, name);
            }

            return spec;
        }

        /*
            Spec-level misconfigurations throw without knowing their owning
            command; re-throwing through the builder attaches the command and
            argument scope so every definition-time failure reports the same
            structured identity. The original exception rides as the inner
            exception, keeping its throw-site stack.
         */
        private CommandConfigurationException WithCommandScope(CommandConfigurationException e, string argumentName)
        {
            return new CommandConfigurationException(
                e.Failure,
                e.Message,
                e.CommandName ?? Name,
                argumentName: e.ArgumentName ?? argumentName,
                subcommandName: e.SubcommandName,
                argumentType: e.ArgumentType,
                innerException: e
            );
        }

        /*
            Failures thrown while configuring a subcommand report the
            subcommand builder's own name; the parent composes the routed
            path here so the identity matches Build-time diagnostics, which
            name the composed path ('inventory add').
         */
        private CommandConfigurationException WithComposedScope(CommandConfigurationException e, string subcommandName)
        {
            string innerCommand = e.CommandName;
            return new CommandConfigurationException(
                e.Failure,
                e.Message,
                string.IsNullOrEmpty(innerCommand) ? $"{Name} {subcommandName}" : $"{Name} {innerCommand}",
                argumentName: e.ArgumentName,
                subcommandName: e.SubcommandName ?? subcommandName,
                argumentType: e.ArgumentType,
                innerException: e
            );
        }

        private CommandArgument[] ValidateLeafConfiguration(string path)
        {
            if (_handler == null)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.MissingHandler,
                    $"Command '{path}': set a handler with CommandBuilder.Handler before registration.",
                    path
                );
            }

            CommandArgument[] specs = _arguments.ToArray();

            /*
                Required arguments must form a leading block; a required
                argument after an optional one would make the derived bounds
                and defaults ambiguous.
             */
            bool seenOptional = false;
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired)
                {
                    if (seenOptional)
                    {
                        throw new CommandConfigurationException(
                            CommandConfigurationFailure.ArgumentOrdering,
                            $"Command '{path}': required argument '{spec.Name}' must be " + "declared before every optional argument.",
                            path,
                            argumentName: spec.Name
                        );
                    }

                    continue;
                }

                seenOptional = true;
            }

            /*
                Defaults are configuration, but the handler contract promises
                validated values, so a default must satisfy its argument's own
                validation. Surfacing the contradiction here beats failing the
                first omitted invocation.
             */
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired || spec.IsRemaining)
                {
                    continue;
                }

                string defaultError = spec.ValidateParsed(spec.GetDefault());
                if (defaultError != null)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.InvalidDefault,
                        $"Command '{path}': the default for argument '{spec.Name}' fails " + $"its own validation: {defaultError}",
                        path,
                        argumentName: spec.Name
                    );
                }
            }

            return specs;
        }

        /*
            One routing target of a subcommand command: either a leaf with
            typed argument specs and a handler, or a nested router with its
            own children.
        */
        internal sealed class SubcommandRoute
        {
            public string Name { get; internal set; }

            public string FullPath { get; internal set; }

            public string Help { get; internal set; }

            public string Example { get; internal set; }

            /// <summary>Usage within a route listing, e.g. <c>add &lt;item:string&gt;</c>.</summary>
            public string Usage { get; internal set; }

            /// <summary>Usage with the composed path for bounds errors, e.g. <c>inventory add &lt;item:string&gt;</c>.</summary>
            public string PathUsage { get; internal set; }

            public CommandArgument[] Specs { get; internal set; }

            public int RequiredCount { get; internal set; }

            /// <summary>True when the route's final argument is its unbounded trailing argument.</summary>
            public bool HasRemaining { get; internal set; }

            public TypedCommandHandler Handler { get; internal set; }

            public TypedCommandHandler Fallback { get; internal set; }

            public CommandCompletionProvider[] CompletionStages { get; internal set; } = Array.Empty<CommandCompletionProvider>();

            public SubcommandRoute[] Children { get; internal set; }

            public Dictionary<string, SubcommandRoute> Lookup { get; internal set; }

            public bool IsRouter => Children != null;
        }
    }
}
