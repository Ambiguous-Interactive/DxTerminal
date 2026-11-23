namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using System.Text;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class CommandSyntaxClassifierTests
    {
        [TestCase("inspect", "Command:inspect", "")]
        [TestCase("missing abc", "UnknownCommand:missing|String:abc", null)]
        [TestCase("mode true", "Command:mode|Choice:true", "true")]
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
            CommandShell shell = new(new CommandHistory(16));
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
            shell.RunCommand(line);
            Assert.That(actualArguments, Is.EqualTo(expectedArguments));
        }
    }
}
