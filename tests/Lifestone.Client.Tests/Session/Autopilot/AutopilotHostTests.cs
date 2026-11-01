using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 item 4 h2 (D20; DoD 9; the controls of M5 7 D145): the dial's logic over the scripted ACE of <see cref="AutopilotTests"/> (both dungeon profiles). Proved: the press starts the autopilot and stops it cleanly, /stop stops him where he
/// stands (the loop stops stepping, <c>interrupted=</c> counts; a person's movement key no longer does) and /auto hands the controls back (a fresh loop reads the state it finds, <c>resumed=</c> counts, the cycle and done lines carry both), the kill switch halts every
/// automated action within one tick (the queued ones cancelled by the Action API, the loop not stepped) and a hand-back is refused while it is on, and the status the inspector shows (state, last decision, next trigger).
/// </summary>
public sealed partial class AutopilotHostTests(ITestOutputHelper output)
{
    private const string Den = AutopilotRig.Den;
    private const string Temple = AutopilotRig.Temple;

    /// <summary>The scripted fight, sending one automation action each step (a chat line, as a swing or a cast would be) so the tests see what the autopilot sends.</summary>
    private sealed class ChattyFactory(AutopilotRig.Factory inner, IGameSessionView view, List<ActionHandle> sent) : IStationFactory
    {
        public IStation Create(StationSpec spec)
        {
            var real = inner.Create(spec);
            return spec.Kind == StationKinds.DenFight ? new Wrapped(real, view, sent) : real;
        }
    }

    private sealed class Wrapped(IStation inner, IGameSessionView view, List<ActionHandle> sent) : IStation
    {
        public string Name => inner.Name;

        public StationNeeds Needs => inner.Needs;

        public IStationRun Start(JourneyContext context) => new Run(inner.Start(context), view, sent);
    }

    private sealed class Run(IStationRun inner, IGameSessionView view, List<ActionHandle> sent) : IStationRun, IStoppableFight
    {
        public string Name => inner.Name;

        public string Stage => inner.Stage;

        public string? Failure => inner.Failure;

        public double? WhereMaxMetres => inner.WhereMaxMetres;

        public long Unmatched => inner.Unmatched;

        public bool Ok => inner.Ok;

        public int Kills => (inner as IStoppableFight)?.Kills ?? 0;

        public FightStopReason? StopReason => (inner as IStoppableFight)?.StopReason;

        public void RequestStop(FightStopReason reason) => (inner as IStoppableFight)?.RequestStop(reason);

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            sent.Add(view.Actions.Talk("swing", ActionSource.Automation));
            return inner.Step(session, now);
        }

