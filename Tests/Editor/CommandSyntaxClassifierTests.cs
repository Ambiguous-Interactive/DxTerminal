namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using CommandSyntaxRole = Backend.CommandSyntaxClassifier.CommandSyntaxRole;
    using CommandSyntaxSpan = Backend.CommandSyntaxClassifier.CommandSyntaxSpan;

    public sealed class CommandSyntaxClassifierTests
    {
        private static CommandArgumentSpec<int> GainRange(CommandArgumentSpec<int> spec)
        {
            return spec.Range(-20, 20);
        }

        [TestCase("inspect", "Command:inspect", "")]
        [TestCase("missing abc", "UnknownCommand:missing|String:abc", null)]
        [TestCase("mode true", "Command:mode|Choice:true", "true")]
        [TestCase("day Friday", "Command:day|Choice:Friday", "Friday")]
        [TestCase("day 5", "Command:day|Choice:5", "5")]
        [TestCase("day monday", "Command:day|Choice:monday", "monday")]
        [TestCase("day someday", "Command:day|Invalid:someday", null)]
        [TestCase("inspect $", "Command:inspect|String:$", "$")]
        [TestCase("point zero", "Command:point|Choice:zero", "zero")]
        [TestCase("gain -12", "Command:gain|Number:-12", "-12")]
        [TestCase("gain 21", "Command:gain|Invalid:21", null)]
        [TestCase("gain abc", "Command:gain|Invalid:abc", null)]
        [TestCase("gain 12 extra", "Command:gain|Number:12|Invalid:extra", null)]
        [TestCase("gain $current", "Command:gain|Variable:$current", "7")]
        [TestCase("gain $missing", "Command:gain|Invalid:$missing", null)]
        [TestCase("gain \"$current\"", "Command:gain|Punctuation:\"|Invalid:$current|Punctuation:\"", null)]
        [TestCase("inspect text", "Command:inspect|String:text", "text")]
        [TestCase("inspect \"two words\"", "Command:inspect|Punctuation:\"|String:two words|Punctuation:\"", "two words")]
        [TestCase("inspect \"two words", "Command:inspect|Punctuation:\"|String:two words", "two words")]
        [TestCase("inspect \"😀\"", "Command:inspect|Punctuation:\"|String:😀|Punctuation:\"", "😀", 10)]
        [TestCase(
            "point \"(1, 2,-3)\"",
            "Command:point|Punctuation:\"|Punctuation:(|Number:1|Punctuation:,|Number: 2|Punctuation:,|Number:-3|Punctuation:)|Punctuation:\"",
            "(1, 2,-3)"
        )]
        [TestCase("point \"<1,2,3>\"", "Command:point|Punctuation:\"|Punctuation:<|Number:1|Punctuation:,|Number:2|Punctuation:,|Number:3|Punctuation:>|Punctuation:\"", "<1,2,3>")]
        [TestCase("tool $action 2", "Command:tool|Variable:$action|Number:2", "2")]
        [TestCase("tool $missing 2", "Command:tool|Invalid:$missing|String:2", null)]
        [TestCase("tool set 2 3", "Command:tool|Choice:set|Number:2|Number:3", "2;3")]
        [TestCase("tool set 2 wrong", "Command:tool|Choice:set|Number:2|Invalid:wrong", null)]
        [TestCase("tool wrong 2", "Command:tool|Invalid:wrong|String:2", null)]
        [TestCase("\"\"tail", "Punctuation:\"|Punctuation:\"|String:tail", null)]
        [TestCase("", "", null)]
        public void ClassifyMatchesShellDispatch(string line, string expectedSpans, string expectedArguments, int caret = 0)
        {
            CommandHistory history = new(16);
            CommandShell shell = new(history);
            string actualArguments = null;
            TypedCommandHandler observe = (_, arguments) =>
            {
                StringBuilder raw = new();
                for (int index = 0; index < arguments.Raw.Count; ++index)
                {
                    if (0 < index)
                    {
                        raw.Append(';');
                    }

                    raw.Append(arguments.Raw[index].contents);
                }

                actualArguments = raw.ToString();
            };

            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("inspect").Contexts(CommandExecutionContextSets.All).Arg<string>("text").Handler(observe), out _));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("mode").Contexts(CommandExecutionContextSets.All).Arg<bool>("enabled").Handler(observe), out _));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("day").Contexts(CommandExecutionContextSets.All).Arg<DayOfWeek>("day").Handler(observe), out _));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("gain").Contexts(CommandExecutionContextSets.All).Arg<int>("value", GainRange).Handler(observe), out _));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("point").Contexts(CommandExecutionContextSets.All).Arg<Vector3>("position").Handler(observe), out _));
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("tool")
                        .Contexts(CommandExecutionContextSets.All)
                        .Subcommand("set", child =>
                        {
                            child.Remaining<int>("values").Handler(observe);
                        }),
                    out _
                )
            );
            Assert.IsTrue(shell.SetVariable("current", "7"));
            Assert.IsTrue(shell.SetVariable("action", "set"));

            if (0 < caret)
            {
                Assert.IsTrue(char.IsSurrogatePair(line, caret - 1));
            }

            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            classifier.Classify(line, shell, spans);

            List<string> actualSpans = new();
            int previousEnd = 0;
            foreach (CommandSyntaxSpan span in spans)
            {
                Assert.That(previousEnd, Is.LessThanOrEqualTo(span.Start));
                Assert.That(span.End, Is.GreaterThan(span.Start));
                Assert.IsFalse(span.Start < line.Length && char.IsLowSurrogate(line[span.Start]));
                Assert.IsFalse(span.End < line.Length && char.IsLowSurrogate(line[span.End]));
                actualSpans.Add($"{span.Role}:{line.Substring(span.Start, span.End - span.Start)}");
                previousEnd = span.End;
            }

            Assert.That(string.Join("|", actualSpans), Is.EqualTo(expectedSpans));
            Assert.That(actualArguments, Is.Null, "Classification must not call a handler.");
            Assert.That(history.GetHistory(onlySuccess: false, onlyErrorFree: false), Is.Empty);
            Assert.IsFalse(shell.TryConsumeErrorMessage(out _));
            shell.RunCommand(line);
            Assert.That(actualArguments, Is.EqualTo(expectedArguments));
        }

        [Test]
        public void WarmClassifierReadsCurrentVariableAndParserConfiguration()
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("gain").Arg<int>("value").Handler((_, _) => { }), out _));
            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            const string line = "gain $value";
            Assert.IsTrue(shell.SetVariable("value", "5"));
            classifier.Classify(line, shell, spans);
            Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Variable));

            Assert.IsTrue(shell.SetVariable("value", "oops"));
            classifier.Classify(line, shell, spans);
            Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Invalid));

            bool allow = true;
            Assert.IsTrue(
                CommandArg.RegisterParser<int>(
                    (string input, out int parsed) =>
                    {
                        parsed = 0;
                        return allow;
                    }
                )
            );
            try
            {
                classifier.Classify(line, shell, spans);
                Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Variable));
                allow = false;
                classifier.Classify(line, shell, spans);
                Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Invalid));
            }
            finally
            {
                CommandArg.UnregisterParser<int>();
            }

            classifier.Classify(line, shell, spans);
            Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Invalid));
        }

        [Test]
        public void WarmStringRangeUsesCurrentCulture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                bool invoked = false;
                CommandShell shell = new(new CommandHistory(16));
                CommandBuilder command = CommandBuilder
                    .Create("label")
                    .Contexts(CommandExecutionContextSets.All)
                    .Arg<string>("text", spec =>
                    {
                        return spec.Required().Range("a", "z");
                    })
                    .Handler(
                        (_, _) =>
                        {
                            invoked = true;
                        }
                    );
                Assert.IsTrue(shell.AddCommand(command, out _));

                CommandSyntaxClassifier classifier = new();
                List<CommandSyntaxSpan> spans = new();
                const string line = "label ä";
                classifier.Classify(line, shell, spans);
                shell.RunCommand(line);
                Assert.IsTrue(invoked);
                Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.String));

                invoked = false;
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
                classifier.Classify(line, shell, spans);
                shell.RunCommand(line);
                Assert.IsFalse(invoked);
                Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Invalid));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Test]
        public void WarmClassifierTracksDirectQuoteAndDelimiterEdits()
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("point").Arg<Vector3>("position").Handler((_, _) => { }), out _));
            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            const string line = "point ^(1, 2, 3)^";
            classifier.Classify(line, shell, spans);
            Assert.That(spans[1].Role, Is.EqualTo(CommandSyntaxRole.Invalid));
            CommandArg.Quotes.Add('^');
            try
            {
                classifier.Classify(line, shell, spans);
                Assert.That(spans[3].Role, Is.EqualTo(CommandSyntaxRole.Number));

                Assert.IsTrue(CommandArg.Delimiters.Remove(','));
                try
                {
                    classifier.Classify(line, shell, spans);
                    Assert.That(spans[2].Role, Is.EqualTo(CommandSyntaxRole.Invalid));
                }
                finally
                {
                    CommandArg.Delimiters.Add(',');
                }
            }
            finally
            {
                CommandArg.Quotes.Remove('^');
            }
        }

