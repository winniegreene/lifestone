using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M6 14 pack room (<c>m6-pack-room</c>; live 2026-11-14, <c>cache/m6/14/live/ladder18</c>: the level 66 climber in the Tusker Barracks stood at 17,700 to 17,801 of her capacity 17,850 from 20:56 UTC, the loot left 473 items over its load line by
/// 22:33, and her own supplies weighed about 14,000): what the session remembers between two visits home, so a visit can size what she carries by choice to her capacity. One per session (<see cref="For"/>: a login makes a new
/// <see cref="GameState"/>, so a new login measures afresh). Game thread only.
/// <list type="bullet">
/// <item><b>The loot since the last visit</b> (<see cref="NoteLoot(MageLootPlan)"/>, from every corpse's plan): the burden the plans kept, and the items, Value and burden they left over the load line. The left items are the full-pack
/// trigger (<see cref="LogisticsCause.FullPack"/>); kept and left together are the loot she would have taken between the two visits, the room the budget leaves for it.</item>
/// <item><b>The departure</b> (<see cref="Visited"/>, at the end of a visit home): the clock and the count of every ware the budget weighs, then the wares the town stock bought on the way out (<see cref="BoughtAfterVisit"/>). The next
/// visit's <see cref="Measure"/> reads from it what she used and the minutes between the two.</item>
/// <item><b>The keeps</b> the last visit's budget cut (<see cref="KeepOf"/>), which the town stock's stop on the way out buys to.</item>
/// </list>
/// </summary>
public sealed class PackRoomMemory
{
    private static readonly ConditionalWeakTable<GameState, PackRoomMemory> PerSession = new();
    private readonly Dictionary<uint, int> _departed = [];
    private readonly Dictionary<uint, int> _bought = [];
    private readonly Dictionary<uint, int> _looted = [];
    private readonly Dictionary<uint, int> _keeps = [];

    /// <summary>The memory of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static PackRoomMemory For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static _ => new PackRoomMemory());
    }

    /// <summary>The items the loot plans left over the load line since the last visit (<see cref="MageLootVerdict.Load"/>): the full-pack trigger.</summary>
    public int LoadLeftItems { get; private set; }

    /// <summary>The Value of the items left over the load line since the last visit.</summary>
    public long LoadLeftValue { get; private set; }

    /// <summary>The burden of the items left over the load line since the last visit.</summary>
    public long LoadLeftBurden { get; private set; }

    /// <summary>The burden the loot plans kept since the last visit (pyreals weigh nothing to the rule).</summary>
    public long KeptBurden { get; private set; }

    /// <summary>The corpses' plans noted since the last visit.</summary>
    public int Plans { get; private set; }

    /// <summary>The session clock (seconds) at the end of the last visit, or NaN before the first.</summary>
    public double VisitedAt { get; private set; } = double.NaN;

    /// <summary>
    /// M6 14 (the lead, on the slice's own finding: her loot wants about eight times her capacity an hour, mostly heavy armour to sell, so a trip for every item left would come every 15 minutes of fighting at 6 to 9 minutes each):
    /// the pieces she would keep (a keep-aside cantrip piece, <see cref="MageLootItem.KeepAside"/>, or a wear upgrade, <see cref="MageLootItem.IsWearUpgrade"/>) the loot left over its load line since the last visit. Only these call the
    /// full-pack trip; sale loot left behind calls none (the room the budget leaves takes the sale loot between trips).
    /// </summary>
    public int LoadLeftKeepers { get; private set; }

    /// <summary>
    /// M6 14 (the owner, 2026-11-14: at her level the potions go, the stamina dance restores the mana): the station's last reading that the dance covers her mana (<c>StaminaDance.Covers</c>), or null when it does not or was not read.
    /// While set, every stock line of a drink is suspended (<see cref="LogisticsCounters.IsSuspended"/>: no stop, no trip, no buy) and the budget sells the drinks held (<see cref="PackBudgetRule.Plan"/>).
    /// </summary>
    public string? DrinksDitched { get; set; }

    /// <summary>The visits ended this session.</summary>
    public int Visits { get; private set; }

    /// <summary>Notes one corpse's plan: the items it left over the load line, the burden it kept, and the drinks and wares among the kept (their count, from the unit burden, so the next measure does not read a picked-up drink as unused).</summary>
    /// <param name="plan">The plan.</param>
    public void NoteLoot(MageLootPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        int left = 0, keepers = 0;
        long value = 0, leftBurden = 0, kept = 0;
        for (var i = 0; i < plan.Items.Count && i < plan.Verdicts.Count; i++)
        {
            var item = plan.Items[i];
            switch (plan.Verdicts[i])
            {
                case MageLootVerdict.Load:
                    left++;
                    if (item.KeepAside is not null)
                    {
                        // M6 14: a keep-aside piece (a great cantrip) left for the load: the full-pack trip's reason. Not a wear upgrade (live 2026-11-15 00:24, ladder19: the fight's loot judge called Leather Sleeves an
                        // upgrade, the station's wear pick found no gain, and the trip it called was wasted; TODO(m6-fight-wear-judge): the fight's judge reads the station's wear terms, then wear upgrades count again)
                        keepers++;
                    }

                    value += item.Value ?? 0;
                    leftBurden += item.Burden;
                    break;
                case MageLootVerdict.Slots when item.GreatCantrip:
                    keepers++; // M6 14 keep every great cantrip (the owner, 2026-11-15): a great-cantrip piece no room could be made for, for want of a slot, calls the trip as one left over the line does
                    break;
                case MageLootVerdict.Keep or MageLootVerdict.KeepAside or MageLootVerdict.KitStone:
                    kept += item.Burden;
                    if (item.Wcid != 0 && PackBudgetRule.UnitBurdenOf(item.Wcid, null) is > 0 and var unit)
                    {
                        _looted[item.Wcid] = _looted.GetValueOrDefault(item.Wcid) + (int)(item.Burden / unit);
                    }

                    break;
            }
        }

        NoteLoot(left, value, leftBurden, kept, keepers);
    }

    /// <summary>
    /// M6 14 keep every great cantrip (check, act, check): a great-cantrip piece the plan kept was taken off it after the loot step's check (its room was not made: a drop refused, or the burden or the slots read after it short): one more piece
    /// she would keep was left, so the full-pack trip is due (<see cref="LoadLeftKeepers"/>), as for one the plan left over its load line.
    /// </summary>
    public void NoteKeeperLeft() => LoadLeftKeepers++;

    /// <summary>Notes one corpse's plan by its numbers (<see cref="NoteLoot(MageLootPlan)"/>).</summary>
    /// <param name="leftItems">The items left over the load line.</param>
    /// <param name="leftValue">Their Value.</param>
    /// <param name="leftBurden">Their burden.</param>
    /// <param name="keptBurden">The burden kept.</param>
    /// <param name="leftKeepers">Of the items left, the pieces she would keep (a keep-aside cantrip piece or a wear upgrade).</param>
    public void NoteLoot(int leftItems, long leftValue, long leftBurden, long keptBurden, int leftKeepers = 0)
    {
        LoadLeftKeepers += Math.Max(0, leftKeepers);
        LoadLeftItems += Math.Max(0, leftItems);
        LoadLeftValue += Math.Max(0, leftValue);
        LoadLeftBurden += Math.Max(0, leftBurden);
        KeptBurden += Math.Max(0, keptBurden);
        Plans++;
    }

    /// <summary>
    /// A visit home ended at <paramref name="clock"/>: the loot record is cleared (the visit sold what it could; the full-pack trigger waits for the next item the loot leaves), and the departure is the counts held now, with the keeps the
    /// visit's budget cut (empty: none).
    /// </summary>
    /// <param name="clock">The session clock, seconds.</param>
    /// <param name="held">The count held of every ware the budget weighs.</param>
    /// <param name="keeps">The keeps the visit's budget cut, by weenie class id.</param>
    public void Visited(double clock, IReadOnlyDictionary<uint, int> held, IReadOnlyDictionary<uint, int> keeps)
    {
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(keeps);
        (LoadLeftItems, LoadLeftValue, LoadLeftBurden, KeptBurden, Plans, LoadLeftKeepers) = (0, 0, 0, 0, 0, 0);
        _departed.Clear();
        _bought.Clear();
        _looted.Clear();
        _keeps.Clear();
        foreach (var (wcid, count) in held)
        {
            _departed[wcid] = count;
        }

        foreach (var (wcid, keep) in keeps)
        {
            _keeps[wcid] = keep;
        }

        VisitedAt = clock;
        Visits++;
    }

    /// <summary>The town stock bought <paramref name="count"/> of <paramref name="wcid"/> on the way out, after the visit: added to the departure, so the next measure does not count them as used. Before any visit, nothing.</summary>
    /// <param name="wcid">The ware.</param>
    /// <param name="count">The count the stop added.</param>
    public void BoughtAfterVisit(uint wcid, int count)
    {
        if (!double.IsNaN(VisitedAt) && count > 0)
        {
            _bought[wcid] = _bought.GetValueOrDefault(wcid) + count;
        }
    }

    /// <summary>The keep the last visit's budget cut for <paramref name="wcid"/>, or null (not cut, or no visit yet).</summary>
    /// <param name="wcid">The ware.</param>
    public int? KeepOf(uint wcid) => _keeps.TryGetValue(wcid, out var keep) ? keep : null;

    /// <summary>
    /// What she used since the last visit ended, read at <paramref name="clock"/>: for each ware the count held at the departure, plus what the town stock bought after it and the loot kept of it, less the count held now (never under 0;
    /// a ware not held at the departure counts from 0); the minutes between; and the loot since. Null before the first visit of the session (nothing to measure from).
    /// </summary>
    /// <param name="clock">The session clock, seconds.</param>
    /// <param name="wcids">The wares to measure.</param>
    /// <param name="heldNow">The count held now of a ware.</param>
    public PackUseMeasure? Measure(double clock, IEnumerable<uint> wcids, Func<uint, int> heldNow)
    {
        ArgumentNullException.ThrowIfNull(wcids);
        ArgumentNullException.ThrowIfNull(heldNow);
        if (double.IsNaN(VisitedAt))
        {
            return null;
        }

        var use = new Dictionary<uint, int>();
        foreach (var wcid in wcids.Distinct())
        {
            use[wcid] = Math.Max(0, _departed.GetValueOrDefault(wcid) + _bought.GetValueOrDefault(wcid) + _looted.GetValueOrDefault(wcid) - heldNow(wcid));
        }

        return new PackUseMeasure(Math.Max(0, (clock - VisitedAt) / 60.0), use, KeptBurden + LoadLeftBurden, KeptBurden, LoadLeftItems, LoadLeftBurden, LoadLeftValue);
    }
}

