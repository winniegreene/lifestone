using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The position managers (AV6), re-expressed from retail <c>InterpolationManager</c>, <c>ConstraintManager</c> and
/// <c>PositionManager</c> (CInterpolationManager.c, CConstraintManager.c, CPositionManager.c), and the
/// <see cref="PositionFrames"/> arithmetic they use. Expected values are worked out by hand from those functions.
/// </summary>
public sealed class PositionManagerTests
{
    private const uint Outdoor = 0xA9B40019;
    private const uint EnvCell = 0xA9B40150;
    private const double Frame = 1.0 / 30.0;

    [Fact]
    public void A_ten_metre_jump_closes_at_twice_the_max_speed_overwriting_the_animation()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        Assert.Equal(InterpolateToOutcome.Queued, im.InterpolateTo(At(Outdoor, 50, 60), keepHeading: true, Ctx(position)).Outcome);

        var steps = new List<float>();
        var animation = new AcFrame(new Vector3(0, 1, 0), Quaternion.Identity);
        for (var frame = 0; frame < 100 && im.IsInterpolating; frame++)
        {
            var offset = animation;
            im.AdjustOffset(ref offset, Frame, Ctx(position));
            if (offset == animation)
            {
                break; // the node completed before a step was made
            }

            steps.Add(offset.Origin.Length());
            position = position with { Frame = AcFrame.Combine(position.Frame, offset) };
        }

        // 10 m at 2 × 4 m/s and 30 frames a second: 37 full steps of 8/30 m (not the animation's 1 m), then the rest.
        Assert.Equal(38, steps.Count);
        Assert.All(steps.Take(37), step => Assert.Equal(8f / 30f, step, 4));
        Assert.Equal(10f - (37 * 8f / 30f), steps[^1], 3);
        Assert.False(im.IsInterpolating);
        Assert.True(PositionFrames.Distance(position, At(Outdoor, 50, 60)) <= InterpolationManager.DesiredDistance);

