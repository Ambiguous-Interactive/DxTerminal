namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    public sealed class TerminalSessionTests
    {
        /*
           Dynamically derived from RegisteredCommands so the tests stay in sync
           with any additions/removals of default commands in BuiltinCommands.cs.
         */
        private static readonly string[] KnownDefaultCommands = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .ToArray();

        private static readonly TerminalLogType[] AllLogTypes =
        {
            TerminalLogType.Error,
            TerminalLogType.Assert,
            TerminalLogType.Warning,
            TerminalLogType.Exception,
            TerminalLogType.Message,
            TerminalLogType.Input,
            TerminalLogType.ShellMessage,
        };

        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static TerminalSession.Config Config(
            int logBufferSize = 64,
            int historyBufferSize = 64,
            IReadOnlyList<TerminalLogType> ignoredLogTypes = null,
            IReadOnlyList<string> disabledCommands = null,
            bool ignoreDefaultCommands = false,
            TerminalStackTraceMode stackTraceMode = TerminalStackTraceMode.All
        )
        {
            return new TerminalSession.Config(logBufferSize, historyBufferSize, ignoredLogTypes, disabledCommands, ignoreDefaultCommands, stackTraceMode);
        }

        [SetUp]
        public void SetUp()
        {
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
        }

        [TearDown]
        public void TearDown()
        {
            Terminal.Buffer = _originalBuffer;
            Terminal.Shell = _originalShell;
            Terminal.History = _originalHistory;
            Terminal.AutoComplete = _originalAutoComplete;
        }

        [Test]
        public void ApplyCreatesAllBackendsWhenNull()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: false);

            Assert.That(session.Buffer != null, "Buffer should be created when null");
            Assert.That(session.History != null, "History should be created when null");
            Assert.That(session.Shell != null, "Shell should be created when null");
            Assert.That(session.AutoComplete != null, "AutoComplete should be created when null");
            Assert.AreEqual(64, session.Buffer.Capacity, "Buffer capacity should match the configured log buffer size");
            Assert.AreEqual(64, session.History.Capacity, "History capacity should match the configured history buffer size");
        }

        [Test]
        public void ApplyClampsInvalidBufferSizesToZero()
        {
            TerminalSession session = new();
            session.Apply(Config(logBufferSize: -5, historyBufferSize: -1), force: false);

            Assert.AreEqual(0, session.Buffer.Capacity, "A negative log buffer size must clamp to zero");
            Assert.AreEqual(0, session.History.Capacity, "A negative history buffer size must clamp to zero");
        }

        [Test]
        public void ApplyIsIdempotentForUnchangedConfig()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();

            CommandLog buffer = session.Buffer;
            CommandHistory history = session.History;
            CommandShell shell = session.Shell;
            CommandAutoComplete autoComplete = session.AutoComplete;

            bool reconfigured = session.Apply(Config(), force: false);

            Assert.IsFalse(reconfigured, "An unchanged configuration must not reconfigure the shell");
            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused");
            Assert.AreSame(history, session.History, "History should be reused");
            Assert.AreSame(shell, session.Shell, "Shell should be reused");
            Assert.AreSame(autoComplete, session.AutoComplete, "AutoComplete should be reused");
        }

        [TestCase(64, 32)]
        [TestCase(32, 128)]
        public void ApplyResizesBufferCapacity(int initialSize, int newSize)
        {
            TerminalSession session = new();
            session.Apply(Config(logBufferSize: initialSize), force: true);
            CommandLog buffer = session.Buffer;

            session.Apply(Config(logBufferSize: newSize), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be resized, not recreated");
            Assert.AreEqual(newSize, session.Buffer.Capacity);
        }

        [TestCase(64, 32)]
        [TestCase(32, 128)]
        public void ApplyResizesHistoryCapacity(int initialSize, int newSize)
        {
            TerminalSession session = new();
            session.Apply(Config(historyBufferSize: initialSize), force: true);
            CommandHistory history = session.History;

            session.Apply(Config(historyBufferSize: newSize), force: false);

            Assert.AreSame(history, session.History, "History should be resized, not recreated");
            Assert.AreEqual(newSize, session.History.Capacity);
        }

        [Test]
        public void ApplySyncsIgnoredLogTypesOnSameBuffer()
        {
            TerminalSession session = new();
            session.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.Error }), force: true);
            CommandLog buffer = session.Buffer;
            Assert.IsFalse(buffer.HandleLog("dropped", TerminalLogType.Error), "Sanity: an ignored type must be dropped while configured");
            Assert.AreEqual(0, buffer.Count);

            session.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.Warning }), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused when syncing filters");
            Assert.IsTrue(buffer.HandleLog("kept", TerminalLogType.Error), "A previously ignored type should be accepted after resync");
            Assert.IsFalse(buffer.HandleLog("dropped", TerminalLogType.Warning), "A newly ignored type must be dropped after resync");
        }

        [Test]
        public void ApplySyncsStackTraceModeOnSameBuffer()
        {
            TerminalSession session = new();
            session.Apply(Config(stackTraceMode: TerminalStackTraceMode.All), force: true);
            CommandLog buffer = session.Buffer;
            Assert.IsTrue(buffer.HandleLog("traced", TerminalLogType.ShellMessage), "Sanity: the traced write must land");
            buffer.TryGetLast(out LogItem traced);
            Assert.AreNotEqual(string.Empty, traced.stackTrace, "Sanity: mode All must capture a trace");

            session.Apply(Config(stackTraceMode: TerminalStackTraceMode.Disabled), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused when syncing mode");
            Assert.AreEqual(TerminalStackTraceMode.Disabled, buffer.StackTraceMode);
            Assert.IsTrue(buffer.HandleLog("untraced", TerminalLogType.ShellMessage), "Sanity: the untraced write must land");
            buffer.TryGetLast(out LogItem untraced);
            Assert.AreEqual(string.Empty, untraced.stackTrace, "Mode Disabled applied through resync must store no trace");

            session.Apply(Config(stackTraceMode: TerminalStackTraceMode.ErrorsAndWarnings), force: false);

            Assert.AreEqual(TerminalStackTraceMode.ErrorsAndWarnings, buffer.StackTraceMode);
        }

        [Test]
        public void ApplyLandsModeAndFilterChangesTogether()
        {
            TerminalSession session = new();
            session.Apply(Config(stackTraceMode: TerminalStackTraceMode.All), force: true);
            CommandLog buffer = session.Buffer;

            /*
                One apply carries both changes. The two reads the old shape
                made outside the lock (SetEquals on the filter, then the
                replace) are gone: whatever a logging thread sees after the
                apply is one applied configuration, never a half of two.
             */
            session.Apply(Config(stackTraceMode: TerminalStackTraceMode.Disabled, ignoredLogTypes: new[] { TerminalLogType.Warning }), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused when syncing config");
            Assert.AreEqual(TerminalStackTraceMode.Disabled, buffer.StackTraceMode);
            foreach (TerminalLogType type in AllLogTypes)
            {
                Assert.AreEqual(type == TerminalLogType.Warning, buffer.IsIgnored(type), $"The applied filter must replace the old one whole (IsIgnored({type}))");
            }
        }

        [Test]
        public void ApplyTreatsNullListsAsEmpty()
        {
            TerminalSession session = new();
            session.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.Error }, disabledCommands: new[] { "help" }), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsFalse(session.Buffer.HandleLog("dropped", TerminalLogType.Error), "Sanity: the ignored type must be dropped while configured");
            Assert.IsTrue(session.Shell.IgnoredCommands.Contains("help"), "Sanity: the disabled command must be ignored while configured");

            session.Apply(Config(), force: false);

            Assert.IsTrue(session.Buffer.HandleLog("kept", TerminalLogType.Error), "A null ignored list must clear the buffer's ignored types");
            Assert.IsEmpty(session.Shell.IgnoredCommands, "A null disabled list must clear the shell's ignored commands");
        }

        [Test]
        public void ApplyReconfiguresShellWhenDisabledCommandsChange()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(session.Shell.Commands.ContainsKey("log"), "Sanity: 'log' should be registered before being disabled");

            bool reconfigured = session.Apply(Config(disabledCommands: new[] { "log" }), force: false);

            Assert.IsTrue(reconfigured, "Changing disabled commands must reconfigure the shell");
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsFalse(session.Shell.Commands.ContainsKey("log"), "A disabled command must not be registered");
            Assert.IsTrue(session.Shell.IgnoredCommands.Contains("log"), "The disabled command should be reported in IgnoredCommands");

            reconfigured = session.Apply(Config(), force: false);

            Assert.IsTrue(reconfigured, "Clearing disabled commands must reconfigure the shell");
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(session.Shell.Commands.ContainsKey("log"), "A re-enabled command should be registered again");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ApplyAppliesIgnoreDefaultCommands(bool ignoreDefaultCommands)
        {
            TerminalSession session = new();
            session.Apply(Config(ignoreDefaultCommands: ignoreDefaultCommands), force: true);
            session.Shell.EnsureAutoCommandsRegistered();

            Assert.AreEqual(ignoreDefaultCommands, session.Shell.IgnoringDefaultCommands, "Shell.IgnoringDefaultCommands should match the applied configuration");
            Assert.IsNotEmpty(KnownDefaultCommands, "Sanity: expected at least one default command");
            foreach (string command in KnownDefaultCommands)
            {
                Assert.AreEqual(
                    !ignoreDefaultCommands,
                    session.Shell.Commands.ContainsKey(command),
                    $"Default command '{command}' should {(ignoreDefaultCommands ? "not " : string.Empty)}be registered"
                );
            }
        }

        [Test]
        public void ApplyForceRecreatesAllBackends()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            session.Buffer.HandleLog("before", TerminalLogType.ShellMessage);

            CommandLog buffer = session.Buffer;
            CommandHistory history = session.History;
            CommandShell shell = session.Shell;
            CommandAutoComplete autoComplete = session.AutoComplete;

            bool reconfigured = session.Apply(Config(), force: true);

            Assert.IsTrue(reconfigured, "Force must reconfigure the recreated shell so its auto-command " + "configuration is applied");
            Assert.AreNotSame(buffer, session.Buffer, "Force must recreate the buffer");
            Assert.AreNotSame(history, session.History, "Force must recreate the history");
            Assert.AreNotSame(shell, session.Shell, "Force must recreate the shell");
            Assert.AreNotSame(autoComplete, session.AutoComplete, "Force must recreate the auto-complete");
            Assert.AreEqual(0, session.Buffer.Count, "A recreated buffer must not retain the previous buffer's logs");
        }

        [Test]
        public void ApplyPreservesHistoryWhenReconfiguringShell()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(session.Shell.RunCommand("log"), "Sanity: 'log' should run to create a history entry");
            CommandHistory history = session.History;
            string[] events = session.History.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.IsNotEmpty(events, "Sanity: expected a history entry after running 'log'");

            session.Apply(Config(disabledCommands: new[] { "log" }), force: false);

            Assert.AreSame(history, session.History, "A shell reconfiguration must preserve the history instance");
            string[] currentEvents = session.History.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.AreEqual(events.Length, currentEvents.Length, "History entries must survive a shell reconfiguration");
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
        }

        [Test]
        public void ApplyDefersDiscoveryUntilFirstCommandRequest()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);

            Assert.IsFalse(session.Shell.AutoCommandsRegistered, "Apply must not walk the command catalog; registration is deferred");

            session.Shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(session.Shell.AutoCommandsRegistered, "The first explicit readiness request should complete registration");
            Assert.IsNotEmpty(session.Shell.AutoRegisteredCommands, "Deferred registration should surface auto commands");
        }

        [Test]
        public void FacadeDelegatesToCurrentSession()
        {
            CommandLog buffer = new(32, null);
            CommandHistory history = new(32);
            CommandShell shell = new(history);
            CommandAutoComplete autoComplete = new(history, shell);

            Terminal.Buffer = buffer;
            Terminal.History = history;
            Terminal.Shell = shell;
            Terminal.AutoComplete = autoComplete;

            Assert.AreSame(buffer, Terminal.Buffer, "Terminal.Buffer should round-trip");
            Assert.AreSame(history, Terminal.History, "Terminal.History should round-trip");
            Assert.AreSame(shell, Terminal.Shell, "Terminal.Shell should round-trip");
            Assert.AreSame(autoComplete, Terminal.AutoComplete, "Terminal.AutoComplete should round-trip");
            Assert.AreSame(buffer, TerminalSession.Current.Buffer, "Terminal.Buffer should delegate to the current session");
            Assert.AreSame(history, TerminalSession.Current.History, "Terminal.History should delegate to the current session");
            Assert.AreSame(shell, TerminalSession.Current.Shell, "Terminal.Shell should delegate to the current session");
            Assert.AreSame(autoComplete, TerminalSession.Current.AutoComplete, "Terminal.AutoComplete should delegate to the current session");
        }

        [Test]
        public void ApplyReconfiguresShellWhenAutoCommandsNeverRegistered()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);

            bool reconfigured = session.Apply(Config(), force: false);

            Assert.IsTrue(reconfigured, "A shell with pending registration must reapply its auto-command " + "configuration on every refresh");
        }

        /*
            The explicit readiness boundary for headless callers: one call
            creates every backend and completes deferred auto-command
            registration synchronously, so command requests and logging work
            before any TerminalUI enables.
         */
        [Test]
        public void EnsureReadyCreatesBackendsAndRegistersCommandsSynchronously()
        {
            TerminalSession session = new();
            session.EnsureReady(Config(), force: false);

            Assert.That(session.Buffer != null, "EnsureReady must create the buffer");
            Assert.That(session.History != null, "EnsureReady must create the history");
            Assert.That(session.Shell != null, "EnsureReady must create the shell");
            Assert.That(session.AutoComplete != null, "EnsureReady must create the auto-complete");
            Assert.IsTrue(session.Shell.AutoCommandsRegistered, "EnsureReady must complete deferred registration synchronously");
            Assert.IsNotEmpty(session.Shell.AutoRegisteredCommands, "EnsureReady should surface auto commands");
        }

        [Test]
        public void EnsureReadyIsIdempotent()
        {
            TerminalSession session = new();
            session.EnsureReady(Config(), force: true);

            CommandLog buffer = session.Buffer;
            CommandHistory history = session.History;
            CommandShell shell = session.Shell;
            CommandAutoComplete autoComplete = session.AutoComplete;

            session.EnsureReady(Config(), force: false);

            Assert.AreSame(buffer, session.Buffer, "A repeated readiness call reuses the buffer");
            Assert.AreSame(history, session.History, "A repeated readiness call reuses the history");
            Assert.AreSame(shell, session.Shell, "A repeated readiness call reuses the shell");
            Assert.AreSame(autoComplete, session.AutoComplete, "A repeated readiness call reuses the auto-complete");
            Assert.IsTrue(session.Shell.AutoCommandsRegistered, "A repeated readiness call keeps registration complete");
        }

        [Test]
        public void ResetClearsLogHistoryAndVariablesWhilePreservingShellAndCommands()
        {
            TerminalSession session = TerminalSession.Current;
            session.Apply(Config(), force: true);
            CommandShell originalShell = Terminal.Shell;
            CommandLog originalBuffer = Terminal.Buffer;
            CommandHistory originalHistory = Terminal.History;
            CommandAutoComplete originalAutoComplete = Terminal.AutoComplete;

            int invocations = 0;
            CommandBuilder command = CommandBuilder
                .Create("reset-survives")
                .Contexts(CommandExecutionContextSets.All)
                .Handler(
                    (CommandExecutionContext context, CommandArguments arguments) =>
                    {
                        ++invocations;
                    }
                );
            Assert.IsTrue(originalShell.AddCommand(command, out CommandRegistrationHandle handle), "Sanity: custom builder command should register");
            try
            {
                originalShell.SetVariable("hp", new CommandArg("100"));
                Terminal.Log("entry before reset");
                Assert.IsTrue(originalShell.RunCommand("reset-survives"), "Sanity: custom command should run before reset");
                Assert.AreEqual(1, invocations);
                Assert.That(0 < originalBuffer.Count, "Sanity: buffer should contain an entry before reset");
                Assert.That(0 < originalHistory.Count, "Sanity: history should contain an entry before reset");
                Assert.IsTrue(originalShell.Variables.ContainsKey("hp"), "Sanity: variable should exist before reset");

                Terminal.Reset();

                Assert.AreSame(originalShell, Terminal.Shell, "Reset must preserve the shell instance");
                Assert.AreSame(originalBuffer, Terminal.Buffer, "Reset must preserve the buffer instance");
                Assert.AreSame(originalHistory, Terminal.History, "Reset must preserve the history instance");
                Assert.AreSame(originalAutoComplete, Terminal.AutoComplete, "Reset must preserve the auto-complete instance");
                Assert.AreEqual(0, originalBuffer.Count, "Reset must clear the log buffer");
                Assert.AreEqual(0, originalHistory.Count, "Reset must clear command history");
                Assert.IsFalse(originalShell.Variables.ContainsKey("hp"), "Reset must clear shell variables");
                Assert.IsTrue(originalShell.Commands.ContainsKey("reset-survives"), "Reset must keep builder registrations");
                Assert.IsTrue(originalShell.RunCommand("reset-survives"), "Builder commands must still run after reset");
                Assert.AreEqual(2, invocations);
            }
            finally
            {
                handle.Dispose();
            }
        }

        [Test]
        public void ResetIsSafeWhenBackendsAreNull()
        {
            Terminal.Buffer = null;
            Terminal.History = null;
            Terminal.Shell = null;
            Terminal.AutoComplete = null;

            Assert.DoesNotThrow(() =>
            {
                Terminal.Reset();
            });
        }

        [Test]
        public void TypedLogOverloadsFormatAndDeliverMessages()
        {
            TerminalSession.Current.Apply(Config(), force: true);

            Assert.IsTrue(Terminal.Log("plain"));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem plain));
            Assert.AreEqual(TerminalLogType.ShellMessage, plain.type);
            Assert.AreEqual("plain", plain.message);

            Assert.IsTrue(Terminal.Log("one:{0}", 7));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem one));
            Assert.AreEqual("one:7", one.message);

            Assert.IsTrue(Terminal.Log("two:{0},{1}", "a", 2));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem two));
            Assert.AreEqual("two:a,2", two.message);

            Assert.IsTrue(Terminal.Log("three:{0},{1},{2}", 1, true, "z"));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem three));
            Assert.AreEqual("three:1,True,z", three.message);

            Assert.IsTrue(Terminal.Log(TerminalLogType.Warning, "typed-plain"));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem typedPlain));
            Assert.AreEqual(TerminalLogType.Warning, typedPlain.type);
            Assert.AreEqual("typed-plain", typedPlain.message);

            Assert.IsTrue(Terminal.Log(TerminalLogType.Message, "typed-one:{0}", 9));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem typedOne));
            Assert.AreEqual(TerminalLogType.Message, typedOne.type);
            Assert.AreEqual("typed-one:9", typedOne.message);

            Assert.IsTrue(Terminal.Log(TerminalLogType.Error, "typed-two:{0},{1}", "x", 4));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem typedTwo));
            Assert.AreEqual(TerminalLogType.Error, typedTwo.type);
            Assert.AreEqual("typed-two:x,4", typedTwo.message);

            Assert.IsTrue(Terminal.Log(TerminalLogType.Warning, "typed-three:{0},{1},{2}", 1, 2, 3));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem typedThree));
            Assert.AreEqual(TerminalLogType.Warning, typedThree.type);
            Assert.AreEqual("typed-three:1,2,3", typedThree.message);
        }

        [Test]
        public void TypedLogOverloadsSkipFormattingWhenTypeIsIgnored()
        {
            TerminalSession.Current.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.ShellMessage, TerminalLogType.Warning }), force: true);

            int[] calls = new int[1];
            FormatSpy spy = new(calls);

            Assert.IsFalse(Terminal.Log("ignored"));
            Assert.IsFalse(Terminal.Log("{0}", spy));
            Assert.IsFalse(Terminal.Log("{0} {1}", spy, 2));
            Assert.IsFalse(Terminal.Log("{0} {1} {2}", spy, 2, 3));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Warning, "ignored"));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Warning, "{0}", spy));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Warning, "{0} {1}", spy, 2));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Warning, "{0} {1} {2}", spy, 2, 3));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Warning, "{0} {1} {2} {3}", spy, 2, 3, 4));

            Assert.AreEqual(0, calls[0], "Ignored log types must return before formatting arguments");
            Assert.AreEqual(0, Terminal.Buffer.Count);
        }

        [Test]
        public void ObjectArrayStillBindsToParamsLogOverload()
        {
            TerminalSession.Current.Apply(Config(), force: true);
            object[] values = { "alpha", 42 };

            Assert.IsTrue(Terminal.Log("{0}-{1}", values));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem defaultType));
            Assert.AreEqual("alpha-42", defaultType.message);

            Assert.IsTrue(Terminal.Log(TerminalLogType.Message, "{0}-{1}", values));
            Assert.IsTrue(Terminal.Buffer.TryGetLast(out LogItem explicitType));
            Assert.AreEqual("alpha-42", explicitType.message);
        }

        [Test]
        public void TypedLogOverloadsReturnFalseWhenBufferIsNull()
        {
            Terminal.Buffer = null;
            int[] calls = new int[1];
            FormatSpy spy = new(calls);

            Assert.IsFalse(Terminal.Log("missing"));
            Assert.IsFalse(Terminal.Log("{0}", spy));
            Assert.IsFalse(Terminal.Log("{0} {1}", spy, 1));
            Assert.IsFalse(Terminal.Log("{0} {1} {2}", spy, 1, 2));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Message, "missing"));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Message, "{0}", spy));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Message, "{0} {1}", spy, 1));
            Assert.IsFalse(Terminal.Log(TerminalLogType.Message, "{0} {1} {2}", spy, 1, 2));
            Assert.AreEqual(0, calls[0], "Null buffer must return before formatting arguments");
        }

        private readonly struct FormatSpy
        {
            private readonly int[] _calls;

            public FormatSpy(int[] calls)
            {
                _calls = calls;
            }

            public override string ToString()
            {
                ++_calls[0];
                return "formatted";
            }
        }
    }
}