#if NET
        [Test]
        public void TypingSimpleArgumentsDoesNotMaterializeTokenStrings()
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.That(shell.AddCommand(CommandBuilder.Create("bench-highlight").Arg<int>("number").Arg<string>("text").Handler((_, _) => { }), out _), Is.True);
            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            const string line = "bench-highlight 123 \"allocation samples\"";
            string[] edits = new string[line.Length];
            long[] samples = new long[line.Length];
            for (int index = 0; index < edits.Length; ++index)
            {
                edits[index] = line.Substring(0, index + 1);
            }
            for (int pass = 0; pass < 100; ++pass)
            {
                for (int index = 0; index < edits.Length; ++index)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    classifier.Classify(edits[index], shell, spans);
                    samples[index] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
            }
            Assert.That(samples, Is.All.Zero);
        }

        [TestCase("point \"(1, 2, 3)\"")]
        [TestCase("point zero")]
        [TestCase("gain 21")]
        [TestCase("gain $value")]
        public void StableBuiltInClassificationDoesNotAllocateAfterWarmup(string line)
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("point").Arg<Vector3>("position").Handler((_, _) => { }), out _));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("gain").Arg<int>("value", GainRange).Handler((_, _) => { }), out _));
            Assert.IsTrue(shell.SetVariable("value", "5"));
            CommandSyntaxClassifier classifier = new();
            List<CommandSyntaxSpan> spans = new();
            for (int index = 0; index < 1024; ++index)
            {
                classifier.Classify(line, shell, spans);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 1024; ++index)
            {
                classifier.Classify(line, shell, spans);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(spans.Count, Is.GreaterThan(1));
            Assert.That(allocated, Is.Zero);

            before = GC.GetAllocatedBytesForCurrentThread();
            byte[] control = new byte[256];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(256));
        }
