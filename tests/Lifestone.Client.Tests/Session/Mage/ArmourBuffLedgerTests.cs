using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M4 4.1 lane b, the armour memory: <see cref="ArmourBuffLedger"/> (record, remaining, forget, the snapshot a store keeps), the cycle's use of it (a second cycle object on the same ledger casts no known armour line, a piece taken
/// off loses its lines) and the appraisal that seeds an empty ledger after a login (<see cref="ArmourLedgerSeeder"/>) on <see cref="ArmourLedgerRig"/>: a <see cref="ScriptedCaster"/> session whose worn pieces answer
/// <see cref="IActionApi.Identify"/> with the enchantments the test gives them. ACE's rule that the clock of an item enchantment stops while its owner is logged out is the ledger's remarks; the restore test below proves
/// that a saved time is used only when the appraisal still shows the same spell on the same piece.
/// </summary>
public sealed class ArmourBuffLedgerTests
{
    private const string Impen = "Impenetrability";
    private const string Bane = "Bludgeon Bane";
    private const uint PieceA = 0x80000201;
    private const uint PieceB = 0x80000202;

    private static IReadOnlyList<uint> IdsOf(string line) => [.. ArenaProfile.LineNamed(line)!.Spells.Select(s => s.Id)];

    /// <summary>The key of a whole-set restore: the exported seconds are checked against a tolerance because the second session's clock starts later than the first's end.</summary>
    private sealed class MemoryStore : IArmourLedgerStore
    {
        public Dictionary<uint, string> Saved { get; } = [];

        public int Loads { get; private set; }

        public string? Load(uint characterGuid)
        {
            Loads++;
            return Saved.GetValueOrDefault(characterGuid);
        }

        public void Save(uint characterGuid, string json) => Saved[characterGuid] = json;
    }

    /// <summary>A scripted session of the BSD mage with five worn pieces whose appraisal says what the test wants (nothing by default).</summary>
    private sealed class ArmourLedgerRig
    {
        private int _answered;

        public ArmourLedgerRig(bool answerAppraisals = true, BuffOptions? options = null)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            Options = options ?? new BuffOptions();
            AnswerAppraisals = answerAppraisals;
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffOptions Options { get; }

        public bool AnswerAppraisals { get; set; }

        /// <summary>What ACE says each piece wears (spell ids); a piece not listed wears nothing.</summary>
        public Dictionary<uint, uint[]> Worn { get; } = [];

        public double Now => R.H.R.Clock.Now;

        public uint Character => R.H.State.Self.Guid;

        public int Identifies => R.H.Net.Of(FakeSendKind.Identify).Count;

        public BuffCycle Cycle(ArmourBuffLedger? ledger, IArmourLedgerStore? store = null) => new(R.Api, R.H.State, Caster.Table, Options, R.H.Log, ledger, store);

        public BuffStatus Frame(BuffCycle cycle, double dt = 0.25)
        {
            R.Tick(dt);
            Caster.Pump();
            AnswerIdentifies();
            R.Tick();
            return cycle.Step(Now);
        }

        public BuffStatus Run(BuffCycle cycle, double maxSeconds = 4000)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                status = Frame(cycle);
            }

