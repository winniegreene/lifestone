using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// <c>MovementManager::unpack_movement</c> (AV8, AV14): the stance, the interpreted state, the sticky object and the
/// standing long jump of type 0; the MoveTo and TurnTo bodies of types 6-9; unknown words apply nothing.
/// </summary>
public sealed class MovementManagerTests
{
    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);
    private static readonly ushort HandCombat = MotionCommandTable.ToWire(MotionCommands.HandCombat);

    [Fact]
    public void Type_0_plays_a_new_stance_first_then_the_state_the_stick_and_the_long_jump_flag()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10));
        _ = new MoverHost(2, world, MoverHost.At(11, 10));
        var state = new WireInterpretedMotion(HandCombat, MotionCommandTable.ToWire(MotionCommands.WalkForward), null, null, null, null, null);
        var movement = new WireMovement(WireMovementType.InterpretedMotion, HandCombat, StickToObject: true, StandingLongJump: true, state, StickyObjectId: 2);
        var result = mover.Movement.UnpackMovement(movement, [new WireMotionItem(MotionCommandTable.ToWire(MotionCommands.MagicPowerUp01), 3, false, 1f)], new InterpretedMotionState(), out _);
        Assert.Equal(MovementUnpackResult.Applied, result);
        Assert.Equal("do HandCombat", mover.Commands()[0]); // the stance differed from NonCombat

        // Twice: the stance's own DoMotion, then apply_interpreted_movement's style call.
        Assert.Equal(2, mover.Calls.Count(c => c.Kind == "do" && c.Motion == MotionCommands.HandCombat));
        Assert.Equal(MotionCommands.WalkForward, mover.Motion.Interpreted.ForwardCommand);
        Assert.Contains("do MagicPowerUp01", mover.Commands());
        Assert.Equal(2u, mover.Positions.GetStickyObjectId());
        Assert.True(mover.Motion.StandingLongJump);
    }

    [Fact]
    public void The_same_stance_is_not_played_again()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10));
        var movement = WireMovement.Interpreted(NonCombat, new WireInterpretedMotion(null, null, null, null, null, null, null));
        Assert.Equal(MovementUnpackResult.Applied, mover.Movement.UnpackMovement(movement, [], new InterpretedMotionState(), out _));

        // Once: apply_interpreted_movement's own style call; unpack_movement plays no stance DoMotion before it.
        Assert.Single(mover.Calls, c => c.Kind == "do" && c.Motion == MotionCommands.NonCombat);
        Assert.False(mover.Motion.StandingLongJump);
    }

    [Fact]
    public void An_unknown_word_applies_nothing_and_names_itself()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10));
        var bad = WireMovement.Interpreted(0xFFF1, default);
        Assert.Equal(MovementUnpackResult.UnknownCommand, mover.Movement.UnpackMovement(bad, [], new InterpretedMotionState(), out var failure));
        Assert.Equal((WireMotionField.Style, (ushort)0xFFF1), (failure.Field, failure.LowWord));
        var badAction = WireMovement.Interpreted(NonCombat, default);
        Assert.Equal(MovementUnpackResult.UnknownCommand, mover.Movement.UnpackMovement(badAction, [new WireMotionItem(0xFFF2, 1, false, 1f)], new InterpretedMotionState(), out failure));
        Assert.Equal(WireMotionField.Action, failure.Field);
        Assert.Empty(mover.Calls);
        Assert.Equal(MovementUnpackResult.UnknownType, mover.Movement.UnpackMovement(new WireMovement((WireMovementType)3, NonCombat, false, false, default), [], new InterpretedMotionState(), out _));
        Assert.Equal(MovementUnpackResult.MissingBody, mover.Movement.UnpackMovement(new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default), [], new InterpretedMotionState(), out _));
    }

    [Fact]
    public void A_moveto_object_the_client_does_not_know_moves_to_the_origin_given_at_the_run_rate_given()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10));
        var origin = MoverHost.At(10, 30);
        var moveTo = new WireMoveTo(0x55, origin, new MovementParameters(), RunRate: 1.8f);
        var movement = new WireMovement(WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: moveTo);
        Assert.Equal(MovementUnpackResult.Applied, mover.Movement.UnpackMovement(movement, [], new InterpretedMotionState(), out _));
        Assert.Equal(1.8f, mover.Motion.MyRunRate);
        Assert.Equal(WireMovementType.MoveToPosition, mover.Movement.MoveTo!.MovementType);
        Assert.Equal(origin, mover.Movement.MoveTo.SoughtPosition);
    }

    [Fact]
    public void A_moveto_object_the_client_knows_asks_it_for_reports()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10));
        _ = new MoverHost(2, world, MoverHost.At(10, 20));
        var movement = new WireMovement(WireMovementType.MoveToObject, NonCombat, false, false, default, MoveTo: new WireMoveTo(2, MoverHost.At(0, 0), new MovementParameters(), 1f));
        mover.Movement.UnpackMovement(movement, [], new InterpretedMotionState(), out _);
        Assert.Equal(WireMovementType.MoveToObject, mover.Movement.MoveTo!.MovementType);
        Assert.Equal(2u, mover.Movement.MoveTo.TopLevelObjectId);
        Assert.True(mover.Movement.MoveTo.Initialized);
    }

    [Fact]
    public void A_turnto_object_the_client_does_not_know_turns_to_the_heading_given()
    {
        var world = new TestWorld();
        var mover = new MoverHost(1, world, MoverHost.At(10, 10, heading: 0f));
        var movement = new WireMovement(WireMovementType.TurnToObject, NonCombat, false, false, default, TurnTo: new WireTurnTo(0x55, 135f, new MovementParameters { DesiredHeading = 10f }));
        Assert.Equal(MovementUnpackResult.Applied, mover.Movement.UnpackMovement(movement, [], new InterpretedMotionState(), out _));
        Assert.Equal(WireMovementType.TurnToHeading, mover.Movement.MoveTo!.MovementType);
        Assert.Equal(new MoveToNode(MoveToNodeType.TurnToHeading, 135f), mover.Movement.MoveTo.PendingActions[0]);

        var turn = new WireMovement(WireMovementType.TurnToHeading, NonCombat, false, false, default, TurnTo: new WireTurnTo(0, 0f, new MovementParameters { DesiredHeading = 300f }));
        mover.Movement.UnpackMovement(turn, [], new InterpretedMotionState(), out _);
        Assert.Equal(new MoveToNode(MoveToNodeType.TurnToHeading, 300f), mover.Movement.MoveTo.PendingActions[0]);
    }
}
