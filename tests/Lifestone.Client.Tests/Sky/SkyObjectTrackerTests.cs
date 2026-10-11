using System.Numerics;
using Lifestone.Client.Sky;
using Lifestone.Data.Tests;
using Lifestone.Data.World.Sky;

namespace Lifestone.Client.Tests.Sky;

/// <summary>
/// Retail sky object lifetimes and sticky lighting (<c>GameSky::CreateDeletePhysicsObjects</c>, <c>GameSky::UseTime</c>,
/// <c>GameSky::Draw</c>, <c>GameSky::UpdatePosition</c>) on synthetic day groups, then on the user's Region.
/// </summary>
public sealed class SkyObjectTrackerTests(SkyDatsFixture fixture) : IClassFixture<SkyDatsFixture>
{
    private const uint Dome = 0x01000001;
    private const uint Moon = 0x01000002;
    private const uint Rain = 0x01000003;
    private const uint OtherDome = 0x01000004;

    private static SkyKeyframe Key(float begin, params SkyObjectReplacement[] replacements) =>
        new(begin, 0.5f, 90f, 45f, AcColor.White, 0.4f, AcColor.White, 100f, 1000f, AcColor.White, 1u, replacements);

    private static SkyObjectDesc Obj(int index, uint gfx, float begin = 0f, float end = 0f, SkyObjectProperties properties = SkyObjectProperties.None) =>
        new(index, begin, end, 0f, 90f, Vector2.Zero, gfx, 0u, properties);

    private static SkyDayGroup Group(IReadOnlyList<SkyObjectDesc> objects, params SkyKeyframe[] keys) =>
        new(0, 5f, "Test", objects, keys.Length == 0 ? [Key(0f)] : keys);

    [Fact]
    public void Objects_are_created_inside_their_window_and_deleted_after_it()
    {
        var group = Group([Obj(0, Dome), Obj(1, Moon, 0.2f, 0.4f)]);
        var tracker = new SkyObjectTracker();

        Assert.True(tracker.Update(SkyEvaluator.Evaluate(group, 0.1f)));
        Assert.Equal((Dome, true, 1), (tracker.Slots[0].GfxObjId, tracker.Slots[0].Drawn, tracker.Slots[0].Generation));
        Assert.False(tracker.Slots[1].Exists);

        Assert.True(tracker.Update(SkyEvaluator.Evaluate(group, 0.3f)));
        Assert.Equal((Moon, 1), (tracker.Slots[1].GfxObjId, tracker.Slots[1].Generation));
        Assert.Equal(1, tracker.Slots[0].Generation); // the dome was kept

        Assert.False(tracker.Update(SkyEvaluator.Evaluate(group, 0.35f)));
        Assert.True(tracker.Update(SkyEvaluator.Evaluate(group, 0.5f)));
        Assert.False(tracker.Slots[1].Exists);
        Assert.Equal(2, tracker.Slots[1].Generation);
        Assert.Equal((2L, 1L), (tracker.CreatedCount, tracker.DeletedCount));
    }

    [Fact]
    public void A_replacement_gfx_recreates_the_object_with_default_lighting()
    {
        var group = Group(
            [Obj(0, Dome)],
            Key(0f, new SkyObjectReplacement(0, 0, 0f, -1f, 40f, 40f)),
            Key(0.4f, new SkyObjectReplacement(0, 0, 0f, -1f, 40f, 40f)),
            Key(0.5f, new SkyObjectReplacement(0, OtherDome, 0f, -1f, 0f, 0f)));
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(group, 0.2f));
        Assert.Equal(0.4f, tracker.Slots[0].Material.Luminosity, 6);