            return status;
        }

        /// <summary>Runs frames until the cycle has left the seed stage (the appraisals are in the ledger), without letting the pass go further than its first step.</summary>
        public void RunSeed(BuffCycle cycle)
        {
            for (var i = 0; i < 200 && cycle.Stage is BuffCycle.StageIdle or BuffCycle.StageSeed && cycle.Status == BuffStatus.Running; i++)
            {
                Frame(cycle);
            }
        }

        private void AnswerIdentifies()
        {
            var sends = R.H.Net.Of(FakeSendKind.Identify);
            for (; _answered < sends.Count; _answered++)
            {
                if (!AnswerAppraisals)
                {
                    continue;
                }

                var guid = sends[_answered].Guid;
                var book = Worn.TryGetValue(guid, out var ids) ? ids.Select(i => i | 0x80000000u).ToArray() : [];
                R.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, new AceAppraisal { Success = true, SpellBook = book }));
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ the ledger

    [Fact]
    public void A_recorded_line_has_its_remaining_seconds_until_the_cast_duration_ends_and_nothing_before_or_after()
    {
        var ledger = new ArmourBuffLedger();
        Assert.Equal(0, ledger.Remaining(PieceA, Impen, 10)); // nothing recorded
        Assert.Equal(0, ledger.Count);

        ledger.Record(PieceA, Impen, 111, doneAt: 100, duration: 1800);
        Assert.Equal(1, ledger.Count);
        Assert.Equal(1800, ledger.Remaining(PieceA, Impen, 100), 3);
        Assert.Equal(1500, ledger.Remaining(PieceA, Impen, 400), 3);
        Assert.Equal(0, ledger.Remaining(PieceA, Impen, 1900)); // ended exactly
        Assert.Equal(0, ledger.Remaining(PieceA, Impen, 5000)); // long ended
        Assert.Equal(0, ledger.Remaining(PieceA, Bane, 400)); // another line
        Assert.Equal(0, ledger.Remaining(PieceB, Impen, 400)); // another piece
        Assert.True(ledger.TryGet(PieceA, Impen, out var entry));
        Assert.Equal(111u, entry.SpellId);
        Assert.True(entry.TimeKnown);

        // A second record of the line replaces the first (a recast).
        ledger.Record(PieceA, Impen, 112, doneAt: 400, duration: 1800);
        Assert.Equal(1, ledger.Count);
        Assert.Equal(1800, ledger.Remaining(PieceA, Impen, 400), 3);
        Assert.Equal(112u, ledger.TryGet(PieceA, Impen, out var recast) ? recast.SpellId : 0u);
    }

    [Fact]
    public void A_line_found_by_an_appraisal_is_present_with_an_unknown_time_and_a_restored_one_has_its_seconds()
    {
        var ledger = new ArmourBuffLedger();
        ledger.RecordPresent(PieceA, Impen, 111);
        Assert.True(double.IsNaN(ledger.Remaining(PieceA, Impen, 50))); // present, time unknown
        Assert.True(ledger.TryGet(PieceA, Impen, out var entry));
        Assert.False(entry.TimeKnown);

        ledger.RecordRemaining(PieceB, Bane, 222, remaining: 640, now: 30);
        Assert.Equal(640, ledger.Remaining(PieceB, Bane, 30), 3);
        Assert.Equal(540, ledger.Remaining(PieceB, Bane, 130), 3);
        Assert.Equal(0, ledger.Remaining(PieceB, Bane, 700));
    }

    [Fact]
    public void Forget_drops_a_piece_or_one_of_its_lines_and_prune_drops_every_piece_no_longer_worn()
    {
        var ledger = new ArmourBuffLedger();
        ledger.Record(PieceA, Impen, 1, 0, 100);
        ledger.Record(PieceA, Bane, 2, 0, 100);
        ledger.Record(PieceB, Impen, 1, 0, 100);

        ledger.Forget(PieceA, Bane);
        Assert.False(ledger.TryGet(PieceA, Bane, out _));
        Assert.True(ledger.TryGet(PieceA, Impen, out _));

        ledger.Forget(PieceA);
        Assert.False(ledger.TryGet(PieceA, Impen, out _));
        Assert.Equal(1, ledger.Count);

        Assert.Equal(0, ledger.Prune([PieceB, 0x80000999u])); // still worn: kept
        Assert.Equal(1, ledger.Prune([0x80000999u])); // taken off: forgotten
        Assert.Equal(0, ledger.Count);
        Assert.Equal(0, ledger.Prune([])); // nothing left to forget
    }

    [Fact]
    public void The_snapshot_keeps_the_known_seconds_left_only_and_refuses_another_character_a_bad_version_and_bad_text()
    {
        var ledger = new ArmourBuffLedger();
        ledger.Record(PieceA, Impen, 1, doneAt: 0, duration: 1000);
        ledger.Record(PieceA, Bane, 2, doneAt: 0, duration: 50); // ended by the log-off
        ledger.RecordPresent(PieceB, Impen, 3); // unknown: nothing to keep
        ledger.Record(PieceB, Bane, 4, doneAt: 100, duration: 900);

        var json = ledger.Export(0x50000001, now: 400);
        var saved = ArmourBuffLedger.ReadSnapshot(json, 0x50000001, out var why);
        Assert.Null(why);
        Assert.Equal(2, saved.Count);
        Assert.Equal((PieceA, Impen, 1u, 600.0), saved[0]);
        Assert.Equal((PieceB, Bane, 4u, 600.0), saved[1]);

        Assert.Empty(ArmourBuffLedger.ReadSnapshot(json, 0x50000002, out why));
        Assert.Contains("another character", why, StringComparison.Ordinal);
        Assert.Empty(ArmourBuffLedger.ReadSnapshot(json.Replace("\"Version\":1", "\"Version\":9", StringComparison.Ordinal), 0x50000001, out why));
        Assert.Contains("version", why, StringComparison.Ordinal);
        Assert.Empty(ArmourBuffLedger.ReadSnapshot("{not json", 0x50000001, out why));
        Assert.Contains("not valid JSON", why, StringComparison.Ordinal);
        Assert.Empty(ArmourBuffLedger.ReadSnapshot(null, 0x50000001, out why));
        Assert.Contains("nothing was saved", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ledger_belongs_to_a_session_state_and_asks_for_one_appraisal_until_it_is_marked()
    {
        var a = new WorldActionRig();
        var b = new WorldActionRig();
        var ledger = ArmourBuffLedger.For(a.H.State);
        Assert.Same(ledger, ArmourBuffLedger.For(a.H.State));
        Assert.NotSame(ledger, ArmourBuffLedger.For(b.H.State)); // a login makes a new state, so a new, empty ledger

        Assert.True(ledger.NeedsSeed);
        ledger.MarkSeeded();
        Assert.False(ledger.NeedsSeed);
        Assert.True(ledger.Seeded);

        var withLines = new ArmourBuffLedger();
        withLines.Record(PieceA, Impen, 1, 0, 10);
        Assert.False(withLines.NeedsSeed); // it already knows something
    }

    // ------------------------------------------------------------------------------------------------ the cycle on the ledger

    [Fact]
    public void A_second_cycle_object_on_the_same_ledger_casts_nothing_for_a_known_armour_line()
    {
        var t = new ArmourLedgerRig();
        var ledger = new ArmourBuffLedger();
        var first = t.Cycle(ledger);
        Assert.True(first.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(first));
        Assert.Equal(2, first.Result!.ArmourCast); // both armour lines, on all five pieces
        Assert.Equal(ScriptedCaster.PieceNames.Length * 2, ledger.Count);
        Assert.Equal(ScriptedCaster.PieceNames.Length, t.Identifies); // the empty ledger was seeded once: one appraisal per worn piece

        var answered = t.Caster.Answered.Count;
        var second = t.Cycle(ledger);
        Assert.True(second.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(second));
        Assert.Equal(0, second.Result!.Cast); // every self line from the registry, every armour line from the ledger
        Assert.Equal(0, second.Result.ArmourCast);
        Assert.Equal(answered, t.Caster.Answered.Count);
        Assert.Equal(24, second.Result.Active);
        Assert.Equal(ScriptedCaster.PieceNames.Length, t.Identifies); // not seeded again

        // A cycle without a ledger of its own to share keeps a private one: it knows nothing, so it recasts both armour lines (the cycle tests' behaviour).
        var third = t.Cycle(null);
        Assert.True(third.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(third));
        Assert.Equal(2, third.Result!.ArmourCast);
        Assert.Equal(ScriptedCaster.PieceNames.Length, t.Identifies); // and appraised nothing
    }

    [Fact]
    public void A_piece_taken_off_forgets_its_lines_and_the_others_keep_theirs()
    {
        var t = new ArmourLedgerRig();
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        Assert.True(cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(cycle));
        var gone = t.Caster.PieceGuids[0];
        Assert.True(ledger.TryGet(gone, Impen, out _));
        var before = t.R.H.State.Self.Inventory.Equipped.Count;

        t.R.Raw(AceWireWriters.ObjectDelete(gone, 1));
        Assert.Equal(before - 1, t.R.H.State.Self.Inventory.Equipped.Count);

        var second = t.Cycle(ledger);
        var due = second.RebuffDue(t.Now, 300); // reads the worn pieces, which prunes the ledger
        Assert.False(ledger.TryGet(gone, Impen, out _));
        Assert.False(ledger.TryGet(gone, Bane, out _));
        Assert.True(ledger.TryGet(t.Caster.PieceGuids[1], Impen, out _));
        Assert.Equal((ScriptedCaster.PieceNames.Length - 1) * 2, ledger.Count);
        Assert.DoesNotContain(Impen, due); // the four pieces left still hold it
    }

    // ------------------------------------------------------------------------------------------------ the appraisal that seeds an empty ledger

    [Fact]
    public void An_empty_ledger_after_a_login_appraises_each_worn_piece_once_and_a_line_on_every_piece_counts_present_with_no_time_left()
    {
        var t = new ArmourLedgerRig();
        var impen = IdsOf(Impen);
        var bane = IdsOf(Bane);
        foreach (var piece in t.Caster.PieceGuids)
        {
            t.Worn[piece] = [impen[3], bane[2]];
        }

        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        Assert.True(cycle.Start(t.Now));
        Assert.Equal(BuffCycle.StageSeed, cycle.Stage);
        t.RunSeed(cycle);

        Assert.Equal(ScriptedCaster.PieceNames.Length, t.Identifies);
        Assert.Equal(t.Caster.PieceGuids.Order(), t.R.H.Net.Of(FakeSendKind.Identify).Select(s => s.Guid).Order()); // each worn piece, once
        Assert.True(ledger.Seeded);
        foreach (var piece in t.Caster.PieceGuids)
        {
            Assert.True(ledger.TryGet(piece, Impen, out var i));
            Assert.Equal(impen[3], i.SpellId); // the line's own level that the piece's spell is
            Assert.False(i.TimeKnown);
            Assert.True(double.IsNaN(ledger.Remaining(piece, Impen, t.Now)));
            Assert.Equal(bane[2], ledger.TryGet(piece, Bane, out var b) ? b.SpellId : 0u);
        }

        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("appraised 5 pieces, 10 lines present on 5 of them, 0 with a restored time left", StringComparison.Ordinal));
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("no saved time is due at once (the conservative rule)", StringComparison.Ordinal));

        // The conservative rule: a line that is present with an unknown time is due at once, so the pass casts both armour lines (and every self line) and the ledger then holds known times.
        Assert.Equal(BuffStatus.Done, t.Run(cycle));
        Assert.Equal(2, cycle.Result!.ArmourCast);
        Assert.Equal(24, cycle.Result.Active);
        Assert.True(ledger.Remaining(t.Caster.PieceGuids[0], Impen, t.Now) > 1000);
    }

    [Fact]
    public void A_stronger_spell_of_the_same_category_counts_a_weaker_or_another_category_does_not_and_a_line_missing_on_one_piece_is_not_active()
    {
        var t = new ArmourLedgerRig();
        var impen = IdsOf(Impen);
        var bane = IdsOf(Bane);
        var pieces = t.Caster.PieceGuids;

        // Impenetrability's top level on four pieces and nothing on the fifth; Bludgeon Bane's lowest level everywhere, so the line is present at level I only.
        foreach (var piece in pieces.Take(4))
        {
            t.Worn[piece] = [impen[^1], bane[0]];
        }

        t.Worn[pieces[4]] = [bane[0]];

        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        Assert.True(cycle.Start(t.Now));
        t.RunSeed(cycle);

        Assert.Equal(impen[^1], ledger.TryGet(pieces[0], Impen, out var top) ? top.SpellId : 0u); // the highest level the piece's enchantment covers
        Assert.False(ledger.TryGet(pieces[4], Impen, out _)); // not on the fifth
        Assert.Equal(bane[0], ledger.TryGet(pieces[4], Bane, out var low) ? low.SpellId : 0u); // level I covers level I only
        Assert.All(pieces, p => Assert.True(ledger.TryGet(p, Bane, out _)));
        Assert.Same(ledger, cycle.Ledger);

        // A piece that wears another category's spell (Bludgeon Bane's, asked for as Impenetrability) is not the line.
        var other = new ArmourLedgerRig();
        foreach (var piece in other.Caster.PieceGuids)
        {
            other.Worn[piece] = [bane[^1]];
        }

        var otherLedger = new ArmourBuffLedger();
        var otherCycle = other.Cycle(otherLedger);
        Assert.True(otherCycle.Start(other.Now));
        other.RunSeed(otherCycle);
        Assert.All(other.Caster.PieceGuids, p => Assert.False(otherLedger.TryGet(p, Impen, out _)));
        Assert.All(other.Caster.PieceGuids, p => Assert.Equal(bane[^1], otherLedger.TryGet(p, Bane, out var e) ? e.SpellId : 0u));
    }

    [Fact]
    public void An_appraisal_that_never_answers_leaves_the_pieces_unknown_and_the_pass_goes_on_and_casts_both_armour_lines()
    {
        var t = new ArmourLedgerRig(answerAppraisals: false);
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger);
        Assert.True(cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(cycle));
        Assert.Equal(2, cycle.Result!.ArmourCast);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("gave no tables", StringComparison.Ordinal));
        Assert.True(ledger.Seeded);
    }

    [Fact]
    public void The_saved_seconds_are_restored_only_for_a_piece_whose_appraisal_still_shows_the_same_spell_and_a_line_missing_there_is_cast()
    {
        // Session one: the whole cycle, then the log-off keeps the ledger's seconds in the store.
        var first = new ArmourLedgerRig();
        var firstLedger = new ArmourBuffLedger();
        var cycle = first.Cycle(firstLedger);
        Assert.True(cycle.Start(first.Now));
        Assert.Equal(BuffStatus.Done, first.Run(cycle));
        var store = new MemoryStore();
        store.Save(first.Character, firstLedger.Export(first.Character, first.Now));
        var pieces = first.Caster.PieceGuids;
        Assert.True(firstLedger.TryGet(pieces[0], Impen, out var castImpen));
        Assert.True(firstLedger.TryGet(pieces[0], Bane, out var castBane));
        var savedImpen = firstLedger.Remaining(pieces[0], Impen, first.Now);
        var duration = first.Caster.Table[castImpen.SpellId].Duration;
        Assert.InRange(savedImpen, duration - 300, duration);

        // Session two (a new login: a new state and an empty ledger). ACE's clock for the items stood still: the same spells are on the pieces, except Impenetrability on the first one.
        var second = new ArmourLedgerRig();
        foreach (var piece in pieces)
        {
            second.Worn[piece] = piece == pieces[0] ? [castBane.SpellId] : [castImpen.SpellId, castBane.SpellId];
        }

        var ledger = new ArmourBuffLedger();
        var restored = second.Cycle(ledger, store);
        var guid = second.Character;
        Assert.Equal(first.Character, guid);
        Assert.True(restored.Start(second.Now));
        second.RunSeed(restored);

        Assert.Equal(1, store.Loads);
        Assert.Contains(second.R.H.Log.Lines, l => l.Contains("9 lines present on 5 of them, 9 with a restored time left", StringComparison.Ordinal));
        foreach (var piece in pieces)
        {
            Assert.InRange(ledger.Remaining(piece, Bane, second.Now), savedImpen - 400, duration); // known seconds, within what the first session had left (its casts were a little apart)
        }

        Assert.False(ledger.TryGet(pieces[0], Impen, out _)); // the appraisal does not show it on this piece: nothing to restore
        Assert.InRange(ledger.Remaining(pieces[1], Impen, second.Now), savedImpen - 400, duration);
        Assert.True(ledger.TryGet(pieces[1], Impen, out var again) && again.TimeKnown);

        // The pass casts Impenetrability (one piece lacks it) and not Bludgeon Bane (known on all five with time left); the self lines are cast as the registry is empty.
        Assert.Equal(BuffStatus.Done, second.Run(restored));
        Assert.Equal(1, restored.Result!.ArmourCast);
        Assert.Contains(second.Caster.Landed, id => IdsOf(Impen).Contains(id));
        Assert.DoesNotContain(second.Caster.Landed, id => IdsOf(Bane).Contains(id));
    }

    [Fact]
    public void A_saved_time_of_another_character_or_of_a_spell_no_longer_on_the_piece_is_not_used()
    {
        var t = new ArmourLedgerRig();
        var impen = IdsOf(Impen);
        var pieces = t.Caster.PieceGuids;
        foreach (var piece in pieces)
        {
            t.Worn[piece] = [impen[4]]; // a stronger version than the one saved
        }

        var saved = new ArmourBuffLedger();
        foreach (var piece in pieces)
        {
            saved.Record(piece, Impen, impen[2], doneAt: 0, duration: 1800);
        }

        var store = new MemoryStore();
        store.Save(t.Character, saved.Export(t.Character, now: 100)); // 1700 s left, of the spell level III
        var ledger = new ArmourBuffLedger();
        var cycle = t.Cycle(ledger, store);
        Assert.True(cycle.Start(t.Now));
        t.RunSeed(cycle);
        foreach (var piece in pieces)
        {
            Assert.True(ledger.TryGet(piece, Impen, out var e));
            Assert.False(e.TimeKnown); // the stronger spell is on the piece, but how long it has is not the saved spell's time
            Assert.Equal(impen[4], e.SpellId);
        }

        // Another character's snapshot is ignored with the reason logged.
        var u = new ArmourLedgerRig();
        foreach (var piece in pieces)
        {
            u.Worn[piece] = [impen[2]];
        }

        var otherStore = new MemoryStore();
        otherStore.Save(u.Character, saved.Export(u.Character + 1, now: 100));
        var otherLedger = new ArmourBuffLedger();
        var otherCycle = u.Cycle(otherLedger, otherStore);
        Assert.True(otherCycle.Start(u.Now));
        u.RunSeed(otherCycle);
        Assert.False(otherLedger.TryGet(pieces[0], Impen, out var o) && o.TimeKnown);
        Assert.Contains(u.R.H.Log.Lines, l => l.Contains("no saved time left (the saved text is of another character)", StringComparison.Ordinal));
    }
}
