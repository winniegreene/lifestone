using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// Group 3 of the hunt rows over the scripted rig (M4 4.1 fix2a): the kill's corpse, its contents and the loot (M4 2.1 R5 and R10, M4 2.2 R3, R7, R9 and R15). The scripted corpse holds a Bowl (Misc, value 10) and a Smock (Clothing, value 25)
/// as the stand-in's did (placeholders: ACE fills a corpse from the creature's drop tables), opens as the live capture C2 records (the turn, 0196 and the UseDone, the contents' CreateObjects 16 ms later) and gives an item up 1.35 s after its pick-up is sent.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private static bool Holds(HuntScriptRig rig, uint wcid) => rig.Held(wcid, out _);

    [Fact]
    public void The_hunt_opens_lists_and_closes_its_kills_corpse_and_with_loot_off_takes_nothing_2_1()
    {
        using var rig = Belt12(Hunt(loot: new LootOptions { Enabled = false }));

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.CorpsesOpened);
        Assert.Equal(2, scenario.CorpseItems); // the scripted corpse's Bowl and Smock
        Assert.Equal(0, scenario.CorpsesMissed);
        Assert.Equal(0, scenario.CorpsesRefused);
        Assert.True(rig.Logged("corpse: ") && rig.Logged("holds 2 item(s)"), "the contents are logged");
        Assert.True(rig.Logged("'Bowl' wcid " + HuntScriptRig.BowlWcid) && rig.Logged("'Smock' wcid " + HuntScriptRig.SmockWcid), "each entry's name and wcid");
        Assert.True(rig.Logged("corpse: closed"), "the corpse is closed");
        Assert.Equal(0u, rig.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
        Assert.False(Holds(rig, HuntScriptRig.BowlWcid), "nothing was taken");
        Assert.Equal(0, scenario.Looted);
        Assert.Equal(0, scenario.LootSkipped);
        Assert.False(rig.Logged("loot: "), "--loot off is 2.1's hunt: no loot line at all");
        Assert.False(rig.Logged("looting"));
        Assert.EndsWith(" returned=yes", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hunt_loots_its_kills_corpse_between_the_open_and_the_close_under_the_default_rule()
    {
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        var sawLooting = false;
        var windowWhileLooting = true;

        // step through the whole run, watching the loot phase: the window (the open sent to the close settled) holds around it
        for (var t = 0.0; t < 600; t += 0.05)
        {
            var status = rig.Run(0.05);
            sawLooting |= scenario.Looting;
            windowWhileLooting &= !scenario.Looting || scenario.CorpseWindow;
            if (status != ScenarioStatus.Running)
            {
                Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure);
                break;
            }
        }

        Assert.True(sawLooting, "the loot phase ran");
        Assert.True(windowWhileLooting);
        Assert.False(scenario.CorpseWindow, "the window is closed at the end");
        Assert.Equal(1, scenario.CorpsesOpened);
        Assert.Equal(2, scenario.CorpseItems);
        Assert.Equal(2, scenario.Looted);
        Assert.Equal(0, scenario.LootSkipped);
        Assert.Equal(2, scenario.Tally.Looted);
        Assert.True(Holds(rig, HuntScriptRig.BowlWcid) && Holds(rig, HuntScriptRig.SmockWcid), "the Bowl and the Smock are in the pack");
        Assert.Equal(0u, rig.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);

        // the order of the log: listed, taken one by one, the loot done, then the close
        Assert.True(rig.LineAt("holds 2 item(s)") >= 0);
        Assert.True(rig.LineAt("holds 2 item(s)") < rig.LineAt("loot: taking 'Bowl'"));
        Assert.True(rig.LineAt("loot: taking 'Bowl'") < rig.LineAt("loot: took 'Bowl'"));
        Assert.True(rig.LineAt("loot: took 'Bowl'") < rig.LineAt("loot: taking 'Smock'"), "one at a time: the Smock goes after the Bowl is confirmed");
        Assert.True(rig.LineAt("loot: taking 'Smock'") < rig.LineAt("loot: done looted=2"));
        Assert.True(rig.LineAt("loot: done looted=2") < rig.LineAt("corpse: closed"), "the close waits for the loot");
        Assert.Contains("looted=2 loot_skipped=0", scenario.DetailFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, scenario.VerdictFields()); // the verdict fields did not change shape
    }

    [Fact]
    public void The_rule_leaves_what_it_does_not_take_and_the_tally_counts_the_skips_by_the_rule()
    {
        // the scripted Bowl is Misc 0x80 value 10, the Smock Clothing 0x4 value 25 (placeholders): a type mask of Clothing takes the Smock and leaves the Bowl by type
        using var rig = Belt12(Hunt(loot: new LootOptions { TypeMask = 0x4 }));

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Looted);
        Assert.Equal(1, scenario.LootSkipped);
        Assert.True(Holds(rig, HuntScriptRig.SmockWcid) && !Holds(rig, HuntScriptRig.BowlWcid));
        Assert.True(rig.Logged("loot: skipped 'Bowl'") && rig.Logged("(type,"), "the skip names its reason");

        // a least value of 20 leaves the Bowl (10) by value and takes the Smock (25)
        using var valued = Belt12(Hunt(loot: new LootOptions { MinValue = 20 }));
        var second = valued.Scenario;
        Assert.True(valued.Run(600) == ScenarioStatus.Succeeded, second.Failure);
        Assert.Equal(1, second.Looted);
        Assert.Equal(1, second.LootSkipped);
        Assert.True(valued.Logged("(value,"));
    }

    [Fact]
    public void A_retreat_after_the_goal_ends_the_hunt_as_done_not_failed_P4_g4()
    {
        // P4's fourth live gate: the Archer's second kill came mid-fight and the retreat check ran before the goal check, failing a hunt that had met its goal
        // (HuntScenario.TimeOrHealthEnds now ends it as done). The rig cannot stage that order (its kill ends the run first): this row pins the outcome only; the live gate proves the path.
        using var rig = Belt12(Hunt(kills: 1, openCorpse: false));
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => rig.Logged("the kill notice came (1 of 1)")), "the hunt made its one kill: " + scenario.Failure);
        rig.SetPlayerHealthLater(0.0, 4); // beaten down while the attack sequence winds down: the goal is met, the health is under the retreat line
        var status = rig.Run(120);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Null(scenario.Failure);
    }

    [Fact]
    public void The_retreat_rule_abandons_the_loot_in_progress_and_closes_the_corpse()
    {
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);

        rig.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health while the first pick-up is in flight
        var status = rig.Run(0.5);

        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), "the abandon is logged within half a second of the drop");
        Assert.False(scenario.Looting);
        Assert.False(scenario.CorpseWindow);
        Assert.True(rig.Logged("loot: done") && rig.Logged("stopped_by=abandoned"), "the loop says it was abandoned");
        status = status == ScenarioStatus.Running ? rig.Run(120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status); // never held for the loot's other item
        Assert.True(scenario.Looted < 2, "the second item was never sent");
        Assert.Equal(0u, rig.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
    }

    [Fact]
    public void M4_2_2_fix_R3_a_pick_up_in_flight_at_the_retreat_is_counted_when_it_settles_so_looted_does_not_read_low()
    {
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => rig.Sends.Any(x => x.Type == GameActionTypes.PutItemInContainer && x.First == HuntScriptRig.FirstContentGuid)), "the first pick-up reached the server: " + scenario.Failure); // the send waits for the post-teleport LoginComplete
        Assert.Equal(0, scenario.Looted);

        rig.SetPlayerHealthLater(0.05, 4); // the retreat rule fires while the first pick-up is in flight
        Assert.True(rig.RunUntil(() => rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), 2), "the abandon is logged");
        var status = rig.Status;
        Assert.True(rig.Logged("is in flight at the abandon"), "the loop kept the pick-up in flight");
        Assert.Equal(0, scenario.Looted); // not settled yet: the count so far

        // ACE's 0022 for the Bowl lands after the abandon (live, the death trip's abort left the pick-up in flight and the item came back; the scripted ACE gives nothing up from a corpse it has seen closed, so the test lands it).
        var bow = rig.Session.State.Objects.All.First(static o => o.Weenie.WeenieClassId == HuntScriptRig.BowlWcid).Guid;
        var self = rig.Session.State.Self.Guid;
        rig.Session.Reducer.OnInventoryPutObjInContainer(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(self, 901, 0x0022), new InventoryPutObjInContainerEvent(bow, self, 0, 0));
        status = status == ScenarioStatus.Running ? rig.Run(120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status);

        Assert.True(scenario.Looted == 1, "looted " + scenario.Looted + rig.Tail(25)); // the old count stopped at 0: the handle was dropped at the abandon
        Assert.Equal(1, scenario.Tally.Looted);
        Assert.True(rig.Logged("the pick-up in flight at the abandon is counted: looted=1 (1 after the abandon"), "the hunt says it counted it");
        Assert.Contains("looted=1 ", scenario.DetailFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M4_2_2_fix_R9_loot_on_with_the_viewed_container_not_the_corpse_logs_one_warning_naming_the_viewed_guid()
    {
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        var injected = false;
        const uint Other = 0x80000EEE;
        var ctx = new InboundContext(0, MessageQueue.SmartBox, 0);
        // the portal director steps the teleports, so the run is the runner's until the corpse is being opened; from there the test acts between the tick and the step
        Assert.True(rig.RunUntil(() => scenario.CorpseWindow), "the corpse sequence began: " + scenario.Failure);
        // a known object, so a 0196 for it opens it as the viewed ground container
        rig.Session.Reducer.OnCreateObject(ctx, ReducerHarness.Create(Other, ReducerHarness.Stamps(), null, name: "Chest"), isUpdate: false);
        for (var t = 0.0; t < 600 && !injected; t += 0.05)
        {
            // the open is Confirmed once the corpse's entries have resolved (OpenContainerTracker.Poll); a view of another container takes the viewed slot right then, before the scenario's step reads the confirmed open
            var status = rig.RunHooked(() =>
            {
                if (!injected && scenario.CorpseWindow && !scenario.CorpseOpen && rig.Session.State.Containers.Viewed is { } viewed && viewed.Guid != Other && viewed.IsResolved && rig.Session.State.Objects.TryGet(viewed.Guid, out var seen) && seen.Name.StartsWith("Corpse of", StringComparison.Ordinal))
                {
                    injected = true;
                    rig.Session.Reducer.OnViewContents(ctx, new GameEventHeader(rig.Session.State.Self.Guid, 900, 0x0196), new ViewContentsEvent(Other, []));
                }
            });
            Assert.True(status == ScenarioStatus.Running, status + ": " + scenario.Failure);
        }

        Assert.True(injected, "a view of another container was put in place of the corpse's");
        rig.Run(5);
        var warnings = rig.Log.Lines.Where(l => l.Contains("--loot is on but the viewed container is", StringComparison.Ordinal)).ToList();
        Assert.True(warnings.Count == 1, "the warning count was " + warnings.Count + " (injected " + injected + ")" + Environment.NewLine + string.Join(Environment.NewLine, rig.Log.Lines.Where(static l => l.Contains("corpse", StringComparison.Ordinal) || l.Contains("loot", StringComparison.Ordinal))));
        Assert.Contains("0x" + Other.ToString("X8", System.Globalization.CultureInfo.InvariantCulture), warnings[0], StringComparison.Ordinal);
        Assert.Contains("the loot did not start", warnings[0], StringComparison.Ordinal);
        Assert.False(rig.Logged("corpse: looting"), "no loop started");
        Assert.Equal(0, scenario.Looted);
    }

    [Fact]
    public void M4_2_2_fix_R7_the_corpse_looting_line_names_the_shop_mask_note_only_for_the_shop_mask()
    {
        using var rig = Belt12(Hunt());
        Assert.True(rig.Run(600) == ScenarioStatus.Succeeded, rig.Scenario.Failure);
        Assert.Contains(rig.Log.Lines, static l => l.Contains("corpse: looting ", StringComparison.Ordinal) && l.Contains("m4-loot-rules-shop-mask closed", StringComparison.Ordinal));

        using var all = Belt12(Hunt(loot: new LootOptions { TypeMask = 0x4 }));
        Assert.True(all.Run(600) == ScenarioStatus.Succeeded, all.Scenario.Failure);
        Assert.Contains(all.Log.Lines, static l => l.Contains("corpse: looting ", StringComparison.Ordinal));
        Assert.DoesNotContain(all.Log.Lines, static l => l.Contains("m4-loot-rules-shop-mask", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_corpse_option_off_the_hunt_leaves_the_corpse_alone()
    {
        using var rig = Belt12(Hunt(openCorpse: false));

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpseItems);
        Assert.False(rig.Logged("corpse: "));
        Assert.Equal(0u, rig.OpenContainerGuid);
    }

    [Fact]
    public void M4_2_2_fix2_R15_GuardWhileHeld_abandons_the_corpse_sequence_when_the_health_falls_while_the_scenario_is_held_and_does_nothing_otherwise()
    {
        // The leg holds the scenario for the corpse review frame and does not step it: the guard alone must see the retreat rule.
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => rig.Session.State.Phase == Lifestone.Net.Session.SessionPhase.InWorld, 60));
        Assert.False(scenario.GuardWhileHeld(rig.Session), "no corpse sequence yet: nothing to abandon");
        Assert.True(rig.RunUntil(() => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);

        Assert.False(scenario.GuardWhileHeld(rig.Session), "health is fine: the sequence goes on");
        Assert.True(scenario.CorpseWindow);

        rig.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health while the scenario is held (RunHeld ticks the session only: no scenario step)
        rig.RunHeld(0.5);
        Assert.True(scenario.CorpseWindow, "held, so not stepped: the sequence has not noticed");
        Assert.True(scenario.GuardWhileHeld(rig.Session), "the guard sees the retreat rule and abandons the sequence");
        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
        Assert.False(scenario.Looting);
        Assert.False(scenario.CorpseWindow);
        Assert.False(scenario.GuardWhileHeld(rig.Session), "abandoned: nothing more to guard");
        rig.RunHeld(2.0);
        Assert.Equal(0u, rig.OpenContainerGuid); // the open view was closed (fire-and-forget)
        Assert.Null(rig.Session.State.Containers.Viewed);
        var status = rig.Run(120);
        Assert.NotEqual(ScenarioStatus.Running, status); // stepped again, the hunt ends on the retreat rule
    }

    [Fact]
    public void M4_2_2_fix2_R15_GuardWhileHeld_abandons_the_corpse_sequence_when_the_hunts_seconds_pass_while_the_scenario_is_held()
    {
        using var rig = Belt12(Hunt(seconds: 300));
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => scenario.Looting), "the hunt reached the loot phase: " + scenario.Failure);
        Assert.False(scenario.GuardWhileHeld(rig.Session));

        rig.RunHeld(301); // the clock passes the hunt's 300 s with the scenario held
        Assert.True(scenario.GuardWhileHeld(rig.Session), "the hunt's seconds are checked while held");
        Assert.False(scenario.CorpseWindow);
        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
    }

    [Fact]
    public void Health_under_the_retreat_share_right_after_the_kill_abandons_the_corpse_within_a_step_and_it_is_not_opened_2_1_R5()
    {
        using var rig = Belt12(Hunt());
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => scenario.Tally.Kills >= 1), scenario.Failure);

        rig.SetPlayerHealthLater(0.05, 4); // the hunter drops to a few points of health a tick after the kill, before the corpse comes
        var status = rig.Run(0.5);

        Assert.True(rig.Logged("corpse: the retreat rule or the hunt's time came; the corpse sequence is abandoned"), "the abandon is logged within half a second of the drop");
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0u, rig.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
        status = status == ScenarioStatus.Running ? rig.Run(120) : status;
        Assert.NotEqual(ScenarioStatus.Running, status); // never held for the corpse's 5 s + 20 s + 5 s
        Assert.Equal(0, scenario.CorpsesOpened);
    }

    [Fact]
    public void An_open_abandoned_in_flight_is_closed_when_it_lands_Confirmed_2_1_R10()
    {
        using var rig = Belt12(Hunt());
        rig.CorpseOpenDelay = 2.0; // the corpse is found about 0.6 s after the kill; its 0196 comes 2 s after the open was sent
        var scenario = rig.Scenario;
        Assert.True(rig.RunUntil(() => rig.CorpseOpensSent >= 1), scenario.Failure);

        rig.SetPlayerHealthLater(0.6, 4); // the drop lands while the open is in flight (answered 2 s after it reached the server)
        for (var t = 0.0; t < 8; t += 0.05)
        {
            rig.RunHooked(static () => { }); // the scenario is stepped on after the run ended (the wind-down is shorter than the open's delay): its first act is to poll the abandoned open
        }

        Assert.True(rig.Logged("the retreat rule or the hunt's time came; the corpse sequence is abandoned"));
        Assert.True(rig.Logged("the abandoned open of"), "the open landed Confirmed after the abandon and the scenario noticed" + rig.Tail(25));
        Assert.Equal(0u, rig.OpenContainerGuid);
        Assert.Null(rig.Session.State.Containers.Viewed);
    }

    [Fact]
    public void A_corpse_that_never_appears_is_counted_as_missed_within_5_seconds_and_the_hunt_succeeds_2_1_R5()
    {
        using var rig = Belt12(Hunt());
        rig.CorpseAppears = false;

        var status = rig.Run(600);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.CorpsesMissed);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpsesRefused);
        Assert.True(rig.Logged("counted, not a failure"));
    }

    [Fact]
    public void An_open_answered_by_a_UseDone_with_no_0196_is_counted_as_refused_and_the_hunt_succeeds_2_1_R5()
    {
        using var rig = Belt12(Hunt());
        rig.OpenAnsweredByUseDoneOnly = true;

        var status = rig.Run(900);

        var scenario = rig.Scenario;
        Assert.True(status == ScenarioStatus.Succeeded, scenario.Failure + rig.Tail());
        Assert.Equal(1, scenario.CorpsesRefused);
        Assert.Equal(0, scenario.CorpsesOpened);
        Assert.Equal(0, scenario.CorpsesMissed);
        Assert.True(rig.Logged("was not confirmed"));
    }
}
