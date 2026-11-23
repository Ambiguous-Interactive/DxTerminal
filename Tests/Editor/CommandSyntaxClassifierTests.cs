namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using CommandSyntaxRole = Backend.CommandSyntaxClassifier.CommandSyntaxRole;
    using CommandSyntaxSpan = Backend.CommandSyntaxClassifier.CommandSyntaxSpan;

    public sealed class CommandSyntaxClassifierTests
    {
        [TestCase("inspect", "Command:inspect", "")]
        [TestCase("missing abc", "UnknownCommand:missing|String:abc", null)]
        [TestCase("mode true", "Command:mode|Choice:true", "true")]
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
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("gain")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<int>("value", spec =>
                        {
                            return spec.Range(-20, 20);
                        })
                        .Handler(observe),
                    out _
                )
            );
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
        [TestCase("point \"(1, 2, 3)\"")]
        [TestCase("point zero")]
        [TestCase("gain 21")]
        [TestCase("gain $value")]
        public void StableBuiltInClassificationDoesNotAllocateAfterWarmup(string line)
        {
            CommandShell shell = new(new CommandHistory(16));
            Assert.IsTrue(shell.AddCommand(CommandBuilder.Create("point").Arg<Vector3>("position").Handler((_, _) => { }), out _));
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("gain")
                        .Arg<int>("value", spec =>
                        {
                            return spec.Range(-20, 20);
                        })
                        .Handler((_, _) => { }),
                    out _
                )
            );
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
    }
}
