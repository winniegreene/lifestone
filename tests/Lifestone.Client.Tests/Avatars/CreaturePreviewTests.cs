using System.Numerics;
using DatReaderWriter.DBObjs;
using Lifestone.Client.Avatars;
using Lifestone.Data.Appearance;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>CharGen's 3D view (AV12): <c>gmCG3DView</c>'s animations, heading, light and appearance changes.</summary>
public sealed class CreaturePreviewTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static readonly PreviewAnimations RigAnimations = new(AvatarRig.WalkAnim, AvatarRig.ReadyAnim, []);

    [Fact]
    public void Start_plays_the_animation_from_frame_0_at_30_fps_and_stop_holds_the_rest_pose()
    {
        var preview = CreaturePreview.Create(AvatarRig.Sources(), Composition(AvatarRig.Human), RigAnimations, now: 0);
        Assert.Equal(CreaturePreview.CharGenHeading, preview.Heading);
        Assert.Equal(180f, PositionFrames.Heading(preview.Frame.Orientation), 3);

        preview.StartAnimation(now: 0);
        Assert.Equal(AvatarRig.WalkAnim, preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(0, preview.Sequence.FrameNumber);
        Assert.Equal(30f, preview.Sequence.CurrentNode.Value.Framerate);

        preview.Advance(0.02); // under 1/30 s: update_position waits
        Assert.Equal(0, preview.Sequence.FrameNumber);
        preview.Advance(0.05); // one step of the whole 0.05 s: 1.5 frames
        Assert.Equal(1.5, preview.Sequence.FrameNumber, 6);
        Assert.Equal(new Vector3(0.05f, 0, 0), Vector3.Transform(preview.Draws[0].Frame.Origin, Quaternion.Inverse(preview.Frame.Orientation)), new Vector3Comparer(1e-5f));

        preview.StopAnimation();
        Assert.Equal(AvatarRig.ReadyAnim, preview.Sequence.CurrentNode!.Value.AnimId);
        preview.Advance(1.0);
        Assert.Equal(0, preview.Sequence.FrameNumber);
    }

    [Fact]
    public void Draws_are_level_0_of_every_part_in_the_view()
    {
        var preview = CreaturePreview.Create(AvatarRig.Sources(), Composition(AvatarRig.Human), RigAnimations, now: 0);
        Assert.Equal(3, preview.Draws.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(preview.Parts[i].Chain.NearestGfxObjId, preview.Draws[i].GfxObjId);
        }

        Assert.Equal(new Vector3(0.3f, 1.9f, 0.65f), CreaturePreview.LightDirection);
        Assert.Equal(2f, CreaturePreview.LightIntensity);
    }

    [Fact]
    public void A_new_look_applies_from_the_default_and_a_new_setup_makes_a_new_object()
    {
        var preview = CreaturePreview.Create(AvatarRig.Sources(), Composition(AvatarRig.Human), RigAnimations, now: 0);
        var swapped = new ObjDescBuilder();
        swapped.AddAnimPartChange(new AnimPartChange(1, 0x01FF0077));
        preview.SetAppearance(Composition(AvatarRig.Human, swapped.Build()), now: 1);
        Assert.Equal(0x01FF0077u, preview.Parts[1].Chain.RootGfxObjId);
        Assert.True(preview.Result.AllApplied);

        preview.SetAppearance(Composition(AvatarRig.Lamp), now: 2);
        Assert.Equal(AvatarRig.Lamp, preview.Setup.Id);
        Assert.Equal(2, preview.Draws.Length);
    }

    /// <summary>
    /// The UIASSET animations: heritage 1 plays 0x03000001 and rests on 0x030003C0 with five idle emotes; the Olthoi
    /// heritages use their own animation (also as the rest) and twitch.
    /// </summary>
    [RequiresDatsFact]
    public void Preview_animations_resolve_through_the_uiasset_map()
    {
        var maps = fixture.Dats.EnumMaps;
        var human = PreviewAnimations.Resolve(maps, 1);
        Assert.Equal(0x03000001u, human.Animation);
        Assert.Equal(0x030003C0u, human.Rest);
        Assert.Equal([0x030000ACu, 0x03000540u, 0x03000508u, 0x03000523u, 0x0300052Eu], human.IdleEmotes);

        foreach (var (heritage, first, second) in new[] { (12, 0x10000011u, 0x10000012u), (13, 0x10000013u, 0x10000014u) })
        {
            var olthoi = PreviewAnimations.Resolve(maps, heritage);
            Assert.Equal(maps.Resolve(Lifestone.Data.Dat.EnumGroup.UiAsset, first), olthoi.Animation);
            Assert.Equal(olthoi.Animation, olthoi.Rest);
            Assert.All(olthoi.IdleEmotes, e => Assert.Equal(maps.Resolve(Lifestone.Data.Dat.EnumGroup.UiAsset, second), e));
        }
    }

    [RequiresDatsFact]
    public void The_chargen_character_animates_in_the_view()
    {
        var dats = fixture.Dats;
        Assert.True(dats.TryGetPortal<CharGen>(0x0E000002, out var charGen));
        var appearanceDats = new DatAppearanceDats(dats);
        var look = AvatarStress.RandomLooks(charGen, appearanceDats, 20, seed: 7).First(l => l.Heritage is >= 1 and <= 3);
        var composition = CharGenAppearance.Compose(charGen, look, appearanceDats);
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        var preview = CreaturePreview.Create(sources, composition, PreviewAnimations.Resolve(dats.EnumMaps, look.Heritage), now: 0);
        Assert.True(preview.Result.AllApplied, preview.Result.ToString());
        preview.StartAnimation(now: 0);
        var pose = preview.PoseVersion;
        for (var i = 1; i <= 30; i++)
        {
            preview.Advance(i / 30.0);
        }

        Assert.True(preview.PoseVersion > pose);
        Assert.True(preview.Sequence.FrameNumber > 20);
        Assert.All(preview.Draws.ToArray(), d => Assert.NotEqual(0u, d.GfxObjId));
    }

    private static CharGenComposition Composition(uint setup, ObjDesc? desc = null) =>
        new(setup, 0, 0, desc ?? ObjDesc.Empty, 0, AppearanceResult.Success);

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector3 obj) => 0;
    }
}
