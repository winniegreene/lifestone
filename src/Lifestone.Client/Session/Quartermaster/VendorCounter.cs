using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>What a caller asks of <see cref="VendorCounter.Buy"/>: one ware of an open shop, by weenie class.</summary>
/// <param name="Vendor">The vendor's guid; his shop is open (the caller met him and opened it).</param>
/// <param name="Wcid">The ware's weenie class.</param>
/// <param name="Count">How many.</param>
public sealed record VendorBuyRequest(uint Vendor, uint Wcid, int Count)
{
    /// <summary>The vendor's name, for the counter's lines.</summary>
    public string VendorName { get; init; } = "the vendor";

    /// <summary>The ware's name for the counter's lines; null: the shop row's name.</summary>
    public string? WareName { get; init; }

    /// <summary>True: fewer in the shop than asked buys what it holds, and a short purse buys what the coin pays (the town trip's lines). False: either is a refusal and nothing is sent.</summary>
    public bool PartialAllowed { get; init; }

    /// <summary>True (the default): when the coin is short a trade note she holds is changed at this counter first, the main pack's before a side pack's of the same face (the town trip's rule, m6-plan.md D99: <see cref="TownTripPurse.Pick"/>; M6 16 E5a2: a side pack's too).</summary>
    public bool ChangeNotes { get; init; } = true;

    /// <summary>True: the bought stack is merged into the held stacks of its weenie class after it arrives (ACE never joins a bought stack to a held one, <c>Player_Commerce.cs</c> 59-102 @95ef426d).</summary>
    public bool MergeAfter { get; init; }

    /// <summary>The catalogue's price of one, or null: a wire price that differs is named once (<see cref="VendorCounterHost.PriceDiffers"/>) and the wire's is paid.</summary>
    public int? CataloguePrice { get; init; }

    /// <summary>True when the ware takes a container slot (a side pack), not a main pack slot: the check before reads <see cref="PackLayout.ContainerRoom"/>.</summary>
    public bool ContainerSlot { get; init; }

    /// <summary>True (the default): the check after wants the coin fallen below what it was at the send, and waits for it with the ware. False: what the trade's own answer says was paid is enough (the town trip).</summary>
    public bool CoinMustFall { get; init; } = true;

    /// <summary>The seconds the ware (and the coin) have to show in the read model after the server's answer.</summary>
    public double ArrivalSeconds { get; init; } = VendorCounter.ArrivalSeconds;

    /// <summary>The trade notes read first (a ground's <see cref="GroundPurse.Notes"/>), or null: ACE's ten in their own order.</summary>
    public IReadOnlyList<PurseNote>? NotesFirst { get; init; }
}

/// <summary>
/// What <see cref="VendorCounter.Buy"/> did; the caller makes it and reads it when the coroutine ends. A refusal is one token: the check before's (<c>vendor-closed</c>, <c>not-sold</c>, <c>stock-short</c>, <c>purse-unread</c>,
/// <c>purse-short</c>, <c>pack-full</c>: <see cref="CheckedBuy.Judge"/>), <c>purse-short</c> again at the counter when a note could not be changed, <see cref="VendorCounter.Refused"/> when the server refused the buy and
/// <see cref="VendorCounter.AfterCheck"/> when the server confirmed it but the ware or the payment did not show.
/// </summary>
public sealed class VendorBuyResult
{
    /// <summary>How many the server confirmed (0 until then; kept when the check after fails).</summary>
    public int Bought { get; internal set; }

    /// <summary>The pyreals paid by the trade's own answer (the coin before the send less the coin at the answer).</summary>
    public long Paid { get; internal set; }

    /// <summary>The refusal token, or null when the buy was confirmed and the check after held.</summary>
    public string? Refusal { get; internal set; }

    /// <summary>The reason in words, with its numbers.</summary>
    public string Why { get; internal set; } = "not judged";

    /// <summary>The shop's row, or null on a refusal before it.</summary>
    public VendorStockEntry? Entry { get; internal set; }

    /// <summary>The wire price of one, or 0.</summary>
    public uint Price { get; internal set; }

    /// <summary>The wire price of the count judged, or 0.</summary>
    public long Cost { get; internal set; }

