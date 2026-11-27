namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Attributes;
    using Backend;
    using NUnit.Framework;
    using UI;
    using CommandTier = Backend.CommandTier;
    using WatchEntry = Backend.CommandWatchRegistry.WatchEntry;
    using WatchValueRole = Backend.CommandWatchRegistry.WatchValueRole;
#if UNITY_EDITOR
    using UnityEngine;
    using UnityEngine.TestTools;
#endif

    /*
        Engine-free pins for the Watches registry and model (milestone C4,
        #244): registration and handle semantics, pin set and pending
        restore, sampling into the reused entry buffers, tier filtering,
        [Watch] member discovery, and the list/HUD projections. Committed
        with the feature; the UI-side pins live in the PlayMode suite.
     */
    public sealed class TerminalWatchesModelTests
    {
        [Watch("discovered.property", Format = "0", Tier = CommandTier.Tester)]
        private static int DiscoveredProperty
        {
            get { return 7; }
        }

        [Watch(null)]
        private static bool DiscoveredUnnamed
        {
            get { return true; }
        }

        [Watch("discovered.duplicate")]
        private static int DiscoveredDuplicate
        {
            get { return 11; }
        }

        [Watch("fps")]
        private static int DiscoveredBuiltInCollision
        {
            get { return 60; }
        }

        [Watch("discovered.field")]
        internal static string DiscoveredField = "field-value";

        [Watch("discovered.duplicate")]
        private static readonly string DiscoveredDuplicateField = "field-loses";

        [Watch("discovered.instance")]
        internal int DiscoveredInstanceProperty
        {
            get { return 1; }
        }

        private CommandWatchRegistry _registry;

        [Watch("discovered.method")]
        private static double DiscoveredMethod()
        {
            return 2.5d;
        }

        [SetUp]
        public void SetUp()
        {
            _registry = new CommandWatchRegistry();
        }

        [Test]
        public void RegistryRegistersBuiltInFpsAndFrameWatches()
        {
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("fps", out WatchEntry fps), Is.True, "The fps watch is built in");
            Assert.That(fps.Source, Is.EqualTo("DxTerminal built-in"));
            Assert.That(fps.Format, Is.EqualTo("F1"));
            Assert.That(fps.Tier, Is.EqualTo(CommandTier.Player));
            Assert.That(fps.Role, Is.EqualTo(WatchValueRole.Number), "A typed built-in renders with the numeric syntax role");
            Assert.That(_registry.TryGetEntry("frame", out WatchEntry frame), Is.True, "The frame watch is built in");
            Assert.That(frame.Format, Is.Null, "The frame watch renders its raw integer");
            Assert.That(frame.Tier, Is.EqualTo(CommandTier.Player));
            Assert.That(frame.Role, Is.EqualTo(WatchValueRole.Number));
        }

        [Test]
        public void WatchSamplesTypedGetterIntoNumberRole()
        {
            WatchHandle handle = _registry.Watch("lives", () => 42);

            Assert.That(handle, Is.Not.Null);
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("lives", out WatchEntry entry), Is.True);
            Assert.That(entry.HasValue, Is.True, "The first sample fills the entry");
            Assert.That(entry.ValueText, Is.EqualTo("42"));
            Assert.That(entry.Role, Is.EqualTo(WatchValueRole.Number));
            Assert.That(entry.Source, Is.EqualTo("Script"), "A watch without a source says Script");
        }

        [Test]
        public void WatchSampleAppliesInvariantFormat()
        {
            _registry.Watch("ratio", () => 1.26f, source: "Stats.Ratio", format: "F1");
            _registry.Sample(CommandTier.Developer);

            Assert.That(_registry.TryGetEntry("ratio", out WatchEntry entry), Is.True);
            Assert.That(entry.ValueText, Is.EqualTo("1.3"));
            Assert.That(entry.Role, Is.EqualTo(WatchValueRole.Number));
        }

        [Test]
        public void WatchSampleClassifiesStringEnumBoolAndObjectRoles()
        {
            _registry.Watch("text", () => "ready");
            _registry.Watch("state", () => WaveState.Calm);
            _registry.Watch("muted", () => true);
            _registry.Watch("player", () => new object());
            _registry.Watch("nothing", () => (object)null);
            _registry.Sample(CommandTier.Developer);

            Assert.That(_registry.TryGetEntry("text", out WatchEntry text), Is.True);
            Assert.That(text.Role, Is.EqualTo(WatchValueRole.String));
            Assert.That(_registry.TryGetEntry("state", out WatchEntry state), Is.True);
            Assert.That(state.Role, Is.EqualTo(WatchValueRole.Choice));
            Assert.That(_registry.TryGetEntry("muted", out WatchEntry muted), Is.True);
            Assert.That(muted.Role, Is.EqualTo(WatchValueRole.Choice));
            Assert.That(_registry.TryGetEntry("player", out WatchEntry player), Is.True);
            Assert.That(player.Role, Is.EqualTo(WatchValueRole.Body));
            Assert.That(_registry.TryGetEntry("nothing", out WatchEntry nothing), Is.True);
            Assert.That(nothing.ValueText, Is.EqualTo("null"));
            Assert.That(nothing.Role, Is.EqualTo(WatchValueRole.Body));
        }

        [Test]
        public void WatchSampleKeepsTextWhenTheValueIsUnchanged()
        {
            string shared = "steady";
            _registry.Watch("label", () => shared);
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("label", out WatchEntry entry), Is.True);
            string first = entry.ValueText;

            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.ValueText, Is.EqualTo(first), "An unchanged value keeps its text");

            shared = "changed";
            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.ValueText, Is.EqualTo("changed"), "A changed value rewrites the text");
        }

        [Test]
        public void WatchSampleSurvivesAThrowingGetter()
        {
            bool throwNow = false;
            _registry.Watch("fragile", () =>
            {
                if (throwNow)
                {
                    throw new InvalidOperationException("boom");
                }

                return "ok";
            });
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("fragile", out WatchEntry entry), Is.True);
            Assert.That(entry.ValueText, Is.EqualTo("ok"));

            /*
                The registry logs the throw through Debug.LogException; the
                live editor's test runner fails a test on an unexpected
                exception log unless it is expected up front.
             */
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: boom");
#endif
            throwNow = true;
            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.ValueText, Is.EqualTo("ok"), "A throwing getter keeps the last value");

            /*
                The recovered sample clears the once-flag even when its value
                is unchanged, so a later failure logs again; the recovered
                text is still the pinned value.
             */
            throwNow = false;
            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.ValueText, Is.EqualTo("ok"), "The recovered getter resumes sampling");

