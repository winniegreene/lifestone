using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.Session;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// <see cref="PlayPresentationFlow"/> over a live session whose server is scripted (<see cref="LiveHarness"/>): the event stream of a login, an
/// entry, an arrival, a teleport, a logoff and a lost connection, and what the world host is told in each.
/// </summary>
public sealed class PlayPresentationFlowTests
{
    [Fact]
    public void The_stages_follow_the_login_to_the_character_list()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        Assert.Equal(PlayStage.Connecting, flow.Stage);
        Assert.Contains("connecting", flow.StatusText, StringComparison.Ordinal);

        var login = h.Actions.Login();
        Assert.True(h.RunUntil(() => login.IsCompleted));
        Assert.Equal(PlayStage.CharacterList, flow.Stage);
        Assert.Equal("character list", flow.StatusText);
        Assert.False(flow.PlayerArrived);
        Assert.Empty(world.Calls);
    }

    [Fact]
    public void Entering_from_the_list_builds_the_world_once_and_the_unhide_makes_the_player_present()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        LogIn(h);

        var enter = h.Actions.EnterWorld(ScriptedAceSession.Amy, ActionSource.Ui);
        Assert.True(h.RunUntil(() => enter.IsCompleted));
        Assert.Equal(PlayStage.Arriving, flow.Stage);
        Assert.Equal(1, flow.Arrivals);
        var arriving = Assert.Single(world.Calls, c => c.StartsWith("arriving", StringComparison.Ordinal));
        Assert.Equal("arriving 0x50000001 recreated=False cell=0x860201AD", arriving);
        Assert.Contains("Amy", flow.StatusText, StringComparison.Ordinal);
        Assert.Contains("arriving", flow.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("present", world.Calls);

        var complete = h.Actions.LoginComplete();
        Assert.True(h.RunUntil(() => complete.IsCompleted));
        Assert.Equal(PlayStage.InWorld, flow.Stage);
        Assert.Equal(1, world.Calls.Count(c => c == "present"));
        Assert.Contains("in the world, cell 0x860201AD", flow.StatusText, StringComparison.Ordinal);
        Assert.Contains(world.Calls, c => c.StartsWith("state", StringComparison.Ordinal)); // the unhide reaches the avatar

        // The three other objects the server creates are the avatars track's, not play mode's: nothing of them reaches the world host.
        Assert.DoesNotContain(world.Calls, c => c.Contains("0x80000101", StringComparison.Ordinal));
    }

    [Fact]
    public void A_character_asked_for_is_selected_and_entered_by_itself_once_the_list_is_in()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions("amy"), h.Log);
        var login = h.Actions.Login();
        Assert.True(h.RunUntil(() => world.Calls.Any(c => c.StartsWith("arriving", StringComparison.Ordinal))));
        Assert.True(login.IsCompleted);
        Assert.Equal(ScriptedAceSession.Amy, h.State.Characters.SelectedGuid);
        Assert.Equal(1, Entered(h, ActionSource.Host));
        Assert.Equal(1, flow.Arrivals);

        // A second list (after a logoff) does not enter again: one entry per connection.
        var logOff = h.Actions.LogOff(ActionSource.Test);
        Assert.True(h.RunUntil(() => logOff.IsCompleted && flow.Stage == PlayStage.CharacterList));
        h.Run(1.0);
        Assert.Equal(1, Entered(h, null));
    }

    /// <summary>
    /// M6 14 body (live ladder49 01:59:12 UTC): the play's <c>--character</c> auto-enter and a login of the same character on the same list race; the login's own EnterWorld meets the session past the character list. The login adopts the
    /// entry that put its character in the world (it never fails "not at the character list (phase InWorld)"), and the session sends one entry to the server.
    /// </summary>
    [Fact]
    public void A_login_that_meets_the_auto_enter_s_entry_of_its_character_adopts_it_instead_of_failing()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions("amy"), h.Log);
        var login = new Lifestone.Client.Session.Scenarios.LoginScenario("Amy") { StayInWorld = true };
        Assert.True(h.RunUntil(() => login.Step(h.Session, h.Session.Clock.Now) != Lifestone.Client.Session.Scenarios.ScenarioStatus.Running || login.Stage == "arriving"));

        Assert.Null(login.Failure);
        Assert.Equal("arriving", login.Stage);
        Assert.NotNull(login.Adopted);
        Assert.Contains("another owner's entry", login.Adopted, StringComparison.Ordinal);
        Assert.Equal(ScriptedAceSession.Amy, login.EnteredGuid);
        Assert.Equal(SessionPhase.InWorld, h.State.Phase);
        Assert.Equal(1, world.Calls.Count(c => c.StartsWith("arriving", StringComparison.Ordinal))); // one entry reached the server and the world
    }

    /// <summary>
    /// M6 14 body: one owner of a re-login's entry. The autopilot's reconnect holds the play's auto-enter for its try (<see cref="PlayPresentationFlow.HoldAutoEnter"/> through the session adapter), so on the new connection's character list
    /// the auto-enter stands aside and only the try's login sends an EnterWorld; the hold ends with the entry.
    /// </summary>
    [Fact]
    public void The_reconnect_s_login_is_the_one_owner_of_the_entry_and_the_auto_enter_stands_aside()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions("amy"), h.Log);
        var adapter = new Lifestone.Client.Session.Hosting.GameSessionAutopilotSession(h.Session, "Amy", flow.HoldAutoEnter);
        adapter.Reconnect();
        Assert.Equal(Lifestone.Client.Session.Hosting.GameSessionAutopilotSession.EntryOwner, flow.EntryOwner);
        Assert.True(h.RunUntil(() => adapter.StepLogin(h.Session.Clock.Now) == Lifestone.Client.Session.Scenarios.ScenarioStatus.Failed || h.State.Phase == SessionPhase.InWorld));
        h.Run(1.0);

        Assert.Null(adapter.LoginFailure);
        Assert.Equal(SessionPhase.InWorld, h.State.Phase);
        Assert.Equal(1, Entered(h, null)); // one EnterWorld, the login's: none of the flow's, none refused
        Assert.Null(adapter.LoginNote); // the login entered itself
        Assert.Contains(h.Log.Entries, l => l.Category == PlayPresentationFlow.LogCategory && l.Message.Contains("stands aside on this character list", StringComparison.Ordinal));
        Assert.Null(flow.EntryOwner); // the hold covered one entry
    }

    [Theory]
    [InlineData("Zed", "no character")]
    [InlineData("BO", "pending deletion")]
    public void A_character_that_cannot_be_entered_is_a_loud_error_that_does_not_repeat_the_name(string name, string phrase)
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(name), h.Log);
        LogIn(h);
        h.Run(1.0);

        Assert.Equal(PlayStage.CharacterList, flow.Stage);
        Assert.Empty(world.Calls);
        Assert.Equal(0, Entered(h, null));
        var errors = h.Log.Entries.Where(l => l.Severity == Lifestone.Client.Diagnostics.LogSeverity.Error && l.Category == PlayPresentationFlow.LogCategory).ToArray();
        var error = Assert.Single(errors);
        Assert.Contains(phrase, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(name, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_server_teleport_and_the_server_s_other_words_about_the_player_reach_the_world_in_order()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        h.Present();
        world.Calls.Clear();

        // F751 then the new position with a newer teleport sequence: a SelfTeleport.
        var reducer = h.Session.Reducer;
        var context = new InboundContext(0, MessageQueue.SmartBox, h.Time.GetTimestamp());
        reducer.OnPlayerTeleport(context, new PlayerTeleportMsg(2));
        reducer.OnUpdatePosition(context, ReducerHarness.Move(ScriptedAceSession.Amy, instance: 1, position: 2, teleport: 2, forcePosition: 1, cell: 0xA9B40019, x: 80));
        Assert.Equal(1, flow.Teleports);
        Assert.Equal(["teleported 0xA9B40019 SelfTeleport"], world.Calls);

        // A newer position sequence with the same teleport sequence is ACE echoing an accepted move; a newer force-position sequence is a correction.
        world.Calls.Clear();
        reducer.OnUpdatePosition(context, ReducerHarness.Move(ScriptedAceSession.Amy, instance: 1, position: 3, teleport: 2, forcePosition: 1, cell: 0xA9B40019, x: 81));
        reducer.OnUpdatePosition(context, ReducerHarness.Move(ScriptedAceSession.Amy, instance: 1, position: 4, teleport: 2, forcePosition: 2, cell: 0xA9B40019, x: 82));
        Assert.Equal(["positioned 0xA9B40019 SelfEcho", "positioned 0xA9B40019 ForceBlip"], world.Calls);

        // Other objects' positions never reach it.
        world.Calls.Clear();
        reducer.OnUpdatePosition(context, ReducerHarness.Move(ScriptedAceSession.FirstObject, instance: 1, position: 5, teleport: 1, forcePosition: 1, cell: 0x860201AD, x: 70));
        Assert.Empty(world.Calls);
    }

    [Fact]
    public void A_logoff_takes_the_player_away_and_a_second_entry_arrives_again()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        h.Present();
        Assert.Equal(PlayStage.InWorld, flow.Stage);

        var logOff = h.Actions.LogOff(ActionSource.Test);
        Assert.True(h.RunUntil(() => logOff.IsCompleted && flow.Stage == PlayStage.CharacterList));
        Assert.False(flow.PlayerArrived);
        Assert.Equal(1, world.Calls.Count(c => c == "left LoggedOff"));
        Assert.Equal("character list", flow.StatusText);

        var enter = h.Actions.EnterWorld(ScriptedAceSession.Bo + 1, ActionSource.Ui);
        Assert.True(h.RunUntil(() => enter.IsCompleted));
        Assert.Equal(2, flow.Arrivals);
        Assert.Equal(2, world.Calls.Count(c => c.StartsWith("arriving", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_lost_connection_in_the_world_tears_the_player_down_and_says_why()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        h.Present();

        h.Server.FailNow();
        h.Run();
        Assert.Equal(PlayStage.Lost, flow.Stage);
        Assert.Equal(1, world.Calls.Count(c => c == "left ConnectionLost"));
        Assert.StartsWith("connection lost: ", flow.StatusText, StringComparison.Ordinal);

        // A reconnect starts over: the next login shows the stages again.
        h.Session.Reconnect();
        Assert.Equal(PlayStage.Connecting, flow.Stage);
    }

    [Fact]
    public void A_boot_is_reported_as_a_boot_not_a_lost_connection()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        using var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        h.Present();

        var context = new InboundContext(0, MessageQueue.SmartBox, h.Time.GetTimestamp());
        h.Session.Reducer.OnAccountBoot(context, new AccountBootMsg("logged in from elsewhere"));
        h.Server.FailNow(SessionFailureKind.Booted, "booted by the server");
        h.Run();
        Assert.Equal(PlayStage.Lost, flow.Stage);
        Assert.Equal(1, world.Calls.Count(c => c == "left Booted"));
        Assert.Contains("logged in from elsewhere", flow.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_disposed_flow_hears_nothing_more()
    {
        using var h = new LiveHarness();
        using var world = new FakeWorld();
        var flow = new PlayPresentationFlow(h.Session, world, new PlayFlowOptions(), h.Log);
        LogIn(h);
        flow.Dispose();
        flow.Dispose();
        var enter = h.Actions.EnterWorld(ScriptedAceSession.Amy, ActionSource.Ui);
        Assert.True(h.RunUntil(() => enter.IsCompleted));
        Assert.Empty(world.Calls);
    }

    /// <summary>The EnterWorld actions that completed (by <paramref name="source"/>, or any).</summary>
    private static int Entered(LiveHarness h, ActionSource? source)
    {
        var count = 0;
        for (var i = 0; i < h.Session.ActionLog.Count; i++)
        {
            var entry = h.Session.ActionLog[i];
            if (entry.Kind == ActionKind.EnterWorld && (source is null || entry.Source == source))
            {
                count++;
            }
        }

        return count;
    }

    private static void LogIn(LiveHarness h)
    {
        var login = h.Actions.Login();
        Assert.True(h.RunUntil(() => login.IsCompleted), "login did not complete");
    }

    /// <summary>A world host that writes down what it was told.</summary>
    private sealed class FakeWorld : IPlayWorld, IDisposable
    {
        public List<string> Calls { get; } = [];

        public void PlayerArriving(WorldObject player, bool recreated) =>
            Calls.Add($"arriving 0x{player.Guid:X8} recreated={recreated} cell=0x{player.Position!.Value.CellId:X8}");

        public void PlayerPresent() => Calls.Add("present");

        public void PlayerTeleported(in ObjectMoved move) => Calls.Add($"teleported 0x{move.Position.CellId:X8} {move.Verdict}");

        public void PlayerPositioned(in ObjectMoved move) => Calls.Add($"positioned 0x{move.Position.CellId:X8} {move.Verdict}");

        public void PlayerStateChanged(in ObjectStateChanged change) => Calls.Add($"state {change.State}");

        public void PlayerMotionChanged(in ObjectMotionChanged change) => Calls.Add("motion");

        public void PlayerDescChanged(in ObjectDescChanged change) => Calls.Add("desc");

        public void PlayerLeft(PlayLeaveReason reason) => Calls.Add($"left {reason}");

        public void Dispose()
        {
        }
    }
}
