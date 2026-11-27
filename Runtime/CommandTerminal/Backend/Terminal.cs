namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using JetBrains.Annotations;

    public static class Terminal
    {
        public static CommandLog Buffer
        {
            get { return TerminalSession.Current.Buffer; }
            internal set { TerminalSession.Current.Buffer = value; }
        }

        public static CommandShell Shell
        {
            get { return TerminalSession.Current.Shell; }
            internal set { TerminalSession.Current.Shell = value; }
        }

        public static CommandHistory History
        {
            get { return TerminalSession.Current.History; }
            internal set { TerminalSession.Current.History = value; }
        }

        public static CommandAutoComplete AutoComplete
        {
            get { return TerminalSession.Current.AutoComplete; }
            internal set { TerminalSession.Current.AutoComplete = value; }
        }

        /// <summary>
        ///     The session's watch registry (milestone C4, #244). Reads null
        ///     until the first <see cref="TerminalSession.Apply"/>, exactly
        ///     like the other facade getters.
        /// </summary>
        public static CommandWatchRegistry Watches
        {
            get { return TerminalSession.Current.Watches; }
            internal set { TerminalSession.Current.Watches = value; }
        }

        /// <summary>
        ///     Registers a watch and returns the handle that removes it.
        ///     Throws when no session backends exist yet (an Edit Mode call
        ///     before any terminal applied its configuration); Play Mode
        ///     backends exist from any Awake onward.
        /// </summary>
        public static WatchHandle Watch<T>(string name, Func<T> getter, string source = null, string format = null, CommandTier tier = CommandTier.Developer)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }

            CommandWatchRegistry watches = TerminalSession.Current.Watches;
            if (watches == null)
            {
                throw new InvalidOperationException("No watch registry is ready. Enable a TerminalUI or call TerminalSession.Current.EnsureReady before Terminal.Watch.");
            }

            return watches.Watch(name, getter, source, format, tier);
        }

        /// <summary>
        ///     Clears the log buffer, command history, and shell variables in place
        ///     without replacing the shell or dropping registered commands.
        /// </summary>
        public static void Reset()
        {
            CommandLog buffer = Buffer;
            if (buffer != null)
            {
                buffer.Clear();
            }

            CommandHistory history = History;
            if (history != null)
            {
                history.Clear();
            }

            CommandShell shell = Shell;
            if (shell != null)
            {
                shell.ClearVariables();
            }
        }

        public static bool Log(string message)
        {
            return Log(TerminalLogType.ShellMessage, message);
        }

        public static bool Log(TerminalLogType type, string message)
        {
            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
            }

            return buffer.HandleLog(message, type);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0>(string format, T0 arg0)
        {
            return Log(TerminalLogType.ShellMessage, format, arg0);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0>(TerminalLogType type, string format, T0 arg0)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
            }

            if (arg0 is object[] objectArray)
            {
                string formatted = 0 < objectArray.Length ? string.Format(format, objectArray) : format;
                return buffer.HandleLog(formatted, type);
            }

            return buffer.HandleLog(string.Format(format, arg0), type);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0, T1>(string format, T0 arg0, T1 arg1)
        {
            return Log(TerminalLogType.ShellMessage, format, arg0, arg1);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0, T1>(TerminalLogType type, string format, T0 arg0, T1 arg1)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
            }

            return buffer.HandleLog(string.Format(format, arg0, arg1), type);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0, T1, T2>(string format, T0 arg0, T1 arg1, T2 arg2)
        {
            return Log(TerminalLogType.ShellMessage, format, arg0, arg1, arg2);
        }

        [StringFormatMethod("format")]
        public static bool Log<T0, T1, T2>(TerminalLogType type, string format, T0 arg0, T1 arg1, T2 arg2)
        {
            if (format == null)
            {
                throw new ArgumentNullException(nameof(format));
            }

            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
            }

            return buffer.HandleLog(string.Format(format, arg0, arg1, arg2), type);
        }

        [StringFormatMethod("format")]
        public static bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }

        [StringFormatMethod("format")]
        public static bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
            }

            string formattedMessage = parameters != null && 0 < parameters.Length ? string.Format(format, parameters) : format;
            return buffer.HandleLog(formattedMessage, type);
        }
    }
}