#if UNITY_EDITOR
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: boom");
#endif
            throwNow = true;
            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.ValueText, Is.EqualTo("ok"), "A later failure logs again instead of staying silent");
        }

        [Test]
        public void WatchSampleSurvivesAThrowingToString()
        {
            _registry.Watch("hostile", () => new HostileText());
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: no text for you");
#endif
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("hostile", out WatchEntry entry), Is.True);
            Assert.That(entry.HasValue, Is.False, "A formatting throw keeps the entry unsampled instead of breaking the pass");

            /*
                The once-flag suppresses the second sample's log, so no second
                expectation is registered; the entry stays unsampled.
             */
            _registry.Sample(CommandTier.Developer);
            Assert.That(entry.HasValue, Is.False, "The formatting throw stays skipped on later samples");
        }

        [Test]
        public void DiscoveredNamesCollidingWithBuiltInsKeepTheBuiltIn()
        {
            Assert.DoesNotThrow(
                () =>
                {
                    CommandWatchRegistry colliding = new CommandWatchRegistry();
                    Assert.That(colliding.Count, Is.GreaterThanOrEqualTo(2), "The registry constructs with its built-ins");
                },
                "A discovered [Watch] named like a built-in must not break registry construction"
            );

            Assert.That(_registry.TryGetEntry("fps", out WatchEntry fps), Is.True);
            Assert.That(fps.Source, Is.EqualTo("DxTerminal built-in"), "The built-in fps wins over the discovered collision");
            Assert.That(fps.Format, Is.EqualTo("F1"));
        }

        [Test]
        public void WatchHandleDisposeRemovesExactlyItsRegistration()
        {
            WatchHandle handle = _registry.Watch("temp", () => 1);
            Assert.That(_registry.TryGetEntry("temp", out _), Is.True);

            handle.Dispose();
            Assert.That(_registry.TryGetEntry("temp", out _), Is.False, "Dispose removes the watch");
            Assert.DoesNotThrow(() => handle.Dispose(), "Disposing twice is safe");

            Assert.That(_registry.Watch("temp", () => 2), Is.Not.Null, "The freed name registers again");
        }

        [Test]
        public void WatchRejectsDuplicatesAndInvalidNames()
        {
            _registry.Watch("taken", () => 1);
            Assert.Throws<ArgumentException>(() => _registry.Watch("taken", () => 2), "A taken name throws");
            Assert.Throws<ArgumentException>(() => _registry.Watch("has space", () => 3), "A spaced name throws");
            Assert.Throws<ArgumentException>(() => _registry.Watch("semi;colon", () => 4), "A semicolon name throws; pin persistence stores names ';'-separated");
            Assert.Throws<ArgumentException>(() => _registry.Watch("  ", () => 4), "A blank name throws");
            Assert.Throws<ArgumentException>(() => _registry.Watch(null, () => 5), "A null name throws");
            Assert.Throws<ArgumentNullException>(() => _registry.Watch<int>("valid", null), "A null getter throws");
            Assert.Throws<ArgumentNullException>(() => _registry.Watch("valid", (Func<object>)null), "A null untyped getter throws");
            Assert.That(_registry.TryGetEntry("valid", out _), Is.False, "A rejected registration registers nothing");
        }

        [Test]
        public void PinLifecycleTracksTheRegisteredWatch()
        {
            Assert.That(_registry.SetPinned("missing", true), Is.False, "An unknown watch cannot pin");

            long versionBefore = _registry.Version;
            _registry.Watch("player.hp", () => 10);
            Assert.That(_registry.SetPinned("player.hp", true), Is.True);
            Assert.That(_registry.IsPinned("player.hp"), Is.True);
            Assert.That(_registry.Version, Is.GreaterThan(versionBefore), "A pin bumps the version");

            List<string> pinned = new();
            _registry.CollectPinnedNames(pinned);
            Assert.That(pinned, Does.Contain("player.hp"));

            Assert.That(_registry.SetPinned("player.hp", true), Is.True);
            long versionAfterFirstPin = _registry.Version;
            Assert.That(_registry.SetPinned("player.hp", true), Is.True);
            Assert.That(_registry.Version, Is.EqualTo(versionAfterFirstPin), "A repeated pin does not bump again");

            Assert.That(_registry.SetPinned("player.hp", false), Is.True);
            Assert.That(_registry.IsPinned("player.hp"), Is.False);
        }

        [Test]
        public void LoadPinsReplacesTheSetAndAppliesToPendingNames()
        {
            _registry.Watch("kept", () => 1);
            _registry.Watch("dropped", () => 2);
            _registry.SetPinned("kept", true);
            _registry.SetPinned("dropped", true);

            _registry.LoadPins(new[] { "kept", "later.thing" });
            Assert.That(_registry.IsPinned("kept"), Is.True, "The stored pin survives the load");
            Assert.That(_registry.IsPinned("dropped"), Is.False, "A pin absent from storage drops");
            Assert.That(_registry.TryGetEntry("kept", out WatchEntry kept), Is.True);
            Assert.That(kept.Pinned, Is.True, "The live entry carries the restored pin");

            _registry.Watch("later.thing", () => 3);
            Assert.That(_registry.IsPinned("later.thing"), Is.True, "A pending pin applies when the watch registers");
        }

        [Test]
        public void WatchRemovalKeepsPinIntentForReRegistration()
        {
            WatchHandle handle = _registry.Watch("wave", () => 3);
            _registry.SetPinned("wave", true);

            handle.Dispose();
            Assert.That(_registry.IsPinned("wave"), Is.True, "The pin set is keyed by name, not by the live entry");

            _registry.Watch("wave", () => 4);
            Assert.That(_registry.TryGetEntry("wave", out WatchEntry entry), Is.True);
            Assert.That(entry.Pinned, Is.True, "A re-registration comes back pinned");
        }

        [Test]
        public void SampleAndCollectHideEntriesAboveTheActiveTier()
        {
            _registry.Watch("open", () => 1, tier: CommandTier.Player);
            _registry.Watch("tester-only", () => 2, tier: CommandTier.Tester);

            _registry.Sample(CommandTier.Player);
            Assert.That(_registry.TryGetEntry("open", out WatchEntry open), Is.True);
            Assert.That(open.HasValue, Is.True, "A Player-tier watch samples at Player tier");
            Assert.That(_registry.TryGetEntry("tester-only", out WatchEntry testerOnly), Is.True);
            Assert.That(testerOnly.HasValue, Is.False, "A Tester-tier watch does not sample at Player tier");

            List<WatchEntry> visible = new();
            _registry.CollectVisibleEntries(CommandTier.Player, visible);
            Assert.That(visible.Count, Is.EqualTo(3), "Two registered watches plus the Player-tier built-ins");
            foreach (WatchEntry entry in visible)
            {
                Assert.That((int)entry.Tier, Is.LessThanOrEqualTo((int)CommandTier.Player), "Every visible entry respects the tier");
            }

            _registry.Sample(CommandTier.Developer);
            Assert.That(testerOnly.HasValue, Is.True, "A Tester-tier watch samples at Developer tier");
        }

        [Test]
        public void DiscoveryFindsStaticWatchMembersOncePerRegistry()
        {
            Assert.That(_registry.TryGetEntry("discovered.property", out WatchEntry property), Is.True);
            Assert.That(property.Format, Is.EqualTo("0"));
            Assert.That(property.Tier, Is.EqualTo(CommandTier.Tester));
            Assert.That(property.Source, Is.EqualTo($"{nameof(TerminalWatchesModelTests)}.{nameof(DiscoveredProperty)}"));

            Assert.That(_registry.TryGetEntry("discovered.field", out WatchEntry field), Is.True);
            Assert.That(field.Source, Is.EqualTo($"{nameof(TerminalWatchesModelTests)}.{nameof(DiscoveredField)}"));

            Assert.That(_registry.TryGetEntry("discovered.method", out WatchEntry method), Is.True);
            Assert.That(method.Source, Is.EqualTo($"{nameof(TerminalWatchesModelTests)}.{nameof(DiscoveredMethod)}"));

            Assert.That(_registry.TryGetEntry("DiscoveredUnnamed", out WatchEntry unnamed), Is.True, "An empty watch name infers the member name");

            _registry.Sample(CommandTier.Developer);
            Assert.That(property.ValueText, Is.EqualTo("7"));
            Assert.That(field.ValueText, Is.EqualTo("field-value"));
            Assert.That(method.ValueText, Is.EqualTo("2.5"));
            Assert.That(unnamed.ValueText, Is.EqualTo("True"));

            /*
                The duplicate pair is one property and one field on the same
                type: the scan reads properties before fields, so the
                property wins deterministically.
             */
            Assert.That(_registry.TryGetEntry("discovered.duplicate", out WatchEntry duplicate), Is.True);
            Assert.That(duplicate.Source, Does.Contain(nameof(DiscoveredDuplicate)), "The property scan stage wins the duplicate");
            _registry.Sample(CommandTier.Developer);
            Assert.That(duplicate.ValueText, Is.EqualTo("11"));
            Assert.That(
                DiscoveredDuplicateField,
                Is.EqualTo("field-loses"),
                "The dropped duplicate keeps its own value; discovery never reads it, which is why the property stage decides the winner"
            );
            Assert.That(
                _registry.TryGetEntry("discovered.instance", out _),
                Is.False,
                "An instance member is not watchable: sampling has no instance to read, so the static-only scan skips it"
            );
        }

        [Test]
        public void FormatListLineRendersNameValueSourceAndPinState()
        {
            _registry.Watch("pinned", () => 5, source: "Stats.Pinned");
            _registry.Watch("fresh", () => "raw");
            _registry.SetPinned("pinned", true);
            _registry.Sample(CommandTier.Developer);
            Assert.That(_registry.TryGetEntry("pinned", out WatchEntry pinned), Is.True);
            Assert.That(_registry.TryGetEntry("fresh", out WatchEntry fresh), Is.True);

            Assert.That(TerminalWatchesModel.FormatListLine(pinned), Is.EqualTo("pinned = 5 · Stats.Pinned · Pinned"));
            Assert.That(TerminalWatchesModel.FormatListLine(fresh), Is.EqualTo("fresh = raw · Script · Pin"));
            Assert.That(TerminalWatchesModel.FormatListLine(null), Is.EqualTo(string.Empty), "A missing entry renders an empty line instead of throwing");

            _registry.Watch("unsampled", () => 1);
            Assert.That(_registry.TryGetEntry("unsampled", out WatchEntry unsampled), Is.True);
            Assert.That(
                TerminalWatchesModel.FormatListLine(unsampled),
                Does.Contain(TerminalWatchesModel.UnsampledValueText),
                "An unsampled entry says so instead of rendering an empty value"
            );
        }

        [Test]
        public void BuildHudRowsKeepsPinnedEntriesInOrderAndCapsRows()
        {
            List<WatchEntry> entries = new();
            Assert.That(_registry.TryGetEntry("fps", out WatchEntry fps), Is.True);
            Assert.That(_registry.TryGetEntry("frame", out WatchEntry frame), Is.True);
            entries.Add(fps);
            entries.Add(frame);
            _registry.Watch("unpinned", () => 1);
            _registry.Watch("hud-one", () => 2);
            _registry.Watch("hud-two", () => 3);
            _registry.Watch("hud-three", () => 4);
            _registry.SetPinned("hud-one", true);
            _registry.SetPinned("hud-two", true);
            _registry.SetPinned("hud-three", true);
            Assert.That(_registry.TryGetEntry("unpinned", out WatchEntry unpinned), Is.True);
            Assert.That(_registry.TryGetEntry("hud-one", out WatchEntry hudOne), Is.True);
            Assert.That(_registry.TryGetEntry("hud-two", out WatchEntry hudTwo), Is.True);
            Assert.That(_registry.TryGetEntry("hud-three", out WatchEntry hudThree), Is.True);
            entries.Add(unpinned);
            entries.Add(hudOne);
            entries.Add(hudTwo);
            entries.Add(hudThree);

            List<WatchEntry> hud = new();
            TerminalWatchesModel.BuildHudRows(entries, 2, hud);
            Assert.That(hud.Count, Is.EqualTo(2), "The HUD caps at max rows");
            Assert.That(hud[0].Name, Is.EqualTo("hud-one"));
            Assert.That(hud[1].Name, Is.EqualTo("hud-two"));

            TerminalWatchesModel.BuildHudRows(entries, 0, hud);
            Assert.That(hud.Count, Is.EqualTo(0), "A zero row cap empties the HUD");
            TerminalWatchesModel.BuildHudRows(null, 4, hud);
            Assert.That(hud.Count, Is.EqualTo(0), "A missing entry list empties the HUD");
        }

        [Test]
        public void ValueClassNamesMapTheSyntaxRoles()
        {
            Assert.That(TerminalWatchesModel.ValueClassName(WatchValueRole.Number), Is.EqualTo("number"));
            Assert.That(TerminalWatchesModel.ValueClassName(WatchValueRole.String), Is.EqualTo("string"));
            Assert.That(TerminalWatchesModel.ValueClassName(WatchValueRole.Choice), Is.EqualTo("choice"));
            Assert.That(TerminalWatchesModel.ValueClassName(WatchValueRole.Body), Is.EqualTo("body"));
            Assert.That(TerminalWatchesModel.ValueClassName((WatchValueRole)99), Is.EqualTo("body"), "An unknown role falls back to body");
        }

        private sealed class HostileText
        {
            public override string ToString()
            {
                throw new InvalidOperationException("no text for you");
            }
        }

        private enum WaveState
        {
            [Obsolete("Use a valid WaveState value.")]
            Unknown = 0,

            Calm = 1,

            Rising = 2,
        }
    }
}
