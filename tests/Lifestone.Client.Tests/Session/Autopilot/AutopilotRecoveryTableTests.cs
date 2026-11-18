using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 D141 part 4: the recovery table. A station failure's typed reason (the failing action's <see cref="ActionOutcome.Reason"/>, found in the action log by the detail the failure quotes) gets its fix and one retry; the same reason
/// after the retry, or a fix that cannot be made, stops the run safely with the reason. Round 13 (2026-10-30 03:07:40 UTC) stopped on two recoveries spent on recalls that asked the magic mode with no caster held.
/// </summary>
public sealed class AutopilotRecoveryTableTests(ITestOutputHelper output)
{
    private const uint WandWcid = 2366; // a Wand's weenie; the scripted ACE does not judge it
    private const uint CasterType = 0x8000;
    private const uint Held = 0x01000000;

    [Theory]
    [InlineData(ActionReason.NoCasterHeld, RecoveryFix.WieldCaster, "wield-caster")]
    [InlineData(ActionReason.NoFreeSlot, RecoveryFix.ShedPack, "shed-pack")]
    [InlineData(ActionReason.NotEnoughMana, RecoveryFix.ManaDance, "mana-dance")]
    [InlineData(ActionReason.TargetGone, RecoveryFix.RePick, "re-pick")]
    [InlineData(ActionReason.Busy, RecoveryFix.Wait, "wait")]
    [InlineData(ActionReason.InTheAir, RecoveryFix.Wait, "wait")] // M6 14 airborne: 0x04EB, the landing then the same action
    [InlineData(ActionReason.NotHeld, RecoveryFix.PlanAgain, "plan-again")]
    [InlineData(ActionReason.Fizzled, RecoveryFix.Generic, "generic")]
    [InlineData(ActionReason.ContraryResult, RecoveryFix.Generic, "generic")]
    [InlineData(ActionReason.None, RecoveryFix.Generic, "generic")]
    public void The_table_maps_each_typed_reason_to_its_fix(ActionReason reason, RecoveryFix fix, string name)
    {
        Assert.Equal(fix, RecoveryTable.FixFor(reason));
        Assert.Equal(name, RecoveryTable.Name(fix));
    }