#endif

        [TestCase("spawn goblin 80", "count must be 1–50. You typed 80.")]
        [TestCase("spawn dragon", "prefab must be one of: goblin, ghoul, golem. You typed dragon.")]
        [TestCase("spawn goblin abc", "count must be int. You typed abc.")]
        [TestCase("spawn goblin $tooHigh", "count must be 1–50. You typed 80.")]
        [TestCase("spwan goblin", "No command spwan. Did you mean spawn")]
        [TestCase("spwn goblin", "No command spwn. Did you mean spawn")]
        [TestCase("xyzzy goblin", "No command xyzzy.")]
        [TestCase("spxyz goblin", "No command spxyz.")]
        public void LoggedValidationErrorMatchesT13Text(string line, string expectedMessage)
        {
            CommandShell shell = new(new CommandHistory(16));
            int runs = 0;
            Assert.That(
                shell.AddCommand(
                    CommandBuilder
                        .Create("spawn", "Spawns enemies at a point")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required().Choices("goblin", "ghoul", "golem");
                        })
                        .Arg<int>("count", spec =>
                        {
                            return spec.Range(1, 50).Default(1);
                        })
                        .Handler(
                            (_, _) =>
                            {
                                ++runs;
                            }
                        ),
                    out _
                ),
                Is.True
            );
            Assert.That(shell.SetVariable("tooHigh", "80"), Is.True);

            CommandShell.LineValidation inlineValidation = shell.ValidateLine(line, CommandExecutionContext.Current, allowIncomplete: true);
            Assert.That(inlineValidation.IsError, Is.True);
            Assert.That(inlineValidation.Message, Is.EqualTo(expectedMessage));

            shell.RunCommand(line);
            Assert.That(runs, Is.Zero);
            Assert.That(shell.TryConsumeErrorMessage(out string error), Is.True);
            Assert.That(error, Is.EqualTo(inlineValidation.Message));
        }

        [Test]
        public void UnclosedQuoteProducesNoteAndStillRuns()
        {
            CommandShell shell = new(new CommandHistory(16));
            string spoken = null;
            Assert.That(
                shell.AddCommand(
                    CommandBuilder
                        .Create("say", "Prints text")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<string>("message", spec =>
                        {
                            return spec.Required();
                        })
                        .Handler(
                            (_, arguments) =>
                            {
                                spoken = arguments.Get<string>("message");
                            }
                        ),
                    out _
                ),
                Is.True
            );

            const string line = "say \"hello wor";
            CommandShell.LineValidation validation = shell.ValidateLine(line, CommandExecutionContext.Current, allowIncomplete: true);
            Assert.That(validation.IsNote, Is.True);
            Assert.That(validation.IsError, Is.False);
            Assert.That(validation.UnclosedQuote, Is.EqualTo('"'));
            Assert.That(validation.Message, Is.EqualTo("Quote not closed. The rest of the line is one argument."));

            Assert.That(shell.RunCommand(line), Is.True);
            Assert.That(spoken, Is.EqualTo("hello wor"));
            Assert.That(shell.TryConsumeErrorMessage(out _), Is.False);
        }

        [TestCase("spwn", "spawn")]
        [TestCase("spwan", "spawn")]
        [TestCase("SPAW", "spawn")]
        [TestCase("spxyz", null)]
        [TestCase("sp", null)]
        public void DidYouMeanSuggestsOnlyWithinEditDistanceTwo(string typed, string expectedSuggestion)
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.That(shell.AddCommand("spawn", _ => { }), Is.True);

            CommandShell.LineValidation validation = shell.ValidateLine(typed, CommandExecutionContext.Current, allowIncomplete: true);
            Assert.That(validation.IsError, Is.True);
            Assert.That(validation.Suggestion, Is.EqualTo(expectedSuggestion));
        }

        [Test]
        public void IncompleteRequiredArgumentsStaySilentWhileTypingAndFailOnEnter()
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.That(
                shell.AddCommand(
                    CommandBuilder
                        .Create("spawn")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<string>("prefab", spec =>
                        {
                            return spec.Required();
                        })
                        .Handler((_, _) => { }),
                    out _
                ),
                Is.True
            );

            CommandShell.LineValidation typing = shell.ValidateLine("spawn", CommandExecutionContext.Current, allowIncomplete: true);
            Assert.That(typing.IsValid, Is.True);

            CommandShell.LineValidation submit = shell.ValidateLine("spawn", CommandExecutionContext.Current, allowIncomplete: false);
            Assert.That(submit.IsError, Is.True);
            shell.RunCommand("spawn");
            Assert.That(shell.TryConsumeErrorMessage(out string error), Is.True);
            Assert.That(error, Is.EqualTo(submit.Message));
        }
    }
}
