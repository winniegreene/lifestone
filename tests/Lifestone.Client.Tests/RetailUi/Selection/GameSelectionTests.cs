using System.Numerics;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Selection;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.RetailUi.Selection;

/// <summary>
/// The shell's selection over the real layout (docs/retail-ui.md section 21.7): the toolbar's name and health meter follow the selection (select, QueryHealth, the answer as a fraction, deselect
/// clears), and the vivid target indicator is drawn around the projected box, as an arrow outside the view, and hidden by the portal cue, the character option and the player itself.
/// </summary>
public sealed class GameSelectionTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Guard = 0x80000A01; // attackable (0x10): a creature, health is asked for
    private const uint Statue = 0x80000A02; // not attackable: name only
    private const uint Portal = 0x80000A03;

    private static StandInVisit Visit(uint options = StandInPlayerStats.AceDefaultOptions1) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = new StandInPlayerStats { Options1 = options },
        Objects =
        [
            new StandInObject(Guard, "Stand-in Guard", new Vector3(3f, 0f, 0f), DescriptionFlags: 0x10),
            new StandInObject(Statue, "Stand-in Statue", new Vector3(0f, 4f, 0f), MotionTable: 0),
            new StandInObject(Portal, "Stand-in Portal", new Vector3(-3f, 0f, 0f), MotionTable: 0, PortalTo: new StandInTeleport(0, 0x860201AD, new Vector3(12f, -28f, 0f))),
        ],
    };

    private sealed class Projector : ISelectionProjector
    {
        public bool Found = true;
        public ScreenRect Rect = new(400, 250, 120, 90);
        public bool OnScreen = true;
        public int Calls;

        public bool TryProject(uint guid, out ScreenRect rect, out bool onScreen)
        {
            Calls++;
            rect = Rect;
            onScreen = OnScreen;
            return Found;
        }
    }

    private sealed class Held(uint value)
    {
        public uint Value = value;
    }

    private (LoginRig Rig, GamePlayScreen Screen, GameSelection Selection, Held Selected, Projector Projector) Enter(StandInVisit? visit = null)
    {
        visit ??= Visit();
        var rig = new LoginRig(
            fixture,
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit, TimeSpan.FromSeconds(0.1));
            },
            startMode: UiModes.GamePlay,
            liveGame: true);
        rig.Run(3.0);
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var selection = Assert.IsType<GameSelection>(screen.SelectionUi);
        var held = new Held(0);
        var projector = new Projector();
        selection.SelectedProvider = () => held.Value;
        selection.ProjectorProvider = () => projector;
        return (rig, screen, selection, held, projector);
    }

    [RequiresLocalDatsFact]
    public void The_selection_is_off_the_placeholder_note_and_its_display_is_the_empty_panel_with_nothing_selected()
    {
        var (rig, screen, selection, _, _) = Enter();
        using var _ = rig;
        Assert.DoesNotContain("selection", screen.Placeholders);
        Assert.Null(screen.PlaceholderNote);
        Assert.Equal(SessionPhase.InWorld, rig.Session.State.Phase);
        Assert.False(selection.FieldShown); // no selection is showing ...
        Assert.True(selection.FieldVisible); // ... but the field is drawn, in its base state: retail's blank target panel (P2; HandleSelectionChanged calls the field's SetState with 0)
        Assert.Equal(0u, selection.FieldState);
        Assert.False(selection.HealthShown);
        Assert.Equal(string.Empty, selection.NameText);
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);
        Assert.False(selection.Indicator.OnScreenElement!.Visible);
        Assert.False(selection.Indicator.OffScreenElement!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_empty_panel_is_the_fields_base_image_and_a_selection_moves_it_to_state_0x1000000B()
    {
        var (rig, screen, selection, held, _) = Enter();
        using var _ = rig;
        var field = screen.Toolbar!.GetChildRecursive(GameSelection.FieldId)!;
        Assert.True(field.Visible);
        Assert.Equal(0u, field.State);
        Assert.Null(field.CurrentStateDesc); // the base state
        Assert.Contains(field.Desc.BaseState.Media, m => m is Lifestone.Data.Ui.UiImageMedia { File: 0x06001126 });
        foreach (var id in GameSelection.StackIds)
        {
            Assert.False(screen.Toolbar!.GetChildRecursive(id)!.Visible); // TODO(p2-stack-initial)
        }

        held.Value = Statue;
        rig.Frame();
        Assert.Equal(GameSelection.SelectedStateId, field.State);
        Assert.True(selection.FieldShown);
        held.Value = 0;
        rig.Frame();
        Assert.Equal(0u, field.State);
        Assert.True(field.Visible);
    }

    [RequiresLocalDatsFact]
    public void Selecting_an_attackable_object_shows_its_name_asks_for_its_health_and_shows_the_answer_as_a_fraction()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Frame();
        Assert.True(selection.FieldShown);
        Assert.Equal("Stand-in Guard", selection.NameText);
        Assert.Equal(1, selection.Queries);
        Assert.NotNull(selection.LastQuery);
        Assert.False(selection.HealthShown); // the answer has not come yet

        rig.Run(0.5);
        Assert.True(selection.LastQuery!.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, selection.LastQuery.Outcome!.Value.Kind);
        Assert.True(selection.HealthShown);
        Assert.Equal(StandInWorld_PlaceholderHealth, selection.HealthFraction!.Value, 3);
        Assert.Equal(StandInWorld_PlaceholderHealth, rig.Session.State.Objects.TryGet(Guard, out var guard) ? guard.Health!.Value : -1f, 3);
    }

    // StandInWorld.PlaceholderHealth is internal to the client assembly; the stand-in answers 0.6.
    private const float StandInWorld_PlaceholderHealth = 0.6f;

    [RequiresLocalDatsFact]
    public void An_object_that_is_not_attackable_shows_its_name_and_no_health_is_asked_for()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Statue;
        rig.Run(0.5);
        Assert.True(selection.FieldShown);
        Assert.Equal("Stand-in Statue", selection.NameText);
        Assert.Equal(0, selection.Queries);
        Assert.False(selection.HealthShown);
    }

    [RequiresLocalDatsFact]
    public void Deselecting_clears_the_name_and_hides_the_field_and_the_meter()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.5);
        Assert.True(selection.HealthShown);

        held.Value = 0;
        rig.Frame();
        Assert.False(selection.FieldShown);
        Assert.True(selection.FieldVisible); // the empty panel stays
        Assert.False(selection.HealthShown);
        Assert.Equal(string.Empty, selection.NameText);
        Assert.Equal(0u, selection.Selected);
    }

    [RequiresLocalDatsFact]
    public void Changing_the_selection_hides_the_old_health_until_the_new_object_answers()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.5);
        Assert.True(selection.HealthShown);

        held.Value = Statue;
        rig.Run(0.5);
        Assert.Equal("Stand-in Statue", selection.NameText);
        Assert.False(selection.HealthShown);
    }

    [RequiresLocalDatsFact]
    public void A_health_answer_for_an_object_that_is_not_selected_is_ignored()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Statue;
        rig.Frame();
        rig.Session.Actions.QueryHealth(Guard, ActionSource.Ui);
        rig.Run(0.5);
        Assert.False(selection.HealthShown);
    }

    [RequiresLocalDatsFact]
    public void An_unknown_object_shows_nothing_and_is_logged()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = 0x80009999;
        rig.Frame();
        Assert.False(selection.FieldShown);
        Assert.Contains(rig.Log.Lines, l => l.Contains("TODO(g5b-unknown)", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the vivid target indicator

    [RequiresLocalDatsFact]
    public void The_indicator_draws_four_tinted_corners_around_the_projected_box()
    {
        var (rig, screen, selection, held, projector) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.3);
        var indicator = selection.Indicator;
        Assert.True(indicator.OptionOn);
        Assert.Equal(VividTargetState.OnScreen, indicator.State);
        Assert.True(indicator.OnScreenElement!.Visible);
        Assert.False(indicator.OffScreenElement!.Visible);

        // The box 400,250 120x90 grown by a 12 pixel corner on each side.
        var corner = indicator.Corner(0)!.ScreenBox;
        var expected = VividTargetMath.OnScreenFrame(projector.Rect, corner.Width, corner.Height, 800, 600);
        Assert.Equal(expected, indicator.FrameBox);
        var box = indicator.OnScreenElement.ScreenBox;
        Assert.Equal((expected.X, expected.Y, expected.Width, expected.Height), (box.X0, box.Y0, box.Width, box.Height));

        // The corners follow the frame's corners (they are anchored by the layout).
        Assert.Equal(box.X0, indicator.Corner(0)!.ScreenBox.X0);
        Assert.Equal(box.Y0, indicator.Corner(0)!.ScreenBox.Y0);
        Assert.Equal(box.X1, indicator.Corner(3)!.ScreenBox.X1);
        Assert.Equal(box.Y1, indicator.Corner(3)!.ScreenBox.Y1);

        // The pictures are the radar colour: the guard is an attackable creature, gold.
        Assert.Equal(new UiRgbaColor(1f, 0.67f, 0f, 1f), indicator.Color);
        Assert.NotNull(screen.Root);
    }

    [RequiresLocalDatsFact]
    public void The_indicator_follows_the_box_as_it_moves()
    {
        var (rig, _, selection, held, projector) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.3);
        var first = selection.Indicator.FrameBox;
        projector.Rect = new ScreenRect(150, 100, 150, 160);
        rig.Frame();
        Assert.NotEqual(first, selection.Indicator.FrameBox);
        Assert.Equal(VividTargetMath.OnScreenFrame(projector.Rect, 12, 12, 800, 600), selection.Indicator.FrameBox);
    }

    [RequiresLocalDatsFact]
    public void An_object_outside_the_view_gets_the_arrow_for_its_heading_on_the_border()
    {
        var (rig, _, selection, held, projector) = Enter();
        using var _ = rig;
        held.Value = Guard;
        projector.OnScreen = false;
        projector.Rect = new ScreenRect(1000, 240, 100, 120); // to the right of the view: heading 90
        rig.Run(0.3);
        var indicator = selection.Indicator;
        Assert.Equal(VividTargetState.OffScreen, indicator.State);
        Assert.True(indicator.OffScreenElement!.Visible);
        Assert.False(indicator.OnScreenElement!.Visible);
        Assert.Equal(9, indicator.ArrowImage);
        Assert.InRange(indicator.Heading, 85.0, 95.0);
        var arrow = indicator.OffScreenElement.ScreenBox;
        Assert.Equal(800 - 8, arrow.X1 + 1);

        // Behind the camera (an empty rectangle): the arrow points back, at the bottom.
        projector.Rect = default;
        rig.Frame();
        Assert.Equal(11, indicator.ArrowImage);
        Assert.Equal(600 - 8, indicator.OffScreenElement.ScreenBox.Y1 + 1);

        // Back in the view: the corners again, the arrow hidden.
        projector.OnScreen = true;
        projector.Rect = new ScreenRect(400, 250, 120, 90);
        rig.Frame();
        Assert.Equal(VividTargetState.OnScreen, indicator.State);
        Assert.False(indicator.OffScreenElement.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_portal_cue_hides_the_indicator_and_the_next_cue_shows_it_again()
    {
        var (rig, _, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.3);
        Assert.Equal(VividTargetState.OnScreen, selection.Indicator.State);

        selection.Indicator.Enabled = false; // PortalCues.SelectionIndicatorOff (VividTargetIndicator::SetEnabled)
        rig.Frame();
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);
        Assert.False(selection.Indicator.OnScreenElement!.Visible);
        Assert.True(selection.FieldShown); // the toolbar's display stays: only the indicator is portal space's

        selection.Indicator.Enabled = true;
        rig.Frame();
        Assert.Equal(VividTargetState.OnScreen, selection.Indicator.State);
    }

    [RequiresLocalDatsFact]
    public void The_indicator_follows_the_characters_option()
    {
        var (rig, _, selection, held, _) = Enter(Visit(StandInPlayerStats.AceDefaultOptions1 & ~PlayerQualities.VividTargetingIndicatorOption));
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.3);
        Assert.False(rig.Session.State.Self.Qualities.VividTargetingIndicator);
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);
        Assert.True(selection.FieldShown); // the name still shows
    }

    [RequiresLocalDatsFact]
    public void Toggling_the_vivid_option_in_the_session_options_hides_and_shows_the_indicator_live()
    {
        var (rig, screen, selection, held, _) = Enter();
        using var _ = rig;
        held.Value = Guard;
        rig.Run(0.3);
        Assert.Equal(VividTargetState.OnScreen, selection.Indicator.State);

        Assert.True(screen.Options!.TrySet(PlayerOptionTable.VividTargetingIndicator, false)); // the options panel's write (g6)
        rig.Frame();
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);
        Assert.True(selection.FieldShown); // the name still shows

        Assert.True(screen.Options.TrySet(PlayerOptionTable.VividTargetingIndicator, true));
        rig.Frame();
        Assert.Equal(VividTargetState.OnScreen, selection.Indicator.State);
    }

    [RequiresLocalDatsFact]
    public void The_indicator_is_hidden_for_an_object_the_projector_does_not_know_and_for_no_projector()
    {
        var (rig, _, selection, held, projector) = Enter();
        using var _ = rig;
        held.Value = Guard;
        projector.Found = false;
        rig.Run(0.3);
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);

        projector.Found = true;
        rig.Frame();
        Assert.Equal(VividTargetState.OnScreen, selection.Indicator.State);

        selection.ProjectorProvider = () => null;
        rig.Frame();
        Assert.Equal(VividTargetState.Hidden, selection.Indicator.State);
    }

    [RequiresLocalDatsFact]
    public void The_source_images_are_the_thirteen_of_the_enum_group_and_the_corners_are_twelve_pixels()
    {
        var maps = fixture.Dats.EnumMaps;
        var loader = new Lifestone.Data.Textures.TextureLoader(fixture.Dats);
        var images = new Lifestone.Client.RetailUi.Rendering.DatUiImageSource(loader, new RecordingLogSink());
        for (var index = 1u; index <= 12u; index++)
        {
            Assert.True(maps.TryResolve(new EnumGroup(VividTargetMath.ImageGroup), index, out var did), $"index {index}");
            Assert.Equal(UiImageLookup.Ready, images.TryGetImage(did, out var image));
            Assert.True(image!.Width > 0 && image.Height > 0);
            if (index <= 4)
            {
                Assert.Equal((12, 12), (image.Width, image.Height));
            }
        }
    }

    [Fact]
    public void Health_is_asked_for_only_attackable_objects_and_the_indicator_never_for_the_player_or_what_it_holds()
    {
        var attackable = Made(0x80000001, 0x10, containerId: null);
        var plain = Made(0x80000002, 0, containerId: null);
        var held = Made(0x80000003, 0x10, containerId: 0x50000001);
        var worn = Made(0x80000004, 0, containerId: null, wielderId: 0x50000001);
        Assert.True(SelectionRules.QueriesHealth(attackable));
        Assert.False(SelectionRules.QueriesHealth(plain));
        Assert.True(SelectionRules.ShowsIndicator(attackable, 0x50000001));
        Assert.True(SelectionRules.ShowsIndicator(plain, 0x50000001));
        Assert.False(SelectionRules.ShowsIndicator(held, 0x50000001));
        Assert.False(SelectionRules.ShowsIndicator(worn, 0x50000001));
        Assert.False(SelectionRules.ShowsIndicator(Made(0x50000001, 0x8, containerId: null), 0x50000001));
    }

    private static WorldObject Made(uint guid, uint flags, uint? containerId, uint? wielderId = null)
    {
        var weenie = new Lifestone.Net.Types.WeenieDesc
        {
            Header = Lifestone.Net.Types.WeenieHeaderFlags.None,
            Name = "Thing",
            WeenieClassId = 2,
            IconId = 0x06000001,
            ItemType = 0x10,
            ObjectDescriptionFlags = flags,
            ContainerId = containerId,
            WielderId = wielderId,
        };
        var physics = new Lifestone.Net.Types.PhysicsDesc { Flags = Lifestone.Net.Types.PhysicsDescFlags.None, State = Lifestone.Net.Types.PhysicsState.None, Timestamps = new Lifestone.Net.Types.PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new Lifestone.Net.Messages.CreateObjectMsg(guid, Lifestone.Net.Types.ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0);
    }
}
