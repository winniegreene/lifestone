using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 pack room (<c>m6-pack-room</c>, the owner's direction of 2026-11-14: size her supplies to her carrying capacity, so loot has room): the visit's burden budget (<see cref="PackBudgetRule"/>) in the home walk. Check, act, check:
/// <list type="number">
/// <item>At the count, what she used and the loot since the last visit are measured (<see cref="PackRoomMemory.Measure"/>), logged.</item>
/// <item>Before the sale walk, the budget is planned from her live capacity, burden and the loot about to be sold, logged with every cut.</item>
/// <item>The drinks over their cut keeps are sold with the loot (amends <c>m6-sale-keeps-drinks</c>): whole stacks first, the rest split off (<see cref="IActionApi.SplitToContainer"/>), each split checked by the new stack showing in the
/// main pack; a split that does not show is named and the stack stays for the next visit.</item>
/// <item>The stock's buy list buys to the cut keeps (<see cref="LogisticsCounters.CutKeep"/>); the town stock's stop on the way out reads them from the memory.</item>
/// <item>At the end, the counts held are the next measure's departure, and the loot record is cleared (<see cref="PackRoomMemory.Visited"/>).</item>
/// </list>
/// </summary>
public sealed partial class ResupplyRun
{
    private static readonly HashSet<uint> NoCasters = [];
    private readonly HashSet<uint> _drinkSales = [];
    private readonly List<uint> _packScratch = [];
    private PackUseMeasure? _packMeasure;

    /// <summary>M6 14 pack room: this visit's burden budget, or null (not planned: a kit, replace or town trip, or the capacity unread).</summary>
    public PackBudget? PackBudget { get; private set; }

    /// <summary>M6 14 pack room: the drinks put on the sale over the budget's cut keeps this trip (whole stacks and split pieces, counted in drinks).</summary>
    public int DrinksOverBudget { get; private set; }

    private int HeldCount(uint wcid) => GearInventory.CountOf(S.State, _packScratch, wcid);

    /// <summary>The wares the budget weighs and the measure counts: the stock and town stock lines, the drinks she uses and the foci (M6 16 E5c2: <see cref="PackBudgetRead.Wcids"/>).</summary>
    private static List<uint> PackWcids(HuntingGround ground) => PackBudgetRead.Wcids(ground);

    /// <summary>Check (1): what she used and the loot since the last visit ended, logged.</summary>
    private void MeasurePackUse(HuntingGround ground)
    {
        _packMeasure = PackRoomMemory.For(S.State).Measure(S.Clock.Now, PackWcids(ground), HeldCount);
        Log(_packMeasure is { } m
            ? "pack budget: measured: " + m.Line()
            : "pack budget: measured: nothing yet: this is the session's first visit, so there is no visit to measure her use and the loot from (m6-pack-budget-unmeasured)");
    }

    /// <summary>M6 14: the stamina dance's last reading logged this visit (a change is logged again).</summary>
    private string? _danceLogged;

    /// <summary>Check (2): the visit's budget, planned before the sale walk from her live capacity and burden and the loot <paramref name="sale"/> is about to sell; logged once a visit with every cut.</summary>
    private void PlanPackBudget(HuntingGround ground, LogisticsCounters counters, List<(uint Guid, WorldObject Object)> sale)
    {
        // M6 14 (the owner, 2026-11-14: at her level the potions go): check before: does the stamina dance cover her mana; logged at each change; while it does no drink is bought or kept
        var ditch = StaminaDance.Covers(S.State, Context.Spells, out var danceWhy);
        var memory = PackRoomMemory.For(S.State);
        var said = ditch ? danceWhy : null;
        if (!string.Equals(memory.DrinksDitched, said, StringComparison.Ordinal) || _danceLogged is null)
        {
            Log(ditch ? "potions: ditched: " + danceWhy : "potions: kept: " + danceWhy);
            _danceLogged = danceWhy;
        }

        memory.DrinksDitched = said;
        counters.DrinksOff(ditch);
        // M6 16 E5c2: the budget's reads are one place (PackBudgetRead), which the session's sale context asks too; the trip's own are the measure it took at the count and the counters' drink lines it has just stated
        var saleBurden = sale.Sum(k => (long)(k.Object.Weenie.Burden ?? 0));
        if (PackBudgetRead.Plan(S.State, ground, counters, counters.IsSuspended, saleBurden, _packMeasure, ditch, out var lines) is not { } budget)
        {
            Log("pack budget: her Strength or her burden is not read (no player description's Strength, or no EncumbranceVal): no budget this visit, never one on a guessed capacity; the keeps are the profile's");
            return;
        }

        PackBudget = budget;
        Log(budget.Summary());
        foreach (var line in budget.CutLines())
        {
            Log(line);
        }

        foreach (var unweighed in lines.Where(l => l.Unit <= 0 && l.Held > 0))
        {
            Log(Text($"pack budget: {unweighed.Name} ({unweighed.Wcid}): no burden of one is known (TODO(m6-pack-unit-burden)): its {unweighed.Held} are counted in the fixed part and never cut"));
        }
    }

    /// <summary>
    /// Act (3): the drinks over the budget's cut keeps join the sale (<paramref name="kept"/>): of each drink, the whole stacks that fit in its surplus, smallest first, then the rest split off the next stack into the main pack. Check
    /// before each split: the stack is held with more than the piece, and the main pack has a free slot; check after: a new stack of the drink shows in the main pack. A split that does not show is named, and the stack is kept whole for the
    /// next visit (no retry here). Looting off, there is no sale walk: the surplus is named and kept.
    /// </summary>
    private IEnumerable<ActionHandle?> SurplusDrinks(List<(uint Guid, WorldObject Object)> kept)
    {
        if (PackBudget is not { } budget || !budget.Cuts.Any(c => c.Sell > 0))
        {
            yield break;
        }

        if (!_options.Loot)
        {
            Log(Text($"pack budget: looting is off (D38), so there is no sale walk: {string.Join(", ", budget.Cuts.Where(c => c.Sell > 0).Select(c => Text($"{c.Sell} {c.Line.Name}")))} over the cut keeps stay this trip"));
            yield break;
        }

        foreach (var cut in budget.Cuts.Where(c => c.Sell > 0))
        {
            var wcid = cut.Line.Wcid;
            var before = HeldCount(wcid);
            var left = PackBudgetRead.Over(cut, before);
            if (left <= 0)
            {
                Log(Text($"pack budget: {cut.Line.Name}: {before} held now, at or under the cut keep {cut.Target}: none to sell"));
                continue;
            }

            var stacks = PackBudgetRead.Stacks(S.State, wcid, (g, o) => !_memory.KeptAside.Contains(g) && NotSellable(g, o, NoCasters) is null); // the sale would refuse a worn, valueless, attuned or retained stack
            var offered = new List<string>();
            foreach (var (guid, size, whole) in PackBudgetRead.Whole(stacks, left))
            {
                _drinkSales.Add(guid);
                kept.Add((guid, whole));
                offered.Add(Text($"0x{guid:X8} x{size}"));
                DrinksOverBudget += size;
                left -= size;
            }

            var from = stacks.FirstOrDefault(s => s.Size > left && !_drinkSales.Contains(s.Guid));
            if (left > 0 && from.Guid != 0)
            {
                if (Quartermaster.PackView.MainPackFree(S.State) <= 0)
                {
                    Log(Text($"pack budget: {cut.Line.Name}: {left} more over the cut keep {cut.Target} cannot be split off 0x{from.Guid:X8} (x{from.Size}): the main pack has no free slot; kept whole for the next visit"));
                }
                else
                {
                    var others = InPack(wcid);
                    var split = S.Actions.SplitToContainer(from.Guid, 0, 0, left, ActionSource.Automation);
                    yield return split;
                    foreach (var h in Until(() => InPack(wcid).Except(others).Any(), 2)) { yield return h; } // ACE's 0022 confirms the split; the new stack's F745 follows it

                    var piece = InPack(wcid).Except(others).FirstOrDefault();
                    if (split.Outcome is not { Kind: ActionOutcomeKind.Confirmed } || piece == 0 || !S.State.Objects.TryGet(piece, out var pieceObject))
                    {
                        Log(Text($"pack budget: {cut.Line.Name}: the split of {left} from 0x{from.Guid:X8} (x{from.Size}) {Describe(split)}; check after: no new stack showed: kept whole for the next visit"));
                    }
                    else
                    {
                        _drinkSales.Add(piece);
                        kept.Add((piece, pieceObject));
                        offered.Add(Text($"0x{piece:X8} x{left} (split from 0x{from.Guid:X8})"));
                        DrinksOverBudget += left;
                        left = 0;
                    }
                }
            }

            Log(Text($"pack budget: {cut.Line.Name}: {before} held, the cut keep {cut.Target}: offered for sale {(offered.Count == 0 ? "none" : string.Join(", ", offered))}{(left > 0 ? Text($"; {left} more stay") : string.Empty)} (m6-pack-room amends m6-sale-keeps-drinks: what she does not drink between trips is sold)"));
        }
    }

    /// <summary>Act (4): the stock lines' keeps of this visit's buy list cut to the budget's (<see cref="LogisticsCounters.CutKeep"/>), each change logged.</summary>
    private void CutPackKeeps(LogisticsCounters counters)
    {
        if (PackBudget is not { } budget)
        {
            return;
        }

        for (var i = 0; i < counters.LineCount; i++)
        {
            var line = counters.LineAt(i);
            if (budget.KeepOf(line.Wcid) is { } keep && counters.CutKeep(i, keep))
            {
                Log(Text($"pack budget: {line.Name}: this visit buys to {counters.LineAt(i).Keep}, not the profile's keep {line.Keep} (held {counters.HeldOf(i)})"));
            }
        }
    }

    /// <summary>Check (5): the visit ends: the counts held now are the next measure's departure, the cut keeps are kept for the town stock, and the loot record is cleared.</summary>
    private void PackVisited(HuntingGround ground)
    {
        var held = PackWcids(ground).ToDictionary(w => w, HeldCount);
        var keeps = PackBudget?.Keeps ?? new Dictionary<uint, int>();
        var room = PackRoomMemory.For(S.State);
        var left = room.LoadLeftItems;
        room.Visited(S.Clock.Now, held, keeps);
        var load = LootContext.Of(S.State);
        Log(Text($"pack budget: the visit ends: burden {load.Burden} of {load.Capacity}; the measure starts again from {string.Join(", ", held.Where(h => h.Value > 0).OrderBy(h => h.Key).Select(h => Text($"{h.Key} x{h.Value}")))}; the loot record ({left} item(s) left over the load line) is cleared, so the full-pack line waits for the next item the loot leaves; {(keeps.Count == 0 ? "no keep cut" : Text($"the cut keeps {string.Join(", ", keeps.OrderBy(k => k.Key).Select(k => Text($"{k.Key}:{k.Value}")))} stand for the town stock"))}"));
    }
}
