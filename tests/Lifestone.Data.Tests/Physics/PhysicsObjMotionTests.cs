using System.Numerics;
using Lifestone.Client.Tests;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Objects;
using Xunit.Abstractions;
using static Lifestone.Data.Tests.Physics.PhysicsObjTestKit;
using static Lifestone.Data.Tests.Physics.PhysicsTestKit;

namespace Lifestone.Data.Tests.Physics;

/// <summary>
/// A <see cref="PhysicsObj"/> hosting the avatars track's <see cref="MotionInterpreter"/> (PH6) on a synthetic motion table
/// (walk 3.12 m/s, run 4 m/s, Falling): root motion only on walkable ground, falling and landing through
/// <c>set_on_walkable</c>, and the jump's leave-ground velocity.
/// </summary>
public sealed class PhysicsObjMotionTests(ITestOutputHelper output)
{
    [Fact]
    public void A_new_object_enters_its_default_state_twice_and_keeps_its_motions_in_step_with_the_table()
    {
        var world = FlatWorld();
        var human = HumanObj(world, motion: SyntheticMotion());
        Assert.NotNull(human.Motion);
        Assert.True(human.Motion!.Initted);
        AssertInStep(human);

        human.EnterWorld(At(Block, 100f, 100f, 0f));
        Run(human, 1.0);
        Assert.True(human.OnWalkable);
        Assert.Equal(MotionCommands.Ready, human.MotionPlayer!.State.Substate);
        AssertInStep(human);
        Assert.Equal(0, human.Motion.PendingMotionCount);
    }

    [Fact]
    public void Walking_moves_by_the_cycles_root_motion_on_walkable_ground()
    {
        var world = FlatWorld();
        var human = Standing(world);
        var start = human.Position;

        Assert.Equal(WeenieError.None, human.DoMotion(MotionCommands.WalkForward, new MovementParameters()));
        Assert.True(human.LastMoveWasAutonomous);
        Run(human, 2.0);

        var walked = start.GetOffset(human.Position);
        output.WriteLine($"walked {walked} in 2 s");
        Assert.Equal(0f, walked.X, 3);
        Assert.InRange(walked.Y, 3.12f * 2f * 0.9f, (3.12f * 2f) + 0.01f);
        Assert.True(human.OnWalkable);
        Assert.Equal(Vector3.Zero, human.Velocity);
        Assert.Equal(new Vector3(0f, 3.12f, 0f), human.Motion!.GetStateVelocity());

        // Held run: RunForward at the weenie's run rate.
        ((FakePhysicsWeenie)human.Weenie!).RunRate = 1.5f;
        human.Motion.SetHoldRun(true, cancelMoveTo: false);
        Assert.Equal(MotionCommands.RunForward, human.Motion.Interpreted.ForwardCommand);
        start = human.Position;
        Run(human, 1.0);
        var ran = start.GetOffset(human.Position).Y;
        output.WriteLine($"ran {ran} m in 1 s");
        Assert.InRange(ran, 4f * 1.5f * 0.85f, (4f * 1.5f) + 0.01f);
    }

    [Fact]
    public void Walking_off_a_ledge_falls_with_the_state_velocity_and_lands_walking()
    {
        var world = FlatWorld(Box(Block, 100f, 101f, 0f, new Vector3(4f, 4f, 2f)));
        var human = HumanObj(world, 0x50000001, FakePhysicsWeenie.Player(), SyntheticMotion());
        human.EnterWorld(At(Block, 100f, 101.5f, 2.5f));
        Run(human, 1.0);
        Assert.True(human.OnWalkable);
        Assert.Equal(2.005f, human.Position.Frame.Origin.Z, 3);

        // A player without EdgeSlide walks off (retail sets EdgeSlide on new objects; players clear it in the server's
        // PhysicsState, and this test clears it the same way).
        human.SetState(human.State & ~PhysicsState.EdgeSlide);
        human.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        var fellAt = -1.0;
        var fallVelocity = Vector3.Zero;
        var substate = 0u;
        Run(human, 2.0, perFrame: o =>
        {
            if (fellAt < 0 && !o.OnWalkable)
            {
                fellAt = world.CurrentTime;
                fallVelocity = o.Velocity;
                substate = o.MotionPlayer!.State.Substate;
            }
        });

        output.WriteLine($"left the ledge at {fellAt:F3} s with {fallVelocity}; now {human.Position}");
        Assert.True(fellAt > 0);
        Assert.Equal(MotionCommands.Falling, substate);
        Assert.Equal(0f, fallVelocity.X, 3);
        Assert.Equal(3.12f, fallVelocity.Y, 3);
        Assert.True(human.OnWalkable);
        Assert.Equal(0.005f, human.Position.Frame.Origin.Z, 3);
        Assert.Equal(MotionCommands.WalkForward, human.MotionPlayer!.State.Substate);
        AssertInStep(human);
    }

