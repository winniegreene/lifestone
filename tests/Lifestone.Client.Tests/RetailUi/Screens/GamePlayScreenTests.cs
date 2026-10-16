using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// A [Fact] for a larger window size than retail's native 800x600: skipped unless <c>LIFESTONE_ALL_SIZES=1</c> (the opt-in; owner rule 2026-10-14, several
/// resolutions are a polish step at the end of the project). Never deleted, not run by default.
/// </summary>
public sealed class AllSizesFactAttribute : FactAttribute
{
    public AllSizesFactAttribute()
    {
        Skip = LocalDatsSkip.Reason();
        if (Skip is null && Environment.GetEnvironmentVariable("LIFESTONE_ALL_SIZES") != "1")
        {
            Skip = "larger window sizes are opt-in: set LIFESTONE_ALL_SIZES=1 (the owner's rule, 2026-10-14)";
        }
    }
}

/// <summary>
/// The in-game shell (<see cref="GamePlayScreen"/>, docs/retail-ui.md section 21) over the user's dats: it releases the forced display, makes the
/// gameplay root at the full window, finds the windows <c>gmGamePlayUI::SetupChildren</c> finds, publishes the SmartBox's rectangle, names its
/// placeholders and returns to character management when the character list comes back.
/// </summary>
public sealed class GamePlayScreenTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static GamePlayScreen EnterGame(LoginRig rig)
    {
        rig.Run(2.5);
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.True(rig.Manager.IsDisplayForced);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        Assert.Equal(UiModes.GamePlay, rig.Flow.CurrentMode);
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    private static GamePlayScreen EnterStagedGame(LoginRig rig)
    {
        rig.Run(2.5);
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        Assert.Equal(UiModes.GamePlay, rig.Flow.CurrentMode);
        return Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    [RequiresLocalDatsFact]
    public void A_staged_shell_is_built_over_frames_and_holds_the_portal_notice_for_its_spew_box()
    {
        using var rig = new LoginRig(fixture, liveGame: true, stageShell: true);
        var screen = EnterStagedGame(rig);
        var portal = UiStringRef.FromLiteral("In Portal Space - Please Wait...");
        rig.Screens.Notices.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, portal); // posted before the spew box exists

        var frames = 1;
        while (!screen.IsBuilt && frames < 400)
        {
            rig.Frame();
            frames++;
        }

        Assert.True(screen.IsBuilt);
        // The build budget is a wall-clock one (BuildBudgetMilliseconds), so how many frames it takes depends on the machine's speed and load: it is staged when it took more than one frame,
        // not a fixed count (gameplay-staged-notice-flake: "> 3" failed on a fast moment).
        Assert.True(screen.BuildFrames >= 2, $"the shell was built in {screen.BuildFrames} frames");
        Assert.False(rig.Screens.Notices.ExpectWindows);
        Assert.Equal(0, rig.Screens.Notices.HeldCount);
        // The spew box turns a notice it was handed into a row in the next frame's Frame message (gmSpewBoxUI::Update). On a fast moment the wiring that registers the box finishes in the same
        // frame as the rest of the build, so the notice is still pending when IsBuilt turns true: one more frame, as the shell has in play, makes the check independent of the machine's speed
        // (gameplay-staged-notice-flake, the cause: a wall-clock budget decided whether that frame had already run).
        rig.Frame();
        rig.Frame();
        Assert.True(screen.Chat?.Spew?.Contains("In Portal Space - Please Wait..."), "the held notice reaches the spew box");
        Assert.True(screen.Root is { IsShown: true });
        Assert.NotNull(screen.SmartBox);
        Assert.NotNull(screen.Vitals);
        Assert.NotNull(screen.RadarUi);
        Assert.NotNull(screen.ToolsUi);
        Assert.NotNull(screen.Hotkeys);
        Assert.Empty(screen.Placeholders);
        Assert.Single(rig.Manager.Roots, r => r.IsShown);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("gmGamePlayUI: element", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void A_staged_shell_has_the_windows_of_an_unstaged_one()
    {
        using var staged = new LoginRig(fixture, liveGame: true, stageShell: true);
        var a = EnterStagedGame(staged);
        for (var i = 0; i < 400 && !a.IsBuilt; i++)
        {
            staged.Frame();
        }

        using var whole = new LoginRig(fixture, liveGame: true);
        var b = EnterGame(whole);
        Assert.True(a.IsBuilt && b.IsBuilt);
        Assert.Equal(b.Root!.ScreenBox, a.Root!.ScreenBox);
        Assert.Equal(b.SmartBox!.ElementId, a.SmartBox!.ElementId);
        Assert.Equal(b.FloatingChats.Count, a.FloatingChats.Count);
        Assert.Equal(b.Placeholders, a.Placeholders);
        Assert.Equal(CountElements(b.Root), CountElements(a.Root));
    }

    [RequiresLocalDatsFact]
    public void A_staged_shell_left_before_it_is_made_leaves_nothing_in_the_runtime()
    {
        using var rig = new LoginRig(fixture, liveGame: true, stageShell: true);
        var screen = EnterStagedGame(rig);
        Assert.False(screen.IsBuilt);
        rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        rig.Frame();
        rig.Frame();
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.False(rig.Screens.Notices.ExpectWindows);
        Assert.DoesNotContain(rig.Manager.Roots, r => r.ElementId == GamePlayScreen.RootElementId);
    }

    private static int CountElements(UiElement element)
    {
        var count = 1;
        foreach (var child in element.Children)
        {
            count += CountElements(child);
        }

        return count;
    }

    [RequiresLocalDatsFact]
    public void The_game_mode_releases_the_forced_display_and_shows_the_gameplay_root_at_the_full_window()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.False(rig.Manager.IsDisplayForced);
        Assert.False(LoginScreens.IsStandIn(screen, UiModes.GamePlay));
        var root = Assert.IsAssignableFrom<UiElement>(screen.Root);
        Assert.Equal(UiLayoutEnums.GameplayRoot.ElementId, root.ElementId);
        Assert.True(root.IsShown);
        Assert.Equal(UiBox.FromPositionAndSize(0, 0, 800, 600), root.ScreenBox);
        Assert.Single(rig.Manager.Roots, r => r.IsShown);
    }

    [RequiresLocalDatsFact]
    public void The_screen_finds_the_windows_SetupChildren_finds_by_id()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.Equal(UiLayoutEnums.Gameplay.SmartBox, Assert.IsAssignableFrom<UiElement>(screen.SmartBox).ElementId);
        Assert.Equal(UiLayoutEnums.Gameplay.Radar, Assert.IsAssignableFrom<UiElement>(screen.Radar).ElementId);
        Assert.Equal(UiLayoutEnums.Gameplay.StackedVitals, Assert.IsAssignableFrom<UiElement>(screen.StackedVitals).ElementId);
        Assert.Equal(UiLayoutEnums.Gameplay.SideBySideVitals, Assert.IsAssignableFrom<UiElement>(screen.SideBySideVitals).ElementId);
        Assert.Equal(UiLayoutEnums.Gameplay.FloatingChats, screen.FloatingChats.Select(c => c.ElementId));
        Assert.Equal(GamePlayScreen.MainChatId, Assert.IsAssignableFrom<UiElement>(screen.MainChat).ElementId);
        Assert.Equal(GamePlayScreen.ToolbarId, Assert.IsAssignableFrom<UiElement>(screen.Toolbar).ElementId);
        Assert.Equal(GamePlayScreen.PanelId, Assert.IsAssignableFrom<UiElement>(screen.Panel).ElementId);
        Assert.Equal(GamePlayScreen.SpewBoxId, Assert.IsAssignableFrom<UiElement>(screen.SpewBox).ElementId);
        Assert.Equal(GamePlayScreen.SmartBoxViewportId, Assert.IsType<UiViewport>(screen.SmartBoxViewport).ElementId);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("gmGamePlayUI: element", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_windows_sit_at_the_anchored_places_of_an_800_by_600_window()
    {
        // Retail's native size, the default of every test and check (owner rule 2026-10-14); docs/retail-ui-data.md section 3.2 anchors, 800 wide.
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.Equal(UiBox.FromPositionAndSize(0, 0, 800, 600), screen.SmartBox!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(0, 0, 800, 600), screen.SmartBoxViewport!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(680, 0, 120, 140), screen.Radar!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(0, 500, 410, 100), screen.MainChat!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(490, 500, 310, 100), screen.Toolbar!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(320, 0, 160, 58), screen.StackedVitals!.ScreenBox);
        Assert.Equal(screen.SmartBox.ScreenBox, screen.GameViewBox);
    }

    [RequiresLocalDatsFact]
    public void At_800_by_600_the_chat_and_the_toolbar_leave_a_gap_and_the_vitals_cover_the_first_rows_of_the_spew_box_as_the_layout_has_it()
    {
        // Review R18: the overlap of the stacked vitals (top centre) and the spew box (top centre, 450 wide) is the layout's own, as at any size; nothing else overlaps at 800 wide.
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        var spew = screen.SpewBox!.ScreenBox;
        var stacked = screen.StackedVitals!.ScreenBox;
        var chat = screen.MainChat!.ScreenBox;
        var toolbar = screen.Toolbar!.ScreenBox;
        var radar = screen.Radar!.ScreenBox;
        Assert.Equal(UiBox.FromPositionAndSize(175, 20, 450, 72), spew);
        Assert.True(stacked.X0 < spew.X1 && spew.X0 < stacked.X1 && stacked.Y0 < spew.Y1 && spew.Y0 < stacked.Y1, "the stacked vitals overlap the spew box");
        Assert.True(chat.X1 < toolbar.X0, $"the main chat {chat} and the toolbar {toolbar} do not touch");
        Assert.True(spew.X1 <= radar.X0 && spew.X0 >= 0, $"the spew box {spew} is clear of the radar {radar} and inside the window");
    }

    [AllSizesFact]
    public void The_windows_sit_at_the_anchored_places_of_a_1920_by_1080_window()
    {
        using var rig = new LoginRig(fixture, liveGame: true, window: 1920, windowHeight: 1080);
        var screen = EnterGame(rig);
        Assert.Equal(UiBox.FromPositionAndSize(0, 0, 1920, 1080), screen.SmartBox!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(0, 0, 1920, 1080), screen.SmartBoxViewport!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(1800, 0, 120, 140), screen.Radar!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(0, 980, 410, 100), screen.MainChat!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(1610, 980, 310, 100), screen.Toolbar!.ScreenBox);
        Assert.Equal(UiBox.FromPositionAndSize(880, 0, 160, 58), screen.StackedVitals!.ScreenBox);
        Assert.Equal(screen.SmartBox.ScreenBox, screen.GameViewBox);
    }

    [RequiresLocalDatsFact]
    public void The_smartbox_viewport_shows_and_the_stacked_vitals_stand_alone()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.True(screen.SmartBoxViewport!.IsShown);
        Assert.True(screen.StackedVitals!.IsShown);
        Assert.False(screen.SideBySideVitals!.IsShown);
        Assert.All(screen.FloatingChats, c => Assert.False(c.IsShown));
        Assert.True(screen.Radar!.IsShown && screen.MainChat!.IsShown && screen.Toolbar!.IsShown);
    }

    [RequiresLocalDatsFact]
    public void A_press_on_the_world_goes_to_the_world_and_a_press_on_a_window_does_not()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        EnterGame(rig);
        var world = new RecordingWorldPointer();
        rig.Manager.Input.WorldPointer = world;
        foreach (var (x, y) in new[] { (300, 250), (400, 300) })
        {
            rig.Manager.Input.MouseMove(x, y);
            rig.Frame();
            Assert.False(rig.Manager.Input.MouseButton(UiMouseButton.Left, true));
            rig.Manager.Input.MouseButton(UiMouseButton.Left, false);
        }

        Assert.Contains("Left down at 300,250", world.Events);
        Assert.Contains("Left down at 400,300", world.Events);
        world.Events.Clear();
        rig.Manager.Input.MouseMove(580, 520); // the toolbar
        rig.Frame();
        rig.Manager.Input.MouseButton(UiMouseButton.Left, true);
        rig.Manager.Input.MouseButton(UiMouseButton.Left, false);
        Assert.Empty(world.Events);
    }

    [RequiresLocalDatsFact]
    public void The_placeholder_note_names_the_windows_without_content_and_a_built_window_leaves_it()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.IsAssignableFrom<IUiPlaceholderScreen>(screen);
        Assert.DoesNotContain("spew box", screen.Placeholders); // g2 built the spew box and the chat
        Assert.DoesNotContain("chat", screen.Placeholders);
        Assert.DoesNotContain("radar", screen.Placeholders); // g3b built the radar
        foreach (var window in screen.Placeholders)
        {
            Assert.Contains(window, screen.PlaceholderNote, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("placeholder: the in-game shell", StringComparison.Ordinal)); // nothing is left to name (g5b built the last window)
        var changed = 0;
        screen.PlaceholderNoteChanged += () => changed++;
        Assert.DoesNotContain("toolbar", screen.Placeholders); // g4 built the toolbar, the panel host and the link indicator
        Assert.DoesNotContain("panels", screen.Placeholders);
        Assert.DoesNotContain("indicators", screen.Placeholders);
        Assert.DoesNotContain("selection", screen.Placeholders); // g5b built the selection display and the vivid target indicator: nothing is left
        Assert.Null(screen.PlaceholderNote);
        screen.MarkWindowBuilt("selection");
        Assert.Equal(0, changed); // a window already built changes nothing
        Assert.Null(screen.PlaceholderNote);
        foreach (var window in GamePlayScreen.InitialPlaceholders)
        {
            screen.MarkWindowBuilt(window);
        }

        Assert.Null(screen.PlaceholderNote);
    }

    [RequiresLocalDatsFact]
    public void The_shell_is_not_painted_until_the_host_says_so()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        var screen = EnterGame(rig);
        Assert.False(screen.ShellPainted);
        screen.MarkPainted(3, 50.0, timedOut: false);
        Assert.True(screen.ShellPainted);
        Assert.Equal(3, screen.PaintFrames);
        Assert.False(screen.PaintTimedOut);
    }

    [RequiresLocalDatsFact]
    public void The_character_list_coming_back_returns_to_character_management_and_forces_the_display_again()
    {
        using var rig = new LoginRig(fixture, liveGame: true);
        EnterGame(rig);

        // A logoff ends with the server's F658; the offline session's reconnect feeds the same list through the same reducer.
        rig.Session.Reconnect();
        rig.Run(2.5);
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.True(rig.Manager.IsDisplayForced);
        Assert.DoesNotContain(rig.Manager.Roots, r => r.ElementId == UiLayoutEnums.GameplayRoot.ElementId);
    }

    [RequiresLocalDatsFact]
    public void Without_a_live_session_the_game_mode_is_the_same_shell()
    {
        using var rig = new LoginRig(fixture);
        var screen = EnterGame(rig);
        Assert.False(rig.Manager.IsDisplayForced);
        Assert.NotNull(screen.SmartBoxViewport);
    }
}
