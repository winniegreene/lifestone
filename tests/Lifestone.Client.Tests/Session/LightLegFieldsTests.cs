using System.Numerics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session;

/// <summary>M3 4.1 lane c: the fight leg's light fields (<c>bolt_light</c>, <c>dyn_lights_max</c>, <c>viewer_light</c>), the <c>viewer</c> review frame's trigger and the death frame judge. No dats, no Godot.</summary>
public sealed class LightLegFieldsTests
{
    private const uint Cell = 0xA9B40019;
    private const uint Bolt = 0x80000401;
    private const uint Bolt2 = 0x80000402;

    private static CellFrame At(float x) => new(Cell, new AcFrame(new Vector3(x, 0f, 0f), Quaternion.Identity));

    /// <summary>A frame holding the viewer light (when asked) and a light for each owner.</summary>
    private static DynamicLightFrame FrameOf(bool viewer, params uint[] owners)
    {
        var list = new DynamicLightList { ViewerLightEnabled = viewer };
        foreach (var owner in owners)
        {
            Assert.True(list.Upsert(owner, Cell, new Vector3(owner & 0xFF, 0f, 0f), Vector3.One, 1f, 5f, on: true));
        }

        list.Update(At(0f), null);
        var frame = new DynamicLightFrame();
        frame.Fill(list);
        return frame;
    }

    private static void Flight(LightFrameSampler s, uint guid, int frames, int lit)
    {
        var withLight = FrameOf(true, guid);
        var without = FrameOf(true);
        for (var i = 0; i < frames; i++)
        {
            s.BeginFrame(i < lit ? withLight : without);
            s.Projectile(guid);
            s.EndFrame();
        }

        s.BeginFrame(without); // the projectile is gone: its flight closes
        s.EndFrame();
    }

    [Fact]
    public void Bolt_light_holds_at_90_percent_of_the_flight_and_fails_at_89()
    {
        var at90 = new LightFrameSampler();
        Flight(at90, Bolt, 100, 90);
        var at89 = new LightFrameSampler();
        Flight(at89, Bolt, 100, 89);

        Assert.True(at90.BoltLight);
        Assert.Equal(1, at90.FlightsJudged);
        Assert.False(at89.BoltLight);
        Assert.Equal(0, at89.FlightsLit);
    }

    [Fact]
    public void Bolt_light_needs_every_judged_flight_and_at_least_one_and_skips_the_two_frame_flight()
    {
        var s = new LightFrameSampler();
        Assert.False(s.BoltLight); // nothing flew
        Flight(s, Bolt, 20, 20);
        Flight(s, Bolt2, 2, 0); // too short to judge
        Assert.True(s.BoltLight);
        Assert.Equal(1, s.ShortFlights);

        Flight(s, Bolt2, 20, 0); // a bolt whose state said lit and the frame did not hold it
        Assert.False(s.BoltLight);
    }

    [Fact]
    public void A_flight_still_alive_is_judged_by_finish()
    {
        var s = new LightFrameSampler();
        var lit = FrameOf(true, Bolt);
        for (var i = 0; i < 10; i++)
        {
            s.BeginFrame(lit);
            s.Projectile(Bolt);
            s.EndFrame();
        }

        Assert.False(s.BoltLight); // not closed yet
        s.Finish();
        Assert.True(s.BoltLight);
    }

    [Fact]
    public void The_flight_ends_when_the_lighting_state_falls_so_a_lingering_avatar_does_not_count()
    {
        var s = new LightFrameSampler();
        var lit = FrameOf(true, Bolt);
        var dark = FrameOf(true);
        for (var i = 0; i < 20; i++)
        {
            s.BeginFrame(lit);
            s.Projectile(Bolt, lightsOn: true);
            s.EndFrame();
        }

        for (var i = 0; i < 300; i++) // the Explode cleared LightingOn; the avatar stays in the set
        {
            s.BeginFrame(dark);
            s.Projectile(Bolt, lightsOn: false);
            s.EndFrame();
        }

        s.Finish();
        Assert.True(s.BoltLight);
        Assert.Equal("20/20/20", s.FlightsText());
    }

    [Fact]
    public void A_projectile_whose_state_was_never_lit_is_counted_and_not_judged()
    {
        var s = new LightFrameSampler();
        var dark = FrameOf(true);
        for (var i = 0; i < 50; i++)
        {
            s.BeginFrame(dark);
            s.Projectile(Bolt, lightsOn: false);
            s.EndFrame();
        }

        Flight(s, Bolt2, 20, 20);
        s.Finish();
        Assert.Equal(1, s.StateOffFlights);
        Assert.Equal(1, s.FlightsJudged);
        Assert.True(s.BoltLight);
    }