/// <summary>What <see cref="PackRoomMemory.Measure"/> read at a visit: the minutes since the last one, the count of each ware used, and the loot.</summary>
/// <param name="Minutes">The minutes since the last visit ended.</param>
/// <param name="Use">The count of each ware used, by weenie class id.</param>
/// <param name="LootWanted">The burden of the loot she took or would have taken: the plans' kept burden and the burden they left over the load line.</param>
/// <param name="LootKept">The plans' kept burden.</param>
/// <param name="LeftItems">The items left over the load line.</param>
/// <param name="LeftBurden">Their burden.</param>
/// <param name="LeftValue">Their Value.</param>
public sealed record PackUseMeasure(double Minutes, IReadOnlyDictionary<uint, int> Use, long LootWanted, long LootKept, int LeftItems, long LeftBurden, long LeftValue)
{
    /// <summary>The count of <paramref name="wcid"/> used, 0 when not measured.</summary>
    /// <param name="wcid">The ware.</param>
    public int UseOf(uint wcid) => Use.TryGetValue(wcid, out var used) ? used : 0;

    /// <summary>The measure in words, for the visit's log.</summary>
    public string Line() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Minutes:0.0} min since the last visit: the loot kept {LootKept} burden and left {LeftItems} item(s) over its load line ({LeftBurden} burden, {LeftValue} Value), {LootWanted} wanted in all ({(Minutes > 0 ? LootWanted / Minutes : 0):0} a minute); used: {(Use.Any(u => u.Value > 0) ? string.Join(", ", Use.Where(u => u.Value > 0).OrderBy(u => u.Key).Select(u => $"{u.Key} x{u.Value}")) : "nothing")}");
}

