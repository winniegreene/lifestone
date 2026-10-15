using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Effects;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The materialise fade (owner decisions D45 and D63), the client side: when the server clears an object's Hidden bit,
/// the UnHide script's one Transparent hook ramps the object's translucency linearly from 1 to 0 over 0.75 s
/// (<c>AvatarRig.RetailUnHideFade</c>, pinned from the dats by <c>AvatarHiddenTests</c>); the Hidden script sets it to 1
/// at once. <see cref="AvatarDraw.Translucency"/> and <see cref="AvatarFrame.PartTranslucency"/> carry the value the
/// frame draws, for the player and for every other object alike. No dats: the rig's two scripts hold those hooks.
/// </summary>
public sealed class AvatarMaterialiseTests(ITestOutputHelper output)
{
    private const uint Npc = 7;
    private const uint Player = 0x50000001;
    private const float FadeTime = 0.75f;
    private const uint Shown = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions;
    private const uint HiddenState = AvatarStateBits.Hidden | AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    private static AvatarSet NewSet(AvatarCreate create)
    {
        var set = new AvatarSet(AvatarRig.Sources(materialise: true));
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        return set;
    }

    private static AvatarCreate CreatedHidden(uint guid, bool self = false, float translucency = 0f) =>
        AvatarRig.HumanAt(guid, new Vector3(30, 30, 0)) with
        {
            IsSelf = self,
            Translucency = translucency,
            PhysicsScriptTableId = AvatarRig.ScriptTable,
            PhysicsState = HiddenState,
        };

    private static AvatarDraw DrawOf(AvatarSet set, uint guid)
    {
        foreach (var draw in set.Frame.Avatars)
        {
            if (draw.Guid == guid)
            {
                return draw;
            }
        }

        throw new Xunit.Sdk.XunitException($"no draw for 0x{guid:X8}");
    }

    /// <summary>Advances to <paramref name="until"/> in steps of <paramref name="step"/>, calling <paramref name="each"/> after every one; returns the last time.</summary>
    private static double Run(AvatarSet set, double from, double until, double step, Action<double, AvatarDraw>? each = null, uint guid = Npc)
    {
        var steps = (int)Math.Round((until - from) / step);
        for (var i = 1; i <= steps; i++)
        {
            var now = from + i * step;
            set.Advance(now, Viewer, long.MaxValue);
            each?.Invoke(now, DrawOf(set, guid));
        }

        return from + steps * step;
    }

