using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests.Animation;
using DatAnimData = DatReaderWriter.Types.AnimData;
using DatMotionCommandData = DatReaderWriter.Types.MotionCommandData;
using DatMotionData = DatReaderWriter.Types.MotionData;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// <see cref="MotionInterpreter"/> driving a real <see cref="MotionPlayer"/>: a small synthetic motion table (no dats)
/// and the human motion table 0x09000001 (with dats). The interpreter's pending motions stay in step with the motion
/// table's, completions flow back through <see cref="MotionTableManager.MotionDone"/>, and the speeds the interpreter
/// reports match what the animations move.
/// </summary>
public sealed class MotionInterpreterPlaybackTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanMotionTable = 0x09000001;

    [Fact]
    public void The_interpreter_and_the_motion_table_queue_the_same_motions()
    {
        var (motion, player, _) = Synthetic(FakeWeenie.RemoteCreature());
        AssertInStep(motion, player, expected: 2); // the style and Ready of the default state; the stops find nothing

        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));
        AssertInStep(motion, player, expected: 4);
        Assert.Equal(new[] { SyntheticMotionTable.ReadyToWalk, SyntheticMotionTable.Walk }, player.Sequence.Nodes.Select(n => n.AnimId).ToArray());
        Assert.Equal(new Vector3(0, 3.12f, 0), player.Sequence.Velocity); // the cycle's velocity × speed

        // The link's three frames end with an animation-done hook, which completes every queued motion.
        for (var i = 0; i < 4; i++)
        {
            player.Update(1.0 / 30.0);
        }

        AssertInStep(motion, player, expected: 0);
        Assert.Equal(SyntheticMotionTable.Walk, player.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(player.Sequence.Velocity, motion.GetStateVelocity());
    }

    [Fact]
    public void Leaving_the_ground_drops_the_links_completes_everything_and_falls()
    {
        var (motion, player, host) = Synthetic(FakeWeenie.RemoteCreature());
        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.WalkForward));
        Assert.True(player.Sequence.FirstCyclic!.Previous is not null);

        host.HasContact = false;
        host.SetOnWalkable(false);

        Assert.Equal(SyntheticMotionTable.Fall, player.Sequence.FirstCyclic!.Value.AnimId);
        Assert.Equal(MotionCommands.Falling, player.State.Substate);
        Assert.Equal(MotionCommands.WalkForward, motion.Interpreted.ForwardCommand); // recorded, to resume on landing
        AssertInStep(motion, player, expected: motion.PendingMotionCount);

        host.HasContact = true;
        host.SetOnWalkable(true);
        Assert.Equal(SyntheticMotionTable.Walk, player.Sequence.FirstCyclic!.Value.AnimId);
        Assert.Equal(MotionCommands.WalkForward, player.State.Substate);
    }

    [Fact]
    public void Completing_an_action_on_the_motion_table_removes_it_from_the_interpreter()
    {
        var (motion, player, host) = Synthetic(FakeWeenie.Player(), autonomous: true);
        player.CheckForCompletedMotions();
        Assert.Equal(0, motion.PendingMotionCount);

        Assert.Equal(WeenieError.None, motion.DoMotion(SyntheticMotionTable.Action, new MovementParameters()));
        Assert.Single(motion.Interpreted.Actions);
        Assert.Single(motion.Raw.Actions);
        AssertInStep(motion, player, expected: 1);

        for (var i = 0; i < 4; i++)
        {
            player.Update(1.0 / 30.0);
        }

        Assert.Empty(motion.Interpreted.Actions);
        Assert.Empty(motion.Raw.Actions);
        Assert.Equal(1, host.UnstickCount);
        AssertInStep(motion, player, expected: 0);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void The_human_walks_runs_and_stops_with_the_tables_cycles_and_runs_at_the_run_rate()
    {
        var source = new DatAnimationSource(fixture.Dats);
        var table = source.GetMotionTable(HumanMotionTable)!;
        var player = new MotionPlayer(table, source);
        var weenie = FakeWeenie.Player(runRate: 1.5f);
        var host = new FakeMotionHost { Weenie = weenie, MovementIsAutonomous = true };
        var motion = new MotionInterpreter(player, host);
        host.Interpreter = motion;
        motion.EnterDefaultState();
        Assert.Equal(CycleAnim(table, MotionCommands.Ready), player.Sequence.FirstCyclic!.Value.AnimId);

        motion.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        Assert.Equal(CycleAnim(table, MotionCommands.WalkForward), player.Sequence.FirstCyclic!.Value.AnimId);
        Assert.Equal(Cycle(table, MotionCommands.WalkForward).Velocity, player.Sequence.Velocity); // zero: root motion only
        AssertInStep(motion, player, motion.PendingMotionCount);
        Assert.Equal(AnimDistSpeed(source, table, MotionCommands.WalkForward), RootMotionSpeed(player), 0.05 * 3.12);

        motion.SetHoldRun(true, cancelMoveTo: false);
        Assert.Equal(CycleAnim(table, MotionCommands.RunForward), player.Sequence.FirstCyclic!.Value.AnimId);
        Assert.Equal(1.5f, player.State.SubstateMod);
        Assert.Equal(Cycle(table, MotionCommands.RunForward).Velocity * 1.5f, player.Sequence.Velocity);
        Assert.Equal(1.5 * AnimDistSpeed(source, table, MotionCommands.RunForward), RootMotionSpeed(player), 0.05 * 6.0);

        // The interpreter's speeds are retail's constants (walk 3.12, run 4 × the run rate), not the animations' own.
        Assert.Equal(6f, motion.GetStateVelocity().Y, 4);

        motion.StopMotion(MotionCommands.WalkForward, new MovementParameters());
        Assert.Equal(CycleAnim(table, MotionCommands.Ready), player.Sequence.FirstCyclic!.Value.AnimId);
        Assert.Equal(MotionCommands.Ready, motion.Raw.ForwardCommand);
        Assert.Equal(Vector3.Zero, motion.GetStateVelocity());
        for (var i = 0; i < 120; i++)
        {
            player.Update(1.0 / 60.0);
        }

        AssertInStep(motion, player, expected: 0);
    }

    private static (MotionInterpreter Motion, MotionPlayer Player, FakeMotionHost Host) Synthetic(FakeWeenie weenie, bool autonomous = false)
    {
        var (table, animations) = SyntheticMotionTable.Create();
        var player = new MotionPlayer(table, animations);
        var host = new FakeMotionHost { Weenie = weenie, MovementIsAutonomous = autonomous };
        var motion = new MotionInterpreter(player, host);
        host.Interpreter = motion;
        motion.EnterDefaultState();
        return (motion, player, host);
    }

    private static void AssertInStep(MotionInterpreter motion, MotionPlayer player, int expected)
    {
        Assert.Equal(expected, motion.PendingMotionCount);
        Assert.Equal(expected, player.PendingAnimationCount);
        Assert.Equal(expected, player.Manager.Pending.Count());
    }

    private static MotionData Cycle(MotionTableData table, uint motion) => table.Cycles[MotionTableData.Key(MotionCommands.NonCombat, motion)];

    /// <summary>The cycle's last animation, which <c>append_animation</c> leaves as the first cyclic node.</summary>
    private static uint CycleAnim(MotionTableData table, uint motion) => Cycle(table, motion).Anims[^1].AnimId;

    /// <summary>
    /// The speed a cycle's root motion moves at speed 1 (ACE MotionTable.GetAnimDist: the pos frames' summed origins over
    /// their count, times the framerate), as MotionSequenceTests measures it.
    /// </summary>
    private static double AnimDistSpeed(DatAnimationSource source, MotionTableData table, uint motion)
    {
        var cycle = Cycle(table, motion);
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var anim in cycle.Anims)
        {
            foreach (var pos in source.GetAnimation(anim.AnimId)!.PosFrames!)
            {
                sum += pos.Origin;
                count++;
            }
        }

        return sum.Length() / count * cycle.Anims[0].Framerate;
    }

    /// <summary>Lets the links finish, then measures the root motion's speed over two seconds.</summary>
    private static double RootMotionSpeed(MotionPlayer player)
    {
        for (var i = 0; i < 90; i++)
        {
            player.Update(1.0 / 60.0);
        }

        var frame = AcFrame.Identity;
        for (var i = 0; i < 120; i++)
        {
            frame = AcFrame.Combine(frame, player.Update(1.0 / 60.0).RootMotion);
        }

        return frame.Origin.Length() / 2.0;
    }
}