        public string VerdictFields() => inner.VerdictFields();
    }

    private sealed class Rig
    {
        public Rig(ITestOutputHelper output, string key, AutopilotOptions options, bool chatty = false, AutopilotSeed? start = null)
        {
            Made = [];
            World = new AutopilotRig.ApWorld(key, output);
            Output = output;
            Options = options;
            Sent = [];
            IStationFactory factory = chatty ? new ChattyFactory(World.Factory, World.Trip.View, Sent) : World.Factory;
            Host = new AutopilotHostLogic(
                carry =>
                {
                    var made = carry.Remaining(options);
                    Made.Add(made);
                    var scenario = new AutopilotScenario(made, factory, World.Trip.Context(), null, null, carry.Seed ?? start);
                    carry.Apply(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground);
        }

        public ITestOutputHelper Output { get; }

        public AutopilotRig.ApWorld World { get; }

        public AutopilotOptions Options { get; }

        public List<ActionHandle> Sent { get; }

        /// <summary>The options each scenario was made with (a start, then every hand-back).</summary>
        public List<AutopilotOptions> Made { get; }

        public AutopilotHostLogic Host { get; }

        public IGameSessionView View => World.Trip.View;

        public MoveIntents Moves => View.Actions.Move;

        /// <summary>One frame, as the play host runs it: the world's ticks, then the dial's.</summary>
        public void Frame()
        {
            var t = World.Trip;
            t.Vendor.R.Tick(0.25);
            t.Shops.Pump();
            t.Xp.Pump();
            t.Vendor.R.Tick();
            Host.Tick(View, World.Now);
        }

        public bool RunUntil(Func<bool> reached, int frames = 40000)
        {
            for (var i = 0; i < frames; i++)
            {
                if (reached())
                {
                    return true;
                }

                Frame();
            }

            return reached();
        }

        public bool Fighting(int kills = 1) => Host.Scenario is { State: AutopilotState.Fight, CurrentRun: IStoppableFight f } && f.Kills >= kills;

        public void Dump() => Output.WriteLine(string.Join(Environment.NewLine, Host.Lines) + Environment.NewLine + string.Join(Environment.NewLine, World.Lines.Where(l => l.Contains("autopilot", StringComparison.Ordinal))));
    }

    private static int Int(string line, string name) =>
        int.Parse(line.Split(' ').First(p => p.StartsWith(name + "=", StringComparison.Ordinal))[(name.Length + 1)..], CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_key_starts_the_loop_from_the_state_it_finds_and_stops_it_cleanly_and_pressed_again_starts_a_new_run(string key)
    {
        var rig = new Rig(output, key, new AutopilotOptions());
        Assert.Equal(AutopilotMode.Off, rig.Host.Mode);
        rig.Frame();
        Assert.Null(rig.Host.Scenario); // off: nothing runs until the key

        rig.Host.Toggle(rig.View);
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.Equal("fight:den-fight", rig.Host.Status.State);

        rig.Host.Toggle(rig.View); // the key again: a clean stop request
        Assert.Equal(AutopilotMode.Stopping, rig.Host.Mode);
        rig.Host.Toggle(rig.View); // a press while it is stopping asks nothing more
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        rig.Dump();

        var done = rig.Host.Lines[^1];
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:key ", done, StringComparison.Ordinal);
        Assert.Equal((0, 0), (Int(done, "interrupted"), Int(done, "resumed")));
        Assert.True(rig.Host.Scenario!.Ok);
        Assert.Contains("stop=request:key", rig.Host.Status.LastDecision, StringComparison.Ordinal);

        // The key after a finished run starts a new one (a fresh loop; its lines replace the old run's).
        rig.Host.Toggle(rig.View);
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        Assert.Empty(rig.Host.Lines);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_7_D145_stop_stops_the_loop_where_he_stands_and_auto_hands_it_back_with_both_counts_in_the_lines(string key)
    {
        var rig = new Rig(output, key, new AutopilotOptions { TripAfterKills = 3 });
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        var scenario = rig.Host.Scenario!;
        var kills = ((IStoppableFight)scenario.CurrentRun!).Kills;

        Assert.Equal(AutopilotHostLogic.StoppedHereLine, rig.Host.StopHere(rig.View)); // the chat's /stop
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        Assert.Equal((1, 0), (rig.Host.Interrupted, rig.Host.Resumed));
        Assert.Contains("stopped by /stop", rig.Host.Status.LastDecision, StringComparison.Ordinal);
        Assert.DoesNotContain("recall-home", AutopilotRig.Kinds(rig.World)); // no recall: he stops where he stands

        // Paused: frames pass, the loop is not stepped (no new state, no new kill, no new line, no further arrow).
        var state = rig.Host.Status.State;
        var lines = rig.Host.Lines.Count;
        for (var i = 0; i < 80; i++)
        {
            rig.Frame();
        }

        Assert.Equal((AutopilotMode.Interrupted, state, lines), (rig.Host.Mode, rig.Host.Status.State, rig.Host.Lines.Count));
        Assert.Same(scenario, rig.Host.Scenario);
        Assert.Null(scenario.CurrentRun); // its run was dropped
        Assert.True(kills >= 1);

        // A second /stop while stopped answers plainly and counts nothing.
        Assert.StartsWith("Lifestone: the autopilot is not running (interrupted)", rig.Host.StopHere(rig.View), StringComparison.Ordinal);
        Assert.Equal(1, rig.Host.Interrupted);

        rig.Host.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason); // the hand-back (/auto through the host's press)
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        Assert.Equal((1, 1), (rig.Host.Interrupted, rig.Host.Resumed));
        Assert.NotSame(scenario, rig.Host.Scenario); // a fresh loop reads the state it finds
        Assert.Contains("handed back (resume)", rig.Host.Status.LastDecision, StringComparison.Ordinal);
        Assert.Equal(AutopilotHostLogic.ResumeReason, rig.Host.StartReason);

        // It goes on from where the player stands: in the ground, so the buff (no enchantment up), then the fight; three kills make the forced trip, which ends the cycle.
        Assert.True(rig.RunUntil(() => rig.Host.Lines.Any(l => l.StartsWith("autopilot cycle n=1 state=trip", StringComparison.Ordinal))));
        Assert.Contains(rig.World.Lines, l => l.Contains("known-state place=ground", StringComparison.Ordinal));
        var cycle = rig.Host.Lines.Single(l => l.StartsWith("autopilot cycle n=1 state=trip", StringComparison.Ordinal));
        Assert.Equal((1, 1), (Int(cycle, "interrupted"), Int(cycle, "resumed"))); // both in the cycle line (a1's fields)
        Assert.Equal(3, Int(cycle, "kills"));

        rig.Host.Stop("test");
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        rig.Dump();
        var done = rig.Host.Lines[^1];
        Assert.StartsWith("autopilot done ok=yes ", done, StringComparison.Ordinal);
        Assert.Equal((1, 1, 1), (Int(done, "interrupted"), Int(done, "resumed"), Int(done, "trips")));
        Assert.Contains("stop=request:test", done, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_D145_a_movement_key_never_takes_the_controls_a_stop_while_stopping_stands_down_and_the_stop_asked_before_still_stands()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        rig.Host.Stop("test"); // not yet stepping: the first step is the stop
        rig.Host.Stop("test");
        rig.Moves.ReportHumanKey("Jump"); // D98's hand-off is retired: a person's key reported to the session does nothing to the dial
        Assert.Equal((AutopilotMode.Stopping, 0), (rig.Host.Mode, rig.Host.Interrupted));
        rig.Host.StopHere(rig.View); // stopping counts as running for /stop
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Contains("stop=request:test", rig.Host.Lines[^1], StringComparison.Ordinal);

        var after = (rig.Host.Interrupted, rig.Host.Resumed);
        rig.Moves.ReportHumanKey("MoveForward"); // done: nothing to interrupt
        Assert.StartsWith("Lifestone: the autopilot is not running (done)", rig.Host.StopHere(rig.View), StringComparison.Ordinal);
        Assert.Equal(after, (rig.Host.Interrupted, rig.Host.Resumed));
        Assert.Equal(AutopilotMode.Done, rig.Host.Mode);
    }

    [Fact]
    public void A_stop_while_interrupted_hands_back_and_asks_the_fresh_loop_to_stop_so_the_lines_are_written()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View);
        rig.Host.Stop(AutopilotHostLogic.KeyReason);

        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        var done = rig.Host.Lines[^1];
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:key ", done, StringComparison.Ordinal);
        Assert.Equal((1, 1), (Int(done, "interrupted"), Int(done, "resumed")));
    }

    [Fact]
    public void M5_7_G2_the_stop_file_while_interrupted_hands_back_recalls_home_and_ends_the_unattended_run_with_the_game_quitting()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        Assert.True(rig.Host.Start(rig.View, "--autopilot-start"));
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View); // the owner holds the keys
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);

        rig.Host.Stop(AutopilotHostLogic.StopFileReason); // what the play host now does for the stop file in this mode

        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Contains("stop=request:stop-file", rig.Host.Lines[^1], StringComparison.Ordinal);
        Assert.Contains("recall-home", AutopilotRig.Kinds(rig.World));
        Assert.NotNull(rig.Host.UnattendedEnd); // the stop-file request ends the unattended run: the game logs off and quits
        Assert.Equal(0, rig.Host.UnattendedEnd!.ExitCode);
    }

    [Fact]
    public void M5_7_G2_the_stop_file_while_halted_by_the_kill_switch_ends_the_unattended_run_with_exit_code_1()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        Assert.True(rig.Host.Start(rig.View, "--autopilot-start"));
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.View.Actions.KillSwitch = true;
        rig.Frame();
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);

        rig.Host.Stop(AutopilotHostLogic.StopFileReason);

        Assert.Equal(AutopilotMode.Done, rig.Host.Mode);
        Assert.Equal(1, rig.Host.UnattendedEnd!.ExitCode);
        rig.View.Actions.KillSwitch = false;
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_kill_switch_halts_every_automated_action_within_one_tick_and_cancels_the_queued_ones(string key)
    {
        var rig = new Rig(output, key, new AutopilotOptions(), chatty: true);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        Assert.NotEmpty(rig.Sent);

        // Work the autopilot has queued and not sent: the chat lane's burst is spent, so these wait.
        var queued = Enumerable.Range(0, 40).Select(i => rig.View.Actions.Talk("queued " + i, ActionSource.Automation)).ToList();
        Assert.Contains(queued, h => !h.IsCompleted);
        var person = rig.View.Actions.Talk("the person's line", ActionSource.Ui);
        var sentBefore = rig.Sent.Count;
        var rowsBefore = rig.View.ActionLog.Total;

        rig.View.Actions.KillSwitch = true; // the person throws it
        rig.Host.Tick(rig.View, rig.World.Now); // ONE tick
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.Equal(1, rig.Host.Halts);
        Assert.All(queued, h => Assert.True(h.IsCompleted, "a queued automation action is withdrawn at once"));
        Assert.DoesNotContain(queued, h => h.Outcome!.Value.Kind is ActionOutcomeKind.Confirmed or ActionOutcomeKind.Sent);
        Assert.Contains(queued, h => h.Outcome!.Value.Kind == ActionOutcomeKind.Cancelled);
        Assert.False(person.IsCompleted && person.Outcome!.Value.Kind == ActionOutcomeKind.Cancelled); // the person's own line is none of the switch's business
        Assert.Equal(sentBefore, rig.Sent.Count); // the loop did not step in that tick

        // More frames: the loop sends nothing more, and the log gets no new automation row but the withdrawn ones.
        for (var i = 0; i < 100; i++)
        {
            rig.Frame();
        }

        Assert.Equal(sentBefore, rig.Sent.Count);
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.Null(rig.Host.Scenario!.CurrentRun);
        Assert.Contains("kill switch", rig.Host.Status.LastDecision, StringComparison.Ordinal);
        Assert.Equal(0, rig.Host.Interrupted); // the switch is not the person's key

        // A hand-back is refused while the switch is on; the key after the switch is off restarts the loop.
        rig.Host.Toggle(rig.View);
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.Contains("kill switch is on", rig.Host.Status.LastDecision, StringComparison.Ordinal);
        rig.View.Actions.KillSwitch = false;
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode); // turning the switch off alone restarts nothing
        rig.Host.Toggle(rig.View);
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        Assert.True(rig.RunUntil(() => rig.Sent.Count > sentBefore));
        Assert.Equal((0, 0), (rig.Host.Interrupted, rig.Host.Resumed));
        rig.Dump();
    }

    /// <summary>M5 4 fix F2 (4-R3): the sends the stations really queue (a vendor open, buy and sell, a corpse open, close and pick-up, a wield) are Automation, so one tick of the switch withdraws them all and the loop sends none after it.</summary>
    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void M5_4_f2_the_kill_switch_withdraws_the_queued_vendor_and_corpse_sends_the_stations_make(string key)
    {
        var rig = new Rig(output, key, new AutopilotOptions(), chatty: true);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        var vendor = rig.World.Ground.Vendors[0].Guid;
        var actions = rig.View.Actions;
        var queued = new List<ActionHandle>
        {
            actions.OpenVendor(vendor, ActionSource.Automation),
            actions.Buy(vendor, [(0x80006001u, 5u)], ActionSource.Automation),
            actions.Sell(vendor, [0x80007001u], ActionSource.Automation),
            actions.OpenContainer(0x80001101, ActionSource.Automation),
            actions.PickUp(0x80007002, 0x50000001, 0, ActionSource.Automation),
            actions.CloseContainer(0x80001101, ActionSource.Automation),
            actions.Wield(0x80007003, EquipMask.Held, ActionSource.Automation),
        };
        Assert.All(queued, h => Assert.False(h.IsCompleted, h.Kind + " waits in the queue"));
        var person = actions.OpenContainer(0x80001102, ActionSource.Ui);
        var sentBefore = rig.Sent.Count;

        actions.KillSwitch = true; // the person throws it
        rig.Host.Tick(rig.View, rig.World.Now); // ONE tick
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.All(queued, h => Assert.Equal(ActionOutcomeKind.Cancelled, h.Outcome!.Value.Kind));
        Assert.False(person.IsCompleted && person.Outcome!.Value.Kind == ActionOutcomeKind.Cancelled); // the person's own open is none of the switch's business

        // A station send made after the switch is refused at once, not queued.
        var after = actions.Buy(vendor, [(0x80006001u, 5u)], ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, after.Outcome!.Value.Kind);
        Assert.Equal(sentBefore, rig.Sent.Count);
        actions.KillSwitch = false;
    }

    /// <summary>M5 4 fix F2 (4-R11): a source pin, since the two log lines live in the Godot runner and the headless host, which no unit test reaches: neither logs the checkpoint's absolute path any more.</summary>
    [Fact]
    public void M5_4_f2_no_host_logs_the_checkpoints_absolute_path()
    {
        var runner = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotLegRunner.cs"));
        Assert.DoesNotContain("(\"checkpoint\", runner.CheckpointPath)", runner, StringComparison.Ordinal);
        Assert.Contains("(\"checkpoint\", System.IO.Path.GetFileName(runner.CheckpointPath))", runner, StringComparison.Ordinal);

        var headless = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Headless", "Program.cs"));
        Assert.DoesNotContain("\"autopilot checkpoint: \" + checkpoint.Path", headless, StringComparison.Ordinal);
        Assert.Contains("Path.GetRelativePath(root, checkpoint.Path)", headless, StringComparison.Ordinal);
        Assert.Contains("checkpoint.Describe", headless, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stop_while_halted_by_the_kill_switch_ends_at_once_with_no_run_in_flight()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.View.Actions.KillSwitch = true;
        rig.Frame();
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);

        rig.Host.Stop("test"); // nothing can be sent: the run ends at once
        Assert.Equal(AutopilotMode.Done, rig.Host.Mode);
        Assert.Contains("stopped while halted", rig.Host.Status.LastDecision, StringComparison.Ordinal);
        rig.View.Actions.KillSwitch = false;
    }

    [Fact]
    public void The_status_names_the_state_the_last_decision_and_the_next_trigger_from_the_stop_rules_the_forced_trip_and_the_stock()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = 30, TripAfterKills = 5 });
        Assert.Equal(AutopilotMode.Off, rig.Host.Status.Mode);
        Assert.Equal(string.Empty, rig.Host.Status.NextTrigger);

        var changes = 0;
        rig.Host.Changed += () => changes++;
        rig.Host.Toggle(rig.View);
        Assert.True(changes >= 1);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));

        var status = rig.Host.Status;
        Assert.Equal((AutopilotMode.Running, "fight:den-fight"), (status.Mode, status.State));
        Assert.Contains("-> fight", status.LastDecision, StringComparison.Ordinal);
        Assert.Contains("stop at 30 min: ", status.NextTrigger, StringComparison.Ordinal);
        Assert.Matches(@"stop at 30 min: \d+ (min|s) left", status.NextTrigger);
        Assert.Matches(@"trip at 5 kills: \d+ so far", status.NextTrigger);
        Assert.Matches(@"(trip|stop) at \d+ .+: \d+ held", status.NextTrigger); // the stock line closest to its trip
        Assert.True(changes > 3, "every arrow raised a change for the inspector");
        var text = AutomationInspector.FormatAutopilot(status);
        Assert.StartsWith("Autopilot: running | fight:den-fight | cycle 1", text, StringComparison.Ordinal);
        Assert.Contains("Next trigger: stop at 30 min:", text, StringComparison.Ordinal);
        rig.Dump();
    }

    [Fact]
    public void The_hand_back_carries_the_time_run_and_the_once_a_run_triggers_so_the_fresh_loop_does_not_start_them_again()
    {
        var carry = new AutopilotCarry(2, new AutopilotTally { Kills = 9 }, new AutopilotTally { Kills = 1, Interrupted = 1, Resumed = 1 }, 600, ForcedTripDone: true, TestDeathDone: true);
        var options = new AutopilotOptions { Minutes = 30, TripAfterKills = 5, TestDeath = true, Level = 80 };
        var remaining = carry.Remaining(options);
        Assert.Equal(20, remaining.Minutes, 6);
        Assert.Equal((0, false, 80), (remaining.TripAfterKills, remaining.TestDeath, remaining.Level));
        Assert.False(carry.IsEmpty);
        Assert.True(AutopilotCarry.None.IsEmpty);

        var untouched = AutopilotCarry.None.Remaining(options);
        Assert.Equal((30d, 5, true), (untouched.Minutes, untouched.TripAfterKills, untouched.TestDeath));
        Assert.Equal(0, AutopilotCarry.None.Remaining(new AutopilotOptions()).Minutes);
    }

    [Fact]
    public void A_scripted_death_and_the_forced_trip_are_taken_once_a_run_even_across_an_interrupt()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { TestDeath = true });
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Host.Lines.Any(l => l.StartsWith("autopilot cycle n=1 state=death", StringComparison.Ordinal))));
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View);
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting(3))); // three kills after the hand-back and no second scripted death
        Assert.Equal(1, rig.World.Factory.Made.Count(s => s.Kind == StationKinds.Death));
        rig.Host.Stop("test");
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Equal(1, Int(rig.Host.Lines[^1], "deaths"));
    }

    // ------------------------------------------------------------------------------------------------ M5 4 fix F3

    [Fact]
    public void M5_4_f3_4_r9_an_interrupt_and_a_halt_fold_the_fights_kills_into_the_cycle_and_the_fresh_loop_continues_the_count()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting(2)));
        var scenario = rig.Host.Scenario!;
        var kills = ((IStoppableFight)scenario.CurrentRun!).Kills;
        Assert.Equal(0, scenario.CycleTally.Kills); // a fight adds its kills to the cycle only when it ends

        rig.Host.StopHere(rig.View);
        Assert.Equal(kills, scenario.CycleTally.Kills); // Abandon folded the fight in
        Assert.Contains(rig.World.Lines, l => l.Contains("autopilot abandoned (chat-stop)", StringComparison.Ordinal));

        rig.Host.Toggle(rig.View);
        var fresh = rig.Host.Scenario!;
        Assert.NotSame(scenario, fresh);
        Assert.Equal((scenario.Cycle, scenario.Cycles), (fresh.Cycle, fresh.Cycles)); // the seed, not the interim cycles + 1 guess
        Assert.True(fresh.CycleTally.Kills >= kills);

        // The kill switch: the same fold at a halt.
        Assert.True(rig.RunUntil(() => rig.Fighting(1)));
        var before = fresh.CycleTally.Kills;
        var inFlight = ((IStoppableFight)fresh.CurrentRun!).Kills;
        rig.View.Actions.KillSwitch = true;
        rig.Frame();
        Assert.Equal(AutopilotMode.Halted, rig.Host.Mode);
        Assert.Equal(before + inFlight, fresh.CycleTally.Kills);
        Assert.Contains(rig.World.Lines, l => l.Contains("autopilot abandoned (halt)", StringComparison.Ordinal));
        rig.View.Actions.KillSwitch = false;
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_4_f3_4_r5_the_minutes_target_stays_the_total_across_a_resume_and_a_hand_back()
    {
        const double Total = 480;
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = Total }, start: new AutopilotSeed(1, 0, 240 * 60)); // a restart that resumed 240 of 480 minutes
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        var first = rig.Host.Scenario!;
        Assert.True(first.SecondsUsed >= 240 * 60);
        Assert.Matches(@"stop at 480 min: 2[0-4]\d min left", rig.Host.Status.NextTrigger); // 480 - 240 - a little: not 480, and not the process's own seconds only

        rig.Host.StopHere(rig.View);
        rig.Host.Toggle(rig.View);
        var fresh = rig.Host.Scenario!;
        Assert.Equal(Total, rig.Made[^1].Minutes); // the target is the run's total, not 480 minus this process's seconds
        Assert.True(fresh.SecondsUsed >= first.SecondsUsed, $"{fresh.SecondsUsed} vs {first.SecondsUsed}"); // the fresh loop starts at the minutes used
        Assert.Matches(@"stop at 480 min: 2[0-4]\d min left", rig.Host.Status.NextTrigger);

        // A carry with no seed (a factory that cannot seed) still gets only what is left, as before.
        Assert.Equal(20, new AutopilotCarry(2, new AutopilotTally(), new AutopilotTally(), 600).Remaining(new AutopilotOptions { Minutes = 30 }).Minutes, 6);
        Assert.Equal(30, new AutopilotCarry(2, new AutopilotTally(), new AutopilotTally(), 600, Seed: new AutopilotSeed(3, 2, 600)).Remaining(new AutopilotOptions { Minutes = 30 }).Minutes, 6);
    }

    [Fact]
    public void M5_4_f3_4_r4_the_dial_owns_the_buffs_while_it_runs_or_stops_and_gives_them_back_at_an_interrupt_or_done()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        bool Owns() => rig.Host.Mode is AutopilotMode.Running or AutopilotMode.Stopping; // the host's OwnsBuffs, which the re-buff driver's StandDownWhile reads
        Assert.False(Owns());
        rig.Host.Toggle(rig.View);
        Assert.True(Owns());
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View);
        Assert.False(Owns()); // the driver goes on while the person has the controls
        rig.Host.Toggle(rig.View);
        Assert.True(Owns());
        rig.Host.Stop("test");
        Assert.True(Owns()); // stopping still owns them
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.False(Owns());

        // The plumbing: the play host parses the buff options once for both owners, the driver finds the dial, and the leg shares the margin.
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        Assert.Contains("with { Buff = BuffOptions.FromArguments(parsed) }", host, StringComparison.Ordinal);
        Assert.Contains("public bool OwnsBuffs", host, StringComparison.Ordinal);
        var driver = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "RebuffDriverHost.cs"));
        Assert.Contains("_logic.StandDownWhile = () => autopilot.OwnsBuffs;", driver, StringComparison.Ordinal);
        var leg = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotLegRunner.cs"));
        Assert.Contains("with { Buff = Lifestone.Client.Session.Mage.BuffOptions.FromArguments(parsedArguments) }", leg, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_4_f3_4_r8_the_dial_yields_when_the_person_reconnects_and_not_for_its_own_tries()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var foreign = 0;
        rig.Host.ForeignReconnects = () => foreign; // the host's: the session's Reconnects less the autopilot's own tries
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        for (var i = 0; i < 20; i++)
        {
            rig.Frame();
        }

        Assert.Equal(AutopilotMode.Running, rig.Host.Mode); // nothing moved: it runs on (the autopilot's own tries are not in the number)

        foreign = 1; // the person pressed the disconnected screen's Reconnect
        rig.Frame();
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        Assert.Equal(1, rig.Host.Interrupted);
        Assert.Contains(rig.World.Lines, l => l.Contains(AutopilotHostLogic.ReconnectedLine, StringComparison.Ordinal));
        var lines = rig.Host.Lines.Count;
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal((AutopilotMode.Interrupted, lines), (rig.Host.Mode, rig.Host.Lines.Count)); // it starts no try of its own

        rig.Host.Toggle(rig.View); // the person's key hands back; the base moves with it, so the same press is not read twice
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        rig.Frame();
        Assert.Equal(AutopilotMode.Running, rig.Host.Mode);
        foreign = 2;
        rig.Frame();
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        rig.Host.Dispose();

        // The autopilot's own try is named in the log by the host's session wrapper and kept out of the number.
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        Assert.Contains("its own reconnect try", host, StringComparison.Ordinal);
        Assert.Contains("game.Reconnects - _ownReconnects", host, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_4_f3_4_r10_the_scripted_interrupt_counts_from_the_first_fight_and_with_a_scripted_death_from_the_cycle_after_it()
    {
        var plain = new Rig(output, Den, new AutopilotOptions());
        var arm = new AutopilotInterruptArm(deathScripted: false);
        plain.Host.Toggle(plain.View);
        Assert.False(arm.Update(plain.Host)); // running, but in no fight yet
        Assert.True(plain.RunUntil(() => arm.Update(plain.Host)));
        Assert.Equal(AutopilotState.Fight, plain.Host.Scenario!.State);
        Assert.Equal("fight:den-fight", plain.Host.Status.State);

        var rig = new Rig(output, Den, new AutopilotOptions { TestDeath = true });
        var deathArm = new AutopilotInterruptArm(deathScripted: true);
        Assert.True(rig.Host.TestDeathScripted);
        rig.Host.Toggle(rig.View);
        var fightBeforeTheDeath = false;
        Assert.True(rig.RunUntil(() =>
        {
            if (deathArm.Update(rig.Host))
            {
                return true;
            }

            fightBeforeTheDeath |= rig.Host.Scenario!.State == AutopilotState.Fight && rig.Host.TestDeathCycle == 0;
            return false;
        }));
        Assert.True(fightBeforeTheDeath, "the first cycle had a fight before the death; the arm waited through it");
        Assert.Equal(1, rig.Host.TestDeathCycle);
        Assert.True(rig.Host.Scenario!.Cycle > rig.Host.TestDeathCycle);
        Assert.Equal(AutopilotState.Fight, rig.Host.Scenario.State);

        // The key line names the state (the hand-off's log) and the help says where S counts from.
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        Assert.Contains("(_state ?? \"unknown\") + \" state\"", host, StringComparison.Ordinal);
        var help = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options", "OptionCatalog.Autopilot.cs"));
        Assert.Contains("first fight", help, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_4_f3_4_r16_a_hand_back_waits_for_a_held_movement_key_and_says_so_once()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var held = true;
        rig.Host.HumanKeyHeld = () => held;
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View);
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);

        Assert.False(rig.Host.HandBack(rig.View)); // the key is still down
        rig.Host.Toggle(rig.View); // and a second press asks the same
        for (var i = 0; i < 10; i++)
        {
            rig.Frame();
        }

        Assert.True(rig.Host.HandBackWaiting);
        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode);
        Assert.Equal(0, rig.Host.Resumed);
        Assert.Equal(1, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.WaitingForKeyLine, StringComparison.Ordinal)));

        held = false; // released: the next frame hands back
        rig.Frame();
        Assert.Equal((AutopilotMode.Running, 1, false), (rig.Host.Mode, rig.Host.Resumed, rig.Host.HandBackWaiting));
        rig.Host.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ M5 item 7 s1

    private const string Option = "--autopilot-start";

    private ITestOutputHelper Out => output;

    [Fact]
    public void M5_7_s1_a_start_by_the_option_runs_the_dial_once_logs_why_and_never_starts_it_again_by_itself()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        Assert.False(rig.Host.StartedByOption);
        rig.Host.Toggle(rig.View, Option);
        Assert.Equal((AutopilotMode.Running, true), (rig.Host.Mode, rig.Host.StartedByOption));
        Assert.Equal(1, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.StartedByOptionLine, StringComparison.Ordinal)));
        Assert.Contains("started by " + Option, rig.Host.Status.LastDecision, StringComparison.Ordinal);
        Assert.True(rig.RunUntil(() => rig.Fighting()));

        // The option is asked once; a press of the key is a person's: it stops (a request), and after the stop only a press starts a run again.
        rig.Host.Toggle(rig.View);
        Assert.Equal(AutopilotMode.Stopping, rig.Host.Mode);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal(AutopilotMode.Done, rig.Host.Mode); // nothing restarted it
        Assert.Equal(1, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.StartedByOptionLine, StringComparison.Ordinal)));
        rig.Host.Toggle(rig.View); // the key after a Done starts a run of the person's
        Assert.Equal((AutopilotMode.Running, false), (rig.Host.Mode, rig.Host.StartedByOption));
        Assert.Equal(1, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.StartedByOptionLine, StringComparison.Ordinal)));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_an_unattended_run_that_reaches_its_minutes_asks_for_a_log_off_and_exit_code_0_after_the_done_line()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = 0.5 });
        rig.Host.Toggle(rig.View, Option);
        Assert.Null(rig.Host.UnattendedEnd);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        rig.Dump();

        var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
        Assert.Equal(0, end.ExitCode);
        Assert.Contains("stop=minutes ok=yes", end.Decision, StringComparison.Ordinal);
        Assert.Contains("exit code 0", end.Decision, StringComparison.Ordinal);
        Assert.Contains("stop=minutes", rig.Host.Lines[^1], StringComparison.Ordinal);
        var lines = rig.World.Lines.ToList();
        var done = lines.FindIndex(l => l.Contains("autopilot done ok=yes", StringComparison.Ordinal));
        var decision = lines.FindIndex(l => l.Contains(end.Decision, StringComparison.Ordinal));
        Assert.True(done >= 0 && decision > done, "the decision is logged after the done line");
        Assert.Equal(1, lines.Count(l => l.Contains(end.Decision, StringComparison.Ordinal)));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_an_unattended_run_that_reaches_its_level_ends_the_same_way_with_exit_code_0()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Level = 80 });
        rig.Host.Toggle(rig.View, Option);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.World.Level(80);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
        Assert.Equal(0, end.ExitCode);
        Assert.Contains("stop=level ok=yes", end.Decision, StringComparison.Ordinal);
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_an_unattended_run_that_fails_ends_with_exit_code_1()
    {
        // The buff station fails every time (a scripted failure): past the cycle's recoveries the run stops by name with ok=no.
        var rig = new Rig(output, Den, new AutopilotOptions());
        rig.World.Factory.FailWith = spec => spec.Kind == StationKinds.Buff ? "scripted: no" : null;
        rig.Host.Toggle(rig.View, Option);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        rig.Dump();
        var end = Assert.IsType<AutopilotUnattendedEnd>(rig.Host.UnattendedEnd);
        Assert.Equal(1, end.ExitCode);
        Assert.Contains("ok=no", end.Decision, StringComparison.Ordinal);
        Assert.Contains("exit code 1", end.Decision, StringComparison.Ordinal);
        Assert.Contains("autopilot done ok=no", rig.Host.Lines[^1], StringComparison.Ordinal);
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_a_stop_request_by_a_person_keeps_the_game_open_and_an_interrupt_alone_ends_nothing()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = 30 });
        rig.Host.Toggle(rig.View, Option);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.StopHere(rig.View); // D145: /stop keeps the game open, as the movement key's interrupt did
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal(AutopilotMode.Interrupted, rig.Host.Mode); // paused: the game stays open
        Assert.Null(rig.Host.UnattendedEnd);
        rig.Host.Toggle(rig.View); // handed back (the run is still the unattended one)
        Assert.True(rig.Host.StartedByOption);
        Assert.True(rig.RunUntil(() => rig.Fighting()));
        rig.Host.Stop(AutopilotHostLogic.KeyReason); // the stop request of a person
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Contains("stop=request:key", rig.Host.Lines[^1], StringComparison.Ordinal);
        Assert.Null(rig.Host.UnattendedEnd);
        Assert.Contains(rig.World.Lines, l => l.Contains("ended on a stop request", StringComparison.Ordinal) && l.Contains("the game stays open", StringComparison.Ordinal));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_a_run_the_key_started_never_asks_the_game_to_quit_even_at_its_minutes()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { Minutes = 0.5 });
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Host.Mode == AutopilotMode.Done));
        Assert.Contains("stop=minutes", rig.Host.Lines[^1], StringComparison.Ordinal);
        Assert.Null(rig.Host.UnattendedEnd);
        Assert.DoesNotContain(rig.World.Lines, l => l.Contains("the game logs off and quits", StringComparison.Ordinal));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_the_dial_closes_the_vitae_page_once_the_death_station_ends_and_leaves_a_page_opened_outside_a_death_alone()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { TestDeath = true });
        var shown = true; // the page is up before the death; the dial does not touch it until the death station has ended
        var asked = 0;
        rig.Host.CloseVitaePage = () =>
        {
            asked++;
            if (!shown)
            {
                return false;
            }

            shown = false;
            return true;
        };
        rig.Host.Toggle(rig.View);
        var inDeath = false;
        Assert.True(rig.RunUntil(() =>
        {
            if (rig.Host.Scenario!.State == AutopilotState.Death && !inDeath)
            {
                inDeath = true;
                Assert.True(shown, "the page stayed open while the death station ran");
                Assert.Equal(0, asked);
            }

            return rig.Host.VitaeCloses > 0;
        }));
        rig.Dump();
        Assert.True(inDeath);
        Assert.False(shown);
        Assert.Equal((1, 1), (rig.Host.VitaeCloses, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.VitaeClosedLine, StringComparison.Ordinal))));
        Assert.NotEqual(AutopilotState.Death, rig.Host.Scenario!.State);

        var closedAt = asked;
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal(closedAt, asked); // closed: it stopped asking
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_a_vitae_page_that_opens_a_little_after_the_death_station_is_still_closed_inside_the_grace_and_only_once()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { TestDeath = true });
        var shown = false;
        var asked = 0;
        rig.Host.CloseVitaePage = () =>
        {
            asked++;
            if (!shown)
            {
                return false;
            }

            shown = false;
            return true;
        };
        rig.Host.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Host.Lines.Any(l => l.StartsWith("autopilot cycle n=1 state=death", StringComparison.Ordinal)) && rig.Host.Scenario!.State != AutopilotState.Death));
        Assert.Equal(0, rig.Host.VitaeCloses);
        Assert.True(asked >= 1, "asked at the station's exit");
        shown = true; // the late vitae message opens the page
        rig.Frame();
        rig.Frame();
        Assert.Equal((1, false), (rig.Host.VitaeCloses, shown));
        shown = true; // a person opening it later is left alone
        for (var i = 0; i < 40; i++)
        {
            rig.Frame();
        }

        Assert.Equal((1, true), (rig.Host.VitaeCloses, shown));
        Assert.Equal(1, rig.World.Lines.Count(l => l.Contains(AutopilotHostLogic.VitaeClosedLine, StringComparison.Ordinal)));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_s1_the_play_host_reads_the_options_starts_once_quits_with_the_logics_code_and_gives_the_button_the_dial()
    {
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        var unattended = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.Unattended.cs"));
        Assert.Contains("AutopilotOptions.FromArguments(parsed)", host, StringComparison.Ordinal); // the stop rules, the watchdog and the reconnect options reach the kit and the logic
        Assert.Contains("kit.Create(carry)", host, StringComparison.Ordinal);
        Assert.Contains("AutopilotHostOptions.FromArguments(parsed)", host, StringComparison.Ordinal);
        Assert.Contains("tools.Autopilot = logic", host, StringComparison.Ordinal);
        Assert.Contains("logic.CloseVitaePage", host, StringComparison.Ordinal);
        Assert.Contains("hostOptions.StartAtLogin", host, StringComparison.Ordinal);
        Assert.Contains("_startByOption = false;", unattended, StringComparison.Ordinal); // once
        Assert.Contains("GetTree().Quit(code)", unattended, StringComparison.Ordinal);
        Assert.Contains("_faultEnd ? FaultExitCode : _logic.UnattendedEnd?.ExitCode", unattended, StringComparison.Ordinal);
        Assert.Contains("Actions.LogOff(ActionSource.Host)", unattended, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_F1_an_unattended_run_whose_host_faults_or_whose_kit_load_fails_logs_off_and_quits_with_exit_code_1_and_a_person_run_is_unchanged()
    {
        // The Godot node cannot be built here: the rule is pinned in the source (the seam is the host's two fault paths and the option that makes a run unattended).
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        var unattended = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.Unattended.cs"));
        Assert.Contains("_unattendedRun = _startByOption;", host, StringComparison.Ordinal); // unattended only with --autopilot-start
        Assert.Contains("BeginFaultEnd(\"the kit load failed\")", host, StringComparison.Ordinal);
        Assert.Contains("BeginFaultEnd(\"the host faulted\")", host, StringComparison.Ordinal);
        Assert.Contains("TickFaultEnd(delta, session)", host, StringComparison.Ordinal); // the guard tripped: the normal tick no longer runs, the fault end still does
        Assert.Contains("public const int FaultExitCode = 1;", unattended, StringComparison.Ordinal);
        Assert.Contains("if (!_unattendedRun || _faultEnd)", unattended, StringComparison.Ordinal); // without the option nothing changes
        Assert.Contains("GetTree().Quit(FaultExitCode)", unattended, StringComparison.Ordinal);
    }
}
