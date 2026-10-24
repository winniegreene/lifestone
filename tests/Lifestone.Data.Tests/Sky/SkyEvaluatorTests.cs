using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>
/// Keyframe interpolation, lighting, fog and sky objects (CSkyDesc.c GetTimeOfDay/GetLighting/GetWorldFog/GetSky,
/// Sky.c GameSky::UseTime/CalcFrame) on synthetic day groups and on the user's Region.
/// </summary>
public sealed class SkyEvaluatorTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    private static readonly SkyObjectReplacement[] NoReplacements = [];

    private static SkyKeyframe Key(
        float begin,
        float heading = 90f,
        float pitch = 10f,
        float dirBright = 1f,
        float ambBright = 0.5f,
        uint dirColor = 0xFF000000u,
        uint ambColor = 0xFF000000u,
        float fogMin = 0f,
        float fogMax = 100f,
        uint fogColor = 0xFF000000u,
        uint fogMode = 1u,
        SkyObjectReplacement[]? replacements = null) =>
        new(begin, dirBright, heading, pitch, new AcColor(dirColor), ambBright, new AcColor(ambColor), fogMin, fogMax, new AcColor(fogColor), fogMode, replacements ?? NoReplacements);

    private static SkyObjectDesc Obj(int index, float begin, float end, float beginAngle = 0f, float endAngle = 0f, uint gfx = 0x01000001u) =>
        new(index, begin, end, beginAngle, endAngle, Vector2.Zero, gfx, 0u, SkyObjectProperties.None);

    private static SkyDayGroup Group(SkyKeyframe[] keys, SkyObjectDesc[]? objects = null) =>
        new(0, 5f, "Test", objects ?? [], keys);

    // ---- DayGroup::GetTimeOfDay ----

    [Fact]
    public void Keyframe_search_and_ratio()
    {
        var g = Group([Key(0f), Key(0.25f), Key(0.5f), Key(0.75f)]);
        Assert.Equal(new SkyKeyframeSpan(0, 1, 0f), SkyEvaluator.GetTimeOfDay(g, 0f));
        Assert.Equal(new SkyKeyframeSpan(0, 1, 0.5f), SkyEvaluator.GetTimeOfDay(g, 0.125f));
        Assert.Equal(new SkyKeyframeSpan(1, 2, 0f), SkyEvaluator.GetTimeOfDay(g, 0.25f)); // begin <= t picks the later key
        Assert.Equal(new SkyKeyframeSpan(2, 3, 0.5f), SkyEvaluator.GetTimeOfDay(g, 0.625f));

        // After the last key retail heads for keyframe 0 as if it sat at 1.0: ratio = (t - 0.75) / (1 - 0.75).
        Assert.Equal(new SkyKeyframeSpan(3, 0, 0.5f), SkyEvaluator.GetTimeOfDay(g, 0.875f));
        Assert.Null(SkyEvaluator.GetTimeOfDay(Group([]), 0.5f));
    }

    [Fact]
    public void Wrap_does_not_account_for_a_late_first_keyframe_and_early_times_extrapolate()
    {
        var g = Group([Key(0.2f), Key(0.6f)]);

        // t = 0.8: before = key 1 (0.6), after = key 0, ratio = (0.8 - 0.6) / (1 - 0.6) = 0.5, not 0.2 / 0.6.
        Assert.Equal(new SkyKeyframeSpan(1, 0, (float)((0.8f - (double)0.6f) / (1.0 - 0.6f))), SkyEvaluator.GetTimeOfDay(g, 0.8f));

        // t = 0.1 < first begin: retail keeps before = key 0 and extrapolates with a negative ratio.
        var early = SkyEvaluator.GetTimeOfDay(g, 0.1f)!.Value;
        Assert.Equal((0, 1), (early.BeforeIndex, early.AfterIndex));
        Assert.True(early.Ratio < 0f);
    }

    [Fact]
    public void Single_keyframe_lerps_to_itself()
    {
        var g = Group([Key(0.5f, ambBright: 0.7f)]);
        Assert.Equal(new SkyKeyframeSpan(0, 0, 0.5f), SkyEvaluator.GetTimeOfDay(g, 0.75f));
        Assert.Equal(0.7f, SkyEvaluator.GetLighting(g, 0.75f).AmbientLevelRaw);
    }

    // ---- SkyDesc::GetLighting ----

    [Fact]
    public void Lighting_at_and_between_keyframes()
    {
        var g = Group([
            Key(0f, heading: 90f, pitch: 0f, dirBright: 0.5f, ambBright: 0.4f, dirColor: 0xFF102030u, ambColor: 0xFF00FF80u),
            Key(0.5f, heading: 90f, pitch: 90f, dirBright: 1.0f, ambBright: 0.8f, dirColor: 0xFF305070u, ambColor: 0xFFFF0081u),
        ]);

        var at = SkyEvaluator.GetLighting(g, 0f);
        Assert.Equal(0.4f, at.AmbientLevelRaw);
        Assert.Equal(0.4f, at.AmbientLevel);
        Assert.Equal(0xFF00FF80u, at.AmbientColor.Argb);
        Assert.Equal(0xFF102030u, at.SunColor.Argb);
        AssertClose(new Vector3(0.5f, 0f, 0f), at.SunVector, 1e-6f);

        var mid = SkyEvaluator.GetLighting(g, 0.25f);
        Assert.Equal(0.6f, mid.AmbientLevelRaw, 6);
        Assert.Equal(0.75f, mid.SunIntensity, 5);

        // Pitch 45°, heading 90° (east): (sin90·cos45, cos90·cos45, sin45) · 0.75.
        AssertClose(new Vector3(0.75f * MathF.Sqrt(0.5f), 0f, 0.75f * MathF.Sqrt(0.5f)), mid.SunVector, 1e-6f);

        // Per-byte truncation: G 0x00 -> 0xFF at 0.5 = 127.5 -> 127; B 0x80 -> 0x81 = 128.5 -> 128.
        Assert.Equal(0xFF7F7F80u, mid.AmbientColor.Argb);
        Assert.Equal(0xFF203850u, mid.SunColor.Argb); // 0x10->0x30, 0x20->0x50, 0x30->0x70 halfway
    }

    [Fact]
    public void Heading_lerps_linearly_not_by_shortest_arc()
    {
        var g = Group([Key(0f, heading: 350f, pitch: 0f), Key(0.5f, heading: 10f, pitch: 0f)]);

        // Linear: 350 -> 10 passes through 180 (south, -Y) at the midpoint. Shortest arc would pass through 0 (north).
        var mid = SkyEvaluator.GetLighting(g, 0.25f);
        AssertClose(new Vector3(0f, -1f, 0f), mid.SunVector, 1e-5f);
    }

    [Fact]
    public void Ambient_is_clamped_to_the_retail_minimum()
    {
        var g = Group([Key(0f, ambBright: 0.1f)]);
        var l = SkyEvaluator.GetLighting(g, 0.3f);
        Assert.Equal(0.1f, l.AmbientLevelRaw);
        Assert.Equal(0.2f, l.AmbientLevel);
    }

    [Fact]
    public void No_keyframes_gives_the_retail_fallback_light_and_no_fog()
    {
        var g = Group([]);
        var l = SkyEvaluator.GetLighting(g, 0.5f);
        Assert.Equal(0.3f, l.AmbientLevelRaw);
        Assert.Equal(AcColor.White, l.AmbientColor);
        Assert.Equal(AcColor.White, l.SunColor);
        Assert.Equal(new Vector3(0.5f, 0f, 0.8f), l.SunVector);
        Assert.False(SkyEvaluator.GetWorldFog(g, 0.5f).Enabled);
    }

    [Fact]
    public void Derived_object_light_follows_LScape_and_SmartBox()
    {
        var g = Group([Key(0f, heading: 0f, pitch: 90f, dirBright: 0.5f, ambBright: 0.3f, dirColor: 0xFFFF8000u, ambColor: 0xFF8040FFu)]);
        var l = SkyEvaluator.GetLighting(g, 0f);

        // calc_object_light = |sun| * 0.2 + ambient_level = 0.1 + 0.3.
        Assert.Equal(0.4f, l.ObjectAmbientIntensity, 6);
        AssertClose(new Vector3(128, 64, 255) * AcColor.ByteToUnit * 0.4f, l.ObjectAmbient, 1e-6f);

        // D3D sunlight: diffuse = colour * |sun|, direction = -sun.
        AssertClose(new Vector3(255, 128, 0) * AcColor.ByteToUnit * 0.5f, l.SunDiffuse, 1e-6f);
        AssertClose(new Vector3(0f, 0f, -1f), l.LightTravelDirection, 1e-6f);
    }

    // ---- SkyDesc::GetWorldFog ----

    [Fact]
    public void Fog_lerps_and_needs_both_keyframes_enabled()
    {
        var g = Group([Key(0f, fogMin: 0f, fogMax: 400f, fogColor: 0xFF171728u), Key(0.5f, fogMin: 150f, fogMax: 2400f, fogColor: 0xFFC3C8DCu)]);
        var at = SkyEvaluator.GetWorldFog(g, 0f);
        Assert.Equal(new SkyFog(true, 0f, 400f, new AcColor(0xFF171728u), 1u), at);

        var mid = SkyEvaluator.GetWorldFog(g, 0.25f);
        Assert.True(mid.Enabled);
        Assert.Equal(75f, mid.Start);
        Assert.Equal(1400f, mid.End);
        Assert.Equal(AcColor.FromBytes(0xFF, (0x17 + 0xC3) / 2, (0x17 + 0xC8) / 2, (0x28 + 0xDC) / 2), mid.Color);

        var off = Group([Key(0f), Key(0.5f, fogMode: 0u)]);
        Assert.False(SkyEvaluator.GetWorldFog(off, 0.25f).Enabled);
        Assert.False(SkyEvaluator.GetWorldFog(off, 0.75f).Enabled);
    }

    // ---- SkyDesc::GetSky ----

    [Fact]
    public void Object_visibility_window_is_strict_and_never_wraps()
    {
        var g = Group([Key(0f)], [Obj(0, 0.2f, 0.4f), Obj(1, 0f, 0f), Obj(2, 0.9f, 0.1f)]);
        bool Drawn(int i, float t) => SkyEvaluator.GetSky(g, t)[i].IsDrawn;

        Assert.False(Drawn(0, 0.2f));
        Assert.True(Drawn(0, 0.2000001f));
        Assert.True(Drawn(0, 0.3f));
        Assert.False(Drawn(0, 0.4f));
        Assert.False(Drawn(0, 0.5f));

        Assert.True(Drawn(1, 0f));
        Assert.True(Drawn(1, 0.999f));

        // Begin > End is never visible (no wrap past midnight).
        Assert.False(Drawn(2, 0.95f));
        Assert.False(Drawn(2, 0.05f));
    }

    [Fact]
    public void Object_arc_angle_lerps_across_its_window_even_outside_it()
    {
        var g = Group([Key(0f)], [Obj(0, 0.2f, 0.6f, -20f, 180f), Obj(1, 0f, 0f, 33f, 99f)]);
        Assert.Equal(80f, SkyEvaluator.GetSky(g, 0.4f)[0].Rotation, 4);
        Assert.Equal(-20f, SkyEvaluator.GetSky(g, 0.2f)[0].Rotation, 4);
        Assert.Equal(230f, SkyEvaluator.GetSky(g, 0.7f)[0].Rotation, 4);
        Assert.Equal(33f, SkyEvaluator.GetSky(g, 0.4f)[1].Rotation);
    }

    [Fact]
    public void Replacements_follow_GetSky_rules()
    {
        SkyObjectReplacement Rep(uint i, uint gfx = 0, float rot = 0, float tr = -1, float lum = 0, float mb = 0) => new(i, gfx, rot, tr, lum, mb);
        var g = Group(
            [
                Key(0f, replacements: [Rep(0, rot: 270f, lum: 10f, mb: 20f, tr: 0f), Rep(1, gfx: 0x01009999u), Rep(2, lum: 50f)]),
                Key(0.5f, replacements: [Rep(0, lum: 30f, mb: 0f, tr: 100f), Rep(2, lum: 70f), Rep(2, lum: 90f)]),
            ],
            [Obj(0, 0f, 0f), Obj(1, 0.8f, 0.9f), Obj(2, 0f, 0f), Obj(3, 0f, 0f)]);

        var s = SkyEvaluator.GetSky(g, 0.25f);

        // Object 0: heading from Rotate; luminosity lerps 10 -> 30; max_bright set only in the before key stays -1;
        // transparent lerps 0 -> 100.
        Assert.Equal(270f, s[0].Heading);
        Assert.Equal(20f, s[0].Luminosity, 5);
        Assert.Equal(-1f, s[0].MaxBright);
        Assert.Equal(50f, s[0].Transparent, 5);
        Assert.Equal(0.5f, s[0].Translucency!.Value, 5);
        Assert.Null(s[0].DiffusionScale);

        // Object 1: outside its window, but a non-zero replacement GfxObj makes it drawn anyway.
        Assert.Equal(0x01009999u, s[1].GfxObjId);
        Assert.True(s[1].IsDrawn);
        Assert.Equal(-1f, s[1].Luminosity);

        // Object 2: the first matching replacement in the after key is used (50 -> 70, not 90).
        Assert.Equal(60f, s[2].Luminosity, 5);

        // Object 3: no replacement at all.
        Assert.Equal((0f, -1f, -1f, -1f), (s[3].Heading, s[3].Luminosity, s[3].MaxBright, s[3].Transparent));
        Assert.Equal(Quaternion.Identity, s[3].Orientation);
    }

    // ---- GameSky::CalcFrame ----

    [Fact]
    public void Calc_frame_heading_turns_north_to_the_heading_and_rotation_tilts_east_to_up()
    {
        // set_heading(90): local +Y (forward) faces east (+X).
        AssertClose(Vector3.UnitX, Vector3.Transform(Vector3.UnitY, SkyEvaluator.CalcFrame(90f, 0f)), 1e-6f);
        AssertClose(-Vector3.UnitX, Vector3.Transform(Vector3.UnitY, SkyEvaluator.CalcFrame(270f, 0f)), 1e-6f);

        // grotate((0, -rotation, 0)): +X (east horizon) rises to +Z at rotation 90 and sets to -X at 180.
        AssertClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitX, SkyEvaluator.CalcFrame(0f, 90f)), 1e-6f);
        AssertClose(-Vector3.UnitX, Vector3.Transform(Vector3.UnitX, SkyEvaluator.CalcFrame(0f, 180f)), 1e-6f);

        // The world-space rotation is applied after the heading.
        var both = SkyEvaluator.CalcFrame(90f, 90f);
        AssertClose(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, both), 1e-6f);

        // grotate ignores rotations below 0.0002 rad.
        Assert.Equal(Quaternion.Identity, SkyEvaluator.CalcFrame(0f, 0.01f));
    }

    // ---- The user's Region ----

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Region_keyframe_values_come_back_exactly_at_each_keyframe_begin()
    {
        foreach (var g in fixture.Sky.Sky.DayGroups)
        {
            foreach (var k in g.Keyframes)
            {
                var l = SkyEvaluator.GetLighting(g, k.Begin);
                Assert.Equal(k.AmbBright, l.AmbientLevelRaw);
                Assert.Equal(k.AmbColor.Argb, l.AmbientColor.Argb);
                Assert.Equal(k.DirColor.Argb, l.SunColor.Argb);
                Assert.Equal(k.DirBright, l.SunIntensity, 5);
                Assert.Equal(MathF.Sin(k.DirPitch * MathF.PI / 180f) * k.DirBright, l.SunVector.Z, 5);

                var f = SkyEvaluator.GetWorldFog(g, k.Begin);
                Assert.True(f.Enabled);
                Assert.Equal((k.MinWorldFog, k.MaxWorldFog, k.WorldFogColor.Argb), (f.Start, f.End, f.Color.Argb));
            }
        }
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Region_noon_flip_lerps_heading_linearly_through_south()
    {
        // Day group 0 keys 0.61 (H90 P90) and 0.611 (H270 P90): halfway the heading is 180 (linear), but with pitch 90
        // the horizontal part is ~0, so the sun stays at the zenith.
        var g = fixture.Sky.Sky.DayGroups[0];
        var span = SkyEvaluator.GetTimeOfDay(g, 0.6105f)!.Value;
        Assert.Equal((5, 6), (span.BeforeIndex, span.AfterIndex));
        var l = SkyEvaluator.GetLighting(g, 0.6105f);
        Assert.True(MathF.Abs(l.SunVector.X) < 1e-5f && MathF.Abs(l.SunVector.Y) < 1e-5f);
        Assert.Equal(0.8f, l.SunVector.Z, 5);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Region_sun_object_rises_east_culminates_and_sets_west_near_the_light_direction()
    {
        // Axis sanity for CalcFrame: day group 0's sun quad (object 5, GfxObj 0x01001348) sits at +X. Its arc must track
        // the directional light's hemisphere through the day (they are separate dat tables, so only roughly: <= ~18°
        // apart in this dat).
        var g = fixture.Sky.Sky.DayGroups[0];
        for (var t = 0.3f; t <= 0.8f; t += 0.01f)
        {
            var sun = SkyEvaluator.GetSky(g, t)[5];
            Assert.Equal(0x01001348u, sun.GfxObjId);
            var objectDir = Vector3.Transform(Vector3.UnitX, sun.Orientation);
            var lightDir = SkyEvaluator.GetLighting(g, t).SunDirection;
            Assert.True(Vector3.Dot(objectDir, lightDir) > MathF.Cos(25f * MathF.PI / 180f), $"t={t}: {objectDir} vs {lightDir}");
        }

        var noonSun = SkyEvaluator.GetSky(g, 0.55f)[5];
        Assert.True(Vector3.Transform(Vector3.UnitX, noonSun.Orientation).Z > 0.99f);
        Assert.False(SkyEvaluator.GetSky(g, 0.1f)[5].IsDrawn);
        Assert.False(SkyEvaluator.GetSky(g, 0.16f)[5].IsDrawn);
        Assert.True(SkyEvaluator.GetSky(g, 0.17f)[5].IsDrawn);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    public void Region_stars_fade_out_at_dawn()
    {
        // Day group 0 object 1 (stars): transparent 0 at night, 70 at 0.21, 100 by 0.27 (rendering.md §8 replace lerps).
        var g = fixture.Sky.Sky.DayGroups[0];
        Assert.Equal(0f, SkyEvaluator.GetSky(g, 0.1f)[1].Transparent);
        Assert.Equal(35f, SkyEvaluator.GetSky(g, 0.185f)[1].Transparent, 3);
        Assert.Equal(100f, SkyEvaluator.GetSky(g, 0.5f)[1].Transparent);
        Assert.Equal(270f, SkyEvaluator.GetSky(g, 0.5f)[0].Heading);
    }

    [Trait("Tier", "Exhaustive")]
    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_day_group_evaluates_cleanly_every_game_second_of_a_day()
    {
        var sky = fixture.Sky;
        var calendar = sky.Calendar;
        var dayStart = calendar.ToGameTime(121, 40);
        var samples = 0;
        for (var group = 0; group < sky.Sky.DayGroups.Count; group++)
        {
            for (var second = 0; second < (int)calendar.DayLength; second++)
            {
                var s = sky.Evaluate(dayStart + second, group);
                samples++;
                Assert.Equal(group, s.DayGroup.Index);
                Assert.InRange(s.TimeOfDay, 0f, 1f);
                var l = s.Lighting;
                Assert.True(float.IsFinite(l.AmbientLevelRaw) && l.AmbientLevel >= SkyDescription.MinAmbientLevel, $"{group}/{second}");
                Assert.True(float.IsFinite(l.SunVector.X) && float.IsFinite(l.SunVector.Y) && float.IsFinite(l.SunVector.Z), $"{group}/{second}");
                Assert.True(l.SunIntensity > 0f && l.SunIntensity <= 1.5f, $"{group}/{second}: {l.SunIntensity}");
                Assert.True(s.Fog.Enabled && float.IsFinite(s.Fog.Start) && float.IsFinite(s.Fog.End) && s.Fog.Start <= s.Fog.End, $"{group}/{second}");
                Assert.Equal(s.DayGroup.SkyObjects.Count, s.Objects.Count);
                foreach (var o in s.Objects)
                {
                    Assert.True(float.IsFinite(o.Rotation) && float.IsFinite(o.Heading), $"{group}/{second}/{o.Index}");
                    Assert.True(float.IsFinite(o.Orientation.X) && float.IsFinite(o.Orientation.W), $"{group}/{second}/{o.Index}");
                    Assert.Equal(1f, o.Orientation.Length(), 4);
                    Assert.True(float.IsFinite(o.Luminosity) && float.IsFinite(o.MaxBright) && float.IsFinite(o.Transparent));
                    Assert.Equal(o.Desc.IsInWindow(s.TimeOfDay) ? o.Desc.DefaultGfxObjId : 0u, o.GfxObjId);
                }
            }
        }

        Assert.Equal(20 * 7620, samples);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual, float tolerance) =>
        Assert.True(Vector3.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");
}
