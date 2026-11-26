namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public readonly struct CommandInfo
    {
        internal string Group
        {
            get { return group; }
        }

        internal CommandBuilder.SubcommandRoute SyntaxRoute { get; }

        public readonly Action<CommandArg[]> proc;
        public readonly int minArgCount;

        /// <summary>
        ///     Maximum number of arguments the command accepts, or
        ///     <c>null</c> for unbounded. Legacy registrations that pass a
        ///     negative value are normalized to <c>null</c>.
        /// </summary>
        public readonly int? maxArgCount;

        public readonly string help;
        public readonly string hint;
        public readonly bool addToHistory;

        /// <summary>
        ///     Context-aware handler. Null for commands registered through the
        ///     legacy <see cref="CommandArg[]"/> surface, which receive a
        ///     materialized array instead.
        /// </summary>
        public readonly CommandHandler handler;

        /// <summary>
        ///     Synchronous argument completion provider. Null leaves completion
        ///     to the history-based suggestions shared by every command
        ///     without a provider.
        /// </summary>
        public readonly CommandCompletionProvider completionProvider;

        /// <summary>
        ///     Environments the command may run in. Commands registered without
        ///     explicit context metadata keep <see cref="CommandExecutionContextSets.All"/>,
        ///     their previous availability everywhere.
        /// </summary>
        public readonly CommandExecutionContexts executionContexts;

        /// <summary>Audience tier for filtering in build profiles and UI surfaces.</summary>
        public readonly CommandTier tier;

        /// <summary>Optional group label shown in completion lists and Actions.</summary>
        public readonly string group;

        /// <summary>When true, omits the command from the Actions surface.</summary>
        public readonly bool hideFromActions;

        public CommandInfo(Action<CommandArg[]> proc, int minArgCount, int? maxArgCount, string help, string hint, bool addToHistory = true)
            : this(proc, handler: null, completionProvider: null, CommandExecutionContextSets.All, minArgCount, maxArgCount, help, hint, addToHistory) { }

        public CommandInfo(
            Action<CommandArg[]> proc,
            CommandHandler handler,
            CommandCompletionProvider completionProvider,
            CommandExecutionContexts executionContexts,
            int minArgCount,
            int? maxArgCount,
            string help,
            string hint,
            bool addToHistory = true
        )
            : this(
                proc,
                handler,
                completionProvider,
                executionContexts,
                minArgCount,
                maxArgCount,
                help,
                hint,
                addToHistory,
                syntaxRoute: null,
                group: null,
                tier: CommandTier.Developer,
                hideFromActions: false
            ) { }

        internal CommandInfo(
            Action<CommandArg[]> proc,
            CommandHandler handler,
            CommandCompletionProvider completionProvider,
            CommandExecutionContexts executionContexts,
            int minArgCount,
            int? maxArgCount,
            string help,
            string hint,
            bool addToHistory,
            CommandBuilder.SubcommandRoute syntaxRoute,
            string group = null,
            CommandTier tier = CommandTier.Developer,
            bool hideFromActions = false
        )
        {
            SyntaxRoute = syntaxRoute;
            this.group = group;
            this.tier = NormalizeTier(tier);
            this.hideFromActions = hideFromActions;
            this.proc = proc;
            this.handler = handler;
            this.completionProvider = completionProvider;
            this.executionContexts = executionContexts;
            this.maxArgCount = NormalizeMaxArgCount(maxArgCount);
            this.minArgCount = minArgCount;
            this.help = help;
            this.hint = hint;
            this.addToHistory = addToHistory;
        }

        private static CommandTier NormalizeTier(CommandTier tier)
        {
            return tier is CommandTier.Player or CommandTier.Tester or CommandTier.Developer ? tier : CommandTier.Developer;
        }

        /// <summary>
        ///     Negative bounds were the legacy "unbounded" spelling; they
        ///     normalize to <c>null</c> so the sentinel has no meaning here.
        /// </summary>
        private static int? NormalizeMaxArgCount(int? maxArgCount)
        {
            return maxArgCount is int bound && 0 <= bound ? bound : null;
        }
    }
}
