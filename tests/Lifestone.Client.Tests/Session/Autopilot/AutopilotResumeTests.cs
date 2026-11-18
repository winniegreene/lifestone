using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 item 4 a2 (D57): the autopilot's checkpoint, watchdogs and reconnect over the scripted ACE on both dungeon profiles (the den's Holtburg, the Tusker Temple's Fort Tethana), with the shared rig <see cref="AutopilotTests"/>' rig
/// (the logistics station's scripted world and scripted stations that move the player as theirs do; a kill every two seconds). Proved: a run abandoned mid-cycle (a kill of the process) and started again from its checkpoint resumes the totals,
/// the cycle count and the stop rule's budget and classifies its state from the world, never from the file; a torn, unreadable, foreign or newer-schema file is named and set aside and the run starts fresh; a run that ended by its stop rule is
/// not resumed, one that ended by a failure is; a dropped session (the state's phase leaves the world, as a failure, a boot or the silence watchdog does) in any state reconnects through the host's
/// <see cref="IAutopilotSession"/> with growing waits, counts <c>reconnects=</c>, classifies the world again and goes on; the retry's last failure stops by name; a state over its watchdog bound is recovered by the recall home, and the third trip in a
/// cycle stops by name. The offline <c>GameSession.Reconnect</c> (which re-runs the stand-in) is the host's (slice h1) and is not driven here: the double drops and restores the phase on the same scripted state.
/// </summary>
public sealed class AutopilotResumeTests(ITestOutputHelper output)
{

    /// <summary>The host's double: <see cref="Reconnect"/> resets the scripted session's phase to idle, <see cref="StepLogin"/> walks it back into the world after a few steps, or fails while <see cref="FailTries"/> tries remain.</summary>
    private sealed class Host(ApWorld w) : IAutopilotSession
    {
        private int _steps;

        public int FailTries { get; set; }

        public int Reconnects { get; private set; }

        public string? LoginFailure { get; private set; }

        /// <summary>The player's guid when the reset had been made (0: the reducer forgot the character, as <c>GameSession.Reconnect</c> makes it; M5 4 F1, 4-R14).</summary>
        public uint GuidAfterReset { get; private set; } = uint.MaxValue;

        public void Reconnect()
        {
            Reconnects++;
            _steps = 0;
            LoginFailure = null;
            w.LeaveWorld(); // the reducer's own Reset: the world is left, the phase idle (M5 4 F1, 4-R14)
            GuidAfterReset = w.Trip.Vendor.State.Self.Guid;
        }

        public ScenarioStatus StepLogin(double now)
        {
            if (Reconnects <= FailTries)
            {
                LoginFailure = "scripted: the server refused the login";
                return ScenarioStatus.Failed;
            }

            if (++_steps < 4)
            {
                return ScenarioStatus.Running;
            }

            w.Trip.Vendor.H.Phase(SessionPhase.Idle, SessionPhase.InWorld);
            w.Relogin(); // the login delivers the player, its description and its pack again
            return ScenarioStatus.Succeeded;
        }
    }

    private static bool InFight(AutopilotScenario a, int kills) => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight f && f.Kills >= kills;

    private static AutopilotCheckpoint Stored(MemoryCheckpointStore store) =>
        AutopilotCheckpoint.Parse(store.Json!, "BSD Mage", out var why) ?? throw new InvalidOperationException("the stored checkpoint does not parse: " + why);

