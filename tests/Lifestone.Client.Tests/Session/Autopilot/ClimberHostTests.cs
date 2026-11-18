using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 0 a6: the play host's wiring of Winnie's level up job as the host logic's own rules (the Godot host cannot be unit-tested). The plan, the required character and the checkpoint file by job (the den job's are unchanged); the ground of the first
/// press from the ladder's rung for the character's level, with the ladder's own name when the rung has no run; the move-on (<c>stop=rung-moved:*</c>): the dial restarts once by itself on the next rung, waits for a host that is still reading, or stops by
/// name when the host refuses, with exit code 1 for an unattended run; the den job never moves on.
/// </summary>
public sealed class ClimberHostTests(ITestOutputHelper output)
{
    private static AutopilotOptions LevelUp => AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up"]));

    [Fact]
    public void The_plan_the_character_and_the_checkpoint_file_follow_the_job_and_the_dens_are_unchanged()
    {
        var den = new AutopilotOptions();

        Assert.Same(JourneyPlans.Mage, AutopilotHostLogic.PlanFor(den));
        Assert.Equal("BSD Mage", AutopilotHostLogic.PlanFor(den).Character);
        var plan = AutopilotHostLogic.PlanFor(LevelUp);
        Assert.Same(JourneyPlans.Climber, plan);
        Assert.Equal((AutopilotOptions.LadderCharacter, "test"), (plan.Character, plan.Account));
        Assert.Equal(AutopilotHostLogic.RequiredCharacter(LevelUp), plan.Character); // the plan's character is the required one, not plan.Character of the mage
        Assert.Equal("winnie.json", AutopilotHostOptions.CheckpointFileName(plan.Character));
        Assert.NotEqual(AutopilotHostOptions.CheckpointFileName(AutopilotHostLogic.PlanFor(den).Character), AutopilotHostOptions.CheckpointFileName(plan.Character));
        // A dungeon rung makes the plan's ground the den (its rooms are read), as the mage plan's is; an outdoor rung keeps the climber's.
        Assert.Equal(StationWhere.Den, AutopilotHostLogic.PlanFor(LevelUp, GroundCatalog.Get(FightOptions.DefaultGround)).Ground);
        Assert.Equal(JourneyPlans.Climber.Ground, AutopilotHostLogic.PlanFor(LevelUp, GroundCatalog.Get("low-b-belt")).Ground);
        Assert.Same(JourneyPlans.Mage, AutopilotHostLogic.PlanFor(den, GroundCatalog.Get("low-b-belt")));
    }

