using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 16 E5c2 (docs/automation-engine.md E5, <c>TODO(engine-sale-copies)</c>): the reads behind a visit's burden budget (M6 14 pack room, <see cref="PackBudgetRule"/>) and the drinks it cuts for sale, as one place the town trip and
/// the session's sale context both ask. Before this slice only a trip could say which drink stacks its budget sells (<c>ResupplyRun.PlanPackBudget</c> and <c>SurplusDrinks</c>, over the trip's own fields), so outside a trip the
/// rule book read every drink as kept. Every member is a read of the state it is given: the trip keeps what it does about them (its lines, the dance's word in the session's memory, the counters' drink lines, the split of a stack).
/// </summary>
internal static class PackBudgetRead
{
    /// <summary>Every drink she uses (<see cref="ResupplyRun.DrinkInUse"/>).</summary>
    public static IEnumerable<uint> DrinkWcids => HealthDrinks.All.Select(d => d.Wcid).Concat(ManaDrinks.All.Select(d => d.Wcid)).Append(FighterKit.StaminaPotionWcid);

    /// <summary>The wares the budget weighs and the measure counts: the stock and town stock lines, the drinks she uses and the foci.</summary>
    /// <param name="ground">The ground.</param>
    public static List<uint> Wcids(HuntingGround ground) =>
        [.. ground.Supplies.Stock.Select(l => l.Wcid).Concat(ground.TownStock.Select(l => l.Wcid)).Concat(DrinkWcids).Concat(GearKit.FociWcids).Distinct()];

    /// <summary>The guids of the objects of weenie class <paramref name="wcid"/> she holds, anywhere (the object table's order).</summary>
    private static List<uint> HeldOf(GameState state, uint wcid)
    {
        var list = new List<uint>();
        foreach (var o in state.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && state.Self.Inventory.Contains(o.Guid))
            {
                list.Add(o.Guid);
            }
        }

