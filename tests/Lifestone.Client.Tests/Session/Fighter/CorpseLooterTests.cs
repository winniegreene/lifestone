using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The shared loot loop (M4 2.2 s0, <see cref="CorpseLooter"/>) over the real Action API with the server's side scripted from ACE's own writers (<see cref="WorldActionRig"/>): one move at a time in view order, an unresolved
/// entry that resolves later, the burden refusal that ends the loop, a refusal that does not, the timeout, a stack beside a held stack of its class (no merge case: its own 0022), take-all and the quiet step.
/// </summary>
public sealed class CorpseLooterTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Corpse = 0x80000201;
    private const uint A = 0x80000211;
    private const uint B = 0x80000212;
    private const uint C = 0x80000213;
    private const uint HeldArrows = 0x80000601;
    private const uint Misc = 0x80;
    private const uint Gem = 0x800;
    private const double Hold = 3.0; // a confirmed move's chain (1.35 s) plus the return stance and the margin, with room

    private sealed class Rig
    {
        private uint _event = 100;

        public Rig()
        {
            R = new WorldActionRig();
            R.Describe([], []);
            R.Tick();
        }

        public WorldActionRig R { get; }

        public int Sent => R.H.Net.Of(FakeSendKind.PutItemInContainer).Count;

        public uint[] SentGuids => [.. R.H.Net.Of(FakeSendKind.PutItemInContainer).Select(s => s.Guid)];

        public double Now => R.H.R.Clock.Now;

        public RecordingLogSink Log => R.H.Log;

        public static WorldObject Thing(uint guid, uint type = Misc, uint? value = 10, ushort? burden = null, string name = "Thing", uint wcid = 141, ushort? maxStack = null) =>
            LootRulesTests.Item(type, value, burden, guid, name, wcid, maxStack);

        private static CreateObjectMsg InCorpse(WorldObject o) =>
            o.Created with { Weenie = o.Created.Weenie with { Header = o.Created.Weenie.Header | WeenieHeaderFlags.Container, ContainerId = Corpse } };

        /// <summary>The corpse, a 0196 naming <paramref name="items"/>, and the CreateObject of every one that is not in <paramref name="late"/>.</summary>
        public void Open(WorldObject[] items, params uint[] late)
        {
            var corpse = ReducerHarness.Create(Corpse, ReducerHarness.Stamps(), null, name: "Corpse of Tusker");
            R.H.R.Reducer.OnCreateObject(R.H.R.Context, corpse, isUpdate: false);
            R.H.R.Reducer.OnViewContents(
                R.H.R.Context,
                new GameEventHeader(Player, ++_event, GameEventTypes.ViewContents),
                new ViewContentsEvent(Corpse, [.. items.Select(i => new ViewContentsEntry(i.Guid, 0))]));
            foreach (var item in items.Where(i => !late.Contains(i.Guid)))
            {
                Arrive(item);
            }

            R.Tick();
        }

        public void Arrive(WorldObject item) => R.H.R.Reducer.OnCreateObject(R.H.R.Context, InCorpse(item), isUpdate: false);

        public void Put(uint item) => R.Server((p, s) => AceM3Writers.PutObjInContainer(p, s, item, Player, 0, 0));

        public void Refuse(uint item, uint code)
        {
            R.Raw(AceWireWriters.WeenieError(Player, ++_event, code));
            R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, item, 0));
        }

        public void RefuseText(uint item, string text)
        {
            R.Raw(AceWireWriters.TransientString(Player, ++_event, text));
            R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, item, 0));
        }

        /// <summary>ACE's refusal of a pick-up into a full main pack: a 028B WeenieErrorWithString (error 0) "Unable to put X into container", then the 00A0 (<c>Container.cs</c> 527, P5a F-gate).</summary>
        public void RefusePackFull(uint item, string name)
        {
            R.Raw(AceWireWriters.WeenieErrorWithString(Player, ++_event, 0, $"Unable to put {name} into container"));
            R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, item, 0));
        }

        public LootStatus Step(CorpseLooter looter) => looter.Step(R.H.State, R.Api, Now);

        /// <summary>Ticks the clock in 0.1 s steps, stepping the looter each time, until it is done or <paramref name="seconds"/> pass.</summary>
        public LootStatus Drive(CorpseLooter looter, double seconds)
        {
            var status = LootStatus.Running;
            for (var t = 0.0; t < seconds - 1e-9 && status == LootStatus.Running; t += 0.1)
            {
                R.Tick(0.1);
                status = Step(looter);
            }

            return status;
        }

        public CorpseLooter Looter(LootRules? rules = null, uint container = Corpse) => new(Log, "hunt", container, rules);
    }

    [Fact]
    public void One_move_at_a_time_in_view_order_each_only_after_the_previous_one_has_an_outcome()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B), Rig.Thing(C)]);
        var looter = r.Looter();

        Assert.Equal(LootStatus.Running, r.Step(looter));
        r.R.Tick();
        Assert.Equal([A], r.SentGuids); // the first entry only
        Assert.Equal(LootStatus.Running, r.Drive(looter, 2.0));
        Assert.Equal([A], r.SentGuids); // no 0022 yet: nothing else is sent, however long the loop waits

        r.Put(A);
        r.R.Tick();
        Assert.Equal(LootStatus.Running, r.Step(looter));
        Assert.Equal(1, looter.Looted);
        Assert.Equal([A], r.SentGuids); // B is queued behind the Inventory lane's hold for A's chain: the loop added no wait of its own and does not send early

        r.Drive(looter, Hold);
        Assert.Equal([A, B], r.SentGuids);
        r.Put(B);
        r.Drive(looter, Hold);
        Assert.Equal([A, B, C], r.SentGuids);
        r.Put(C);
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));

        Assert.Equal(3, looter.Looted);
        Assert.Equal("end", looter.StoppedBy);
        Assert.Equal(0, looter.Refused + looter.TimedOut + looter.Skipped);
        Assert.True(r.R.H.State.Self.Inventory.Find(A).Held && r.R.H.State.Self.Inventory.Find(C).Held);
        Assert.Contains(r.Log.Lines, l => l.Contains("loot: done looted=3", StringComparison.Ordinal));
        Assert.Equal(3, r.Log.Lines.Count(l => l.Contains("loot: taking ", StringComparison.Ordinal)));
        Assert.Equal(LootStatus.Done, r.Step(looter));
        Assert.Equal(3, r.Sent);
    }

    [Fact]
    public void An_unresolved_entry_is_left_until_its_object_arrives_and_is_then_taken()
    {
        var r = new Rig();
        var late = Rig.Thing(A);
        r.Open([late, Rig.Thing(B)], A);
        var looter = r.Looter();

        r.Drive(looter, 0.5);
        Assert.Equal([B], r.SentGuids); // A is first in view order but has no object yet: B goes first

        r.Put(B);
        r.Drive(looter, 1.0);
        Assert.Equal(LootStatus.Running, r.Step(looter)); // A is still waited for, inside the cap
        r.Arrive(late); // its F745 comes
        r.Drive(looter, Hold);
        Assert.Equal([B, A], r.SentGuids);
        r.Put(A);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(2, looter.Looted);
        Assert.Equal(0, looter.Skipped);
    }

    [Fact]
    public void An_entry_that_never_resolves_is_left_after_the_cap_as_not_resolved()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A)], A);
        var looter = r.Looter();

        Assert.Equal(LootStatus.Running, r.Drive(looter, CorpseLooter.UnresolvedCapSeconds - 0.5));
        Assert.Equal(LootStatus.Done, r.Drive(looter, 1.0));

        Assert.Equal(0, r.Sent);
        Assert.Equal(1, looter.SkippedBy(LootDecision.NotResolved));
        Assert.Equal("end", looter.StoppedBy);
    }

    [Fact]
    public void The_rule_decides_each_resolved_entry_and_the_skips_are_counted_by_reason()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A, Gem, 453), Rig.Thing(B, Misc, 0), Rig.Thing(C, Misc, 4000)]);
        var looter = r.Looter(LootRules.Default);

        r.Drive(looter, 0.3);
        Assert.Equal([C], r.SentGuids);
        r.Put(C);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(1, looter.Looted);
        Assert.Equal(2, looter.Skipped);
        Assert.Equal(1, looter.SkippedBy(LootDecision.Type));
        Assert.Equal(1, looter.SkippedBy(LootDecision.Value));
        Assert.Contains("looted=1 skipped=2 (value=1 type=1 burden=0 held=0 unresolved=0) refused=0 timed_out=0 stopped_by=end", looter.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Take_all_takes_every_resolved_entry_whatever_its_type_or_value()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A, Gem, 453), Rig.Thing(B, 0x10, 0)]);
        var looter = r.Looter(); // no rule: take all

        r.Drive(looter, 0.3);
        r.Put(A);
        r.Drive(looter, Hold);
        r.Put(B);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal([A, B], r.SentGuids);
        Assert.Equal(2, looter.Looted);
        Assert.Equal(0, looter.Skipped);
    }

    [Fact]
    public void ACEs_quest_refusal_that_names_no_item_ends_the_pick_up_at_once_not_at_its_timeout()
    {
        // M6 sprint (run 5, cache/m6/sprint/run5/step3-levelup.log 15:58:51-15:59:16 UTC): a second Female Tusker Tusk inside its 20 h gate; ACE's HandleSolveError (QuestManager.cs:558-578 @95ef426d) sends the
        // two chat lines and a 0x00A0 for guid 0 with 0x043E YouHaveSolvedThisQuestTooRecently, which the pick-up did not own: it waited out its 25 s timeout, standing.
        var r = new Rig();
        r.Open([Rig.Thing(A)]);
        var looter = r.Looter();
        r.Drive(looter, 0.3);
        Assert.Equal([A], r.SentGuids);
        var asked = r.Now;

        r.R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, 0, 0x043E));
        var status = r.Drive(looter, 2.0);

        Assert.NotEqual(LootStatus.Running, status);
        Assert.True(r.Now - asked < 2.5, $"the pick-up ended {r.Now - asked:0.0} s after it was asked");
        Assert.Equal(1, looter.Refused);
        Assert.Equal(0, looter.TimedOut);
    }

    [Fact]
    public void A_failure_naming_no_item_with_another_error_is_not_the_pick_ups()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A)]);
        var looter = r.Looter();
        r.Drive(looter, 0.3);

        r.R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, 0, 0x0029));
        Assert.Equal(LootStatus.Running, r.Drive(looter, 2.0)); // not a quest refusal: still waiting for its own answer
        Assert.Equal(0, looter.Refused);
    }

    [Fact]
    public void A_burden_refusal_ends_the_loop_and_the_rest_is_not_sent()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B), Rig.Thing(C)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Put(A);
        r.Drive(looter, Hold);
        Assert.Equal([A, B], r.SentGuids);
        r.Refuse(B, CorpseLooter.TooEncumberedCode); // 028A 0x002A "You are too encumbered to carry that!", then the 00A0

        Assert.Equal(LootStatus.Done, r.Drive(looter, 1.0));
        Assert.Equal("burden", looter.StoppedBy);
        Assert.Equal(1, looter.Looted);
        Assert.Equal(1, looter.Refused);
        Assert.Equal(0, looter.TimedOut);
        r.R.Tick(Hold);
        Assert.Equal([A, B], r.SentGuids); // C never went
        Assert.Contains(r.Log.Lines, l => l.Contains("the burden limit: stopping", StringComparison.Ordinal));
    }

    [Fact]
    public void The_burden_refusal_in_its_transient_text_form_ends_the_loop_too()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.RefuseText(A, "You are too encumbered to carry that!");

        Assert.Equal(LootStatus.Done, r.Drive(looter, 1.0));
        Assert.Equal("burden", looter.StoppedBy);
        Assert.Equal(1, looter.Refused);
        Assert.Equal([A], r.SentGuids);
    }

    [Fact]
    public void A_refusal_that_is_not_a_burden_one_is_counted_and_the_loop_goes_on()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Refuse(A, 0x0029); // "You cannot pick that up!"
        r.Drive(looter, Hold);
        Assert.Equal([A, B], r.SentGuids);
        r.Put(B);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal("end", looter.StoppedBy);
        Assert.Equal(1, looter.Refused);
        Assert.Equal(1, looter.Looted);
        Assert.False(r.R.H.State.Self.Inventory.Find(A).Held);
    }

    [Fact]
    public void Two_pack_full_refusals_in_a_row_end_the_loop_naming_the_rest_as_skipped_P5a_Fgate()
    {
        var r = new Rig();
        const uint D = 0x80000214;
        r.Open([Rig.Thing(A), Rig.Thing(B), Rig.Thing(C), Rig.Thing(D)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.RefusePackFull(A, "Thing");
        r.Drive(looter, Hold);
        Assert.Equal([A, B], r.SentGuids); // one refusal: the loop goes on
        r.RefusePackFull(B, "Thing");

        Assert.Equal(LootStatus.Done, r.Drive(looter, 1.0));
        Assert.Equal("pack-full", looter.StoppedBy);
        Assert.Equal(2, looter.Refused);
        Assert.Equal(2, looter.SkippedPackFull); // C and D were never sent
        Assert.Equal(2, looter.Skipped);
        r.R.Tick(Hold);
        Assert.Equal([A, B], r.SentGuids);
        Assert.Contains(r.Log.Lines, l => l.Contains("loot_skipped=2 why=pack-full", StringComparison.Ordinal) && l.Contains("TODO(m4-hunt-pack-full)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_pack_full_refusal_that_is_not_followed_by_another_does_not_stop_the_loop_P5a_Fgate()
    {
        var r = new Rig();
        const uint D = 0x80000214;
        r.Open([Rig.Thing(A), Rig.Thing(B), Rig.Thing(C), Rig.Thing(D)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.RefusePackFull(A, "Thing");
        r.Drive(looter, Hold);
        r.Put(B); // a confirmation breaks the run
        r.Drive(looter, Hold);
        r.RefusePackFull(C, "Thing");
        r.Drive(looter, Hold);
        Assert.Equal([A, B, C, D], r.SentGuids);
        r.Put(D);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal("end", looter.StoppedBy);
        Assert.Equal(2, looter.Looted);
        Assert.Equal(0, looter.SkippedPackFull);
    }

    [Fact]
    public void A_move_that_gets_no_answer_times_out_is_counted_and_the_loop_goes_on()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        Assert.Equal([A], r.SentGuids);
        r.Drive(looter, 26.0); // the 25 s timeout of a pick-up
        Assert.Equal(1, looter.TimedOut);
        Assert.Equal([A, B], r.SentGuids);
        r.Put(B);

        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(1, looter.Looted);
        Assert.Equal(1, looter.TimedOut);
    }

    private const uint HeldCoins = 0x80000602;
    private const uint Pyreal = 273;
    private static byte _seq;

    /// <summary>A held stack of weenie <paramref name="wcid"/> in the main pack (the model's CreateObject with the container, the size and the maximum).</summary>
    private static void HeldStack(Rig r, uint guid, uint wcid, ushort size, ushort max, bool describe = true)
    {
        if (describe)
        {
            r.R.Describe([new ContentDesc(guid, 0)], []);
        }

        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: "Pyreal");
        r.R.H.R.Reducer.OnCreateObject(
            r.R.H.R.Context,
            basis with
            {
                Weenie = basis.Weenie with
                {
                    Header = WeenieHeaderFlags.Container | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize,
                    ContainerId = Player,
                    WeenieClassId = wcid,
                    StackSize = size,
                    MaxStackSize = max,
                },
            },
            isUpdate: false);
        r.R.Tick();
    }

    /// <summary>A corpse stack of <paramref name="size"/> of weenie <paramref name="wcid"/>, at most 25000, worth 1 each.</summary>
    private static WorldObject CorpseStack(uint guid, uint wcid, ushort size)
    {
        var o = Rig.Thing(guid, Misc, size, wcid: wcid, maxStack: 25000, name: "Pyreal");
        return new WorldObject(o.Created with { Weenie = o.Created.Weenie with { StackSize = size } }, isSelf: false, createdAt: 0);
    }

    private static (uint Guid, int Amount, string? Target) OnlyMerge(Rig r)
    {
        var sent = Assert.Single(r.R.H.Net.Of(FakeSendKind.StackableMerge));
        return (sent.Guid, sent.Index, sent.Target);
    }

    [Fact]
    public void M5_7_a_coin_stack_looted_beside_a_held_stack_is_merged_into_it_and_the_loop_waits_for_the_merge()
    {
        // ACE never merges at pick-up (Container.cs 499-581): the client sends GameAction 0x0054 from the new stack into the held one, for the whole of it when it fits.
        var r = new Rig();
        HeldStack(r, HeldCoins, Pyreal, 10000, 25000);
        r.Open([CorpseStack(A, Pyreal, 1500), Rig.Thing(B)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Put(A);
        Assert.Equal(LootStatus.Running, r.Drive(looter, Hold));
        Assert.Equal((A, 1500, HeldCoins.ToString("X8")), OnlyMerge(r));
        Assert.Equal([A], r.SentGuids); // the next pick-up waits for the merge's outcome
        Assert.Equal(1, looter.StacksBesideHeld);

        r.R.Raw(AceM3Writers.InventoryRemoveObject(A)); // ACE's whole merge: the source's removal, then the target's new size (Player_Inventory.cs 3096, 3121-3123)
        r.R.Raw(AceM3Writers.SetStackSize(++_seq, HeldCoins, 11500, 0));
        r.Drive(looter, Hold);
        Assert.Equal(1, looter.StackMerges);
        Assert.Equal(0, looter.MergesRefused);
        Assert.True(looter.WasMergedAway(A));
        Assert.False(r.R.H.State.Self.Inventory.Contains(A));
        Assert.Equal([A, B], r.SentGuids);

        r.Put(B);
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(2, looter.Looted);
        Assert.Contains("stack_merges=1 merges_refused=0", looter.Summary, StringComparison.Ordinal);
        Assert.Contains(r.Log.Lines, l => l.Contains("loot: merged ", StringComparison.Ordinal));
    }

    [Fact]
    public void M6_sprint_with_the_main_pack_full_a_corpse_coin_stack_joins_the_held_stack_straight_from_the_corpse()
    {
        // Run 5 (2026-11-19, 16:57-17:52 UTC): "Unable to put Pyreal into container" eight times at main=0/102 while the held stack had room (16,839 of 25,000): a pick-up needs a slot (Player_Inventory.cs 1318-1322), a merge
        // from the open corpse does not (HandleActionStackableMerge finds the source among LocationsICanMove, 2849ff): no pick-up is sent, the coins join the held stack, the move counts as looted.
        var r = new Rig();
        var pack = new List<ContentDesc> { new(HeldCoins, 0) };
        for (uint i = 1; i < 102; i++)
        {
            pack.Add(new ContentDesc(0x80000700 + i, 0));
        }

        r.R.Describe([.. pack], []);
        HeldStack(r, HeldCoins, Pyreal, 16839, 25000, describe: false);
        Assert.Equal(0, Lifestone.Client.Session.Quartermaster.PackView.MainPackFree(r.R.H.State));
        r.Open([CorpseStack(A, Pyreal, 200)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        Assert.Equal((A, 200, HeldCoins.ToString("X8")), OnlyMerge(r));
        Assert.Empty(r.SentGuids); // no pick-up: it would be refused for the slot
        Assert.Contains(r.Log.Lines, l => l.Contains("m6-loot-merge-from-corpse", StringComparison.Ordinal));

        r.R.Raw(AceM3Writers.InventoryRemoveObject(A)); // ACE's whole merge: the source's removal, then the target's new size (Player_Inventory.cs 3096, 3121-3123)
        r.R.Raw(AceM3Writers.SetStackSize(++_seq, HeldCoins, 17039, 0));
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(1, looter.Looted);
        Assert.Equal(0, looter.Refused);
        Assert.True(looter.WasMergedAway(A));
    }

    [Fact]
    public void M6_sprint_with_a_free_slot_a_corpse_coin_stack_is_still_picked_up_then_merged()
    {
        // The merge from the corpse is the full pack's answer only: with a slot free the proven pick-up and merge stand.
        var r = new Rig();
        HeldStack(r, HeldCoins, Pyreal, 16839, 25000);
        r.Open([CorpseStack(A, Pyreal, 200)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        Assert.Equal([A], r.SentGuids);
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.StackableMerge));
    }

    [Fact]
    public void M5_7_a_stackable_with_no_held_stack_of_its_class_is_not_merged()
    {
        var r = new Rig();
        r.Open([CorpseStack(A, Pyreal, 1500)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Put(A);
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.StackableMerge));
        Assert.Equal(0, looter.StackMerges);
        Assert.Equal(0, looter.StacksBesideHeld);
    }

    [Fact]
    public void M5_7_a_held_stack_that_is_full_takes_nothing_and_one_with_less_room_takes_part_of_it()
    {
        var full = new Rig();
        HeldStack(full, HeldCoins, Pyreal, 25000, 25000);
        full.Open([CorpseStack(A, Pyreal, 1500)]);
        var fullLooter = full.Looter();
        full.Drive(fullLooter, 0.3);
        full.Put(A);
        Assert.Equal(LootStatus.Done, full.Drive(fullLooter, Hold));
        Assert.Empty(full.R.H.Net.Of(FakeSendKind.StackableMerge)); // no room: the looted stack stays its own stack
        Assert.Equal(0, fullLooter.StackMerges);
        Assert.Equal(1, fullLooter.StacksBesideHeld);

        var part = new Rig();
        HeldStack(part, HeldCoins, Pyreal, 24000, 25000);
        part.Open([CorpseStack(A, Pyreal, 1500)]);
        var partLooter = part.Looter();
        part.Drive(partLooter, 0.3);
        part.Put(A);
        Assert.Equal(LootStatus.Running, part.Drive(partLooter, Hold));
        Assert.Equal((A, 1000, HeldCoins.ToString("X8")), OnlyMerge(part)); // the room, not the whole stack

        part.R.Raw(AceM3Writers.SetStackSize(++_seq, A, 500, 0)); // ACE's partial merge: the source's new size first, then the target's (3151-3161)
        part.R.Raw(AceM3Writers.SetStackSize(++_seq, HeldCoins, 25000, 0));
        Assert.Equal(LootStatus.Done, part.Drive(partLooter, Hold)); // 500 are left and no other stack has room: nothing more is sent
        Assert.Single(part.R.H.Net.Of(FakeSendKind.StackableMerge));
        Assert.Equal(1, partLooter.StackMerges);
        Assert.False(partLooter.WasMergedAway(A));
        Assert.True(part.R.H.State.Self.Inventory.Contains(A));
    }

    [Fact]
    public void M5_7_a_refused_merge_is_logged_and_counted_the_stack_stays_and_the_loop_goes_on()
    {
        var r = new Rig();
        HeldStack(r, HeldCoins, Pyreal, 10000, 25000);
        r.Open([CorpseStack(A, Pyreal, 1500), Rig.Thing(B)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Put(A);
        r.Drive(looter, Hold);
        Assert.Single(r.R.H.Net.Of(FakeSendKind.StackableMerge));

        r.R.Server((p, s) => AceM3Writers.InventoryServerSaysFailed(p, s, A, 0)); // ACE's 00A0 for the source
        r.Drive(looter, Hold);
        Assert.Equal(1, looter.MergesRefused);
        Assert.Equal(0, looter.StackMerges);
        Assert.Equal([A, B], r.SentGuids); // the loop went on with the next entry
        Assert.Single(r.R.H.Net.Of(FakeSendKind.StackableMerge)); // and did not ask for the same merge again
        Assert.True(r.R.H.State.Self.Inventory.Contains(A));
        Assert.Contains(r.Log.Lines, l => l.Contains("the merge of ", StringComparison.Ordinal) && l.Contains("ended ServerError", StringComparison.Ordinal));

        r.Put(B);
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Equal(2, looter.Looted);
        Assert.Contains("stack_merges=0 merges_refused=1", looter.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void M5_7_the_buy_planner_merges_the_smallest_held_stack_into_the_fullest_one_with_room_and_skips_a_refused_source()
    {
        var r = new Rig();
        const uint Small = 0x80000603;
        const uint Mid = 0x80000604;
        r.R.Describe([new ContentDesc(HeldCoins, 0), new ContentDesc(Small, 0), new ContentDesc(Mid, 0)], []);
        HeldStack(r, HeldCoins, Pyreal, 20, 100, describe: false);
        HeldStack(r, Small, Pyreal, 3, 100, describe: false);
        HeldStack(r, Mid, Pyreal, 10, 100, describe: false);
        var scratch = new List<uint>();
        var skip = new HashSet<uint>();

        Assert.True(StackMerge.TryPlanFor(r.R.H.State, Pyreal, scratch, skip, out var from, out var to, out var amount));
        Assert.Equal((Small, HeldCoins, 3), (from, to, amount));

        skip.Add(Small); // its merge was refused: the next smallest goes
        Assert.True(StackMerge.TryPlanFor(r.R.H.State, Pyreal, scratch, skip, out from, out to, out amount));
        Assert.Equal((Mid, HeldCoins, 10), (from, to, amount));

        Assert.False(StackMerge.TryPlanFor(r.R.H.State, 999, scratch, skip, out _, out _, out _)); // another weenie class: nothing held
    }

    [Fact]
    public void M5_7_with_the_merge_off_a_looted_stack_beside_a_held_one_is_left_as_it_landed()
    {
        var r = new Rig();
        HeldStack(r, HeldCoins, Pyreal, 10000, 25000);
        r.Open([CorpseStack(A, Pyreal, 1500)]);
        var looter = new CorpseLooter(r.Log, "hunt", Corpse, null, mergeStacks: false);

        r.Drive(looter, 0.3);
        r.Put(A);
        Assert.Equal(LootStatus.Done, r.Drive(looter, Hold));
        Assert.Empty(r.R.H.Net.Of(FakeSendKind.StackableMerge));
        Assert.Equal(1, looter.StacksBesideHeld);
    }

    [Fact]
    public void A_looted_item_with_no_stack_of_its_class_held_is_not_counted()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A, Misc, 50, wcid: 7, maxStack: 250)]);
        var looter = r.Looter();

        r.Drive(looter, 0.3);
        r.Put(A);
        r.Drive(looter, Hold);

        Assert.Equal(1, looter.Looted);
        Assert.Equal(0, looter.StacksBesideHeld);
    }

    [Fact]
    public void A_container_that_is_not_the_viewed_one_is_closed_at_once_and_an_abandoned_loop_sends_nothing_more()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);

        var elsewhere = r.Looter(container: 0x80000999);
        Assert.Equal(LootStatus.Done, r.Step(elsewhere));
        Assert.Equal("closed", elsewhere.StoppedBy);

        var looter = r.Looter();
        r.Drive(looter, 0.3);
        looter.Abandon("abandoned");
        Assert.True(looter.IsDone);
        Assert.Equal("abandoned", looter.StoppedBy);
        r.Put(A);
        r.Drive(looter, Hold);
        Assert.Equal([A], r.SentGuids);
        Assert.Equal(LootStatus.Done, r.Step(looter));
    }

    [Fact]
    public void M4_2_2_fix_R3_an_abandon_with_a_move_in_flight_keeps_the_handle_counts_the_0022_that_follows_and_sends_nothing_more()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();
        r.Drive(looter, 0.3);
        Assert.Equal([A], r.SentGuids);

        looter.Abandon("abandoned");
        Assert.True(looter.IsDone);
        Assert.True(looter.IsDraining); // the pick-up of A is still in flight
        Assert.Equal(0, looter.Looted);
        Assert.Equal("abandoned", looter.StoppedBy);

        r.Put(A); // the 0022 lands after the abandon
        r.R.Tick(0.1);
        Assert.Equal(LootStatus.Done, r.Step(looter));
        Assert.False(looter.IsDraining);
        Assert.Equal(1, looter.Looted); // counted
        Assert.Equal("abandoned", looter.StoppedBy);
        Assert.True(r.R.H.State.Self.Inventory.Find(A).Held);

        r.R.Tick(Hold * 2);
        Assert.Equal(LootStatus.Done, r.Step(looter));
        Assert.Equal([A], r.SentGuids); // B was never sent
        Assert.Equal(1, looter.Looted);
        Assert.Contains(r.Log.Lines, l => l.Contains("it settled after the loop was abandoned", StringComparison.Ordinal));
        Assert.Contains(r.Log.Lines, l => l.Contains("is in flight at the abandon", StringComparison.Ordinal));
    }

    [Fact]
    public void M4_2_2_fix_R3_a_drain_whose_pick_up_gets_no_answer_ends_by_the_pick_ups_own_timeout_counted_as_timed_out()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();
        r.Drive(looter, 0.3);
        looter.Abandon("abandoned");
        Assert.True(looter.IsDraining);

        for (var t = 0.0; t < CorpseLooter.DrainSeconds && looter.IsDraining; t += 0.1)
        {
            r.R.Tick(0.1);
            _ = r.Step(looter);
        }

        Assert.False(looter.IsDraining, "the pick-up's own 25 s timeout ends the drain within CorpseLooter.DrainSeconds");
        Assert.Equal(1, looter.TimedOut);
        Assert.Equal(0, looter.Looted);
        Assert.Equal([A], r.SentGuids);
        Assert.Contains(r.Log.Lines, l => l.Contains("(after the loop was abandoned)", StringComparison.Ordinal));
    }

    [Fact]
    public void M4_2_2_fix_R3_a_refusal_that_settles_after_the_abandon_is_counted_and_does_not_end_the_loop_a_second_time()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)]);
        var looter = r.Looter();
        r.Drive(looter, 0.3);
        looter.Abandon("abandoned");

        r.Refuse(A, CorpseLooter.TooEncumberedCode);
        r.R.Tick(0.1);
        Assert.Equal(LootStatus.Done, r.Step(looter));

        Assert.False(looter.IsDraining);
        Assert.Equal(1, looter.Refused);
        Assert.Equal("abandoned", looter.StoppedBy); // not rewritten to "burden"
        Assert.Equal(1, r.Log.Lines.Count(l => l.Contains("loot: done ", StringComparison.Ordinal))); // the end was logged once
    }

    [Fact]
    public void An_empty_corpse_is_done_at_once()
    {
        var r = new Rig();
        r.Open([]);
        var looter = r.Looter();

        Assert.Equal(LootStatus.Done, r.Step(looter));
        Assert.Equal(0, looter.Looted);
        Assert.Equal("end", looter.StoppedBy);
    }

    [Fact]
    public void A_step_that_waits_allocates_nothing()
    {
        var r = new Rig();
        r.Open([Rig.Thing(A), Rig.Thing(B)], B);
        var looter = r.Looter();
        r.Drive(looter, 0.3); // A is in flight; B is unresolved
        for (var i = 0; i < 5; i++)
        {
            _ = r.Step(looter); // warm up
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = looter.Step(r.R.H.State, r.R.Api, r.Now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        // and with the move settled, the pass over a view with an unresolved entry (the context, the set, the entries) allocates nothing either
        r.Put(A);
        r.Drive(looter, 0.3);
        for (var i = 0; i < 5; i++)
        {
            _ = r.Step(looter);
        }

        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            _ = looter.Step(r.R.H.State, r.R.Api, r.Now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