    [Fact]
    public void Dyn_lights_max_is_the_most_in_one_frame_and_viewer_light_reads_the_last_frame()
    {
        var s = new LightFrameSampler();
        foreach (var frame in new[] { FrameOf(true), FrameOf(true, Bolt, Bolt2), FrameOf(true, Bolt) })
        {
            s.BeginFrame(frame);
            s.EndFrame();
        }

        Assert.Equal(3, s.DynamicLightsMax); // the viewer light and two bolts
        Assert.True(s.ViewerLight);
        s.BeginFrame(FrameOf(false, Bolt));
        s.EndFrame();
        Assert.False(s.ViewerLight);
        Assert.Equal(3, s.DynamicLightsMax);
    }

    [Fact]
    public void The_leg_line_prints_the_three_fields_and_bolt_light_is_report_only_live_and_judged_offline()
    {
        var m = new FightLegMeasures(3, 912, 14, 2, 90_000, 0, 0, 1, 14, 31.0, 40.0, 58.5, 123.9, BoltLight: true, DynamicLightsMax: 3, ViewerLight: true);
        Assert.EndsWith(" fight_s=123 bolt_light=yes dyn_lights_max=3 viewer_light=on", FightLegVerdict.Compose(null, m).Line, StringComparison.Ordinal);

        var dark = m with { BoltLight = false, ViewerLight = false };
        var live = FightLegVerdict.Compose(null, dark);
        Assert.False(live.Ok); // judged in every leg since M3 4.2 (was report-only in 4.1)
        Assert.Contains(" bolt_light=no ", live.Line, StringComparison.Ordinal);
        Assert.Contains(" viewer_light=off", live.Line, StringComparison.Ordinal);
        var offline = FightLegVerdict.Compose(null, dark, judgeBoltLight: true);
        Assert.False(offline.Ok);
        Assert.Contains(offline.Problems, p => p.StartsWith("bolt_light=no", StringComparison.Ordinal));
        Assert.True(FightLegVerdict.Compose(null, m, judgeBoltLight: true).Ok);
    }

    [Fact]
    public void The_viewer_frame_is_due_only_after_the_walk_began_with_one_quiet_second_and_half_a_still_one_in_the_den()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 0.0;
        for (var i = 0; i < 100; i++) // never: out of the den, before the walk, a projectile alive, or moving
        {
            var k = i % 4;
            Assert.Equal(-1, p.Step(t += 0.05, 0, 0, 0, 0, projectileAlive: k == 0, inDen: k != 1, walked: k != 2, moving: k == 3));
        }

        // Quiet and still in the den but the walk has not begun (the arrival): never, however long.
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(-1, p.Step(t += 0.05, 0, 0, 0, 0, projectileAlive: false, inDen: true, walked: false, moving: false));
        }

        Assert.False(p.Owed);
        var due = -1;
        var start = t;
        while (due < 0 && t < start + 3)
        {
            due = p.Step(t += 0.05, 0, 0, 0, 0, projectileAlive: false, inDen: true, walked: true, moving: false);
        }

        Assert.Equal(3, due);
        Assert.InRange(t - start, FightShotPlan.QuietSeconds, FightShotPlan.QuietSeconds + 0.3);
        Assert.Equal("viewer", FightShotPlan.Names[due]);
        Assert.EndsWith("-viewer.png", FightShotPlan.ShotPath("shot.png", due), StringComparison.Ordinal);
    }

    [Fact]
    public void Movement_restarts_the_still_half_second()
    {
        var p = new FightShotPlan();
        p.Begin(0, 0, 0, 0);
        var t = 0.0;
        var due = -1;
        while (t < 1.2 && due < 0)
        {
            due = p.Step(t += 0.05, 0, 0, 0, 0, projectileAlive: false, inDen: true, walked: true, moving: true);
        }

        Assert.Equal(-1, due); // quiet for 1.2 s but never still
        var stillFrom = t;
        while (due < 0 && t < stillFrom + 2)
        {
            due = p.Step(t += 0.05, 0, 0, 0, 0, projectileAlive: false, inDen: true, walked: true, moving: false);
        }

        Assert.Equal(3, due);
        Assert.InRange(t - stillFrom, FightShotPlan.StillSeconds, FightShotPlan.StillSeconds + 0.3);
    }

    [Theory]
    [InlineData(50.0, true, false)]
    [InlineData(50.1, true, true)]
    [InlineData(75.0, true, true)]
    [InlineData(75.1, false, true)]
    public void The_death_frame_over_75_fails_and_over_50_prints_the_flag(double ms, bool ok, bool over50)
    {
        var good = new DeathLegMeasures(1, 0, 1, 40, DeathRespawn.Lifestone, 8, 0, 0, 75.6, true, 5, 4, 87, 1, 0.0, 20, ms, 10.1);
        var outcome = DeathLegVerdict.Compose(null, good);

        Assert.Equal(ok, outcome.Ok);
        Assert.Equal(over50, outcome.Line.EndsWith(" frame_over_50=yes", StringComparison.Ordinal));
        if (!ok)
        {
            Assert.Contains(outcome.Problems, p => p.StartsWith("death_frame_max_ms=75.1", StringComparison.Ordinal));
        }

        Assert.True(DeathLegVerdict.Compose(null, good with { DeathFrameMaxMs = null }).Ok); // unmeasured: not judged
    }
}
