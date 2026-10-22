using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The death loop's corpse stage (M4 2.2 s1, D5; <see cref="DeathStages.Corpse"/>) over the same scripted ACE as the rest of this class, now also answering a corpse's open (0196, UseDone, F745s), the pick-ups (0022), the close
/// (0052) and the trip's places: the happy path (found, opened, every item taken one at a time, closed, placed back, <c>corpse_looted=yes items_recovered=n</c>), each precondition's skip, each abort, a death on the trip, and
/// <c>--death-recover-corpse off</c> being M3's run. The re-gear is not scripted here (the rig has no inventory); the kit change it needs is <c>GearScenarioTests</c>'.
/// </summary>
public sealed partial class DeathScenarioTests
{
    private static DeathStages Trip => Core | DeathStages.Corpse;

    private static Rig TripRig(int items = 3)
    {
        var rig = new Rig(buffed: false);
        for (var i = 0; i < items; i++)
        {
            rig.CorpseItems.Add(LootRulesTests.Item(0x80, 10, 5, 0x80000B10u + (uint)i, "Thing " + i, 141u + (uint)i));
        }

        return rig;
    }

    /// <summary>Frames until the scenario enters <paramref name="stage"/> (it must still be running when it does).</summary>
    private static void RunUntilStage(Rig rig, DeathScenario scenario, string stage)
    {
        var end = rig.Now + 900;
        while (scenario.Stage != stage && rig.Now < end)
        {
            Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario));
        }

        Assert.Equal(stage, scenario.Stage);
    }

    private static List<string> StageLog(Rig rig) => [.. Lines(rig).Where(l => l.StartsWith("stage ", StringComparison.Ordinal)).Select(l => l[6..])];

    private static readonly string[] M3Stages = ["arriving", "bind-place", "bind-teleporting", "bind-settle", "bind-use", "bind-line", "den-place", "den-teleporting", "den-settle", "unbuff", "death-mode", "death-wait", "respawn", "respawn-vitals"];

    // --- the happy path

    [Fact]
    public void The_trip_goes_to_the_corpse_opens_it_takes_every_item_one_at_a_time_closes_it_and_places_back_to_Holtburg()
    {
        var rig = TripRig(3);
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(3, scenario.ItemsRecovered);
        Assert.Equal(DeathLegVerdict.TripAbortNone, scenario.TripAbort);
        Assert.Equal([rig.CorpseGuid], rig.Opened);
        Assert.Equal(rig.CorpseItems.Select(i => i.Guid), rig.PickUps); // every item, in the 0196's order, one pick-up each
        Assert.Equal(1, rig.Closes);
        Assert.All(rig.CorpseItems, i => Assert.True(rig.R.H.State.Self.Inventory.Find(i.Guid).Held, "0x" + i.Guid.ToString("X8", CultureInfo.InvariantCulture) + " is in the pack"));

        // The places: the bind's point, the den, the corpse (an exact location in the cell the corpse died in), then Holtburg's point (the bind's) again.
        Assert.Equal(4, rig.Places.Count);
        Assert.Contains("loc", rig.Places[2], StringComparison.Ordinal);
        Assert.Contains(ToolsWire.FormatCell(DenArrival), rig.Places[2], StringComparison.Ordinal);
        Assert.Contains("poi", rig.Places[3], StringComparison.Ordinal);
        Assert.Contains("Holtburg", rig.Places[3], StringComparison.Ordinal);
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);

        Assert.Equal([.. M3Stages, "corpse-teleporting", "corpse-settle", "corpse-seek", "corpse-opening", "corpse-looting", "corpse-closing", "corpse-return", "corpse-return-settle"], StageLog(rig));
        var fields = scenario.VerdictFields();
        Assert.Contains(" died=1 ", fields, StringComparison.Ordinal);
        Assert.Matches(new Regex(@" corpse_looted=yes items_recovered=3 trip_s=\d+\.\d trip_abort=none "), fields);
        Assert.True(scenario.TripElapsedSeconds is > 5 and < DeathScenario.TripSeconds, "the trip took " + scenario.TripElapsedSeconds);
        Assert.Contains(Lines(rig), l => l.Contains("loot: done looted=3", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tally_remembers_the_deaths_own_corpse_its_cell_and_position_and_the_clock_of_its_F745()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Core);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        var tally = scenario.Tally;
        Assert.Equal(rig.CorpseGuid, tally.LastCorpseGuid);
        var at = Assert.NotNull(tally.LastCorpsePosition);
        Assert.Equal(DenArrival, at.CellId);
        Assert.Equal(50f, at.Frame.Origin.X);
        Assert.Equal(30f, at.Frame.Origin.Y);
        Assert.True(tally.LastCorpseAt > 0 && tally.LastCorpseAt <= rig.Now);
    }

    [Fact]
    public void The_trips_blows_and_teleports_do_not_move_the_death_loops_judged_fields()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        RunUntilStage(rig, scenario, "corpse-teleporting");
        var hitsAtStart = scenario.Tally.HitsTaken;
        var deathsAtStart = scenario.Tally.Deaths;
        rig.Damage = 1;
        rig.TripBlows = true;
        rig.BlowEvery = 3;

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.True(scenario.Tally.HitsTaken > hitsAtStart, "the tuskers did hit the mage on the trip");
        var fields = scenario.VerdictFields();
        Assert.Contains(" died=1 ", fields, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $" deaths={deathsAtStart} "), fields, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $" hits_taken={hitsAtStart} "), fields, StringComparison.Ordinal); // frozen at the end of the respawn's vitals
        Assert.Contains(" protection=none ", fields, StringComparison.Ordinal); // the trip's teleports dispelled nothing the verdict reports
        Assert.Contains(" corpse_looted=yes ", fields, StringComparison.Ordinal);
    }

    // --- the preconditions

    [Fact]
    public void A_health_share_under_the_minimum_skips_the_trip_and_names_it()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Trip, new DeathOptions { RecoverMinHealthPercent = 90 });

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(CorpseLoot.Skipped, scenario.CorpseLooted);
        Assert.Equal(2, rig.Places.Count); // the bind's and the den's: no trip
        Assert.Empty(rig.Opened);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: skipped (corpse_looted=skipped): the health share is ", StringComparison.Ordinal) && l.Contains("under --death-recover-min-health 90%", StringComparison.Ordinal));
        Assert.Contains(" corpse_looted=skipped items_recovered=0 trip_s=n/a trip_abort=none ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(M3Stages, StageLog(rig));
    }

    [Fact]
    public void A_corpse_at_or_past_ACEs_rot_floor_skips_the_trip_and_names_it()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        RunUntilStage(rig, scenario, "respawn-vitals");
        rig.R.Tick(DeathScenario.PlayerCorpseMinRotSeconds + 100); // an hour and more pass between the corpse's F745 and the end of the respawn's vitals wait

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(CorpseLoot.Skipped, scenario.CorpseLooted);
        Assert.Equal(2, rig.Places.Count);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: skipped (corpse_looted=skipped): the corpse is ", StringComparison.Ordinal) && l.Contains("past ACE's rot floor of 3600 s (Corpse.cs 103-118)", StringComparison.Ordinal));
    }

    [Fact]
    public void No_remembered_corpse_skips_the_trip_and_names_it()
    {
        var rig = TripRig();
        rig.NoCorpse = true;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(CorpseLoot.Skipped, scenario.CorpseLooted);
        Assert.Equal(2, rig.Places.Count);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: skipped (corpse_looted=skipped): no corpse of the mage was remembered", StringComparison.Ordinal));
    }

    [Fact]
    public void Recover_corpse_off_and_a_run_without_the_stage_are_M3s_run_exactly()
    {
        var off = TripRig();
        var offScenario = off.Scenario(new FakeWalk(), Trip, new DeathOptions { RecoverCorpse = false });
        Assert.Equal(ScenarioStatus.Succeeded, off.Run(offScenario));

        Assert.Equal(M3Stages, StageLog(off)); // no corpse stage was entered
        Assert.Equal(2, off.Places.Count);
        Assert.Empty(off.Opened);
        Assert.Empty(off.PickUps);
        Assert.Contains(Lines(off), l => l.Contains("--death-recover-corpse is off", StringComparison.Ordinal));
        Assert.Contains(" corpse_looted=skipped items_recovered=0 trip_s=n/a trip_abort=none ", offScenario.VerdictFields(), StringComparison.Ordinal);

        var without = TripRig();
        var withoutScenario = without.Scenario(new FakeWalk(), Core);
        Assert.Equal(ScenarioStatus.Succeeded, without.Run(withoutScenario));

        Assert.Equal(M3Stages, StageLog(without));
        Assert.DoesNotContain(Lines(without), l => l.StartsWith("corpse:", StringComparison.Ordinal));
        Assert.Empty(without.Opened);
        Assert.Equal(CorpseLoot.Skipped, withoutScenario.CorpseLooted);
    }

    // --- the aborts: each is named, counted as trip_abort=<reason>, and still places back to Holtburg

    private void AssertAbortedAndBack(Rig rig, DeathScenario scenario, string reason)
    {
        Dump(rig, scenario);
        Assert.Null(scenario.Failure);
        Assert.Equal(reason, scenario.TripAbort);
        Assert.Equal(CorpseLoot.No, scenario.CorpseLooted);
        Assert.Contains(Lines(rig), l => l.StartsWith("corpse: trip aborted (" + reason + "):", StringComparison.Ordinal));
        Assert.Contains(" trip_abort=" + reason + " ", scenario.VerdictFields() + " ", StringComparison.Ordinal);
        Assert.Contains("poi", rig.Places[^1], StringComparison.Ordinal); // the abort placed back to Holtburg
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);
        Assert.Contains(" died=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_health_share_under_forty_percent_aborts_the_trip_and_places_back()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        RunUntilStage(rig, scenario, "corpse-settle");
        rig.SetHealth(30);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        AssertAbortedAndBack(rig, scenario, "health-under-40");
        Assert.Empty(rig.Opened);
        Assert.Equal(0, scenario.ItemsRecovered);
    }

    [Fact]
    public void A_trip_over_ninety_seconds_aborts_while_looting_closes_the_view_and_places_back()
    {
        var rig = TripRig(6);
        rig.PickUpSilent = true; // each pick-up times out: the loop goes on to the next, and the trip's clock runs out
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        AssertAbortedAndBack(rig, scenario, "trip-over-90s");
        Assert.Equal([rig.CorpseGuid], rig.Opened);
        Assert.True(rig.Closes >= 1, "the open view was closed fire-and-forget");
        Assert.True(scenario.TripElapsedSeconds >= DeathScenario.TripSeconds);
    }

    [Fact]
    public void A_corpse_that_is_not_in_the_table_within_fifteen_seconds_aborts_the_trip()
    {
        var rig = TripRig();
        rig.CorpseGone = true;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        AssertAbortedAndBack(rig, scenario, "corpse-not-found");
        Assert.Empty(rig.Opened);
        Assert.True(scenario.TripElapsedSeconds >= DeathScenario.CorpseFindSeconds);
    }

    [Fact]
    public void A_corpse_that_comes_back_late_but_within_fifteen_seconds_is_found_and_looted()
    {
        var rig = TripRig(2);
        rig.CorpseBackAfter = 9;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal(2, scenario.ItemsRecovered);
    }

    [Fact]
    public void A_refused_open_aborts_the_trip_and_places_back()
    {
        var rig = TripRig();
        rig.OpenRefused = true;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        AssertAbortedAndBack(rig, scenario, "open-refused");
        Assert.Equal([rig.CorpseGuid], rig.Opened);
        Assert.Empty(rig.PickUps);
        Assert.Equal(0, scenario.ItemsRecovered);
    }

    [Fact]
    public void An_arrival_off_the_dens_floor_aborts_the_trip_by_the_dens_own_rule()
    {
        var rig = TripRig();
        rig.TripCell = NotOnTheFloor;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        AssertAbortedAndBack(rig, scenario, "off-floor");
        Assert.Empty(rig.Opened);
    }

    [Fact]
    public void A_teleport_the_server_refuses_aborts_the_trip_and_places_nothing_back_because_the_mage_never_left()
    {
        var rig = TripRig();
        rig.RefuseTripPlace = true;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));

        Assert.Null(scenario.Failure);
        Assert.Equal("place-refused", scenario.TripAbort);
        Assert.Equal(CorpseLoot.No, scenario.CorpseLooted);
        Assert.Equal(3, rig.Places.Count); // the bind's, the den's and the refused corpse place: no way back was needed
        Assert.Equal(HoltburgCell, rig.R.H.State.Self.Position?.CellId);
    }

    // --- M4 2.2 fix: the first live round's findings (R1 the vitals, R2 partial, R3 the drain, R5 the hold)

    [Fact]
    public void M4_2_2_fix_R1_with_no_suicide_a_stamina_and_a_mana_update_of_zero_before_ACEs_restore_leave_vitals_pct_at_the_restore()
    {
        // The live order of round 1: no suicide, the arrival, the drain of 30 evades reaching the client as stamina 0 and mana 0, then ACE's 75% three seconds after the teleport.
        var rig = new Rig(buffed: false) { ZeroesBeforeRestore = true };
        var scenario = rig.Scenario(new FakeWalk(), Core);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal(0, rig.Suicides);
        var tally = scenario.Tally;
        Assert.Equal(0.0, tally.FirstStaminaAfterArrivalPercent, 3); // the first updates are the zeroes
        Assert.Equal(0.0, tally.FirstManaAfterArrivalPercent, 3);
        Assert.InRange(tally.VitalsAfterArrivalPercent, 60, 90); // DeathLiveTests (g)'s range: the value at the end of the wait
        Assert.InRange(tally.VitalsAfterArrivalPercent, 70, 80);
        Assert.Matches(new Regex(@" vitals_pct=7\d\.\d "), scenario.VerdictFields());
        var line = Assert.Single(Lines(rig), static l => l.StartsWith("respawn: lifestone (", StringComparison.Ordinal));
        Assert.Matches(new Regex(@"vitals 7\d% \(first health=7\d stamina=0 mana=0; at_wait_end health=7\d stamina=7\d mana=7\d\)"), line);
    }

    [Fact]
    public void M4_2_2_fix_R1_the_trips_later_blows_teleports_and_drains_never_move_the_vitals()
    {
        var rig = TripRig(3);
        rig.ZeroesBeforeRestore = true;
        rig.TripDrainsVitals = true;
        rig.TripBlows = true;
        rig.Damage = 1;
        rig.BlowEvery = 3;
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        RunUntilStage(rig, scenario, "corpse-teleporting");
        var atTripStart = scenario.Tally.VitalsAfterArrivalPercent;
        Assert.InRange(atTripStart, 70, 80);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.True(scenario.Tally.HitsTaken > 0, "the tuskers did hit the mage on the trip");
        Assert.Equal(atTripStart, scenario.Tally.VitalsAfterArrivalPercent); // the live tally (DeathLiveTests (g) reads it)
        Assert.InRange(scenario.Tally.VitalsAfterArrivalPercent, 60, 90);
        Assert.Matches(new Regex(@" vitals_pct=7\d\.\d "), scenario.VerdictFields());
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
    }

    [Fact]
    public void M4_2_2_fix_R2_a_pick_up_that_times_out_is_partial_not_yes_and_the_regear_note_says_n_of_m()
    {
        var rig = TripRig(4);
        rig.SilentItems.Add(rig.CorpseItems[1].Guid); // the second item's pick-up is never answered: it times out, the loop goes on
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal(DeathLegVerdict.TripAbortNone, scenario.TripAbort); // no abort: the loop ran to its end
        Assert.Equal(3, scenario.ItemsRecovered);
        Assert.Equal(4, scenario.CorpseEntries);
        Assert.Equal(CorpseLoot.Partial, scenario.CorpseLooted); // the old rule said yes (end with at least one)
        Assert.Matches(new Regex(@" corpse_looted=partial items_recovered=3 trip_s=\d+\.\d trip_abort=none "), scenario.VerdictFields());
        var note = scenario.RegearCorpseNote();
        Assert.Contains("recovered 3 of 4", note, StringComparison.Ordinal);
        Assert.Contains("corpse_looted=partial", note, StringComparison.Ordinal);
        Assert.DoesNotContain("D8", note, StringComparison.Ordinal);
        Assert.Contains(Lines(rig), static l => l.Contains("items_recovered=3 of 4; not a full recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void M4_2_2_fix_R2_the_happy_path_is_yes_with_every_entry_and_no_trip_keeps_M3s_note()
    {
        var rig = TripRig(3);
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Assert.Equal(CorpseLoot.Yes, scenario.CorpseLooted);
        Assert.Equal((3, 3), (scenario.ItemsRecovered, scenario.CorpseEntries));
        Assert.Contains("recovered 3 of 3", scenario.RegearCorpseNote(), StringComparison.Ordinal);

        var off = TripRig();
        var offScenario = off.Scenario(new FakeWalk(), Trip, new DeathOptions { RecoverCorpse = false });
        Assert.Equal(ScenarioStatus.Succeeded, off.Run(offScenario));
        Assert.Equal(CorpseLoot.Skipped, offScenario.CorpseLooted);
        Assert.Contains("(D8)", offScenario.RegearCorpseNote(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_2_2_fix_R3_an_abort_with_a_pick_up_in_flight_counts_it_when_its_0022_lands_and_the_abort_is_partial()
    {
        var rig = TripRig(6);
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        var end = rig.Now + 900;
        while (rig.PickUps.Count < 2 && rig.Now < end)
        {
            Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario));
        }

        Assert.Equal(2, rig.PickUps.Count); // the second pick-up has just been sent (the first one landed); its 0022 comes 1.35 s later
        Assert.Equal(1, scenario.ItemsRecovered);
        rig.SetHealth(30); // the guard aborts the trip at the next step, with the second pick-up in flight

        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        Dump(rig, scenario);

        Assert.Equal("health-under-40", scenario.TripAbort);
        Assert.Equal(2, rig.PickUps.Count); // nothing new was sent after the abort
        Assert.Equal(2, scenario.ItemsRecovered); // the old count stopped at 1: the handle was dropped at the abandon
        Assert.Equal(CorpseLoot.Partial, scenario.CorpseLooted);
        Assert.True(rig.R.H.State.Self.Inventory.Find(rig.CorpseItems[1].Guid).Held, "the second item is in the pack");
        Assert.Contains(" corpse_looted=partial items_recovered=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("recovered 2 of 6", scenario.RegearCorpseNote(), StringComparison.Ordinal);
        Assert.Contains(Lines(rig), static l => l.Contains("it settled after the loop was abandoned", StringComparison.Ordinal));
        Assert.Contains("poi", rig.Places[^1], StringComparison.Ordinal); // placed back to Holtburg
    }

    [Fact]
    public void M4_2_2_fix_R5_a_hold_during_the_trip_does_not_suspend_the_guard_the_health_falling_under_forty_aborts_it()
    {
        var rig = TripRig(6);
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        RunUntilStage(rig, scenario, "corpse-looting");
        rig.Held = true; // the runner holds the scenario for the corpse-open frame: Step is not called, only the guard

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario));
            Assert.Null(rig.LastGuard); // the trip goes on
        }

        Assert.Equal("corpse-looting", scenario.Stage); // the held scenario took no step
        Assert.Equal(DeathLegVerdict.TripAbortNone, scenario.TripAbort);

        rig.SetHealth(30); // the health falls through 40% among the tuskers while the frame is owed
        Assert.Equal(ScenarioStatus.Running, rig.Frame(scenario));
        Assert.Equal(ScenarioStatus.Running, rig.LastGuard); // the guard fired: the abort began the way back
        Assert.Equal("health-under-40", scenario.TripAbort);
        Assert.Equal("corpse-return", scenario.Stage);
        Assert.False(scenario.CorpseOpen);

        rig.Held = false;
        Assert.Equal(ScenarioStatus.Succeeded, rig.Run(scenario));
        AssertAbortedAndBack(rig, scenario, "health-under-40");
    }

    [Fact]
    public void M4_2_2_fix_R5_the_guard_while_held_does_nothing_outside_the_trip_and_on_the_way_back()
    {
        var rig = TripRig();
        var scenario = rig.Scenario(new FakeWalk(), Trip);
        Assert.Null(scenario.GuardWhileHeld(rig.View)); // before the run: no trip
        RunUntilStage(rig, scenario, "respawn-vitals");
        Assert.Null(scenario.GuardWhileHeld(rig.View)); // the respawn's wait is not the trip
        RunUntilStage(rig, scenario, "corpse-return");
        Assert.Null(scenario.GuardWhileHeld(rig.View)); // the way back is not guarded here (Step guards a death on it)
    }

    // --- a death on the trip

    [Fact]
    public void A_death_on_the_trip_fails_the_leg_loudly_with_the_corpses_state_and_does_not_change_died_or_deaths()
    {
        var rig = TripRig();
        rig.KillOnTripAfter = 2.5;
        var scenario = rig.Scenario(new FakeWalk(), Trip);

        var status = rig.Run(scenario);
        Dump(rig, scenario);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("corpse: the mage died on the trip", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the corpse's state: 0x", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("item(s) taken", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal("died", scenario.TripAbort);
        Assert.Equal(2, scenario.Tally.VictimNotices); // the second death really came
        var fields = scenario.VerdictFields();
        Assert.Contains(" died=1 ", fields, StringComparison.Ordinal); // M3's fields were frozen at the end of the respawn's vitals
        Assert.Contains(" deaths=1 ", fields, StringComparison.Ordinal);
        Assert.Contains(" corpse_looted=no ", fields, StringComparison.Ordinal);
        Assert.Contains(" trip_abort=died ", fields, StringComparison.Ordinal);
        Assert.False(scenario.Outcome().Ok, "the failure fails the leg");
    }
}
