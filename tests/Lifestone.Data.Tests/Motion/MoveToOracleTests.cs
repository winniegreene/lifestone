using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// <see cref="MoveToManager"/> against <see cref="AceMoveToOracle"/> (ACE's MoveToManager, test-only): the same
/// movements on two identical kinematic bodies end in the same place facing the same way; each difference between ACE
/// and retail has its own test.
/// </summary>
public sealed class MoveToOracleTests
{
    private const double Dt = 1.0 / 30.0;

    [Fact]
    public void Seeded_moves_to_positions_and_turns_end_where_aces_do()
    {
        var random = new Random(20261012);
        for (var scenario = 0; scenario < 120; scenario++)
        {
            var start = MoverHost.At(80 + (float)(random.NextDouble() * 20), 80 + (float)(random.NextDouble() * 20), (float)(random.NextDouble() * 360));
            var angle = random.NextDouble() * Math.PI * 2;
            var distance = 0.3 + (random.NextDouble() * 35);
            var target = MoverHost.At(start.Origin.X + (float)(Math.Cos(angle) * distance), start.Origin.Y + (float)(Math.Sin(angle) * distance));
            var flags = MovementParameterFlags.MoveTowards | MovementParameterFlags.CanWalk;
            if (random.Next(2) == 0)
            {
                flags |= MovementParameterFlags.CanRun;
            }

            var parameters = new MovementParameters
            {
                Flags = flags,
                DistanceToObject = 0.3f + (float)random.NextDouble(),
                WalkRunThreshold = 5f + (float)(random.NextDouble() * 10),
                DesiredHeading = (float)(random.NextDouble() * 360),
            };
            var turn = random.Next(4) == 0;

            var (ours, ace) = Pair(start);
            if (turn)
            {
                ours.Movement.TurnToHeading(parameters);
                ace.Oracle!.PerformMovement(WireMovementType.TurnToHeading, 0, 0, 0, 0, default, parameters);
            }
            else
            {
                ours.Movement.MakeMoveToManager().PerformMoveToPosition(target, parameters);
                ace.Oracle!.PerformMovement(WireMovementType.MoveToPosition, 0, 0, 0, 0, target, parameters);
            }

            RunBoth(ours, ace);
            var where = $"scenario {scenario} ({(turn ? "turn" : "move")}, {distance:0.0} m)";
            Assert.True(PositionFrames.Distance(ours.Position, ace.Position) < 0.35f, $"{where}: {ours.Position} vs {ace.Position}");
            Assert.True(HeadingGap(ours.Heading, ace.Heading) < 1f, $"{where}: heading {ours.Heading} vs {ace.Heading}");
            Assert.Equal(MotionCommands.Ready, ours.Motion.Interpreted.ForwardCommand);
            Assert.Equal(MotionCommands.Ready, ace.Motion.Interpreted.ForwardCommand);
        }
    }

    [Fact]
    public void Moving_to_a_moving_object_ends_beside_it_as_aces_does()
    {
        var world = new TestWorld();
        var ours = new MoverHost(1, world, MoverHost.At(50, 50));
        var ace = new MoverHost(3, world, MoverHost.At(50, 50));
        ace.Oracle = new AceMoveToOracle(ace);
        var target = new MoverHost(2, world, MoverHost.At(58, 62)) { Drift = new Vector3(1f, 0, 0) };
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.CanRun | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseSpheres };
        ours.Movement.MoveToObject(2, parameters);
        ace.Oracle.PerformMovement(WireMovementType.MoveToObject, 2, 2, target.Radius, target.Height, default, parameters);
        (double Time, float Gap)? oursDone = null, aceDone = null;
        for (var i = 0; i < 1200 && (oursDone is null || aceDone is null); i++)
        {
            target.Tick(Dt);
            ours.Tick(Dt);
            ace.Tick(Dt);
            oursDone ??= ours.Movement.IsMovingTo ? null : (ours.Time, ReportedGap(ours, target));
            aceDone ??= ace.Oracle.is_moving_to() ? null : (ace.Time, ReportedGap(ace, target));
        }

