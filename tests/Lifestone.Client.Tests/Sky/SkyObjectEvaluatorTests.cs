using Lifestone.Client.Navigation;
using Lifestone.Client.Sky;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Tests.Sky;

/// <summary>
/// The allocation-free object evaluator gives the same bits as <see cref="SkyEvaluator.GetSky"/>, and the clock's start
/// rule keeps automated runs reproducible.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class SkyObjectEvaluatorTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    [RequiresDatsFact]
    public void Matches_GetSky_for_every_day_group_at_keyframes_window_edges_and_a_dense_grid()
    {
        var evaluator = new SkyObjectEvaluator();
        var compared = 0;
        foreach (var group in fixture.Sky.Sky.DayGroups)
        {
            var times = new SortedSet<float>();
            for (var i = 0; i <= 2_000; i++)
            {
                times.Add(i / 2_000f);
            }

            foreach (var k in group.Keyframes)
            {
                times.UnionWith([k.Begin, BitDecrement(k.Begin), BitIncrement(k.Begin)]);
            }

            foreach (var o in group.SkyObjects)
            {
                times.UnionWith([o.BeginTime, o.EndTime, BitIncrement(o.BeginTime), BitDecrement(o.EndTime)]);
            }

            foreach (var t in times.Where(t => t >= 0f && t < 1f))
            {
                var expected = SkyEvaluator.GetSky(group, t).Select(SkyObjectFrame.From).ToArray();
                Assert.Equal(expected, evaluator.Evaluate(group, t).ToArray());
                compared++;
            }
        }

        Assert.True(compared > 20 * 2_000);
    }

    [RequiresDatsFact]
    public void Automated_runs_start_paused_on_a_sunny_day_at_the_given_fraction_whatever_the_date()
    {
        var sky = fixture.Sky;

        // --screenshot / --fly-path with --time-of-day=F: day group 0 at fraction F, paused (render-contract.md §1).
        foreach (var fraction in new[] { 0.08, 0.3, 0.5, 0.9 })
        {
            var start = SkyClockStart.Resolve(automated: true, gameTime: null, timeOfDay: fraction, speed: null, sky);
            Assert.Equal(0.0, start.Speed);
            Assert.Null(start.DayFraction);
            Assert.Equal(GoldenViewpoints.Sunny, sky.DayGroupIndexAt(start.GameTime!.Value));
            Assert.Equal(fraction, sky.Calendar.ToDate(start.GameTime.Value).DayFractionExact, 9);
        }

        // No time option: Midsong of that day, paused (Solclaim 10, 10 P.Y.).
        var noon = SkyClockStart.Resolve(automated: true, gameTime: null, timeOfDay: null, speed: null, sky);
        Assert.Equal(new SkyClockStart(GoldenViewpoints.GameTimeFor(new GoldenTime(0.5, 0), sky), 0.0, null), noon);
        Assert.Equal("Solclaim 10, 10 P.Y.", sky.Calendar.ToDate(noon.GameTime!.Value).DateString);

        // --game-time and --time-speed are honoured; --time-of-day then moves within that day.
        Assert.Equal(new SkyClockStart(1234.0, 0.0, 0.25), SkyClockStart.Resolve(true, 1234.0, 0.25, null, sky));
        Assert.Equal(600.0, SkyClockStart.Resolve(true, null, 0.5, 600.0, sky).Speed);
    }

    [RequiresDatsFact]
    public void Interactive_runs_start_on_the_live_clock_at_the_retail_rate()
    {
        Assert.Equal(new SkyClockStart(null, 1.0, null), SkyClockStart.Resolve(false, null, null, null, fixture.Sky));
        Assert.Equal(new SkyClockStart(null, 1.0, 0.86), SkyClockStart.Resolve(false, null, 0.86, null, fixture.Sky));
        Assert.Equal(new SkyClockStart(99.0, 60.0, null), SkyClockStart.Resolve(false, 99.0, null, 60.0, fixture.Sky));
        Assert.Equal(Math.BitDecrement(1.0), SkyClockStart.Resolve(false, null, 7.0, null, fixture.Sky).DayFraction);
    }

    private static float BitIncrement(float f) => MathF.BitIncrement(f);

    private static float BitDecrement(float f) => MathF.BitDecrement(f);
}