/// <summary>What a ware of the budget is to it.</summary>
public enum PackLineKind
{
    /// <summary>A stock line a home vendor sells: its buys are cut (a drink line's surplus is also sold).</summary>
    Stock,

    /// <summary>A town stock line: its buys on the way out are cut (a drink line's surplus is also sold).</summary>
    Town,

    /// <summary>A drink she uses that no line names (kept from the loot): what she does not drink between visits is sold when the budget is over.</summary>
    LootDrink,

    /// <summary>Weighed and never cut: a focus, a carried line (no vendor of the ground sells it), a line whose unit burden is not known.</summary>
    Held,
}

/// <summary>One ware the budget weighs.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="Kind">What it is to the budget.</param>
/// <param name="Held">The count held.</param>
/// <param name="Keep">The line's keep (a loot drink's: the count held).</param>
/// <param name="Floor">The line's floor: the higher of its <c>trip_under</c> and <c>stop_under</c> (a town line's: its <c>trip_under</c>; a loot drink's: 0). A cut never goes under it.</param>
/// <param name="Unit">The burden of one.</param>
/// <param name="Drink">True for a drink she uses (<c>ResupplyRun.DrinkInUse</c>): its count over the cut keep is sold.</param>
public sealed record PackLine(uint Wcid, string Name, PackLineKind Kind, int Held, int Keep, int Floor, int Unit, bool Drink)
{
    /// <summary>True when the budget may cut it.</summary>
    public bool Cuttable => Kind != PackLineKind.Held && Unit > 0;
}