    [Theory]
    [InlineData(0, "rung-level-unknown")]
    [InlineData(127, "past the ladder's top")]
    public void A_level_whose_rung_has_no_run_gives_no_ground_and_the_ladders_own_reason(int level, string reason)
    {
        var ground = AutopilotHostLogic.LevelUpGround(level, out var why);

        Assert.Null(ground);
        Assert.Contains(reason, why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_level_with_a_run_gets_the_rungs_ground_from_the_ladder_and_nothing_else()
    {
        foreach (var level in Enumerable.Range(1, LadderLoader.WarMage.TopLevel))
        {
            var ground = AutopilotHostLogic.LevelUpGround(level, out var why);
            var expected = AutopilotScenario.LadderGroundFor(level, out var expectedWhy, _ => true); // M6 0 lead: a quest rung's level is built on the next rung, provisionally

            Assert.Equal(expected?.Key, ground?.Key);
            Assert.EndsWith(expectedWhy, why, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_move_on_rule_is_the_level_up_jobs_ok_rung_moved_stop_only()
    {
        Assert.True(AutopilotHostLogic.IsMoveOn(LevelUp, "rung-moved:outskirts", ok: true));
        Assert.False(AutopilotHostLogic.IsMoveOn(LevelUp, "rung-moved:outskirts", ok: false));
        Assert.False(AutopilotHostLogic.IsMoveOn(LevelUp, "rung-pending:burrow", ok: false));
        Assert.False(AutopilotHostLogic.IsMoveOn(LevelUp, "ladder-top", ok: true));
        Assert.False(AutopilotHostLogic.IsMoveOn(LevelUp, null, ok: true));
        Assert.False(AutopilotHostLogic.IsMoveOn(new AutopilotOptions(), "rung-moved:outskirts", ok: true)); // the den job never moves on
    }

    private sealed class Rig
    {
        private readonly List<string> _stopped = [];

        public Rig(ITestOutputHelper output, Func<int, MoveOnAnswer> answer)
        {
            World = new ApWorld(Den, output, atHome: false);
            World.Level(127); // past the top: the run ends ok at its first cycle, standing in for the move-on's stop (MoveOnStop is narrowed to it)
            var options = new AutopilotOptions { Job = AutopilotJob.LevelUp };
            Logic = new AutopilotHostLogic(
                carry =>
                {
                    var scenario = new AutopilotScenario(carry.Remaining(options), World.Factory, World.Trip.Context(), null, null, carry.Seed);
                    carry.Apply(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground)
            {
                MoveOnStop = stop => stop == "ladder-top",
                MoveOn = _ => answer(++Asked),
            };
            Logic.Stopped += _stopped.Add;
        }

        public ApWorld World { get; }

        public AutopilotHostLogic Logic { get; }

        public int Asked { get; private set; }

        public IReadOnlyList<string> Stopped => _stopped;

        public IGameSessionView View => World.Trip.View;

        public void RunUntilDone(int frames = 4000)
        {
            for (var i = 0; i < frames && Logic.Mode != AutopilotMode.Done; i++)
            {
                var t = World.Trip;
                t.Vendor.R.Tick(0.25);
                t.Shops.Pump();
                t.Xp.Pump();
                t.Vendor.R.Tick();
                Logic.Tick(View, World.Now);
            }
        }
    }

    [Fact]
    public void A_move_on_restarts_the_dial_once_by_itself_and_the_same_stop_straight_after_it_is_the_named_loop()
    {
        // M6 item 14 (live, 2026-11-12: 33 restarts in 15 s): the restarted run stops the same way in its first frame, on the same ground. Before, the hook was asked again (and here refused); now the dial's own guard stops it by name.
        var rig = new Rig(output, _ => MoveOnAnswer.Go);
        rig.Logic.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        rig.RunUntilDone();

        Assert.Equal(AutopilotMode.Done, rig.Logic.Mode);
        Assert.Equal((1, 1), (rig.Asked, rig.Logic.MoveOns)); // the first stop restarted the dial; the second is a loop, and the hook is not asked for it
        Assert.StartsWith(AutopilotHostLogic.MoveOnLoopStop + ": the run that move-on restart 1 started on ", rig.Logic.MoveOnRefusal, StringComparison.Ordinal);
        Assert.Equal([AutopilotHostLogic.MoveOnLoopStop], rig.Stopped); // raised once, only for the refusal: the restart is no stop
        Assert.Null(rig.Logic.UnattendedEnd); // an attended run has no end of the game
        Assert.Contains(rig.World.Lines, l => l.Contains("ladder: move-on restart: check before: the run ended with ladder-top (ok)", StringComparison.Ordinal));
        Assert.Contains(rig.World.Lines, l => l.Contains("ladder: move-on restart: act: the next rung's parts are ready; the dial starts the next run now (restart 1)", StringComparison.Ordinal));
        Assert.Contains(rig.World.Lines, l => l.Contains("ladder: move-on restart: REFUSED after ladder-top", StringComparison.Ordinal));
        Assert.Single(rig.World.Lines, l => l.StartsWith("autopilot host: stop=failed:move-on-loop: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_waiting_host_leaves_the_dial_done_and_neither_stops_nor_ends_an_unattended_run()
    {
        var rig = new Rig(output, _ => MoveOnAnswer.Wait);
        rig.Logic.Toggle(rig.View, startedBy: AutopilotHost_StartOption);
        Assert.True(rig.Logic.StartedByOption);
        rig.RunUntilDone();

        Assert.Equal((1, 1), (rig.Asked, rig.Logic.MoveOns));
        Assert.Empty(rig.Stopped);
        Assert.Null(rig.Logic.UnattendedEnd); // the host presses the dial when its parts are read; the unattended run stays unattended (StartedByOption)
        Assert.True(rig.Logic.StartedByOption);
        Assert.Null(rig.Logic.MoveOnRefusal);
    }

    [Fact]
    public void A_refused_move_on_of_an_unattended_run_ends_the_game_with_exit_code_1_by_name()
    {
        var rig = new Rig(output, _ => MoveOnAnswer.Refused("failed:rung-restart-refused: the next rung at level 127 has no run"));
        rig.Logic.Toggle(rig.View, startedBy: AutopilotHost_StartOption);
        rig.RunUntilDone();

        Assert.Equal(0, rig.Logic.MoveOns);
        Assert.Equal(1, rig.Logic.UnattendedEnd!.ExitCode);
        Assert.Contains("refused", rig.Logic.UnattendedEnd.Decision, StringComparison.Ordinal);
        Assert.Equal(["failed:rung-restart-refused"], rig.Stopped);
        Assert.Contains("rung-restart-refused", rig.Logic.Status.LastDecision, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_host_hook_a_move_on_is_refused_by_name_and_never_a_silent_halt()
    {
        var rig = new Rig(output, _ => MoveOnAnswer.Go);
        rig.Logic.MoveOn = null;
        rig.Logic.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
        rig.RunUntilDone();

        Assert.Equal(0, rig.Logic.MoveOns);
        Assert.Contains("no-host-hook", rig.Logic.MoveOnRefusal, StringComparison.Ordinal);
        Assert.Equal(["failed:rung-restart-refused"], rig.Stopped);
    }

    [Fact]
    public void A_run_that_is_not_a_move_on_never_asks_the_host_and_ends_as_before()
    {
        var rig = new Rig(output, _ => MoveOnAnswer.Go);
        rig.Logic.MoveOnStop = _ => false; // the stop is ladder-top, an ok end of the climb, not a move-on
        rig.Logic.Toggle(rig.View, startedBy: AutopilotHost_StartOption);
        rig.RunUntilDone();

        Assert.Equal(0, rig.Asked);
        Assert.Equal(["ladder-top"], rig.Stopped);
        Assert.Equal(0, rig.Logic.UnattendedEnd!.ExitCode); // ok=yes: the unattended end is as M5's
    }

    [Fact]
    public void The_move_on_goes_to_the_rung_the_run_named_not_to_the_level_band()
    {
        Assert.Equal("burrow", RungOwner.NamedRung(LadderLoader.WarMage, "rung-moved:burrow"));
        Assert.Equal("lodge", RungOwner.NamedRung(LadderLoader.WarMage, "rung-moved:lodge"));
        Assert.Null(RungOwner.NamedRung(LadderLoader.WarMage, "rung-moved:no-such-rung"));
        Assert.Null(RungOwner.NamedRung(LadderLoader.WarMage, "level"));
        Assert.Null(RungOwner.NamedRung(LadderLoader.WarMage, null));
    }

    private const string AutopilotHost_StartOption = "--autopilot-start";
}
