using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.4 fix A (1.4-R2): the stand-in answers GameAction 0x0054 as ACE does (<c>Player_Inventory.cs</c> <c>HandleActionStackableMerge</c> and <c>DoHandleActionStackableMerge</c>), in ACE's order: a whole merge is
/// the source's 0x0024 and then the target's 0x0197 (no F747: ACE's <c>Destroy</c> of a contained item sends none), a partial merge the source's 0x0197 and then the target's, and a refusal is GameEvent 0x00A0 for the source
/// (error 0x0427 for two different weenies). The tracker completes against it, and the stand-in's own pack agrees with the client's model.
/// </summary>
public sealed class StandInMergeTests
{
    private const uint Hyssop = 774;
    private const uint Scarab = 691;

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig()
        {
            var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                new RecordingLogSink());
            Run(2.0);
        }

        public GameSession Session { get; }

        public StandInWorld World => Session.Reducer.LocalResponder!.World!;

        public PlayerInventory Inventory => Session.State.Self.Inventory;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        /// <summary>Creates a stack through the item verb and returns its guid once its world object has arrived.</summary>
        public uint Make(uint wcid, int stack)
        {
            var handle = Session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
            Run(1.5);
            Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(handle));
            var guid = handle.Result!.Guid;
            Assert.True(Session.State.Objects.Contains(guid));
            return guid;
        }

        public uint Size(uint guid) => Inventory.TryGetStack(guid, out var size, out _) ? size : Session.State.Objects.TryGet(guid, out var o) && o.Weenie.StackSize is { } s ? (uint)s : 0u;

        /// <summary>The stand-in's answers since <paramref name="from"/> (its log).</summary>
        public string[] Answers(int from) => [.. World.AnswerLog.Skip(from)];

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_whole_merge_is_told_as_the_removal_then_the_targets_size_and_nothing_else()
    {
        using var rig = new Rig();
        var big = rig.Make(Hyssop, 15);
        var small = rig.Make(Hyssop, 10);
        var mark = rig.World.AnswerLog.Count;

        var merge = rig.Session.Actions.MergeStack(small, big, 10, ActionSource.Host);
        rig.Run(1.5);

        Assert.Equal(ActionOutcomeKind.Confirmed, merge.Outcome!.Value.Kind);
        Assert.Equal(["0024", "0197"], rig.Answers(mark)); // ACE: GameMessageInventoryRemoveObject, then GameMessageSetStackSize; no F747
        Assert.False(rig.Inventory.Contains(small));
        Assert.Equal(25u, rig.Size(big));
        var only = Assert.Single(rig.World.Pack);
        Assert.Equal((big, 25), (only.Guid, only.Stack));
        Assert.Contains(rig.Session.Reducer.LocalResponder!.ActionSends, a => a == (GameActionTypes.StackableMerge, small, big, 10u));
    }

    [Fact]
    public void A_partial_merge_is_told_as_the_sources_size_then_the_targets_size()
    {
        using var rig = new Rig();
        var big = rig.Make(Hyssop, 15);
        var small = rig.Make(Hyssop, 10);
        var mark = rig.World.AnswerLog.Count;

        var merge = rig.Session.Actions.MergeStack(small, big, 4, ActionSource.Host);
        rig.Run(1.5);

        Assert.Equal(ActionOutcomeKind.Confirmed, merge.Outcome!.Value.Kind);
        Assert.Equal(["0197", "0197"], rig.Answers(mark));
        Assert.Equal((6u, 19u), (rig.Size(small), rig.Size(big)));
        Assert.Equal([19, 6], rig.World.Pack.Select(e => e.Stack));
        Assert.Equal(2, rig.Inventory.Pack.Count);
    }

    [Fact]
    public void A_merge_ACE_would_refuse_is_a_00A0_for_the_source_and_changes_nothing()
    {
        using var rig = new Rig();
        var hyssop = rig.Make(Hyssop, 15);
        var more = rig.Make(Hyssop, 10);
        var lead = rig.Make(Scarab, 5);
        var full = rig.Make(Hyssop, 100);
        var session = rig.Session.Reducer.LocalResponder!.Session;

        // The Action API refuses most of these at once from the model, so they go to the stand-in as the wire does.
        var cases = new (uint From, uint To, int Amount, string Why)[]
        {
            (more, hyssop, 0, "an amount of 0"),
            (more, hyssop, -1, "a negative amount"),
            (more, 0x80009999, 1, "a target that is not held"),
            (0x80009999, hyssop, 1, "a source that is not held"),
            (more, hyssop, 11, "more than the source holds"),
            (more, full, 1, "a target at its maximum"),
            (lead, hyssop, 1, "two different weenies"),
        };
        foreach (var c in cases)
        {
            var mark = rig.World.AnswerLog.Count;
            session.SendStackableMerge(c.From, c.To, c.Amount);
            rig.Run(0.5);
            Assert.True(rig.Answers(mark) is ["00A0"], c.Why + ": " + string.Join(",", rig.Answers(mark)));
        }

        Assert.Equal((15u, 10u, 5u, 100u), (rig.Size(hyssop), rig.Size(more), rig.Size(lead), rig.Size(full)));
        Assert.Equal(4, rig.Inventory.Pack.Count);
    }

    [Fact]
    public void Two_different_weenies_fail_with_ACE_s_YouCannotMergeDifferentStacks()
    {
        using var rig = new Rig();
        var hyssop = rig.Make(Hyssop, 15);
        var lead = rig.Make(Scarab, 5);
        InventoryFailed? failure = null;
        rig.Session.Events.Subscribe((in InventoryFailed e) => failure = e);

        rig.Session.Reducer.LocalResponder!.Session.SendStackableMerge(lead, hyssop, 1);
        rig.Run(0.5);

        Assert.Equal(new InventoryFailed(lead, StandInWorld.CannotMergeDifferentStacks), failure);
        Assert.Equal(0x0427u, StandInWorld.CannotMergeDifferentStacks);
    }
}