/// <summary>The budget's answer for one ware it may cut.</summary>
/// <param name="Line">The ware.</param>
/// <param name="Use">The count used between the two visits.</param>
/// <param name="Need">What the interval needs: the floor and the count used, at least one over the floor (a loot drink: the count used), never over the top.</param>
/// <param name="Top">The count with no cut: the higher of the keep and the count held.</param>
/// <param name="Low">The least the cut can bring it to: the need (a drink, which is sold down), or the higher of the need and the count held (a ware that is not sold, only not bought).</param>
/// <param name="Target">The count after the cut: the keep the buys go to, and for a drink the count kept.</param>
/// <param name="Sell">The drinks over the target, sold.</param>
public sealed record PackCut(PackLine Line, int Use, int Need, int Top, int Low, int Target, int Sell)
{
    /// <summary>The burden the cut takes off the top.</summary>
    public long Saved => (long)(Top - Target) * Line.Unit;

    /// <summary>True when the cut changes anything.</summary>
    public bool Cuts => Target < Top;
}

/// <summary>What the budget decided at a visit.</summary>
public enum PackBudgetVerdict
{
    /// <summary>No visit to measure from (the session's first): the keeps are the profile's, named.</summary>
    Unmeasured,

    /// <summary>The supplies at their keeps fit the budget: nothing is cut.</summary>
    Within,

    /// <summary>The supplies at their keeps are over the budget: the keeps are scaled toward the needs.</summary>
    Scaled,

    /// <summary>The needs alone are over the line even with no room for the loot: every cuttable ware stands between its need and one interval's use, by one scale.</summary>
    AtNeeds,

    /// <summary>M6 audit (the owner, 2026-11-19: about 75 minutes of consumables): the needs fit only with the loot's room cut: the loot yields, every cuttable ware stands at its need, the room left is the loot's.</summary>
    KitFirst,
}

