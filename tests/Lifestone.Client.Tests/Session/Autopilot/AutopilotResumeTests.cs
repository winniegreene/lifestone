using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
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
        Assert.Equal(["bind", "buff", "den-travel", "den-fight", "recall-home"], Kinds(w, made)); // M5 7: the rebuilt run's start-up audit binds at home (a new journey context: not proven this session) // from home, not from the file's fight; the last is the recall home before the stop (M5 7 F18)
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
        Assert.Equal(["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(w)); // M5 7: the start-up audit binds at home first // two recoveries, then the stop
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
        Assert.Equal(["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(w)); // M5 7: the start-up audit binds at home first // two recoveries (the budget), then the stop
        Assert.Equal(2, w.Lines.Count(l => l.StartsWith("autopilot recovery after buff:", StringComparison.Ordinal)));
        Assert.StartsWith("autopilot done ok=no cycles=0 stop=failed:buff:scripted:_no ", autopilot.Lines[^1], StringComparison.Ordinal);

        // M5 7 G2 (edge audit 12h): a failure text that merely contains "kit" is recovered like any station failure (the budget, then the stop by name)
        var k = new ApWorld(key, output);
        k.Factory.FailWith = spec => spec.Kind == StationKinds.Buff ? "kit: the wand is missing" : null;
        var kit = k.Autopilot(new AutopilotOptions());
        Assert.Equal(ScenarioStatus.Failed, k.Run(kit));
        Assert.StartsWith("failed:buff:kit:", kit.StopName, StringComparison.Ordinal);
        Assert.Equal(["bind", "buff", "recall-home", "buff", "recall-home", "buff"], Kinds(k)); // M5 7: the start-up audit binds at home first
        Assert.Equal(2, k.Lines.Count(l => l.StartsWith("autopilot recovery after buff:", StringComparison.Ordinal)));

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
