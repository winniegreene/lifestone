using System.Numerics;
using DatReaderWriter.Enums;
using Lifestone.Client.Avatars;
using Lifestone.Client.PortalSpace;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Textures;
using Lifestone.Data.Effects;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// Hidden and NoDraw (AV9): <c>set_state</c> acts on changed bits only; <c>set_hidden</c> plays the Hidden or UnHide
/// script (through the portal track's <see cref="HiddenTransition"/>), freezes the animation and physics while
/// interpolation still runs, and hides the children.
/// </summary>
public sealed class AvatarHiddenTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void A_state_change_without_the_hidden_bit_plays_no_hidden_script()
    {
        // Changing other bits acts on them alone: no script, no hidden change counted.
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)) with { PhysicsScriptTableId = AvatarRig.ScriptTable }, now: 0);
        var body = set.All[0].Body;
        Assert.Equal(AvatarApply.Applied, set.SetState(7, AvatarStateBits.Gravity | AvatarStateBits.LightingOn));
        Assert.True(body.LightsOn);
        Assert.Equal(AvatarApply.Applied, set.SetState(7, AvatarStateBits.Gravity | AvatarStateBits.NoDraw));
        Assert.False(body.IsDrawn);
        Assert.Equal(0, set.Stats.HiddenChanges);
        Assert.True(body.Effects.Scripts.IsIdle);
    }

    [Fact]
    public void An_object_created_hidden_is_registered_hidden_and_plays_its_hidden_script()
    {
        // set_description loads the script table, then applies the CreateObject's state through set_state: the Hidden
        // bit differs from the constructed state's, so set_hidden plays the Hidden script at creation.
        var set = new AvatarSet(AvatarRig.Sources());
        var create = AvatarRig.HumanAt(7, new Vector3(30, 30, 0)) with
        {
            PhysicsScriptTableId = AvatarRig.ScriptTable,
            PhysicsState = AvatarStateBits.Hidden | AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions,
        };
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        Assert.True(set.TryGet(7, out var avatar));
        var body = avatar.Body;
        Assert.True(body.IsHidden);
        Assert.True(body.InWorld);
        Assert.False(body.IsDrawn);
        Assert.Equal(AvatarStateBits.IgnoreCollisions, body.State & (AvatarStateBits.IgnoreCollisions | AvatarStateBits.ReportCollisions));
        Assert.Equal([AvatarRig.HiddenScript], body.Effects.Scripts.Queue.Select(s => s.ScriptId));

        // Showing it plays UnHide after it, and restores the collision bits.
        set.SetState(7, AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions);
        Assert.True(body.IsDrawn);
        Assert.Equal(AvatarStateBits.ReportCollisions, body.State & (AvatarStateBits.IgnoreCollisions | AvatarStateBits.ReportCollisions));
        Assert.Equal([AvatarRig.HiddenScript, AvatarRig.UnHideScript], body.Effects.Scripts.Queue.Select(s => s.ScriptId));
        Assert.Equal(1, set.Stats.HiddenChanges);
    }

    [Fact]
    public void Hiding_freezes_the_pose_and_physics_while_interpolation_still_moves_the_object()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)) with { Velocity = new Vector3(0, 0, 0) }, now: 0);
        set.SetMovement(7, isSelf: false, autonomous: false, AvatarRig.Moving(MotionCommands.WalkForward), []);
        var body = set.All[0].Body;
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(AvatarApply.Applied, set.SetState(7, AvatarStateBits.Hidden | AvatarStateBits.Gravity));
        Assert.True(body.IsHidden);
        Assert.False(body.IsDrawn);
        var frame = body.Sequence.FrameNumber;
        body.MoveOrTeleport(AvatarRig.At(body.Position.Origin + new Vector3(2, 0, 0)), teleportAdvanced: false, hasContact: true, placement: null);
        var before = body.Position.Origin;
        for (var i = 2; i <= 20; i++)
        {
            set.Advance(i / 20.0, Viewer, long.MaxValue);
        }

        Assert.Equal(frame, body.Sequence.FrameNumber);
        Assert.True(body.Position.Origin.X > before.X + 1f); // the interpolation still moved it

        set.SetState(7, AvatarStateBits.Gravity);
        set.Advance(1.1, Viewer, long.MaxValue);
        Assert.NotEqual(frame, body.Sequence.FrameNumber);
    }

    [Fact]
    public void A_hidden_parent_hides_its_children_and_unhiding_restores_them()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(7, new Vector3(30, 30, 0)), now: 0);
        set.Create(new AvatarCreate { Guid = 9, SetupId = AvatarRig.Sword, ParentGuid = 7, ParentLocation = 1 }, now: 0);
        var sword = set.All[1].Body;
        set.SetState(7, AvatarStateBits.Hidden);
        Assert.False(sword.IsDrawn);
        Assert.Equal(AvatarStateBits.NoDraw, sword.State & AvatarStateBits.NoDraw);
        set.SetState(7, 0);
        Assert.True(sword.IsDrawn);
    }

    [Fact]
    public void A_repeated_state_with_the_same_hidden_bit_plays_nothing_and_the_players_changes_are_noticed()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(0x50000001, new Vector3(30, 30, 0)) with { IsSelf = true }, now: 0);
        set.SetState(0x50000001, AvatarStateBits.Hidden);
        set.SetState(0x50000001, AvatarStateBits.Hidden | AvatarStateBits.Gravity);
        set.SetState(0x50000001, AvatarStateBits.Gravity);
        Assert.Equal(2, set.Stats.HiddenChanges);
        var notices = new List<AvatarNotice>();
        set.DrainNotices(notices);
        Assert.Equal(["hide", "unhide"], notices.Where(n => n.Kind == AvatarNoticeKind.PlayerHiddenChanged).Select(n => n.Detail));
    }

    /// <summary>
    /// The PhysicsScriptTable of the human (0x34000004) maps UnHide to 0x3300032F and Hidden to 0x33000331
    /// (m2-log-in.md §4.9). The census lists what the two scripts do: it is the input to owner decision D45 (per-instance
    /// translucency in the object shaders).
    /// </summary>
    [RequiresDatsFact]
    public void The_hidden_and_unhide_scripts_and_their_hooks()
    {
        var effects = new DatEffectSource(fixture.Dats);
        var table = effects.GetScriptTable(0x34000004)!;
        Assert.Equal(0x3300032Fu, table.GetScript(PlayScript.UnHide, 1f));
        Assert.Equal(0x33000331u, table.GetScript(PlayScript.Hidden, 1f));
        foreach (var id in new uint[] { 0x3300032F, 0x33000331 })
        {
            var script = effects.GetScript(id)!;
            var census = script.Hooks.GroupBy(h => h.Hook.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}");
            output.WriteLine($"0x{id:X8}: {script.Hooks.Count} hooks, {script.Length:0.###} s: {string.Join(", ", census)}");
            Assert.NotEmpty(script.Hooks);
        }

        // Hiding starts 14 particle emitters (the bubbles that surround a hidden player) and fades the object with one
        // Transparent hook; unhiding stops the 14 emitters and fades it back. The fades are whole-object translucency:
        // drawing them needs per-instance translucency in the object shaders (owner decision D45).
        Assert.Equal("CreateParticle×14,Transparent×1", Census(effects.GetScript(0x33000331)!));
        Assert.Equal("StopParticle×14,Transparent×1", Census(effects.GetScript(0x3300032F)!));
    }

    /// <summary>
    /// The materialise fade's numbers, read from the user's dats (D45, D63): the UnHide script 0x3300032F holds one
    /// Transparent hook at script time 0, start 1, end 0, over 0.75 s; the Hidden script 0x33000331 holds one at time 0
    /// from 1 to 1 over 0 s (an immediate translucency of 1: invisible). <c>AvatarRig.RetailUnHideFade</c> and
    /// <c>RetailHiddenFade</c> repeat them for the dat-free tests of the ramp (<c>AvatarMaterialiseTests</c>).
    /// </summary>
    [RequiresDatsFact]
    public void The_materialise_fade_hooks_are_a_linear_ramp_from_1_to_0_over_0_75_s()
    {
        var effects = new DatEffectSource(fixture.Dats);
        var unhide = FadeHooks(effects, 0x3300032F);
        var hidden = FadeHooks(effects, 0x33000331);
        output.WriteLine($"UnHide 0x3300032F: Transparent at {unhide.At:R} s, start {unhide.Hook.Start:R} end {unhide.Hook.End:R} time {unhide.Hook.Time:R} s");
        output.WriteLine($"Hidden 0x33000331: Transparent at {hidden.At:R} s, start {hidden.Hook.Start:R} end {hidden.Hook.End:R} time {hidden.Hook.Time:R} s");
        Assert.Equal(0.0, unhide.At);
        Assert.Equal((1f, 0f, 0.75f), (unhide.Hook.Start, unhide.Hook.End, unhide.Hook.Time));
        Assert.Equal(0.0, hidden.At);
        Assert.Equal((1f, 1f, 0f), (hidden.Hook.Start, hidden.Hook.End, hidden.Hook.Time));
        Assert.Equal(AvatarRig.RetailUnHideFade, (unhide.Hook.Start, unhide.Hook.End, unhide.Hook.Time));
        Assert.Equal(AvatarRig.RetailHiddenFade, (hidden.Hook.Start, hidden.Hook.End, hidden.Hook.Time));

        // Both scripts end at time 0 (every hook starts at 0): the fade is the hook's own 0.75 s, not the script's length.
        Assert.Equal(0.0, effects.GetScript(0x3300032F)!.Length);
        Assert.Equal(0.0, effects.GetScript(0x33000331)!.Length);
    }

    /// <summary>
    /// The bubbles round a hidden player are pink by the dats, not a missing material: each of the Hidden script
    /// 0x33000331's 14 CreateParticle emitters draws a textured GfxObj whose texture resolves and whose visible texels are
    /// magenta (red and blue well above green). The emitters have no total time, so the cloud lasts while the object is
    /// hidden and ends only when the UnHide script 0x3300032F stops them (the login's arrival: retail's
    /// <c>gmSmartBoxUI::UseTime</c>, 11.6096 0x004D7A90, sends LoginComplete only when state 6 ends; ACE then clears Hidden).
    /// A live frame taken during the arrival shows the cloud with no body inside it, as retail's <c>CPhysicsObj::set_hidden</c>
    /// (11.6096 0x00515760) draws it (M5 item 7, the doll-proof frames).
    /// </summary>
    [RequiresDatsFact]
    public void The_hidden_scripts_bubbles_are_pink_textured_and_last_until_the_unhide_stops_them()
    {
        var effects = new DatEffectSource(fixture.Dats);
        var textures = new TextureLoader(fixture.Dats);
        var appearance = new DatAppearanceSource(textures);
        var creates = effects.GetScript(0x33000331)!.Hooks.Select(h => h.Hook).OfType<CreateParticleHook>().ToList();
        var stops = effects.GetScript(0x3300032F)!.Hooks.Select(h => h.Hook).OfType<StopParticleHook>().Select(h => h.EmitterId).ToHashSet();
        Assert.Equal(14, creates.Count);
        foreach (var create in creates)
        {
            Assert.Contains(create.EmitterId, stops);
            var info = effects.GetEmitterInfo(create.EmitterInfoId)!;
            Assert.Equal(0.0, info.TotalSeconds); // no end of its own: only the UnHide's StopParticle ends it
            var surfaces = appearance.SurfaceIds(info.HwGfxObjId);
            Assert.NotEmpty(surfaces);
            foreach (var surfaceId in surfaces)
            {
                var decoded = textures.DecodeSurface(surfaceId, TextureDecodeMode.Rgba8)!;
                Assert.NotNull(decoded.Texture);
                Assert.True(decoded.Resolution!.IsResolved);
                var (r, g, b) = MeanVisibleRgb(decoded.Texture!);
                output.WriteLine($"emitter info 0x{create.EmitterInfoId:X8} (id {create.EmitterId}, part {create.PartIndex}) gfxobj 0x{info.HwGfxObjId:X8} surface 0x{surfaceId:X8} texture 0x{decoded.Material.SurfaceTextureId:X8} {decoded.Texture!.Width}x{decoded.Texture.Height} mean rgb ({r:0}, {g:0}, {b:0}) type {decoded.Material.Type} lifespan {info.Lifespan:0.##} s birthrate {info.Birthrate:0.###} s");
                Assert.True(r > g + 40 && b > g + 40, $"surface 0x{surfaceId:X8} is not magenta: ({r:0}, {g:0}, {b:0})");
            }
        }
    }

    private static (double R, double G, double B) MeanVisibleRgb(Data.Textures.DecodedTexture texture)
    {
        Assert.Equal(Data.Textures.DecodedTextureFormat.Rgba8, texture.Format);
        double r = 0, g = 0, b = 0, w = 0;
        for (var i = 0; i + 3 < texture.Data.Length; i += 4)
        {
            var a = texture.HasAlpha ? texture.Data[i + 3] / 255.0 : 1.0;
            var lum = texture.Data[i] + texture.Data[i + 1] + texture.Data[i + 2];
            var weight = a * lum; // the bright texels are what a viewer sees (an additive or blended sprite)
            r += texture.Data[i] * weight;
            g += texture.Data[i + 1] * weight;
            b += texture.Data[i + 2] * weight;
            w += weight;
        }

        Assert.True(w > 0, $"texture 0x{texture.SourceId:X8} has no visible texel");
        return (r / w, g / w, b / w);
    }

    private static (double At, TransparentHook Hook) FadeHooks(DatEffectSource effects, uint scriptId)
    {
        var entry = Assert.Single(effects.GetScript(scriptId)!.Hooks, h => h.Hook is TransparentHook);
        return (entry.StartTime, (TransparentHook)entry.Hook);
    }

    private static string Census(Data.Effects.PhysicsScriptData script) =>
        string.Join(",", script.Hooks.GroupBy(h => h.Hook.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}"));
}