/// <summary>The budget of one visit (<see cref="PackBudgetRule.Plan"/>): its numbers, the verdict and the cuts.</summary>
/// <param name="Verdict">What it decided.</param>
/// <param name="Capacity">Her capacity (the loot plan's: 150 a point of Strength, the buffs in).</param>
/// <param name="LoadLinePercent">The loot's load line, in percent.</param>
/// <param name="Line">The burden at the load line.</param>
/// <param name="Burden">Her burden.</param>
/// <param name="SaleBurden">The loot about to be sold.</param>
/// <param name="SuppliesHeld">The burden of the wares the budget weighs, as held.</param>
/// <param name="Fixed">What is neither loot nor the budget's wares: the worn gear, the packs, the coin and notes, the kept-aside finds.</param>
/// <param name="Room">The room left for the loot between two visits: what the loot kept and left over the load line since the last one.</param>
/// <param name="Minutes">The minutes between the two visits (0: unmeasured).</param>
/// <param name="Budget">The burden the budget's wares may weigh: the line less the fixed and the room.</param>
/// <param name="AtTop">The budget's wares at their keeps (none cut).</param>
/// <param name="AtLow">The budget's wares at their needs.</param>
/// <param name="Planned">The budget's wares after the cuts.</param>
/// <param name="Scale">How far from the needs to the keeps the cut keeps stand (1: no cut).</param>
/// <param name="Cuts">The cuttable wares, in the order given.</param>
/// <param name="Uncut">The wares weighed and never cut.</param>
public sealed record PackBudget(PackBudgetVerdict Verdict, int Capacity, int LoadLinePercent, long Line, long Burden, long SaleBurden, long SuppliesHeld, long Fixed, long Room, double Minutes, long Budget, long AtTop, long AtLow, long Planned, double Scale,
    IReadOnlyList<PackCut> Cuts, IReadOnlyList<PackLine> Uncut)
{
    /// <summary>M6 audit: the room the loot wanted between the two visits (<see cref="PackUseMeasure.LootWanted"/>); <see cref="Room"/> is what it is given, less when the needs come first (<see cref="PackBudgetVerdict.KitFirst"/>).</summary>
    public long Wanted { get; init; }

    /// <summary>The room the plan leaves for the loot: the line less the fixed and the planned wares.</summary>
    public long RoomLeft => Line - Fixed - Planned;

    /// <summary>The cut keep of <paramref name="wcid"/>, or null when it is not cut.</summary>
    /// <param name="wcid">The ware.</param>
    public int? KeepOf(uint wcid)
    {
        foreach (var cut in Cuts)
        {
            if (cut.Line.Wcid == wcid && cut.Cuts && cut.Line.Kind is PackLineKind.Stock or PackLineKind.Town)
            {
                return cut.Target;
            }
        }

        return null;
    }

    /// <summary>The drinks of <paramref name="wcid"/> to sell (0: none).</summary>
    /// <param name="wcid">The ware.</param>
    public int SellOf(uint wcid)
    {
        foreach (var cut in Cuts)
        {
            if (cut.Line.Wcid == wcid)
            {
                return cut.Sell;
            }
        }

        return 0;
    }

    /// <summary>The cut keeps of the lines, by weenie class id (for the memory: the town stock reads them on the way out).</summary>
    public IReadOnlyDictionary<uint, int> Keeps => Cuts.Where(c => c.Cuts && c.Line.Kind is PackLineKind.Stock or PackLineKind.Town).ToDictionary(c => c.Line.Wcid, c => c.Target);

    /// <summary>The budget in one line, every number named (the visit's log, once a visit).</summary>
    public string Summary()
    {
        var c = CultureInfo.InvariantCulture;
        var head = string.Create(c, $"pack budget (m6-pack-room): capacity {Capacity} at the loot's load line {LoadLinePercent}% = {Line}; her burden {Burden} less the loot to sell {SaleBurden} and the supplies weighed {SuppliesHeld} leaves {Fixed} fixed (worn gear, packs, coin, kept-aside)");
        return Verdict switch
        {
            PackBudgetVerdict.Unmeasured => head + string.Create(c, $"; not measured yet (m6-pack-budget-unmeasured: no visit of this session to measure her use and the loot from): the keeps are the profile's this visit; at them the supplies weigh {AtTop} and leave {Line - Fixed - AtTop} for the loot; the full-pack line calls the trip when it fills, and this visit's end starts the measure"),
            PackBudgetVerdict.Within => head + string.Create(c, $"; the loot's room {Room} ({Minutes:0.0} min of it since the last visit); the budget {Budget}; at the keeps the supplies weigh {AtTop}: within it, no keep is cut (room left {RoomLeft})"),
            PackBudgetVerdict.KitFirst => head + string.Create(c, $"; the loot wanted {Wanted} ({Minutes:0.0} min of it since the last visit); at the keeps {AtTop}, at the needs ({PackBudgetRule.SupplyMinutes:0} min of her use) {AtLow}: the needs come first, so the loot's room is cut to {Room} and every cut ware stands at its need: planned {Planned}, room left {RoomLeft}"),
            PackBudgetVerdict.Scaled => head + string.Create(c, $"; the loot's room {Room} ({Minutes:0.0} min of it since the last visit); the budget {Budget}; at the keeps {AtTop}, at the needs {AtLow}: over by {AtTop - Budget}, so the keeps stand {Scale:0.00} of the way from the needs to the keeps: planned {Planned}, room left {RoomLeft}"),
            _ => head + string.Create(c, $"; the loot wanted {Wanted} ({Minutes:0.0} min of it since the last visit); the line less the fixed {Budget}; at the keeps {AtTop}, at the needs ({PackBudgetRule.SupplyMinutes:0} min of her use) {AtLow}: over even with no room for the loot by {AtLow - Budget}, so every cut ware stands {Scale:0.00} of the way from one interval's use to its need: planned {Planned}, room left {RoomLeft} (the full-pack line calls the trip when it fills)"),
        };
    }

    /// <summary>One line for each ware the budget cuts, with its numbers, and one for each drink sold.</summary>
    public IEnumerable<string> CutLines()
    {
        var c = CultureInfo.InvariantCulture;
        foreach (var cut in Cuts.Where(x => x.Cuts))
        {
            var line = cut.Line;
            var need = line.Kind == PackLineKind.LootDrink
                ? string.Create(c, $"need {cut.Need}: {cut.Use} drunk in {Minutes:0.0} min, no line names it")
                : string.Create(c, $"need {cut.Need}: its line {line.Floor} and {PackBudgetRule.SupplyMinutes:0} min of her use ({cut.Use} used in {Minutes:0.0} min)");
            yield return string.Create(c, $"pack budget: cut {line.Name} ({line.Wcid}, {line.Kind}): {cut.Top} -> {cut.Target} (held {line.Held}, keep {line.Keep}; {need}; burden {line.Unit} each, {cut.Saved} saved){(cut.Sell > 0 ? $"; sells {cut.Sell}, the drinks over {cut.Target} (m6-pack-room amends m6-sale-keeps-drinks)" : string.Empty)}");
        }
    }
}

