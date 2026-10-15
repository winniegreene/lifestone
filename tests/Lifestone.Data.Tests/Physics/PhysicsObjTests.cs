using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsObjTestKit;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// <see cref="PhysicsObj"/> (PH5) on synthetic worlds: retail <c>CPhysicsObj</c>'s stepping, falling and landing,
/// placement, the lost cell, hiding, bouncing, and other objects' shadows.
/// </summary>
public sealed class PhysicsObjTests(ITestOutputHelper output)
{
    [Fact]
    public void A_10_m_fall_lands_on_the_terrain_and_comes_to_rest()
    {
        var world = FlatWorld();
        var events = new RecordingPhysicsEvents();
        var human = HumanObj(world, 0x50000001, FakePhysicsWeenie.Player());
        human.Events = events;
        world.SetPlayer(human);
        Assert.True(human.EnterWorld(At(Block, 100f, 100f, 10f)));
        Assert.False(human.HasContact);
        Assert.Equal(new Vector3(0, 0, PhysicsGlobals.Gravity), human.Acceleration);

        var landedAt = -1.0;
        Run(human, 3.0, perFrame: o =>
        {
            if (landedAt < 0 && o.HasContact)
            {
                landedAt = world.CurrentTime;
            }
        });

        output.WriteLine($"landed at {landedAt:F3} s; rest {human.Position}; {events.Environment.Count} environment reports; velocity {human.Velocity}");
        Assert.True(human.HasContact);
        Assert.True(human.OnWalkable);
        Assert.Equal(0.005f, human.Position.Frame.Origin.Z, 3);
        Assert.InRange(landedAt, Math.Sqrt(2 * 9.995 / 9.8), Math.Sqrt(2 * 9.995 / 9.8) + (2.0 / 30.0));
        Assert.Equal(Vector3.Zero, human.Velocity);
        Assert.Equal(Vector3.Zero, human.Acceleration);
        Assert.Equal(TransientState.None, human.TransientState & TransientState.StationaryMask);
        Assert.All(events.Environment, e => Assert.False(e.HadContact));

        // Landing bounces the human back up at 5% of its 14 m/s (handle_all_collisions' elasticity): it hops about 2 cm and
        // lands again, and each landing is an environment collision.
        Assert.Equal(2, events.Environment.Count);
    }

    [Fact]
    public void A_jump_peaks_at_the_square_of_its_speed_over_twice_gravity()
    {
        var world = FlatWorld();
        var human = HumanObj(world);
        Assert.True(human.EnterWorld(At(Block, 100f, 100f, 0.5f)));
        Run(human, 1.0);
        Assert.True(human.OnWalkable);
        var ground = human.Position.Frame.Origin.Z;

        // CMotionInterp::jump: set_on_walkable(0) (gravity at once), then LeaveGround's velocity.
        ((IMotionInterpHost)human).SetOnWalkable(false);
        const float vz = 5f;
        human.SetVelocity(new Vector3(0f, 0f, vz));
        var apex = ground;
        Run(human, 1.5, fps: 240, perFrame: o => apex = MathF.Max(apex, o.Position.Frame.Origin.Z));

        output.WriteLine($"apex {apex - ground:F4} m, expected {vz * vz / 19.6f:F4} m");
        Assert.Equal(vz * vz / 19.6f, apex - ground, 0.01f);
        Assert.True(human.OnWalkable);
    }

