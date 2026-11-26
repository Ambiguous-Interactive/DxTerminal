namespace WallstopStudios.DxCommandTerminal.Backend
{
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
            CommandLog buffer = Buffer;
            if (buffer == null || buffer.IsIgnored(type))
            {
                return false;
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
