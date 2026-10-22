using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s2: <c>OpenContainer</c> and <c>CloseContainer</c> over the order ACE really sends (cache/m4/2-1/capture.md C2, C3): the 0196, then the UseDone, then the contents' F745s for a corpse; the locked chest's sound 0x94
/// and UseDone with no 0196 and no text; the close's 0052 at once (a corpse) or 0.42 s later (a chest), with no UseDone.
/// </summary>
public sealed class ContainerTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Corpse = 0x80000201;
    private const uint Chest = 0x7A9B401D;
    private const uint Locked = 0x7A9B4072;
    private const uint Item1 = 0x80000211;
    private const uint Item2 = 0x80000212;
    private const uint Item3 = 0x80000213;
    private const uint Item4 = 0x80000214;

    private sealed class Rig
    {
        private uint _event = 100;

        public Rig()
        {
            R = new WorldActionRig();
        }

        public WorldActionRig R { get; }

        public ActionApi Api => R.Api;

        /// <summary>A ground object (a corpse or chest): its CreateObject, no container.</summary>
        public void Object(uint guid, string name)
        {
            var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, basis, isUpdate: false);
        }

        public void View(uint container, params uint[] entries) =>
            R.H.R.Reducer.OnViewContents(
                R.H.R.Context,
                new GameEventHeader(Player, ++_event, GameEventTypes.ViewContents),
                new ViewContentsEvent(container, [.. entries.Select(e => new ViewContentsEntry(e, 0))]));

        public void Close(uint container) =>
            R.H.R.Reducer.OnCloseGroundContainer(R.H.R.Context, new GameEventHeader(Player, ++_event, GameEventTypes.CloseGroundContainer), new CloseGroundContainerEvent(container));

        public void Transient(string text) => R.Raw(AceWireWriters.TransientString(Player, ++_event, text));

        public void Sound(uint guid, uint sound) => R.Raw(AceWireWriters.Sound(guid, sound));

        public void UseDone(uint error = 0) => R.Raw(AceWireWriters.UseDone(Player, ++_event, error));

        public void Contents(uint container, params uint[] items)
        {
            foreach (var item in items)
            {
                R.Item(item, container);
            }
        }
    }

    [Fact]
    public void A_corpse_opens_on_the_0196_then_waits_for_the_F745s_and_the_UseDone_between_them_is_not_the_end()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Blue Phyntos Wasp");
        var open = r.Api.OpenContainer(Corpse, ActionSource.Ui);
        r.R.Tick();

        Assert.Equal(Corpse, Assert.Single(r.R.H.Net.Of(FakeSendKind.Use)).Guid);
        r.R.Tick(0.15);
        r.View(Corpse, Item1, Item2, Item3, Item4);
        r.UseDone(); // capture C2: 37116 the 0196, 37129 the UseDone, 37145 the F745s
        r.R.Tick();
        Assert.False(open.IsCompleted);

        r.Contents(Corpse, Item1, Item2);
        r.R.Tick(0.05);
        Assert.False(open.IsCompleted);

        r.Contents(Corpse, Item3, Item4);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Contains("4 entries", open.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(Corpse, r.R.H.State.Containers.ViewedGuid);
        Assert.True(r.R.H.State.Containers.Viewed!.IsResolved);
        Assert.Contains(r.R.H.ActionLog.All(), e => e.Kind == ActionKind.OpenContainer && e.Outcome.Kind == ActionOutcomeKind.Confirmed);
    }

    [Fact]
    public void Contents_that_never_arrive_end_the_open_Confirmed_after_3_seconds_with_the_missing_guids_logged()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        var open = r.Api.OpenContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        r.View(Corpse, Item1, Item2);
        r.UseDone();
        r.Contents(Corpse, Item1);
        r.R.Tick(0.05);
        r.R.Tick(2.85);
        Assert.False(open.IsCompleted);

        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Contains("1 without an object", open.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains(r.R.H.Log.Entries, e => e.Message.Contains(ActionText0x(Item2), StringComparison.Ordinal) && e.Message.Contains("no object", StringComparison.Ordinal));
    }

    private static string ActionText0x(uint guid) => "0x" + guid.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void An_empty_chest_opens_on_its_0196_and_the_UseDone_after_it_is_consumed_not_given_to_the_next_use()
    {
        var r = new Rig();
        r.Object(Chest, "Chest");
        var open = r.Api.OpenContainer(Chest, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(1.24);
        r.View(Chest);
        r.R.Tick(); // the 0196 alone: the open completes, the UseDone is still owed
        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);

        var use = r.Api.Use(0x80000999, ActionSource.Ui);
        r.R.Tick();
        r.UseDone(); // the chest's UseDone 0 (capture C3: right after the 0196) belongs to the open
        r.R.Tick();
        Assert.False(use.IsCompleted);

        r.UseDone(); // the second use's own
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_locked_chest_is_a_ServerError_on_the_UseDone_with_no_0196_and_the_reason_is_its_sound_0x94_not_a_text()
    {
        var r = new Rig();
        r.Object(Locked, "Aluvian Pathwarden Chest");
        var open = r.Api.OpenContainer(Locked, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(1.41);
        r.Sound(Locked, 0x94);
        r.UseDone();
        r.R.Tick();

        var outcome = open.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("locked", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(0u, r.R.H.State.Containers.ViewedGuid);
    }

    [Fact]
    public void A_refusal_with_a_02EB_text_carries_the_text()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        var open = r.Api.OpenContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        r.Transient("You do not have permission to loot that corpse!");
        r.UseDone();
        r.R.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, open.Outcome!.Value.Kind);
        Assert.Contains("permission to loot", open.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_that_is_already_open_is_Confirmed_at_once_and_nothing_is_sent()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        r.View(Corpse);
        r.R.Tick();
        Assert.Equal(Corpse, r.R.H.State.Containers.ViewedGuid);

        var open = r.Api.OpenContainer(Corpse, ActionSource.Ui);
        r.R.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, open.Outcome!.Value.Kind);
        Assert.Equal("already open", open.Outcome.Value.Detail);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.Use));
        Assert.Contains(r.R.H.Log.Entries, e => e.Message.Contains("already open", StringComparison.Ordinal));
    }

    [Fact]
    public void An_open_that_hears_nothing_times_out_after_20_seconds_and_guid_0_is_refused_at_once()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        var open = r.Api.OpenContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(19.9);
        Assert.False(open.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, open.Outcome!.Value.Kind);

        var none = r.Api.OpenContainer(0, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, none.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_corpse_closes_on_the_0052_at_once_and_a_chest_after_0_42_seconds_with_no_UseDone()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        r.View(Corpse, Item1);
        r.Contents(Corpse, Item1);
        r.R.Tick();

        var close = r.Api.CloseContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        Assert.Equal(Corpse, Assert.Single(r.R.H.Net.Of(FakeSendKind.NoLongerViewingContents)).Guid);
        Assert.False(close.IsCompleted);
        r.R.Tick(0.016);
        r.Close(Corpse);
        r.R.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, close.Outcome!.Value.Kind);

        r.Object(Chest, "Chest");
        r.View(Chest);
        r.R.Tick();
        var chest = r.Api.CloseContainer(Chest, ActionSource.Ui);
        r.R.Tick(0.4);
        Assert.False(chest.IsCompleted);
        r.Close(Chest);
        r.R.Tick(0.02);
        Assert.Equal(ActionOutcomeKind.Confirmed, chest.Outcome!.Value.Kind);
        Assert.Contains(r.R.H.ActionLog.All(), e => e.Kind == ActionKind.CloseContainer);
    }

    [Fact]
    public void Closing_a_container_that_is_not_open_is_Confirmed_at_once_and_a_silent_close_times_out_after_5_seconds()
    {
        var r = new Rig();
        r.Object(Corpse, "Corpse of Tusker");
        var shut = r.Api.CloseContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        Assert.Equal("not open", shut.Outcome!.Value.Detail);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.NoLongerViewingContents));

        r.View(Corpse);
        r.R.Tick();
        var close = r.Api.CloseContainer(Corpse, ActionSource.Ui);
        r.R.Tick();
        r.R.Tick(4.9);
        Assert.False(close.IsCompleted);
        r.R.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, close.Outcome!.Value.Kind);
    }
}

/// <summary>The action log's entries, oldest first, for assertions.</summary>
internal static class ActionLogView
{
    public static IEnumerable<ActionLogEntry> All(this ActionLog log) => Enumerable.Range(0, log.Count).Select(i => log[i]);
}
