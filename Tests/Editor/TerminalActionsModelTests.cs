namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using ActionCard = UI.TerminalActionsModel.ActionCard;
    using ActionControlKind = UI.TerminalActionsModel.ActionControlKind;
    using ActionGroup = UI.TerminalActionsModel.ActionGroup;
    using RecentEntry = UI.TerminalActionsModel.RecentEntry;

    public sealed class TerminalActionsModelTests
    {
        private CommandShell _shell;

        [SetUp]
        public void SetUp()
        {
            _shell = new CommandShell(new CommandHistory(16));
            _shell.ClearAllCommands();
        }

        [Test]
        public void BuildGroupsSortsGroupsRespectsHideFromActionsAndFlattensSubcommands()
        {
            _shell.AddCommand(CommandBuilder.Create("alpha").Group("World").Handler((_, _) => { }), out _);
            _shell.AddCommand(CommandBuilder.Create("secret").Group("World").HideFromActions().Handler((_, _) => { }), out _);
            _shell.AddCommand(CommandBuilder.Create("ungrouped").Handler((_, _) => { }), out _);
            _shell.AddCommand(
                CommandBuilder
                    .Create("inventory")
                    .Group("Items")
                    .Subcommand("add", sub =>
                    {
                        sub.Help("Add item.")
                            .Arg<string>("item", arg =>
                            {
                                return arg.Required();
                            })
                            .Handler((_, _) => { });
                    })
                    .Subcommand("clear", sub =>
                    {
                        sub.Help("Clear inventory.").Handler((_, _) => { });
                    }),
                out _
            );

            List<ActionGroup> groups = new();
            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorPlayMode), CommandTier.Developer, null, groups);

            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[0].Name, Is.EqualTo("Commands"));
            Assert.That(groups[1].Name, Is.EqualTo("Items"));
            Assert.That(groups[2].Name, Is.EqualTo("World"));

            Assert.That(groups[1].Cards.Count, Is.EqualTo(2));
            Assert.That(groups[1].Cards[0].CommandPath, Is.EqualTo("inventory add"));
            Assert.That(groups[1].Cards[1].CommandPath, Is.EqualTo("inventory clear"));

            Assert.That(groups[2].Cards.Count, Is.EqualTo(1));
            Assert.That(groups[2].Cards[0].CommandPath, Is.EqualTo("alpha"));
        }

        [Test]
        public void ClassifyFieldMapsEverySpecToExpectedControlKind()
        {
            _shell.AddCommand(
                CommandBuilder
                    .Create("matrix")
                    .Group("Test")
                    .Arg<bool>("flag", arg =>
                    {
                        return arg.BoolChoices();
                    })
                    .Arg<string>("smallChoice", arg =>
                    {
                        return arg.Choices("goblin", "ghoul", "golem").Default("goblin");
                    })
                    .Arg<string>("largeChoice", arg =>
                    {
                        return arg.Choices("a", "b", "c", "d", "e").Default("a");
                    })
                    .Arg<string>("dynamicChoice", arg =>
                    {
                        return arg.Choices(_ =>
                        {
                            return new[] { "x", "y" };
                        });
                    })
                    .Arg<SampleMode>("enumChoice", arg =>
                    {
                        return arg.EnumChoices();
                    })
                    .Arg<int>("ranged", arg =>
                    {
                        return arg.Range(1, 50).Default(10);
                    })
                    .Arg<float>("plainNumber", arg =>
                    {
                        return arg;
                    })
                    .Arg<string>("plainText", arg =>
                    {
                        return arg;
                    })
                    .Arg<Vector2>("v2", arg =>
                    {
                        return arg;
                    })
                    .Arg<Vector3>("v3", arg =>
                    {
                        return arg;
                    })
                    .Arg<Vector4>("v4", arg =>
                    {
                        return arg;
                    })
                    .Arg<Color>("tint", arg =>
                    {
                        return arg;
                    })
                    .Arg<UnityEngine.Object>("sceneObj", arg =>
                    {
                        return arg.Parser(
                            (string _, out UnityEngine.Object value) =>
                            {
                                value = null;
                                return true;
                            }
                        );
                    })
                    .Remaining<string>("rest", arg =>
                    {
                        return arg;
                    })
                    .Handler((_, _) => { }),
                out _
            );

            List<ActionGroup> groups = new();
            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorPlayMode), CommandTier.Developer, null, groups);
            ActionCard card = groups[0].Cards[0];

            Assert.That(card.Fields[0].Kind, Is.EqualTo(ActionControlKind.Toggle));
            Assert.That(card.Fields[1].Kind, Is.EqualTo(ActionControlKind.Segmented));
            Assert.That(card.Fields[2].Kind, Is.EqualTo(ActionControlKind.Menu));
            Assert.That(card.Fields[3].Kind, Is.EqualTo(ActionControlKind.Menu));
            Assert.That(card.Fields[4].Kind, Is.EqualTo(ActionControlKind.Menu));
            Assert.That(card.Fields[5].Kind, Is.EqualTo(ActionControlKind.RangeNumber));
            Assert.That(card.Fields[5].HasRange, Is.True);
            Assert.That(card.Fields[5].RangeMin, Is.EqualTo(1d));
            Assert.That(card.Fields[5].RangeMax, Is.EqualTo(50d));
            Assert.That(card.Fields[6].Kind, Is.EqualTo(ActionControlKind.Number));
            Assert.That(card.Fields[7].Kind, Is.EqualTo(ActionControlKind.Text));
            Assert.That(card.Fields[8].Kind, Is.EqualTo(ActionControlKind.Vector2));
            Assert.That(card.Fields[9].Kind, Is.EqualTo(ActionControlKind.Vector3));
            Assert.That(card.Fields[10].Kind, Is.EqualTo(ActionControlKind.Vector4));
            Assert.That(card.Fields[11].Kind, Is.EqualTo(ActionControlKind.Color));
            Assert.That(card.Fields[12].Kind, Is.EqualTo(ActionControlKind.SceneObject));
            Assert.That(card.Fields[13].Kind, Is.EqualTo(ActionControlKind.Remaining));
        }

        [Test]
        public void LegacyCommandsExposeSingleRawFieldWithHintUnlessZeroArgs()
        {
            _shell.AddCommand("legacy-args", _ => { }, 1, 2, "With args", "<target> [count]");
            _shell.AddCommand("legacy-zero", _ => { }, 0, 0, "Zero args", string.Empty);

            List<ActionGroup> groups = new();
            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorPlayMode), CommandTier.Developer, null, groups);

            ActionCard withArgs = groups[0].Cards[0];
            ActionCard zeroArgs = groups[0].Cards[1];

            Assert.That(withArgs.IsRawArguments, Is.True);
            Assert.That(withArgs.Fields.Length, Is.EqualTo(1));
            Assert.That(withArgs.Fields[0].Description, Is.EqualTo("<target> [count]"));
            Assert.That(withArgs.Fields[0].IsRequired, Is.True);

            Assert.That(zeroArgs.IsRawArguments, Is.False);
            Assert.That(zeroArgs.Fields.Length, Is.EqualTo(0));
            Assert.That(TerminalActionsModel.CanRun(zeroArgs, Array.Empty<string>()), Is.True);
        }

        [Test]
        public void UnavailableReasonReportsTierAndContext()
        {
            _shell.AddCommand(CommandBuilder.Create("tester-cmd").Tier(CommandTier.Tester).Handler((_, _) => { }), out _);
            _shell.AddCommand(CommandBuilder.Create("play-only").Contexts(CommandExecutionContexts.EditorPlayMode).Handler((_, _) => { }), out _);

            List<ActionGroup> groups = new();
            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorEditMode), CommandTier.Player, null, groups);

            ActionCard playOnly = groups[0].Cards[0];
            ActionCard testerCmd = groups[0].Cards[1];

            Assert.That(testerCmd.IsUnavailable, Is.True);
            Assert.That(testerCmd.UnavailableReason, Is.EqualTo("Unavailable in Player tier builds. Tester and Developer only."));
            Assert.That(TerminalActionsModel.CanRun(testerCmd, Array.Empty<string>()), Is.False);

            Assert.That(playOnly.IsUnavailable, Is.True);
            Assert.That(playOnly.UnavailableReason, Is.EqualTo("Unavailable in Player tier builds. Developer only."));

            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorEditMode), CommandTier.Developer, null, groups);
            playOnly = groups[0].Cards[0];
            Assert.That(playOnly.IsUnavailable, Is.True);
            Assert.That(playOnly.UnavailableReason, Is.EqualTo("Unavailable in Edit Mode. Play Mode only."));
        }

        [Test]
        public void BuildCommandLineQuotesSpacesAndCanRunChecksRequiredFields()
        {
            _shell.AddCommand(
                CommandBuilder
                    .Create("say")
                    .Arg<string>("message", arg =>
                    {
                        return arg.Required();
                    })
                    .Arg<int>("repeat", arg =>
                    {
                        return arg.Default(1);
                    })
                    .Handler((_, _) => { }),
                out _
            );

            List<ActionGroup> groups = new();
            TerminalActionsModel.BuildGroups(_shell, new CommandExecutionContext(CommandExecutionContexts.EditorPlayMode), CommandTier.Developer, null, groups);
            ActionCard card = groups[0].Cards[0];

            Assert.That(TerminalActionsModel.CanRun(card, new[] { string.Empty, "1" }), Is.False);
            Assert.That(TerminalActionsModel.CanRun(card, new[] { "hello world", "2" }), Is.True);
            Assert.That(TerminalActionsModel.BuildCommandLine(card, new[] { "hello world", "2" }), Is.EqualTo("say \"hello world\" 2"));
        }

        [Test]
        public void RecordRecentCapsAtFourAndMovesRepeatsToFront()
        {
            List<RecentEntry> recent = new();
            TerminalActionsModel.RecordRecent(recent, "a", "a");
            TerminalActionsModel.RecordRecent(recent, "b", "b");
            TerminalActionsModel.RecordRecent(recent, "c", "c");
            TerminalActionsModel.RecordRecent(recent, "d", "d");
            TerminalActionsModel.RecordRecent(recent, "e", "e");
            TerminalActionsModel.RecordRecent(recent, "c", "c 2");

            Assert.That(recent.Count, Is.EqualTo(4));
            Assert.That(recent[0].CommandPath, Is.EqualTo("c"));
            Assert.That(recent[0].CommandLine, Is.EqualTo("c 2"));
            Assert.That(recent[1].CommandPath, Is.EqualTo("e"));
            Assert.That(recent[2].CommandPath, Is.EqualTo("d"));
            Assert.That(recent[3].CommandPath, Is.EqualTo("b"));
        }

        private enum SampleMode
        {
            [Obsolete("Use a valid SampleMode value.")]
            Unknown = 0,
            Normal = 1,
            Hard = 2,
        }
    }
}