    /// <summary>The count judged, then the count sent (a partial buy's is smaller than the count asked).</summary>
    public int Count { get; internal set; }

    /// <summary>The coin at the send (after any note was changed); on a refusal before the send, the coin last read.</summary>
    public long CoinBefore { get; internal set; }

    /// <summary>The coin at the check after (the coin last read until then).</summary>
    public long CoinAfter { get; internal set; }

    /// <summary>How many of the ware she held at the send (every place a ware can be held).</summary>
    public int HeldBefore { get; internal set; }

    /// <summary>How many she held at the check after.</summary>
    public int HeldAfter { get; internal set; }

    /// <summary>True when the server confirmed the buy and the ware had not shown in the read model by the check after.</summary>
    public bool Unseen { get; internal set; }

    /// <summary>The trade notes changed to coin for this buy.</summary>
    public int NotesChanged { get; internal set; }

    /// <summary>True when the wire price differed from <see cref="VendorBuyRequest.CataloguePrice"/>.</summary>
    public bool PriceDiffers { get; internal set; }

    /// <summary>The buy's outcome in one phrase, or null when nothing was sent.</summary>
    public string? Outcome { get; internal set; }

    /// <summary>The detail of the server's answer to the buy (its refusal text), or null.</summary>
    public string? Detail { get; internal set; }

    /// <summary>True when the buy was sent, confirmed and seen: nothing refused.</summary>
    public bool Ok => Refusal is null && Bought > 0;

    internal void Refuse(string token, string why)
    {
        Refusal = token;
        Why = why;
    }
}

/// <summary>
/// The caller's side of <see cref="VendorCounter.Buy"/>: its log, its waits (a leg's are held with the leg, <c>ScriptLeg.Until</c>), its read of the main pack's free slots, and the lines the counter says through it. The default
/// lines carry the caller's <see cref="Prefix"/>; a caller with lines of its own (the town trip) overrides them.
/// </summary>
public class VendorCounterHost
{
    private readonly Action<string> _log;
    private readonly Func<Func<bool>, double, IEnumerable<ActionHandle?>> _until;
    private readonly Func<int> _mainRoom;