/// <summary>
/// M6 14 pack room (<c>m6-pack-room</c>, the owner's direction of 2026-11-14: size her supplies to her carrying capacity so loot has room): the burden budget for what she carries by choice, derived at each visit from her live state, every
/// number named in the log. No number is the profile's or a constant of the code: the capacity is the loot plan's, the line is the loot's own load line, the room is the loot she took between the last two visits, the needs are her own use
/// over the same minutes.
/// <para><b>The rule</b> (<see cref="Plan"/>, pure): the line (capacity times the loot's load line) less what is neither loot nor the budget's wares (her burden less the loot to sell and the wares weighed: worn gear, packs, coin) less the
/// room for the loot (what the loot plans kept and left over the load line between the two visits: the loot she takes between two trips, its burden a minute times the minutes) is the budget. Within it, nothing changes. Over it, each
/// cuttable ware (a stock or town line's buys; a drink she uses) moves from its top (the higher of its keep and the count held) toward its need (its line, the higher of <c>trip_under</c> and <c>stop_under</c>, plus what she used between the visits and at least one, so a cut line is never left on its trip line, one use from a trip: the M6 14 T failure;
/// a drink no line names: what she drank), all by one scale, so the larger lines give the most burden; a ware that is not a drink is never sold, only not bought, so it never goes under the count held. A drink over its cut
/// keep is sold: what she does not drink between trips. Never under a line's floor.</para>
/// <para><b>Unmeasured</b> (the session's first visit, or none before it this login): nothing is cut and it is said by name (<c>m6-pack-budget-unmeasured</c>); the full-pack line (<see cref="LogisticsCause.FullPack"/>) still calls the
/// trip when the loot fills the pack, and the visit's end starts the measure.</para>
/// </summary>
public static class PackBudgetRule
{
    /// <summary>
    /// M6 audit (the owner's decision of 2026-11-19): the minutes of her own measured use a consumable's need holds over its line, about 75, so a supply lasts past the hourly town trip that the buff bot's
    /// rebuff calls (its lines last an hour; the trip home is due at <c>BuffBotRule</c>'s 420 s left) with a margin. A policy figure, the owner's, not a game fact.
    /// </summary>
    public const double SupplyMinutes = 75;

    /// <summary>
    /// The burden of one of <paramref name="wcid"/>: the supply weenies (<see cref="GearKit.FloorWeenieOf"/>: the foci, the Mana Potion, the Potion of Healing), the spell components (<see cref="GearKit.PrismaticTaperBurden"/>,
    /// <see cref="GearKit.ComponentBurden"/>), the ground's town stock line, else the drinks she uses (<see cref="DrinkBurdens"/>); 0 when none knows it (the ware is weighed by the caller's live read or not at all).
    /// </summary>
    /// <param name="wcid">The ware.</param>
    /// <param name="ground">The ground (its town stock), or null.</param>
    public static int UnitBurdenOf(uint wcid, HuntingGround? ground)
    {
        if (GearKit.FloorWeenieOf(wcid) is { UnitBurden: > 0 } supply)
        {
            return supply.UnitBurden;
        }

        if (ComponentWeenies.TryGetByWcid(wcid, out _))
        {
            return wcid == GearKit.PrismaticTaperWcid ? GearKit.PrismaticTaperBurden : GearKit.ComponentBurden;
        }

        if (ground?.TownStockOf(wcid) is { Burden: > 0 } town)
        {
            return town.Burden;
        }

        return DrinkBurdens.TryGetValue(wcid, out var drink) ? drink : 0;
    }