    [Fact]
    public void A_jump_leaves_the_ground_at_the_weenies_speed_falls_and_lands()
    {
        var world = FlatWorld();
        var human = Standing(world);
        ((FakePhysicsWeenie)human.Weenie!).JumpVelocity = 5f;
        var ground = human.Position.Frame.Origin.Z;

        Assert.Equal(WeenieError.None, human.Jump(1f, out _));
        Assert.False(human.OnWalkable);
        Assert.Equal(new Vector3(0f, 0f, 5f), human.Velocity);
        Assert.Equal(new Vector3(0f, 0f, PhysicsGlobals.Gravity), human.Acceleration);
        Assert.Equal(MotionCommands.Falling, human.MotionPlayer!.State.Substate);

        var apex = ground;
        Run(human, 1.5, perFrame: o => apex = MathF.Max(apex, o.Position.Frame.Origin.Z));
        output.WriteLine($"apex {apex - ground:F4} m");
        Assert.Equal(25f / 19.6f, apex - ground, 0.01f);
        Assert.True(human.OnWalkable);
        Assert.Equal(MotionCommands.Ready, human.MotionPlayer.State.Substate);

        // In the air a second jump is refused.
        Assert.Equal(WeenieError.None, human.Jump(1f, out _));
        Assert.Equal(WeenieError.YouCantJumpWhileInTheAir, human.Jump(1f, out _));
    }

    /// <summary>A player standing on the flat ground at (100, 100), its default state done.</summary>
    internal static PhysicsObj Standing(Lifestone.Data.Physics.World.PhysicsWorld world)
    {
        var human = HumanObj(world, 0x50000001, FakePhysicsWeenie.Player(), SyntheticMotion());
        world.SetPlayer(human);
        human.EnterWorld(At(Block, 100f, 100f, 0f));
        Run(human, 1.0);
        Assert.True(human.OnWalkable);
        return human;
    }

    private static void AssertInStep(PhysicsObj obj)
    {
        Assert.Equal(obj.Motion!.PendingMotionCount, obj.MotionPlayer!.PendingAnimationCount);
        Assert.Equal(obj.Motion.PendingMotionCount, obj.MotionPlayer.Manager.Pending.Count());
    }
}

/// <summary>
/// A walking <see cref="PhysicsObj"/> allocates nothing per update once warm (m2-plan PH5): the motion table, the
/// interpreter, the position managers and the transition reuse what they have. Measured on this thread in the
/// non-parallel allocation collection; a window with a GC inside is not judged.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class PhysicsObjAllocationTests
{
    [Fact]
    public void Walking_into_a_wall_and_along_it_allocates_nothing_per_update_once_warm()
    {
        var world = FlatWorld(Box(Block, 100f, 104f, 0f, new Vector3(20f, 0.4f, 3f)));
        var human = PhysicsObjMotionTests.Standing(world);
        human.SetHeading(10f);
        human.DoMotion(MotionCommands.WalkForward, new MovementParameters());
        var time = world.CurrentTime;

        void Walk()
        {
            for (var i = 0; i < 60; i++)
            {
                time += 1.0 / 60.0;
                if (world.UseTime(time))
                {
                    human.UpdateObject();
                }
            }
        }

        Walk();
        Walk();
        var judged = 0;
        for (var attempt = 0; attempt < 10 && judged < 3; attempt++)
        {
            var window = AllocationWindow.Open();
            Walk();
            var (bytes, gcInside) = window.Close();
            if (gcInside)
            {
                continue;
            }

            judged++;
            Assert.Equal(0, bytes);
        }

        Assert.True(judged > 0, "every measured window had a GC inside");
        Assert.True(human.Position.Frame.Origin.X > 101f, "the walker slid along the wall");
    }
}
