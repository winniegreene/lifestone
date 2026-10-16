using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>The application's side of character creation's 3D views: records what it was asked to draw.</summary>
internal sealed class RecordingViewHost : ICg3DViewHost
{
    /// <summary>Every attachment made, in order.</summary>
    public List<(uint ViewportId, Cg3DViewModel Model)> Attached { get; } = [];

    /// <summary>Every figure attached (the paperdoll's), in order.</summary>
    public List<(uint ViewportId, ICreatureView Figure)> AttachedFigures { get; } = [];

    /// <summary>Attachments taken away again.</summary>
    public int Detached { get; private set; }

    public IDisposable Attach(uint viewportElementId, Cg3DViewModel model)
    {
        Attached.Add((viewportElementId, model));
        return new Attachment(this);
    }

    public IDisposable? AttachFigure(uint viewportElementId, ICreatureView figure)
    {
        AttachedFigures.Add((viewportElementId, figure));
        return new Attachment(this);
    }

    private sealed class Attachment(RecordingViewHost host) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                host.Detached++;
            }
        }
    }
}

/// <summary>
/// Character creation's 3D character (<see cref="Cg3DViewModel"/>, retail's <c>gmCG3DView</c>) and the pieces it is made of, over the user's dats: the look the
/// state gives, the composition and the preview it makes, the still pose, the camera, the heading and the views the pages attach.
/// </summary>
public sealed class CharGenViewTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private readonly Lazy<Cg3DViewData> _data = new(() => Cg3DViewData.FromDats(fixture.Dats, new Lifestone.Data.Textures.TextureLoader(fixture.Dats), new Lifestone.Data.Models.GfxObjMeshCache(fixture.Dats)));

    private CharGenState State(uint heritage, uint gender, int seed = 3)
    {
        var state = new CharGenState(CharGenCatalog.Load(fixture.Dats), new CharGenRandom(seed));
        state.RandomizeCharacter(true);
        state.SetHeritageGroup(heritage);
        state.SetGender(gender);
        return state;
    }

    // ---------------------------------------------------------------- The look and the composition

    [RequiresLocalDatsTheory]
    [InlineData(1u, 1u)]
    [InlineData(2u, 2u)]
    [InlineData(6u, 1u)]
    [InlineData(12u, 1u)]
    public void The_state_gives_the_look_the_view_composes(uint heritage, uint gender)
    {
        var state = State(heritage, gender);

        var look = state.ToLook();

        Assert.Equal((int)heritage, look.Heritage);
        Assert.Equal((int)gender, look.Sex);
        Assert.Equal(state.HairStyle, look.HairStyle);
        Assert.Equal(state.EyesStrip, look.EyeStrip);
        Assert.Equal(state.NoseStrip, look.NoseStrip);
        Assert.Equal(state.MouthStrip, look.MouthStrip);
        Assert.Equal(state.HairColor, look.HairColor);
        Assert.Equal(state.EyeColor, look.EyeColor);
        Assert.Equal(state.SkinShade, look.SkinShade);
        Assert.Equal(state.HairShade, look.HairShade);
        Assert.Equal(state.ShirtStyle, look.Shirt.Index);
        Assert.Equal(state.GetCharGenResult().ShirtColor, look.Shirt.PaletteTemplate);
        Assert.Equal(state.GetCharGenResult().FootwearColor, look.Footwear.PaletteTemplate);
        Assert.Equal(state.TrousersShade, look.Pants.Shade);
        var composition = CharGenAppearance.Compose(_data.Value.Record, look, _data.Value.Dats);
        Assert.Equal(state.GetSetupId(), composition.SetupId);
    }

    [RequiresLocalDatsTheory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(6u)]
    [InlineData(11u)]
    [InlineData(12u)]
    [InlineData(13u)]
    public void The_view_poses_the_character_of_the_state_with_its_heritages_own_animation_and_rest_animations(uint heritage)
    {
        var state = State(heritage, 1);
        var view = new Cg3DViewModel(state, _data.Value, new RecordingLogSink());

        view.Update(1.0);

        var preview = Assert.IsType<CreaturePreview>(view.Preview);
        Assert.Equal(state.GetSetupId(), preview.Setup.Id);
        Assert.True(preview.Draws.Length > 0);
        Assert.Contains(preview.Draws.ToArray(), d => d.GfxObjId != 0);
        Assert.Equal(CreaturePreview.CharGenHeading, view.Heading);
        Assert.True(view.LastResult.AllApplied, string.Join("; ", view.LastResult.Problems));

        // gmCG3DView::Initialize (0x004EF3F0): the Olthoi heritages (12, 13) have an animation of their own that is also their rest; the others have
        // the CharGen animation (UIASSET 0x10000006) and the paper-doll rest (0x10000005).
        var (animation, rest) = heritage switch
        {
            12u => (0x10000011u, 0x10000011u),
            13u => (0x10000013u, 0x10000013u),
            _ => (0x10000006u, 0x10000005u),
        };
        Assert.Equal(_data.Value.Maps.Resolve(EnumGroup.UiAsset, animation), preview.Animations.Animation);
        Assert.Equal(_data.Value.Maps.Resolve(EnumGroup.UiAsset, rest), preview.Animations.Rest);
        Assert.Equal(PreviewAnimations.Resolve(_data.Value.Maps, (int)heritage).Animation, preview.Animations.Animation);
        Assert.Equal(heritage >= 12, preview.Animations.Animation == preview.Animations.Rest);
    }

    [RequiresLocalDatsTheory]
    [InlineData(1u)]
    [InlineData(12u)]
    [InlineData(13u)]
    public void Start_loops_the_heritages_animation_from_frame_0_at_30_fps_and_the_pose_changes_and_wraps(uint heritage)
    {
        var view = new Cg3DViewModel(State(heritage, 1), _data.Value, new RecordingLogSink());
        view.Update(1.0);
        var preview = view.Preview!;

        view.StartAnimation();

        Assert.True(view.IsAnimating);
        Assert.Equal(preview.Animations.Animation, preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(0, preview.Sequence.FrameNumber);
        Assert.Equal(30f, preview.Sequence.CurrentNode.Value.Framerate);
        var first = preview.Draws.ToArray();
        var version = view.Version;

        // The pose changes with the clock: half a second is 15 frames in.
        view.Advance(1.0 + 0.5);
        Assert.Equal(15.0, preview.Sequence.FrameNumber, 3);
        Assert.NotEqual(version, view.Version);
        Assert.NotEqual(first, preview.Draws.ToArray());

        // And it loops: stepping on, the frame number falls back below an earlier one (the animation's last frame passed) and keeps playing.
        var now = 1.5;
        var wrapped = false;
        var last = preview.Sequence.FrameNumber;
        for (var i = 0; i < 40 && !wrapped; i++)
        {
            now += 0.1;
            view.Advance(now);
            wrapped = preview.Sequence.FrameNumber < last;
            last = preview.Sequence.FrameNumber;
        }

        Assert.True(wrapped, "the animation did not loop within four seconds of play");
        Assert.Equal(preview.Animations.Animation, preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.True(view.IsAnimating);
    }

    [RequiresLocalDatsFact]
    public void Stop_holds_frame_0_of_the_rest_animation_at_0_fps_and_start_loops_again()
    {
        var view = new Cg3DViewModel(State(1, 1), _data.Value, new RecordingLogSink());
        view.Update(1.0);
        var preview = view.Preview!;
        view.StartAnimation();
        view.Advance(1.4);
        var playing = preview.Draws.ToArray();

        view.StopAnimation();

        Assert.False(view.IsAnimating);
        Assert.Equal(preview.Animations.Rest, preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(0f, preview.Sequence.CurrentNode.Value.Framerate);
        Assert.Equal(0, preview.Sequence.FrameNumber);
        var rest = preview.Draws.ToArray();
        Assert.NotEqual(playing, rest);
        var version = view.Version;
        view.Advance(2.0);
        view.Advance(3.0);
        Assert.Equal(version, view.Version); // a held pose costs nothing to step
        Assert.Equal(rest, preview.Draws.ToArray());

        // A start after a long hold plays from frame 0 (not from the time that passed while the rest pose was held).
        view.StartAnimation();
        Assert.True(view.IsAnimating);
        Assert.Equal(0, preview.Sequence.FrameNumber);
        Assert.Equal(preview.Animations.Animation, preview.Sequence.CurrentNode!.Value.AnimId);
        view.Advance(3.0 + 0.1);
        Assert.Equal(3.0, preview.Sequence.FrameNumber, 3);
        Assert.NotEqual(rest, preview.Draws.ToArray());
    }

    [RequiresLocalDatsFact]
    public void A_pinned_animation_holds_the_pose_at_the_chosen_time_until_the_next_start_or_stop()
    {
        var view = new Cg3DViewModel(State(1, 1), _data.Value, new RecordingLogSink());
        view.Update(1.0);
        var preview = view.Preview!;

        Assert.False(view.PinAnimation(0.5)); // nothing is playing yet: a view that was never started holds the rest pose
        view.StartAnimation();
        view.Advance(1.3);

        Assert.True(view.PinAnimation(0.5));
        Assert.True(view.IsPinned);
        Assert.Equal(15.0, preview.Sequence.FrameNumber, 3);
        var pinned = preview.Draws.ToArray();
        view.Advance(5.0);
        view.Advance(5.2);
        Assert.Equal(pinned, preview.Draws.ToArray());

        view.StartAnimation();
        Assert.False(view.IsPinned);
        view.StopAnimation();
        Assert.False(view.PinAnimation(0.5)); // zoomed in: the rest pose is not pinned
    }

    [RequiresLocalDatsFact]
    public void A_changed_choice_makes_the_view_compose_again_and_a_new_body_makes_a_new_object()
    {
        var state = State(1, 1);
        var view = new Cg3DViewModel(state, _data.Value, new RecordingLogSink());
        view.Update(1.0);
        var first = view.Preview!;
        var version = view.Version;

        state.SetGender(2);
        view.Update(2.0);

        Assert.Same(first, view.Preview); // the same heritage animations: the preview is kept, its object replaced
        Assert.Equal(state.GetSetupId(), view.Preview!.Setup.Id);
        Assert.NotEqual(version, view.Version);

        state.SetHeritageGroup(12);
        view.Update(3.0);

        Assert.NotSame(first, view.Preview); // the Olthoi have animations of their own
        Assert.Equal(state.GetSetupId(), view.Preview!.Setup.Id);
        Assert.Equal(PreviewAnimations.Resolve(_data.Value.Maps, 12).Animation, view.Preview.Animations.Animation);
    }

    [RequiresLocalDatsFact]
    public void The_view_does_nothing_before_a_heritage_and_a_gender_and_keeps_the_camera_and_heading_it_is_given()
    {
        var state = new CharGenState(CharGenCatalog.Load(fixture.Dats), new CharGenRandom(1));
        var view = new Cg3DViewModel(state, _data.Value, new RecordingLogSink());

        view.Update(1.0);
        view.SetCamera(new Vector3(0, -2.5f, 0.95f), Vector3.Zero);
        view.SetPlayerHeading(90f);

        Assert.Null(view.Preview);
        Assert.Equal(new Vector3(0, -2.5f, 0.95f), view.CameraPosition);
        Assert.Equal(90f, view.Heading);

        state.RandomizeCharacter(true);
        view.Update(2.0);

        Assert.NotNull(view.Preview);
        Assert.Equal(90f, view.Preview!.Heading); // the heading given before the character existed is applied to it
    }

    [RequiresLocalDatsTheory]
    [InlineData(1u)]
    [InlineData(4u)]
    [InlineData(12u)]
    public void The_heritages_backdrop_is_one_static_part_at_the_origin(uint heritage)
    {
        var state = State(heritage, 1);
        var view = new Cg3DViewModel(state, _data.Value, new RecordingLogSink());

        view.Update(1.0);

        var hg = CharGenCatalog.Load(fixture.Dats).Data.GetHeritage(heritage);
        Assert.Equal(hg.EnvironmentSetupId, view.BackdropSetupId);
        Assert.NotEmpty(view.BackdropDraws.ToArray());
        Assert.All(view.BackdropDraws.ToArray(), d => Assert.NotEqual(0u, d.GfxObjId));
    }

    [RequiresLocalDatsFact]
    public void The_light_is_creature_modes_ambient_and_one_distant_light_towards_the_charGen_vector()
    {
        Assert.Equal(new Vector3(0.3f, 1.9f, 0.65f), Cg3DViewModel.LightDirection);
        Assert.Equal(new Vector3(0.3f, 0.3f, 0.3f), Cg3DViewModel.Ambient);
        Assert.Equal(2.0f, CreaturePreview.LightIntensity);
    }

    // ---------------------------------------------------------------- The camera tables

    [Theory]
    [InlineData(1u, 0f, -2.5f, 0.95f, 0f, -0.55f, 1.65f)]
    [InlineData(7u, 0f, -2.5f, 0.95f, 0f, -0.85f, 1.65f)]
    [InlineData(12u, 0f, -3.8f, 1.15f, 0f, -1.85f, 1.85f)]
    [InlineData(13u, 0f, -5.7f, 1.65f, 0f, -3.05f, 2.75f)]
    public void The_cameras_are_the_ones_the_pages_put_the_view_at(uint heritage, float bx, float by, float bz, float fx, float fy, float fz)
    {
        Assert.Equal(new Vector3(bx, by, bz), CharGenRules.BodyCamera(heritage));
        Assert.Equal(new Vector3(fx, fy, fz), CharGenRules.FaceCamera(heritage));
    }
}
