using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 item 3 c1, "the grants counted and removed" (DoD 6, spec 5.6): <see cref="ModVerbCounter"/> counts every mod verb the session sends at the one send path (<see cref="PlayerTools"/>), by verb; the journey reads the delta from the end of its
/// build-verify station and prints <c>granted=</c> (every verb but the place: items, experience, <c>corpses rot</c> and <c>spell learn</c>, M5 3 F2, 3-R8) and <c>places=</c>; a recovery place is counted apart; the mage's script stations (kit-refused, cast-facts, harm-heal, and <see cref="ScriptLeg.GrantIfMissing"/>) send no mod verb;
/// and the heal station's casts run on an offline rig (<c>k4-heal-station-offline</c>). The gear station's no-grant mode is in <c>GearScenarioTests</c>.
/// </summary>
public sealed partial class GrantsCountedTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly uint[] HarmAndHeal = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];

    [GeneratedRegex(@"^journey done ok=(true|false) name=\S+ stations=\d+/\d+ failed=\S+ teleports=\d+ recalls=\d+ budget=\d+ route_m=\d+\.\d run_s=\d+\.\d rungs=\d+ blocked=\d+ sightless=\d+ vitals_passes=\d+ recovery_places=\d+ granted=(\d+) places=(\d+) recovery_place=[01] recovery=(none|suicide)$")]
    private static partial Regex DoneShape();

    private static TeleportDestination Somewhere() =>
        new("somewhere", [], new WorldLocation("somewhere", new CellPosition(0xA9B40111, Vector3.Zero), HasHeight: true, HeadingDegrees: 0), "test");

    // ------------------------------------------------------------------------------------------------ the counter

    [Fact]
    public void Each_mod_verb_is_counted_once_at_the_seam_by_verb_and_the_other_requests_are_not_counted()
    {
        var rig = new J3aStationRig();
        var tools = rig.View.Tools;
        Assert.Equal(default, tools.Verbs.Counts);

        tools.SpawnItem(628, null, ActionSource.Host);
        tools.AddXp(1000, ActionSource.Host);
        tools.Place(Somewhere(), ActionSource.Host);
        tools.RotCorpses(ActionSource.Host);
        tools.LearnSpells([1, 2, 3], ActionSource.Host);

        Assert.Equal(new ModVerbCounts(Item: 1, Xp: 1, Place: 1, CorpsesRot: 1, SpellLearn: 1), tools.Verbs.Counts);
        Assert.Equal(1, tools.Verbs.Of(ModVerb.CorpsesRot));
        Assert.Equal(4, tools.Verbs.Counts.Granted); // the item, the experience, corpses rot and spell learn: every verb but the place (M5 3 F2, 3-R8)
        Assert.Equal(5, tools.Verbs.Counts.Total);

        // The requests that change nothing are no verb of the ledger.
        tools.Status(ActionSource.Host);
        tools.WhereAmI(ActionSource.Host);
        tools.HasQuest("SomeQuest", ActionSource.Host);
        Assert.Equal(5, tools.Verbs.Counts.Total);
    }

    [Fact]
    public void A_request_refused_before_it_is_sent_counts_nothing_and_each_item_request_counts_one()
    {
        var rig = new J3aStationRig();
        var tools = rig.View.Tools;

        var refused = tools.SpawnItem(628, 0, ActionSource.Host); // a stack outside 1 to 65,535 is refused at once and never sent
        Assert.Equal(ActionOutcomeKind.Refused, refused.Outcome!.Value.Kind);
        Assert.Equal(0, tools.Verbs.Counts.Total);

        tools.SpawnItem(628, 1, ActionSource.Host);
        tools.SpawnItem(377, 5, ActionSource.Host);
        Assert.Equal(2, tools.Verbs.Counts.Item);
    }

    [Fact]
    public void The_delta_since_a_mark_leaves_the_builds_verbs_out_and_a_recovery_place_is_counted_apart_from_places()
    {
        var rig = new J3aStationRig();
        var tools = rig.View.Tools;
        tools.AddXp(5000, ActionSource.Host); // the build
        tools.SpawnItem(628, null, ActionSource.Host);
        tools.Place(Somewhere(), ActionSource.Host);
        var mark = tools.Verbs.Counts;

        tools.SpawnItem(377, 1, ActionSource.Host);
        tools.Place(Somewhere(), ActionSource.Host);
        using (tools.Verbs.RecoveryPlaces())
        {
            tools.Place(Somewhere(), ActionSource.Host);
        }

        tools.Place(Somewhere(), ActionSource.Host); // outside the scope again: a place

        var play = tools.Verbs.Since(mark);
        Assert.Equal(new ModVerbCounts(Item: 1, Place: 2, RecoveryPlace: 1), play);
        Assert.Equal(1, play.Granted);
        Assert.Equal(1, tools.Verbs.Counts.Xp); // the whole counts keep the build's
        Assert.Equal("item=1 xp=0 place=2 corpses_rot=0 spell_learn=0 recovery_place=1", play.Describe());
    }

    // ------------------------------------------------------------------------------------------------ the done line

    [Fact]
    public void The_done_line_prints_granted_as_items_plus_experience_and_places_without_the_recovery_place_after_the_build()
    {
        var verdict = new JourneyVerdict(1);
        verdict.Add(new JourneyStationResult("s1", true, 1.0, string.Empty));
        var facts = new JourneyFacts { TeleportBudget = 1, Verbs = new ModVerbCounts(Item: 2, Xp: 1, Place: 3, RecoveryPlace: 1) };

        var line = verdict.DoneLine("mage", facts, 0, 0);

        Assert.Contains(" granted=3 places=3 recovery_place=", line, StringComparison.Ordinal);
        Assert.Matches(DoneShape(), line);
        Assert.Contains(" granted=0 places=0 ", verdict.DoneLine("mage", new JourneyFacts { TeleportBudget = 1 }, 0, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void Granted_counts_every_verb_but_the_place_so_a_corpses_rot_or_a_spell_learn_after_the_build_is_on_the_done_line_M5_3_F2()
    {
        // 3-R8: DoD 6 names four verbs (item, xp, corpses rot, spell learn); the done line's granted= counts them all and places= counts the place only.
        var counts = new ModVerbCounts(Item: 1, Xp: 1, Place: 2, CorpsesRot: 1, SpellLearn: 1, RecoveryPlace: 1);
        Assert.Equal(4, counts.Granted);
        Assert.Equal(1, new ModVerbCounts(CorpsesRot: 1).Granted);
        Assert.Equal(1, new ModVerbCounts(SpellLearn: 1).Granted);
        Assert.Equal(0, new ModVerbCounts(Place: 3, RecoveryPlace: 1).Granted);
        Assert.Contains(" granted=4 places=2 ", new JourneyVerdict(1).DoneLine("mage", new JourneyFacts { TeleportBudget = 1, Verbs = counts }, 0, 0), StringComparison.Ordinal);
    }

    [Fact]
    public void A_corpses_rot_and_a_spell_learn_sent_after_the_build_verify_station_read_granted_2_on_the_journeys_done_line_M5_3_F2()
    {
        // Through a journey: the build-verify station's verbs are build time; a corpses rot and a spell learn sent by the next station are play.
        var t = new J1JourneyRig();
        var factory = new J1Factory(new()
        {
            ["build-verify"] = new J1StationScript(OnEnd: s => { s.Tools.RotCorpses(ActionSource.Host); s.Tools.LearnSpells([1, 2], ActionSource.Host); }),
            ["rot"] = new J1StationScript(OnEnd: s => { s.Tools.RotCorpses(ActionSource.Host); s.Tools.LearnSpells([3], ActionSource.Host); }),
        });
        var plan = new JourneyPlan(
            "r8",
            "BSD Mage",
            "test",
            StationWhere.Belt,
            1,
            [new StationSpec("build-verify", StationKinds.BuildVerify, StationNeeds.None, 30), new StationSpec("rot", StationKinds.HarmHeal, StationNeeds.None, 30)]);
        var journey = t.Journey(plan, factory, t.Context());

        t.Run(journey);

        var done = journey.Lines[^1];
        Assert.Matches(DoneShape(), done);
        Assert.Contains(" granted=2 places=0 ", done, StringComparison.Ordinal);
    }

    [Fact]
    public void A_journey_counts_what_the_session_sent_after_its_build_verify_station_and_reads_zero_when_nothing_was()
    {
        // The build-verify station sends a grant and a place (build time, the owner's cheat layer); the next station sends one item, one experience grant and one place.
        var t = new J1JourneyRig();
        var factory = new J1Factory(new()
        {
            ["build-verify"] = new J1StationScript(OnEnd: s => { s.Tools.SpawnItem(628, null, ActionSource.Host); s.Tools.Place(Somewhere(), ActionSource.Host); s.Tools.AddXp(10, ActionSource.Host); }),
            ["grants"] = new J1StationScript(OnEnd: s => { s.Tools.SpawnItem(377, 1, ActionSource.Host); s.Tools.AddXp(10, ActionSource.Host); s.Tools.Place(Somewhere(), ActionSource.Host); }),
        });
        var plan = new JourneyPlan(
            "c1",
            "BSD Mage",
            "test",
            StationWhere.Belt,
            1,
            [new StationSpec("build-verify", StationKinds.BuildVerify, StationNeeds.None, 30), new StationSpec("grants", StationKinds.HarmHeal, StationNeeds.None, 30)]);
        var journey = t.Journey(plan, factory, t.Context());

        t.Run(journey);

        var done = journey.Lines[^1];
        Assert.Matches(DoneShape(), done);
        Assert.Contains(" granted=2 places=1 ", done, StringComparison.Ordinal); // the build's item, experience and place are not in it
        Assert.Contains(t.Log.Entries, e => e.Message.Contains("journey verbs marked at the end of build-verify", StringComparison.Ordinal));

        var quiet = new J1JourneyRig();
        var quietPlan = new JourneyPlan(
            "c1q",
            "BSD Mage",
            "test",
            StationWhere.Belt,
            1,
            [new StationSpec("build-verify", StationKinds.BuildVerify, StationNeeds.None, 30), new StationSpec("harm-heal", StationKinds.HarmHeal, StationNeeds.None, 30)]);
        var quietJourney = quiet.Journey(quietPlan, new J1Factory(new() { ["build-verify"] = new J1StationScript(OnEnd: s => s.Tools.SpawnItem(628, null, ActionSource.Host)) }), quiet.Context());
        quiet.Run(quietJourney);
        Assert.Contains(" granted=0 places=0 ", quietJourney.Lines[^1], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the mage's stations send no mod verb

    [Fact]
    public void The_kit_refused_station_grants_nothing_with_no_kit_held_and_ends_ok_skipped_by_name()
    {
        var rig = new J3aStationRig();
        var run = (KitRefusedRun)rig.Start(new KitRefusedStation(), "BSD Mage");

        var status = rig.Run(run, 40);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(run.Ok, string.Join(" ; ", run.Problems));
        Assert.Contains(" skipped=precondition:handy-healing-kit", run.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, rig.ToolRequests("item"));
        Assert.Equal(0, rig.View.Tools.Verbs.Counts.Total);
        Assert.Equal(0, run.Granted);
    }

    [Fact]
    public void The_cast_facts_station_sends_no_mod_verb()
    {
        var rig = new J3aStationRig();
        var run = rig.Start(new CastFactsStation("account"), "BSD Mage");

        rig.Run(run, 40);

        Assert.Equal(0, rig.View.Tools.Verbs.Counts.Total);
        Assert.Equal(0, rig.ToolRequests("item"));
    }

    [Fact]
    public void A_mage_station_that_needs_an_item_it_does_not_hold_fails_a_named_check_and_sends_no_grant_while_a_fighter_still_gets_it_counted()
    {
        var mage = new J3aStationRig();
        NameThePlayer(mage.R, "BSD Mage");
        var probe = new GrantProbe();
        mage.Run(probe, 60);
        Assert.Contains(probe.Problems, p => p.Contains("is not in the pack and play grants nothing (M5 DoD 6)", StringComparison.Ordinal));
        Assert.Equal(0, mage.View.Tools.Verbs.Counts.Total);
        Assert.Equal(0, probe.Granted);

        var fighter = new J3aStationRig();
        NameThePlayer(fighter.R, "Holtburg Brawler");
        fighter.Run(new GrantProbe(), 8);
        Assert.Equal(1, fighter.View.Tools.Verbs.Counts.Item); // the fighters' plans keep their grants (their DoD is M4's), counted by the same counter
    }

    [RequiresDatsFact]
    public void The_mage_journeys_buff_station_verifies_the_journey_grounds_kit_and_sends_no_grant_on_every_dungeon_M5_3_F2()
    {
        // 3-R3: the buff station's gear stage (BuffScenario) used to grant over the default ground's kit; it now verifies the kit of the journey's --fight-ground and asks the mod for nothing. The rig's pack holds nothing, so the run fails at the
        // gear stage naming the ground's own robe: a granting stage would have sent the item verb, and a stage over the default ground's kit would have named the den's robe on Tusker Temple.
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var dungeons = GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon && g.Supplies.Kit.Count > 0).ToList();
        Assert.True(dungeons.Count >= 2);
        foreach (var ground in dungeons)
        {
            var factory = JourneyStations.Create(JourneyPlans.Mage, [$"--fight-ground={ground.Key}"], null, "test");
            var spec = JourneyPlans.Mage.Stations.Single(s => s.Name == "buff") with { NeedsOverride = StationNeeds.None };
            var rig = new J3aStationRig(spells: spells);
            rig.R.H.R.Reducer.OnPlayerDescription(rig.R.H.R.Context, new GameEventHeader(WorldActionRig.Player, 1, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Inventory = [], Equipped = [] }); // the pack is known, and empty
            var run = rig.Start(factory.Create(spec), "BSD Mage");

            var status = rig.Run(run, 40);

            Assert.True(status == ScenarioStatus.Failed, $"{ground.Key}: {status} at stage {run.Stage} {run.Failure} {string.Join(" | ", rig.R.H.Log.Lines.TakeLast(6))}");
            Assert.Equal(0, rig.View.Tools.Verbs.Counts.Total);
            Assert.Equal(0, rig.ToolRequests("item"));
            var robe = GearKit.Build(spells, "test", ground: ground).Items.First(i => i.Kind == GearKind.Armour);
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("play grants nothing (M5 DoD 6)", StringComparison.Ordinal) && l.Contains($"weenie {robe.Wcid}", StringComparison.Ordinal));
            foreach (var other in dungeons.Where(o => o.Key != ground.Key))
            {
                var otherRobe = GearKit.Build(spells, "test", ground: other).Items.First(i => i.Kind == GearKind.Armour);
                if (otherRobe.Wcid != robe.Wcid)
                {
                    Assert.DoesNotContain(rig.R.H.Log.Lines, l => l.Contains($"weenie {otherRobe.Wcid})", StringComparison.Ordinal));
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ k4-heal-station-offline

    [Fact]
    public void K4_the_mages_heal_station_runs_offline_its_Heal_Self_casts_go_through_the_cast_policy_and_the_keeper_restores_the_health_with_no_mod_verb()
    {
        var c2 = new C2VitalsRig(new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = HarmAndHeal });
        var rig = new J3aStationRig(c2.R, c2.Table);
        var run = (HarmHealRun)rig.Start(new HarmHealStation(), "BSD Mage", c2.Keeper);

        var status = rig.Run(run, 16000, pump: c2.Pump);

        var trace = string.Join(" | ", c2.R.H.Log.Lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal)).TakeLast(6));
        Assert.True(status == ScenarioStatus.Succeeded && run.Ok, $"{status} {run.Failure} {string.Join(" ; ", run.Problems)} {trace}");
        Assert.Equal(HarmHealState.Done, run.Harm!.State);

        // The mage heals by Heal Self (no kit, no potion, no swap): the casts are the keeper's and went through the cast policy as Automation.
        Assert.Contains(c2.Casts, c => VitalsMeans.HealSelf.Contains(c.Spell) && !c.Fizzled);
        Assert.Contains(c2.R.H.Log.Lines, l => l.Contains("[cast]", StringComparison.Ordinal) && l.Contains("Automation", StringComparison.Ordinal) && l.Contains("Heal Self", StringComparison.Ordinal));
        var log = c2.R.H.ActionLog;
        var casts = 0;
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Kind == ActionKind.CastSpell)
            {
                casts++;
                Assert.True(log[i].Source == ActionSource.Automation, $"a cast was sent as {log[i].Source}: {log[i].Summary}");
            }
        }

        Assert.True(casts > 0);
        Assert.DoesNotContain(c2.Used, u => u is "kit" or "potion"); // the mage's means is the spell
        Assert.True(c2.LowestHealth < 150, "the station harmed the mage: " + c2.LowestHealth); // it went down...
        Assert.True(c2.Health > c2.LowestHealth + 40, $"the keeper restored {c2.LowestHealth} -> {c2.Health}"); // ...and the health came back
        Assert.Equal(0, c2.R.Api.Tools.Verbs.Counts.Total);
        Assert.Equal(0, run.Granted);
    }

    private static void NameThePlayer(WorldActionRig r, string name)
    {
        var basis = ReducerHarness.Create(WorldActionRig.Player, ReducerHarness.Stamps(), null, name: name);
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, basis, isUpdate: true);
    }

    /// <summary>A station leg that needs a Handy Healing Kit (628) the pack does not hold.</summary>
    private sealed class GrantProbe : ScriptLeg
    {
        public GrantProbe()
            : base("c1-probe", _ => { })
        {
        }

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in GrantIfMissing(() => 0u, 628, "Handy Healing Kit 628", _ => { })) { yield return h; }
        }
    }
}
