using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>
/// Parsed Region SkyDesc / GameTime against the user's end-of-retail client_portal.dat. Expected values come from the
/// research dumps in docs/research/rendering.md §8 ("Data and time", "Sky objects") and docs/research/local-scout.md
/// (Region 0x13000000 GameTime), both produced by independent Python readers.
/// </summary>
public sealed class SkyDescriptionTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    private SkyDescription Sky => fixture.Sky.Sky;

    [RequiresDatsFact]
    public void Region_has_20_day_groups_with_the_scouted_names_and_unused_chances()
    {
        Assert.Equal(20, Sky.DayGroups.Count);

        // rendering.md §8: "20 DayGroups: Sunny ×6, Clear ×3, Cloudy ×3, Rainy ×8. Each has ChanceOfOccur 5.0".
        var names = Sky.DayGroups.GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, names["Sunny"]);
        Assert.Equal(3, names["Clear"]);
        Assert.Equal(3, names["Cloudy"]);
        Assert.Equal(8, names["Rainy"]);
        Assert.All(Sky.DayGroups, g => Assert.Equal(5f, g.ChanceOfOccur));
        Assert.Equal(Enumerable.Range(0, 20), Sky.DayGroups.Select(g => g.Index));
    }

    [RequiresDatsFact]
    public void Tick_sizes_match_the_dat()
    {
        // rendering.md §8: "SkyDesc TickSize 0.8 s, LightTickSize 15 s". TickSize is stored as a double holding 0.8f.
        Assert.Equal((double)0.8f, Sky.TickSize);
        Assert.Equal(15.0, Sky.LightTickSize);
    }

    [RequiresDatsFact]
    public void Object_and_keyframe_counts_per_day_group()
    {
        // Counts from the Python region dump (scratchpad research/render/region_dump.txt, mirrored by this parser).
        int[] rainy13 = [3, 7, 9, 15, 16, 17];
        int[] rainy11 = [18, 19];
        foreach (var g in Sky.DayGroups)
        {
            var (objects, keys) = rainy13.Contains(g.Index) ? (19, 13) : rainy11.Contains(g.Index) ? (17, 11) : (7, 11);
            Assert.True(objects == g.SkyObjects.Count, $"{g}: expected {objects} objects");
            Assert.True(keys == g.Keyframes.Count, $"{g}: expected {keys} keyframes");
        }

        Assert.Equal(232, Sky.DayGroups.Sum(g => g.Keyframes.Count));
    }

    [RequiresDatsFact]
    public void Keyframes_are_sorted_start_at_midnight_and_all_have_fog_on()
    {
        foreach (var g in Sky.DayGroups)
        {
            Assert.Equal(0f, g.Keyframes[0].Begin);
            for (var k = 1; k < g.Keyframes.Count; k++)
            {
                Assert.True(g.Keyframes[k].Begin > g.Keyframes[k - 1].Begin, $"{g} keyframe {k}");
            }

            Assert.All(g.Keyframes, k => Assert.True(k.Begin < 1f));
            Assert.All(g.Keyframes, k => Assert.Equal(1u, k.WorldFogMode));
            Assert.All(g.Keyframes, k => Assert.True(k.MinWorldFog <= k.MaxWorldFog));
        }
    }

    [RequiresDatsFact]
    public void Day_group_0_keyframe_at_0_27_matches_the_research_row()
    {
        // rendering.md §8: "b=0.27 DirBright .7 H90 P20 DirColor FAD797 AmbBright .35 AmbColor E6E6FF fog[150,2400] C3C8DC".
        var k = Sky.DayGroups[0].Keyframes.Single(x => x.Begin == 0.27f);
        Assert.Equal(0.7f, k.DirBright);
        Assert.Equal(90f, k.DirHeading);
        Assert.Equal(20f, k.DirPitch);
        Assert.Equal(0xFFFAD797u, k.DirColor.Argb);
        Assert.Equal(0.35f, k.AmbBright);
        Assert.Equal(0xFFE6E6FFu, k.AmbColor.Argb);
        Assert.Equal(150f, k.MinWorldFog);
        Assert.Equal(2400f, k.MaxWorldFog);
        Assert.Equal(0xFFC3C8DCu, k.WorldFogColor.Argb);
    }

    [RequiresDatsFact]
    public void Night_dawn_and_day_fog_ranges_match_the_research()
    {
        // rendering.md §8: "Dat values: night [0, 400] m, dawn [90, 800] m, day [150, 2400] m" (day group 0).
        var keys = Sky.DayGroups[0].Keyframes;
        Assert.Equal((0f, 400f), (keys[0].MinWorldFog, keys[0].MaxWorldFog));
        Assert.Equal((90f, 800f), (keys[3].MinWorldFog, keys[3].MaxWorldFog));
        Assert.Equal((150f, 2400f), (keys[4].MinWorldFog, keys[4].MaxWorldFog));
    }

    [RequiresDatsFact]
    public void Day_group_0_sky_objects_are_the_scouted_geometry()
    {
        // rendering.md §8 "Geometry from the dat": dome 0x010015EE, stars 0x010015EF, clouds 0x01004C36 with
        // TexVelocity (-0.013, 0.013), sun 0x01001348.
        var objects = Sky.DayGroups[0].SkyObjects;
        Assert.Equal(0x010015EEu, objects[0].DefaultGfxObjId);
        Assert.Equal(0x010015EFu, objects[1].DefaultGfxObjId);
        Assert.Equal(0x01004C36u, objects[4].DefaultGfxObjId);
        Assert.Equal(new Vector2(-0.013f, 0.013f), objects[4].TexVelocity);
        Assert.Equal(SkyObjectProperties.HiddenUnderFogOverride, objects[4].Properties);
        Assert.Equal(0x01001348u, objects[5].DefaultGfxObjId);
        Assert.Equal((0.16f, 0.94f, -23f, 203f), (objects[5].BeginTime, objects[5].EndTime, objects[5].BeginAngle, objects[5].EndAngle));
    }

    [RequiresDatsFact]
    public void Heading_flips_from_east_to_west_at_the_zenith_between_0_610_and_0_611()
    {
        // rendering.md §8: "The data has a 0.610→0.611 keyframe pair that flips heading from 90° to 270° at zenith."
        foreach (var g in Sky.DayGroups)
        {
            var noon = g.Keyframes.Where(k => k.DirPitch == 90f).ToArray();
            Assert.Contains(noon, k => k.DirHeading == 90f);
            Assert.Contains(noon, k => k.Begin == 0.611f && k.DirHeading == 270f);
        }
    }

    [RequiresDatsFact]
    public void Replacements_reference_existing_objects_and_never_swap_geometry_in_this_dat()
    {
        foreach (var g in Sky.DayGroups)
        {
            foreach (var k in g.Keyframes)
            {
                Assert.All(k.Replacements, r => Assert.True(r.ObjectIndex < g.SkyObjects.Count));

                // Data fact behind SkyEvaluator.GetSky's "a replacement GfxObj makes an object drawn outside its window"
                // rule: the end-of-retail Region never uses a replacement GfxObj, so that path is synthetic-tested only.
                Assert.All(k.Replacements, r => Assert.Equal(0u, r.GfxObjId));
            }
        }
    }

    [RequiresDatsFact]
    public void Property_bits_are_the_known_set()
    {
        var all = Sky.DayGroups.SelectMany(g => g.SkyObjects).Select(o => (uint)o.Properties).Distinct().Order().ToArray();
        Assert.Equal([0u, 2u, 4u, 5u, 0xDu], all);
    }

    [RequiresDatsFact]
    public void Game_time_calendar_matches_the_scouted_region()
    {
        // local-scout.md: "GameTime: day_length 7620 s, 360 days/year, zero_year 10, "P.Y.", 16 times of day
        // (Darktide…Gloaming-and-Half), 6 weekdays, 12 seasons (Morningthaw…Wintersebb)"; ZeroTimeOfYear 3600 from the
        // Python region dump.
        var c = fixture.Calendar;
        Assert.Equal(3600.0, c.ZeroTimeOfYear);
        Assert.Equal(10, c.ZeroYear);
        Assert.Equal(7620f, c.DayLength);
        Assert.Equal(360, c.DaysPerYear);
        Assert.Equal(2743200.0, c.YearLength);
        Assert.Equal("P.Y.", c.YearSpec);
        Assert.Equal(16, c.TimesOfDay.Count);
        Assert.Equal("Darktide", c.TimesOfDay[0].Name);
        Assert.Equal("Morntide-and-Half", c.TimesOfDay[7].Name);
        Assert.Equal("Gloaming-and-Half", c.TimesOfDay[15].Name);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(i / 16f, c.TimesOfDay[i].Begin);

            // rendering.md §8: night for fractions < 0.25 and >= 0.875.
            Assert.Equal(i < 4 || i >= 14, c.TimesOfDay[i].IsNight);
        }

        Assert.Equal(6, c.WeekDayNames.Count);
        Assert.Equal(12, c.Seasons.Count);
        Assert.Equal("Morningthaw", c.Seasons[0].Name);
        Assert.Equal("Wintersebb", c.Seasons[11].Name);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => i * 30), c.Seasons.Select(s => s.FirstDay));
    }
}
