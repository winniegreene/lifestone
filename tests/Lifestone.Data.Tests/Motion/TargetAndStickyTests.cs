using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Data.Tests.Motion;

/// <summary>
/// The target manager (CTargetting.c: reports to voyeurs, predictions, timeouts) and the sticky manager
/// (CStickyManager.c: the follow step, its one-second life).
/// </summary>
public sealed class TargetAndStickyTests
{
    [Fact]
    public void Setting_a_target_reports_at_once_and_then_only_past_the_radius_every_half_second()
    {
        var world = new TestWorld();
        var watcher = new MoverHost(1, world, MoverHost.At(10, 10));
        var target = new MoverHost(2, world, MoverHost.At(20, 10));
        watcher.Targets.SetTarget(0, 2, radius: 0.5f, quantum: 0.0);
        var first = Assert.Single(watcher.Reports);
        Assert.Equal((2u, TargetStatus.Ok), (first.ObjectId, first.Status));
        Assert.Equal(target.Position, first.TargetPosition);
        Assert.Equal(TargetStatus.Ok, watcher.Targets.Target!.Value.Status);
        Assert.Equal(Vector3.UnitX, watcher.Targets.Target!.Value.InterpolatedHeading);

        // Moving 0.4 m: no report; past 0.5 m: one, at the next half-second check.
        target.Position = MoverHost.At(20.4f, 10);
        target.Time = 0.5;
        target.Targets.HandleTargetting(0.5);
        Assert.Single(watcher.Reports);
        target.Position = MoverHost.At(20.6f, 10);
        target.Time = 0.7;
        target.Targets.HandleTargetting(0.7); // within the half second: nothing checked
        Assert.Single(watcher.Reports);
        target.Time = 1.0;
        target.Targets.HandleTargetting(1.0);
        Assert.Equal(2, watcher.Reports.Count);
        Assert.Equal(20.6f, watcher.Reports[^1].TargetPosition.Origin.X, 4);
    }

    [Fact]
    public void A_quantum_predicts_the_position_from_the_velocity()
    {
        var world = new TestWorld();
        var watcher = new MoverHost(1, world, MoverHost.At(10, 10));
        var target = new MoverHost(2, world, MoverHost.At(20, 10)) { Drift = new Vector3(2, 0, 0) };
        target.Tick(0.1); // measures the velocity
        watcher.Targets.SetTarget(0, 2, radius: 0.5f, quantum: 2.0);
        var first = watcher.Reports[^1];
        Assert.Equal(first.TargetPosition, first.InterpolatedPosition); // AddVoyeur reports the position itself

        // The half-second check predicts 2 s ahead at the measured velocity: 4 m further, past the radius, so it reports.
        target.Tick(0.4);
        var report = watcher.Reports[^1];
        Assert.Equal(20.2f + 0.8f, report.TargetPosition.Origin.X, 3);
        Assert.Equal(report.TargetPosition.Origin.X + 4f, report.InterpolatedPosition.Origin.X, 3);
        Assert.Equal(2f, report.Velocity.X, 3);
    }

    [Fact]
    public void Leaving_the_world_tells_the_voyeurs_and_clears_their_target()
    {
        var world = new TestWorld();
        var watcher = new MoverHost(1, world, MoverHost.At(10, 10));
        var target = new MoverHost(2, world, MoverHost.At(20, 10));
        watcher.Targets.SetTarget(0, 2, 0.5f, 0.0);
        target.Targets.NotifyVoyeurOfEvent(TargetStatus.ExitWorld);
        Assert.Equal(TargetStatus.ExitWorld, watcher.Reports[^1].Status);
        Assert.Null(watcher.Targets.Target);
        Assert.False(target.Targets.HasVoyeur(1));
    }

    [Fact]
    public void An_unknown_target_times_out_after_ten_seconds_and_id_zero_at_once()
    {
        var world = new TestWorld();
        var watcher = new MoverHost(1, world, MoverHost.At(10, 10));
        watcher.Targets.SetTarget(0, 0x77, 0.5f, 0.0);
        Assert.Empty(watcher.Reports);
        watcher.Time = 9.9;
        watcher.Targets.HandleTargetting(9.9);
        Assert.Empty(watcher.Reports);
        watcher.Time = 10.5;
        watcher.Targets.HandleTargetting(10.5);
        Assert.Equal(TargetStatus.TimedOut, Assert.Single(watcher.Reports).Status);

        watcher.Targets.SetTarget(0, 0, 0.5f, 0.0);
        Assert.Equal((0u, TargetStatus.TimedOut), (watcher.Reports[^1].ObjectId, watcher.Reports[^1].Status));
    }

    [Fact]
    public void A_stick_steps_to_the_targets_cylinder_less_the_gap_and_faces_it()
    {
        var world = new TestWorld();
        var follower = new MoverHost(1, world, MoverHost.At(10, 10, heading: 0f), radius: 0.4f);
        _ = new MoverHost(2, world, MoverHost.At(13, 10), radius: 0.6f);
        follower.Positions.StickTo(2, 0.6f, 1.8f, follower);
        var sticky = follower.Positions.Sticky!;
        Assert.True(sticky.Initialized); // the target reported at once
        Assert.Equal(2u, follower.Positions.GetStickyObjectId());

        // 3 m apart, radii 0.4 + 0.6, gap 0.3: 1.7 m to go, capped at 5 × 4 m/s × quantum.
        var offset = AcFrame.Identity;
        var ctx = PositionContext.For(follower.Position, true, false, follower.Motion, 2);
        follower.Positions.AdjustOffset(ref offset, 1.0 / 30.0, ctx);
        Assert.Equal(20f / 30f, offset.Origin.Length(), 4);
        Assert.Equal(90f, PositionFrames.Heading(offset.Orientation), 2); // the turn that faces east
        Assert.True(Vector3.Transform(offset.Origin, follower.Position.Orientation).X > 0.6f); // towards the target

        offset = AcFrame.Identity;
        follower.Positions.AdjustOffset(ref offset, 1.0, ctx);
        Assert.Equal(1.7f, offset.Origin.Length(), 4);
    }

    [Fact]
    public void A_stick_lasts_one_second_and_ends_on_any_report_but_ok()
    {
        var world = new TestWorld();
        var follower = new MoverHost(1, world, MoverHost.At(10, 10));
        var target = new MoverHost(2, world, MoverHost.At(12, 10));
        follower.Time = 5;
        follower.Positions.StickTo(2, 0.4f, 1.8f, follower);
        follower.Time = 5.9;
        follower.Positions.UseTime(default);
        Assert.Equal(2u, follower.Positions.GetStickyObjectId());
        follower.Time = 6.01;
        follower.Positions.UseTime(default);
        Assert.Equal(0u, follower.Positions.GetStickyObjectId());
        Assert.Null(follower.Targets.Target);

        follower.Positions.StickTo(2, 0.4f, 1.8f, follower);
        target.Targets.NotifyVoyeurOfEvent(TargetStatus.Teleported);
        Assert.Equal(0u, follower.Positions.GetStickyObjectId());
    }
}
