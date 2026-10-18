namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using Persistence;
    using UI;
    using UnityEngine;

    public sealed class ValueEqualityTests
    {
        private static void AssertEquality<T>(T first, T second, bool expected)
            where T : struct, IEquatable<T>
        {
            Assert.That(first.Equals(first), Is.True);
            Assert.That(first.Equals(second), Is.EqualTo(expected), "Constrained equality");
            Assert.That(second.Equals(first), Is.EqualTo(expected), "Symmetric equality");
            Assert.That(first.Equals((object)second), Is.EqualTo(expected), "Boxed equality");
            Assert.That(EqualityComparer<T>.Default.Equals(first, second), Is.EqualTo(expected), "Default comparer equality");
            Assert.That(first.Equals((object)null), Is.False);
            Assert.That(first.Equals(new object()), Is.False);
            if (expected)
            {
                Assert.That(first.GetHashCode(), Is.EqualTo(second.GetHashCode()), "Equal values must share a hash");
            }

            HashSet<T> values = new() { first };
            Assert.That(values.Contains(second), Is.EqualTo(expected), "Hash collection lookup");
        }

        private static void AssertOperators<T>(T first, T equal, T different, Func<T, T, bool> equals, Func<T, T, bool> notEquals)
            where T : struct, IEquatable<T>
        {
            AssertEquality(first, equal, true);
            AssertEquality(first, different, false);
            Assert.That(equals(first, equal), Is.True, $"{typeof(T).Name} == for equal values");
            Assert.That(equals(equal, first), Is.True, $"{typeof(T).Name} == is symmetric");
            Assert.That(notEquals(first, equal), Is.False, $"{typeof(T).Name} != for equal values");
            Assert.That(notEquals(equal, first), Is.False, $"{typeof(T).Name} != is symmetric");
            Assert.That(equals(first, different), Is.False, $"{typeof(T).Name} == for distinct values");
            Assert.That(equals(different, first), Is.False, $"{typeof(T).Name} == is symmetric");
            Assert.That(notEquals(first, different), Is.True, $"{typeof(T).Name} != for distinct values");
            Assert.That(notEquals(different, first), Is.True, $"{typeof(T).Name} != is symmetric");
        }

        [Test]
        public void PublicValueOperatorsAgreeWithEquality()
        {
            AssertOperators(
                new ConsoleKeyBindings.Shortcut(KeyCode.F8),
                new ConsoleKeyBindings.Shortcut(KeyCode.F8),
                new ConsoleKeyBindings.Shortcut(KeyCode.F9),
                static (first, second) => first == second,
                static (first, second) => first != second
            );
            AssertOperators(
                new CommandArg("value"),
                new CommandArg("value"),
                new CommandArg("value", startQuote: '"'),
                static (first, second) => first == second,
                static (first, second) => first != second
            );
            AssertOperators(
                new CommandCompletionReplacement(1, 2),
                new CommandCompletionReplacement(1, 2),
                new CommandCompletionReplacement(2, 2),
                static (first, second) => first == second,
                static (first, second) => first != second
            );
            AssertOperators(
                new CommandCompletion("value"),
                new CommandCompletion("value"),
                new CommandCompletion("value", "value"),
                static (first, second) => first == second,
                static (first, second) => first != second
            );
            LogItem log = new(TerminalLogType.Message, "value", "trace");
            LogItem equalLog = new(TerminalLogType.Message, "value", "trace");
            Assert.That(log.Sequence, Is.Not.EqualTo(equalLog.Sequence));
            AssertOperators(
                log,
                equalLog,
                new LogItem(TerminalLogType.Warning, "value", "trace"),
                static (first, second) => first == second,
                static (first, second) => first != second
            );
            AssertOperators(
                new TerminalThemeConfiguration
                {
                    terminalId = "terminal",
                    font = "Mono",
                    theme = "Dark",
                },
                new TerminalThemeConfiguration
                {
                    terminalId = "TERMINAL",
                    font = "mono",
                    theme = "dark",
                },
                new TerminalThemeConfiguration
                {
                    terminalId = "terminal",
                    font = "Mono",
                    theme = "Light",
                },
                static (first, second) => first == second,
                static (first, second) => first != second
            );
        }

        [TestCase(KeyCode.F8, EventModifiers.Control, false, true)]
        [TestCase(KeyCode.F9, EventModifiers.Control, false, false)]
        [TestCase(KeyCode.F8, EventModifiers.Command, false, false)]
        [TestCase(KeyCode.F8, EventModifiers.Control, true, false)]
        public void ShortcutEqualityIncludesEveryConfiguredField(KeyCode key, EventModifiers modifiers, bool controlOrCommand, bool equal)
        {
            AssertEquality(new ConsoleKeyBindings.Shortcut(KeyCode.F8, EventModifiers.Control), new ConsoleKeyBindings.Shortcut(key, modifiers, controlOrCommand), equal);
        }

        [TestCase("value", null, null, true)]
        [TestCase("Value", null, null, false)]
        [TestCase("value", '"', null, false)]
        [TestCase("value", null, '"', false)]
        public void CommandArgEqualityIncludesTextAndBothQuotes(string contents, char? startQuote, char? endQuote, bool equal)
        {
            AssertEquality(new CommandArg(new string("value".ToCharArray())), new CommandArg(contents, startQuote, endQuote), equal);
        }

        [TestCase(1, 2, true)]
        [TestCase(2, 2, false)]
        [TestCase(1, 3, false)]
        public void ReplacementEqualityIncludesBothRangeValues(int start, int length, bool equal)
        {
            AssertEquality(new CommandCompletionReplacement(1, 2), new CommandCompletionReplacement(start, length), equal);
        }

        [TestCase("value", "label", "description", 1, 2, true)]
        [TestCase("Value", "label", "description", 1, 2, false)]
        [TestCase("value", "Label", "description", 1, 2, false)]
        [TestCase("value", "label", "Description", 1, 2, false)]
        [TestCase("value", "label", "description", 2, 2, false)]
        [TestCase("value", "label", "description", 1, 3, false)]
        public void CompletionEqualityIncludesAllRawFields(string insertion, string label, string description, int start, int length, bool equal)
        {
            AssertEquality(
                new CommandCompletion(
                    new string("value".ToCharArray()),
                    new string("label".ToCharArray()),
                    new string("description".ToCharArray()),
                    new CommandCompletionReplacement(1, 2)
                ),
                new CommandCompletion(insertion, label, description, new CommandCompletionReplacement(start, length)),
                equal
            );
        }

        [Test]
        public void OptionalCompletionFieldsRemainDistinctFromTheirEffectiveValues()
        {
            CommandCompletion omittedLabel = new("value");
            CommandCompletion explicitLabel = new("value", "value");
            Assert.That(omittedLabel.EffectiveDisplayLabel, Is.EqualTo(explicitLabel.EffectiveDisplayLabel));
            AssertEquality(omittedLabel, explicitLabel, false);
            AssertEquality(omittedLabel, new CommandCompletion("value", string.Empty), false);
            AssertEquality(omittedLabel, new CommandCompletion("value", description: string.Empty), false);
            AssertEquality(omittedLabel, new CommandCompletion("value", replacement: default(CommandCompletionReplacement)), false);
            AssertEquality(default(CommandCompletion), new CommandCompletion(string.Empty), false);
            AssertEquality(default(CommandArg), new CommandArg(string.Empty), false);
            AssertEquality(new CommandArg(null), new CommandArg(string.Empty), true);
        }

        [Test]
        public void DefaultValuesHaveTheSameEqualityAndHashContracts()
        {
            AssertEquality(default(ConsoleKeyBindings.Shortcut), default(ConsoleKeyBindings.Shortcut), true);
            AssertEquality(default(ConsoleKeyBindings.Shortcut), new ConsoleKeyBindings.Shortcut(KeyCode.None), true);
            AssertEquality(default(CommandArg), default(CommandArg), true);
            AssertEquality(default(CommandCompletionReplacement), new CommandCompletionReplacement(0, 0), true);
            AssertEquality(default(CommandCompletion), default(CommandCompletion), true);
            AssertEquality(default(TerminalThemeConfiguration), default(TerminalThemeConfiguration), true);
        }

        [TestCase(nameof(TerminalThemeConfiguration.terminalId))]
        [TestCase(nameof(TerminalThemeConfiguration.font))]
        [TestCase(nameof(TerminalThemeConfiguration.theme))]
        public void ThemeConfigurationCaseInsensitiveEqualityUsesMatchingHashes(string field)
        {
            TerminalThemeConfiguration first = new()
            {
                terminalId = "Σ",
                font = "Σ",
                theme = "Σ",
            };
            TerminalThemeConfiguration second = first;
            switch (field)
            {
                case nameof(TerminalThemeConfiguration.terminalId):
                    second.terminalId = "ς";
                    break;
                case nameof(TerminalThemeConfiguration.font):
                    second.font = "ς";
                    break;
                case nameof(TerminalThemeConfiguration.theme):
                    second.theme = "ς";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(field));
            }

            AssertEquality(first, second, StringComparer.OrdinalIgnoreCase.Equals("Σ", "ς"));
        }
    }
}
