using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsObjTestKit;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// The player against other moving objects (owner decision D25): retail <c>CPhysicsObj::FindObjCollisions</c>'s rules for
/// creatures, players, player killers, impenetrable and ethereal objects, objects with physics BSPs, and the collision
/// reports of <c>handle_all_collisions</c>.
/// </summary>
public sealed class PhysicsObjCollisionTests(ITestOutputHelper output)
{
    private const uint PlayerId = 0x50000001;
    private const uint OtherId = 0x80000001;

    [Fact]
    public void A_creature_in_the_way_stops_the_walking_player_and_both_hear_of_it_once()
    {
        var world = FlatWorld();
        var (player, playerEvents) = WalkingPlayer(world);
        var (creature, creatureEvents) = Obstacle(world, CreatureSetup, FakePhysicsWeenie.Creature(), 100f, 104f);

        Run(player, 3.0);
        output.WriteLine($"stopped at {player.Position}");
        Assert.InRange(player.Position.Frame.Origin.Y, 102.5f, 104f - 0.98f + 0.001f);
        Assert.True(player.OnWalkable);
        var hit = Assert.Single(playerEvents.Objects);
        Assert.Same(creature, hit.Collision.Other);
        Assert.Equal(new Vector3(0, 0, 0), hit.Collision.RelativeVelocity);
        Assert.True(hit.Collision.SelfHadContact);
        var heard = Assert.Single(creatureEvents.Objects);
        Assert.Same(player, heard.Collision.Other);
        Assert.Empty(playerEvents.Ends);

        // Walking straight into the creature left a sliding normal opposite the walk, which bends every later step to
        // nothing: with the creature gone the player still stands (no step, so no transition result clears it, as in
        // retail CTransition::find_transitional_position), until it turns.
        creature.LeaveWorld();
        var stuck = player.Position;
        Run(player, 0.5);
        Assert.Equal(stuck, player.Position);
        Assert.True((player.TransientState & TransientState.Sliding) != 0);

        // Turned 20 degrees it slides off the old normal, walks on, and a second later the collision ends for both.
        player.SetHeading(20f);
        Run(player, 1.5);
        Assert.True(player.Position.Frame.Origin.Y > 105f);
        Assert.True((player.TransientState & TransientState.Sliding) == 0);
        Assert.Equal((player, OtherId), Assert.Single(playerEvents.Ends));
        Assert.Equal((creature, PlayerId), Assert.Single(creatureEvents.Ends));
        Assert.Equal(0, player.CollisionCount);
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void Players_pass_through_each_other_unless_one_is_impenetrable_or_both_kill_players(
        bool otherImpenetrable, bool bothPK, bool bothPKLite, bool playerImpenetrable, bool passes)
    {
        var world = FlatWorld();
        var weenie = FakePhysicsWeenie.Player();
        weenie.IsImpenetrable = playerImpenetrable;
        weenie.IsPK = bothPK;
        weenie.IsPKLite = bothPKLite;
        var (player, _) = WalkingPlayer(world, weenie);
        var other = FakePhysicsWeenie.OtherPlayer();
        other.IsImpenetrable = otherImpenetrable;
        other.IsPK = bothPK;
        other.IsPKLite = bothPKLite;
        Obstacle(world, HumanSetup, other, 100f, 104f);

        Run(player, 3.0);
        Assert.Equal(passes, player.Position.Frame.Origin.Y > 106f);
    }

    [Fact]
    public void An_ethereal_creature_is_walked_through_but_still_reported()
    {
        var world = FlatWorld();
        var (player, events) = WalkingPlayer(world);
        var (creature, _) = Obstacle(world, CreatureSetup, FakePhysicsWeenie.Creature(), 100f, 104f);
        Assert.True(creature.SetEthereal(true));

        Run(player, 3.0);
        Assert.True(player.Position.Frame.Origin.Y > 106f);
        Assert.Same(creature, Assert.Single(events.Objects).Collision.Other);
    }

    [Fact]
    public void A_door_with_a_physics_BSP_blocks_until_it_turns_ethereal_and_turns_solid_only_when_clear()
    {
        var world = FlatWorld();
        var (player, _) = WalkingPlayer(world);
        var doorSetup = PhysicsSetup.FromParts(
            [],
            parts: [new PhysicsSetupPart(BoxShape(0x0100F001, new Vector3(3f, 0.2f, 3f)), AcFrame.Identity, Vector3.One, Always2D: false)]);
        var (door, _) = Obstacle(world, doorSetup, weenie: null, 100f, 104f);
        Assert.True((door.State & PhysicsState.HasPhysicsBsp) != 0);
        Assert.True(door.ShadowCellCount > 0);

        Run(player, 3.0);
        output.WriteLine($"stopped at {player.Position}");
        Assert.InRange(player.Position.Frame.Origin.Y, 102.9f, 104f - 0.1f - 0.48f);

        // Ethereal, the door lets the player through once it turns off the sliding normal its head-on walk left.
        Assert.True(door.SetEthereal(true));
        player.SetHeading(10f);
        Run(player, 0.3);
        Assert.True(player.Position.Frame.Origin.Y > 103.5f);

        // The player stands in the doorway: the door cannot turn solid, and keeps trying until the player has walked on.
        Assert.False(door.SetEthereal(false));
        Assert.True((door.TransientState & TransientState.CheckEthereal) != 0);
        Assert.True((door.State & PhysicsState.Ethereal) != 0);
        Run(player, 1.0);
        door.UpdateObject();
        world.SetTime(world.CurrentTime + (1.0 / 30.0));
        door.UpdateObject();
        Assert.True((door.State & PhysicsState.Ethereal) == 0);
        Assert.True((door.TransientState & TransientState.CheckEthereal) == 0);
    }

    [Fact]
    public void Moving_an_object_with_force_into_cell_moves_its_shadows_between_cells()
    {
        var world = FlatWorld();
        var (creature, _) = Obstacle(world, CreatureSetup, FakePhysicsWeenie.Creature(), 100f, 100f);
        Assert.Equal(At(Block, 100f, 100f, 0f).CellId, creature.Cell!.Id);
        var firstCells = Enumerable.Range(0, creature.ShadowCellCount).Select(creature.ShadowCellIdAt).ToArray();
        Assert.Contains(creature.Cell.Id, firstCells);

        var far = At(Block, 150f, 30f, 0f);
        Assert.Equal(SetPositionError.OK, creature.ForceIntoCell(far));
        Assert.Equal(far.CellId, creature.Cell!.Id);
        Assert.Equal(far.CellId, creature.ShadowCellIdAt(0));
        Assert.DoesNotContain(creature.ShadowCellIdAt(0), firstCells);

        // A player walking where the creature was is not stopped.
        var (player, _) = WalkingPlayer(world, at: 96f);
        Run(player, 3.0);
        Assert.True(player.Position.Frame.Origin.Y > 102f);
    }

    /// <summary>A player at (100, <paramref name="at"/>) walking north at 3.12 m/s (the synthetic table's walk cycle).</summary>
    private static (PhysicsObj Player, RecordingPhysicsEvents Events) WalkingPlayer(PhysicsWorld world, FakePhysicsWeenie? weenie = null, float at = 100f)
    {
        var events = new RecordingPhysicsEvents();
        var player = HumanObj(world, PlayerId, weenie ?? FakePhysicsWeenie.Player(), SyntheticMotion());
        player.Events = events;
        world.SetPlayer(player);
        Assert.True(player.EnterWorld(At(Block, 100f, at, 0f)));
        Run(player, 0.5);
        Assert.True(player.OnWalkable);
        Assert.Equal(WeenieError.None, player.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
        return (player, events);
    }

    /// <summary>A server object at (<paramref name="x"/>, <paramref name="y"/>) on the ground, placed as the avatars track moves one (D24).</summary>
    private static (PhysicsObj Obstacle, RecordingPhysicsEvents Events) Obstacle(PhysicsWorld world, PhysicsSetup setup, FakePhysicsWeenie? weenie, float x, float y)
    {
        var events = new RecordingPhysicsEvents();
        var obstacle = new PhysicsObj(world, setup, OtherId, weenie) { Events = events };
        Assert.Equal(SetPositionError.OK, obstacle.ForceIntoCell(At(Block, x, y, 0f)));
        return (obstacle, events);
    }
}