        return list;
    }

    /// <summary>The burden of one of a ware: the facts (<see cref="PackBudgetRule.UnitBurdenOf"/>), else a held piece of it that is no stack (its described burden is its own), else 0.</summary>
    private static int UnitOf(GameState state, HuntingGround ground, uint wcid)
    {
        if (PackBudgetRule.UnitBurdenOf(wcid, ground) is > 0 and var known)
        {
            return known;
        }

        foreach (var guid in HeldOf(state, wcid))
        {
            if (state.Objects.TryGet(guid, out var o) && GearInventory.StackOf(state, o) <= 1 && o.Weenie.Burden is > 0 and var own)
            {
                return own;
            }
        }

        return 0;
    }

    /// <summary>
    /// The wares the budget weighs, counted now: each stock line (a carried one, or one no home vendor sells, is weighed and never cut; a suspended one is not weighed as a line), each town stock line, each drink she uses that no
    /// line names, the foci.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="counters">The ground's logistics counters (their lines, with what the buff passes' needs raised), or null: the profile's stock lines as they are written (a read outside a trip on a ground the session has no counters for yet).</param>
    /// <param name="suspended">True for a stock line that is suspended (an account formula's line while every focus is held; a drink's line while the stamina dance covers her mana).</param>
    public static List<PackLine> Lines(GameState state, HuntingGround ground, LogisticsCounters? counters, Func<int, bool> suspended)
    {
        var scratch = new List<uint>();
        int Held(uint wcid) => GearInventory.CountOf(state, scratch, wcid);
        var lines = new List<PackLine>();
        var seen = new HashSet<uint>();
        var count = counters?.LineCount ?? ground.Supplies.Stock.Count;
        for (var i = 0; i < count; i++)
        {
            var line = LineAt(ground, counters, i);
            if (suspended(i) || !seen.Add(line.Wcid))
            {
                continue;
            }

            var kind = (counters?.IsCarried(i) ?? ground.IsCarried(line.Wcid)) || ground.VendorSelling(line.Wcid) is null ? PackLineKind.Held : PackLineKind.Stock;
            lines.Add(new PackLine(line.Wcid, line.Name, kind, Held(line.Wcid), line.Keep, Math.Max(line.TripUnder, line.StopUnder), UnitOf(state, ground, line.Wcid), ResupplyRun.DrinkInUse(line.Wcid)));
        }

        var level = state.Self.Qualities.Level;
        foreach (var town in ground.TownStock)
        {
            if (!seen.Add(town.Wcid))
            {
                continue;
            }

            var kind = town.FeeOnly && level < town.FeeFromLevel ? PackLineKind.Held : PackLineKind.Town; // under the bot's fee level nothing buys its line
            lines.Add(new PackLine(town.Wcid, town.Name, kind, Held(town.Wcid), town.Keep, town.TripUnder, town.Burden > 0 ? town.Burden : UnitOf(state, ground, town.Wcid), ResupplyRun.DrinkInUse(town.Wcid)));
        }

        foreach (var wcid in DrinkWcids.Concat(GearKit.FociWcids))
        {
            var held = seen.Contains(wcid) ? 0 : Held(wcid);
            if (held <= 0)
            {
                continue;
            }

            seen.Add(wcid);
            var name = HeldOf(state, wcid).Select(g => state.Objects.TryGet(g, out var o) ? o.Name : null).FirstOrDefault(n => n is not null) ?? wcid.ToString(CultureInfo.InvariantCulture);
            var drink = ResupplyRun.DrinkInUse(wcid);
            lines.Add(new PackLine(wcid, name, drink ? PackLineKind.LootDrink : PackLineKind.Held, held, held, 0, UnitOf(state, ground, wcid), drink));
        }

        return lines;
    }

    /// <summary>The stock line <paramref name="index"/>: the counters' when given, else the profile's.</summary>
    private static StockLine LineAt(HuntingGround ground, LogisticsCounters? counters, int index) => counters is null ? ground.Supplies.Stock[index] : counters.LineAt(index);

    /// <summary>
    /// The burden budget as the state stands (<see cref="PackBudgetRule.Plan"/>), or null when her Strength or her burden is not read (never a budget on a guessed capacity; <paramref name="lines"/> is then empty).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="counters">As <see cref="Lines"/>.</param>
    /// <param name="suspended">As <see cref="Lines"/>.</param>
    /// <param name="saleBurden">The burden of the loot the sale sells.</param>
    /// <param name="measure">What she used and the loot since the last visit, or null (unmeasured).</param>
    /// <param name="ditch">True when the stamina dance covers her mana: every drink held is sold.</param>
    /// <param name="lines">The wares weighed.</param>
    public static PackBudget? Plan(GameState state, HuntingGround ground, LogisticsCounters? counters, Func<int, bool> suspended, long saleBurden, PackUseMeasure? measure, bool ditch, out List<PackLine> lines)
    {
        var load = LootContext.Of(state);
        if (!state.Self.Qualities.TryGetAttributeLevels(1, out _, out _) || load.Capacity <= 0 || state.Self.Inventory.Burden is null)
        {
            lines = [];
            return null;
        }

        lines = Lines(state, ground, counters, suspended);
        return PackBudgetRule.Plan(load.Capacity, MageLootThresholds.LoadLineOf(ground), load.Burden, saleBurden, lines, measure, ditch);
    }

    /// <summary>The drinks of a cut still over its cut keep when <paramref name="held"/> are held: the cut's count to sell, never more than what is held over the keep.</summary>
    /// <param name="cut">The cut.</param>
    /// <param name="held">The count held now.</param>
    public static int Over(PackCut cut, int held) => Math.Min(cut.Sell, held - cut.Target);

    /// <summary>The stacks of a drink the sale may offer, smallest first (then by guid): every stack she holds that <paramref name="offerable"/> lets through.</summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="wcid">The drink.</param>
    /// <param name="offerable">True for a stack the sale would take (not kept aside; not worn, valueless, attuned or retained).</param>
    public static List<(uint Guid, int Size, WorldObject Object)> Stacks(GameState state, uint wcid, Func<uint, WorldObject, bool> offerable)
    {
        var stacks = new List<(uint Guid, int Size, WorldObject Object)>();
        foreach (var g in HeldOf(state, wcid))
        {
            if (state.Objects.TryGet(g, out var o) && offerable(g, o))
            {
                stacks.Add((g, GearInventory.StackOf(state, o), o));
            }
        }

        stacks.Sort((a, b) => a.Size != b.Size ? a.Size.CompareTo(b.Size) : a.Guid.CompareTo(b.Guid));
        return stacks;
    }

    /// <summary>The whole stacks that fit in <paramref name="over"/>, in the order given: each one that fits what is left is taken. What is left after them is a split's, which only a trip makes.</summary>
    /// <param name="stacks">The stacks, smallest first (<see cref="Stacks"/>).</param>
    /// <param name="over">The count over the cut keep (<see cref="Over"/>).</param>
    public static List<(uint Guid, int Size, WorldObject Object)> Whole(IReadOnlyList<(uint Guid, int Size, WorldObject Object)> stacks, int over)
    {
        var whole = new List<(uint Guid, int Size, WorldObject Object)>();
        foreach (var stack in stacks)
        {
            if (stack.Size <= over)
            {
                whole.Add(stack);
                over -= stack.Size;
            }
        }

        return whole;
    }

    /// <summary>
    /// The drink stacks a trip's budget would put on the sale as the pack stands now, with no trip under way: the stamina dance read now (<see cref="StaminaDance.Covers"/>), the use and the loot since the last visit measured to
    /// now (<see cref="PackRoomMemory.Measure"/>), the budget planned from them, and of each drink it cuts the whole stacks that fit over the cut keep. Empty when the budget cannot be read or cuts no drink. A read: the session's
    /// memory and the counters are not written. The rest of a stack a trip would split off is not in it (no stack of that size exists until the trip splits it).
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells (the dance's casts).</param>
    /// <param name="counters">The session's logistics counters of the ground when it has any, for their lines alone, else null (the profile's lines): which are suspended is read here from the facts (an account formula's line while every focus is held, a drink's line while the dance covers her mana), not from the counters' last check. No counters are made for this read.</param>
    /// <param name="allFociHeld">True when she holds the focus of every school she casts (<see cref="FociComponents.AllFociHeld"/>).</param>
    /// <param name="saleBurden">The burden of the loot the sale sells, the drinks aside.</param>
    /// <param name="offerable">As <see cref="Stacks"/>.</param>
    public static HashSet<uint> SurplusNow(GameState state, HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, LogisticsCounters? counters, bool allFociHeld, long saleBurden, Func<uint, WorldObject, bool> offerable)
    {
        var surplus = new HashSet<uint>();
        var ditch = StaminaDance.Covers(state, spells, out _);
        var memory = PackRoomMemory.For(state);
        var scratch = new List<uint>();
        var measure = memory.Measure(memory.VisitedAt, Wcids(ground), w => GearInventory.CountOf(state, scratch, w)); // the minutes are only the log's: no clock is asked here
        bool Suspended(int i) => LineAt(ground, counters, i).Wcid is var wcid && ((allFociHeld && FociComponents.IsAccountOnly(wcid)) || (ditch && PackBudgetRule.DrinkBurdens.ContainsKey(wcid))); // LogisticsCounters.IsSuspended, from the facts
        if (Plan(state, ground, counters, Suspended, saleBurden, measure, ditch, out _) is not { } budget)
        {
            return surplus;
        }

        foreach (var cut in budget.Cuts)
        {
            if (cut.Sell <= 0)
            {
                continue;
            }

            var over = Over(cut, GearInventory.CountOf(state, scratch, cut.Line.Wcid));
            if (over <= 0)
            {
                continue;
            }

            foreach (var stack in Whole(Stacks(state, cut.Line.Wcid, offerable), over))
            {
                surplus.Add(stack.Guid);
            }
        }

        return surplus;
    }
}
