using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 14 (the owner's order of 2026-11-12: leather in place of the plate): the trip's part of the leather swap (<see cref="LeatherSwap"/>, <see cref="LeatherSet"/>). After the stock is planned, the rule reads the purse over the trip's
/// other buys: held back, nothing of the set is bought (said once a run, and once when it can pay); due, the missing pieces join the buy list at the set's vendor, each bought with the floor's check, act, check. In the equip pass,
/// once every piece is held, the robe comes off and the set goes on as one act with its checks, and one named recovery (the set off again, the robe back on) when a piece will not go on: the body is never left with a bare part.
/// </summary>
public sealed partial class ResupplyRun
{
    private readonly List<ResupplyBuy> _leatherBuys = [];
    private long _waitingScrollCoin;
    private int _leatherKept;

    /// <summary>The trip's word on the leather swap: <c>n/a</c> (not this character's, or not this kind of trip), <c>purse</c> (held back), <c>due</c> (bought this trip), <c>wear</c> (every piece was held), then <c>worn</c> or <c>failed</c> after the swap.</summary>
    public string Leather { get; private set; } = "n/a";

    /// <summary>The weenie classes she wears, and those she wears or carries (the main pack and the side packs).</summary>
    private (List<uint> Worn, List<uint> Held) LeatherHeld()
    {
        var worn = new List<uint>();
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            if (S.State.Objects.TryGet(e.Guid, out var o))
            {
                worn.Add(o.Weenie.WeenieClassId);
            }
        }

        var guids = new List<uint>();
        GearInventory.Collect(S.State, guids);
        var held = new List<uint>(worn);
        foreach (var guid in guids)
        {
            if (S.State.Objects.TryGet(guid, out var o))
            {
                held.Add(o.Weenie.WeenieClassId);
            }
        }

        return (worn, held);
    }

    /// <summary>The parts covered by worn pieces that are neither of the set nor the robe it replaces (<see cref="LeatherSwap.CoveredByOthers"/>): a looted piece the gear pass wore over a part.</summary>
    private uint LeatherCoveredByOthers(LeatherSet set) =>
        LeatherSwap.CoveredByOthers(set, SelfEquipJudge.Worn(S.State).Select(w => (S.State.Objects.TryGet(w.Guid, out var o) ? o.Weenie.WeenieClassId : 0u, w.Coverage, w.Location)));

    /// <summary>True on a trip that may buy and wear the set: a supply trip with the gear phase, not a kit trip and not the death's replace trip.</summary>
    private bool LeatherTrip => _options.Kit is null && _options.Gear && !_options.Replace;

    /// <summary>True when the sale keeps an item of <paramref name="wcid"/>: a piece of the leather set while the swap is still hers (the starter boots, a piece bought on an earlier trip that waits for the rest).</summary>
    private bool LeatherKeeps(uint wcid) => LeatherSet.Default is { } set && set.PieceFilledBy(wcid) is not null && LeatherSwap.Applies(set, LeatherHeld().Worn);

    /// <summary>
    /// The plan, after the stock: check (she wears the robe; what she holds of the set; the purse, this trip's other buys and the scrolls it put off), then the rule's answer logged with its numbers: held back once a run,
    /// due once, and the missing pieces added to the buy list at the set's vendor.
    /// </summary>
    private void PlanLeather(HuntingGround ground)
    {
        if (!LeatherTrip || LeatherSet.Default is not { } set)
        {
            return;
        }

        var (worn, held) = LeatherHeld();
        if (!LeatherSwap.Applies(set, worn))
        {
            return;
        }

        long planned = 0;
        foreach (var buy in _buyList)
        {
            planned += ((long)buy.Count * (buy.Vendor.Sells.FirstOrDefault(w => w.Wcid == buy.Line.Wcid)?.Price ?? 0)) + buy.ComponentCoin;
        }

        var plan = LeatherSwap.Decide(set, worn, held, FloorPurse(), planned, _waitingScrollCoin, ground.Supplies.Purse.TripSpendPercent, LeatherCoveredByOthers(set));
        Leather = plan.Token;
        if (plan.Kind == LeatherSwapKind.HeldBack)
        {
            if (Context.Facts.TownStockSayOnce(set.Pieces[0].Wcid, "leather:purse"))
            {
                Log(plan.Why + " (said once a run)");
            }

            return;
        }

        Log(plan.Why);
        if (plan.Kind != LeatherSwapKind.Buy)
        {
            return;
        }

        var buys = new List<ResupplyBuy>();
        foreach (var piece in plan.Missing)
        {
            var buy = new ResupplyBuy(new StockLine(piece.Wcid, piece.Name, 1, 1, 1), set.Vendor, 0, 1);
            _floorBuys.Add(buy); // bought with the floor's check before and after (FloorBuy)
            _leatherBuys.Add(buy);
            buys.Add(buy);
        }

        _buyList = [.. _buyList, .. buys]; // last in the list: the stock and the scrolls are never short for it
    }

    /// <summary>
    /// The swap, in the equip pass. Check before: the robe is worn, every piece of the set is held, the main pack has a slot for the robe. Act: the robe off, then each piece on at its own locations. Check after: every piece worn and
    /// no part of the body bare. A piece that will not go on: the named recovery <c>leather-swap-undo</c> (the pieces put on come off, the robe goes back on at its location), and the trip says <c>leather=failed</c>.
    /// </summary>
    private IEnumerable<ActionHandle?> LeatherSwapPass()
    {
        if (!LeatherTrip || LeatherSet.Default is not { } set)
        {
            yield break;
        }

        var (wornWcids, heldWcids) = LeatherHeld();
        if (!LeatherSwap.Applies(set, wornWcids))
        {
            yield break;
        }

        var others = LeatherCoveredByOthers(set);
        var missing = LeatherSwap.Missing(set, heldWcids, others);
        if (missing.Count > 0)
        {
            if (_leatherBuys.Count > 0)
            {
                Leather = "failed";
                // Live 2026-11-12 01:02 UTC: the walk to the blacksmith failed once (npc-in-lane) and the set was put off for the whole run. A walk that fails is tried again on every later trip (one attempt a trip, and one more
                // after a short wait when the failure is npc-in-lane: LeatherWalkAgain); each failure is named, none is remembered.
                var why = Text($"leather-pieces-missing: {missing.Count} of {set.Pieces.Count} piece(s) were not held after the trip's buys at {set.Vendor.Name} ({string.Join(", ", missing.Select(p => p.Name))}){(_leatherUnreached is null ? string.Empty : "; the walk: " + _leatherUnreached)}");
                Log(Text($"WARNING: leather: the swap waits (named: {why}): the robe stays on, no part of the set is worn alone; the pieces bought are kept; the next trip tries the buy again"));
            }

            yield break;
        }

        var robe = S.State.Self.Inventory.Equipped.Select(e => (Entry: e, Object: S.State.Objects.TryGet(e.Guid, out var o) ? o : null)).FirstOrDefault(p => p.Object is not null && set.InsteadOf.Contains(p.Object.Weenie.WeenieClassId));
        if (robe.Object is null)
        {
            yield break;
        }

        // The item that fills each part: the worn one when she wears one, else the first held.
        var guids = new List<uint>();
        GearInventory.Collect(S.State, guids);
        var chosen = new List<(LeatherPiece Piece, uint Guid, string Name, uint Valid)>();
        var coveredParts = new List<string>();
        foreach (var piece in set.Pieces)
        {
            // Check before, by part (live 2026-11-12 02:17 UTC: ACE refused the set's Leather Gauntlets over the looted pair she wore, and the swap undid itself): a part another worn piece covers whole is on already.
            // That piece stays, nothing is sent for the part, and the set's own piece for it (if she carries one) stays in the pack. A part such a piece covers only partly cannot take the set's piece and would be left
            // part-bare without it: the swap is not started, by name.
            var overlap = others & piece.Coverage;
            if (overlap != 0)
            {
                var by = SelfEquipJudge.Worn(S.State).FirstOrDefault(w => (GearCoverage.OfWorn(w.Coverage, w.Location) & piece.Coverage) != 0 && S.State.Objects.TryGet(w.Guid, out var wo) && !set.InsteadOf.Contains(wo.Weenie.WeenieClassId) && set.PieceFilledBy(wo.Weenie.WeenieClassId) is null);
                if (!LeatherSwap.Covers(others, piece))
                {
                    Leather = "failed";
                    Log(Text($"WARNING: leather: the swap is not made (named: leather-part-partly-covered): the worn '{by?.Name ?? "?"}' covers 0x{overlap:X} of the '{piece.Name}' part 0x{piece.Coverage:X}, not all of it: ACE would refuse the set's piece over it and the rest of the part would be bare: the robe stays on"));
                    yield break;
                }

                coveredParts.Add(Text($"'{by?.Name ?? "?"}' over the '{piece.Name}' part 0x{piece.Coverage:X}"));
                continue;
            }

            var candidates = guids.Where(g => S.State.Objects.TryGet(g, out var o) && piece.FilledBy(o.Weenie.WeenieClassId)).ToList();
            var guid = candidates.FirstOrDefault(IsWorn);
            guid = guid != 0 ? guid : candidates.FirstOrDefault();
            if (guid == 0 || !S.State.Objects.TryGet(guid, out var item))
            {
                Leather = "failed";
                Log(Text($"WARNING: leather: the swap is not made (named: leather-piece-unread): no held object fills '{piece.Name}' although its weenie class is held: the robe stays on"));
                yield break;
            }

            chosen.Add((piece, guid, item.Name, item.Weenie.ValidLocations ?? piece.Locations));
        }

        if (PackView.MainPackFree(S.State) <= 0)
        {
            Leather = "failed";
            Log(Text($"WARNING: leather: the swap is not made (named: leather-no-slot): the main pack has no free slot to take the '{robe.Object.Name}' off into: the robe stays on; the next trip tries again"));
            yield break;
        }

        var robeGuid = robe.Entry.Guid;
        var robeLocation = robe.Entry.Location;
        Log(Text($"leather: swap: check before: '{robe.Object.Name}' 0x{robeGuid:X8} is worn at 0x{robeLocation:X}, every one of the {set.Pieces.Count} parts is held or covered ({string.Join(", ", chosen.Select(c => $"{c.Name}{(IsWorn(c.Guid) ? " (worn)" : string.Empty)}"))}{(coveredParts.Count == 0 ? string.Empty : "; covered already, left as it is: " + string.Join(", ", coveredParts))}), {PackView.MainPackFree(S.State)} main-pack slot(s) free: the robe comes off and the set goes on"));
        foreach (var part in coveredParts)
        {
            Log(Text($"leather: swap: {part}: worn already, and no piece of the set's can go on over it (ACE refuses a piece over a worn one that covers its part): counted on, nothing sent; the set's own piece for the part stays in the pack"));
        }

        var off = S.Actions.MoveToPack(robeGuid, S.State.Self.Guid, 0, ActionSource.Automation);
        yield return off;
        if (IsWorn(robeGuid))
        {
            Leather = "failed";
            _equipRefused.Add(Text($"take off '{robe.Object.Name}' ({Describe(off)})"));
            Log(Text($"WARNING: leather: the swap is not made (named: leather-robe-stays): '{robe.Object.Name}' is still worn after the take-off ({Describe(off)}): nothing else was sent"));
            yield break;
        }

        Log(Text($"leather: swap: '{robe.Object.Name}' 0x{robeGuid:X8} off to the main pack: {Describe(off)}"));
        var put = new List<uint>();
        string? failed = null;
        foreach (var (piece, guid, name, valid) in chosen)
        {
            if (IsWorn(guid))
            {
                Log(Text($"leather: swap: '{name}' 0x{guid:X8} is worn already (coverage 0x{piece.Coverage:X}): counted on, no Wield sent"));
                continue;
            }

            for (var attempt = 0; attempt < 2 && !IsWorn(guid); attempt++)
            {
                if (attempt == 1)
                {
                    Log(Text($"leather: swap: recovery=leather-wield-again: '{name}' is not worn after the Wield; one more Wield"));
                }

                var on = new WieldResult();
                foreach (var h in Wardrobe.PutOn(S, new WieldRequest(guid) { Name = name, Valid = valid }, on)) { yield return h; } // M6 16 E5e1: the one wield routine, the slot by its one rule
                var location = on.Location;
                Log(Text($"leather: swap: '{name}' 0x{guid:X8} on at 0x{location:X} (coverage 0x{piece.Coverage:X}, armour level {piece.ArmourLevel}): {on.Outcome(Describe)}; check after: {(IsWorn(guid) ? "worn" : "not worn")}"));
            }

            if (!IsWorn(guid))
            {
                failed = name;
                break;
            }

            put.Add(guid);
        }

        var bare = GearFloor.BareParts(SelfEquipJudge.Worn(S.State));
        if (failed is null && bare == 0)
        {
            Leather = "worn";
            EquipWorn += put.Count;
            SelfEquips += put.Count;
            Log(Text($"leather: swap done: {put.Count} piece(s) put on, the set of {set.Pieces.Count} is worn in place of '{robe.Object.Name}'{(coveredParts.Count == 0 ? string.Empty : $" ({coveredParts.Count} part(s) by a piece she wore already)")}, no part of the body is bare (0x0); the robe is in the pack for its return to its giver (attuned: no vendor is offered it)"));
            yield break;
        }

        // The named recovery: the set off again, the robe back on, so the body is covered as it was.
        Leather = "failed";
        Log(Text($"WARNING: leather: swap: recovery=leather-swap-undo: {(failed is null ? $"the body is bare at 0x{bare:X} ({GearFloor.PartNames(bare)}) with the set on" : $"'{failed}' would not go on")}: the {put.Count} piece(s) put on come off and '{robe.Object.Name}' goes back on"));
        foreach (var guid in put)
        {
            var undo = S.Actions.MoveToPack(guid, S.State.Self.Guid, 0, ActionSource.Automation);
            yield return undo;
        }

        var back = new WieldResult();
        foreach (var h in Wardrobe.PutOn(S, new WieldRequest(robeGuid) { Name = robe.Object.Name, Location = robeLocation }, back)) { yield return h; } // M6 16 E5e1: the one wield routine
        _equipRefused.Add(Text($"leather swap ({failed ?? "bare parts"})"));
        Log(Text($"leather: swap: recovery: '{robe.Object.Name}' back on at 0x{robeLocation:X}: {back.Outcome(Describe)}; check: {(IsWorn(robeGuid) ? "worn again: the body is covered as before" : $"NOT worn: the body is bare at 0x{GearFloor.BareParts(SelfEquipJudge.Worn(S.State)):X} (named: leather-swap-bare)")}"));
    }

    /// <summary>The seconds the trip waits before its one second walk to the set's vendor after an <c>npc-in-lane</c> failure (a body in a doorway lane moves on by itself; the walker's own wait is 3 s).</summary>
    public const double LaneWaitSeconds = 5;

    private string? _leatherUnreached;

    /// <summary>
    /// True when a visit that could not reach its vendor is walked once more on this trip: the visit holds a buy of the leather set and the walk's failure names <c>npc-in-lane</c>. Every other vendor, and every other
    /// failure, keeps the trip's rule as it was: the stop is skipped for this trip and planned again on the next (the generic vendor code remembers no failure across trips).
    /// </summary>
    internal bool LeatherWalkAgain(ResupplyVisit visit, string unreachable)
    {
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(unreachable);
        if (!visit.Buys.Any(_leatherBuys.Contains))
        {
            return false;
        }

        _leatherUnreached = unreachable;
        return unreachable.Contains("npc-in-lane", StringComparison.Ordinal);
    }

    private bool IsWorn(uint guid) => S.State.Self.Inventory.Contains(guid) && S.State.Self.Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;
}
