using System.Collections.Immutable;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Shell;

/// <summary>
/// Character information, the options panel, the logout confirmation and the UI's busy count (docs/retail-ui.md section 21.8): the report from a scripted PlayerDescription, an honoured option
/// that takes effect and a ghosted one that cannot change, the confirmation (Yes logs off, No stays), and the busy count.
/// </summary>
public sealed class GameShellG6Tests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static PlayerDescriptionMsg Description(uint options1 = StandInPlayerStats.AceDefaultOptions1, uint options2 = PlayerOptionTable.DefaultWord2, int deaths = 1, int burden = 0) => new()
    {
        Options1 = options1,
        Options2 = options2,
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(0x62, 1_000_000_000).Add(0x7D, 93_784).Add(0x2B, deaths).Add(5, burden),
        Attributes =
        [
            new AttributeDesc(1, 0, 90, 0), new AttributeDesc(2, 0, 80, 0), new AttributeDesc(3, 0, 70, 0),
            new AttributeDesc(4, 0, 60, 0), new AttributeDesc(5, 0, 50, 0), new AttributeDesc(6, 0, 40, 0),
        ],
        Vitals = [new VitalDesc(1, 5, 0, 0, 52), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, 75)],
    };

    private (LoginRig Rig, GamePlayScreen Screen, IInboundMessageHandler Handler) EnterGame(bool describe = true, uint options1 = StandInPlayerStats.AceDefaultOptions1)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        if (describe)
        {
            handler!.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), Description(options1));
            rig.Frame();
        }

        return (rig, screen, handler!);
    }

    private static UiDialog OnlyDialog(LoginRig rig) => Assert.Single(rig.Manager.Dialogs.OpenDialogs);

    // ------------------------------------------------------------------ no dats

    [Fact]
    public void The_option_table_lists_retails_49_toggles_once_and_honours_two()
    {
        var all = PlayerOptionTable.All.ToList();
        Assert.Equal(49, all.Count);
        Assert.Equal(all.Count, all.Select(static o => o.Name).Distinct().Count());
        Assert.Equal(all.Count, all.Select(static o => (o.Word, o.Mask)).Distinct().Count());
        Assert.All(all, static o => Assert.Equal(1, System.Numerics.BitOperations.PopCount(o.Mask)));
        Assert.Equal(["VividTargetingIndicator", "SideBySideVitals"], PlayerOptionTable.Honoured.Select(static o => o.Name).ToArray());
        Assert.Equal(PlayerQualities.SideBySideVitalsOption, PlayerOptionTable.SideBySideVitals.Mask);
        Assert.True(PlayerOptionTable.VividTargetingIndicator.Default);
        Assert.False(PlayerOptionTable.SideBySideVitals.Default);
        Assert.True(PlayerOptionTable.All.First(static o => o.Name == "ShowCloak").Default);
    }

    [Fact]
    public void The_busy_count_is_a_counter_that_picks_the_busy_pointer_while_it_is_up()
    {
        var log = new RecordingLogSink();
        var busy = new UiBusyCount(log);
        Assert.False(busy.IsBusy);
        Assert.Equal(0, busy.CursorIndex);
        busy.Increment();
        busy.Increment();
        Assert.Equal(2, busy.Count);
        Assert.Equal(UiBusyCount.BusyCursor, busy.CursorIndex);
        busy.OverObject = true;
        Assert.Equal(UiBusyCount.BusyCursorOverObject, busy.CursorIndex);
        busy.Decrement();
        Assert.True(busy.IsBusy);
        busy.Decrement();
        Assert.False(busy.IsBusy);
        Assert.Equal(2, busy.CursorUpdates); // 0 to 1 and 1 to 0
        busy.Decrement(); // below zero: reported, not applied
        Assert.Equal(0, busy.Count);
        Assert.Contains(log.Entries, static e => e.Message.Contains("TODO(g6-busy-cursor-image)", StringComparison.Ordinal));
        Assert.Contains(log.Entries, static e => e.Message.Contains("already 0", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(200u, "None")]
    [InlineData(201u, "Poor")]
    [InlineData(320u, "Mediocre")]
    [InlineData(380u, "Hardy")]
    [InlineData(440u, "Resilient")]
    [InlineData(441u, "Indomitable")]
    public void The_resist_word_follows_retails_thresholds(uint sum, string word) =>
        Assert.Equal(word, GameCharacterInfo.Word(sum, 200, 260, 320, 380, 440));

    // ------------------------------------------------------------------ character information

    [RequiresLocalDatsFact]
    public void Character_information_is_a_text_report_of_the_description()
    {
        var (rig, screen, handler) = EnterGame();
        using var _ = rig;
        var host = screen.PanelHost!;
        var info = Assert.IsType<GameCharacterInfo>(screen.CharacterInfo);
        Assert.Equal(string.Empty, info.Report); // nothing before the page shows
        Assert.NotEqual(0u, host.ElementFor(3)!.VisibilityToggleAction);
        Assert.True(rig.Manager.DoVisibilityToggleAction(host.ElementFor(3)!.VisibilityToggleAction), "the key action of the character page reaches the host");
        rig.Frame();

        Assert.Equal(3u, host.CurrentPanel);
        Assert.True(host.ElementFor(3)!.Visible);
        var report = info.Report;
        Assert.Contains("You have died only once!", report, StringComparison.Ordinal);
        Assert.StartsWith("You were born on ", report, StringComparison.Ordinal); // the date is in local time
        Assert.Contains("You have played for 1d 2h 3m 4s.", report, StringComparison.Ordinal);
        Assert.Contains("Regeneration Bonus: Poor", report, StringComparison.Ordinal); // Strength 90 + 2 * Endurance 80 = 250
        Assert.Contains("Natural Resistances: None", report, StringComparison.Ordinal); // 90 + 80 = 170
        Assert.Contains("Innate Strength: 90", report, StringComparison.Ordinal);
        Assert.Contains("Innate Self: 40", report, StringComparison.Ordinal);
        Assert.Contains("Chess Rank: 1,400", report, StringComparison.Ordinal); // the default rank
        Assert.Contains("Fishing Skill: 0", report, StringComparison.Ordinal);
        Assert.Contains("You are not overburdened at this time.", report, StringComparison.Ordinal);
        Assert.DoesNotContain("string table error", report, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Lines, static l => l.Contains("TODO(g6-character-info-augmentations)", StringComparison.Ordinal));
        // A change while the page shows rebuilds the report.
        var before = info.Updates;
        handler.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), Description());
        Assert.Equal(before + 1, info.Updates);

        // A load over the capacity (150 * Strength 90 = 13,500) is reported with the penalty (UpdateLoad).
        handler.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), Description(deaths: 5, burden: 20_000));
        rig.Frame();
        Assert.Contains("You have died 5 times.", info.Report, StringComparison.Ordinal);
        Assert.Contains("overburdened by 6,500 burden units", info.Report, StringComparison.Ordinal);
        Assert.Contains("by 50%", info.Report, StringComparison.Ordinal); // LoadMod(1.48) = 0.52: 10 - 5 = 5 tenths of the skills
    }

    // ------------------------------------------------------------------ options

    [RequiresLocalDatsFact]
    public void The_options_panel_lists_every_option_and_ghosts_those_m2_does_not_honour()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        var panel = Assert.IsType<GameOptionsPanel>(screen.OptionsPanel);
        Assert.Equal(49, panel.Boxes.Count);
        foreach (var box in panel.Boxes)
        {
            Assert.Equal(box.Option!.Honoured, box.State != UiStateIds.Ghosted && !box.Ghosted);
            Assert.False(string.IsNullOrWhiteSpace(box.Text), box.Option.Name);
        }

        Assert.Equal("Side By Side Vitals", panel.BoxOf(PlayerOptionTable.SideBySideVitals)!.Text);
        Assert.False(panel.BoxOf(PlayerOptionTable.SideBySideVitals)!.Current); // ACE's default word
        Assert.True(panel.BoxOf(PlayerOptionTable.VividTargetingIndicator)!.Current);
        Assert.True(panel.Boxes.First(static b => b.Option!.Name == "ShowCloak").Current); // word 2 comes from the description too
        Assert.True(panel.Apply!.Ghosted); // nothing changed yet
        Assert.True(panel.Reset!.Ghosted);
    }

    [RequiresLocalDatsFact]
    public void An_honoured_option_takes_effect_on_Apply_and_swaps_the_vitals_window()
    {
        var (rig, screen, handler) = EnterGame();
        using var _ = rig;
        var panel = screen.OptionsPanel!;
        Assert.True(screen.StackedVitals!.Visible);
        Assert.False(screen.SideBySideVitals!.Visible);
        var options1Before = rig.Session.State.Self.Qualities.CharacterOptions1;

        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!)); // the toolbar's options button
        rig.Frame();
        Assert.True(Assert.IsType<UiPanel>(panel.Page).OpenTab(0x1000020E)); // the Character tab
        rig.Frame();
        var box = panel.BoxOf(PlayerOptionTable.SideBySideVitals)!;
        Assert.True(panel.ScrollTo(PlayerOptionTable.SideBySideVitals));
        rig.Frame();
        Assert.True(rig.Click(box));
        rig.Frame();
        Assert.True(box.Current);
        Assert.True(screen.StackedVitals.Visible); // a click changes the box, not the option: Apply does (retail's Apply and Reset)
        Assert.False(panel.Apply!.Ghosted);
        Assert.False(panel.Reset!.Ghosted);

        Assert.True(rig.Click(panel.Apply));
        rig.Frame();
        Assert.False(screen.StackedVitals.Visible);
        Assert.True(screen.SideBySideVitals.Visible);
        Assert.True(screen.Options!.SideBySideVitals);
        Assert.Equal(options1Before | PlayerQualities.SideBySideVitalsOption, rig.Session.State.Self.Qualities.CharacterOptions1);
        Assert.True(panel.Apply.Ghosted);

        // Reset undoes a pending change; Defaults restores the honoured options' defaults.
        Assert.True(rig.Click(box));
        Assert.False(box.Current);
        Assert.True(rig.Click(panel.Reset));
        Assert.True(box.Current);
        Assert.True(rig.Click(box));
        Assert.True(rig.Click(panel.Apply));
        rig.Frame();
        Assert.True(screen.StackedVitals.Visible);
        Assert.Equal(options1Before, rig.Session.State.Self.Qualities.CharacterOptions1);

        // A description that arrives later keeps the player's choice for the session.
        Assert.True(rig.Click(box));
        Assert.True(rig.Click(panel.Apply));
        handler.OnPlayerDescription(new InboundContext(0, MessageQueue.SmartBox, 0), new GameEventHeader(1, 1, 0x0013), Description());
        Assert.True(screen.Options!.SideBySideVitals);
    }

    [RequiresLocalDatsFact]
    public void A_ghosted_option_cannot_change_and_nothing_goes_to_the_server()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        var panel = screen.OptionsPanel!;
        var options = screen.Options!;
        var run = PlayerOptionTable.All.First(static o => o.Name == "ToggleRun");
        var before = (rig.Session.State.Self.Qualities.CharacterOptions1, rig.Session.State.Self.Qualities.CharacterOptions2);

        Assert.False(options.TrySet(run, !options.Get(run))); // by code
        var box = panel.BoxOf(run)!;
        var shown = box.Current;
        box.BroadcastElementMessage(UiMessageIds.ButtonClick); // by the page, as a click that reached a ghosted box would
        Assert.Equal(shown, box.Current);
        Assert.True(panel.Apply!.Ghosted);
        Assert.False(options.Get(run) != ((PlayerOptionTable.DefaultWord1 & run.Mask) != 0));
        Assert.Equal(before, (rig.Session.State.Self.Qualities.CharacterOptions1, rig.Session.State.Self.Qualities.CharacterOptions2));
        Assert.Equal(0, options.ChangeCount);
        Assert.Contains(rig.Log.Lines, static l => l.Contains("ToggleRun", StringComparison.Ordinal) && l.Contains("ghosted", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_vivid_indicator_option_flips_its_bit_in_the_sessions_word_only()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        var options = screen.Options!;
        Assert.True(options.VividTargetingIndicator);
        Assert.True(options.TrySet(PlayerOptionTable.VividTargetingIndicator, false));
        Assert.False(options.VividTargetingIndicator);
        Assert.Equal(StandInPlayerStats.AceDefaultOptions1 & ~0x8000u, rig.Session.State.Self.Qualities.CharacterOptions1);
        Assert.Equal(1, options.ChangeCount);
    }

    // ------------------------------------------------------------------ logout

    [RequiresLocalDatsFact]
    public void The_options_panels_exit_button_asks_first_and_No_stays()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!));
        rig.Frame();
        var exit = screen.Root.GetChildRecursive(0x10000203)!;
        Assert.True(rig.Click(exit));
        rig.Frame();

        var dialog = OnlyDialog(rig);
        Assert.IsType<UiConfirmationDialog>(dialog);
        Assert.Equal("This will exit your character from the game world.\n\nAre you sure?", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text.Replace("\r", string.Empty, StringComparison.Ordinal));
        Assert.True(screen.Logout!.IsOpen);
        Assert.False(screen.Logout.RequestEndCharacterSession()); // a second one is refused while it is open

        Assert.True(rig.Click(dialog.GetChildRecursive(UiConfirmationDialog.NoButton)!));
        rig.Frame();
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.False(screen.Logout.IsOpen);
        Assert.Equal(0, screen.Logout.Confirmed);
        Assert.Null(screen.Logout.Last);
        Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
    }

    [RequiresLocalDatsFact]
    public void Yes_logs_off()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!));
        rig.Frame();
        Assert.True(rig.Click(screen.Root.GetChildRecursive(0x10000203)!));
        rig.Frame();
        Assert.True(rig.Click(OnlyDialog(rig).GetChildRecursive(UiConfirmationDialog.YesButton)!));
        rig.Frame();

        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(1, screen.Logout!.Confirmed);
        Assert.NotNull(screen.Logout.Last);
        Assert.Single(rig.Log.Lines, static l => l.Contains("TODO(g6-logoff-midair)", StringComparison.Ordinal)); // the mid-air refusal is not built: a named TODO, logged once
    }

    [RequiresLocalDatsFact]
    public void Exit_Game_asks_the_logout_confirmation_and_No_stays()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!));
        rig.Frame();
        Assert.True(rig.Click(screen.Root.GetChildRecursive(0x10000617)!));
        rig.Frame();

        var dialog = OnlyDialog(rig);
        Assert.IsType<UiConfirmationDialog>(dialog);
        Assert.StartsWith("This will exit your character from the game world.", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text, StringComparison.Ordinal);
        Assert.Contains(rig.Log.Lines, static l => l.Contains("TODO(g6-exit-game-confirm)", StringComparison.Ordinal));

        Assert.True(rig.Click(dialog.GetChildRecursive(UiConfirmationDialog.NoButton)!));
        rig.Run(0.3);
        Assert.Empty(rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(0, screen.Logout!.ExitConfirmed);
        Assert.Equal(0, rig.Host.Exits);
        Assert.Equal(UiModes.GamePlay, rig.Flow.CurrentMode);
    }

    [RequiresLocalDatsFact]
    public void Exit_Game_Yes_queues_the_epilogue_which_closes_the_application()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!));
        rig.Frame();
        Assert.True(rig.Click(screen.Root.GetChildRecursive(0x10000617)!));
        rig.Frame();
        Assert.True(rig.Click(OnlyDialog(rig).GetChildRecursive(UiConfirmationDialog.YesButton)!));
        rig.Run(0.3);

        Assert.Equal(UiModes.Epilogue, rig.Flow.CurrentMode);
        Assert.Equal(1, rig.Host.Exits);
        Assert.Equal(1, screen.Logout!.ExitConfirmed);
        Assert.Equal(0, screen.Logout.Confirmed); // not the log off
    }

    [RequiresLocalDatsFact]
    public void The_unbuilt_buttons_are_loud_placeholders()
    {
        var (rig, screen, _) = EnterGame();
        using var _ = rig;
        Assert.True(rig.Click(screen.Root!.GetChildRecursive(0x1000019B)!));
        rig.Frame();

        // R6: the five buttons look live (the layout's ghosted art is its normal art), so they are not ghosted and a click says "not in M2" under the button's own label.
        foreach (var id in new uint[] { 0x10000204, 0x100005CC, 0x10000205, 0x10000206, 0x10000207 })
        {
            var button = Assert.IsAssignableFrom<UiButton>(screen.Root.GetChildRecursive(id));
            Assert.False(button.Ghosted);
            if (id == 0x10000204)
            {
                Assert.True(rig.Click(button), $"button 0x{id:X8}"); // a real click: this button fires an input action that the page hears
            }
            else
            {
                button.BroadcastElementMessage(UiMessageIds.ButtonClick); // the others: the click message (a second real click after a closed dialog does not reach the page in this rig: see the handoff)
            }

            rig.Frame();
            var dialog = OnlyDialog(rig);
            Assert.StartsWith(NotInM2.Prefix, Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text, StringComparison.Ordinal);
            Assert.True(rig.Click(Assert.IsAssignableFrom<UiElement>(dialog.GetChildRecursive(UiMessageDialog.OkButton))));
            rig.Run(0.3);
        }

        Assert.Contains(rig.Log.Lines, static l => l.Contains("TODO(g6-options-buttons)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_managers_busy_count_starts_at_zero()
    {
        var (rig, _, _) = EnterGame(describe: false);
        using var _ = rig;
        Assert.Equal(0, rig.Manager.Busy.Count);
    }
}