        // Each stops within reach of the target's last report. (The reports themselves lag: the target predicts its
        // position the voyeur's quantum ahead, and as the chaser closes in the quantum shrinks about as fast as the
        // target walks, so the prediction stands still and no new report comes; retail's server sends a new MoveTo.)
        Assert.NotNull(oursDone);
        Assert.NotNull(aceDone);
        Assert.True(oursDone.Value.Gap <= 0.6f + 0.15f, $"ours {oursDone}");
        Assert.True(aceDone.Value.Gap <= 0.6f + 0.15f, $"ACE {aceDone}");
        Assert.True(ours.Position.Origin.X > 55f && ace.Position.Origin.X > 55f);
        Assert.Equal([WeenieError.None], ace.OracleCompletions);
    }

    [Fact]
    public void Retail_begins_a_turn_to_heading_at_once_ace_on_the_next_update()
    {
        var (ours, ace) = Pair(MoverHost.At(50, 50, 0f));
        // Without StopCompletely no motion is pending, so retail's BeginTurnToHeading issues the turn inside the call.
        var parameters = new MovementParameters { DesiredHeading = 90f }.WithFlag(MovementParameterFlags.StopCompletely, false);
        ours.Movement.TurnToHeading(parameters);
        ace.Oracle!.PerformMovement(WireMovementType.TurnToHeading, 0, 0, 0, 0, default, parameters);
        Assert.Equal(MotionCommands.TurnRight, ours.Motion.Interpreted.TurnCommand);
        Assert.Equal(0u, ace.Motion.Interpreted.TurnCommand);
        ace.Tick(Dt);
        Assert.Equal(MotionCommands.TurnRight, ace.Motion.Interpreted.TurnCommand);
    }

    [Fact]
    public void Retail_turns_to_the_final_heading_of_a_move_to_position_ace_reads_the_previous_movements_flag()
    {
        var (ours, ace) = Pair(MoverHost.At(50, 50, 0f));
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards | MovementParameterFlags.UseFinalHeading,
            DesiredHeading = 270f,
        };
        var target = MoverHost.At(50, 55);
        ours.Movement.MakeMoveToManager().PerformMoveToPosition(target, parameters);
        ace.Oracle!.PerformMovement(WireMovementType.MoveToPosition, 0, 0, 0, 0, target, parameters);
        RunBoth(ours, ace);
        Assert.Equal(270f, ours.Heading, 1);
        Assert.Equal(0f, ace.Heading, 1); // ACE read UseFinalHeading from the parameters it held before (none)
    }

    [Fact]
    public void Retail_runs_when_the_parameters_allow_charging_ace_ignores_the_flag()
    {
        var p = new MovementParameters { Flags = MovementParameterFlags.CanCharge | MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards };
        var away = false;
        p.GetCommand(2f, 0f, out _, out var ours, ref away);
        uint motion = 0;
        var ace = HoldKey.Invalid;
        AceMoveToOracle.GetCommand(p, 2f, 0f, ref motion, ref ace, ref away);
        Assert.Equal(HoldKey.Run, ours);
        Assert.Equal(HoldKey.None, ace);
    }

    [Fact]
    public void Retail_clears_the_parameters_flags_when_it_cleans_up_ace_zeroes_the_distance()
    {
        var (ours, ace) = Pair(MoverHost.At(50, 50, 0f));
        var parameters = new MovementParameters { Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards, DistanceToObject = 1.5f };
        ours.Movement.MakeMoveToManager().PerformMoveToPosition(MoverHost.At(50, 51), parameters);
        ace.Oracle!.PerformMovement(WireMovementType.MoveToPosition, 0, 0, 0, 0, MoverHost.At(50, 51), parameters);
        RunBoth(ours, ace);
        Assert.Equal((MovementParameterFlags.None, 1.5f), (ours.Movement.MoveTo!.MovementParams.Flags, ours.Movement.MoveTo.MovementParams.DistanceToObject));
        Assert.Equal((parameters.Flags, 0f), (ace.Oracle.MovementParams.Flags, ace.Oracle.MovementParams.DistanceToObject));
    }

    [Fact]
    public void Retail_measures_cylinders_by_the_gap_when_one_is_above_the_other_ace_by_the_reach()
    {
        var low = new CellFrame(0xA9B40019, new AcFrame(new Vector3(10, 10, 0), Quaternion.Identity));
        var high = new CellFrame(0xA9B40019, new AcFrame(new Vector3(10.2f, 10, 5), Quaternion.Identity));
        // The reach (the 3-D distance less both radii, 5.004 − 6) is negative while the lower top is 3 m below the
        // higher base: retail measures the gap, ACE the reach.
        Assert.Equal(3f, PositionFrames.CylinderDistance(3f, 2f, low, 3f, 2f, high), 4);
        Assert.Equal(Math.Sqrt((0.2 * 0.2) + 25) - 6, AceMoveToOracle.CylinderDistance(3f, 2f, low, 3f, 2f, high), 3);
    }

    private static float ReportedGap(MoverHost mover, MoverHost target) =>
        PositionFrames.CylinderDistance(mover.Radius, mover.Height, mover.Position, target.Radius, target.Height, mover.Reports[^1].TargetPosition);

    private static (MoverHost Ours, MoverHost Ace) Pair(CellFrame start)
    {
        var ours = new MoverHost(1, new TestWorld(), start);
        var ace = new MoverHost(1, new TestWorld(), start);
        ace.Oracle = new AceMoveToOracle(ace);
        return (ours, ace);
    }

    private static void RunBoth(MoverHost ours, MoverHost ace)
    {
        for (var i = 0; i < 3000 && (ours.Movement.IsMovingTo || ace.Oracle!.is_moving_to()); i++)
        {
            ours.Tick(Dt);
            ace.Tick(Dt);
        }

        Assert.False(ours.Movement.IsMovingTo, "Lifestone's MoveTo did not finish");
        Assert.False(ace.Oracle!.is_moving_to(), "ACE's MoveTo did not finish");
    }

    private static float HeadingGap(float a, float b)
    {
        var d = Math.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }
}