/// <summary>
/// A NonCombat-only motion table built in code: Ready, WalkForward (3.12 m/s), RunForward (4 m/s) and Falling cycles, a
/// Ready↔WalkForward link each way, and one action linked from Ready.
/// </summary>
internal static class SyntheticMotionTable
{
    public const uint Ready = 0x03FF0001;
    public const uint Walk = 0x03FF0002;
    public const uint Run = 0x03FF0003;
    public const uint Fall = 0x03FF0004;
    public const uint ReadyToWalk = 0x03FF0005;
    public const uint WalkToReady = 0x03FF0006;
    public const uint ActionAnim = 0x03FF0007;

    /// <summary>MagicPowerUp01, as the table's one action.</summary>
    public const uint Action = MotionCommands.MagicPowerUp01;

    public static (MotionTableData Table, IAnimationSource Animations) Create()
    {
        var animations = new SequencePlaybackTests.FakeAnimations()
            .Add(Ready, 2).Add(Walk, 4).Add(Run, 4).Add(Fall, 2).Add(ReadyToWalk, 3).Add(WalkToReady, 3).Add(ActionAnim, 3);
        var style = MotionCommands.NonCombat;
        var table = new DatMotionTable { Id = 0x09FFFF01, DefaultStyle = (MotionCommand)style };
        table.StyleDefaults[(MotionCommand)style] = (MotionCommand)MotionCommands.Ready;
        table.Cycles[Key(style, MotionCommands.Ready)] = Data(Ready);
        table.Cycles[Key(style, MotionCommands.WalkForward)] = Data(Walk, new Vector3(0, 3.12f, 0));
        table.Cycles[Key(style, MotionCommands.RunForward)] = Data(Run, new Vector3(0, 4f, 0));
        table.Cycles[Key(style, MotionCommands.Falling)] = Data(Fall);
        table.Links[Key(style, MotionCommands.Ready)] = new DatMotionCommandData
        {
            MotionData =
            {
                [unchecked((int)MotionCommands.WalkForward)] = Data(ReadyToWalk),
                [unchecked((int)Action)] = Data(ActionAnim),
            },
        };
        table.Links[Key(style, MotionCommands.WalkForward)] = new DatMotionCommandData
        {
            MotionData = { [unchecked((int)MotionCommands.Ready)] = Data(WalkToReady) },
        };
        return (MotionTableData.FromDat(table), animations);
    }

    private static int Key(uint style, uint motion) => unchecked((int)MotionTableData.Key(style, motion));

    private static DatMotionData Data(uint anim, Vector3 velocity = default) => new()
    {
        Anims = { new DatAnimData { AnimId = anim, LowFrame = 0, HighFrame = -1, Framerate = 30f } },
        Velocity = velocity,
    };
}