        tracker.Update(SkyEvaluator.Evaluate(group, 0.6f));
        var slot = tracker.Slots[0];
        Assert.Equal((OtherDome, 3), (slot.GfxObjId, slot.Generation)); // created, deleted, created
        Assert.Equal(SkyPartMaterial.Default, slot.Material); // a new physics object starts with no material
    }

    [Fact]
    public void Lighting_not_set_by_the_keyframes_stays_as_it_was()
    {
        // Luminosity is only lerped while both bracketing keyframes set it (SkyDesc::GetSky); otherwise GameSky::UseTime
        // leaves the physics object's value alone.
        var group = Group(
            [Obj(0, Dome)],
            Key(0f, new SkyObjectReplacement(0, 0, 0f, -1f, 50f, 0f)),
            Key(0.5f, new SkyObjectReplacement(0, 0, 0f, -1f, 50f, 0f)),
            Key(0.7f, new SkyObjectReplacement(0, 0, 0f, -1f, 0f, 0f)));
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(group, 0.25f));
        Assert.Equal(0.5f, tracker.Slots[0].Material.Luminosity, 6);
        tracker.Update(SkyEvaluator.Evaluate(group, 0.6f)); // after keyframe has no luminosity: unset
        tracker.Update(SkyEvaluator.Evaluate(group, 0.9f)); // before keyframe has none: unset
        Assert.Equal(0.5f, tracker.Slots[0].Material.Luminosity, 6);
        Assert.Equal(1, tracker.Slots[0].Generation);
    }

    [Fact]
    public void Full_translucency_hides_without_deleting()
    {
        var group = Group(
            [Obj(0, Dome)],
            Key(0f, new SkyObjectReplacement(0, 0, 0f, 100f, 0f, 0f)),
            Key(0.5f, new SkyObjectReplacement(0, 0, 0f, 100f, 0f, 0f)));
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(group, 0.2f));
        var slot = tracker.Slots[0];
        Assert.True(slot.Exists);
        Assert.True(slot.Material.Hidden);
        Assert.False(slot.Drawn);
    }

    [Fact]
    public void Weather_objects_need_the_weather_option_and_are_recreated_when_it_changes()
    {
        var group = Group([Obj(0, Dome), Obj(1, Rain, properties: SkyObjectProperties.Weather | SkyObjectProperties.AfterPass)]);
        var tracker = new SkyObjectTracker { WeatherEnabled = false };
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        Assert.False(tracker.Slots[1].Exists);

        tracker.WeatherEnabled = true;
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        var rain = tracker.Slots[1];
        Assert.Equal((Rain, SkyPass.After, true), (rain.GfxObjId, rain.Pass, rain.Drawn));
        var generation = rain.Generation;

        tracker.WeatherEnabled = false;
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        Assert.False(tracker.Slots[1].Exists);
        Assert.True(tracker.Slots[1].Generation > generation);
        Assert.Equal(1, tracker.Slots[0].Generation); // non-weather objects are untouched
    }

    [Fact]
    public void The_fog_override_hides_marked_objects_only_while_fog_is_on()
    {
        var group = Group([Obj(0, Dome, properties: SkyObjectProperties.HiddenUnderFogOverride)]);
        var tracker = new SkyObjectTracker { FogOverrideEnabled = true };
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f), fogEnabled: true);
        Assert.False(tracker.Slots[0].Drawn);
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f), fogEnabled: false);
        Assert.True(tracker.Slots[0].Drawn);
    }

    [Fact]
    public void Weather_objects_follow_the_viewer_and_the_rest_stay_at_the_anchor()
    {
        var group = Group(
        [
            Obj(0, Dome),
            Obj(1, Rain, properties: SkyObjectProperties.Weather),
            Obj(2, Moon, properties: SkyObjectProperties.Weather | SkyObjectProperties.WeatherKeepsPlayerZ),
        ]);
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        var viewer = new Vector3(84f, 7.1f, 140f);
        Assert.Equal(Vector3.Zero, SkyObjectTracker.LocalOrigin(tracker.Slots[0], viewer));
        Assert.Equal(new Vector3(84f, 7.1f, -120f), SkyObjectTracker.LocalOrigin(tracker.Slots[1], viewer));
        Assert.Equal(viewer, SkyObjectTracker.LocalOrigin(tracker.Slots[2], viewer));
    }

    [Fact]
    public void Fewer_objects_after_a_day_group_change_delete_the_extra_slots()
    {
        var big = Group([Obj(0, Dome), Obj(1, Moon), Obj(2, Rain)]);
        var small = Group([Obj(0, Dome)]);
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(big, 0.5f));
        Assert.True(tracker.Update(SkyEvaluator.Evaluate(small, 0.5f)));
        Assert.Single(tracker.Slots);
        Assert.Equal(1, tracker.Slots[0].Generation); // same GfxObj and properties: kept across the change
    }

    [RequiresDatsFact]
    public void Sunny_day_group_0_from_noon_to_night()
    {
        var group = fixture.Sky.Sky.DayGroups[0];
        var tracker = new SkyObjectTracker();

        // Midsong: dome lit (its surfaces are luminous anyway), stars hidden (tr 100), clouds nearly fully lit, sun up.
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        var s = tracker.Slots;
        Assert.Equal(0x010015EEu, s[0].GfxObjId);
        Assert.True(s[0].Drawn);
        Assert.True(s[1].Material.Hidden);
        Assert.False(s[1].Drawn);
        Assert.InRange(s[4].Material.Luminosity, 0.96f, 0.97f); // lerp 90% (0.27) -> 100% (0.61) at 0.5
        Assert.Equal(s[4].Material.Luminosity, s[4].Material.Diffusion);
        Assert.True(s[5].Drawn);
        Assert.Equal(0x01001348u, s[5].GfxObjId);
        Assert.False(s[2].Exists); // moons are outside their windows (0.04-0.21, 0-0.23)

        // Darktide-and-Half: stars opaque (tr 0), clouds at 15-22% light, moons up, sun down.
        tracker.Update(SkyEvaluator.Evaluate(group, 0.08f));
        Assert.False(s[1].Material.Hidden);
        Assert.Equal(0f, s[1].Material.Translucency);
        Assert.True(s[1].Drawn);
        Assert.InRange(s[4].Material.Luminosity, 0.15f, 0.22f);
        Assert.True(s[2].Exists && s[3].Exists);
        Assert.False(s[5].Exists);

        // Gloaming (0.9): the stars fade in at 70% (tr 30).
        tracker.Update(SkyEvaluator.Evaluate(group, 0.9f));
        Assert.Equal(0.3f, s[1].Material.Translucency, 5);
        Assert.Equal(0.7f, s[1].Material.VertexAlpha(fixture.Surface(0x0800004D)), 5);
    }

    [RequiresDatsFact]
    public void Rainy_groups_put_rain_in_both_passes_around_the_viewer()
    {
        var group = fixture.Sky.Sky.DayGroups[3];
        Assert.Equal("Rainy", group.Name);
        var tracker = new SkyObjectTracker();
        tracker.Update(SkyEvaluator.Evaluate(group, 0.5f));
        var rainAfter = tracker.Slots[9];
        var rainBefore = tracker.Slots[10];
        Assert.Equal((0x01004C44u, SkyPass.After, true), (rainAfter.GfxObjId, rainAfter.Pass, rainAfter.Drawn));
        Assert.Equal((0x01004C42u, SkyPass.Before, true), (rainBefore.GfxObjId, rainBefore.Pass, rainBefore.Drawn));
        Assert.Equal(-120f, SkyObjectTracker.LocalOrigin(rainBefore, new Vector3(1, 2, 300)).Z);
    }
}
