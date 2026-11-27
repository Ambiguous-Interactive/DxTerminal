namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using Helper;

    /*
        Pure-logic helpers for the Watches tab and HUD (#244, board S3):
        the command list line, the pinned HUD row order under the max-rows
        setting, and the per-role USS class a value cell carries. The view
        reads registry entries directly; only these rules live here so the
        engine-free fixtures can pin them.
     */
    internal static class TerminalWatchesModel
    {
        internal const string UnsampledValueText = "(not sampled)";
        internal const string PinLabel = "Pin";
        internal const string PinnedLabel = "Pinned";

        /// <summary>Formats one <c>watches</c> command output line: name, value, source, pin state.</summary>
        internal static string FormatListLine(CommandWatchRegistry.WatchEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(64);
            StringBuilder builder = scope.Builder;
            builder.Append(entry.Name).Append(" = ");
            if (entry.HasValue)
            {
                builder.Append(entry.ValueText);
            }
            else
            {
                builder.Append(UnsampledValueText);
            }

            builder.Append(" · ").Append(entry.Source).Append(" · ");
            builder.Append(entry.Pinned ? PinnedLabel : PinLabel);
            return builder.ToString();
        }

        /// <summary>
        ///     Copies the pinned entries, in registration order, capped at
        ///     <paramref name="maxRows"/>, into <paramref name="output"/>.
        ///     <paramref name="entries"/> must already be tier-filtered.
        /// </summary>
        internal static void BuildHudRows(IReadOnlyList<CommandWatchRegistry.WatchEntry> entries, int maxRows, List<CommandWatchRegistry.WatchEntry> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            if (entries == null || maxRows <= 0)
            {
                return;
            }

            int entryCount = entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                if (output.Count == maxRows)
                {
                    return;
                }

                CommandWatchRegistry.WatchEntry entry = entries[index];
                if (entry.Pinned)
                {
                    output.Add(entry);
                }
            }
        }

        /// <summary>The USS class suffix a value cell carries for its syntax role.</summary>
        internal static string ValueClassName(CommandWatchRegistry.WatchValueRole role)
        {
            return role switch
            {
                CommandWatchRegistry.WatchValueRole.Number => "number",
                CommandWatchRegistry.WatchValueRole.String => "string",
                CommandWatchRegistry.WatchValueRole.Choice => "choice",
                _ => "body",
            };
        }
    }
}