    [Fact]
    public void A_player_walking_into_a_cell_a_housing_barrier_guards_is_let_in_and_counted()
    {
        // TODO(physics): no house weenie exists in M2, so the barrier is not checked (retail would refuse the player until
        // the barrier object is loaded); the world counts each such entry so the client can say so. Only a player with a
        // weenie is counted, as retail only checks such an object.
        var world = FlatWorld();
        var restricted = LandCellAt(Block, 100f, 130f);
        world.GetCell(restricted)!.RestrictionObject = 0x7A9B4001;
        Assert.Equal(0, world.UnenforcedRestrictionEntries);

        static PhysicsObj WalkNorth(PhysicsWorld world, uint id, FakePhysicsWeenie weenie, float x)
        {
            var obj = HumanObj(world, id, weenie, SyntheticMotion());
            Assert.True(obj.EnterWorld(At(Block, x, 117f, 0f)));
            Run(obj, 0.5);
            Assert.Equal(WeenieError.None, obj.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
            Run(obj, 2.5);
            return obj;
        }

        var creature = WalkNorth(world, 0x80000002, FakePhysicsWeenie.Creature(), 110f);
        Assert.Equal(restricted, creature.Position.CellId);
        Assert.Equal(0, world.UnenforcedRestrictionEntries);

        var player = WalkNorth(world, 0x50000001, FakePhysicsWeenie.Player(), 100f);
        output.WriteLine($"{world.UnenforcedRestrictionEntries} unenforced entries; player at {player.Position}");
        Assert.Equal(restricted, player.Position.CellId);
        Assert.True(world.UnenforcedRestrictionEntries > 0);
        Assert.Equal(restricted, world.LastUnenforcedRestrictionCell);
    }

    [Fact]
    public void Identical_runs_give_bitwise_identical_trajectories()
    {
        static List<CellFrame> Trajectory()
        {
            var world = FlatWorld(Box(Block, 104f, 100f, 0f, new Vector3(1f, 6f, 2f)));
            var human = HumanObj(world);
            human.EnterWorld(At(Block, 100f, 100f, 3f));
            human.SetVelocity(new Vector3(4f, 0.5f, 2f));
            var positions = new List<CellFrame>();
            Run(human, 2.0, fps: 75, perFrame: o => positions.Add(o.Position));
            return positions;
        }

        Assert.Equal(Trajectory(), Trajectory());
    }

    [Fact]
    public void Placement_into_a_landblock_that_is_not_attached_waits_and_completes_on_attach()
    {
        var world = FlatWorld();
        var elsewhere = new LandblockCoord(0x41, 0x40);
        var human = HumanObj(world);
        var target = At(elsewhere, 50f, 60f, 1f);

        Assert.Equal(SetPositionError.OK, human.SetPosition(target, SetPositionFlags.Placement | SetPositionFlags.Slide));
        Assert.True(human.PlacementPending);
        Assert.Null(human.Cell);
        Assert.Contains(human, world.LostObjects);
        Assert.Equal(target.CellId, human.Position.CellId);

        world.Attach(Flat(elsewhere));
        Assert.False(human.PlacementPending);
        Assert.Empty(world.LostObjects);
        Assert.NotNull(human.Cell);
        Assert.Equal(target.CellId, human.Cell!.Id);

        // The sliding placement sets the human down on the ground within its step-down height (1.5 m).
        Assert.Equal(50f, human.Position.Frame.Origin.X);
        Assert.Equal(60f, human.Position.Frame.Origin.Y);
        Assert.Equal(0.005f, human.Position.Frame.Origin.Z, 5);
        Assert.True(human.OnWalkable);

        // Detaching its landblock sends it back to the lost cell, keeping its position.
        world.Detach(elsewhere);
        Assert.True(human.PlacementPending);
        Assert.Null(human.Cell);
        Assert.Equal(target.CellId, human.Position.CellId);
    }

    [Fact]
    public void A_placement_that_does_not_slide_must_stay_within_5_cm_and_a_sliding_one_is_pushed_aside()
    {
        var world = FlatWorld(Box(Block, 100f, 100f, 0f, new Vector3(2f, 2f, 3f)));
        var inBox = At(Block, 100.6f, 100f, 0.1f);

        var stuck = HumanObj(world);
        var error = stuck.SetPosition(inBox, SetPositionFlags.Placement);
        Assert.Equal(SetPositionError.NoValidPosition, error);
        Assert.Null(stuck.Cell);

        var slid = HumanObj(world);
        Assert.Equal(SetPositionError.OK, slid.SetPosition(inBox, SetPositionFlags.Placement | SetPositionFlags.Slide));
        var pushed = slid.Position.GetOffset(inBox).Length();
        output.WriteLine($"pushed {pushed:F3} m to {slid.Position}");
        Assert.True(pushed > 0.05f);

        Assert.Equal(SetPositionError.InvalidArguments, HumanObj(world).SetPosition(new CellFrame(0x40400050, inBox.Frame), SetPositionFlags.Placement));
    }

    [Fact]
    public void A_hidden_object_neither_falls_nor_animates_and_resumes_when_shown()
    {
        var world = FlatWorld();
        var human = HumanObj(world, motion: SyntheticMotion());
        human.EnterWorld(At(Block, 100f, 100f, 5f));
        human.SetState(human.State | PhysicsState.Hidden);
        Assert.True((human.State & PhysicsState.IgnoreCollisions) != 0);
        var frame = human.MotionPlayer!.Sequence.FrameNumber;
        Run(human, 1.0);
        Assert.Equal(5f, human.Position.Frame.Origin.Z);
        Assert.Equal(frame, human.MotionPlayer.Sequence.FrameNumber);

        human.SetState(human.State & ~PhysicsState.Hidden);
        Assert.True((human.State & PhysicsState.IgnoreCollisions) == 0);
        Run(human, 0.5);
        Assert.True(human.Position.Frame.Origin.Z < 5f);
        Assert.NotEqual(frame, human.MotionPlayer.Sequence.FrameNumber);
    }

    [Fact]
    public void Update_object_steps_at_most_a_fifth_of_a_second_and_carries_a_small_remainder()
    {
        var world = FlatWorld();
        var human = HumanObj(world);
        human.EnterWorld(At(Block, 100f, 100f, 50f));
        world.SetTime(1.0);
        human.UpdateObject();
        Assert.Equal(1.0, human.UpdateTime, 9);

        // 0.4 s: two steps of 0.2 s; the cached velocity is the last step's average.
        world.SetTime(1.4);
        human.UpdateObject();
        Assert.Equal(1.4, human.UpdateTime, 9);
        var retailLast = human.CachedVelocity.Z;

        // 0.21 s: one step of 0.2 s and 0.01 s carried.
        world.SetTime(1.61);
        human.UpdateObject();
        Assert.Equal(1.6, human.UpdateTime, 9);

        // A gap of 2 s or more, or of at most 0.0002 s, is skipped.
        world.SetTime(4.0);
        var z = human.Position.Frame.Origin.Z;
        human.UpdateObject();
        Assert.Equal(4.0, human.UpdateTime, 9);
        Assert.Equal(z, human.Position.Frame.Origin.Z);
        world.SetTime(4.0001);
        human.UpdateObject();
        Assert.Equal(4.0001, human.UpdateTime, 9);
        Assert.Equal(z, human.Position.Frame.Origin.Z);

        // ACE's 0.1 s steps (decision table row 1) end the same 0.4 s on a 0.1 s step.
        var aceWorld = WorldOf(PhysicsCompat.Ace, Flat(Block));
        aceWorld.SetTime(0.0);
        var ace = HumanObj(aceWorld);
        ace.EnterWorld(At(Block, 100f, 100f, 50f));
        aceWorld.SetTime(1.0);
        ace.UpdateObject();
        aceWorld.SetTime(1.4);
        ace.UpdateObject();
        output.WriteLine($"last step's velocity: retail {retailLast:F4}, ACE {ace.CachedVelocity.Z:F4}");
        Assert.Equal(-9.8 * 1.3, retailLast, 3);
        Assert.Equal(-9.8 * 1.35, ace.CachedVelocity.Z, 3);
    }

    [Fact]
    public void An_object_more_than_96_m_from_the_player_is_not_updated()
    {
        var world = FlatWorld();
        var player = HumanObj(world, 0x50000001, FakePhysicsWeenie.Player());
        player.EnterWorld(At(Block, 10f, 10f, 0f));
        world.SetPlayer(player);
        var near = HumanObj(world, 0x80000001);
        near.EnterWorld(At(Block, 80f, 10f, 5f));
        var far = HumanObj(world, 0x80000002);
        far.EnterWorld(At(Block, 120f, 10f, 5f));

        for (var i = 1; i <= 30; i++)
        {
            world.SetTime(i / 30.0);
            near.UpdateObject();
            far.UpdateObject();
        }

        Assert.InRange(near.PlayerDistance, 70f, 71f);
        Assert.True(near.Position.Frame.Origin.Z < 5f);
        Assert.InRange(far.PlayerDistance, 110f, 111f);
        Assert.False(far.IsActive);
        Assert.Equal(5f, far.Position.Frame.Origin.Z);
    }

    [Fact]
    public void A_thrown_object_bounces_off_a_wall_with_its_elasticity_and_an_inelastic_one_stops()
    {
        var world = FlatWorld(Box(Block, 105f, 100f, 0f, new Vector3(1f, 20f, 10f)));
        var ball = HumanObj(world);
        ball.EnterWorld(At(Block, 100f, 100f, 3f));
        ball.SetVelocity(new Vector3(10f, 0f, 0f));
        Run(ball, 0.5, perFrame: _ => { });
        output.WriteLine($"bounced to {ball.Velocity}");
        Assert.True(ball.Velocity.X < 0f);
        Assert.Equal(-10f * ball.Elasticity, ball.Velocity.X, 1);

        var lump = HumanObj(world);
        lump.SetState(lump.State | PhysicsState.Inelastic);
        lump.EnterWorld(At(Block, 100f, 96f, 3f));
        lump.SetVelocity(new Vector3(10f, 0f, 0f));
        Run(lump, 0.5);
        Assert.Equal(0f, lump.Velocity.X);
    }
}
