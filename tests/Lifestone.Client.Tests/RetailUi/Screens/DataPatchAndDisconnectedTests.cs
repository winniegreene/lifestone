using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Tests;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.RetailUi.Screens;

/// <summary>
/// The data check and the disconnected screen (m2-plan G2 U3-U4) over the user's dats with the retail fonts and strings: the flow starts
/// on the data check, follows the session's data check and goes on to character management with the intro skipped (D38); a lost
/// connection or a session-ending character error shows the disconnected screen with its reason, whose button reconnects (D43) or, with
/// retail's flag, exits.
/// </summary>
public sealed class DataPatchAndDisconnectedTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void The_flow_starts_on_the_data_check_and_follows_the_session_to_character_management()
    {
        using var rig = new LoginRig(fixture);

        rig.Frame();
        var screen = rig.Screen<DataPatchScreen>();
        Assert.Equal(UiModes.DataPatch, rig.Flow.CurrentMode);
        Assert.True(rig.Manager.IsDisplayForced);

        // Connecting: the stand-in's handshake is done and F7E5 was answered, so the connection is up and the data check is on.
        Assert.Equal(1f, screen.ConnectLevel);
        Assert.Equal(0f, screen.PatchLevel);
        Assert.Equal("Looking for data to patch...", screen.StatusText);
        Assert.Equal(DataPatchScreen.ConnectedState, rig.Element<UiTextElement>(DataPatchScreen.ConnectTextId).State);
        Assert.Equal(1f, rig.Element<UiMeter>(DataPatchScreen.ConnectMeterId).Position);
        Assert.Equal(0f, rig.Element<UiMeter>(DataPatchScreen.PatchMeterId).Position);

        rig.Run(1.5);
        Assert.Equal(UiModes.DataPatch, rig.Flow.CurrentMode);
        Assert.Equal(0f, screen.PatchLevel);

        // The data check completes at 2 s: the patch meter fills, "Patching Done!" shows, and the next frame starts character management.
        while (screen.PatchLevel < 1f)
        {
            rig.Frame();
        }

        Assert.Equal(1f, screen.PatchLevel);
        Assert.Equal("Patching Done!", screen.StatusText);
        Assert.Equal(1f, rig.Element<UiMeter>(DataPatchScreen.PatchMeterId).Position);
        rig.Frame();
        rig.Frame();
        Assert.Equal(UiModes.CharacterManagement, rig.Flow.CurrentMode);
        Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Contains("Intro", StringComparison.OrdinalIgnoreCase));
    }

    [RequiresLocalDatsFact]
    public void The_connect_label_takes_the_state_of_the_level_as_retail_sets_it()
    {
        using var rig = new LoginRig(fixture, StandInSessions.DataCheckInProgress);
        rig.Frame();
        var screen = rig.Screen<DataPatchScreen>();
        var label = rig.Element<UiTextElement>(DataPatchScreen.ConnectTextId);
        var meter = rig.Element<UiMeter>(DataPatchScreen.ConnectMeterId);

        screen.SetConnectLevel(0f);
        Assert.Equal(UiStateIds.Normal, label.State);
        Assert.Equal(0f, meter.Position);
        screen.SetConnectLevel(0.5f);
        Assert.Equal(DataPatchScreen.ConnectingState, label.State);
        Assert.Equal(0.5f, meter.Position);
        screen.SetConnectLevel(1f);
        Assert.Equal(DataPatchScreen.ConnectedState, label.State);
        Assert.Equal(1f, meter.Position);
    }

    [RequiresLocalDatsFact]
    public void A_data_check_that_never_completes_stays_on_the_data_check()
    {
        using var rig = new LoginRig(fixture, StandInSessions.DataCheckInProgress);

        rig.Run(5);

        Assert.Equal(UiModes.DataPatch, rig.Flow.CurrentMode);
        Assert.Equal(0f, rig.Screen<DataPatchScreen>().PatchLevel);
    }

    [RequiresLocalDatsFact]
    public void The_patch_events_show_the_queue_the_download_and_its_progress_as_retail_words_them()
    {
        using var rig = new LoginRig(fixture, StandInSessions.DataCheckInProgress);
        rig.Frame();
        var screen = rig.Screen<DataPatchScreen>();

        screen.OnDataEvent(DataPatchEvent.Waiting, 7);
        Assert.Equal("Too many people patching. We're #7 in line...", screen.StatusText);

        screen.OnDataEvent(DataPatchEvent.Patching, 4 * 1024 * 1024);
        Assert.Equal("Patching data...", screen.StatusText);
        screen.OnDataEvent(DataPatchEvent.Waiting, 9); // no longer shown once a download is expected
        Assert.Equal("Patching data...", screen.StatusText);

        screen.OnDataEvent(DataPatchEvent.Progress, 1024 * 1024);
        Assert.Equal(0.25f, screen.PatchLevel);
        Assert.Equal("25% of 4,096K complete...", screen.StatusText);
        screen.OnDataEvent(DataPatchEvent.Progress, 4 * 1024 * 1024);
        Assert.Equal(1f, screen.PatchLevel);

        screen.OnDataEvent(DataPatchEvent.Done);
        Assert.Equal("Patching Done!", screen.StatusText);
    }

    [RequiresLocalDatsFact]
    public void The_quit_button_queues_the_epilogue_which_closes_the_application()
    {
        using var rig = new LoginRig(fixture, StandInSessions.DataCheckInProgress);
        rig.Frame();

        Assert.True(rig.Click(rig.Element<UiButton>(DataPatchScreen.QuitButtonId)));
        rig.Frame();
        rig.Frame();

        Assert.Equal(UiModes.Epilogue, rig.Flow.CurrentMode);
        Assert.IsType<EpilogueScreen>(rig.Flow.CurrentScreen);
        Assert.Equal(1, rig.Host.Exits);
        rig.Run(0.5);
        Assert.Equal(1, rig.Host.Exits);
    }

    [RequiresLocalDatsFact]
    public void A_data_mismatch_is_a_loud_todo_and_the_failed_login_shows_the_disconnected_screen()
    {
        using var rig = new LoginRig(fixture, h => StandInSessions.Disconnected(h, new SessionFailure(SessionFailureKind.DataCheckMismatch, SessionPhase.DataCheck, "stand-in: the dats do not match")));

        Assert.True(rig.RunUntilMode(UiModes.Disconnected));
        var screen = rig.Screen<DisconnectedScreen>();

        Assert.Equal("The connection to the server has been lost!", screen.ReasonText);
        Assert.Contains(rig.Log.Lines, l => l.Contains("the dats do not match", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void A_lost_connection_shows_the_disconnected_screen_with_the_reason()
    {
        using var rig = new LoginRig(fixture);
        rig.ToCharacterManagement();

        StandInSessions.Disconnected(rig.Session.Reducer, StandInSessions.ServerStopped);
        Assert.True(rig.RunUntilMode(UiModes.Disconnected));

        Assert.Equal("The connection to the server has been lost!", rig.Screen<DisconnectedScreen>().ReasonText);
        Assert.True(rig.Manager.IsDisplayForced);
        Assert.Contains(rig.Log.Lines, l => l.Contains("the session ended in failure", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_disconnected_button_reconnects_and_the_data_check_starts_again()
    {
        using var rig = new LoginRig(fixture);
        rig.ToCharacterManagement();
        StandInSessions.Disconnected(rig.Session.Reducer, StandInSessions.ServerStopped);
        Assert.True(rig.RunUntilMode(UiModes.Disconnected));

        Assert.True(rig.Click(rig.Element<UiButton>(DisconnectedScreen.ButtonId)));
        rig.Frame();
        rig.Frame();

        Assert.Equal(1, rig.Host.Reconnects);
        Assert.Equal(0, rig.Host.Exits);
        Assert.Equal(UiModes.DataPatch, rig.Flow.CurrentMode);
        Assert.Equal(1, rig.Session.Reconnects);
        Assert.True(rig.RunUntilMode(UiModes.CharacterManagement));
        Assert.Equal(3, rig.Characters.Entries.Count);
    }

    [RequiresLocalDatsFact]
    public void The_button_says_what_it_does_and_keeps_saying_it_through_its_states()
    {
        using var reconnecting = new LoginRig(fixture, StandInSessions.DataCheckInProgress, startMode: UiModes.Disconnected);
        using var exiting = new LoginRig(fixture, StandInSessions.DataCheckInProgress, retailDisconnect: true, startMode: UiModes.Disconnected);
        Assert.True(reconnecting.RunUntilMode(UiModes.Disconnected));
        Assert.True(exiting.RunUntilMode(UiModes.Disconnected));

        var button = reconnecting.Element<UiButton>(DisconnectedScreen.ButtonId);
        Assert.Equal(DisconnectedScreen.ReconnectLabel, button.Text);
        foreach (var state in new[] { UiStateIds.NormalRollover, UiStateIds.NormalPressed, UiStateIds.Normal })
        {
            button.SetState(state);
            Assert.Equal(DisconnectedScreen.ReconnectLabel, button.Text);
        }

        // Retail's button says "Quit Game" and keeps its own label.
        Assert.Equal("Quit Game", exiting.Element<UiButton>(DisconnectedScreen.ButtonId).Text);
    }

    [RequiresLocalDatsFact]
    public void With_retails_flag_the_disconnected_button_exits_instead()
    {
        using var rig = new LoginRig(fixture, retailDisconnect: true);
        rig.ToCharacterManagement();
        StandInSessions.Disconnected(rig.Session.Reducer, StandInSessions.ServerStopped);
        Assert.True(rig.RunUntilMode(UiModes.Disconnected));

        Assert.True(rig.Click(rig.Element<UiButton>(DisconnectedScreen.ButtonId)));
        rig.Frame();
        rig.Frame();

        Assert.Equal(0, rig.Host.Reconnects);
        Assert.Equal(UiModes.Epilogue, rig.Flow.CurrentMode);
        Assert.Equal(1, rig.Host.Exits);
    }

    [RequiresLocalDatsFact]
    public void A_host_that_cannot_reconnect_exits_loudly()
    {
        using var rig = new LoginRig(fixture);
        rig.Host.CanReconnect = false;
        rig.ToCharacterManagement();
        StandInSessions.Disconnected(rig.Session.Reducer, StandInSessions.ServerStopped);
        Assert.True(rig.RunUntilMode(UiModes.Disconnected));

        rig.Click(rig.Element<UiButton>(DisconnectedScreen.ButtonId));
        rig.Frame();
        rig.Frame();

        Assert.Equal(UiModes.Epilogue, rig.Flow.CurrentMode);
        Assert.Contains(rig.Log.Lines, l => l.Contains("TODO(retailui-reconnect)", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void Starting_at_the_disconnected_screen_gives_it_the_lost_connection_reason()
    {
        using var rig = new LoginRig(fixture, StandInSessions.DataCheckInProgress, startMode: UiModes.Disconnected);

        Assert.True(rig.RunUntilMode(UiModes.Disconnected));

        Assert.Equal("The connection to the server has been lost!", rig.Screen<DisconnectedScreen>().ReasonText);
    }

    [Theory]
    [InlineData(SessionPhase.Failed, true)]
    [InlineData(SessionPhase.Closed, false)]
    [InlineData(SessionPhase.Closing, false)]
    [InlineData(SessionPhase.CharacterSelect, false)]
    [InlineData(SessionPhase.InWorld, false)]
    public void Only_a_failure_is_a_lost_connection(SessionPhase phase, bool lost)
    {
        var state = new GameState { Phase = phase };
        Assert.Equal(lost, state.ConnectionLost);
    }

    [RequiresLocalDatsFact]
    public void The_intro_is_skipped_straight_to_character_management_with_a_log_line()
    {
        using var rig = new LoginRig(fixture, h => StandInSessions.CharacterSelect(h), startMode: UiModes.Intro);

        Assert.True(rig.RunUntilMode(UiModes.CharacterManagement));

        Assert.Contains(rig.Log.Lines, l => l.Contains("intro movies are skipped", StringComparison.Ordinal));
        Assert.IsType<CharacterManagementScreen>(rig.Flow.CurrentScreen);
    }
}
