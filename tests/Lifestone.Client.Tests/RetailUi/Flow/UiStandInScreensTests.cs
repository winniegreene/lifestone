using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Flow;

/// <summary>
/// The stand-in screens of <c>--retail-ui=standin</c> (m2-plan G2 U2, until U3-U5 register the real screens): each login mode shows its
/// layout root in the forced, centred 800x600 display; the game screen releases the display; the intro, credits and epilogue make no
/// screen and say so.
/// </summary>
public sealed class UiStandInScreensTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private (UiElementManager Manager, RecordingLogSink Log) Manager()
    {
        var log = new RecordingLogSink(LogSeverity.Warning);
        var manager = new UiElementManager(new UiServices { Descs = UiDescSource.From(new UiLayouts(fixture.Dats)), Log = log }, UiScalePolicy.Choose(1920, 1080));
        return (manager, log);
    }

    [RequiresLocalDatsTheory]
    [InlineData(UiModes.DataPatch)]
    [InlineData(UiModes.Disconnected)]
    [InlineData(UiModes.CharacterManagement)]
    [InlineData(UiModes.CharGen)]
    public void A_login_mode_shows_its_root_in_the_forced_centred_display_and_leaves_nothing_behind(uint mode)
    {
        var (manager, log) = Manager();

        var screen = UiStandInScreens.Create(manager, mode);

        Assert.NotNull(screen);
        var root = Assert.Single(manager.Roots);
        Assert.True(manager.IsDisplayForced);
        Assert.Equal(new UiBox(560, 240, 1359, 839), root.ScreenBox);
        Assert.Contains(log.Lines, l => l.Contains("stand-in", StringComparison.Ordinal) && l.Contains("placeholder", StringComparison.Ordinal));

        screen.Show(false);
        Assert.False(root.IsShown);
        screen.Show(true);
        Assert.True(root.IsShown);
        screen.Dispose();
        Assert.Empty(manager.Roots);
    }

    [RequiresLocalDatsTheory]
    [InlineData(UiModes.Intro)]
    [InlineData(UiModes.Credits)]
    [InlineData(UiModes.Epilogue)]
    public void A_mode_without_a_stand_in_makes_no_screen_and_logs_a_todo_once(uint mode)
    {
        var (manager, log) = Manager();

        Assert.Null(UiStandInScreens.Create(manager, mode));
        Assert.Null(UiStandInScreens.Create(manager, mode));

        Assert.Empty(manager.Roots);
        Assert.Single(log.Lines, l => l.Contains("has no stand-in screen", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_flow_starts_the_queued_mode_with_its_stand_in_at_the_next_frame()
    {
        var (manager, _) = Manager();
        var flow = new GmUiFlow(manager, mode => UiStandInScreens.Create(manager, mode), new UiCursors(fixture.Dats).Default);
        flow.Flow.QueueUIMode(UiModes.CharacterManagement);

        manager.Update(0.0);

        Assert.Equal(UiModes.CharacterManagement, flow.Flow.CurrentMode);
        Assert.Single(manager.Roots);
        flow.Dispose();
    }
}
