using System.Numerics;
using Lifestone.Client.PortalSpace;
using Lifestone.Client.RetailUi.CharacterCreation;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The appearance page (retail's <c>gmCGAppearancePage</c>) on the real screen over the user's dats: the face and clothes halves, the spinners, the colour wheel
/// and its pictures, the shade slider, the zoom and the turn, the heritages that have no clothes, the Random button, and the 3D character the page attaches.
/// </summary>
public sealed class CharGenAppearanceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private LoginRig Rig(RecordingViewHost? host = null, bool view3D = true, int seed = 7, UiRetailGlitches glitches = UiRetailGlitches.None) =>
        new(fixture, startMode: UiModes.CharGen, seed: seed, view3D: view3D, viewHost: host, glitches: glitches);

    private static CharGenMainScreen Open(LoginRig rig)
    {
        rig.Run(0.3);
        return Assert.IsType<CharGenMainScreen>(rig.Flow.CurrentScreen);
    }

    private static CharGenAppearancePage ToAppearance(LoginRig rig, CharGenMainScreen screen)
    {
        ClickAndRun(rig, screen.TabOf(CharGenPageId.Appearance)!);
        rig.Run(0.8);
        return screen.Appearance!;
    }

    private static void ClickAndRun(LoginRig rig, UiElement element)
    {
        Assert.True(rig.Click(element), $"the click on element 0x{element.ElementId:X8} reached nothing");
        rig.Run(0.1);
    }

    private string Pregame(string token) => new UiStrings(new UiStringTables(fixture.Dats)).Render(new UiStringRef(StringHash.Tokenize(token), GmUiFlow.PregameTableId)).Text;

    // ---------------------------------------------------------------- The pictures (no dats)

    [Fact]
    public void A_colour_spot_is_the_blank_bullet_with_its_opaque_black_replaced()
    {
        var blank = new UiImage(0, 2, 2, UiPixelFormat.Argb, [0, 0, 0, 255, /**/ 0, 0, 0, 128, /**/ 10, 20, 30, 255, /**/ 0, 0, 1, 255]);

        var spot = CharGenSwatches.ReplaceBlack(blank, 200, 100, 50);

        Assert.Equal(new byte[] { 200, 100, 50, 255, /**/ 0, 0, 0, 128, /**/ 10, 20, 30, 255, /**/ 0, 0, 1, 255 }, spot.Pixels);
        Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 0, 128, 10, 20, 30, 255, 0, 0, 1, 255 }, blank.Pixels); // the source is not changed
    }

    [Fact]
    public void An_opaque_source_format_is_copied_opaque()
    {
        var xrgb = new UiImage(0, 1, 1, UiPixelFormat.Xrgb, [0, 0, 0, 7]);

        Assert.Equal(new byte[] { 0, 0, 0, 255 }, CharGenSwatches.Copy(xrgb).Pixels);
        Assert.Equal(new byte[] { 9, 8, 7, 255 }, CharGenSwatches.ReplaceBlack(xrgb, 9, 8, 7).Pixels);
        Assert.Equal(UiPixelFormat.Argb, CharGenSwatches.Copy(xrgb).Format);
    }

    [Theory]
    [InlineData(255, 200, 200)]
    [InlineData(0, 200, 0)]
    [InlineData(128, 200, 100)]
    [InlineData(1, 255, 1)]
    public void The_gradient_disk_scales_each_channel_by_the_colour_plus_one_over_256(int colour, int source, int expected)
    {
        var ring = new UiImage(0, 1, 1, UiPixelFormat.Argb, [(byte)source, (byte)source, (byte)source, 77]);

        var disk = CharGenSwatches.Multiply(ring, (byte)colour, (byte)colour, (byte)colour);

        Assert.Equal(new[] { (byte)expected, (byte)expected, (byte)expected, (byte)77 }, disk.Pixels);
    }

    // ---------------------------------------------------------------- Opening and the halves

    [RequiresLocalDatsFact]
    public void The_page_opens_on_the_face_half_zoomed_in_with_its_character_attached_to_its_viewport()
    {
        var host = new RecordingViewHost();
        using var rig = Rig(host);
        var screen = Open(rig);

        var page = ToAppearance(rig, screen);

        Assert.Equal(CharGenAppearanceType.Face, page.CurrentType);
        Assert.Equal(CharGenAppearancePart.Hair, page.CurrentPart);
        Assert.True(page.IsZoomedIn);
        Assert.False(page.IsZooming);
        Assert.Equal(CharGenRules.FaceCamera(screen.State.Heritage), page.View!.CameraPosition);
        Assert.Equal(CreaturePreviewHeading, page.View.Heading);
        Assert.NotNull(page.View.Preview);
        Assert.Equal(screen.State.GetSetupId(), page.View.Preview!.Setup.Id);
        Assert.Contains(host.Attached, a => a.ViewportId == CharGenMainScreen.AppearanceViewportId && ReferenceEquals(a.Model, page.View));
        Assert.Contains(host.Attached, a => a.ViewportId == CharGenMainScreen.SummaryViewportId && ReferenceEquals(a.Model, screen.Summary!.View));
        Assert.Equal(2, host.Attached.Count);
        Assert.Equal(-1, screen.State.HeadgearStyle); // the face half takes the headgear off
        Assert.Equal(UiStateIds.Highlight, page.ZoomInButton!.State);
        Assert.Equal(CharGenRules.SelectedState, page.FaceButton!.State);
        Assert.Equal(CharGenRules.UnselectedState, page.ClothesButton!.State);
        Assert.True(page.Choices(CharGenAppearanceType.Face)!.Visible);
        Assert.False(page.Choices(CharGenAppearanceType.Clothes)!.Visible);
        Assert.Equal(UiStateIds.Highlight, page.Spinner(CharGenAppearancePart.Hair)!.State);
    }

    private const float CreaturePreviewHeading = 180f;

    [RequiresLocalDatsFact]
    public void The_view_is_detached_when_the_screen_goes()
    {
        var host = new RecordingViewHost();
        using var rig = Rig(host);
        var screen = Open(rig);
        Assert.Equal(2, host.Attached.Count);

        rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        rig.Run(0.5);

        Assert.Equal(2, host.Detached);
    }

    [RequiresLocalDatsFact]
    public void The_clothes_tab_gives_the_headgear_back_and_the_face_tab_takes_it_off_again()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(2);
        Assert.Equal(2, state.HeadgearStyle);

        ClickAndRun(rig, page.FaceButton!);
        Assert.Equal(-1, state.HeadgearStyle);
        Assert.Equal(CharGenAppearancePart.Hair, page.CurrentPart);
        ClickAndRun(rig, page.ClothesButton!);

        Assert.Equal(2, state.HeadgearStyle);
        Assert.Equal(CharGenAppearanceType.Clothes, page.CurrentType);
        Assert.Equal(CharGenAppearancePart.Headgear, page.CurrentPart);
        Assert.True(page.Choices(CharGenAppearanceType.Clothes)!.Visible);
        Assert.False(page.Choices(CharGenAppearanceType.Face)!.Visible);
        Assert.Equal(CharGenRules.SelectedState, page.ClothesButton!.State);
        Assert.Equal(UiStateIds.Highlight, page.ZoomOutButton!.State);
        Assert.Equal(UiStateIds.Normal, page.ZoomInButton!.State);
    }

    [RequiresLocalDatsFact]
    public void The_camera_zooms_in_a_straight_line_over_six_tenths_of_a_second()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var face = CharGenRules.FaceCamera(screen.State.Heritage);
        var body = CharGenRules.BodyCamera(screen.State.Heritage);

        ClickAndRun(rig, page.ClothesButton!); // 0.1 s: the zoom is under way, its first frame has started the clock
        Assert.False(page.IsZoomedIn);
        Assert.True(page.IsZooming);
        Assert.Equal(body, page.CameraTarget);
        var elapsed = 0.1 - LoginRig.Step; // the clock started on the first frame after the click

        for (var i = 0; i < 20; i++)
        {
            rig.Frame();
            elapsed += LoginRig.Step;
            if (!page.IsZooming)
            {
                break;
            }

            var t = (float)Math.Min(elapsed / CharGenAppearancePage.ZoomSeconds, 1.0);
            var expected = Vector3.Lerp(face, body, t);
            Assert.True(Vector3.Distance(expected, page.CameraPosition) < 0.06f, $"at {elapsed:0.000} s the camera is {page.CameraPosition}, expected about {expected}");
            Assert.Equal(page.CameraPosition, page.View!.CameraPosition);
        }

        rig.Run(0.7);
        Assert.False(page.IsZooming);
        Assert.Equal(body, page.CameraPosition);
        Assert.Equal(body, page.View!.CameraPosition);

        ClickAndRun(rig, page.ZoomInButton!);
        rig.Run(0.8);
        Assert.True(page.IsZoomedIn);
        Assert.Equal(face, page.CameraPosition);
    }

    [RequiresLocalDatsFact]
    public void A_turn_button_turns_the_character_a_whole_turn_in_five_seconds_and_the_same_button_stops_it()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        Assert.Equal(180f, page.Heading);

        ClickAndRun(rig, page.RotateClockButton!);
        Assert.True(page.IsRotating);
        rig.Run(1.25);
        Assert.InRange(page.Heading, 270f, 285f); // 1.35 s of a 5 s turn: 97 degrees on from 180
        Assert.Equal(page.Heading, page.View!.Heading);

        ClickAndRun(rig, page.RotateCounterClockButton!); // the other button takes over
        Assert.True(page.IsRotating);
        Assert.Equal(UiStateIds.Normal, page.RotateClockButton!.State);
        var before = page.Heading;
        rig.Run(0.5);
        Assert.True(page.Heading < before);

        ClickAndRun(rig, page.RotateCounterClockButton!);
        Assert.False(page.IsRotating);
        var stopped = page.Heading;
        rig.Run(0.5);
        Assert.Equal(stopped, page.Heading);
    }

    [RequiresLocalDatsFact]
    public void The_heading_wraps_through_zero_and_360()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        ClickAndRun(rig, page.RotateCounterClockButton!);
        rig.Run(3.0); // 216 degrees back from 180

        Assert.InRange(page.Heading, 0f, 360f);
        Assert.InRange(page.Heading, 315f, 335f);
    }

    // ---------------------------------------------------------------- The spinners

    [RequiresLocalDatsFact]
    public void The_arrows_step_the_styles_round_and_the_headgears_also_hold_none()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        var hair = page.Choice(CharGenAppearancePart.Hair);
        var start = state.HairStyle;

        for (var i = 0; i < hair.NumChoices; i++)
        {
            ClickAndRun(rig, page.Arrow(CharGenAppearancePart.Hair, CharGenAppearancePage.RightArrowId)!);
            Assert.Equal(state.HairStyle, hair.CurrentChoice);
            Assert.InRange(state.HairStyle, 0, hair.NumChoices - 1);
        }

        Assert.Equal(start, state.HairStyle); // a whole round
        ClickAndRun(rig, page.Arrow(CharGenAppearancePart.Hair, CharGenAppearancePage.LeftArrowId)!);
        Assert.Equal((start + hair.NumChoices - 1) % hair.NumChoices, state.HairStyle);

        ClickAndRun(rig, page.ClothesButton!);
        var headgear = page.Choice(CharGenAppearancePart.Headgear);
        state.SetHeadgearStyle(-1);
        headgear.CurrentChoice = -1;
        var seen = new List<int>();
        for (var i = 0; i <= headgear.NumChoices; i++)
        {
            ClickAndRun(rig, page.Arrow(CharGenAppearancePart.Headgear, CharGenAppearancePage.RightArrowId)!);
            seen.Add(headgear.CurrentChoice);
        }

        Assert.Equal([.. Enumerable.Range(0, headgear.NumChoices), -1], seen); // 0 to n-1, then none again
        ClickAndRun(rig, page.Arrow(CharGenAppearancePart.Headgear, CharGenAppearancePage.LeftArrowId)!);
        Assert.Equal(headgear.NumChoices - 1, headgear.CurrentChoice);
    }

    [RequiresLocalDatsFact]
    public void Each_part_s_arrows_write_their_own_field_of_the_state()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;

        page.Step(CharGenAppearancePart.Eyes, 1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Eyes).CurrentChoice, state.EyesStrip);
        page.Step(CharGenAppearancePart.Nose, 1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Nose).CurrentChoice, state.NoseStrip);
        page.Step(CharGenAppearancePart.Mouth, 1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Mouth).CurrentChoice, state.MouthStrip);
        page.Step(CharGenAppearancePart.Shirt, 1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Shirt).CurrentChoice, state.ShirtStyle);
        page.Step(CharGenAppearancePart.Trousers, -1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Trousers).CurrentChoice, state.TrousersStyle);
        page.Step(CharGenAppearancePart.Footwear, 1);
        Assert.Equal(page.Choice(CharGenAppearancePart.Footwear).CurrentChoice, state.FootwearStyle);
        Assert.Equal(CharGenAppearancePart.Footwear, page.CurrentPart);
        Assert.Equal(UiStateIds.Highlight, page.Spinner(CharGenAppearancePart.Footwear)!.State);
        Assert.Equal(UiStateIds.Normal, page.Spinner(CharGenAppearancePart.Hair)!.State);
    }

    [RequiresLocalDatsFact]
    public void The_skin_spinner_ghosts_its_arrows_and_selects_the_skin()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        Assert.Equal(UiStateIds.Ghosted, page.Arrow(CharGenAppearancePart.Skin, CharGenAppearancePage.LeftArrowId)!.State);
        Assert.Equal(UiStateIds.Ghosted, page.Arrow(CharGenAppearancePart.Skin, CharGenAppearancePage.RightArrowId)!.State);

        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Skin)!);

        Assert.Equal(CharGenAppearancePart.Skin, page.CurrentPart);
        Assert.Equal(1, page.Choice(CharGenAppearancePart.Skin).NumColors);
        Assert.True(page.ShadeScroll!.Visible);
    }

    // ---------------------------------------------------------------- The wheel, the shade, the pictures

    [RequiresLocalDatsFact]
    public void A_colour_spot_picks_the_colour_and_spots_past_the_colours_do_nothing()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        var colours = page.Choice(CharGenAppearancePart.Hair).NumColors;
        Assert.InRange(colours, 2, CharGenAppearancePage.SpotCount);
        var pick = state.HairColor == 0 ? 1 : 0;

        ClickAndRun(rig, page.Bullet(pick)!);

        Assert.Equal(pick, state.HairColor);
        Assert.Equal(pick, page.CurrentColor);
        for (var i = 0; i < CharGenAppearancePage.SpotCount; i++)
        {
            Assert.Equal(i == pick, page.Pointer(i)!.Visible);
        }

        if (colours < CharGenAppearancePage.SpotCount)
        {
            ClickAndRun(rig, page.Bullet(colours)!);
            Assert.Equal(pick, state.HairColor);
        }
    }

    [RequiresLocalDatsFact]
    public void The_wheel_shows_the_parts_colours_sampled_from_its_palette_sets()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var data = screen.View3DData!;
        var hg = CharGenCatalog.Load(fixture.Dats).Data.GetHeritage(screen.State.Heritage);
        var sx = hg.Genders[screen.State.Gender];

        for (var i = 0; i < sx.HairColors.Count; i++)
        {
            var palettes = data.PalSetPalettes(sx.HairColors[i]);
            var r = palettes.Sum(p => data.ColorFromPalette(p, 208)!.Value.R) / palettes.Count;
            var g = palettes.Sum(p => data.ColorFromPalette(p, 208)!.Value.G) / palettes.Count;
            var b = palettes.Sum(p => data.ColorFromPalette(p, 208)!.Value.B) / palettes.Count;
            Assert.Equal(((byte)r, (byte)g, (byte)b), page.SpotColor(i));
        }

        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Eyes)!);
        for (var i = 0; i < sx.EyeColors.Count; i++)
        {
            Assert.Equal(data.ColorFromPalette(sx.EyeColors[i], 259), page.SpotColor(i));
        }

        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Skin)!);
        Assert.NotNull(page.SpotColor(0));
        Assert.Null(page.SpotColor(1));
    }

    [RequiresLocalDatsFact]
    public void The_colour_spots_and_the_disk_are_made_from_the_layouts_bullets_and_ring()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var data = screen.View3DData!;
        var images = rig.Manager.Services.Images!;
        UiImage Image(uint asset)
        {
            Assert.Equal(UiImageLookup.Ready, images.TryGetImage(data.UiAssetDid(asset), out var image));
            return image!;
        }

        var blank = Image(UiAssetIds.ColorBullet);
        var cap = Image(UiAssetIds.ColorEmpty);
        var ring = Image(UiAssetIds.ColorRing);
        var plug = Image(UiAssetIds.GradientPlug);
        var colours = page.Choice(CharGenAppearancePart.Hair).NumColors;
        for (var i = 0; i < CharGenAppearancePage.SpotCount; i++)
        {
            var made = Assert.IsType<UiImage>(page.Bullet(i)!.GeneratedImage);
            if (i < colours)
            {
                var (r, g, b) = page.SpotColor(i)!.Value;
                Assert.Equal(CharGenSwatches.ReplaceBlack(blank, r, g, b).Pixels, made.Pixels);
            }
            else
            {
                Assert.Equal(CharGenSwatches.Copy(cap).Pixels, made.Pixels);
            }
        }

        var current = page.SpotColor(page.CurrentColor)!.Value;
        Assert.Equal(CharGenSwatches.Multiply(ring, current.R, current.G, current.B).Pixels, page.GradCircle!.GeneratedImage!.Pixels);

        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Eyes)!); // the eyes show the plug and have no slider
        Assert.Equal(CharGenSwatches.Copy(plug).Pixels, page.GradCircle.GeneratedImage!.Pixels);
        Assert.False(page.ShadeScroll!.Visible);
        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Hair)!);
        Assert.True(page.ShadeScroll.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_shade_slider_sets_the_shade_of_the_selected_part_clamped_to_zero_and_one()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;

        page.ShadeScroll!.SetScrollbarPosition(0.25f);
        Assert.Equal(0.25, state.HairShade, 3);
        page.ShadeScroll.SetScrollbarPosition(1f);
        Assert.Equal(1.0, state.HairShade, 3);

        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Nose)!); // the nose, the mouth and the skin share the skin shade
        page.ShadeScroll.SetScrollbarPosition(0.5f);
        Assert.Equal(0.5, state.SkinShade, 3);

        ClickAndRun(rig, page.ClothesButton!);
        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Shirt)!);
        page.ShadeScroll.SetScrollbarPosition(0.75f);
        Assert.Equal(0.75, state.ShirtShade, 3);
        Assert.Equal(0.5, state.SkinShade, 3);
        Assert.Equal(0.75, page.Choice(CharGenAppearancePart.Shirt).Shade, 3);

        page.SetShade(7.0); // outside the slider's range the page's own clamp is the slider message's: 0.001 x position, clamped
        Assert.Equal(7.0, state.ShirtShade, 3);
    }

    [RequiresLocalDatsFact]
    public void A_garment_s_colour_goes_to_its_own_colour_field()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Shirt)!);
        var colours = page.Choice(CharGenAppearancePart.Shirt).NumColors;
        Assert.True(colours >= 2);
        var pick = state.ShirtColor == 1 ? 0 : 1;

        ClickAndRun(rig, page.Bullet(pick)!);

        Assert.Equal(pick, state.ShirtColor);
        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Footwear)!);
        ClickAndRun(rig, page.Bullet(0)!);
        Assert.Equal(0, state.FootwearColor);
        Assert.Equal(pick, state.ShirtColor);
    }

    // ---------------------------------------------------------------- The camera and the headgear (glitch fixes)

    [RequiresLocalDatsFact]
    public void Leaving_the_clothes_half_and_coming_back_keeps_the_whole_figure_and_the_zoom_buttons_work()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var body = CharGenRules.BodyCamera(screen.State.Heritage);
        var face = CharGenRules.FaceCamera(screen.State.Heritage);
        ClickAndRun(rig, page.ClothesButton!);
        rig.Run(1.0);
        Assert.Equal(body, page.CameraPosition);

        ClickAndRun(rig, screen.RightButton!);
        Assert.NotEqual(CharGenPageId.Appearance, screen.Progress);
        ClickAndRun(rig, screen.LeftButton!);
        rig.Run(0.8);

        Assert.Equal(CharGenPageId.Appearance, screen.Progress);
        Assert.Equal(CharGenAppearanceType.Clothes, page.CurrentType);
        Assert.False(page.IsZoomedIn);
        Assert.Equal(body, page.CameraPosition);
        Assert.Equal(body, page.View!.CameraPosition);

        ClickAndRun(rig, page.ZoomInButton!);
        rig.Run(1.0);
        Assert.True(page.IsZoomedIn);
        Assert.Equal(face, page.CameraPosition);
        ClickAndRun(rig, page.ZoomOutButton!);
        rig.Run(1.0);
        Assert.False(page.IsZoomedIn);
        Assert.Equal(body, page.CameraPosition);
    }

    // ---------------------------------------------------------------- The view's animation (D60)

    /// <summary>The pose of a view's character as the host would draw it.</summary>
    private static Lifestone.Client.Effects.PartDraw[] Pose(Cg3DViewModel view) => view.Preview!.Draws.ToArray();

    /// <summary>Runs <paramref name="seconds"/> of frames and tells whether the pose changed at any frame (a loop moves, the held rest pose does not).</summary>
    private static bool PoseMoves(LoginRig rig, Cg3DViewModel view, double seconds)
    {
        var first = Pose(view);
        for (var t = 0.0; t < seconds; t += 0.1)
        {
            rig.Run(0.1);
            if (!first.AsSpan().SequenceEqual(Pose(view)))
            {
                return true;
            }
        }

        return false;
    }

    [RequiresLocalDatsFact]
    public void The_summary_view_is_started_when_the_page_is_made_and_loops_while_the_page_shows()
    {
        using var rig = Rig();
        var screen = Open(rig);
        ToAppearance(rig, screen); // the pages are made when the player first reaches them (the summary's view is made with the appearance page's)
        var view = screen.Summary!.View!;

        // gmCGSummaryPage::InitializePage (0x0047BFB0) starts the animation once, before the page is shown.
        // The character is composed when the page is first updated (shown); it then plays the animation the start asked for.
        Assert.True(view.IsAnimating);

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Summary)!);
        rig.Run(0.3);
        Assert.Equal(CharGenPageId.Summary, screen.Progress);
        Assert.True(view.IsAnimating);
        Assert.Equal(view.Preview!.Animations.Animation, view.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.True(PoseMoves(rig, view, 0.5), "the summary view's pose did not change over half a second");

        // It loops: the frame number falls back at the end of the animation and the animation goes on.
        var last = view.Preview.Sequence.FrameNumber;
        var wrapped = false;
        for (var i = 0; i < 30 && !wrapped; i++)
        {
            rig.Run(0.1);
            wrapped = view.Preview.Sequence.FrameNumber < last;
            last = view.Preview.Sequence.FrameNumber;
        }

        Assert.True(wrapped, "the summary view's animation did not loop within three seconds");
        Assert.True(view.IsAnimating);
        Assert.True(PoseMoves(rig, view, 0.5));
    }

    [RequiresLocalDatsFact]
    public void Zoomed_in_the_view_holds_the_rest_pose_at_0_fps_and_zooming_out_loops_again_and_zooming_in_stops_it_at_once()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var view = page.View!;

        // The page opens zoomed in on the face: ZoomIn (0x0047D2B0) stopped the animation (the Update at the page's show, 0x0047ECA0, does so too).
        Assert.True(page.IsZoomedIn);
        Assert.False(view.IsAnimating);
        Assert.Equal(view.Preview!.Animations.Rest, view.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(0f, view.Preview.Sequence.CurrentNode.Value.Framerate);
        Assert.False(PoseMoves(rig, view, 1.0), "the zoomed-in view's pose moved");

        // ZoomOut (0x0047D400) starts the animation as the zoom begins, not when it ends.
        ClickAndRun(rig, page.ClothesButton!);
        Assert.False(page.IsZoomedIn);
        Assert.True(page.IsZooming);
        Assert.True(view.IsAnimating);
        Assert.Equal(view.Preview.Animations.Animation, view.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(30f, view.Preview.Sequence.CurrentNode.Value.Framerate);
        rig.Run(1.0);
        Assert.False(page.IsZooming);
        Assert.True(PoseMoves(rig, view, 0.5), "the zoomed-out view's pose did not change");

        // ZoomIn stops it at once, with the camera still moving, and the rest pose stays through the zoom and after it.
        ClickAndRun(rig, page.ZoomInButton!);
        Assert.True(page.IsZoomedIn);
        Assert.True(page.IsZooming);
        Assert.False(view.IsAnimating);
        Assert.Equal(view.Preview.Animations.Rest, view.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(0, view.Preview.Sequence.FrameNumber);
        Assert.False(PoseMoves(rig, view, 1.0));

        // And out again: it loops again from frame 0.
        ClickAndRun(rig, page.ZoomOutButton!);
        Assert.True(view.IsAnimating);
        Assert.True(PoseMoves(rig, view, 0.5));
    }

    [RequiresLocalDatsFact]
    public void A_selection_while_zoomed_out_keeps_the_loop_going_and_an_update_starts_or_stops_it_by_the_zoom()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var view = page.View!;
        ClickAndRun(rig, page.ClothesButton!);
        rig.Run(1.0);
        Assert.True(view.IsAnimating);

        // gmCGAppearancePage::SetSelection (0x0047E610) does not touch the animation: it keeps looping, from the frame it had.
        var before = view.Preview!.Sequence.FrameNumber;
        page.SetSelection(CharGenAppearancePart.Shirt);
        Assert.True(view.IsAnimating);
        Assert.Equal(view.Preview.Animations.Animation, view.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.Equal(before, view.Preview.Sequence.FrameNumber);
        Assert.True(PoseMoves(rig, view, 0.5));

        // Update (0x0047ECA0), the page's refresh when it is shown: a start when zoomed out (from frame 0), a stop when zoomed in.
        rig.Run(0.5);
        page.Update();
        Assert.True(view.IsAnimating);
        Assert.Equal(0, view.Preview.Sequence.FrameNumber);
        ClickAndRun(rig, page.ZoomInButton!);
        rig.Run(1.0);
        page.Update();
        Assert.False(view.IsAnimating);
        Assert.Equal(view.Preview.Animations.Rest, view.Preview.Sequence.CurrentNode!.Value.AnimId);
    }

    [RequiresLocalDatsTheory]
    [InlineData(12u, 0x10000011u)]
    [InlineData(13u, 0x10000013u)]
    public void The_Olthoi_heritages_loop_their_own_animation_which_is_also_their_rest(uint heritage, uint animationEnum)
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        screen.State.SetHeritageGroup(heritage);
        page.Update();
        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RightButtonId)); // the Olthoi skip the profession, skills and town pages
        rig.Run(0.5);

        var expected = new EnumIdMaps(fixture.Dats).Resolve(EnumGroup.UiAsset, animationEnum);
        Assert.Equal(CharGenPageId.Summary, screen.Progress);
        var summary = screen.Summary!.View!;
        Assert.True(summary.IsAnimating);
        Assert.Equal(expected, summary.Preview!.Animations.Animation);
        Assert.Equal(expected, summary.Preview.Animations.Rest);
        Assert.Equal(expected, summary.Preview.Sequence.CurrentNode!.Value.AnimId);
        Assert.True(PoseMoves(rig, summary, 1.0), "the Olthoi's summary pose did not change");
    }

    [RequiresLocalDatsFact]
    public void Random_and_a_gender_button_on_the_clothes_half_keep_the_whole_figure()
    {
        using var rig = Rig(seed: 11);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var body = CharGenRules.BodyCamera(screen.State.Heritage);
        ClickAndRun(rig, page.ClothesButton!);
        rig.Run(1.0);

        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RandomButtonId));
        Assert.False(page.IsZoomedIn);
        Assert.Equal(body, page.CameraPosition);
        Assert.Equal(body, page.View!.CameraPosition);

        foreach (var gender in new[] { page.MaleButton!, page.FemaleButton! })
        {
            ClickAndRun(rig, gender);
            Assert.False(page.IsZoomedIn);
            Assert.Equal(body, page.CameraPosition);
            Assert.Equal(body, page.View!.CameraPosition);
        }

        ClickAndRun(rig, page.FaceButton!);
        rig.Run(1.0);
        Assert.Equal(CharGenRules.FaceCamera(screen.State.Heritage), page.CameraPosition);
    }

    [RequiresLocalDatsFact]
    public void Retails_camera_snaps_to_the_face_on_a_gender_button_whatever_the_half_and_zoom_out_then_does_nothing()
    {
        using var rig = Rig(glitches: UiRetailGlitches.CharGenAppearanceCamera);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var face = CharGenRules.FaceCamera(screen.State.Heritage);
        ClickAndRun(rig, page.ClothesButton!);
        rig.Run(1.0);

        ClickAndRun(rig, page.FemaleButton!);
        ClickAndRun(rig, page.MaleButton!);

        Assert.Equal(CharGenAppearanceType.Clothes, page.CurrentType);
        Assert.False(page.IsZoomedIn);
        Assert.Equal(face, page.CameraPosition);
        ClickAndRun(rig, page.ZoomOutButton!);
        Assert.False(page.IsZooming);
        Assert.Equal(face, page.CameraPosition);
    }

    [RequiresLocalDatsFact]
    public void A_headgear_held_in_the_face_half_goes_back_when_the_page_is_left_and_is_held_again_on_return()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(2);
        ClickAndRun(rig, page.FaceButton!);
        Assert.Equal(-1, state.HeadgearStyle);

        ClickAndRun(rig, screen.RightButton!);
        Assert.Equal(2, state.HeadgearStyle); // it reaches the other pages and the created character

        ClickAndRun(rig, screen.LeftButton!);
        Assert.Equal(CharGenAppearanceType.Face, page.CurrentType);
        Assert.Equal(-1, state.HeadgearStyle); // the face half shows the head again
        ClickAndRun(rig, page.ClothesButton!);
        Assert.Equal(2, state.HeadgearStyle);
    }

    [RequiresLocalDatsFact]
    public void A_headgear_cleared_on_the_clothes_half_stays_cleared_when_the_page_is_left()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(2);
        ClickAndRun(rig, page.FaceButton!);
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(-1);

        ClickAndRun(rig, page.FaceButton!);
        ClickAndRun(rig, screen.RightButton!);

        Assert.Equal(-1, state.HeadgearStyle);
    }

    [RequiresLocalDatsFact]
    public void A_headgear_the_summary_page_s_Random_rolls_is_the_one_the_clothes_half_shows_next()
    {
        using var rig = Rig(seed: 3);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(0);
        ClickAndRun(rig, page.FaceButton!);

        ClickAndRun(rig, screen.TabOf(CharGenPageId.Summary)!);
        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RandomButtonId));
        ClickAndRun(rig, rig.Dialog(screen.RandomizeDialog)!.GetChildRecursive(UiConfirmationDialog.YesButton)!);
        var rolled = state.HeadgearStyle;
        Assert.NotEqual(0, rolled); // not the hat the page held before, so the test tells the stale one from the new
        ClickAndRun(rig, screen.TabOf(CharGenPageId.Appearance)!);
        Assert.Equal(CharGenPageId.Appearance, screen.Progress);
        Assert.Equal(-1, state.HeadgearStyle); // the face half takes the rolled hat off
        ClickAndRun(rig, page.ClothesButton!);

        Assert.Equal(rolled, state.HeadgearStyle);
    }

    [RequiresLocalDatsFact]
    public void Retail_drops_a_headgear_held_in_the_face_half_when_the_page_is_left()
    {
        using var rig = Rig(glitches: UiRetailGlitches.CharGenHeldHeadgear);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;
        ClickAndRun(rig, page.ClothesButton!);
        state.SetHeadgearStyle(2);
        ClickAndRun(rig, page.FaceButton!);

        ClickAndRun(rig, screen.RightButton!);

        Assert.Equal(-1, state.HeadgearStyle);
    }

    // ---------------------------------------------------------------- Gender, heritages, Random

    [RequiresLocalDatsFact]
    public void The_gender_buttons_set_the_gender_and_show_it()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        ClickAndRun(rig, page.MaleButton!);
        Assert.Equal(1u, screen.State.Gender);
        Assert.Equal(CharGenRules.SelectedState, page.MaleButton!.State);
        Assert.Equal(CharGenRules.UnselectedState, page.FemaleButton!.State);
        Assert.Equal(screen.State.GetSetupId(), page.View!.Preview!.Setup.Id);

        ClickAndRun(rig, page.FemaleButton!);
        Assert.Equal(2u, screen.State.Gender);
        Assert.Equal(CharGenRules.SelectedState, page.FemaleButton!.State);
        Assert.Equal(CharGenRules.UnselectedState, page.MaleButton!.State);
        Assert.Equal(screen.State.GetSetupId(), page.View!.Preview!.Setup.Id);
        Assert.Equal(page.Choice(CharGenAppearancePart.Hair).NumChoices, CharGenCatalog.Load(fixture.Dats).Data.GetHeritage(screen.State.Heritage).Genders[2].HairStyles.Count);
    }

    [RequiresLocalDatsTheory]
    [InlineData(6u, "ID_CharGen_GearText_")]
    [InlineData(12u, "ID_CharGen_OlthoiText_")]
    [InlineData(13u, "ID_CharGen_OlthoiText_")]
    public void The_Gear_Knights_and_the_Olthoi_have_no_clothes_nose_or_mouth_and_other_texts(uint heritage, string stem)
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        Assert.Equal(180, page.Spinner(CharGenAppearancePart.Skin)!.Box.Y0); // a human's skin spinner stands last

        screen.State.SetHeritageGroup(heritage);
        page.Update();

        Assert.False(page.ClothesButton!.Visible);
        Assert.False(page.Spinner(CharGenAppearancePart.Nose)!.Visible);
        Assert.False(page.Spinner(CharGenAppearancePart.Mouth)!.Visible);
        Assert.Equal(90, page.Spinner(CharGenAppearancePart.Skin)!.Box.Y0);
        Assert.Equal(Pregame(stem + "HairButton"), page.Spinner(CharGenAppearancePart.Hair)!.Text);
        Assert.Equal(Pregame(stem + "EyesButton"), page.Spinner(CharGenAppearancePart.Eyes)!.Text);
        Assert.Equal(Pregame(stem + "SkinButton"), page.Spinner(CharGenAppearancePart.Skin)!.Text);
        Assert.Equal(UiStateIds.Ghosted, page.Arrow(CharGenAppearancePart.Eyes, CharGenAppearancePage.LeftArrowId)!.State);
        Assert.Equal(CharGenAppearanceType.Face, page.CurrentType);
        Assert.Equal(CharGenRules.FaceCamera(heritage), page.CameraPosition);
        Assert.Equal(screen.State.GetSetupId(), page.View!.Preview!.Setup.Id);

        screen.State.SetHeritageGroup(1); // back to a human: everything returns
        page.Update();
        Assert.True(page.ClothesButton.Visible);
        Assert.True(page.Spinner(CharGenAppearancePart.Nose)!.Visible);
        Assert.Equal(180, page.Spinner(CharGenAppearancePart.Skin)!.Box.Y0);
        Assert.Equal(Pregame("ID_CharGen_HairStyle"), page.Spinner(CharGenAppearancePart.Hair)!.Text);
        Assert.Equal(UiStateIds.Normal, page.Arrow(CharGenAppearancePart.Eyes, CharGenAppearancePage.LeftArrowId)!.State);
    }

    [RequiresLocalDatsFact]
    public void Entering_the_Gear_Knight_heritage_from_another_draws_the_appearance_and_clothing_at_random_again()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        screen.State.SetHeritageGroup(1);
        page.Update();
        var before = (screen.State.HairStyle, screen.State.EyesStrip, screen.State.NoseStrip, screen.State.MouthStrip, screen.State.ShirtStyle, screen.State.TrousersStyle);

        screen.State.SetHeritageGroup(6);
        page.Update();
        var gear = (screen.State.HairStyle, screen.State.EyesStrip, screen.State.NoseStrip, screen.State.MouthStrip, screen.State.ShirtStyle, screen.State.TrousersStyle);

        Assert.NotEqual(before, gear);
        var stay = gear;
        page.Update(); // already a Gear Knight: nothing is drawn again
        Assert.Equal(stay, (screen.State.HairStyle, screen.State.EyesStrip, screen.State.NoseStrip, screen.State.MouthStrip, screen.State.ShirtStyle, screen.State.TrousersStyle));
    }

    [RequiresLocalDatsFact]
    public void Random_draws_the_face_or_the_clothing_by_the_half_showing()
    {
        using var rig = Rig(seed: 11);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var state = screen.State;

        var face = (state.HairStyle, state.EyesStrip, state.NoseStrip, state.MouthStrip);
        var clothing = (state.ShirtStyle, state.TrousersStyle, state.FootwearStyle);
        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RandomButtonId));
        Assert.NotEqual(face, (state.HairStyle, state.EyesStrip, state.NoseStrip, state.MouthStrip));
        Assert.Equal(clothing, (state.ShirtStyle, state.TrousersStyle, state.FootwearStyle));

        ClickAndRun(rig, page.ClothesButton!);
        face = (state.HairStyle, state.EyesStrip, state.NoseStrip, state.MouthStrip);
        clothing = (state.ShirtStyle, state.TrousersStyle, state.FootwearStyle);
        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RandomButtonId));
        Assert.Equal(face, (state.HairStyle, state.EyesStrip, state.NoseStrip, state.MouthStrip));
        Assert.NotEqual(clothing, (state.ShirtStyle, state.TrousersStyle, state.FootwearStyle));
        Assert.Equal(screen.State.GetSetupId(), page.View!.Preview!.Setup.Id);
    }

    // ---------------------------------------------------------------- The character

    [RequiresLocalDatsFact]
    public void A_choice_changes_the_character_the_view_shows()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);
        var view = page.View!;

        var version = view.Version;
        var parts = view.Preview!.Parts.Select(p => p.VariantKey).ToArray();
        page.Step(CharGenAppearancePart.Hair, 1);
        Assert.NotEqual(version, view.Version);

        version = view.Version;
        page.SetColor(page.Choice(CharGenAppearancePart.Hair).NumColors > 1 ? (page.CurrentColor + 1) % page.Choice(CharGenAppearancePart.Hair).NumColors : 0);
        Assert.NotEqual(version, view.Version);

        // A skin shade moves the dressing of the skin's parts.
        ClickAndRun(rig, page.Spinner(CharGenAppearancePart.Skin)!);
        page.SetShade(0.0);
        var dark = view.Preview!.Parts.Select(p => p.VariantKey).ToArray();
        page.SetShade(1.0);
        var light = view.Preview!.Parts.Select(p => p.VariantKey).ToArray();
        Assert.NotEqual(dark, light);
        Assert.Equal(parts.Length, light.Length);
    }

    [RequiresLocalDatsFact]
    public void An_Olthoi_character_has_the_Olthoi_body_and_the_bigger_cameras()
    {
        using var rig = Rig();
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        screen.State.SetHeritageGroup(12);
        page.Update();
        ClickAndRun(rig, rig.Element<UiButton>(CharGenMainScreen.RightButtonId)); // the Olthoi skip the profession, skills and town pages
        rig.Run(0.5);

        Assert.Equal(CharGenPageId.Summary, screen.Progress);
        Assert.Equal(CharGenRules.BodyCamera(12), screen.Summary!.View!.CameraPosition);
        Assert.Equal(new Vector3(0, -3.8f, 1.15f), screen.Summary.View.CameraPosition);
        Assert.Equal(screen.State.GetSetupId(), screen.Summary.View.Preview!.Setup.Id);
    }

    [RequiresLocalDatsFact]
    public void Without_the_view_data_the_page_still_works_and_says_it_has_no_character_or_colours()
    {
        using var rig = Rig(view3D: false);
        var screen = Open(rig);
        var page = ToAppearance(rig, screen);

        Assert.Null(page.View);
        Assert.StartsWith("placeholder:", page.PlaceholderText, StringComparison.Ordinal);
        Assert.Null(page.Bullet(0)!.GeneratedImage);
        var start = screen.State.HairStyle;
        ClickAndRun(rig, page.Arrow(CharGenAppearancePart.Hair, CharGenAppearancePage.RightArrowId)!);
        Assert.NotEqual(start, screen.State.HairStyle);
        Assert.True(rig.Log.AnyLineContains("TODO(retailui-chargen-view)"));
    }
}