    // ------------------------------------------------------------------------------------------------ the checkpoint

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_run_abandoned_mid_cycle_resumes_its_totals_and_cycle_from_the_checkpoint_and_classifies_the_world(string key)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output);
        var first = w.Autopilot(new AutopilotOptions { TestDeath = true, Minutes = 10 }, store);
        w.Run(first, a => a.Cycles == 1 && InFight(a, 3)); // the scripted death after kill 1 ended cycle 1; cycle 2 has made three kills
        Assert.Equal((1, 1), (first.Cycles, first.Totals.Deaths));
        var mark = w.Now;
        w.Run(first, _ => w.Now - mark >= AutopilotScenario.CheckpointRefreshSeconds + 5); // the refresh (every 60 s) saves the fight's kills so far

        // a kill of the process: the first autopilot is neither stepped nor disposed again. The file holds what the last write knew.
        var saved = Stored(store);
        Assert.Equal((AutopilotCheckpoint.CurrentSchema, "BSD Mage", 2, 1), (saved.Schema, saved.Character, saved.Cycle, saved.Cycles));
        Assert.Equal((1, 1), (saved.Totals.Kills, saved.Totals.Deaths)); // the ended cycle's counts are in the totals
        Assert.True(saved.CycleTally.Kills >= 20, "the refresh saved the fight's kills so far: " + saved.CycleTally.Kills);
        Assert.False(saved.Completed);
        Assert.Equal("fight", saved.State);
        var used = saved.MinutesUsed;
        Assert.True(used > 1, "minutes used " + used);

        // the world has changed since (the file says fight; the mage stands at home): the state is classified from the world
        w.Trip.Vendor.StandAt(w.Trip.Ground.Home.LifestoneCell, w.Trip.Ground.Home.LifestonePoint);
        var made = w.Factory.Made.Count;
        var second = w.Autopilot(new AutopilotOptions { Minutes = 10 }, store);
        w.Run(second, a => InFight(a, 2));
        second.RequestStop("test");
        var status = w.Run(second);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(["buff", "den-travel", "den-fight", "recall-home"], Kinds(w, made)); // M6 item 14: the rebuilt run's audit does not bind again: the first run's bind is proven in the checkpoint by the character's guid (M5 7 bound at every start: a new journey context) // from home, not from the file's fight; the last is the recall home before the stop (M5 7 F18)
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: proof carried from the checkpoint of character 0x", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: proven by the proof carried from the checkpoint of character 0x", StringComparison.Ordinal) && l.Contains("fix=none: no walk to the lifestone", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("checkpoint: read: resuming cycle 2 (1 ended)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("known-state place=home", StringComparison.Ordinal));
        var stopped = second.Lines[^2];
        var done = second.Lines[^1];
        Assert.StartsWith("autopilot cycle n=2 state=stopped ", stopped, StringComparison.Ordinal);
        Assert.StartsWith("autopilot done ok=yes cycles=1 stop=request:test ", done, StringComparison.Ordinal);
        Assert.Equal(1, Int(done, "deaths")); // the first run's death is in the second run's totals
        Assert.Equal(saved.Totals.Kills + Int(stopped, "kills"), Int(done, "kills")); // the totals of the ended cycles plus the stopped cycle (the saved kills and the new fight's)
        Assert.True(Int(stopped, "kills") >= saved.CycleTally.Kills + 2, stopped); // the saved kills of the cycle in progress plus the new fight's
        Assert.True(Stored(store).Completed);
        Assert.Equal("request:test", Stored(store).Stop);
    }

    [Fact]
    public void A_hand_back_store_lets_the_tie_memory_read_the_file_and_still_reads_the_checkpoint_as_empty_once_M6_6()
    {
        // M6 6 live round 2: since M6 4 the tie memory is read before the checkpoint; through Read it used up the hand-back's empty read, so the checkpoint written at the /stop was added on top of the carry (interrupted=2, deaths and kills doubled)
        var inner = new MemoryCheckpointStore { Json = "{}" };
        var cold = new ColdReadOnceStore(inner);
        Assert.Equal("{}", cold.ReadForTies());
        Assert.Null(cold.Read());
        Assert.Equal("{}", cold.Read());
        Assert.Equal("{}", ((IAutopilotCheckpointStore)inner).ReadForTies()); // every other store: the tie read is Read
    }

    [Fact]
    public void A_hand_back_scenario_over_the_interrupt_checkpoint_counts_nothing_from_the_file_M6_6()
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var first = w.Autopilot(new AutopilotOptions { TestDeath = true, Minutes = 10 }, store);
        w.Run(first, a => a.Cycles == 1 && InFight(a, 3));
        var mark = w.Now;
        w.Run(first, _ => w.Now - mark >= AutopilotScenario.CheckpointRefreshSeconds + 5);
        Assert.Equal(1, Stored(store).Totals.Deaths); // the file holds the ended cycle's death

        var seen = w.Lines.Count();
        var second = w.Autopilot(new AutopilotOptions { Minutes = 10 }, new ColdReadOnceStore(store)); // the hand-back's store (AutopilotHost's factory); the carry would bring the counts
        w.Run(second, a => InFight(a, 1));
        Assert.Equal(0, second.Totals.Deaths); // nothing counted from the file: the carry is the only source
        Assert.DoesNotContain(w.Lines.Skip(seen), l => l.Contains("checkpoint: read: resuming", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_stop_rules_budget_carries_over_a_restart(string key)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output, atHome: false);
        var start = w.Now;
        var first = w.Autopilot(new AutopilotOptions { Minutes = 3 }, store);
        w.Run(first, _ => w.Now - start >= 90); // a minute and a half of a three-minute budget, then the process is killed
        var used = Stored(store).MinutesUsed * 60;
        Assert.InRange(used, 60, 90); // the refresh every 60 s saved the time used so far

        var restart = w.Now;
        var second = w.Autopilot(new AutopilotOptions { Minutes = 3 }, store);
        var status = w.Run(second);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("minutes", second.StopName);
        Assert.InRange(w.Now - restart, 180 - used - 2, 180 - used + 3); // only what is left of the budget
        Assert.Contains(w.Lines, l => l.Contains("checkpoint: read: resuming cycle 1 (0 ended)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den, "torn")]
    [InlineData(Den, "foreign")]
    [InlineData(Den, "newer")]
    [InlineData(Den, "unreadable")]
    [InlineData(Temple, "torn")]
    [InlineData(Temple, "foreign")]
    [InlineData(Temple, "newer")]
    [InlineData(Temple, "unreadable")]
    public void A_torn_unreadable_foreign_or_newer_file_is_named_set_aside_and_the_run_starts_fresh(string key, string fault)
    {
        var good = new AutopilotCheckpoint { Character = "BSD Mage", Ground = key, State = "fight", Cycle = 7, Cycles = 6, Totals = new CheckpointTally { Kills = 500 }, MinutesUsed = 100 }.ToJson();
        var (text, why) = fault switch
        {
            "torn" => (good[..(good.Length / 2)], "torn"),
            "foreign" => (good.Replace("BSD Mage", "Someone Else", StringComparison.Ordinal), "foreign character"),
            "newer" => (good.Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal), "newer schema 2"),
            _ => ("[1, 2, 3]", "unreadable"),
        };
        var store = new MemoryCheckpointStore { Json = text };
        var w = new ApWorld(key, output);
        var autopilot = w.Autopilot(new AutopilotOptions { Minutes = 10 }, store);
        w.Run(autopilot, a => InFight(a, 1));
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal([text], store.Aside); // moved aside, never deleted
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: set aside: " + why, StringComparison.Ordinal));
        Assert.True(autopilot.Totals.Kills < 20, "the file's 500 kills were not taken: " + autopilot.Totals.Kills);
        Assert.DoesNotContain(w.Lines, l => l.Contains("checkpoint: read:", StringComparison.Ordinal));
        var fresh = Stored(store);
        Assert.Equal((1, 0), (fresh.Cycle, fresh.Cycles)); // the run started at cycle 1 and wrote its own file
        Assert.True(fresh.Completed);
    }

    [Fact]
    public void A_run_that_stopped_by_its_rule_is_not_resumed_but_one_that_failed_is()
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output, atHome: false);
        var first = w.Autopilot(new AutopilotOptions { Minutes = 1 }, store);
        w.Run(first);
        Assert.Equal("minutes", first.StopName);
        Assert.True(Stored(store).Completed);

        var second = w.Autopilot(new AutopilotOptions { Minutes = 1 }, store);
        w.Run(second, a => InFight(a, 1));
        Assert.Contains(w.Lines, l => l.Contains("checkpoint: the previous run finished (stop=minutes): starting fresh", StringComparison.Ordinal));
        Assert.Empty(store.Aside);
        Assert.Equal(1, second.Cycle);
        Assert.False(Stored(store).Completed); // the new run's own file
    }

    [Fact]
    public void A_scripted_run_sets_an_earlier_runs_file_aside_and_gets_its_whole_budget_with_both_cycles_but_a_plain_run_resumes_it_M6_7_D()
    {
        // M6 4 round 4 (m6-gate-autopilot-cycles): the gate's rerun kept the character, so round 3's failed file was resumed: 17.4 of the 30 minutes used, and the minutes ran out before the forced trip (cycles=1)
        var options = new AutopilotOptions { TestDeath = true, TripAfterKills = 5, Minutes = 8 };
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var start = w.Now;
        var abandoned = w.Autopilot(options, store);
        w.Run(abandoned, _ => w.Now - start >= 7 * 60); // most of the budget, then the run is gone
        var earlier = Stored(store);
        Assert.False(earlier.Completed);
        Assert.True(earlier.MinutesUsed > 6, "minutes used " + earlier.MinutesUsed);
        var text = store.Json!;

        // a plain run resumes it: the deliberate rule of a failed or killed run
        var plain = w.Autopilot(new AutopilotOptions { Minutes = 8 }, store);
        w.Run(plain, a => InFight(a, 1));
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: read: resuming cycle", StringComparison.Ordinal));
        Assert.Empty(store.Aside);
        plain.Dispose();
        store.Json = text;

        var seen = w.Lines.Count();
        var restart = w.Now;
        var scripted = w.Autopilot(options, store);
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(scripted));

        Assert.Single(store.Aside);
        Assert.Contains(w.Lines.Skip(seen), l => l.StartsWith("checkpoint: set aside: a scripted run (--autopilot-test-death) starts whole, and this file is an earlier run's (cycle ", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines.Skip(seen), l => l.Contains("checkpoint: read:", StringComparison.Ordinal));
        Assert.Equal("minutes", scripted.StopName);
        Assert.True(w.Now - restart >= (8 * 60) - 1, "the whole budget");
        Assert.Equal(2, scripted.Cycles); // the scripted death's and the forced trip's
        Assert.Equal((1, 1), (scripted.Totals.Deaths, scripted.Totals.Trips)); // and nothing of the earlier run's
    }

    [Fact]
    public void The_checkpoint_round_trips_the_scrolls_bought_the_spells_learned_and_the_skills_trained_M6_1_fix_1_R11()
    {
        var tally = new AutopilotTally { Kills = 12, SpentXp = 4_000, ScrollsBought = 3, Learned = 2 };
        var written = new AutopilotCheckpoint { Character = "Winnie", Ground = "holtburg-outskirts", State = "fight", Cycle = 4, Cycles = 3, Totals = CheckpointTally.From(tally), CycleTally = CheckpointTally.From(new AutopilotTally { ScrollsBought = 1 }), Trained = [MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment] };

        var read = AutopilotCheckpoint.Parse(written.ToJson(), "Winnie", out var why) ?? throw new InvalidOperationException("the checkpoint does not parse: " + why);

        var back = read.Totals.ToTally();
        Assert.Equal((12, 4_000L, 3, 2), (back.Kills, back.SpentXp, back.ScrollsBought, back.Learned));
        Assert.Equal(1, read.CycleTally.ToTally().ScrollsBought);
        Assert.Equal([MageTemplate.CreatureEnchantment, MageTemplate.ItemEnchantment], read.Trained);
        Assert.Null(AutopilotCheckpoint.Parse(new AutopilotCheckpoint { Character = "Winnie" }.ToJson(), "Winnie", out _)!.Trained); // a file written before the field reads as none
    }

    [Fact]
    public void The_checkpoint_round_trips_the_retreats_and_the_last_kill_corpses_and_an_old_file_loads_without_them_M6_6_F3()
    {
        var written = new AutopilotCheckpoint
        {
            Character = "Winnie",
            Totals = CheckpointTally.From(new AutopilotTally { Kills = 3, Retreats = 2 }),
            CycleTally = CheckpointTally.From(new AutopilotTally { Retreats = 1 }),
            Corpses = [new CheckpointCorpse { Guid = 0x80001101, Name = "Tusker Slave" }],
        };

        var read = AutopilotCheckpoint.Parse(written.ToJson(), "Winnie", out var why) ?? throw new InvalidOperationException("the checkpoint does not parse: " + why);

        Assert.Equal(2, read.Totals.ToTally().Retreats);
        Assert.Equal(1, read.CycleTally.ToTally().Retreats);
        var corpse = Assert.Single(read.Corpses!);
        Assert.Equal((0x80001101u, "Tusker Slave"), (corpse.Guid, corpse.Name));
        var old = AutopilotCheckpoint.Parse("{ \"schema\": 1, \"character\": \"Winnie\", \"totals\": { \"kills\": 4 } }", "Winnie", out _)!;
        Assert.Null(old.Corpses);
        Assert.Equal((4, 0), (old.Totals.ToTally().Kills, old.Totals.ToTally().Retreats));
    }

    [Fact]
    public void A_resumed_run_carries_the_retreats_total_and_writes_it_back_M6_6_F3()
    {
        var store = new MemoryCheckpointStore
        {
            Json = new AutopilotCheckpoint { Character = "BSD Mage", Ground = Den, State = "fight", Cycle = 2, Cycles = 1, Totals = new CheckpointTally { Kills = 5, Retreats = 2 }, MinutesUsed = 1 }.ToJson(),
        };
        var w = new ApWorld(Den, output);
        var autopilot = w.Autopilot(new AutopilotOptions { Minutes = 10 }, store);
        w.Run(autopilot, a => InFight(a, 1));
        autopilot.RequestStop("test");
        w.Run(autopilot);

        Assert.Equal(2, autopilot.Retreats);
        Assert.Equal(2, Stored(store).Totals.Retreats);
        Assert.Equal(2, Int(autopilot.Lines[^1], "retreats"));
    }

    [Fact]
    public void The_checkpoint_is_written_on_every_state_change_and_on_exit_with_its_fields_and_a_failed_write_is_named()
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var autopilot = w.Autopilot(new AutopilotOptions(), store);
        var arrows = new List<AutopilotTransition>();
        autopilot.StateChanged = arrows.Add;
        w.Run(autopilot, a => InFight(a, 2));
        Assert.Equal(6, arrows.Count); // known-state, the start-up audit's bind (travel) and known-state again (M5 7), buff, travel, fight
        Assert.Equal(arrows.Count, w.Lines.Count(l => l.StartsWith("checkpoint: written", StringComparison.Ordinal)));
        Assert.Equal(arrows.Count, store.Writes);

        using var json = JsonDocument.Parse(store.Json!);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("fight", root.GetProperty("state").GetString());
        Assert.Equal(1, root.GetProperty("cycle").GetInt32());
        Assert.Equal(0, root.GetProperty("cycles").GetInt32());
        Assert.True(root.GetProperty("totals").TryGetProperty("kills", out _));
        Assert.True(root.GetProperty("minutesUsed").GetDouble() >= 0);
        Assert.Contains("in the ground", root.GetProperty("lastDecision").GetString(), StringComparison.Ordinal);

        store.FailWrites = true; // a disk that cannot be written: named, the run goes on
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: write failed (memory)", StringComparison.Ordinal));
        Assert.Equal(AutopilotState.Stopped, autopilot.State);

        // on exit without a stop: the process ends the autopilot, the file gets one last write
        var store2 = new MemoryCheckpointStore();
        var w2 = new ApWorld(Den, output);
        var other = w2.Autopilot(new AutopilotOptions(), store2);
        w2.Run(other, a => InFight(a, 1));
        var writes = store2.Writes;
        other.Dispose();
        Assert.Equal(writes + 1, store2.Writes);
        Assert.StartsWith("exit:", Stored(store2).LastDecision, StringComparison.Ordinal);
        Assert.False(Stored(store2).Completed);
    }

    // ------------------------------------------------------------------------------------------------ the reconnect

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_dropped_session_reconnects_with_the_first_wait_counts_one_and_the_run_goes_on(string key)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output, atHome: false);
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), store, host);
        w.Run(autopilot, a => InFight(a, 3));
        w.Drop();
        var dropped = w.Now;
        w.Run(autopilot, a => a.CycleTally.Reconnects == 1 && a.State == AutopilotState.Fight && w.Phase == SessionPhase.InWorld && a.CurrentRun is not null);
        var reconnected = w.Now - dropped;
        w.Run(autopilot, a => InFight(a, 2));
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1, host.Reconnects);
        Assert.InRange(reconnected, 5, 9); // the first wait (5 s) and the scripted login's few steps
        Assert.Equal(["buff", "den-fight", "buff", "den-fight", "recall-home"], Kinds(w)); // the state is classified from the world again: in the ground with no enchantment up
        var order = w.Lines.Where(l => l.StartsWith("reconnect:", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, order.Count);
        Assert.Contains("try 1 of 5 in 5 s", order[0], StringComparison.Ordinal);
        Assert.Contains("try 1 of 5 after 5 s", order[1], StringComparison.Ordinal);
        Assert.Contains("try 1 of 5 ok", order[2], StringComparison.Ordinal);
        var stopped = autopilot.Lines[^2];
        var done = autopilot.Lines[^1];
        Assert.Equal(1, Int(stopped, "reconnects"));
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:test ", done, StringComparison.Ordinal);
        Assert.Equal(1, Int(done, "reconnects"));
        Assert.True(Int(done, "kills") >= 3 + 2, done); // the first fight's kills before the drop are counted
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: written", StringComparison.Ordinal));
        Assert.Equal(1, Stored(store).Totals.Reconnects);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_drop_in_any_state_reconnects_and_a_failed_try_waits_longer_before_the_next(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.Wedge = spec => spec.Kind == StationKinds.Buff && w.Factory.Made.Count(s => s.Kind == StationKinds.Buff) == 1; // the first buff never ends
        var host = new Host(w) { FailTries = 1 };
        var autopilot = w.Autopilot(new AutopilotOptions(), null, host);
        w.Run(autopilot, a => a.State == AutopilotState.Buff && a.CurrentRun is not null);
        w.Drop(); // dropped mid-buff
        var dropped = w.Now;
        w.Run(autopilot, a => InFight(a, 1));
        var took = w.Now - dropped;
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, host.Reconnects); // the first try failed, the second (after the next, longer wait) got in
        Assert.InRange(took, 5 + 15, 5 + 15 + 12);
        Assert.Contains(w.Lines, l => l.Contains("reconnect: try 1 of 5 failed: scripted: the server refused the login", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("reconnect: try 2 of 5 in 15 s", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("reconnect: try 2 of 5 ok", StringComparison.Ordinal));
        Assert.Equal(1, Int(autopilot.Lines[^1], "reconnects")); // the successes, not the tries
        Assert.Equal(["bind", "buff", "buff", "den-travel", "den-fight", "recall-home"], Kinds(w)); // M5 7: the start's audit binds; the reconnect's audit finds the bind proven // the buff in progress was dropped; the world (home) classified again
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_last_failed_try_stops_by_name_and_a_restart_resumes_what_the_failed_run_counted(string key)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output, atHome: false);
        var host = new Host(w) { FailTries = int.MaxValue };
        var options = new AutopilotOptions { ReconnectWaits = [1, 2, 3] };
        var autopilot = w.Autopilot(options, store, host);
        w.Run(autopilot, a => InFight(a, 2));
        w.Drop();
        var dropped = w.Now;
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(AutopilotScenario.ReconnectFailedStop, autopilot.StopName);
        Assert.False(autopilot.Ok);
        Assert.Equal(3, host.Reconnects);
        Assert.InRange(w.Now - dropped, 1 + 2 + 3, 1 + 2 + 3 + 3);
        for (var n = 1; n <= 3; n++)
        {
            Assert.Contains(w.Lines, l => l.Contains($"reconnect: try {n} of 3 failed", StringComparison.Ordinal));
        }

        var done = autopilot.Lines[^1];
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=reconnect-failed ", done, StringComparison.Ordinal);
        Assert.Equal(0, Int(done, "reconnects"));
        Assert.True(Int(done, "kills") >= 2);
        Assert.Contains("reconnect-failed", autopilot.Failure, StringComparison.Ordinal);
        var saved = Stored(store);
        Assert.Equal(("reconnect-failed", false), (saved.Stop, saved.Completed)); // a failure stop is resumable

        w.Trip.Vendor.H.Phase(w.Phase, SessionPhase.InWorld); // the person fixed the link and started again (the failed tries had left the world: the login delivers the character again)
        w.Relogin();
        var second = w.Autopilot(new AutopilotOptions(), store);
        w.Run(second, a => InFight(a, 1));
        Assert.Contains(w.Lines, l => l.Contains("checkpoint: read: resuming cycle 1 (0 ended)", StringComparison.Ordinal) && l.Contains("stop=reconnect-failed", StringComparison.Ordinal));
        Assert.True(second.Totals.Kills >= 2, "the failed run's kills resumed: " + second.Totals.Kills);
    }

    [Fact]
    public void A_session_dropped_with_no_host_or_closed_by_the_person_stops_by_name()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var none = w.Autopilot(new AutopilotOptions());
        w.Run(none, a => InFight(a, 1));
        w.Drop();
        Assert.Equal(ScenarioStatus.Failed, w.Run(none));
        Assert.Equal(AutopilotScenario.NoReconnectStop, none.StopName);

        var w2 = new ApWorld(Den, output, atHome: false);
        var host = new Host(w2);
        var closed = w2.Autopilot(new AutopilotOptions(), null, host);
        w2.Run(closed, a => InFight(a, 1));
        w2.Trip.Vendor.H.Phase(w2.Phase, SessionPhase.LoggingOff); // the person's log off is not a drop
        Assert.Equal(ScenarioStatus.Succeeded, w2.Run(closed));
        Assert.Equal(AutopilotScenario.SessionClosedStop, closed.StopName);
        Assert.Equal(0, host.Reconnects);
        Assert.True(closed.Ok);
    }

    [Fact]
    public void The_default_waits_outlast_aces_sixty_second_drop_and_the_options_parse_and_are_registered()
    {
        Assert.Equal(new[] { 5.0, 15, 30, 60, 120 }, AutopilotOptions.DefaultReconnectWaits.ToArray());
        Assert.True(AutopilotOptions.DefaultReconnectWaits.Sum() > 60);
        Assert.True(AutopilotOptions.DefaultReconnectWaits.Take(3).Sum() > 45); // three tries in, the old session is nearly gone; the fourth lands after it
        Assert.Equal(20.0, SessionTimeouts.Default.Silence.TotalSeconds); // m5-reconnect-auto: the 20 s watchdog is kept

        var o = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-watchdog-scale=0.5", "--autopilot-watchdog-recoveries=4", "--autopilot-reconnect-waits=1, 2.5,3"]));
        Assert.Empty(o.Errors);
        Assert.Equal((0.5, 4), (o.WatchdogScale, o.WatchdogRecoveries));
        Assert.Equal(new[] { 1.0, 2.5, 3 }, o.ReconnectWaits.ToArray());
        Assert.Contains("watchdog_scale=0.5 watchdog_recoveries=4 reconnect_waits=1,2.5,3", o.Resilience, StringComparison.Ordinal);
        var bad = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-watchdog-scale=0", "--autopilot-watchdog-recoveries=21", "--autopilot-reconnect-waits=5,x", "--autopilot-reconnect-waits=-1"]));
        Assert.Equal(4, bad.Errors.Count);
        Assert.Equal(AutopilotOptions.DefaultReconnectWaits.ToArray(), new AutopilotOptions().ReconnectWaits.ToArray());
        Assert.Equal(AutopilotWatchdogs.DefaultMaxRecoveries, new AutopilotOptions().WatchdogRecoveries);

        var help = OptionCatalog.HelpText();
        foreach (var name in new[] { "--autopilot-watchdog-scale", "--autopilot-watchdog-recoveries", "--autopilot-reconnect-waits" })
        {
            Assert.Contains(name, help, StringComparison.Ordinal);
        }
    }

    // ------------------------------------------------------------------------------------------------ the watchdogs

    [Fact]
    public void A_climbers_buff_state_is_bound_past_the_passs_named_limit_so_the_watchdog_never_cuts_a_whole_pass()
    {
        // M6 7 AO: a climber's pass waits for mana up to 900 s and ends done (M6 7 AN); the state's 300 s would cut her first whole pass, about 650 mana on a pool of 165
        var limit = LadderLoader.WarMage.Vitals.BuffPassWaitSeconds;
        var dogs = new AutopilotWatchdogs(passBaseSeconds: AutopilotWatchdogs.ClimberPassSeconds(LadderLoader.WarMage.Vitals));
        Assert.Equal(limit + AutopilotWatchdogs.BaseSeconds(AutopilotState.Buff), dogs.BoundSeconds(AutopilotState.Buff));
        Assert.Equal(limit + AutopilotWatchdogs.BaseSeconds(AutopilotState.Buff), dogs.BoundSeconds(AutopilotState.Upkeep));
        Assert.Equal(AutopilotWatchdogs.BaseSeconds(AutopilotState.Trip), dogs.BoundSeconds(AutopilotState.Trip));
        dogs.Enter(AutopilotState.Buff, 0);
        Assert.Null(dogs.Over(limit + 1));
        Assert.Equal(AutopilotWatchdogs.BaseSeconds(AutopilotState.Buff), new AutopilotWatchdogs().BoundSeconds(AutopilotState.Buff)); // the den's job keeps its bound
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutopilotWatchdogs(passBaseSeconds: -1));
    }

    [Fact]
    public void The_watchdogs_scale_their_bounds_restart_on_progress_and_allow_a_bounded_number_of_recoveries()
    {
        var dogs = new AutopilotWatchdogs(scale: 0.5, maxRecoveries: 2);
        foreach (var state in new[] { AutopilotState.KnownState, AutopilotState.Buff, AutopilotState.Travel, AutopilotState.Fight, AutopilotState.Upkeep, AutopilotState.Trip, AutopilotState.Death })
        {
            Assert.True(AutopilotWatchdogs.BaseSeconds(state) > 0, state.ToString());
            Assert.Equal(AutopilotWatchdogs.BaseSeconds(state) * 0.5, dogs.BoundSeconds(state), 6);
        }

        Assert.Equal(0, AutopilotWatchdogs.BaseSeconds(AutopilotState.Stopped));
        dogs.Enter(AutopilotState.Fight, 100);
        Assert.Null(dogs.Over(100 + 450)); // the fight's bound is 450 s here, and "over" is strictly over
        Assert.NotNull(dogs.Over(100 + 451));
        dogs.Progress(551);
        Assert.Null(dogs.Over(551 + 450));
        Assert.NotNull(dogs.Over(551 + 451));
        Assert.Equal("watchdog: fight over 91 s", AutopilotWatchdogs.Line(AutopilotState.Fight, 91.2));
        dogs.Enter(AutopilotState.Stopped, 0);
        Assert.Null(dogs.Over(1e9)); // a stopped state has no bound
        Assert.True(dogs.TryRecover());
        Assert.True(dogs.TryRecover());
        Assert.False(dogs.TryRecover());
        dogs.NewCycle();
        Assert.True(dogs.TryRecover());
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutopilotWatchdogs(scale: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutopilotWatchdogs(maxRecoveries: -1));
    }

    [Theory]
    [InlineData(Den, "buff")]
    [InlineData(Temple, "buff")]
    [InlineData(Den, "den-fight")]
    [InlineData(Temple, "den-fight")]
    public void A_state_over_its_bound_is_named_and_recovered_by_the_recall_home_and_the_known_state(string key, string wedged)
    {
        var w = new ApWorld(key, output, atHome: wedged == "buff");
        w.Factory.Wedge = spec => spec.Name == wedged && w.Factory.Made.Count(s => s.Name == wedged) == 1; // the first one never ends
        var autopilot = w.Autopilot(new AutopilotOptions { WatchdogScale = 0.05 }); // buff 15 s, fight 45 s without a kill
        var start = w.Now;
        w.Run(autopilot, a => a.CurrentRun is IStoppableFight { Kills: >= 2 } && w.Factory.Made.Count > 2);
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var bound = wedged == "buff" ? 15 : AutopilotWatchdogs.FightBaseSeconds(w.Ground) * 0.05; // the fight's bound is read from the ground's respawn schedule (M5 4 F1, 4-R7), scaled
        var tripLine = w.Lines.First(l => l.StartsWith($"watchdog: {(wedged == "buff" ? "buff" : "fight")} over ", StringComparison.Ordinal));
        var held = double.Parse(tripLine.Split(' ')[3], CultureInfo.InvariantCulture);
        Assert.InRange(held, Math.Round(bound), Math.Round(bound) + 1); // the held seconds, whole, just past the bound
        Assert.Contains(w.Lines, l => l.Contains("watchdog recovery after the", StringComparison.Ordinal));
        Assert.Contains("recall-home", Kinds(w));
        Assert.Equal("den-fight", Kinds(w)[^2]); // the last is the recall home before the stop (M5 7 F18) // after the recall home and the known state the run is fighting again
        Assert.True(w.Now - start > bound);
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:test ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_third_trip_in_a_cycle_stops_by_name_and_the_recoveries_are_counted_per_cycle(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.Wedge = spec => spec.Kind == StationKinds.Buff; // every buff never ends
        var store = new MemoryCheckpointStore();
        var autopilot = w.Autopilot(new AutopilotOptions { WatchdogScale = 0.05 }, store);
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("watchdog:buff", autopilot.StopName);
        Assert.Equal(key == Den ? ["bind", "buff", "buff", "buff"] : ["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(w)); // M5 7: the start-up audit binds at home first // two recoveries, then the stop; M6 7 BC: at the travel file's home (the Den's Holtburg) a recovery runs no recall home, the known state classifies; the Temple's own home town is a place elsewhere for the rule
        Assert.Equal(3, w.Lines.Count(l => l.StartsWith("watchdog: buff over 15 s", StringComparison.Ordinal)));
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=watchdog:buff ", autopilot.Lines[^1], StringComparison.Ordinal);
        Assert.Equal(("watchdog:buff", false), (Stored(store).Stop, Stored(store).Completed));

        // none: a run allowed no recovery stops on its first trip
        var w0 = new ApWorld(key, output);
        w0.Factory.Wedge = spec => spec.Kind == StationKinds.Buff;
        var strict = w0.Autopilot(new AutopilotOptions { WatchdogScale = 0.05, WatchdogRecoveries = 0 });
        Assert.Equal(ScenarioStatus.Failed, w0.Run(strict));
        Assert.Equal(["bind", "buff"], Kinds(w0)); // M5 7: the start-up audit's bind, then the wedged buff
    }

    // ------------------------------------------------------------------------------------------------ M5 4 fix F1 (review.md 4-R1, 4-R2, 4-R5, 4-R6, 4-R7, 4-R9, 4-R14, 4-R15)

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_checkpoint_of_a_re_created_character_or_of_another_run_is_set_aside_by_its_guid_and_its_tag(string key)
    {
        // 4-R1: the file holds the character's guid and the run's tag
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output, atHome: false);
        var guid = w.Trip.Vendor.State.Self.Guid;
        Assert.NotEqual(0u, guid);
        var first = w.Autopilot(new AutopilotOptions { RunTag = "round-1" }, store);
        w.Run(first, a => InFight(a, 3));
        first.Dispose();
        var saved = Stored(store, "round-1");
        Assert.Equal((guid, "round-1"), (saved.CharacterGuid, saved.RunTag));
        var text = store.Json!;

        // the same character, the same tag: resumed
        var same = w.Autopilot(new AutopilotOptions { RunTag = "round-1" }, store);
        w.Run(same, a => InFight(a, 1));
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: read: resuming cycle 1", StringComparison.Ordinal));
        Assert.Empty(store.Aside);

        // the character was re-created under the same name: another guid, the file is another character's
        store.Json = text.Replace($"\"characterGuid\": {guid}", "\"characterGuid\": 1342177999", StringComparison.Ordinal);
        var recreated = new ApWorld(key, output, atHome: false);
        var second = recreated.Autopilot(new AutopilotOptions { RunTag = "round-1" }, store);
        recreated.Run(second, a => InFight(a, 1));
        Assert.Single(store.Aside);
        Assert.Contains(recreated.Lines, l => l.StartsWith("checkpoint: set aside: another character (guid 0x", StringComparison.Ordinal));
        Assert.DoesNotContain(recreated.Lines, l => l.Contains("checkpoint: read:", StringComparison.Ordinal));
        Assert.True(second.Totals.Kills < 3 && second.CycleTally.Kills < 3, "the other character's kills were not taken");
        Assert.Equal(guid, Stored(store, "round-1").CharacterGuid); // the new run wrote its own file with the guid it is

        // another run's tag
        store.Json = text;
        store.Aside.Clear();
        var other = new ApWorld(key, output, atHome: false);
        var third = other.Autopilot(new AutopilotOptions { RunTag = "round-2" }, store);
        other.Run(third, a => InFight(a, 1));
        Assert.Single(store.Aside);
        Assert.Contains(other.Lines, l => l.StartsWith("checkpoint: set aside: another run (tag 'round-1' in the file, 'round-2' now)", StringComparison.Ordinal));

        // a file of an older build holds no guid (0): it matches
        store.Json = text.Replace($"\"characterGuid\": {guid}", "\"characterGuid\": 0", StringComparison.Ordinal);
        store.Aside.Clear();
        var older = new ApWorld(key, output, atHome: false);
        var fourth = older.Autopilot(new AutopilotOptions { RunTag = "round-1" }, store);
        older.Run(fourth, a => InFight(a, 1));
        Assert.Empty(store.Aside);
        Assert.Contains(older.Lines, l => l.StartsWith("checkpoint: read: resuming cycle 1", StringComparison.Ordinal));
    }

    /// <summary>M6 6 ME (<c>m6-trip-mean-resume</c>): the trip's mean is in the seed (a hand-back) and in the checkpoint (a restart), and a file of an older build, with no mean, loads and leaves the seed length.</summary>
    [Fact]
    public void The_trips_mean_survives_a_hand_back_and_a_restart_and_an_older_file_without_it_loads()
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        w.Factory.Script = spec => spec.Kind == StationKinds.Logistics ? new ScriptedRun(spec.Name, _ => ScenarioStatus.Succeeded, () => "sold=3 bought=2") : null;
        var first = w.Autopilot(new AutopilotOptions { TripAfterKills = 2 }, store);
        Assert.True(double.IsNaN(first.Seed.TripMeanSeconds)); // no trip folded yet: nothing to carry
        w.Run(first, a => a.State == AutopilotState.Fight && w.Factory.Made.Any(s => s.Kind == StationKinds.Logistics));
        var mean = first.TripMeanSeconds;
        Assert.NotEqual(AutopilotScenario.TripSeedSeconds, mean);

        // a hand-back: the seed carries it into the rebuilt scenario before its first step
        var seed = first.Seed;
        Assert.Equal((mean, 1), (seed.TripMeanSeconds, seed.TripMeanCount));
        var handed = new ApWorld(Den, output).Autopilot(new AutopilotOptions(), seed: seed);
        Assert.Equal(mean, handed.TripMeanSeconds);

        // a restart: the checkpoint holds it, and the next run reads it back
        var saved = Stored(store);
        Assert.Equal(mean, saved.TripMeanSeconds);
        Assert.Equal(1, saved.TripMeanCount);
        var w2 = new ApWorld(Den, output);
        var second = w2.Autopilot(new AutopilotOptions(), store);
        w2.Run(second, a => InFight(a, 1));
        Assert.Equal(mean, second.TripMeanSeconds);

        // a file written before the fields: they read as null and 0, and the seed length stands
        var root = System.Text.Json.Nodes.JsonNode.Parse(store.Json!)!.AsObject();
        root.Remove("tripMeanSeconds");
        root.Remove("tripMeanCount");
        store.Json = root.ToJsonString();
        var w3 = new ApWorld(Den, output);
        var third = w3.Autopilot(new AutopilotOptions(), store);
        w3.Run(third, a => InFight(a, 1));
        Assert.Equal(AutopilotScenario.TripSeedSeconds, third.TripMeanSeconds);
        Assert.Contains(w3.Lines, l => l.StartsWith("checkpoint: read: resuming", StringComparison.Ordinal));
    }

    private static AutopilotCheckpoint Stored(MemoryCheckpointStore store, string runTag) =>
        AutopilotCheckpoint.Parse(store.Json!, "BSD Mage", out var why, runTag: runTag) ?? throw new InvalidOperationException("the stored checkpoint does not parse: " + why);

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_drop_during_the_death_station_keeps_the_death_and_enters_the_station_again_after_the_reducer_was_reset(string key)
    {
        // 4-R2, 4-R14, 4-R15: the death station died, stood at the lifestone and hangs; the session drops
        var w = new ApWorld(key, output, atHome: false);
        var calls = 0;
        w.Factory.OnDeathStep = () => calls++;
        w.Factory.HangDeathAt = 2;
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true }, new MemoryCheckpointStore(), host);
        w.Run(autopilot, a => a.State == AutopilotState.Death && calls >= 4);
        Assert.Equal(AutopilotState.Death, autopilot.State);
        Assert.Equal(0, autopilot.Totals.Deaths);

        w.Drop();
        var status = w.Run(autopilot, a => a.Cycles == 1);

        Assert.Equal(ScenarioStatus.Running, status);
        Assert.Equal(0u, host.GuidAfterReset); // the double went through the reducer's Reset: the character was forgotten before it walked back in
        Assert.Equal(1, host.Reconnects);
        Assert.Equal(1, autopilot.Totals.Deaths); // the death is counted: the station ran again and finished
        Assert.Equal(2, Kinds(w).Count(n => n == "death"));
        var seen = w.Factory.ObservedAtStart ?? throw new InvalidOperationException("the second death station was not given the autopilot's tally");
        Assert.True(seen.VictimNotices >= 1, "the tally kept the death's notices: " + seen.VictimNotices);
        Assert.True(w.Factory.Made.Last(s => s.Kind == StationKinds.Death).NeedsOverride is not null, "the second station is the one entered after a respawn");
        var line = autopilot.Lines[0];
        Assert.StartsWith("autopilot cycle n=1 state=death ", line, StringComparison.Ordinal);
        Assert.Equal((1, 1), (Int(line, "deaths"), Int(line, "reconnects")));
        Assert.Contains(w.Lines, l => l.Contains("the death station was cut: the death's tally is kept", StringComparison.Ordinal));
        w.Run(autopilot, a => InFight(a, 1));
        Assert.Equal(AutopilotState.Fight, autopilot.State); // and the loop went on to its fight
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M6_7_AG_a_death_station_whose_body_moves_is_not_cut_and_one_that_stands_still_is(string key)
    {
        // gate m6-7-g: the 900 s bound ran from the state's start, cut the corpse trip 9.7 m from the corpse with the trip working. The death state reads the body as the trip state does (M6 4 F1): 10 m is progress.
        var w = new ApWorld(key, output, atHome: false);
        var (moving, frames, side) = (true, 0, 1f);
        w.Factory.HangDeathAt = 2;
        w.Factory.OnDeathStep = () =>
        {
            if (moving && ++frames % 40 == 0 && w.Trip.Vendor.State.Self.Position is { } at)
            {
                side = -side;
                w.Trip.Vendor.StandAt(at.CellId, at.Origin + new Vector3(side * 11, 0, 0));
            }
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true, WatchdogScale = 0.05 }); // the bound: 45 s
        w.Run(autopilot, a => a.State == AutopilotState.Death && a.CurrentRun is not null);
        var start = w.Now;
        w.Run(autopilot, _ => w.Now > start + 180);

        Assert.Equal(AutopilotState.Death, autopilot.State);
        Assert.Equal(0, autopilot.Cycles);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("watchdog: death over ", StringComparison.Ordinal));

        moving = false; // the trip stands still: the bound holds
        w.Run(autopilot, a => a.Cycles == 1);
        Assert.Contains(w.Lines, l => l.StartsWith("watchdog: death over ", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Totals.Deaths); // one death, cut once: not two
        Assert.Equal(2, Kinds(w).Count(n => n == "death"));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_watchdog_trip_during_the_death_station_keeps_the_death_and_a_death_not_yet_made_is_scripted_again(string key)
    {
        // the death happened and the station hangs: the watchdog cuts it and the station is entered again from the known state, with no recall (a dead mage casts nothing)
        var w = new ApWorld(key, output, atHome: false);
        w.Factory.HangDeathAt = 2;
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true, WatchdogScale = 0.05 });
        w.Run(autopilot, a => a.Cycles == 1);
        Assert.Contains(w.Lines, l => l.StartsWith("watchdog: death over ", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Totals.Deaths);
        Assert.NotNull(w.Factory.ObservedAtStart);
        Assert.DoesNotContain("recall-home", Kinds(w));
        Assert.Equal(2, Kinds(w).Count(n => n == "death"));

        // the station hung before the mage died: nothing to recover, the scripted death is made again at the next fight with a kill
        var w2 = new ApWorld(key, output, atHome: false);
        w2.Factory.HangDeathAt = 0;
        var again = w2.Autopilot(new AutopilotOptions { TestDeath = true, WatchdogScale = 0.05 });
        w2.Run(again, a => a.Cycles == 1);
        Assert.Contains(w2.Lines, l => l.StartsWith("watchdog: death over ", StringComparison.Ordinal));
        Assert.Equal(1, again.Totals.Deaths);
        Assert.Null(w2.Factory.ObservedAtStart); // no tally was offered: the second station is the scripted one
        Assert.Equal(2, w2.Factory.DeathStationsMade);
        Assert.Contains("recall-home", Kinds(w2));
        Assert.Contains(w2.Lines, l => l.Contains("the scripted death is made again", StringComparison.Ordinal));
    }

    /// <summary>The fields of a handler that answered for the corpse, on the death station's line.</summary>
    private static string Answered(string corpse, string abort) => $"deaths=1 corpse_recovered={corpse} route=rung-change trip_abort={abort}";

    [Theory]
    [InlineData(DeathCorpse.NoneNeeded, "none-needed", true, false)] // r1f-climb's end: verdict ok=false (hits_taken=0 on a one-blow death), the handler's answer none-needed
    [InlineData(DeathCorpse.Yes, "none", true, false)] // any recovered end
    [InlineData(DeathCorpse.Rotted, "rotted", true, false)]
    [InlineData(DeathCorpse.No, "corpse-off-route", true, false)] // the named end
    [InlineData(DeathCorpse.NoneNeeded, "none-needed", false, true)] // a failure after none-needed: nothing is owed either
    public void M6_7_U_a_station_that_answered_for_the_corpse_is_not_entered_again_and_the_climb_goes_on(string corpse, string abort, bool verdictFails, bool failed)
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.Script = spec =>
        {
            if (spec.Kind != StationKinds.Death)
            {
                return null;
            }

            made++;
            return new ScriptedRun(spec.Name, _ => failed ? ScenarioStatus.Failed : ScenarioStatus.Succeeded, () => Answered(corpse, abort)) { VerdictFails = verdictFails, Failure = failed ? "handler: wear: scripted" : null };
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true });

        w.Run(autopilot, a => a.Cycles == 1);
        w.Run(autopilot, a => InFight(a, 1));

        Assert.Equal(1, made); // no second corpse trip
        Assert.Equal(1, autopilot.Totals.Deaths + autopilot.CycleTally.Deaths);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Null(autopilot.StopName);
        Assert.Contains(w.Lines, l => l.Contains($"the handler answered corpse_recovered={corpse} trip_abort={abort}: nothing is owed, no second trip", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("the corpse is not recovered yet", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot recovery", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_U_a_cut_after_none_needed_counts_the_death_goes_to_the_known_state_and_never_walks_home()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.Script = spec =>
        {
            if (spec.Kind != StationKinds.Death)
            {
                return null;
            }

            made++;
            return new ScriptedRun(spec.Name, step => step == 0 ? w.Factory.Died() : null, () => Answered(DeathCorpse.NoneNeeded, "none-needed")); // hangs in the items back
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true, WatchdogScale = 0.05 });

        w.Run(autopilot, a => a.State == AutopilotState.Death && a.CurrentRun is not null);
        w.Run(autopilot, a => InFight(a, 2) || a.State == AutopilotState.Stopped);

        Assert.Contains(w.Lines, l => l.StartsWith("watchdog: death over ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("the death station was cut after the handler answered corpse_recovered=none-needed trip_abort=none-needed: nothing is owed, it is not entered again", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.Contains("the corpse is not recovered yet", StringComparison.Ordinal));
        Assert.DoesNotContain("recall-home", Kinds(w));
        Assert.True(autopilot.Totals.Deaths + autopilot.CycleTally.Deaths >= 1);
        Assert.Null(autopilot.StopName);
    }

    [Theory]
    [InlineData("handler: route=rung-change: travel:rung-change:exit:tusker-burrow:walk")]
    [InlineData("handler: route=runner: the travel seam has no runner from the lifestone to Tusker Burrow's portal (the ground has no portal section): TODO(m6-first-tie-walk)")] // r1f-climb's three in a row
    public void M6_7_U_three_identical_death_station_failures_name_the_corpse_lost_and_the_night_goes_on(string failure)
    {
        var w = new ApWorld(Den, output, atHome: false);
        var made = 0;
        w.Factory.FailWith = spec =>
        {
            if (spec.Kind != StationKinds.Death)
            {
                return null;
            }

            made++;
            return failure;
        };
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true });

        w.Run(autopilot, a => a.Cycles == 1 || a.State == AutopilotState.Stopped);
        w.Run(autopilot, a => InFight(a, 1) || a.State == AutopilotState.Stopped);

        Assert.Equal(3, made);
        Assert.Null(autopilot.StopName);
        Assert.Equal(AutopilotState.Fight, autopilot.State);
        Assert.Equal(1, autopilot.Totals.Deaths + autopilot.CycleTally.Deaths); // one death, however many entries
        Assert.Equal(1, autopilot.Totals.CorpsesLost + autopilot.CycleTally.CorpsesLost); // counted: the [ladder] line's corpses_lost=
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot death: corpse_recovered=no trip_abort={AutopilotScenario.CorpseLostAbort}: the corpse trip is given up after failed:death:", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("autopilot stop=", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_7_U_the_third_death_in_one_handling_stays_the_owners_named_stop()
    {
        // M5 D92: the rule (e) moves a corpse trip's failures, not this stop
        var w = new ApWorld(Den, output, atHome: false);
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Death ? DeathHandler.ThirdDeathStop + ": a third death in one handling, during the respawn" : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true });

        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot, a => a.State == AutopilotState.Stopped));

        Assert.StartsWith("failed:death:" + DeathHandler.ThirdDeathStop, autopilot.StopName, StringComparison.Ordinal);
        Assert.Equal(0, autopilot.Totals.CorpsesLost);
    }

    [Theory]
    [InlineData(null, DeathCorpse.NoneNeeded, "none-needed", true)] // a cut after the answer
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.NoneNeeded, "none-needed", true)]
    [InlineData("handler: wear: scripted", DeathCorpse.NoneNeeded, "none-needed", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.Yes, "none", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.Partial, "none", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.Rotted, "rotted", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.NotFound, "corpse-not-found", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.No, "corpse-off-route", true)]
    [InlineData(DeathAnswer.VerdictOnly, DeathCorpse.No, "none", false)] // a plain no: no name
    [InlineData(DeathAnswer.VerdictOnly, "n/a", "none", false)]
    [InlineData("handler: route=rung-change: travel:rung-change:walk", DeathCorpse.No, "none", false)] // the handler failed on the way: the trip is still owed
    [InlineData("handler: wear: scripted", DeathCorpse.Yes, "none", false)] // the handler failed after the corpse: the station runs again, as before
    [InlineData(null, DeathCorpse.Yes, "none", false)] // a cut: only none-needed owes nothing by itself
    [InlineData(null, null, null, false)]
    public void M6_7_U_the_handlers_answer_is_read_from_its_named_ends_only(string? why, string? corpse, string? abort, bool answered) => Assert.Equal(answered, DeathAnswer.Of(why, corpse, abort) is not null);

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_drop_during_the_trip_runs_the_logistics_station_again_and_the_cut_trips_sold_and_bought_count(string key)
    {
        var ground = GroundCatalog.Get(key);
        var w = new ApWorld(key, output, held: l => ground.IsCarried(l.Wcid) ? l.Keep : Math.Max(l.StopUnder, l.TripUnder - 1), atHome: false)
        {
            HeldAfterRelogin = l => l.Keep, // what the cut trip bought is in the pack the login delivers
        };
        var host = new Host(w);
        var autopilot = w.Autopilot(new AutopilotOptions(), new MemoryCheckpointStore(), host);
        w.Run(autopilot, a => a.State == AutopilotState.Trip && a.CurrentRun is { } r && Int(r.VerdictFields(), "bought") > 0);
        var cutBought = Int(autopilot.CurrentRun!.VerdictFields(), "bought");
        Assert.True(cutBought > 0);

        w.Drop();
        w.Run(autopilot, a => a.Cycles == 1);

        Assert.Equal(0u, host.GuidAfterReset);
        Assert.Equal(2, Kinds(w).Count(n => n == "logistics"));
        var line = autopilot.Lines[0];
        Assert.StartsWith("autopilot cycle n=1 state=trip ", line, StringComparison.Ordinal);
        Assert.Equal(1, Int(line, "trips")); // one trip, though two stations ran
        Assert.Equal(cutBought, Int(line, "bought")); // what the cut trip bought is not lost (the second station found nothing more due)
        Assert.Equal(1, Int(line, "reconnects"));
        Assert.Contains(w.Lines, l => l.Contains("the trip was cut: the logistics station runs again (by its counters)", StringComparison.Ordinal));

        // a forced trip cut before any sale, purchase or spending is forced again
        var f = new ApWorld(key, output, atHome: false);
        var forcedHost = new Host(f);
        var forced = f.Autopilot(new AutopilotOptions { TripAfterKills = 1 }, null, forcedHost);
        f.Run(forced, a => a.State == AutopilotState.Trip && a.CurrentRun is not null);
        f.Drop();
        f.Run(forced, a => a.Cycles == 1);
        Assert.Equal([JourneyPlans.TripForced, JourneyPlans.TripForced], f.Factory.Made.Where(s => s.Kind == StationKinds.Logistics).Select(s => s.Arguments[JourneyPlans.TripArgument]).ToArray());
        Assert.Equal(1, forced.Totals.Trips);
        Assert.Contains(f.Lines, l => l.Contains("forced: the forced trip was not taken yet", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void An_abandoned_autopilot_folds_the_fight_in_and_keeps_a_cut_death_or_trip_for_the_next_one(string key)
    {
        // 4-R9: the fight in flight counts, the checkpoint is written, nothing runs, no done line
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output, atHome: false);
        var first = w.Autopilot(new AutopilotOptions { Minutes = 3 }, store);
        w.Run(first, a => InFight(a, 4));
        var writes = store.Writes;
        first.Abandon("interrupt");
        Assert.True(first.CycleTally.Kills >= 4, "the fight's kills were folded in: " + first.CycleTally.Kills);
        Assert.Null(first.CurrentRun);
        Assert.Empty(first.Lines);
        Assert.Null(first.StopName);
        Assert.Equal(writes + 1, store.Writes);
        var written = Stored(store);
        Assert.True(written.CycleTally.Kills >= 4);
        Assert.Equal("abandoned: interrupt", written.LastDecision);
        Assert.False(written.Completed);
        first.Dispose();
        Assert.Equal(writes + 1, store.Writes); // the disposal writes no second exit
        Assert.Equal(ScenarioStatus.Running, first.Step(w.Trip.View, w.Now)); // nothing runs after it

        // 4-R5: the seed carries the seconds used, so the budget survives the hand-back
        var seed = first.Seed;
        Assert.InRange(seed.SecondsUsed, 6, 3 * 60);
        Assert.Equal(first.SecondsUsed, seed.SecondsUsed, 6);
        var restart = w.Now;
        var second = w.Autopilot(new AutopilotOptions { Minutes = 3 }, null, null, seed);
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(second));
        Assert.Equal("minutes", second.StopName);
        Assert.InRange(w.Now - restart, 180 - seed.SecondsUsed - 2, 180 - seed.SecondsUsed + 3); // only what is left of the budget, with no checkpoint file to carry it
        Assert.InRange(second.SecondsUsed, 178, 183);

        // 4-R2: a death cut by the abandon is kept in the seed; the next autopilot enters the death station again with its tally
        var store2 = new MemoryCheckpointStore();
        var d = new ApWorld(key, output, atHome: false);
        var calls = 0;
        d.Factory.OnDeathStep = () => calls++;
        d.Factory.HangDeathAt = 2;
        var cutDeath = d.Autopilot(new AutopilotOptions { TestDeath = true }, store2);
        d.Run(cutDeath, a => a.State == AutopilotState.Death && calls >= 4);
        cutDeath.Abandon("interrupt");
        Assert.NotNull(cutDeath.Seed.PendingDeath);
        cutDeath.Dispose();
        var afterDeath = d.Autopilot(new AutopilotOptions(), store2, null, cutDeath.Seed);
        d.Run(afterDeath, a => a.Cycles >= 1);
        Assert.Equal(1, afterDeath.Totals.Deaths);
        Assert.True((d.Factory.ObservedAtStart ?? throw new InvalidOperationException("no tally was offered")).VictimNotices >= 1);
        Assert.Null(afterDeath.Seed.PendingDeath);

        // 4-R2: a trip cut by the abandon is kept in the seed, with what it sold and bought in the checkpoint
        var store3 = new MemoryCheckpointStore();
        var ground = GroundCatalog.Get(key);
        var t = new ApWorld(key, output, held: l => ground.IsCarried(l.Wcid) ? l.Keep : Math.Max(l.StopUnder, l.TripUnder - 1), atHome: false) { HeldAfterRelogin = l => l.Keep };
        var cutTrip = t.Autopilot(new AutopilotOptions(), store3);
        t.Run(cutTrip, a => a.State == AutopilotState.Trip && a.CurrentRun is { } r && Int(r.VerdictFields(), "bought") > 0);
        var cutBought = Int(cutTrip.CurrentRun!.VerdictFields(), "bought");
        cutTrip.Abandon("interrupt");
        Assert.Equal(JourneyStations.TripByCounters, cutTrip.Seed.PendingTrip);
        Assert.Equal(cutBought, cutTrip.CycleTally.Bought);
        cutTrip.Dispose();
        var afterTrip = t.Autopilot(new AutopilotOptions(), store3, null, cutTrip.Seed);
        t.Run(afterTrip, a => a.Cycles >= 1);
        Assert.Equal(1, afterTrip.Totals.Trips);
        Assert.True(afterTrip.Totals.Bought >= cutBought, $"what the cut trip bought ({cutBought}) is in the totals with what the second station bought: {afterTrip.Totals.Bought}");
        Assert.Equal(2, Kinds(t).Count(n => n == "logistics"));
    }

    [Theory]
    [InlineData(Den, "den-fight")]
    [InlineData(Temple, "den-fight")]
    [InlineData(Den, "buff")]
    [InlineData(Temple, "buff")]
    public void A_station_failure_is_recovered_by_the_recall_home_and_the_known_state_and_logged_by_name(string key, string failing)
    {
        // 4-R6
        var w = new ApWorld(key, output, atHome: failing == "buff");
        var made = 0;
        w.Factory.FailWith = spec => spec.Name == failing && ++made == 1 ? "scripted: the walk failed" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => made >= 2 && InFight(a, 2));
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        var state = failing == "buff" ? "buff" : "fight";
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot recovery after {state}:scripted:_the_walk_failed", StringComparison.Ordinal));
        Assert.Contains("recall-home", Kinds(w));
        Assert.Equal("den-fight", Kinds(w)[^2]); // the last is the recall home before the stop (M5 7 F18)
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:test ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_failure_past_the_cycles_recoveries_stops_by_name_and_the_facts_a_recall_cannot_change_stop_at_once(string key)
    {
        var w = new ApWorld(key, output);
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Buff ? "scripted: no" : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        Assert.Equal(ScenarioStatus.Failed, w.Run(autopilot));
        Assert.Equal("failed:buff:scripted:_no", autopilot.StopName);
        Assert.Equal(key == Den ? ["bind", "buff", "buff", "buff"] : ["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(w)); // M5 7: the start-up audit binds at home first // two recoveries (the budget), then the stop; M6 7 BC: no recall home from home (the Den)
        Assert.Equal(key == Den ? 1 : 2, w.Lines.Count(l => l.StartsWith("autopilot recovery after buff:", StringComparison.Ordinal))); // the Den's second recovery is the repeated failure's settle-step (RepeatedFailure), the same cell now
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=failed:buff:scripted:_no ", autopilot.Lines[^1], StringComparison.Ordinal);

        // M5 7 G2 (edge audit 12h): a failure text that merely contains "kit" is recovered like any station failure (the budget, then the stop by name)
        var k = new ApWorld(key, output);
        k.Factory.FailWith = spec => spec.Kind == StationKinds.Buff ? "kit: the wand is missing" : null;
        var kit = k.Autopilot(new AutopilotOptions());
        Assert.Equal(ScenarioStatus.Failed, k.Run(kit));
        Assert.StartsWith("failed:buff:kit:", kit.StopName, StringComparison.Ordinal);
        Assert.Equal(key == Den ? ["bind", "buff", "buff", "buff"] : ["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(k)); // M5 7: the start-up audit binds at home first; M6 7 BC: no recall home from home (the Den)
        Assert.Equal(key == Den ? 1 : 2, k.Lines.Count(l => l.StartsWith("autopilot recovery after buff:", StringComparison.Ordinal)));

        // the counters' stop in a trip: no recovery either
        var c = new ApWorld(key, output, atHome: false);
        c.Factory.FailWith = spec => spec.Kind == StationKinds.Logistics ? "logistics: stop: a line is under its stop line" : null;
        var counters = c.Autopilot(new AutopilotOptions { TripAfterKills = 1 });
        Assert.Equal(ScenarioStatus.Failed, c.Run(counters));
        Assert.StartsWith("failed:trip:logistics:", counters.StopName, StringComparison.Ordinal);
        Assert.Equal("recall-home", Kinds(c)[^1]); // no recovery recall; the one recall is the recall home before the stop (M5 7 F18)
        Assert.Equal(1, Kinds(c).Count(k => k == "recall-home"));
        Assert.DoesNotContain(c.Lines, l => l.StartsWith("autopilot recovery after", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_death_station_that_fails_keeps_the_death_and_is_entered_again_and_a_failed_trip_is_made_again(string key)
    {
        var w = new ApWorld(key, output, atHome: false);
        var made = 0;
        w.Factory.FailWith = spec => spec.Kind == StationKinds.Death && ++made == 1 ? "scripted: the arrival wait ran out" : null;
        var autopilot = w.Autopilot(new AutopilotOptions { TestDeath = true });
        w.Run(autopilot, a => a.Cycles == 1);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot recovery after death:scripted:_the_arrival_wait_ran_out", StringComparison.Ordinal));
        Assert.Equal(1, autopilot.Totals.Deaths);
        Assert.NotNull(w.Factory.ObservedAtStart);
        Assert.DoesNotContain("recall-home", Kinds(w));

        var t = new ApWorld(key, output, atHome: false);
        var trips = 0;
        t.Factory.FailWith = spec => spec.Kind == StationKinds.Logistics && ++trips == 1 ? "scripted: the vendor was not in reach" : null;
        var trip = t.Autopilot(new AutopilotOptions { TripAfterKills = 1 });
        t.Run(trip, a => a.Cycles == 1);
        Assert.Contains(t.Lines, l => l.StartsWith("autopilot recovery after trip:scripted:_the_vendor_was_not_in_reach", StringComparison.Ordinal));
        Assert.Equal(2, Kinds(t).Count(n => n == "logistics"));
        Assert.Equal(1, trip.Totals.Trips);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_watchdog_clock_starts_when_the_station_starts_and_the_fights_bound_comes_from_the_grounds_respawn_schedule(string key)
    {
        // 4-R7: a station held in its peace phase (the keeper's pass and the peace have their own bounds) is not on the state's clock
        var w = new ApWorld(key, output, atHome: false);
        w.Factory.FightNeeds = new StationNeeds(Peace: true);
        w.Trip.Vendor.R.CombatModeIs(CombatModes.Melee); // the scripted ACE never answers the peace request: the phase ends by its own 15 s bound
        var autopilot = w.Autopilot(new AutopilotOptions { WatchdogScale = 0.005, WatchdogRecoveries = 0 }); // the fight's bound is under 8 s here
        var status = w.Run(autopilot);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal("failed:fight:peace", autopilot.StopName);
        Assert.DoesNotContain(w.Lines, l => l.StartsWith("watchdog:", StringComparison.Ordinal));

        // the fight's bound is the ground's longest respawn (delay plus tick) and a margin: a profile read, not a den constant (D46)
        var ground = GroundCatalog.Get(key);
        var longest = ground.Respawn.Generators.Max(g => g.DelaySeconds + g.TickSeconds);
        var bound = AutopilotWatchdogs.FightBaseSeconds(ground);
        Assert.Equal(longest + AutopilotWatchdogs.FightMarginSeconds, bound, 6);
        Assert.True(bound > longest);
        var dogs = new AutopilotWatchdogs(scale: 0.5, fightBaseSeconds: bound);
        Assert.Equal(bound * 0.5, dogs.BoundSeconds(AutopilotState.Fight), 6);
        Assert.Equal(AutopilotWatchdogs.BaseSeconds(AutopilotState.Buff) * 0.5, dogs.BoundSeconds(AutopilotState.Buff), 6); // the other states keep their bounds
        Assert.Equal(AutopilotWatchdogs.BaseSeconds(AutopilotState.Fight), new AutopilotWatchdogs().BoundSeconds(AutopilotState.Fight), 6); // no ground: the fixed default

        // a held state has no clock until its station starts
        dogs.Hold(AutopilotState.Buff);
        Assert.Null(dogs.Over(1e9));
        dogs.Enter(AutopilotState.Buff, 100);
        Assert.Null(dogs.Over(100 + 150));
        Assert.NotNull(dogs.Over(100 + 151));
    }
}
