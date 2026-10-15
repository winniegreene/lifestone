using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Movement;
using Lifestone.Client.Tests.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Movement.MovementTestKit;

namespace Lifestone.Client.Tests.Movement;

/// <summary>
/// The player's <see cref="PhysicsObj"/> as the avatars track's player body (PH6 after C-avatars): it takes the server's
/// movements for the player (<see cref="IPlayerMovementBody"/>; a MoveTo after an out-of-range Use turns and walks it,
/// m2-plan DoD 9) and is the player other objects target (<see cref="ITargetable"/>). Synthetic: the flat world and the
/// controller kit's table, whose TurnRight modifier turns at 90 degrees a second.
/// </summary>
public sealed class PhysicsPlayerBodyTests(ITestOutputHelper output)
{
    private const uint PlayerGuid = 0x50000001;
    private const uint CreatureGuid = 0x80000101;

    private static readonly ushort NonCombat = MotionCommandTable.ToWire(MotionCommands.NonCombat);

    [Fact]
    public void The_body_is_the_physics_object_as_a_movement_body_and_a_target()
    {
        var world = WorldOf(Flat(Block));
        var physics = Player(world, At(Block, 96f, 96f, 0f));
        var body = new PhysicsPlayerBody(physics);

        Assert.IsAssignableFrom<IPlayerMovementBody>(body);
        Assert.Same(physics.Motion, body.Motion);
        Assert.Same(physics.Movement, body.Movement);
        Assert.Same(physics.Targets, body.Targets);
        Assert.Equal(PlayerGuid, body.Id);
        Assert.Equal(PlayerGuid, body.TopLevelId);
        Assert.Equal(0.48f, body.Radius);
        Assert.Equal(1.8f, body.Height);
        Assert.Equal(world.CurrentTime, body.Time);
        Assert.Equal(physics.Position, body.Position);
    }

    [Fact]
    public void A_server_move_to_position_turns_the_player_and_walks_it_there()
    {
        var world = WorldOf(Flat(Block));
        var physics = Player(world, At(Block, 96f, 96f, 0f));
        var body = new PhysicsPlayerBody(physics);
        var target = At(Block, 102f, 102f, 0f); // north-east: heading 45

        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(MoveTo(target), [], autonomous: false, out _));
        Assert.False(physics.LastMoveWasAutonomous);
        Assert.True(physics.IsMovingTo);
        Assert.Equal(MotionCommands.TurnRight, physics.Motion!.Interpreted.TurnCommand);

