using System.Globalization;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 16 E0b (D139; docs/automation-engine.md section 5): the climb runs with no window. The rung move's steps are the client's (<see cref="AutopilotRungKits"/>, moved from the play host's node) behind the kit-factory seam
/// (<see cref="IAutopilotKitFactory"/>), and the headless host has its half (<see cref="HeadlessAutopilotKits"/>, <see cref="HeadlessClimbScenario"/>). The world is <see cref="AutopilotMoveOnLoopTests"/>' scripted one: the
/// climber at level 9 on Holtburg's outskirts, who reaches level 10 in her first fight there, the Burrow's gate open to the run by the buff bot's standing spells.
/// </summary>
/// <remarks>
/// <para><b>Proved:</b> the climber starts on the outskirts by her level's band (the first press, the client's); she levels in the fight, the run reads the Burrow's gate open where she stands and ends
/// <c>stop=rung-moved:burrow</c>; the client's move-on asks the one rung owner for the next ground, has the headless kit prepared over it and the next scenario built on it with the ended run's hand-over, on the plan that ground
/// needs; that scenario steps, travels and fights there; the run stays unattended across the move; the climb's lines hold both runs'; a stop ends the climb ok. The play host's shape (a kit read on workers: not ready after
/// the prepare) is proved through the same client code with a kit that waits: the move-on answers wait, asks the host to press with the unattended token, and the press starts the next run. The refusals end the climb by name.
/// A hand-back's carry goes through the one shared copy (<see cref="AutopilotRungKits.Scenario"/>).</para>
/// <para><b>Not proved:</b> any real walk between the grounds (the travel and the rung change are scripted stations that stand her at the arrival), any real fight, buff pass, trip or gate read (scripted, or set by the test:
/// <c>TODO(engine-climb-replay)</c>), the real parts (<c>HeadlessAutopilotScenario</c>'s: the keeper, the runner host, the catalog's stations need the dats and a server: <c>TODO(engine-headless-climb-live)</c>), the login and
/// log off, the checkpoint as a file, what the ladder character's name switches on (the scripted character keeps its own name), and the Godot node's own half (its worker loads and its frame's pending press: pinned in the source
/// by <see cref="The_play_host_builds_its_kits_through_the_clients_seam_and_keeps_only_what_needs_the_game"/>).</para>
/// </remarks>
public sealed class HeadlessClimbTests(ITestOutputHelper output)
{
    // M6 redoubt (the owner's decision of 2026-11-18): the rung outskirts' own ground is the Holtburg Redoubt; the outdoor ground holtburg-redoubt is the rung's fallback for a climber the portal does not admit (RungFallback)
    private const string Outskirts = "holtburg-redoubt";
    private const string Burrow = "tusker-retreat"; // M6 retreat (the owner, 2026-11-19): the burrow rung's ground is the Tusker Retreat (the Burrow its alternate)

    /// <summary>What the run reads standing in a gate line's place: the bot's level VII spell, as the live log read it (<see cref="AutopilotMoveOnLoopTests"/>).</summary>
    private static string? Stands(string line, int level) =>
        level <= 7 ? string.Create(CultureInfo.InvariantCulture, $"not in the book; stands by another's spell 2066 at level VII, 3510 s left: no scroll of her own is needed while it stands") : null;

    /// <summary>A kit as the play host's is: asked for, it is not ready until its parts are read (<see cref="Read"/>, the node's frame), and a move-on that finds it so asks for the press.</summary>
    private sealed class WorkerKits(HeadlessAutopilotKits inner) : IAutopilotKitFactory
    {
        private bool _read;

        public List<string> Calls { get; } = [];

        public (string? StartedBy, string? Reason)? Pending { get; private set; }

        public bool Ready => inner.Ready && _read;

        public void Prepare(HuntingGround ground, JourneyPlan plan)
        {
            Calls.Add("prepare:" + ground.Key);
            _read = false;
            inner.Prepare(ground, plan);
        }

        public void PressWhenReady(string? startedBy, string? reason)
        {
            Calls.Add($"press-when-ready:{startedBy ?? "null"}:{reason ?? "null"}");
            Pending = (startedBy, reason);
        }

        public AutopilotScenario Create(AutopilotCarry carry)
        {
            Calls.Add("create");
            return Ready ? inner.Create(carry) : throw new InvalidOperationException("the kit's parts are not read");
        }

        /// <summary>The workers are done and the node built the kit: the press that waited is returned, once.</summary>
        public (string? StartedBy, string? Reason)? Read()
        {
            _read = true;
            var pending = Pending;
            Pending = null;
            return pending;
        }
    }

    private sealed class Rig
    {
        /// <summary>
        /// The climber at <paramref name="level"/> (9: the outskirts' last level) on the outskirts; her third kill of the run's first fight brings her to <paramref name="levelsTo"/> (10: the Burrow's first), 0 for no level-up.
        /// The character keeps the scripted world's own name, not the ladder's: the ladder's name switches the level-up spend to the climber's health rule, whose attribute raises this scripted server never answers (the name as a
        /// mode is E6's to remove).
        /// </summary>
        public Rig(ITestOutputHelper output, bool tripFirst = false, int level = 9, int levelsTo = 10)
        {
            var kills = 0;
            World = AutopilotMoveOnLoopTests.Climb.ClimberWorld(output, tripFirst, level, onKill: (world, _) =>
            {
                if (++kills == 3 && levelsTo > 0)
                {
                    world.Level(levelsTo); // the level-up in the fight: the run reads the next rung's gate where she stands
                }
            });
            var home = World.Ground.Home;
            World.Factory.Script = spec => spec.Kind switch
            {
                StationKinds.Logistics => new ScriptedRun(spec.Name, step => step >= 12 ? ScenarioStatus.Succeeded : null, () => "sold=0 bought=0"), // a trip of three seconds that restocks nothing
                StationKinds.RungChange or StationKinds.DenTravel => new ScriptedRun(spec.Name, _ =>
                {
                    // no walk: she is stood at the arrival of the ground the kit in hand was prepared over (or at home), as the scripted travel of the move-on's own tests does
                    var goesHome = spec.Arguments.TryGetValue(RungChangeStation.DirectionArgument, out var direction) && direction == "home";
                    var arrival = Kits!.Ground!.ArrivalDestination().Location.Position;
                    World.Trip.Vendor.StandAt(goesHome ? home.LifestoneCell : arrival.CellId, goesHome ? home.LifestonePoint : arrival.Local);
                    return ScenarioStatus.Succeeded;
                }, () => $"ground={Kits!.Ground!.Key} ok=yes fail=none route=recall"),
                _ => null,
            };
            Kits = new HeadlessAutopilotKits(
                Options,
                (ground, plan, _) =>
                {
                    Plans.Add(plan);
                    return new HeadlessKitParts(World.Factory, World.Context().WithDungeon(ground));
                },
                Store)
            {
                Created = scenario =>
                {
                    scenario.BuffsForTest = new RungBuffRead(static _ => 8, static _ => 4); // every line up, as after the bot's pass and her own
                    scenario.GateStandsForTest = Stands;
                    scenario.TrophyQuests = []; // no trophy errand: the scripted world makes no quest station
                    Scenarios.Add(scenario);
                },
            };
        }

        public ApWorld World { get; }

        public AutopilotOptions Options { get; } = new() { Job = AutopilotJob.LevelUp, LadderQuests = false };

        public MemoryCheckpointStore Store { get; } = new();

        public HeadlessAutopilotKits Kits { get; }

        public List<JourneyPlan> Plans { get; } = [];

        public List<AutopilotScenario> Scenarios { get; } = [];

        public IGameSessionView View => World.Trip.View;

        public IEnumerable<string> Lines => World.Lines;

        public HeadlessClimbScenario NewClimb() => new(Options, Kits, World.Trip.Context().Log);

        public void Frame()
        {
            var t = World.Trip;
            t.Vendor.R.Tick(0.25);
            t.Shops.Pump();
            t.Xp.Pump();
            t.Vendor.R.Tick();
            AnswerWieldsAndModes(World);
        }

        public ScenarioStatus Run(HeadlessClimbScenario climb, Func<bool>? until = null, int frames = 6000)
        {
            var status = ScenarioStatus.Running;
            for (var i = 0; i < frames && status == ScenarioStatus.Running && until?.Invoke() != true; i++)
            {
                Frame();
                status = climb.Step(View, World.Now);
            }

            return status;
        }

        public bool FightsOn(AutopilotHostLogic logic, string ground, int kills = 1) =>
            logic.Scenario is { State: AutopilotState.Fight, CurrentRun: IStoppableFight fight } && fight.Kills >= kills && Kits.Ground?.Key == ground;

        public void Print() => World.Output.WriteLine(string.Join(Environment.NewLine, World.Lines.Where(l => (l.StartsWith("autopilot", StringComparison.Ordinal) || l.StartsWith("ladder", StringComparison.Ordinal)) && !l.StartsWith("autopilot audit ", StringComparison.Ordinal))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // a stale stock line calls a trip before the first check, as live
    public void The_climb_runs_with_no_window_across_one_rung_move_and_the_next_scenario_steps_on_the_next_ground(bool tripFirst)
    {
        var rig = new Rig(output, tripFirst);
        using var climb = rig.NewClimb();

        var status = rig.Run(climb, () => rig.FightsOn(climb.Logic, Burrow));
        rig.Print();

        // the next scenario steps: it travelled to the next rung's ground and fights there, and the climb goes on
        Assert.Equal(ScenarioStatus.Running, status);
        Assert.True(rig.FightsOn(climb.Logic, Burrow), "she fights on the Burrow");
        Assert.True(JourneyGrounds.At(StationWhere.Den, rig.World.Trip.Vendor.State.Self.Position, GroundCatalog.Get(Burrow)), "she stands on the Burrow");
        Assert.Equal(AutopilotMode.Running, climb.Logic.Mode);

        // the first press and the move-on built one scenario each, through the client's seam: the first on the start's ground, the next on the ground the one rung owner gave the move-on
        Assert.Equal([Outskirts, Burrow], rig.Kits.MadeOn);
        Assert.Equal(2, rig.Scenarios.Count);
        Assert.Same(rig.Scenarios[1], climb.Autopilot);
        Assert.Equal(Burrow, climb.Rungs.Ground?.Key);
        Assert.Equal(JourneyPlans.Climber with { Ground = StationWhere.Den }, rig.Plans[0]); // M6 redoubt: the rung outskirts is a dungeon rung now (the Holtburg Redoubt): the plan's ground is the den, as the Burrow's
        Assert.Same(JourneyPlans.Climber, AutopilotHostLogic.PlanFor(rig.Options, GroundCatalog.Get("holtburg-outskirts"))); // an outdoor ground (the rung's fallback): the climber's plan as it is
        Assert.Equal(StationWhere.Den, rig.Plans[1].Ground); // a dungeon rung: the plan's ground is the den (AutopilotHostLogic.PlanFor), chosen by the client, not by the host
        Assert.Equal(StationWhere.Den, climb.Rungs.Plan?.Ground);

        // she levelled in the fight, the run read the Burrow's gate open where she stood and ended rung-moved, and the dial restarted once by itself, in the frame of the stop (a headless kit is ready as soon as it is asked for)
        Assert.Contains(rig.Lines, l => l.StartsWith("ladder: rung: asked by start: level=9 read_at=9", StringComparison.Ordinal) && l.EndsWith("-> rung=outskirts ground=holtburg-redoubt reason=band", StringComparison.Ordinal));
        Assert.Contains(rig.Lines, l => l.StartsWith("autopilot fight: stop asked (move-on-reached) at kills=3", StringComparison.Ordinal));
        Assert.Single(rig.Lines, l => l.StartsWith("autopilot stop=rung-moved:burrow: ", StringComparison.Ordinal));
        Assert.Equal(1, climb.Logic.MoveOns);
        Assert.Null(climb.Logic.MoveOnRefusal);
        Assert.Contains(rig.Lines, l => l.Contains("ladder: rung: asked by move-on: level=10 read_at=10", StringComparison.Ordinal) && l.EndsWith("named=burrow -> rung=burrow ground=tusker-retreat reason=stop-named:burrow", StringComparison.Ordinal));
        Assert.Contains(rig.Lines, l => l.Contains("ladder: move-on restart: act: the next rung's parts are ready; the dial starts the next run now (restart 1)", StringComparison.Ordinal));

        // the play host's own lines, word for word, now written by the client: one for each ground the kit was built over, the start's first
        var built = rig.Lines.Where(l => l.StartsWith("autopilot: level up: level ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, built.Length);
        Assert.Equal($"autopilot: level up: level 9, the run's ground is {Outskirts} ({GroundCatalog.Get(Outskirts).Name}); the parts are built over it", built[0]);
        Assert.Equal($"autopilot: level up: level 10, the run's ground is {Burrow} ({GroundCatalog.Get(Burrow).Name}); the parts are built over it", built[1]);

        // what the restart carries: the ended run's hand-over, and the run stays unattended (the one start's token, then the move-on's)
        Assert.Contains(rig.Lines, l => l == "autopilot host: the start carries the hand-over of the run that just ended (rung-moved:burrow on holtburg-redoubt): the next run checks each part before it uses it");
        Assert.Contains(rig.Lines, l => l == "autopilot: started by " + HeadlessClimbScenario.StartedBy);
        Assert.Contains(rig.Lines, l => l == "autopilot: started by " + AutopilotHostLogic.MoveOnReason);
        Assert.True(climb.Logic.StartedByOption);
        Assert.Contains(rig.Lines, l => l.StartsWith("autopilot start: ", StringComparison.Ordinal) && l.Contains(" on tusker-retreat ", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Lines, l => l.Contains(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal));

        // the climb's lines hold the ended run's done line though the dial's scenario is the next one
        Assert.Contains(climb.Lines, l => l.StartsWith("autopilot done ok=yes ", StringComparison.Ordinal) && Field(l, "stop") == "rung-moved:burrow");
    }

    [Fact]
    public void A_stop_asked_after_the_move_ends_the_climb_ok_and_its_lines_hold_both_runs()
    {
        var rig = new Rig(output);
        using var climb = rig.NewClimb();
        Assert.Equal(ScenarioStatus.Running, rig.Run(climb, () => rig.FightsOn(climb.Logic, Burrow)));

        climb.Logic.Stop(AutopilotHostLogic.StopFileReason);
        var status = rig.Run(climb);
        rig.Print();

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(climb.Ok);
        Assert.Null(climb.Failure);
        Assert.Equal(1, climb.Logic.MoveOns); // the stop made no further restart
        var done = climb.Lines.Where(l => l.StartsWith("autopilot done ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["rung-moved:burrow", "request:stop-file"], done.Select(l => Field(l, "stop")));
        Assert.All(done, l => Assert.Equal("yes", Field(l, "ok")));
        Assert.Equal(0, climb.Logic.UnattendedEnd!.ExitCode); // the unattended end's code is the host's exit code
    }

    [Fact]
    public void A_kit_that_is_read_on_workers_makes_the_move_on_wait_and_the_hosts_press_starts_the_next_run_still_unattended()
    {
        // The play host's shape, through the same client code: its kit is null until the node's frame has read the parts, so a move-on to another ground always waits and the node presses the dial.
        var rig = new Rig(output);
        var kits = new WorkerKits(rig.Kits);
        rig.Kits.Session = rig.View;
        using var logic = new AutopilotHostLogic(kits.Create, rig.World.Trip.Context().Log, rig.Options);
        var rungs = new AutopilotRungKits(logic, rig.Options, kits, rig.World.Trip.Context().Log);
        logic.MoveOn = rungs.MoveOnNext;

        // the first press: the rung's kit is asked for, the workers read it, then the press goes through
        Assert.Null(rungs.PrepareRung(rig.View));
        Assert.False(kits.Ready);
        Assert.Null(kits.Read());
        logic.Toggle(rig.View, AutopilotMoveOnLoopTests.StartOption);
        Assert.Equal(AutopilotMode.Running, logic.Mode);

        for (var i = 0; i < 6000 && logic.Mode == AutopilotMode.Running; i++)
        {
            rig.Frame();
            logic.Tick(rig.View, rig.World.Now);
        }

        // the run ended rung-moved; the next kit is not read, so the dial waits (done, not stopped, no end of the game) and the press is the host's, with the unattended token
        Assert.Equal(AutopilotMode.Done, logic.Mode);
        Assert.Equal(1, logic.MoveOns);
        Assert.Null(logic.MoveOnRefusal);
        Assert.Null(logic.UnattendedEnd);
        Assert.Equal((AutopilotHostLogic.MoveOnReason, (string?)null), kits.Pending);
        Assert.Equal(["prepare:" + Outskirts, "create", "prepare:" + Burrow, $"press-when-ready:{AutopilotHostLogic.MoveOnReason}:null"], kits.Calls);
        Assert.Contains(rig.Lines, l => l.Contains("ladder: move-on restart: act: the next rung's parts are being read; the host presses the dial when they are (restart 1)", StringComparison.Ordinal));
        Assert.Equal([Outskirts], rig.Kits.MadeOn); // nothing is built before the parts are read

        // the node's frame: the parts are read, the kit is built, the pending press is made
        var (startedBy, reason) = kits.Read()!.Value;
        logic.Toggle(rig.View, startedBy, reason);
        for (var i = 0; i < 6000 && !rig.FightsOn(logic, Burrow); i++)
        {
            rig.Frame();
            logic.Tick(rig.View, rig.World.Now);
        }

        rig.Print();
        Assert.True(rig.FightsOn(logic, Burrow), "she fights on the Burrow");
        Assert.Equal([Outskirts, Burrow], rig.Kits.MadeOn);
        Assert.True(logic.StartedByOption); // the run stays the unattended one across the rung
        Assert.Contains(rig.Lines, l => l == "autopilot: started by " + AutopilotHostLogic.MoveOnReason);
        Assert.Contains(rig.Lines, l => l == "autopilot host: the start carries the hand-over of the run that just ended (rung-moved:burrow on holtburg-redoubt): the next run checks each part before it uses it");
    }

    [Fact]
    public void A_persons_run_that_moves_on_to_a_kit_not_read_is_pressed_as_a_resume_and_a_kit_over_the_same_ground_is_not_built_again()
    {
        var rig = new Rig(output);
        var kits = new WorkerKits(rig.Kits);
        using var logic = new AutopilotHostLogic(kits.Create, rig.World.Trip.Context().Log, rig.Options);
        var rungs = new AutopilotRungKits(logic, rig.Options, kits, rig.World.Trip.Context().Log);
        var outskirts = GroundCatalog.Get(Outskirts);

        // no run was started by an option: a move-on's press is a person's resume (the dial has no run here, so its read is the start's: the outskirts by her level's band)
        Assert.Same(MoveOnAnswer.Wait, rungs.MoveOnNext(rig.View));
        Assert.Equal(((string?)null, AutopilotHostLogic.ResumeReason), kits.Pending);
        Assert.Equal(["prepare:" + Outskirts, $"press-when-ready:null:{AutopilotHostLogic.ResumeReason}"], kits.Calls);

        // read, the same ground's kit stands: nothing is asked for again, and the answer is go
        _ = kits.Read();
        rungs.UseRung(outskirts, 9);
        Assert.Same(MoveOnAnswer.Go, rungs.MoveOnNext(rig.View));
        Assert.Equal(2, kits.Calls.Count);

        // another ground: the kit is asked for again, and the one line is written for each ground built over
        rungs.UseRung(GroundCatalog.Get(Burrow), 10);
        Assert.Equal("prepare:" + Burrow, kits.Calls[^1]);
        Assert.False(kits.Ready);
        Assert.Equal(2, rig.Lines.Count(l => l.StartsWith("autopilot: level up: level ", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_climb_refuses_by_name_a_level_with_no_run_and_the_den_job()
    {
        // a level whose rung has no run: the ladder's own name, logged with the play host's line, and nothing is built
        var top = new Rig(output, level: 127);
        using (var climb = top.NewClimb())
        {
            Assert.Equal(ScenarioStatus.Failed, top.Run(climb));
            Assert.StartsWith("level 127 has no run on the ladder: ", climb.Failure, StringComparison.Ordinal);
            Assert.False(climb.Ok);
            Assert.Equal(AutopilotMode.Off, climb.Logic.Mode);
            Assert.Contains(top.Lines, l => l == "autopilot: " + climb.Failure);
            Assert.Empty(top.Kits.MadeOn);
            Assert.Equal(ScenarioStatus.Failed, climb.Step(top.View, top.World.Now)); // and it stays failed
        }

        // the den job has no rung to move to: its headless run is the one scenario of before
        var den = new Rig(output);
        Assert.Throws<ArgumentException>(() => new HeadlessClimbScenario(new AutopilotOptions(), den.Kits, den.World.Trip.Context().Log));
    }

    [Fact]
    public void A_refused_move_on_ends_the_climb_by_its_name()
    {
        // The dial cannot read what the run read (the hand-over's gate read is taken away): its own read keeps her on the ground the run left, which is the named loop.
        var rig = new Rig(output);
        using var climb = rig.NewClimb();
        climb.Logic.HandOverForTest = h => h with { GateStands = null };

        var status = rig.Run(climb);
        rig.Print();

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(climb.Ok);
        Assert.StartsWith(AutopilotHostLogic.MoveOnLoopStop + ": the run on holtburg-redoubt stopped with rung-moved:burrow to leave that ground", climb.Failure, StringComparison.Ordinal);
        Assert.Equal([Outskirts], rig.Kits.MadeOn); // no second run was built
        Assert.Equal(1, climb.Logic.UnattendedEnd!.ExitCode);
    }

    [Fact]
    public void A_hand_back_goes_through_the_one_carry_its_counts_are_kept_and_the_checkpoint_is_not_counted_on_top()
    {
        // The person's controls are the play host's; the carry they need is the shared copy (AutopilotRungKits.Scenario), reached here through the headless kit and the dial itself.
        var rig = new Rig(output);
        using var climb = rig.NewClimb();
        Assert.Equal(ScenarioStatus.Running, rig.Run(climb, () => rig.FightsOn(climb.Logic, Burrow, kills: 3)));
        var logic = climb.Logic;

        _ = logic.StopHere(rig.View, "/stop");
        Assert.Equal(AutopilotMode.Interrupted, logic.Mode);
        var old = logic.Scenario!;
        var kills = old.Totals.Kills + old.CycleTally.Kills; // the cut fight's kills are folded into the cycle
        Assert.True(old.CycleTally.Kills >= 3);
        Assert.NotNull(rig.Store.Read()); // the interrupt wrote the checkpoint

        logic.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        Assert.Equal(AutopilotMode.Running, logic.Mode);
        var resumed = logic.Scenario!;
        for (var i = 0; i < 4; i++)
        {
            rig.Frame(); // the fresh scenario's start: it reads its checkpoint store, a second of the session clock (no kill of a new fight yet)
            logic.Tick(rig.View, rig.World.Now);
        }

        rig.Print();
        Assert.Equal([Outskirts, Burrow, Burrow], rig.Kits.MadeOn); // the hand-back's scenario is on the kit in hand
        Assert.NotSame(old, resumed);
        Assert.Same(resumed, logic.Scenario);
        Assert.Equal((1, 1), (resumed.Totals.Interrupted + resumed.CycleTally.Interrupted, resumed.Totals.Resumed + resumed.CycleTally.Resumed));
        Assert.Equal(kills, resumed.Totals.Kills + resumed.CycleTally.Kills); // carried once: the file the interrupt wrote reads as empty to the hand-back's scenario (ColdReadOnceStore)
        Assert.Equal(old.Cycle, resumed.Cycle);
    }

    [Fact]
    public void The_play_host_builds_its_kits_through_the_clients_seam_and_keeps_only_what_needs_the_game()
    {
        // The Godot node cannot be built here: the cut is pinned in the source.
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        Assert.Contains("node._rungs = new AutopilotRungKits(logic, options, new RungKitSeam(node), GodotLogSink.Instance);", host, StringComparison.Ordinal);
        Assert.Contains("logic.MoveOn = node._rungs.MoveOnNext;", host, StringComparison.Ordinal);
        Assert.Contains("_rungs!.PrepareRung(session)", host, StringComparison.Ordinal);
        Assert.Contains("private sealed class RungKitSeam(AutopilotHost host) : IAutopilotKitFactory", host, StringComparison.Ordinal);
        Assert.Contains("AutopilotRungKits.Scenario(carry, _options, factory, context, _store, session)", host, StringComparison.Ordinal);

        // the orchestration is no longer the node's: it asks no ground, makes no plan, builds no scenario and carries nothing itself
        Assert.DoesNotContain("StartGround(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveOnGroundFor(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("_logic.UseGround(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new AutopilotScenario(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("carry.Apply(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("new ColdReadOnceStore(", host, StringComparison.Ordinal);

        // and the headless host reaches the level up job through the same client code
        var headless = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Client", "Session", "Hosting", "HeadlessAutopilotScenario.cs"));
        Assert.Contains("options.Job == AutopilotJob.LevelUp", headless, StringComparison.Ordinal);
        Assert.Contains("new HeadlessClimbScenario(options, kits, log, host.Tick)", headless, StringComparison.Ordinal);
        var options = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Headless", "HeadlessOptions.cs"));
        Assert.Contains("AutopilotHostLogic.PlanFor(autopilot)", options, StringComparison.Ordinal);
    }
}
