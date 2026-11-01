using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Physics;

/// <summary>
/// M5 item 5 F7 (D74): the interpolation manager's progress check counts update steps at retail's rate
/// (<c>InterpolationManager::adjust_offset</c>, 11.6096 0x00556950, behind <c>CPhysics::UseTime</c>'s 30 Hz gate, 11.6096
/// 0x0050A420; <see cref="InterpolationManager.UpdateInterval"/>), so a tusker (Setup 0x02000964) held back by another tusker
/// blips at the same object time whether the avatars are stepped at 30, 60 or 240 Hz. Before D74 a "frame" was a drawn frame,
/// and at 240 Hz a tusker held for a tenth of a second was blipped (round 2 of item 5's live gate: 60 blips in 75 s). The scenes
/// are <see cref="ServerObjectTransitionTests"/>' Holtburg with the server-object transitions on.
/// </summary>
public sealed class InterpolationRateTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const uint Blocker = 0x70000001;
    private const uint Held = 0x70000002;

    /// <summary>Open ground south of Holtburg's buildings (<see cref="ServerObjectTransitionTests"/>' OpenSpot).</summary>
    private static readonly Vector3 OpenSpot = new(106f, 16f, 0f);

    /// <summary>
    /// Held by a standing tusker for 0.3 s and then let through, a tusker following a line of server positions does not blip at
    /// 240 Hz, as at 30 and 60 Hz, and reaches the end of the line; counting every drawn frame (before D74) the same hold blips
    /// at 240 Hz, which proves the scene holds it.
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_held_by_another_for_0_3_s_does_not_blip_at_any_rate()
    {
        foreach (var hz in new[] { 30, 60, 240 })
        {
            var run = Hold(hz, holdSeconds: 0.3, InterpolationManager.DefaultUpdateInterval);
            output.WriteLine($"{hz} Hz, held 0.3 s: blips {run.Blips}, held {run.HeldFor:F2} s, ends {run.EndGap:F2} m from the last position");
            Assert.True(run.HeldFor >= 0.3, $"{hz} Hz: the tusker was never held");
            Assert.Equal(0L, run.Blips.Total);
            Assert.True(run.EndGap < 0.3f, $"{hz} Hz: the tusker did not reach the end of its line once let through: {run.EndGap:F2} m");
        }

        var before = Hold(240, holdSeconds: 0.3, interval: 0.0);
        output.WriteLine($"240 Hz counting every frame (before D74), held 0.3 s: blips {before.Blips}, first {before.BlipAfter:F3} s into the hold");
        Assert.True(before.Blips.Total >= 1, "counting every drawn frame, the 0.3 s hold did not blip: the scene proves nothing");
    }

    /// <summary>
    /// Held for 2 s, the tusker blips at about retail's time at every rate: four failed nodes of five update steps after the
    /// first check, about 25/30 s into the hold, the three rates within a quarter of a second of one another (before D74, about
    /// 0.1 s at 240 Hz).
    /// </summary>
    [RequiresDatsFact]
    public void A_tusker_held_for_2_s_blips_at_retails_time_at_every_rate()
    {
        var times = new List<double>();
        foreach (var hz in new[] { 30, 60, 240 })
        {
            var run = Hold(hz, holdSeconds: 2.0, InterpolationManager.DefaultUpdateInterval);
            output.WriteLine($"{hz} Hz, held 2 s: blips {run.Blips}, first {run.BlipAfter:F3} s into the hold");
            Assert.True(run.Blips.Total >= 1, $"{hz} Hz: the held tusker was not blipped: {run.Blips}");
            Assert.InRange(run.BlipAfter, 0.5, 1.4);
            times.Add(run.BlipAfter);
        }

        Assert.True(times.Max() - times.Min() <= 0.25, $"the blip time depends on the rate: {string.Join(", ", times.Select(t => t.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)))} s");
        var before = Hold(240, holdSeconds: 2.0, interval: 0.0);
        output.WriteLine($"240 Hz counting every frame (before D74), held 2 s: first blip {before.BlipAfter:F3} s into the hold");
        Assert.True(before.BlipAfter < 0.3, $"counting every drawn frame, the blip came at {before.BlipAfter:F3} s");
    }

    /// <summary>
    /// The offline measure (report only, a new judge): sixteen tuskers chasing a standing player in a den-like ring, fed server
    /// positions every 0.2 s that converge on a ring too small for all of them (as ACE's do) and shift around the player every
    /// 4 s, for 30 s at 240 Hz counting every drawn frame (before D74) and at retail's rate (after), and at 30 Hz for reference.
    /// Asserts only that the crowd moved and that the measure is no worse than before.
    /// </summary>
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void A_den_like_crowd_reports_its_blips_per_minute_before_and_after()
    {
        var before = Crowd(240, interval: 0.0);
        var after = Crowd(240, InterpolationManager.DefaultUpdateInterval);
        var reference = Crowd(30, InterpolationManager.DefaultUpdateInterval);
        output.WriteLine($"240 Hz before D74 (every drawn frame): {before.PerMinute:F1} blips a minute ({before.Blips}), {before.Seconds:F1} s wall");
        output.WriteLine($"240 Hz after D74 (retail's update rate): {after.PerMinute:F1} blips a minute ({after.Blips}), {after.Seconds:F1} s wall");
        output.WriteLine($"30 Hz (retail's rate either way): {reference.PerMinute:F1} blips a minute ({reference.Blips})");
        Assert.True(before.Transitions > 0 && after.Transitions > 0, "the crowd ran no transitions: it is not moving");
        Assert.True(after.PerMinute <= before.PerMinute, $"retail's rate blipped more than the render rate: {after.PerMinute:F1} against {before.PerMinute:F1}");
    }

    private (AvatarBlipCauses Blips, double HeldFor, double BlipAfter, float EndGap) Hold(int hz, double holdSeconds, double interval)
    {
        var frame = 1.0 / hz;
        var scene = new ServerObjectTransitionTests.Scene(fixture.Dats, transitions: true, withPlayer: false, frame: frame);
        scene.Tusker(Blocker, Ground(OpenSpot), heading: 0f);
        var held = scene.Tusker(Held, Ground(OpenSpot + new Vector3(-5f, 0f, 0f)), heading: 90f);
        held.Positions.Interpolation.UpdateInterval = interval;
        scene.Advance(10);

        // A line of server positions through the blocker (within the 10 m constraint start), as a chase sends them.
        var last = Ground(OpenSpot);
        for (var k = 0; k <= 12; k++)
        {
            last = Ground(OpenSpot + new Vector3(-4f + (0.6f * k), 0f, 0f));
            Assert.Equal(AvatarApply.Applied, scene.Set.Move(Held, Facing(last, 90f), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
        }

        var start = double.NaN;
        var blipAt = double.NaN;
        var released = false;
        var limit = (int)Math.Ceiling(6.0 * hz);
        for (var f = 0; f < limit; f++)
        {
            scene.Advance(1);
            if (double.IsNaN(start) && held.ShortRun > 0)
            {
                start = scene.Now;
            }

            if (double.IsNaN(blipAt) && scene.Set.Stats.BlipCauses.Total > 0)
            {
                blipAt = scene.Now;
            }

            if (!released && !double.IsNaN(start) && scene.Now - start >= holdSeconds)
            {
                var far = Ground(OpenSpot + new Vector3(-30f, 0f, 0f));
                Assert.Equal(AvatarApply.Applied, scene.Set.Move(Blocker, Facing(far, 0f), teleportAdvanced: true, hasContact: true, placement: null, scene.Now));
                released = true;
            }
        }

        var gap = Vector2.Distance(new Vector2(held.Position.Origin.X, held.Position.Origin.Y), new Vector2(last.X, last.Y));
        var heldFor = double.IsNaN(start) ? 0.0 : (released ? holdSeconds : scene.Now - start);
        return (scene.Set.Stats.BlipCauses, heldFor, double.IsNaN(blipAt) || double.IsNaN(start) ? double.NaN : blipAt - start, gap);
    }

    private (AvatarBlipCauses Blips, double PerMinute, long Transitions, double Seconds) Crowd(int hz, double interval)
    {
        const int count = 16;
        const double seconds = 30.0;
        const double sendEvery = 0.2;
        const float serverSpeed = 2.5f;
        var watch = Stopwatch.StartNew();
        var centre = Ground(new Vector3(106f, 10f, 0f));
        var scene = new ServerObjectTransitionTests.Scene(fixture.Dats, transitions: true, withPlayer: true, playerAt: centre, frame: 1.0 / hz);
        var random = new Random(74);
        var server = new Vector3[count];
        var angles = new float[count];
        var bodies = new AvatarBody[count];
        for (var i = 0; i < count; i++)
        {
            angles[i] = i * (MathF.Tau / count);
            server[i] = Ground(centre + (Direction(angles[i]) * 7f));
            bodies[i] = scene.Tusker(0x70000001u + (uint)i, server[i], HeadingTo(server[i], centre));
            bodies[i].Positions.Interpolation.UpdateInterval = interval;
        }

        scene.Advance(10);
        var startTransitions = scene.Host.Stats.Transitions;
        var sends = (int)Math.Round(seconds / sendEvery);
        var framesPerSend = (int)Math.Round(sendEvery * hz);
        for (var s = 0; s < sends; s++)
        {
            var t = s * sendEvery;
            for (var i = 0; i < count; i++)
            {
                if (s % 20 == i % 20 && t > 0)
                {
                    angles[i] += (float)((random.NextDouble() - 0.5) * (Math.PI / 1.5)); // a new place around the player every 4 s, staggered
                }

                var goal = centre + (Direction(angles[i]) * 1.3f);
                var toGoal = new Vector2(goal.X - server[i].X, goal.Y - server[i].Y);
                var step = serverSpeed * (float)sendEvery;
                if (toGoal.Length() <= InterpolationManager.DesiredDistance)
                {
                    continue;
                }

                var move = toGoal.Length() > step ? Vector2.Normalize(toGoal) * step : toGoal;
                server[i] = Ground(server[i] + new Vector3(move.X, move.Y, 0f));
                Assert.Equal(AvatarApply.Applied, scene.Set.Move(0x70000001u + (uint)i, Facing(server[i], HeadingTo(server[i], centre)), teleportAdvanced: false, hasContact: true, placement: null, scene.Now));
            }

            scene.Advance(framesPerSend);
        }

        var blips = scene.Set.Stats.BlipCauses;
        return (blips, blips.Total * 60.0 / seconds, scene.Host.Stats.Transitions - startTransitions, watch.Elapsed.TotalSeconds);
    }

    private static Vector3 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle), 0f);

    /// <summary>The heading (degrees, retail's) that faces <paramref name="to"/> from <paramref name="from"/>, as <see cref="ServerObjectTransitionTests"/>' crowd faces the player.</summary>
    private static float HeadingTo(Vector3 from, Vector3 to) =>
        (float)((Math.Atan2(-(from.X - to.X), -(from.Y - to.Y)) * 180.0 / Math.PI) + 360.0) % 360f;

    private static CellFrame Facing(Vector3 at, float heading) =>
        new(ServerObjectPhysicsTests.OutdoorAt(at).CellId, new AcFrame(at, PositionFrames.WithHeading(Quaternion.Identity, heading)));

    private Vector3 Ground(Vector3 at) => ServerObjectPhysicsTests.OnGround(fixture.Dats, at);
}