    [Fact]
    public void The_reason_is_the_newest_failed_action_since_the_station_began_whose_detail_the_failure_quotes()
    {
        var log = new ActionLog();
        Add(log, 1, new ActionOutcome(ActionOutcomeKind.Refused, 0, "not enough mana: an old one", ActionReason.NotEnoughMana)); // before the station: not read
        var since = log.Total;
        Add(log, 2, new ActionOutcome(ActionOutcomeKind.ServerError, 0x0402, "fizzled at 12:00", ActionReason.Fizzled)); // got over: the failure does not quote it
        Add(log, 3, new ActionOutcome(ActionOutcomeKind.Refused, 0, "no caster held: magic needs a caster", ActionReason.NoCasterHeld));
        Add(log, 4, new ActionOutcome(ActionOutcomeKind.Confirmed, 0, "combat mode 1"));

        Assert.Equal(ActionReason.NoCasterHeld, RecoveryTable.ReasonOf(log, since, "back: travel: recall:mode: Refused: no caster held: magic needs a caster"));
        Assert.Equal(ActionReason.None, RecoveryTable.ReasonOf(log, since, "back: travel: recall:mode: TimedOut: no confirmation within 5 s"));
        Assert.Equal(ActionReason.None, RecoveryTable.ReasonOf(log, since, "not enough mana: an old one")); // older than the station
        Assert.Equal(ActionReason.NoCasterHeld, RecoveryTable.ReasonOf(null, 0, "recall:mode: no caster held: the packs hold no caster")); // the recall's own, with no action behind it
        Assert.Equal(ActionReason.None, RecoveryTable.ReasonOf(log, since, null));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void Round_13_the_held_wand_refused_into_a_full_pack_then_a_recall_asking_magic_recovers_by_wielding_and_recalling_instead_of_stopping(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var rig = w.Trip.Vendor.R;
        var wand = w.Trip.Own("Wand", WandWcid, CasterType, 100);
        rig.Server((p, s) => AceM3Writers.WieldObject(p, s, wand, (int)Held));
        rig.Tick();
        Assert.Equal(wand, w.Trip.Vendor.State.Self.Inventory.EquippedAt(Held));

        var trips = 0;
        w.Factory.Script = spec => spec.Kind switch
        {
            // The trip's caster swap (03:06:37 UTC): ACE unwields the wand toward the full kit pack, refuses it (0x00A0) and drops it in the main pack; the trip's way back then asks the magic mode with the hands empty.
            StationKinds.Logistics when ++trips == 1 => new AsksMagic("back: travel: recall:", w, before: () => RefusedMove(w, wand), then: () => null),
            StationKinds.Logistics => new AsksMagic(string.Empty, w, before: null, then: () => ScenarioStatus.Succeeded) { Fields = "sold=1 bought=1" },
            StationKinds.RecallHome => new AsksMagic("recall-home: recall:", w, before: null, then: () => Home(w)),
            _ => null,
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });

        var status = w.Run(autopilot, a => trips >= 2 && a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight { Kills: >= 1 }, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Null(autopilot.StopName);
        var wield = Assert.Single(w.Trip.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem));
        Assert.Equal(wand, wield.Guid);
        Assert.Equal(wand, w.Trip.Vendor.State.Self.Inventory.EquippedAt(Held));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=NoCasterHeld fix=wield-caster after trip:", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=NoCasterHeld fix=wield-caster done: wield 'Wand' 0x", StringComparison.Ordinal) && l.Contains("a caster is held now", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=NoCasterHeld fix=wield-caster retry=ok: the recall-home station passed", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal)); // the typed recovery, not the generic one
        Assert.Equal(2, trips); // the cut trip was made again after the recall
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, perFrame: _ => AnswerWieldsAndModes(w)));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_full_pack_with_nothing_the_pack_rules_may_shed_stops_the_run_safely_with_the_reason(string key)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var free = Lifestone.Client.Session.Quartermaster.PackView.AllPacksLessFociFree(w.Trip.Vendor.State)!.Value;
        for (var i = 0; i < free; i++)
        {
            w.Trip.Own("Loot " + i, 9000 + (uint)i, VendorRig.MiscType, 10); // the packs full of loot, and no mana stone: nothing the pack rules shed away from a vendor
        }

        Assert.Equal(0, Lifestone.Client.Session.Quartermaster.PackView.AllPacksLessFociFree(w.Trip.Vendor.State));
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics
            ? new Failing(w, new ActionOutcome(ActionOutcomeKind.Refused, 0, "no free pack slot: the pick-up of 0x80009999 needs one and the packs have 0 free", ActionReason.NoFreeSlot))
            : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot, perFrame: _ => AnswerWieldsAndModes(w)));

        Assert.StartsWith("failed:trip:", autopilot.StopName, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=NoFreeSlot fix=shed-pack after trip:", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=NoFreeSlot fix=shed-pack failed: nothing can be shed by the pack rules", StringComparison.Ordinal) && l.Contains("the run stops safely", StringComparison.Ordinal));
        Assert.Contains("the shed-pack fix for NoFreeSlot could not be made", autopilot.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal)); // no generic recovery spent on it
        Assert.Empty(w.Trip.Vendor.H.Net.Of(FakeSendKind.DropItem)); // nothing was dropped
    }

    [Theory]
    [InlineData(Den, 2, true)]
    [InlineData(Den, 99, false)]
    public void M6_7_I1_the_same_station_failing_again_from_the_same_cell_settles_and_steps_before_its_last_retry_and_then_stops_by_name(string key, int homeFailures, bool survives)
    {
        var w = new ApWorld(key, output, atHome: false) { Prime = c => Proven(c, key) };
        var trips = 0;
        var homes = 0;
        w.Factory.Script = spec => spec.Kind switch
        {
            StationKinds.Logistics when ++trips == 1 => new Plain("back: travel: run: no path: indoor-start-unreachable"),
            StationKinds.Logistics => new AsksMagic(string.Empty, w, before: null, then: () => ScenarioStatus.Succeeded) { Fields = "sold=1 bought=1" },
            StationKinds.RecallHome when ++homes <= homeFailures => new Plain("walk-home: run 'journey-bind-" + homes + "' no path: indoor-start-unreachable"), // the leg's number changes; the failure is the same
            StationKinds.RecallHome => new Plain(null) { Reaches = () => Home(w) },
            _ => null,
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });

        var status = w.Run(autopilot, a => survives && trips >= 2 && a.State == AutopilotState.Fight, perFrame: _ => AnswerWieldsAndModes(w));

        Assert.Equal(1, w.Lines.Count(l => l.StartsWith("autopilot recovery: repeat=2 fix=settle-step", StringComparison.Ordinal))); // the second identical failure settles and steps; its retry is not charged
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=None fix=settle-step done: settled 5 s", StringComparison.Ordinal));
        if (survives)
        {
            Assert.Equal(ScenarioStatus.Running, status);
            Assert.Null(autopilot.StopName);
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery: reason=None fix=settle-step retry=ok", StringComparison.Ordinal));
            autopilot.RequestStop("test");
            Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot, perFrame: _ => AnswerWieldsAndModes(w)));
        }
        else
        {
            Assert.Equal(ScenarioStatus.Failed, status);
            Assert.StartsWith("failed:", autopilot.StopName, StringComparison.Ordinal);
            Assert.Equal(3, homes); // the first, the second, and the last retry after the settle and the step: no fourth
        }
    }

    [Fact]
    public void M6_7_I1_the_signature_collapses_digits_and_tells_stations_and_cells_apart()
    {
        Assert.Equal(RecoveryTable.Signature("failed:travel:a", "cell 0x1", "leg bind-11 at 6.6 m"), RecoveryTable.Signature("failed:travel:a", "cell 0x1", "leg bind-12 at 7.1 m"));
        Assert.NotEqual(RecoveryTable.Signature("failed:travel:a", "cell 0x1", "x"), RecoveryTable.Signature("failed:travel:a", "cell 0x2", "x"));
        Assert.NotEqual(RecoveryTable.Signature("failed:travel:a", "cell 0x1", "x"), RecoveryTable.Signature("failed:trip:a", "cell 0x1", "x"));
        Assert.Equal("settle-step", RecoveryTable.Name(RecoveryFix.SettleStep));
    }

    private static void Proven(JourneyContext c, string key)
    {
        c.Facts.LifestoneTied = true;
        c.Facts.SetPortalTie(key, PortalTie.Tied);
    }

    private static void Add(ActionLog log, long id, ActionOutcome outcome) => log.Add(new ActionLogEntry(id, ActionKind.SetCombatMode, ActionSource.Automation, "test", outcome, 0, 0));

    /// <summary>ACE's refused move of the held wand (Player_Inventory.cs 1268-1340 @95ef426d): 02DA Wielder 0, 02CE CurrentWieldedLocation 0, then 0x00A0; the model puts it in the main pack (P7a, D139 l).</summary>
    private static void RefusedMove(ApWorld w, uint wand)
    {
        var rig = w.Trip.Vendor.R;
        rig.Raw(AceM3Writers.PublicUpdateInstanceId(40, wand, 3, 0));
        rig.Raw(AceM3Writers.PublicUpdatePropertyInt(40, wand, 10, 0));
        rig.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, wand, 0));
        rig.CombatModeIs(CombatModes.Melee); // ACE's stance swap when the held caster leaves the hand in the magic mode (TryDequipObjectWithNetworking, Player_Inventory.cs 437-451 @95ef426d; P7n: the audit now holds magic)
    }

    private static ScenarioStatus? Home(ApWorld w)
    {
        w.Trip.Vendor.StandAt(w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint);
        return ScenarioStatus.Succeeded;
    }

    /// <summary>
    /// A scripted station that acts as the real ones do at their magic step (the recall's <c>StepMode</c>): <paramref name="before"/> once, then the magic mode asked of the Action API; refused or failed, the station fails with the
    /// outcome quoted as the recall quotes it (<c>&lt;prefix&gt;mode: &lt;kind&gt;: &lt;detail&gt;</c>); confirmed, <paramref name="then"/> ends it.
    /// </summary>
    private sealed class AsksMagic(string prefix, ApWorld w, Action? before, Func<ScenarioStatus?> then) : IStationRun
    {
        private ActionHandle? _mode;
        private ScenarioStatus _status = ScenarioStatus.Running;

        public string Fields { get; init; } = "recalls=1";

        public string Name => "asks-magic";

        public string Stage => "scripted";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => _status == ScenarioStatus.Succeeded;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_status != ScenarioStatus.Running)
            {
                return _status;
            }

            if (_mode is null)
            {
                before?.Invoke();
                w.Trip.Vendor.R.Tick();
                _mode = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Automation);
                return _status;
            }

            if (_mode.Outcome is not { } outcome)
            {
                return _status;
            }

            if (!outcome.Succeeded)
            {
                Failure = prefix + "mode: " + outcome.Kind + ": " + outcome.Detail;
                _status = ScenarioStatus.Failed;
                return _status;
            }

            _status = then() ?? ScenarioStatus.Failed;
            return _status;
        }

        public string VerdictFields() => Fields;
    }

    /// <summary>A scripted station that fails with a typed action failure it records in the session's action log (an action's ending, as the Action API writes it), quoting its detail.</summary>
    private sealed class Failing(ApWorld w, ActionOutcome outcome) : IStationRun
    {
        public string Name => "failing";

        public string Stage => "scripted";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => false;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (Failure is null)
            {
                Add(w.Trip.Vendor.H.ActionLog, 9000 + session.ActionLog.Total, outcome);
                Failure = "loot: pick-up: " + outcome.Kind + ": " + outcome.Detail;
            }

            return ScenarioStatus.Failed;
        }

        public string VerdictFields() => "sold=0 bought=0";
    }

    /// <summary>A scripted station that fails at once with a plain, untyped failure text.</summary>
    private sealed class Plain(string? failure) : IStationRun
    {
        public string Name => "plain";

        public string Stage => "scripted";

        public string? Failure => failure;

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool Ok => failure is null;

        /// <summary>Set: the station ends well after this (it stands the player where it says).</summary>
        public Func<ScenarioStatus?>? Reaches { get; init; }

        public ScenarioStatus Step(IGameSessionView session, double now) => failure is null ? Reaches?.Invoke() ?? ScenarioStatus.Succeeded : ScenarioStatus.Failed;

        public string VerdictFields() => "sold=0 bought=0";
    }
}