    /// <summary>
    /// The burden of one of each drink she uses (<see cref="HealthDrinks.All"/>, <see cref="ManaDrinks.All"/>, the Stamina Potion): <c>weenie_properties_int</c> type 5 (EncumbranceVal) of the world database, a read-only SELECT on
    /// 2026-11-14: Health Philtre 27318 150, Health Tonic 27320 100, Health Elixir 2458 75, Health Tincture 27319 50, Potion of Healing 377 15, Health Draught 2457 5, Mana Tincture 27322 50, Mana Potion 379 15, Mana Draught 2460 5,
    /// Stamina Potion 378 15.
    /// </summary>
    public static IReadOnlyDictionary<uint, int> DrinkBurdens { get; } = new Dictionary<uint, int>
    {
        [27318] = 150,
        [27320] = 100,
        [2458] = 75,
        [HealthDrinks.TinctureWcid] = 50,
        [FighterKit.PotionWcid] = 15,
        [VitalsMeans.HealthDraughtWcid] = 5,
        [ManaDrinks.TinctureWcid] = 50,
        [FighterKit.ManaPotionWcid] = 15,
        [VitalsMeans.ManaDraughtWcid] = 5,
        [FighterKit.StaminaPotionWcid] = 15,
    };

    /// <summary>The budget of one visit (see the class).</summary>
    /// <param name="capacity">Her capacity (the loot plan's, <see cref="LootContext.Of"/>).</param>
    /// <param name="loadLinePercent">The loot's load line (<see cref="MageLootThresholds.LoadLineOf"/>); 0, no limit, reads as the whole capacity.</param>
    /// <param name="burden">Her burden (the <c>EncumbranceVal</c> quality).</param>
    /// <param name="saleBurden">The burden of the loot this visit sells.</param>
    /// <param name="lines">The wares weighed.</param>
    /// <param name="measure">What she used and the loot since the last visit, or null (unmeasured).</param>
    /// <param name="ditchDrinks">M6 14 (the owner, 2026-11-14): the stamina dance covers her mana: every drink's keep is 0 and those held are sold, measured or not.</param>
    public static PackBudget Plan(int capacity, int loadLinePercent, long burden, long saleBurden, IReadOnlyList<PackLine> lines, PackUseMeasure? measure, bool ditchDrinks = false) =>
        Ditched(ditchDrinks, PlanCuts(capacity, loadLinePercent, burden, saleBurden, lines, measure));

    /// <summary>The budget with every drink ditched when <paramref name="ditch"/> (its target 0, every one held sold), else as it is.</summary>
    private static PackBudget Ditched(bool ditch, PackBudget budget)
    {
        if (!ditch || !budget.Cuts.Any(x => x.Line.Drink))
        {
            return budget;
        }

        var was = budget.Cuts.Where(x => x.Line.Drink).Sum(x => (long)x.Target * x.Line.Unit);
        var cuts = budget.Cuts.Select(x => x.Line.Drink ? x with { Need = 0, Low = 0, Target = 0, Sell = x.Line.Held } : x).ToList();
        return budget with { Cuts = cuts, Planned = budget.Planned - was };
    }

    private static PackBudget PlanCuts(int capacity, int loadLinePercent, long burden, long saleBurden, IReadOnlyList<PackLine> lines, PackUseMeasure? measure)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var percent = loadLinePercent <= 0 ? 100 : loadLinePercent;
        var line = (long)Math.Max(0, capacity) * percent / 100;
        var held = lines.Sum(l => (long)l.Held * l.Unit);
        var fixedBurden = Math.Max(0, burden - Math.Max(0, saleBurden) - held);
        var uncut = lines.Where(l => !l.Cuttable).ToList();
        var uncutBurden = uncut.Sum(l => (long)l.Held * l.Unit);
        var cuts = new List<PackCut>();
        var shortLows = new List<int>(); // M6 audit: each ware's low at one interval's use, where the budget falls back to when even the needs do not fit the line
        foreach (var l in lines.Where(l => l.Cuttable))
        {
            var top = Math.Max(l.Held, l.Keep);
            var use = measure?.UseOf(l.Wcid) ?? 0;
            var interval = (long)l.Floor + (l.Kind == PackLineKind.LootDrink ? use : Math.Max(use, 1)); // a line at least one over its line: cut to it, her first use would call a trip (the M6 14 T failure)
            var horizon = measure is { Minutes: > 0 } m && use > 0 ? (long)l.Floor + (long)Math.Ceiling(use * SupplyMinutes / m.Minutes) : interval; // M6 audit (the owner, 2026-11-19): 75 minutes of her use over its line
            var need = (int)Math.Min(top, Math.Max(interval, horizon));
            cuts.Add(new PackCut(l, use, need, top, LowOf(l, need, top), top, 0));
            shortLows.Add(LowOf(l, (int)Math.Min(top, interval), top));
        }