    /// <summary>Creates the host.</summary>
    /// <param name="prefix">The first word of the default lines (<c>buy</c>, <c>buy-trip</c>, <c>lockpick</c>).</param>
    /// <param name="log">The caller's log.</param>
    /// <param name="until">The caller's wait: until the condition holds or the seconds passed.</param>
    /// <param name="mainRoom">The main pack's free slots now (the quartermaster's view, M6 16 E5a; until then the caller's own read).</param>
    public VendorCounterHost(string prefix, Action<string> log, Func<Func<bool>, double, IEnumerable<ActionHandle?>> until, Func<int> mainRoom)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(until);
        ArgumentNullException.ThrowIfNull(mainRoom);
        Prefix = prefix;
        _log = log;
        _until = until;
        _mainRoom = mainRoom;
    }

    /// <summary>The first word of the default lines.</summary>
    public string Prefix { get; }

    /// <summary>Called once with the check before's answer, before anything is sent: the caller's own check-before line.</summary>
    public Action<CheckedBuy.Verdict>? CheckBefore { get; init; }

    /// <summary>The caller's line for a wire price (first) that differs from the catalogue's (second), or null for the default line.</summary>
    public Func<uint, int, string>? PriceLine { get; init; }

    /// <summary>The main pack's free slots now.</summary>
    internal int MainRoom => _mainRoom();

    /// <summary>A wait on the session clock alone, for a caller that is no leg.</summary>
    /// <param name="session">The session.</param>
    public static Func<Func<bool>, double, IEnumerable<ActionHandle?>> ClockUntil(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Wait;

        IEnumerable<ActionHandle?> Wait(Func<bool> done, double max)
        {
            var until = session.Clock.Now + max;
            while (!done() && session.Clock.Now < until)
            {
                yield return null;
            }
        }
    }

    /// <summary>Writes a line to the caller's log.</summary>
    /// <param name="text">The line.</param>
    protected void Log(string text) => _log(text);

    internal IEnumerable<ActionHandle?> Until(Func<bool> done, double max) => _until(done, max);

    /// <summary>The check before's answer, before anything is sent.</summary>
    /// <param name="verdict">The answer.</param>
    public virtual void Judged(CheckedBuy.Verdict verdict) => CheckBefore?.Invoke(verdict);

    /// <summary>The wire price differs from the catalogue's: said once the row and the stock are judged, whatever the purse and the pack say, before <see cref="Judged"/>.</summary>
    /// <param name="wire">The wire price of one.</param>
    /// <param name="catalogue">The catalogue's.</param>
    public virtual void PriceDiffers(uint wire, int catalogue) =>
        Log(PriceLine?.Invoke(wire, catalogue) ?? Text($"{Prefix}: the wire price {wire} differs from the catalogue's {catalogue}: the wire wins"));

    /// <summary>One note was split off its stack to be changed.</summary>
    /// <param name="note">The note.</param>
    /// <param name="one">The split note's guid.</param>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="size">The stack's size before the split.</param>
    public virtual void NoteSplit(PurseNote note, uint one, uint stack, int size)
    {
        ArgumentNullException.ThrowIfNull(note);
        Log(Text($"{Prefix}: purse: split one {note.Name} 0x{one:X8} from its stack 0x{stack:X8} (x{size}) to change it"));
    }

    /// <summary>M6 16 E5a2: the note to change lies inside a side pack: said before it is split or sold there.</summary>
    /// <param name="note">The note.</param>
    /// <param name="stack">The stack's guid.</param>
    /// <param name="size">The stack's size.</param>
    /// <param name="pack">The side pack that holds it.</param>
    /// <param name="into">Where one note of a stack is split off to: 0 the main pack, else a side pack; null for a single note, sold where it lies.</param>
    public virtual void NoteInSidePack(PurseNote note, uint stack, int size, uint pack, uint? into)
    {
        ArgumentNullException.ThrowIfNull(note);
        var how = into is not { } target ? "a single note, sold where it lies"
            : target == 0 ? "one note is split off into the main pack"
            : Text($"the main pack has no free slot, so one note is split off into the side pack 0x{target:X8}");
        Log(Text($"{Prefix}: purse: {note.Name} 0x{stack:X8} (x{size}) lies in the side pack 0x{pack:X8}: {how}"));
    }

    /// <summary>A note was changed to coin at the counter.</summary>
    /// <param name="note">The note.</param>
    /// <param name="vendor">The vendor's name.</param>
    /// <param name="paid">What the vendor paid.</param>
    /// <param name="before">The coin before.</param>
    /// <param name="after">The coin after.</param>
    public virtual void NoteChanged(PurseNote note, string vendor, long paid, long before, long after)
    {
        ArgumentNullException.ThrowIfNull(note);
        Log(Text($"{Prefix}: purse: the coin was short: changed {note.Name} to coin at {vendor}: paid {paid} pyreals (the coin {before} -> {after})"));
    }

    /// <summary>A note could not be changed.</summary>
    /// <param name="kind">The refusal's kind (<c>pack</c>, <c>coin</c>, <c>unsellable</c>, <c>stock</c>, <c>other</c>: <c>ResupplyRun.Kind</c>).</param>
    /// <param name="what">The refusal in words.</param>
    public virtual void NoteRefused(string kind, string what) => Log(Text($"{Prefix}: purse: refused: {kind}: {what}"));

    /// <summary>A partial buy's purse is short after the notes: it buys what the coin pays (<paramref name="affordable"/>, which may be none).</summary>
    /// <param name="ware">The ware's name.</param>
    /// <param name="cost">The cost of <paramref name="count"/>.</param>
    /// <param name="coins">The coin.</param>
    /// <param name="count">The count wanted.</param>
    /// <param name="affordable">The count the coin pays.</param>
    /// <param name="noNote">Why no note covered the rest.</param>
    public virtual void PurseShort(string ware, long cost, long coins, int count, int affordable, string noNote) =>
        Log(Text($"{Prefix}: purse: short by {cost - coins} pyreals for {ware} (the {count} cost {cost}, the coin is {coins} and {noNote}): buys {affordable}"));

    /// <summary>The server confirmed the buy.</summary>
    /// <param name="count">How many.</param>
    /// <param name="paid">The pyreals paid by the trade's answer.</param>
    public virtual void Bought(int count, long paid)
    {
    }

    /// <summary>A stack is about to be merged into another.</summary>
    /// <param name="bought">True after a buy; false for held stacks.</param>
    /// <param name="name">The ware's name.</param>
    /// <param name="from">The source stack.</param>
    /// <param name="to">The target stack.</param>
    /// <param name="amount">How many move.</param>
    public virtual void Merging(bool bought, string name, uint from, uint to, int amount) =>
        Log(Text($"{Prefix}: merging a {(bought ? "bought" : "held")} {name} stack 0x{from:X8} into 0x{to:X8}: x{amount}"));

    /// <summary>A merge ended.</summary>
    /// <param name="confirmed">True when the server confirmed it.</param>
    /// <param name="name">The ware's name.</param>
    /// <param name="from">The source stack.</param>
    /// <param name="to">The target stack.</param>
    /// <param name="outcome">The merge's outcome in one phrase.</param>
    public virtual void Merged(bool confirmed, string name, uint from, uint to, string outcome)
    {
        if (!confirmed)
        {
            Log(Text($"{Prefix}: the merge of {name} 0x{from:X8} into 0x{to:X8} ended {outcome}; the stack stays, going on"));
        }
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// M6 item 16, slice E5b (docs/automation-engine.md section 2.5; m6-plan.md D139): the vendor counter, the one buy for every caller. A buy from a vendor was written once a caller (the town trip, the quest step, the buy-trip, the
/// lockpick buy), and only the town trip's changed a trade note when the coin was short. This is the town trip's buy, as one coroutine a caller yields from; the caller keeps what is its own: meeting the vendor, opening the
/// shop, its plan, its counters, its refusal names and its lines.
/// <para><b>Check, act, check (M5 D141).</b></para>
/// <list type="number">
/// <item>Before (<see cref="CheckedBuy.Judge"/>, pure): the shop open and the vendor's, the row by weenie class, the stock, the purse (the coin and, when notes may be changed, the faces of every trade note she holds at a shop
/// whose mask takes one), the slot. The purse is read once, by the town trip's read (<see cref="PurseNotes.Read"/>).</item>
/// <item>The notes: while the coin is short, the note <see cref="TownTripPurse.Pick"/> names (the smallest face that covers the rest, else the largest; the main pack's before a side pack's of the same face) is changed to
/// coin at this counter: one note of a stack is split off first, for ACE sells the whole object it is handed (<c>Player_Commerce.cs</c> 232-298 @95ef426d), and a vendor pays a note at its face (<c>Vendor.GetBuyCost</c>,
/// <c>Vendor.cs</c> 591-599). M6 16 E5a2: a note inside a side pack is changed where it lies (<c>ChangeNote</c>). A refused change ends the notes.</item>
/// <item>The purse at the counter: still short, a partial buy takes what the coin pays (the price is by stack, so it is searched), any other buy is <c>purse-short</c> and nothing is sent.</item>
/// <item>The act: one <see cref="IActionApi.Buy"/>. A refusal is <see cref="Refused"/>, never retried.</item>
/// <item>After: the ware is waited for in every place a ware can be held (<see cref="GearInventory.CountOf"/>), merged when asked, and checked: held up by the count, and paid.</item>
/// </list>
/// The melee and archer paths (<c>HuntScenario.Vendor</c>) do not come here (docs/automation-engine.md section 2).
/// </summary>
public static class VendorCounter
{
    /// <summary>The refusal token of a buy the server refused.</summary>
    public const string Refused = "refused";

    /// <summary>The refusal token of a buy the server confirmed whose ware or payment did not show.</summary>
    public const string AfterCheck = "after-check";

    /// <summary>How long a paid ware may take to reach the read model (M6 7 AX: ACE's answer and the ware's F745 and 0022 are separate sends; the town trip's 10 s).</summary>
    public const double ArrivalSeconds = 10;

    /// <summary>The seconds a note's split, and its payout, have to show after the server's answer (the town trip's 2 s).</summary>
    public const double NoteSeconds = 2;

    /// <summary>The most merges after one buy.</summary>
    public const int MergeRounds = 12;

    /// <summary>Buys <paramref name="request"/> at the open shop (see the class): yields the handle to wait for, or null to wait a frame; <paramref name="result"/> holds the answer when it ends.</summary>
    /// <param name="session">The session.</param>
    /// <param name="request">What to buy.</param>
    /// <param name="result">Filled as the buy goes.</param>
    /// <param name="host">The caller's log, waits and lines.</param>
    public static IEnumerable<ActionHandle?> Buy(IGameSessionView session, VendorBuyRequest request, VendorBuyResult result, VendorCounterHost host)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(host);
        return Run(session, request, result, host);
    }

    private static IEnumerable<ActionHandle?> Run(IGameSessionView session, VendorBuyRequest request, VendorBuyResult result, VendorCounterHost host)
    {
        // Check before: the one read of the purse, the pure judge.
        var purse = PurseNotes.Read(session.State, request.NotesFirst);
        var verdict = CheckedBuy.Judge(
            session.State.Vendor,
            request.Vendor,
            request.Wcid,
            request.Count,
            purse.Coin,
            host.MainRoom,
            request.ChangeNotes ? purse.NotesWorth : 0,
            request.PartialAllowed,
            request.ContainerSlot ? PackLayout.ContainerRoom(session.State) : null);
        result.Entry = verdict.Entry;
        result.Price = verdict.Price;
        result.Cost = verdict.Cost;
        result.Count = verdict.Count;
        result.CoinBefore = purse.Coin;
        result.CoinAfter = purse.Coin;
        if (request.CataloguePrice is { } catalogue && verdict.Price != 0 && verdict.Price != (uint)catalogue)
        {
            result.PriceDiffers = true;
            host.PriceDiffers(verdict.Price, catalogue);
        }

        host.Judged(verdict);

        if (verdict.Refusal is { } refusal)
        {
            result.Refuse(refusal, verdict.Why);
            yield break;
        }

        var entry = verdict.Entry!;
        var name = request.WareName ?? entry.Name;
        var stock = session.State.Vendor!;
        var count = verdict.Count;
        var cost = verdict.Cost;

        // The notes: one at a time while the coin is short and the shop takes a note.
        if (request.ChangeNotes)
        {
            while (cost > Coins(session) && stock.BuysType(VendorStock.PromissoryNoteType) && NextNote(session, request, cost - Coins(session)) is { } held)
            {
                var changed = false;
                foreach (var h in ChangeNote(session, request, host, held, ok => changed = ok)) { yield return h; }

                if (changed)
                {
                    result.NotesChanged++;
                }

                if (!changed || session.State.Vendor is not { } refreshed || refreshed.Vendor != request.Vendor)
                {
                    break;
                }

                stock = refreshed;
            }
        }

        // The purse at the counter.
        var coins = Coins(session);
        result.CoinBefore = coins;
        result.CoinAfter = coins;
        if (cost > coins)
        {
            var noNote = !request.ChangeNotes ? "no trade note is changed for this buy"
                : session.State.Vendor is { } now && now.Vendor == request.Vendor && !now.BuysType(VendorStock.PromissoryNoteType) ? Text($"{request.VendorName}'s mask takes no trade note") // M6 item 14 (m6-purse): the counter's own reason, by name: a vendor that buys no note changes none
                : PurseNotes.Read(session.State, request.NotesFirst).Notes.Count > 0 ? "a held note was not changed (its refusal is named above)"
                : "no note is left";
            var affordable = request.PartialAllowed ? Affordable(stock, entry, count, coins) : 0;
            if (request.PartialAllowed)
            {
                host.PurseShort(name, cost, coins, count, affordable, noNote);
            }

            if (affordable == 0)
            {
                result.Count = 0;
                result.Refuse("purse-short", Text($"the coin {coins} does not cover {count} {name} at {verdict.Price} each ({cost}): {noNote}"));
                yield break;
            }

            count = affordable;
        }

        // The act.
        var before = Coins(session);
        var scratch = new List<uint>();
        var heldBefore = GearInventory.CountOf(session.State, scratch, request.Wcid);
        result.Count = count;
        result.CoinBefore = before;
        result.CoinAfter = before;
        result.HeldBefore = heldBefore;
        result.HeldAfter = heldBefore;
        var handle = session.Actions.Buy(request.Vendor, [(entry.Guid, (uint)count)], ActionSource.Automation);
        yield return handle;
        result.Outcome = ScriptLeg.Describe(handle);
        result.Detail = handle.Outcome?.Detail;
        if (handle.Outcome is not { Kind: ActionOutcomeKind.Confirmed })
        {
            result.Refuse(Refused, result.Outcome);
            yield break;
        }

        var paid = handle.Result?.Actual ?? Math.Max(0, before - Coins(session));
        result.Bought = count;
        result.Paid = paid;
        host.Bought(count, paid);

        // The ware: waited for, merged when asked.
        var expected = heldBefore + count;
        if (request.MergeAfter)
        {
            foreach (var h in Merge(session, host, request.Wcid, expected, name, request.ArrivalSeconds)) { yield return h; }

            if (request.CoinMustFall)
            {
                foreach (var h in host.Until(() => Coins(session) < before, request.ArrivalSeconds)) { yield return h; }
            }
        }
        else
        {
            foreach (var h in host.Until(() => GearInventory.CountOf(session.State, scratch, request.Wcid) >= expected && (!request.CoinMustFall || Coins(session) < before), request.ArrivalSeconds)) { yield return h; }
        }

        // Check after: every place a ware can be held (a focus comes as ContainerType 2, beside the side packs: M6 7 AX), and the payment.
        var heldAfter = GearInventory.CountOf(session.State, scratch, request.Wcid);
        var after = Coins(session);
        result.HeldAfter = heldAfter;
        result.CoinAfter = after;
        result.Unseen = heldAfter < expected;
        var unpaid = request.CoinMustFall ? after >= before : paid <= 0;
        if (result.Unseen || unpaid)
        {
            result.Refuse(AfterCheck, Text($"after the buy she holds {heldAfter} {name} (was {heldBefore}, want +{count}) and the coin is {after} (was {before}; the trade's answer says {paid} paid)"));
            yield break;
        }

        result.Why = Text($"bought {count} {name} for {paid}: held {heldBefore} -> {heldAfter}, the coin {before} -> {after}{(result.NotesChanged > 0 ? Text($", {result.NotesChanged} trade note(s) changed first") : string.Empty)}");
    }

    /// <summary>
    /// M5 7: ACE never merges a bought stack into a held one (<c>Player_Commerce.cs</c> 59-102 <c>FinalizeBuyTransaction</c> creates each ware as a new object), so every purchase of a stackable would take new pack slots. Waits up to
    /// <paramref name="arrivalSeconds"/> for <paramref name="expectedTotal"/> of the weenie to be held, then merges the smallest held stack into the fullest other one with room (<see cref="StackMerge"/>, GameAction 0x0054) until none
    /// is left to merge, at most <see cref="MergeRounds"/> times; a refused merge leaves that stack. A ware that does not stack does nothing. M5 7 D136: with <paramref name="mainPackTarget"/> (the trade notes, the coin) a stack joins
    /// only a stack in the main pack (D121).
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="host">The caller's waits and lines.</param>
    /// <param name="wcid">The ware's weenie class.</param>
    /// <param name="expectedTotal">How many are held once the bought ones arrived; 0 for held stacks alone.</param>
    /// <param name="name">The ware's name.</param>
    /// <param name="arrivalSeconds">The seconds the ware has to arrive.</param>
    /// <param name="mainPackTarget">True when a merge's target must be in the main pack.</param>
    public static IEnumerable<ActionHandle?> Merge(IGameSessionView session, VendorCounterHost host, uint wcid, int expectedTotal, string name, double arrivalSeconds = ArrivalSeconds, bool mainPackTarget = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(host);
        return Merging(session, host, wcid, expectedTotal, name, arrivalSeconds, mainPackTarget);
    }

    private static IEnumerable<ActionHandle?> Merging(IGameSessionView session, VendorCounterHost host, uint wcid, int expectedTotal, string name, double arrivalSeconds, bool mainPackTarget)
    {
        var scratch = new List<uint>();
        foreach (var h in host.Until(() => GearInventory.CountOf(session.State, scratch, wcid) >= expectedTotal, arrivalSeconds)) { yield return h; }

        var skip = new HashSet<uint>();
        for (var round = 0; round < MergeRounds && StackMerge.TryPlanFor(session.State, wcid, scratch, skip, out var from, out var to, out var amount, mainPackTarget); round++)
        {
            host.Merging(expectedTotal > 0, name, from, to, amount);
            var merge = session.Actions.MergeStack(from, to, amount, ActionSource.Automation);
            yield return merge;
            var confirmed = merge.Outcome is { Kind: ActionOutcomeKind.Confirmed };
            if (!confirmed)
            {
                skip.Add(from);
            }

            host.Merged(confirmed, name, from, to, ScriptLeg.Describe(merge));
        }
    }

    /// <summary>
    /// Changes one trade note to coin at the open vendor (D49: a vendor pays a promissory note at 1.0). ACE's <c>Vendor.GetBuyCost</c> (<c>Vendor.cs</c> 591-599 @95ef426d) pays <c>max(1, floor(rate x Value + 0.1))</c> with the rate
    /// the vendor's BuyPrice (1 when absent) and exactly 1.0 for a promissory note (593-595), so a note's face value is its payout whatever the vendor's own rate.
    /// <para>M5 7 D136: ACE sells the whole object it is handed, whatever the amount (<c>Player_Commerce.cs</c> 232-298 <c>VerifySellItems</c> only checks the amount against the stack; 199-205 removes the item; <c>Vendor.cs</c>
    /// 586-609 pays the stack's whole Value), so a note from a stack of several is first split off into the main pack with GameAction 0x0055 (<see cref="IActionApi.SplitToContainer"/>; ACE <c>Player_Inventory.cs</c> 2243
    /// <c>HandleActionStackableSplitToContainer</c>) and that one note is sold; the rest of the stack stays.</para>
    /// <para>M6 16 E5a2: a note inside a side pack. The sale takes it where it lies: <c>VerifySellItems</c> looks the guid up in <c>GetAllPossessions()</c> (<c>Player_Commerce.cs</c> 234, 242), which holds the contents of every
    /// side pack (<c>Player_Inventory.cs</c> 29-44), and the removal searches the side packs (<c>Container.TryRemoveFromInventory</c>, <c>Container.cs</c> 644-684). The split of one note off a side pack's stack: ACE finds the
    /// stack wherever she holds it (<c>Player_Inventory.cs</c> 2255; <c>FindObject</c> 514-536), makes the split a new object in the container the action names (2256, 2424-2436) and adds it to that container alone, never
    /// spilling to another (2445: <c>TryAddToInventory</c> with the main-pack-only flag; <c>Container.cs</c> 499-549), so the free slot it needs is in the container named. The main pack is named when it has a free slot (the
    /// send of every split before this slice); else the stack's own side pack, else the first other side pack with a free slot; with none the refusal names it and no split is sent. A stack in the main pack keeps its one
    /// rule: the main pack's slot.</para>
    /// </summary>
    private static IEnumerable<ActionHandle?> ChangeNote(IGameSessionView session, VendorBuyRequest request, VendorCounterHost host, PurseNotes.HeldNote held, Action<bool> done)
    {
        var one = held.Guid;
        var size = held.Stack;
        uint into = 0; // where one note of a stack is split off to: 0 is the main pack
        if (held.InSidePack)
        {
            if (size > 1 && host.MainRoom <= 0)
            {
                if (SidePackWithRoom(session, held.Container) is not { } side)
                {
                    host.NoteRefused("pack", Text($"the trade note {held.Note.Name}: one note of the stack 0x{held.Guid:X8} (x{size}) in the side pack 0x{held.Container:X8} cannot be split off: neither the main pack nor a side pack has a free slot"));
                    done(false);
                    yield break;
                }

                into = side;
            }

            host.NoteInSidePack(held.Note, held.Guid, size, held.Container, size > 1 ? into : null);
        }

        if (size > 1)
        {
            if (into == 0 && host.MainRoom <= 0)
            {
                host.NoteRefused("pack", Text($"the trade note {held.Note.Name}: one note of the stack 0x{held.Guid:X8} (x{size}) cannot be split off: the main pack has no free slot"));
                done(false);
                yield break;
            }

            var others = NotesIn(session, into, held.Note.Wcid);
            var split = session.Actions.SplitToContainer(held.Guid, into, 0, 1, ActionSource.Automation);
            yield return split;
            foreach (var h in host.Until(() => NotesIn(session, into, held.Note.Wcid).Except(others).Any(), NoteSeconds)) { yield return h; } // ACE's 0022 confirms the split; the new stack's F745 follows it (the tracker's note)

            one = NotesIn(session, into, held.Note.Wcid).Except(others).FirstOrDefault();
            if (split.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || one == 0)
            {
                host.NoteRefused(ResupplyRun.Kind(split.Outcome?.Detail, "other"), Text($"the trade note {held.Note.Name}: the split of one from 0x{held.Guid:X8} (x{size}) {ScriptLeg.Describe(split)}"));
                done(false);
                yield break;
            }

            host.NoteSplit(held.Note, one, held.Guid, size);
        }

        var before = Coins(session);
        var sell = session.Actions.Sell(request.Vendor, [one], ActionSource.Automation);
        yield return sell;
        foreach (var h in host.Until(() => Coins(session) > before, NoteSeconds)) { yield return h; }

        if (sell.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || session.State.Self.Inventory.Contains(one))
        {
            host.NoteRefused(ResupplyRun.Kind(sell.Outcome?.Detail, "other"), Text($"the trade note {held.Note.Name} at {request.VendorName}: {ScriptLeg.Describe(sell)}"));
            done(false);
            yield break;
        }

        host.NoteChanged(held.Note, request.VendorName, sell.Result?.Actual ?? (Coins(session) - before), before, Coins(session));
        done(true);
    }

    /// <summary>The guids of the items of that weenie class in <paramref name="container"/> (0: the main pack, the stations' read; else a side pack), in its order.</summary>
    private static List<uint> NotesIn(IGameSessionView session, uint container, uint wcid)
    {
        if (container == 0)
        {
            return StationItems.InPackOf(session.State, wcid);
        }

        var list = new List<uint>();
        foreach (var entry in session.State.Self.Inventory.ItemsIn(container))
        {
            if (session.State.Objects.TryGet(entry.Guid, out var o) && o.Weenie.WeenieClassId == wcid)
            {
                list.Add(entry.Guid);
            }
        }

        return list;
    }

    /// <summary>M6 16 E5a2: a side pack with a free slot for one note split off a stack that lies in <paramref name="own"/>: the stack's own pack first, else the first other in slot order; null with none.</summary>
    private static uint? SidePackWithRoom(IGameSessionView session, uint own)
    {
        var packs = PackLayout.SidePacks(session.State);
        foreach (var pack in packs)
        {
            if (pack.Guid == own && pack.Free > 0)
            {
                return pack.Guid;
            }
        }

        foreach (var pack in packs)
        {
            if (pack.Free > 0)
            {
                return pack.Guid;
            }
        }

        return null;
    }

    /// <summary>The held trade note to change for <paramref name="shortfall"/> pyreals: the smallest whose face covers it, else the largest (<see cref="TownTripPurse.Pick"/>, the one rule; among stacks of one face the first read, so the main pack's before a side pack's); null when she holds none.</summary>
    private static PurseNotes.HeldNote? NextNote(IGameSessionView session, VendorBuyRequest request, long shortfall)
    {
        var held = PurseNotes.Read(session.State, request.NotesFirst).Notes;
        return TownTripPurse.Pick([.. held.Select(h => h.Note.Value)], shortfall) is >= 0 and var k ? held[k] : null;
    }

    /// <summary>The most of the row the coin pays for (the price is chunked by stack, so it is searched, not divided).</summary>
    private static int Affordable(VendorStock stock, VendorStockEntry entry, int count, long coins)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (stock.PriceToBuy(entry, (uint)mid) <= coins)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    /// <summary>The coin held, or -1 unread: the stations' one coin read.</summary>
    private static long Coins(IGameSessionView session) => StationItems.CoinsOf(session.State);

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
