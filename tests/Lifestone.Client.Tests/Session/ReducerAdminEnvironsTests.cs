using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.3 s2b: GM 0xEA60 AdminEnvirons is applied (<c>CM_Admin::DispatchUI_Environs</c> 11.6096 0x006AE870, then
/// <c>CPlayerSystem::Handle_Admin__Environs</c> 0x0055EB40): the sound options publish <see cref="AdminEnvironChanged"/> once the player's object
/// exists, the fog options and the clear publish it for the host's named TODO, the options retail ignores publish nothing, and nothing is
/// "decoded, not yet applied" any more.
/// </summary>
public sealed class ReducerAdminEnvironsTests
{
    /// <summary>The harness lists the event types it records by name; this subscribes to the one under test.</summary>
    private static List<AdminEnvironChanged> Record(ReducerHarness rig)
    {
        var seen = new List<AdminEnvironChanged>();
        rig.Bus.Subscribe((in AdminEnvironChanged e) => seen.Add(e));
        return seen;
    }

    private static void Send(ReducerHarness rig, uint option) =>
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(rig.Context, AceM3Writers.AdminEnvirons(option), rig.Reducer).Outcome);

    [Fact]
    public void A_sound_option_is_published_once_the_players_object_exists_and_dropped_before()
    {
        var rig = new ReducerHarness();
        var seen = Record(rig);

        Send(rig, 118);
        Assert.Empty(seen);

        rig.EnterWorld();
        Send(rig, 118);

        var published = Assert.Single(seen);
        Assert.Equal(new AdminEnvironChanged(118, EnvironKind.Sound, 0x85), published);
        Assert.Equal(0, rig.Reducer.Counters.DecodedNotApplied);
        Assert.Empty(rig.Of<UnknownMessage>());
        Assert.Empty(rig.Of<MessageDropped>());
    }

    [Fact]
    public void The_fog_options_and_the_clear_are_published_without_a_player_and_name_their_todo_once()
    {
        var rig = new ReducerHarness();
        var seen = Record(rig);

        Send(rig, 3);
        Send(rig, 3);
        Send(rig, 0);

        Assert.Equal([EnvironKind.Fog, EnvironKind.Fog, EnvironKind.Clear], seen.Select(e => e.Kind));
        Assert.Equal(1, rig.Log.Entries.Count(e => e.Message.Contains("option 3 ", StringComparison.Ordinal) && e.Message.Contains("TODO(m3-environs-fog)", StringComparison.Ordinal)));
        Assert.Equal(0, rig.Reducer.Counters.DecodedNotApplied);
    }

    [Fact]
    public void An_option_retail_ignores_publishes_nothing_and_is_logged_once()
    {
        var rig = new ReducerHarness();
        var seen = Record(rig);
        rig.EnterWorld();

        Send(rig, 115);
        Send(rig, 115);
        Send(rig, 7);

        Assert.Empty(seen);
        Assert.Equal(1, rig.Log.Entries.Count(e => e.Message.Contains("option 115 ", StringComparison.Ordinal)));
        Assert.Equal(0, rig.Reducer.Counters.DecodedNotApplied);
        Assert.Empty(rig.Of<UnknownMessage>());
    }
}