        var atTop = uncutBurden + cuts.Sum(x => (long)x.Top * x.Line.Unit);
        var atLow = uncutBurden + cuts.Sum(x => (long)x.Low * x.Line.Unit);
        if (measure is null)
        {
            return new PackBudget(PackBudgetVerdict.Unmeasured, capacity, percent, line, burden, saleBurden, held, fixedBurden, 0, 0, line - fixedBurden, atTop, atLow, atTop, 1, cuts, uncut);
        }

        var wanted = Math.Max(0, measure.LootWanted);
        var room = wanted;
        var budget = line - fixedBurden - room;
        if (atTop <= budget)
        {
            return new PackBudget(PackBudgetVerdict.Within, capacity, percent, line, burden, saleBurden, held, fixedBurden, room, measure.Minutes, budget, atTop, atLow, atTop, 1, cuts, uncut) { Wanted = wanted };
        }

        if (budget < atLow)
        {
            // M6 audit (the owner, 2026-11-19: about 75 minutes of consumables, a town trip an hour with the bot's rebuff): the needs come before the loot's room. Live 2026-11-19 04:03:03 and 04:26:05 UTC
            // (run 4, level 11, the Burrow): the rule of before cut the tapers to their line and one interval's use (200 -> 79, then 92), and the next trip came 23 minutes after the last.
            var free = line - fixedBurden;
            if (free >= atLow)
            {
                for (var i = 0; i < cuts.Count; i++)
                {
                    var x = cuts[i];
                    cuts[i] = x with { Target = x.Low, Sell = x.Line.Drink ? Math.Max(0, x.Line.Held - x.Low) : 0 };
                }

                var kit = uncutBurden + cuts.Sum(x => (long)x.Target * x.Line.Unit);
                return new PackBudget(PackBudgetVerdict.KitFirst, capacity, percent, line, burden, saleBurden, held, fixedBurden, free - atLow, measure.Minutes, atLow, atTop, atLow, kit, 0, cuts, uncut) { Wanted = wanted };
            }

            // Even with no room for the loot the needs are over the line: from the needs toward one interval's use, by one scale (at worst one interval, the rule of before).
            var atShort = uncutBurden + cuts.Select((x, i) => (long)shortLows[i] * x.Line.Unit).Sum();
            var back = atLow == atShort ? 0 : Math.Clamp((double)(free - atShort) / (atLow - atShort), 0, 1);
            for (var i = 0; i < cuts.Count; i++)
            {
                var x = cuts[i];
                var target = shortLows[i] + (int)Math.Floor((x.Low - shortLows[i]) * back);
                cuts[i] = x with { Target = target, Sell = x.Line.Drink ? Math.Max(0, x.Line.Held - target) : 0 };
            }

            var squeezed = uncutBurden + cuts.Sum(x => (long)x.Target * x.Line.Unit);
            return new PackBudget(PackBudgetVerdict.AtNeeds, capacity, percent, line, burden, saleBurden, held, fixedBurden, 0, measure.Minutes, free, atTop, atLow, squeezed, back, cuts, uncut) { Wanted = wanted };
        }

        var scale = atTop == atLow ? 0 : Math.Clamp((double)(budget - atLow) / (atTop - atLow), 0, 1);
        for (var i = 0; i < cuts.Count; i++)
        {
            var x = cuts[i];
            var target = x.Low + (int)Math.Floor((x.Top - x.Low) * scale);
            cuts[i] = x with { Target = target, Sell = x.Line.Drink ? Math.Max(0, x.Line.Held - target) : 0 };
        }

        var planned = uncutBurden + cuts.Sum(x => (long)x.Target * x.Line.Unit);
        return new PackBudget(PackBudgetVerdict.Scaled, capacity, percent, line, burden, saleBurden, held, fixedBurden, room, measure.Minutes, budget, atTop, atLow, planned, scale, cuts, uncut) { Wanted = wanted };
    }

    /// <summary>M6 audit: a ware's low for a need: a drink may go down to it (the rest is sold), any other ware never under the count held (it is only not bought); never under its floor.</summary>
    private static int LowOf(PackLine l, int need, int top)
    {
        var low = l.Drink ? need : Math.Max(l.Held, need);
        return Math.Min(top, Math.Max(low, Math.Min(l.Floor, top)));
    }
}