    /// <summary>The frame's translucency of every part of <paramref name="draw"/> equals its object's (no TransparentPart hook is involved).</summary>
    private static void AssertPartsFollowObject(AvatarSet set, AvatarDraw draw)
    {
        Assert.True(draw.PartCount > 0);
        var parts = set.Frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount);
        for (var i = 0; i < parts.Length; i++)
        {
            Assert.Equal(draw.Translucency, parts[i]);
        }
    }

    [Fact]
    public void An_object_created_hidden_is_invisible_until_the_server_clears_the_bit()
    {
        var set = NewSet(CreatedHidden(Npc));
        Run(set, 0, 0.5, 1 / 30.0);
        var draw = DrawOf(set, Npc);
        Assert.False(draw.Visible);
        Assert.Equal(1f, draw.Translucency); // the Hidden script's immediate 1
        Assert.True(PartTranslucencyRules.IsHidden(draw.Translucency));
    }

    [Fact]
    public void Unhiding_an_object_ramps_its_translucency_linearly_from_1_to_0_over_three_quarters_of_a_second()
    {
        var set = NewSet(CreatedHidden(Npc));
        var t0 = Run(set, 0, 0.5, 1 / 30.0);
        Assert.Equal(AvatarApply.Applied, set.SetState(Npc, Shown));
        var samples = new List<(double Time, float Translucency)>();
        var previous = 1f;
        var tail = Run(set, t0, t0 + 1.5, 1 / 30.0, (now, draw) =>
        {
            Assert.True(draw.Visible);
            samples.Add((now, draw.Translucency));
            Assert.InRange(draw.Translucency, 0f, 1f);
            Assert.True(draw.Translucency <= previous, $"not monotonic at {now}");
            previous = draw.Translucency;
            AssertPartsFollowObject(set, draw);
        });

        // The hook starts in the first update after the bit clears (the script runs inside that update) and runs linearly.
        var start = t0 + 1 / 30.0;
        foreach (var (time, translucency) in samples)
        {
            var expected = (float)Math.Max(0.0, 1.0 - (time - start) / FadeTime);
            Assert.Equal(expected, translucency, 4);
        }

        output.WriteLine($"at 30 fps: {samples.Count} frames, first {samples[0].Translucency:0.####}, last {samples[^1].Translucency:0.####} at {tail:0.###}");
        Assert.Equal(1f, samples[0].Translucency); // the first frame is still invisible: no pop-in
        Assert.Equal(0f, samples[^1].Translucency); // ends fully opaque
        Assert.Contains(samples, s => s.Translucency is > 0f and < 1f);
        Assert.False(PartTranslucencyRules.HasPartMaterial(samples[^1].Translucency)); // and then draws on the ordinary opaque path
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(240)]
    public void The_ramp_is_a_function_of_time_not_of_frames(int fps)
    {
        var set = NewSet(CreatedHidden(Npc));
        var step = 1.0 / fps;
        var t0 = Run(set, 0, 0.5, step);
        set.SetState(Npc, Shown);
        var seen = new Dictionary<int, float>();
        Run(set, t0, t0 + 1.5, step, (now, draw) => seen[(int)Math.Round((now - t0) * 1000)] = draw.Translucency);

        // The fade starts one frame after the bit clears, at the frame rate in use, then runs on the clock: it matches the
        // closed form at the same instants, and the three rates agree to within one 30 fps frame's worth of ramp.
        foreach (var ms in new[] { 200, 400, 600, 700 })
        {
            var expected = 1.0 - (ms / 1000.0 - step) / FadeTime;
            Assert.Equal((float)expected, seen[ms], 3);
            var at240 = 1.0 - (ms / 1000.0 - 1 / 240.0) / FadeTime;
            var at30 = 1.0 - (ms / 1000.0 - 1 / 30.0) / FadeTime;
            Assert.InRange(seen[ms], at240 - 1e-3, at30 + 1e-3);
        }

        Assert.Equal(0f, seen[800]);
        Assert.Equal(0f, seen[1500]);
    }

    [Fact]
    public void The_fade_never_goes_below_the_objects_own_translucency()
    {
        // translucencyOriginal (CPhysicsObj::SetTranslucencyInternal): a 0.4 object fades 1 to 0.4, not to 0.
        var set = NewSet(CreatedHidden(Npc, translucency: 0.4f));
        var t0 = Run(set, 0, 0.5, 1 / 60.0);
        Assert.Equal(1f, DrawOf(set, Npc).Translucency);
        set.SetState(Npc, Shown);
        var lowest = 1f;
        Run(set, t0, t0 + 1.5, 1 / 60.0, (_, draw) =>
        {
            lowest = Math.Min(lowest, draw.Translucency);
            Assert.True(draw.Translucency >= 0.4f);
            AssertPartsFollowObject(set, draw);
        });
        Assert.Equal(0.4f, lowest);
        Assert.Equal(0.4f, DrawOf(set, Npc).Translucency);
    }

    [Fact]
    public void An_object_that_was_never_hidden_draws_opaque_and_never_fades()
    {
        var set = NewSet(AvatarRig.HumanAt(Npc, new Vector3(30, 30, 0)) with { PhysicsScriptTableId = AvatarRig.ScriptTable });
        Run(set, 0, 1, 1 / 30.0, (_, draw) =>
        {
            Assert.True(draw.Visible);
            Assert.Equal(0f, draw.Translucency);
        });
    }

    [Fact]
    public void The_player_materialises_the_same_way()
    {
        var set = NewSet(CreatedHidden(Player, self: true));
        Assert.NotNull(set.Player);
        var t0 = Run(set, 0, 0.5, 1 / 60.0, guid: Player);
        var hidden = DrawOf(set, Player);
        Assert.True(hidden.IsPlayer);
        Assert.False(hidden.Visible);
        Assert.Equal(1f, hidden.Translucency);

        set.SetState(Player, Shown);
        var samples = new List<float>();
        Run(set, t0, t0 + 1.25, 1 / 60.0, (now, draw) =>
        {
            Assert.True(draw.IsPlayer);
            Assert.True(draw.Visible);
            samples.Add(draw.Translucency);
            AssertPartsFollowObject(set, draw);
            Assert.Equal((float)Math.Max(0.0, 1.0 - (now - t0 - 1 / 60.0) / FadeTime), draw.Translucency, 4);
        }, guid: Player);
        Assert.Equal(1f, samples[0]);
        Assert.Equal(0f, samples[^1]);
        Assert.Equal(samples.OrderByDescending(x => x), samples);
    }

    [Fact]
    public void A_first_person_player_is_not_drawn_while_it_materialises_but_the_value_still_ramps()
    {
        var set = NewSet(CreatedHidden(Player, self: true));
        var t0 = Run(set, 0, 0.5, 1 / 60.0, guid: Player);
        set.SetState(Player, Shown);
        set.Player!.FirstPerson = true;
        Run(set, t0, t0 + 0.4, 1 / 60.0, guid: Player);
        var draw = DrawOf(set, Player);
        Assert.False(draw.Visible); // first person hides the body outright (the first-person fade is its own TODO for M3)
        Assert.InRange(draw.Translucency, 0.01f, 0.99f);
    }

    [Fact]
    public void Hiding_again_in_the_middle_of_the_fade_stops_drawing_the_object_at_once_and_plays_the_hidden_script()
    {
        var set = NewSet(CreatedHidden(Npc));
        var t0 = Run(set, 0, 0.5, 1 / 60.0);
        set.SetState(Npc, Shown);
        var t1 = Run(set, t0, t0 + 0.3, 1 / 60.0);
        var mid = DrawOf(set, Npc);
        Assert.True(mid.Visible);
        Assert.InRange(mid.Translucency, 0.3f, 0.7f);

        set.SetState(Npc, HiddenState);
        set.Advance(t1 + 1 / 60.0, Viewer, long.MaxValue);
        var hidden = DrawOf(set, Npc);
        Assert.False(hidden.Visible); // nothing is drawn, whatever the translucency says
        Assert.Equal(1f, hidden.Translucency); // the Hidden script's immediate 1 ran in that update
    }

    /// <summary>Hide, show, re-hide mid-fade and unhide again after the old hook has run out unseen; returns the translucencies drawn for a second after that last unhide.</summary>
    private List<float> UnhideAfterAReHide(bool retailFlash, AvatarSet? existing = null)
    {
        // Retail's FPHook list is newest first (CPhysicsObj::SetTranslucency2 puts the new hook at the head and
        // process_hooks walks from the head), so an older hook is applied last and overrides a newer one while it lives;
        // the Hidden script's own hook is immediate (time 0) and so leaves the old fade running behind it. Hidden, the
        // object is not drawn, so the old hook runs unseen to 0; unhiding after that, the first frame has translucency 0
        // (the UnHide script's hook is only queued in that update) and then the new fade starts from 1.
        var set = existing ?? NewSet(CreatedHidden(Npc));
        set.RetailUnhideFlash = retailFlash;
        var t0 = Run(set, 0, 0.5, 1 / 60.0);
        set.SetState(Npc, Shown);
        var t1 = Run(set, t0, t0 + 0.3, 1 / 60.0);
        set.SetState(Npc, HiddenState);
        var t2 = Run(set, t1, t1 + 1.5, 1 / 60.0);
        var parked = DrawOf(set, Npc);
        Assert.False(parked.Visible);
        output.WriteLine($"hidden for 1.5 s after a re-hide: translucency {parked.Translucency}");
        Assert.Equal(0f, parked.Translucency); // the stale hook ran on unseen

        set.SetState(Npc, Shown);
        var seen = new List<float>();
        Run(set, t2, t2 + 1.0, 1 / 60.0, (_, draw) =>
        {
            Assert.True(draw.Visible);
            seen.Add(draw.Translucency);
            AssertPartsFollowObject(set, draw);
        });
        return seen;
    }

    [Fact]
    public void Retail_quirk_restored_by_the_flag_the_unfinished_fade_hook_outlives_a_rehide_and_the_next_unhide_starts_opaque_for_one_frame()
    {
        // --retail-unhide-flash: EffectObject reproduces the retail order; the first frame after the unhide is drawn at the stale 0 (docs/avatars.md section 17).
        var seen = UnhideAfterAReHide(retailFlash: true);
        Assert.Equal(0f, seen[0]);
        Assert.Equal(1f - 1f / 60f / FadeTime, seen[1], 4); // the new fade's first step; the old hook has finished
        Assert.Equal(0f, seen[^1]);
    }

    [Fact]
    public void The_glitch_fix_the_first_frame_after_an_unhide_after_a_rehide_is_still_invisible_and_the_fade_is_unchanged()
    {
        // The default: the same sequence draws no opaque frame. Frame 0 holds translucency 1 until the new fade's hook applies, then the ramp is the normal one.
        var seen = UnhideAfterAReHide(retailFlash: false);
        Assert.Equal(1f, seen[0]);
        Assert.Equal(1f - 1f / 60f / FadeTime, seen[1], 4);
        Assert.Equal(0f, seen[^1]);
        Assert.Equal(seen.OrderByDescending(x => x), seen); // never opaque and then translucent again
    }

    [Fact]
    public void The_glitch_fix_changes_nothing_for_an_ordinary_unhide_and_the_flag_makes_no_difference_there()
    {
        // No re-hide: the Hidden script's 1 is already what the first frame shows, so default and retail draw the same frames.
        List<float> Ordinary(bool flash)
        {
            var set = NewSet(CreatedHidden(Npc));
            set.RetailUnhideFlash = flash;
            var t0 = Run(set, 0, 0.5, 1 / 60.0);
            set.SetState(Npc, Shown);
            var seen = new List<float>();
            Run(set, t0, t0 + 1.0, 1 / 60.0, (_, draw) => seen.Add(draw.Translucency));
            return seen;
        }

        var fixedFrames = Ordinary(false);
        Assert.Equal(Ordinary(true), fixedFrames);
        Assert.Equal(1f, fixedFrames[0]);
    }

    [Fact]
    public void The_glitch_fix_does_not_hide_an_object_whose_unhide_plays_no_fade()
    {
        // An object with no PhysicsScriptTable never fades: unhiding it shows it at once, in the first frame (the hold is only for a queued fade).
        var set = new AvatarSet(AvatarRig.Sources(materialise: true));
        var plain = AvatarRig.HumanAt(Npc, new Vector3(30, 30, 0)) with { PhysicsState = HiddenState };
        Assert.Equal(AvatarApply.Applied, set.Create(plain, now: 0));
        var t0 = Run(set, 0, 0.5, 1 / 60.0);
        set.SetState(Npc, Shown);
        var seen = new List<float>();
        Run(set, t0, t0 + 0.2, 1 / 60.0, (_, draw) => seen.Add(draw.Translucency));
        Assert.All(seen, t => Assert.Equal(0f, t));
    }

    [Fact]
    public void The_retail_unhide_flash_option_is_registered_and_parsed()
    {
        Assert.False(Lifestone.Client.Play.PlayOptions.Parse([]).RetailUnhideFlash);
        Assert.True(Lifestone.Client.Play.PlayOptions.Parse(["--retail-unhide-flash"]).RetailUnhideFlash);
        Assert.Contains(Lifestone.Client.Options.OptionCatalog.Play().Options, o => o.Name == "--retail-unhide-flash");
    }
}