        var seconds = RunWhileMovingTo(physics, 8.0);
        var left = PositionFrames.Distance(target, physics.Position);
        output.WriteLine($"arrived after {seconds:F2} s, {left:F3} m short, heading {physics.Heading:F2}, at {physics.Position}");
        Assert.False(physics.IsMovingTo);
        Assert.InRange(left, 0f, 0.6f + 0.15f);
        Assert.InRange(physics.Heading, 45f - 5f, 45f + 5f);
        Assert.Equal(MotionCommands.Ready, physics.Motion.Interpreted.ForwardCommand);
        Assert.Equal(0u, physics.Motion.Interpreted.TurnCommand);
        Assert.True(physics.OnWalkable);
    }

    [Fact]
    public void A_server_turn_to_heading_turns_the_player_to_it()
    {
        var world = WorldOf(Flat(Block));
        var physics = Player(world, At(Block, 96f, 96f, 0f));
        var body = new PhysicsPlayerBody(physics);
        var turn = new WireMovement(
            WireMovementType.TurnToHeading, NonCombat, false, false, default, TurnTo: new WireTurnTo(0, 0f, MovementParameters.FromTurnToWire(0, 1f, 90f)));

        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(turn, [], autonomous: false, out _));
        Assert.True(physics.IsMovingTo);
        var seconds = RunWhileMovingTo(physics, 4.0);

        output.WriteLine($"turned in {seconds:F2} s to {physics.Heading:F3}");
        Assert.False(physics.IsMovingTo);
        Assert.Equal(90f, physics.Heading, 2);
        Assert.InRange(seconds, 0.9, 1.5); // a quarter turn at 90 degrees a second, then the snap
        Assert.Equal(0u, physics.Motion!.Interpreted.TurnCommand);
    }

    [Fact]
    public void While_the_servers_move_to_runs_a_held_key_does_not_take_control_back()
    {
        var (controller, _, world) = FlatController();
        var physics = controller.Player;
        var body = new PhysicsPlayerBody(physics);
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 0.5);
        Assert.False(controller.ControlledByServer);

        // A non-autonomous UpdateMotion for the player: the host hands it to the body and gives control to the server.
        var target = At(Block, physics.Position.Frame.Origin.X, physics.Position.Frame.Origin.Y + 5f, 0f);
        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(MoveTo(target), [], autonomous: false, out _));
        controller.LoseControlToServer();
        Run(controller, 0.5);
        Assert.True(physics.IsMovingTo, "the held key did not cancel the server's MoveTo");
        Assert.True(controller.ControlledByServer);

        // CommandInterpreter::UseTime takes control back once the MoveTo is done and a key is still held.
        var now = Run(controller, 3.0);
        output.WriteLine($"at {now:F2} s: moving to {physics.IsMovingTo}, server control {controller.ControlledByServer}, {physics.Position}");
        Assert.False(physics.IsMovingTo);
        Assert.False(controller.ControlledByServer);
        Assert.Equal(MotionCommands.WalkForward, physics.Motion!.Interpreted.ForwardCommand);
        Assert.True(world.CurrentTime > 3.0);
    }

    [Fact]
    public void With_a_server_control_the_body_takes_control_from_the_keys_for_a_move_to_and_not_for_an_emote()
    {
        var (controller, _, _) = FlatController();
        var physics = controller.Player;
        var body = new PhysicsPlayerBody(physics) { ServerControl = controller };
        controller.HandleCommand(MotionCommands.WalkForward, start: true);
        Run(controller, 0.3);
        Assert.False(controller.ControlledByServer);

        // An interpreted motion (an emote) is not a server walk: the keys keep control.
        var emote = WireMovement.Interpreted(NonCombat, default);
        body.ApplyServerMovement(emote, [], autonomous: false, out _);
        Assert.False(controller.ControlledByServer);

        // A MoveTo is: ACSmartBox::DispatchSmartBoxEvent loses control to the server first, with no call from the host.
        var target = At(Block, physics.Position.Frame.Origin.X, physics.Position.Frame.Origin.Y + 5f, 0f);
        Assert.Equal(MovementUnpackResult.Applied, body.ApplyServerMovement(MoveTo(target), [], autonomous: false, out _));
        Assert.True(controller.ControlledByServer);
        Run(controller, 0.5);
        Assert.True(physics.IsMovingTo, "the held key did not cancel the server's MoveTo");
    }

    [Fact]
    public void Through_the_avatar_set_the_player_takes_its_server_move_to_and_a_chasing_creature_hears_where_it_goes()
    {
        var world = WorldOf(Flat(Block));
        var physics = Player(world, At(Block, 96f, 96f, 0f));
        var body = new PhysicsPlayerBody(physics);
        var cell = physics.Position.CellId;
        var set = new AvatarSet(AvatarRig.Sources());
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(PlayerGuid, physics.Position.Frame.Origin, cell: cell) with { IsSelf = true }, now: 0));
        Assert.Equal(AvatarApply.Applied, set.Create(AvatarRig.HumanAt(CreatureGuid, new Vector3(96f, 106f, 0f), heading: 180f, cell: cell), now: 0));
        set.PlayerBody = body;
        physics.TargetWorld = set;
        Assert.Same(body, set.FindObject(PlayerGuid));

        // The creature chases the player: its target is the physics object, which reports to it at once.
        var chase = new WireMovement(
            WireMovementType.MoveToObject,
            NonCombat,
            false,
            false,
            default,
            MoveTo: new WireMoveTo(PlayerGuid, physics.Position, new MovementParameters(), 1f));
        Assert.Equal(AvatarApply.Applied, set.SetMovement(CreatureGuid, isSelf: false, autonomous: false, chase, []));
        Assert.True(physics.Targets.HasVoyeur(CreatureGuid));
        Assert.True(set.TryGet(CreatureGuid, out var creature));
        var creatureMoveTo = creature!.Body.Movement!.MoveTo!;
        Assert.True(creatureMoveTo.Initialized);

        // The player's server MoveTo (east) reaches the physics body; its own update reports to the creature.
        var east = At(Block, 104f, 96f, 0f);
        Assert.Equal(AvatarApply.Applied, set.SetMovement(PlayerGuid, isSelf: true, autonomous: false, MoveTo(east), []));
        Assert.True(physics.IsMovingTo);
        RunWhileMovingTo(physics, 8.0);

        var reported = creatureMoveTo.CurrentTargetPosition.Frame.Origin;
        output.WriteLine($"player at {physics.Position}; the creature last heard {reported}");
        Assert.False(physics.IsMovingTo);
        Assert.True(PositionFrames.Distance(east, physics.Position) <= 0.6f + 0.15f);
        Assert.True(reported.X > 102f, "the player's reports reached the chasing creature");
    }

    /// <summary>A type-7 MoveToPosition from the server, walking only, to within 0.6 m.</summary>
    private static WireMovement MoveTo(in CellFrame target)
    {
        var parameters = new MovementParameters
        {
            Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.MoveTowards,
            DistanceToObject = 0.6f,
        };
        return new WireMovement(WireMovementType.MoveToPosition, NonCombat, false, false, default, MoveTo: new WireMoveTo(0, target, parameters, 1f));
    }

    /// <summary>Steps <paramref name="obj"/> at 60 fps until its MoveTo ends (or <paramref name="limit"/> seconds); answers the time taken.</summary>
    private static double RunWhileMovingTo(PhysicsObj obj, double limit)
    {
        var world = obj.World;
        var start = world.CurrentTime;
        while (obj.IsMovingTo && world.CurrentTime - start < limit)
        {
            if (world.UseTime(world.CurrentTime + (1.0 / 60.0)))
            {
                obj.UpdateObject();
            }
        }

        return world.CurrentTime - start;
    }
}