        // Done: the animation's offset is left alone again.
        var after = animation;
        im.AdjustOffset(ref after, Frame, Ctx(position));
        Assert.Equal(animation, after);
    }

    [Fact]
    public void Without_keep_heading_the_first_step_turns_to_the_targets_heading()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        im.InterpolateTo(At(Outdoor, 50, 55, heading: 90), keepHeading: false, Ctx(position));

        var offset = AcFrame.Identity;
        im.AdjustOffset(ref offset, Frame, Ctx(position));
        var moved = position with { Frame = AcFrame.Combine(position.Frame, offset) };
        Assert.Equal(90f, PositionFrames.Heading(moved.Orientation), 3);

        var keeping = new InterpolationManager();
        keeping.InterpolateTo(At(Outdoor, 50, 55, heading: 90), keepHeading: true, Ctx(position));
        Assert.Equal(0f, PositionFrames.Heading(keeping.Queue[0].Orientation), 3); // the node takes the object's heading
        var still = AcFrame.Identity;
        keeping.AdjustOffset(ref still, Frame, Ctx(position));
        Assert.Equal(0f, PositionFrames.Heading(still.Orientation), 3);
    }

    [Fact]
    public void A_node_within_5_cm_completes_without_touching_the_offset()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        im.InterpolateTo(At(Outdoor, 50, 50.2f), keepHeading: true, Ctx(position));
        var moved = At(Outdoor, 50, 50.17f);

        var offset = new AcFrame(new Vector3(0, 1, 0), Quaternion.Identity);
        im.AdjustOffset(ref offset, Frame, Ctx(moved));

        Assert.False(im.IsInterpolating);
        Assert.Equal(new Vector3(0, 1, 0), offset.Origin);
        Assert.Null(im.UseTime());
    }

    [Fact]
    public void A_stalled_object_completes_under_20_cm_and_fails_then_blips_above()
    {
        var near = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        near.InterpolateTo(At(Outdoor, 50, 50.15f), keepHeading: true, Ctx(position));
        Stall(near, position, frames: 10);
        Assert.False(near.IsInterpolating);
        Assert.Equal(0, near.NodeFailCounter);
        Assert.Null(near.UseTime());

        var far = new InterpolationManager();
        var target = At(Outdoor, 50, 51);
        far.InterpolateTo(target, keepHeading: false, Ctx(position));
        Stall(far, position, frames: 9);
        Assert.True(far.IsInterpolating); // the first check passes against BIG_DISTANCE
        Stall(far, position, frames: 1);
        Assert.False(far.IsInterpolating);
        Assert.Equal(1, far.NodeFailCounter);
        Assert.Equal(target, far.BlipToPosition);

        Assert.Equal(new InterpolationBlip(target), far.UseTime());
        far.CompleteBlip(placed: false);
        Assert.Equal(new InterpolationBlip(target), far.UseTime()); // retail retries a failed placement
        far.CompleteBlip(placed: true);
        Assert.Null(far.UseTime());
        Assert.Equal(0, far.NodeFailCounter);
    }

    [Fact]
    public void More_than_three_failed_nodes_blip_to_the_newest()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        for (var i = 1; i <= 5; i++)
        {
            im.InterpolateTo(At(Outdoor, 50, 50 + i), keepHeading: false, Ctx(position));
        }

        Stall(im, position, frames: 10); // node 1 fails (its first check passes against BIG_DISTANCE)
        Assert.Equal(1, im.NodeFailCounter);
        Stall(im, position, frames: 15); // nodes 2, 3 and 4 fail a check each
        Assert.Equal(4, im.NodeFailCounter);
        Assert.Equal(new InterpolationBlip(At(Outdoor, 50, 55)), im.UseTime());
    }

    [Fact]
    public void Progress_checks_pass_while_the_object_keeps_up()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        im.InterpolateTo(At(Outdoor, 50, 70), keepHeading: true, Ctx(position));

        for (var i = 0; i < 30; i++)
        {
            var offset = AcFrame.Identity;
            im.AdjustOffset(ref offset, Frame, Ctx(position));
            position = position with { Frame = AcFrame.Combine(position.Frame, offset.Scale(0.35f)) }; // 35% of the step
        }

        Assert.True(im.IsInterpolating);
        Assert.Equal(0, im.NodeFailCounter);
    }

    [Fact]
    public void A_twenty_first_node_drops_the_oldest_and_close_nodes_merge()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        for (var i = 1; i <= 21; i++)
        {
            im.InterpolateTo(At(Outdoor, 50, 50 + i), keepHeading: false, Ctx(position));
        }

        Assert.Equal(20, im.Queue.Count);
        Assert.Equal(At(Outdoor, 50, 52), im.Queue[0]);

        im.InterpolateTo(At(Outdoor, 50, 71.03f), keepHeading: false, Ctx(position));
        Assert.Equal(20, im.Queue.Count);
        Assert.Equal(At(Outdoor, 50, 71.03f), im.Queue[^1]);
    }

    [Theory]
    [InlineData(Outdoor, false, 150f, InterpolateToOutcome.BlipArmed)]
    [InlineData(Outdoor, true, 99f, InterpolateToOutcome.Queued)]
    [InlineData(EnvCell, false, 22f, InterpolateToOutcome.BlipArmed)] // 20 m for other objects indoors
    [InlineData(EnvCell, true, 22f, InterpolateToOutcome.Queued)] // 25 m for the player
    [InlineData(EnvCell, true, 26f, InterpolateToOutcome.BlipArmed)]
    public void A_position_beyond_the_autonomy_blip_distance_arms_a_blip(uint cell, bool isThePlayer, float distance, InterpolateToOutcome outcome)
    {
        var im = new InterpolationManager();
        var position = At(cell, 50, 50);
        var target = At(cell, 50, 50 + distance);

        Assert.Equal(outcome, im.InterpolateTo(target, keepHeading: false, Ctx(position, isThePlayer: isThePlayer)).Outcome);

        if (outcome == InterpolateToOutcome.BlipArmed)
        {
            Assert.Equal(InterpolationManager.ArmedBlipFailures, im.NodeFailCounter);
            Assert.Equal(new InterpolationBlip(target), im.UseTime());
        }
        else
        {
            Assert.Null(im.UseTime());
        }
    }

    [Fact]
    public void The_blip_distance_is_measured_from_the_newest_node()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 10, 10);
        im.InterpolateTo(At(Outdoor, 10, 60), keepHeading: true, Ctx(position));
        Assert.Equal(InterpolateToOutcome.Queued, im.InterpolateTo(At(Outdoor, 10, 150), keepHeading: true, Ctx(position)).Outcome);
    }

    [Fact]
    public void Arriving_stops_interpolating_and_takes_the_heading_unless_kept()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        im.InterpolateTo(At(Outdoor, 50, 52), keepHeading: true, Ctx(position));

        var arrived = im.InterpolateTo(At(Outdoor, 50, 50.04f, heading: 90), keepHeading: false, Ctx(position));
        Assert.Equal(InterpolateToOutcome.Arrived, arrived.Outcome);
        Assert.Equal(90f, arrived.SetHeading!.Value, 3);
        Assert.False(im.IsInterpolating);

        Assert.Null(im.InterpolateTo(At(Outdoor, 50, 50.04f, heading: 90), keepHeading: true, Ctx(position)).SetHeading);
    }

    [Fact]
    public void Without_contact_nothing_moves()
    {
        var im = new InterpolationManager();
        var position = At(Outdoor, 50, 50);
        im.InterpolateTo(At(Outdoor, 50, 55), keepHeading: true, Ctx(position));
        var offset = new AcFrame(new Vector3(0, 1, 0), Quaternion.Identity);

        im.AdjustOffset(ref offset, Frame, Ctx(position) with { HasContact = false });

        Assert.Equal(new Vector3(0, 1, 0), offset.Origin);
        Assert.Equal(0, im.FrameCounter);
    }

    [Fact]
    public void The_offset_is_continuous_across_a_landblock_seam()
    {
        var im = new InterpolationManager();
        var position = At(0xA9B40039, 191.9f, 10);
        var target = At(0xAAB40001, 0.1f, 10);
        Assert.Equal(0.2f, PositionFrames.Distance(position, target), 4);

        im.InterpolateTo(target, keepHeading: true, Ctx(position));
        var offset = AcFrame.Identity;
        im.AdjustOffset(ref offset, Frame, Ctx(position));

        Assert.Equal(0.2f, offset.Origin.X, 4);
        Assert.Equal(0f, offset.Origin.Y, 4);
    }

    [Fact]
    public void The_step_speed_follows_the_adjusted_speed_switch_and_falls_back_to_7_5()
    {
        var position = At(Outdoor, 50, 50);
        var target = At(Outdoor, 50, 60);
        var ctx = Ctx(position) with { MaxSpeed = 4f, AdjustedMaxSpeed = 6f };

        Assert.Equal(12f / 30f, Step(new InterpolationManager(), position, target, ctx), 4);
        Assert.Equal(8f / 30f, Step(new InterpolationManager { UseAdjustedSpeed = false }, position, target, ctx), 4);
        Assert.Equal(7.5f / 30f, Step(new InterpolationManager(), position, target, ctx with { AdjustedMaxSpeed = 0f }), 4);
    }

    [Theory]
    [InlineData(MotionCommands.RunForward, 1.5f)]
    [InlineData(MotionCommands.WalkForward, 1f)]
    public void For_a_server_object_both_speeds_agree(uint forward, float speed)
    {
        // A server object's weenie cannot tell its run rate, so both speeds come from my_run_rate, which running sets.
        var (motion, _, _) = MotionRig.Create(FakeWeenie.RemoteCreature());
        motion.MoveToInterpretedState(MotionRig.State(forward: MotionCommands.RunForward, forwardSpeed: 1.5f));
        motion.MoveToInterpretedState(MotionRig.State(forward: forward, forwardSpeed: speed));

        var ctx = PositionContext.For(At(Outdoor, 0, 0), hasContact: true, isThePlayer: false, motion);
        Assert.Equal(6f, ctx.MaxSpeed, 4);
        Assert.Equal(ctx.MaxSpeed, ctx.AdjustedMaxSpeed, 4);

        var none = PositionContext.For(At(Outdoor, 0, 0), hasContact: true, isThePlayer: false, motion: null);
        Assert.Equal((4f, 4f), (none.MaxSpeed, none.AdjustedMaxSpeed));
    }

    [Fact]
    public void The_constraint_slows_between_start_and_max_and_stops_beyond()
    {
        var cm = new ConstraintManager();
        var confirmed = At(Outdoor, 50, 50);
        cm.ConstrainTo(confirmed, 10f, 50f, At(Outdoor, 50, 70));
        Assert.Equal(20f, cm.Offset, 4);

        var offset = new AcFrame(new Vector3(1, 0, 0), Quaternion.Identity);
        cm.AdjustOffset(ref offset, Frame, Ctx(At(Outdoor, 50, 70)));
        Assert.Equal(0.75f, offset.Origin.X, 5); // (50 - 20) / (50 - 10)
        Assert.Equal(20.75f, cm.Offset, 4);

        cm.ConstrainTo(confirmed, 10f, 50f, At(Outdoor, 50, 100));
        var stopped = new AcFrame(new Vector3(1, 0, 0), Quaternion.Identity);
        cm.AdjustOffset(ref stopped, Frame, Ctx(At(Outdoor, 50, 100)));
        Assert.Equal(Vector3.Zero, stopped.Origin);
        Assert.Equal(50f, cm.Offset, 4);
        Assert.True(cm.IsFullyConstrained);
    }

    [Fact]
    public void The_constraint_accumulates_the_path_where_ACE_assigns_the_last_step()
    {
        var cm = new ConstraintManager();
        var here = At(Outdoor, 50, 50);
        cm.ConstrainTo(here, 10f, 50f, here);

        for (var i = 0; i < 12; i++)
        {
            var offset = new AcFrame(new Vector3(1, 0, 0), Quaternion.Identity);
            cm.AdjustOffset(ref offset, Frame, Ctx(here));
        }

        // Ten unscaled metres, then 40/40 = 1 at 10 m and 39/40 at 11 m: retail walks 11.975 m. ACE's assignment would
        // keep the offset at the last step's length (about 1) and never slow down.
        Assert.Equal(10f + 1f + 0.975f, cm.Offset, 4);
    }

    [Fact]
    public void Without_contact_the_constraint_does_not_scale_but_still_accumulates()
    {
        var cm = new ConstraintManager();
        var here = At(Outdoor, 50, 50);
        cm.ConstrainTo(here, 10f, 50f, At(Outdoor, 50, 80));
        var offset = new AcFrame(new Vector3(0, 2, 0), Quaternion.Identity);

        cm.AdjustOffset(ref offset, Frame, Ctx(here) with { HasContact = false });

        Assert.Equal(new Vector3(0, 2, 0), offset.Origin);
        Assert.Equal(32f, cm.Offset, 4);
    }

    [Fact]
    public void Unconstrained_offsets_pass_and_the_distance_stays()
    {
        var cm = new ConstraintManager();
        var here = At(Outdoor, 50, 50);
        cm.ConstrainTo(here, 10f, 50f, At(Outdoor, 50, 95.5f));
        Assert.True(cm.IsFullyConstrained); // 45.5 > 0.9 × 50
        cm.UnConstrain();
        var offset = new AcFrame(new Vector3(0, 2, 0), Quaternion.Identity);

        cm.AdjustOffset(ref offset, Frame, Ctx(here));

        Assert.Equal(new Vector3(0, 2, 0), offset.Origin);
        Assert.Equal(45.5f, cm.Offset, 4);
        Assert.True(cm.IsFullyConstrained);
    }

    [Theory]
    [InlineData(Outdoor, false, 100f, 10f, 50f)]
    [InlineData(Outdoor, true, 100f, 10f, 50f)]
    [InlineData(EnvCell, false, 20f, 5f, 20f)]
    [InlineData(EnvCell, true, 25f, 5f, 20f)]
    [InlineData(0xA9B4FFFF, false, 20f, 5f, 20f)] // the landblock id counts as indoors (low word 0x100 or more)
    public void Retail_distances(uint cell, bool isThePlayer, float blip, float start, float max)
    {
        Assert.Equal(blip, PositionManager.GetAutonomyBlipDistance(cell, isThePlayer));
        Assert.Equal(start, PositionManager.GetStartConstraintDistance(cell));
        Assert.Equal(max, PositionManager.GetMaxConstraintDistance(cell));
    }

    [Fact]
    public void The_position_manager_interpolates_then_constrains()
    {
        var pm = new PositionManager();
        var position = At(Outdoor, 50, 50);
        Assert.Null(pm.Constraint);
        Assert.False(pm.IsFullyConstrained);

        pm.InterpolateTo(At(Outdoor, 50, 60), keepHeading: true, Ctx(position));
        pm.ConstrainTo(At(Outdoor, 50, 20), 10f, 50f, Ctx(position)); // 30 m: factor 0.5

        var offset = new AcFrame(new Vector3(5, 0, 0), Quaternion.Identity);
        pm.AdjustOffset(ref offset, Frame, Ctx(position));

        Assert.Equal(4f / 30f, offset.Origin.Y, 4); // half the 8/30 m step
        Assert.Equal(0f, offset.Origin.X, 5);
        Assert.True(pm.IsInterpolating);
        Assert.Null(pm.UseTime(Ctx(position)));

        pm.StopInterpolating();
        pm.UnConstrain();
        Assert.False(pm.IsInterpolating);
        Assert.False(pm.Constraint!.IsConstrained);
    }

    [Fact]
    public void Positions_compare_across_landblocks()
    {
        var a = At(0xA9B40039, 191.5f, 20, 3);
        var b = At(0xAAB50001, 0.5f, 1, 3);
        Assert.Equal(new Vector3(1, 173, 0), PositionFrames.Offset(a, b));
        Assert.Equal(new Vector3(1, 173, 0).Length(), PositionFrames.Distance(a, b), 3);
        Assert.Equal(-PositionFrames.Offset(a, b), PositionFrames.Offset(b, a));
    }

    [Fact]
    public void Subtract2_is_the_frame_that_takes_the_current_position_to_the_target()
    {
        var current = At(Outdoor, 10, 20, 1, heading: 30);
        var target = At(Outdoor, 13, 24, 2, heading: 135);

        var step = PositionFrames.Subtract2(target, current);
        var reached = AcFrame.Combine(current.Frame, step);

        Assert.Equal(target.Origin.X, reached.Origin.X, 4);
        Assert.Equal(target.Origin.Y, reached.Origin.Y, 4);
        Assert.Equal(target.Origin.Z, reached.Origin.Z, 4);
        Assert.Equal(135f, PositionFrames.Heading(reached.Orientation), 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(180f)]
    [InlineData(270f)]
    [InlineData(359f)]
    public void Headings_round_trip(float heading)
    {
        var q = PositionFrames.WithHeading(Quaternion.Identity, heading);
        Assert.Equal(heading, PositionFrames.Heading(q), 3);
        Assert.Equal((double)heading, new CellFrame(Outdoor, new AcFrame(Vector3.Zero, q)).HeadingDegrees, 3);
    }

    [Fact]
    public void Setting_a_heading_keeps_the_forward_axis_vertical_part()
    {
        var pitched = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 6); // forward 30 degrees up
        var turned = PositionFrames.WithHeading(pitched, 90f);
        var forward = Vector3.Transform(Vector3.UnitY, turned);

        // Retail puts the forward axis's vertical part (0.5) next to a unit horizontal heading, so the pitch flattens a little.
        Assert.Equal(0.5f / MathF.Sqrt(1.25f), forward.Z, 4);
        Assert.Equal(90f, PositionFrames.Heading(turned), 3);
        Assert.True(forward.X > 0.8f); // east
    }

    private static CellFrame At(uint cell, float x, float y, float z = 0, float heading = 0) =>
        new(cell, new AcFrame(new Vector3(x, y, z), PositionFrames.WithHeading(Quaternion.Identity, heading)));

    private static PositionContext Ctx(in CellFrame position, bool isThePlayer = false) =>
        new(position, HasContact: true, IsThePlayer: isThePlayer, MaxSpeed: 4f, AdjustedMaxSpeed: 4f);

    /// <summary>Calls AdjustOffset for a number of frames without moving the object.</summary>
    private static void Stall(InterpolationManager im, in CellFrame position, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            var offset = AcFrame.Identity;
            im.AdjustOffset(ref offset, Frame, Ctx(position));
        }
    }

    private static float Step(InterpolationManager im, in CellFrame position, in CellFrame target, in PositionContext ctx)
    {
        im.InterpolateTo(target, keepHeading: true, ctx);
        var offset = AcFrame.Identity;
        im.AdjustOffset(ref offset, Frame, ctx);
        return offset.Origin.Length();
    }
}

internal static class AcFrameScaling
{
    /// <summary>The frame with its origin scaled (a partial step, for the progress test).</summary>
    public static AcFrame Scale(this AcFrame frame, float factor) => frame with { Origin = frame.Origin * factor };
}
