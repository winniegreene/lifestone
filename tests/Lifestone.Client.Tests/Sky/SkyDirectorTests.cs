using Lifestone.Client.Sky;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Tests.Sky;

/// <summary>
/// The per-frame sky driver on the user's Region: retail's fog and light cadence (<c>LScape::UseTime</c>: fog every
/// 0.8 s, light every 15 s of <c>Timer::cur_time</c>), immediate refreshes on jumps, the model list and equality with
/// <see cref="DerethSky.Evaluate(double)"/>. No per-frame garbage: <see cref="SkyDirectorAllocationTests"/> (not parallel).
/// </summary>
public sealed class SkyDirectorTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    private double Noon(int group = 0) => Lifestone.Client.Navigation.GoldenViewpoints.GameTimeFor(new(0.5, group), fixture.Sky);

    [RequiresDatsFact]
    public void First_frame_lights_and_fogs_then_the_retail_cadence_applies()
    {
        var director = new SkyDirector(fixture.Sky);
        var t0 = Noon();
        var first = director.Update(t0, clientClock: 100.0);
        Assert.True(first.LightingChanged && first.FogChanged);
        var firstLighting = first.Lighting;
        Assert.Equal(fixture.Sky.Evaluate(t0).Lighting, firstLighting);

        // Within the 0.8 s fog tick nothing is re-read, even though the sky moved.
        var later = director.Update(t0 + 0.5, clientClock: 100.5);
        Assert.False(later.LightingChanged || later.FogChanged);
        Assert.Equal(firstLighting, later.Lighting);

        // 0.8 s (tick_size is the float 0.8f, a hair above 0.8): fog only. The light waits for its 15 s tick.
        var fogTick = director.Update(t0 + 0.81, clientClock: 100.81);
        Assert.True(fogTick.FogChanged);
        Assert.False(fogTick.LightingChanged);

        var lightTick = director.Update(t0 + 15.5, clientClock: 115.5);
        Assert.True(lightTick.LightingChanged);
        Assert.Equal(fixture.Sky.Evaluate(t0 + 15.5).Lighting, lightTick.Lighting);
    }

    [RequiresDatsFact]
    public void A_jump_refreshes_light_and_fog_at_once_even_with_a_paused_clock()
    {
        var director = new SkyDirector(fixture.Sky);
        var noon = Noon();
        director.Update(noon, clientClock: 0.0);
        var night = fixture.Sky.Calendar.WithDayFraction(noon, 0.08);
        var stale = director.Update(night, clientClock: 0.0);
        Assert.False(stale.LightingChanged); // paused at clock 0: the cadence alone would never relight

        var jumped = director.Update(night, clientClock: 0.0, discontinuity: true);
        Assert.True(jumped.LightingChanged && jumped.FogChanged);
        Assert.Equal(fixture.Sky.Evaluate(night).Lighting, jumped.Lighting);
        Assert.Equal(400f, jumped.Fog.End); // night fog 0-400 m

        var still = director.Update(night, clientClock: 0.0);
        Assert.True(still.Unchanged);
        Assert.False(still.LightingChanged || still.FogChanged || still.ObjectsChanged);
    }

    [RequiresDatsFact]
    public void A_forced_day_group_is_used_and_refreshes_the_light()
    {
        var director = new SkyDirector(fixture.Sky);
        var noon = Noon();
        director.Update(noon, 0.0);
        director.ForcedDayGroup = 3;
        var rainy = director.Update(noon, 0.0);
        Assert.Equal(3, rainy.DayGroup.Index);
        Assert.True(rainy.LightingChanged);
        Assert.Equal(1500f, rainy.Fog.End); // Rainy days: fog 150-1,500 m
        Assert.Equal(3, director.DayGroupAt(noon));
        director.ForcedDayGroup = null;
        Assert.Equal(0, director.DayGroupAt(noon));
        Assert.Throws<ArgumentOutOfRangeException>(() => director.ForcedDayGroup = 20);
    }

    [RequiresDatsFact]
    public void Every_frame_matches_DerethSky_Evaluate_across_days_and_their_boundaries()
    {
        var director = new SkyDirector(fixture.Sky);
        var calendar = fixture.Sky.Calendar;
        var start = Noon();
        var dayBegin = calendar.ToDate(start).DayBeginTime;
        var times = new List<double>();

        // Three days at an odd step (keyframes, window edges and the day changes fall between and on samples), plus
        // the instants around two day boundaries.
        for (var t = start; t < start + (3 * calendar.DayLength); t += 37.3)
        {
            times.Add(t);
        }

        foreach (var edge in new[] { dayBegin + calendar.DayLength, dayBegin + (2 * calendar.DayLength) })
        {
            times.AddRange([edge - 0.01, edge - 1e-4, edge, edge + 1e-4, edge + 0.01]);
        }

        times.Sort();
        foreach (var t in times)
        {
            var frame = director.Update(t, clientClock: t, discontinuity: true); // a refresh re-reads light and fog
            var expected = fixture.Sky.Evaluate(t);
            Assert.Equal(expected.DayGroup.Index, frame.DayGroup.Index);
            Assert.Equal(expected.TimeOfDay, frame.TimeOfDay);
            Assert.Equal(expected.Lighting, frame.Lighting);
            Assert.Equal(expected.Fog, frame.Fog);
            Assert.Equal(SkyObjectFrame.From(expected), frame.Objects.ToArray());
            Assert.Equal(expected.Date!.DateString, frame.Date.DateString);
            Assert.Equal(expected.Date.TimeString, frame.Date.TimeString);
        }
    }

    [RequiresDatsFact]
    public void Model_ids_cover_defaults_and_replacements()
    {
        var ids = SkyDirector.ModelIds(fixture.Sky.Sky.DayGroups[0]);
        Assert.Equal(
            new uint[] { 0x01001348, 0x010015EE, 0x010015EF, 0x01001F67, 0x01001F6A, 0x01004C36, 0x02000714 },
            ids.OrderBy(i => i).ToArray());
        Assert.All(fixture.Sky.Sky.DayGroups, g => Assert.InRange(SkyDirector.ModelIds(g).Count, 7, 15));
    }

    [RequiresDatsFact]
    public void Cloud_scroll_runs_on_the_client_clock_and_wraps_only_for_wrap_addressing()
    {
        var director = new SkyDirector(fixture.Sky);
        var noon = Noon();
        director.Update(noon, 0.0);
        director.Update(noon, 10.0);
        var raw = director.Scroll.GetOffset(0x01004C36); // TexVelocity (-0.013, 0.013) per second
        Assert.Equal(-0.13f, raw.X, 4);
        Assert.Equal(0.13f, raw.Y, 4);
        var wrapped = director.ScrollOffset(0x01004C36, wrapAddressing: true);
        Assert.Equal(0.87f, wrapped.X, 4);
        Assert.Equal(raw, director.ScrollOffset(0x01004C36, wrapAddressing: false));
    }
}
