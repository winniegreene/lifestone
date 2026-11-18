using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>A worn item the refill is due for: it is out of mana, or it runs out before the next visit home plus the margin.</summary>
/// <param name="Item">The worn item as its appraisal read it.</param>
/// <param name="Need">The mana to its maximum.</param>
/// <param name="SecondsLeft">The seconds until empty at its rate (0 when out, infinity with no rate).</param>
/// <param name="Why">Why it is due, with the numbers.</param>
public readonly record struct ManaDue(WornMana Item, int Need, double SecondsLeft, string Why)
{
    /// <summary>True when the item holds no mana: ACE removed its spells at 0 and only wearing it again brings them back.</summary>
    public bool Out => Item.Mana <= 0;
}

/// <summary>What the refill does next.</summary>
public enum ManaRefillAct
{
    /// <summary>Nothing: nothing is due, or no means is left (the reason says which).</summary>
    None,

    /// <summary>A charged stone or a bought charge is used on one due item or on the player.</summary>
    Use,

    /// <summary>An empty stone is used on a loot item that would otherwise be sold: the item is destroyed and the stone holds a share of its mana.</summary>
    Capture,

    /// <summary>Ready charges join this trip's buy list.</summary>
    Buy,
}

/// <summary>
/// A pack item a stone could drain, with the facts the never-list reads. The caller fills them from the state; <see cref="WornManaRefill.NeverDrain"/> decides.
/// </summary>
/// <param name="Item">The item: its mana, its Value and the payout its sale would bring.</param>
/// <param name="Worn">It is worn or wielded.</param>
/// <param name="QuestItem">A quest file names it (a hunt-until, expected or done item).</param>
/// <param name="KeptAside">The keep-aside rule or the loot step kept it (worth a look, a kept scroll).</param>
/// <param name="KeptCaster">A caster she keeps: rotation gear, or one of the gear floor's two.</param>
/// <param name="ArmourSet">A piece of her armour set that is still to be worn.</param>
/// <param name="Attuned">Attuned: kept for the return to its NPC.</param>
/// <param name="Retained">Retained: ACE refuses the drain (ManaStone.cs:91-92).</param>
/// <param name="Supply">A stock, kit or purse ware, a focus, money, a note, a container or a stone.</param>
/// <param name="MainPack">It lies in the main pack (the sale's items do).</param>
/// <param name="Kept">M6 16 E5c2: the rule book's row that keeps it when none of the facts above names why (a piece of no value, an item no rule names), else null. An item the rule book drops at the home is not kept.</param>
public readonly record struct DrainFacts(
    DrainTarget Item,
    bool Worn = false,
    bool QuestItem = false,
    bool KeptAside = false,
    bool KeptCaster = false,
    bool ArmourSet = false,
    bool Attuned = false,
    bool Retained = false,
    bool Supply = false,
    bool MainPack = true,
    string? Kept = null);

/// <summary>A stone's use the rule picked.</summary>
/// <param name="Stone">The charged stone or charge.</param>
/// <param name="Target">The worn item it is used on, or 0 for the player (ACE then rations it over every worn item under its maximum).</param>
/// <param name="TargetName">The item's name, or "the player".</param>
/// <param name="Predicted">The mana each worn item gains by ACE's rule, by guid.</param>
/// <param name="Waste">The stone's mana that reaches no item (the stone is emptied or destroyed either way).</param>
/// <param name="Why">Why this stone and this target, with the numbers.</param>
public readonly record struct ManaUse(HeldStone Stone, uint Target, string TargetName, IReadOnlyDictionary<uint, int> Predicted, int Waste, string Why)
{
    /// <summary>The mana the use should add in all.</summary>
    public int PredictedTotal => Predicted.Values.Sum();
}

/// <summary>A capture the rule picked: the empty stone, the loot item and what it costs.</summary>
/// <param name="Stone">The empty stone.</param>
/// <param name="Item">The loot item drained (and destroyed).</param>
/// <param name="Captured">The mana the stone will hold: <c>round(efficiency x ItemCurMana)</c> (ManaStone.cs:108).</param>
/// <param name="Cost">The pyreals given up: the item's payout and the stone's expected loss (its destroy chance times its Value).</param>
/// <param name="Why">Why, with the numbers.</param>
public readonly record struct ManaDrain(HeldStone Stone, DrainTarget Item, int Captured, double Cost, string Why)
{
    /// <summary>The pyreals one captured point costs.</summary>
    public double CostPerPoint => Captured > 0 ? Cost / Captured : double.PositiveInfinity;
}

/// <summary>A buy the rule picked: ready charges at a profile vendor.</summary>
/// <param name="Offer">The vendor's ware.</param>
/// <param name="Count">How many.</param>
/// <param name="Each">The mana of one.</param>
/// <param name="Need">The mana the due items need to last two horizons.</param>
/// <param name="Why">Why, with the numbers.</param>
public readonly record struct ManaBuy(ManaOffer Offer, int Count, int Each, int Need, string Why)
{
    /// <summary>The pyreals the buy costs.</summary>
    public long Cost => (long)Count * Offer.Price;
}

/// <summary>What the rule is asked about.</summary>
/// <param name="Worn">Every worn item with a mana maximum, as read from the server now.</param>
/// <param name="Stones">The stones and charges held whose mana is known (an appraised stone, or a charge by its weenie): <see cref="WornManaRefill.StonesOf"/>.</param>
/// <param name="Drains">The pack's items with mana, with the never-list's facts.</param>
/// <param name="Offers">The wares the ground's profile vendors list (the rule picks the charges).</param>
/// <param name="Purse">The pyreals the trip can spend.</param>
/// <param name="NextVisitSeconds">The seconds to her next expected visit home.</param>
/// <param name="MarginSeconds">The margin on top (<see cref="WornManaRefill.Margin"/>).</param>
/// <param name="Skip">Worn items another rule owns (the held caster: <see cref="CasterManaRule"/>): never due here, though a use on the player reaches them.</param>
/// <param name="Failed">The stones and loot items a use of this visit did nothing with: never picked again.</param>
/// <param name="BuyPlanned">True once charges joined this trip's buy list.</param>
public sealed record ManaRefillInput(
    IReadOnlyList<WornMana> Worn,
    IReadOnlyList<HeldStone> Stones,
    IReadOnlyList<DrainFacts> Drains,
    IReadOnlyList<ManaOffer> Offers,
    long Purse,
    double NextVisitSeconds,
    double MarginSeconds,
    IReadOnlySet<uint>? Skip = null,
    IReadOnlySet<uint>? Failed = null,
    bool BuyPlanned = false);

/// <summary>One step of the refill: what, with which stone, loot item or ware, and why with the numbers.</summary>
/// <param name="Act">What.</param>
/// <param name="Due">The worn items due, the most urgent first.</param>
/// <param name="Use">The use, for <see cref="ManaRefillAct.Use"/>.</param>
/// <param name="Drain">The capture, for <see cref="ManaRefillAct.Capture"/>.</param>
/// <param name="Buy">The buy, for <see cref="ManaRefillAct.Buy"/>.</param>
/// <param name="Why">Why.</param>
public sealed record ManaRefillStep(ManaRefillAct Act, IReadOnlyList<ManaDue> Due, ManaUse? Use, ManaDrain? Drain, ManaBuy? Buy, string Why)
{
    /// <summary>The step as the log prints it.</summary>
    public override string ToString() => Act switch
    {
        ManaRefillAct.Use => "gear mana: refill: use: " + Why,
        ManaRefillAct.Capture => "gear mana: refill: capture: " + Why,
        ManaRefillAct.Buy => "gear mana: refill: buy: " + Why,
        _ => "gear mana: refill: nothing more: " + Why,
    };
}

/// <summary>
/// M6 item 14 (closes <c>m5-stone-field-refill</c> and <c>m5-stone-short</c>): the rule that keeps every worn item with built-in spells from running out of mana, with no engine in it. Given the worn items' mana, their burn rates and the
/// time to her next visit home, it says which items are due, how much they need and by when, and picks the means, the cheapest sound one first.
/// </summary>
/// <remarks>
/// <para><b>Due.</b> A worn item is due when it is out of mana, or when it would run out before her next expected visit home plus a margin (<see cref="Due"/>): never only once it is empty. The next visit is the longest of her last
/// home visits' gaps (<see cref="WornManaMemory"/>), the margin one more gap and at least <see cref="MarginFloorSeconds"/>, so an item lasts through a visit that comes late or is missed.</para>
/// <para><b>The game's rules</b> (ManaStone.cs @95ef426d). A charged stone used on one item pours <c>min(the item's need, the stone's mana)</c> into it (:192-225); used on the player it rations its mana evenly over every worn item
/// under its maximum, round after round (:121-162, <see cref="PourOnPlayer"/>). Either way the stone is then emptied, or destroyed on a roll under its destroy chance (:235-252): what did not reach an item is lost. An empty stone used
/// on an item with mana takes <c>round(efficiency x mana)</c> and destroys the item (:84-117). A Mana Charge is a stone sold full, of efficiency 1 and destroy chance 1.</para>
/// <para><b>The means, by cost.</b> (1) A charged stone or charge she holds (<see cref="PickUse"/>). (2) A capture from a loot item she would otherwise sell, with the empty stone of the highest efficiency, on the item whose captured
/// point costs the fewest pyreals of payout forgone (<see cref="PickDrain"/>: much mana, little sale value), when that is no dearer than a bought point. (3) Ready charges at a profile vendor, the cheapest pyreals a point
/// (<see cref="PickCharges"/>). (4) A capture dearer than a bought point when nothing can be bought. Buying uncharged stones is never a means in Holtburg: Archmage Cindrue sells only the Minor (efficiency 0.1) and the Lesser (0.25), and a
/// capture at 0.25 costs more a point than his Tiny Mana Charge (473 pyreals for 100: 4.73 a point).</para>
/// <para><b>The never-list</b> (<see cref="NeverDrain"/>; M6 16 E5c2: also an item any other row of the rule book keeps): a worn item, a quest item, an item kept aside, a caster she keeps, a piece of her armour set, an attuned or retained item, a supply ware, and anything outside the main pack.</para>
/// <para><b>The reserve.</b> With nothing due, one capture a visit keeps <see cref="ReserveCharged"/> charged stone for the field (<see cref="WornManaWatch"/> uses it when an item would run out before she is next home), only at a
/// cost a point no dearer than a bought one.</para>
/// <para><b>Not retail:</b> the horizon, the reserve and the order of the means are Lifestone's own; the stones, the charges, their use and their effect are ACE's.</para>
/// </remarks>
public static class WornManaRefill
{
    /// <summary>The gap between two visits home when none was measured yet: the long run's supply trips came about every 46 minutes (docs/session.md). Lifestone's own.</summary>
    public const double DefaultVisitGapSeconds = 2760;

    /// <summary>The least margin on top of the next visit: half an hour. Lifestone's own.</summary>
    public const double MarginFloorSeconds = 1800;

    /// <summary>The charged stones kept for the field once nothing is due. Lifestone's own.</summary>
    public const int ReserveCharged = 1;

    /// <summary>The most charges one trip buys. Lifestone's own.</summary>
    public const int MaxChargesATrip = 12;

    /// <summary>The mana stones' ManaStoneDestroyChance by weenie (PropertyFloat 137, the pinned world SQL v0.9.296), for a stone whose appraisal lacks it: Minor 0.025, Lesser 0.05, Mana Stone 0.1, Moderate 0.15, Greater 0.2, Major 0.25; a Mana Charge 1.</summary>
    public static IReadOnlyDictionary<uint, double> DestroyChanceByWcid { get; } = new Dictionary<uint, double>
    {
        [27331] = 0.025, [2434] = 0.05, [2435] = 0.1, [27330] = 0.15, [2436] = 0.2, [27328] = 0.25,
    };

    /// <summary>The margin for a visit gap: one more gap, at least <see cref="MarginFloorSeconds"/>.</summary>
    /// <param name="visitGapSeconds">The expected seconds between two visits home.</param>
    public static double Margin(double visitGapSeconds) => Math.Max(visitGapSeconds, MarginFloorSeconds);

    /// <summary>The worn items due, the most urgent first: out of mana, or under <paramref name="nextVisitSeconds"/> plus <paramref name="marginSeconds"/> left at the item's rate. An item at its maximum is never due.</summary>
    /// <param name="worn">The worn items with their mana.</param>
    /// <param name="nextVisitSeconds">The seconds to her next expected visit home.</param>
    /// <param name="marginSeconds">The margin on top.</param>
    /// <param name="skip">Items another rule owns, or null.</param>
    public static List<ManaDue> Due(IReadOnlyList<WornMana> worn, double nextVisitSeconds, double marginSeconds, IReadOnlySet<uint>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var horizon = nextVisitSeconds + marginSeconds;
        var due = new List<ManaDue>();
        foreach (var w in worn)
        {
            var need = w.MaxMana - w.Mana;
            if (w.MaxMana <= 0 || need <= 0 || skip?.Contains(w.Guid) == true)
            {
                continue;
            }

            if (w.Mana <= 0)
            {
                due.Add(new ManaDue(w, need, 0, Text($"'{w.Name}' 0/{w.MaxMana}: out of mana, its spells are off")));
            }
            else if (w.SecondsLeft < horizon)
            {
                due.Add(new ManaDue(w, need, w.SecondsLeft, Text($"'{w.Name}' {w.Mana}/{w.MaxMana}: {w.SecondsLeft / 60:0} min left at {-w.ManaRate:0.####}/s, under the next visit in {nextVisitSeconds / 60:0} min plus {marginSeconds / 60:0} min")));
            }
        }

        due.Sort((a, b) => a.SecondsLeft != b.SecondsLeft ? a.SecondsLeft.CompareTo(b.SecondsLeft) : a.Item.Guid.CompareTo(b.Item.Guid));
        return due;
    }

    /// <summary>
    /// ACE's ration of a stone used on the player (ManaStone.cs:121-162 @95ef426d): round after round, <c>max(left / items needing mana, 1)</c> to each worn item under its maximum, capped at its need, until the stone or the needs end.
    /// </summary>
    /// <param name="stoneMana">The stone's mana.</param>
    /// <param name="worn">The worn items (ACE walks its equipped objects; the order matters only for the last odd points).</param>
    /// <param name="left">The mana that reached no item.</param>
    /// <returns>The mana each item gains, by guid (only the items that gain).</returns>
    public static Dictionary<uint, int> PourOnPlayer(int stoneMana, IReadOnlyList<WornMana> worn, out int left)
    {
        ArgumentNullException.ThrowIfNull(worn);
        var needing = worn.Where(w => w.MaxMana > 0 && w.Mana < w.MaxMana).ToList();
        var gains = new Dictionary<uint, int>();
        left = Math.Max(0, stoneMana);
        while (left > 0)
        {
            var now = needing.Where(w => w.Mana + gains.GetValueOrDefault(w.Guid) < w.MaxMana).ToList();
            if (now.Count == 0)
            {
                break;
            }

            var ration = Math.Max(left / now.Count, 1);
            foreach (var w in now)
            {
                var add = Math.Min(ration, w.MaxMana - w.Mana - gains.GetValueOrDefault(w.Guid));
                left -= add;
                gains[w.Guid] = gains.GetValueOrDefault(w.Guid) + add;
                if (left <= 0)
                {
                    break;
                }
            }
        }

        return gains;
    }

    /// <summary>
    /// Picks the stone and its target for the most urgent due item. A stone that holds no more than the item needs goes on the item (all of it lands where it is due; the largest such stone). Otherwise the smallest stone: on the player
    /// when ACE's ration then leaves nothing due (nothing is lost while other items take the rest), else on the item, the rest lost and named.
    /// </summary>
    /// <param name="due">The due items, the most urgent first (not empty).</param>
    /// <param name="worn">Every worn item with mana (the ration reaches them all).</param>
    /// <param name="charged">The charged stones and charges held (not empty).</param>
    /// <param name="nextVisitSeconds">The seconds to her next expected visit home.</param>
    /// <param name="marginSeconds">The margin on top.</param>
    /// <param name="skip">Items another rule owns, or null.</param>
    public static ManaUse PickUse(IReadOnlyList<ManaDue> due, IReadOnlyList<WornMana> worn, IReadOnlyList<HeldStone> charged, double nextVisitSeconds, double marginSeconds, IReadOnlySet<uint>? skip = null)
    {
        ArgumentNullException.ThrowIfNull(due);
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(charged);
        var urgent = due[0];
        var fit = charged.Where(s => s.Mana <= urgent.Need).OrderByDescending(s => s.Mana).ThenBy(s => s.Guid).FirstOrDefault();
        if (fit.Guid != 0)
        {
            var all = fit.Mana ?? 0;
            return new ManaUse(fit, urgent.Item.Guid, urgent.Item.Name, new Dictionary<uint, int> { [urgent.Item.Guid] = all }, 0, Text($"'{fit.Name}' 0x{fit.Guid:X8} ({all}) on '{urgent.Item.Name}' 0x{urgent.Item.Guid:X8}, which needs {urgent.Need}: all of it lands there"));
        }

        var stone = charged.OrderBy(s => s.Mana).ThenBy(s => s.Guid).First();
        var mana = stone.Mana ?? 0;
        var gains = PourOnPlayer(mana, worn, out var left);
        var after = worn.Select(w => w with { Mana = w.Mana + gains.GetValueOrDefault(w.Guid) }).ToList();
        var still = Due(after, nextVisitSeconds, marginSeconds, skip);
        if (still.Count == 0)
        {
            return new ManaUse(stone, 0, "the player", gains, left, Text($"'{stone.Name}' 0x{stone.Guid:X8} ({mana}) on the player: more than '{urgent.Item.Name}' needs ({urgent.Need}); ACE's even ration gives {string.Join(", ", gains.Select(g => $"{g.Value} to '{worn.First(w => w.Guid == g.Key).Name}'"))} and leaves nothing due{(left > 0 ? $"; {left} reaches no item" : string.Empty)}"));
        }

        return new ManaUse(stone, urgent.Item.Guid, urgent.Item.Name, new Dictionary<uint, int> { [urgent.Item.Guid] = urgent.Need }, mana - urgent.Need, Text($"'{stone.Name}' 0x{stone.Guid:X8} ({mana}) on '{urgent.Item.Name}' 0x{urgent.Item.Guid:X8}, which needs {urgent.Need}: on the player the even ration would leave '{still[0].Item.Name}' due; {mana - urgent.Need} of the stone is lost"));
    }

    /// <summary>Why <paramref name="facts"/> must never be drained, or null when it is loot she would otherwise sell.</summary>
    /// <param name="facts">The item's facts.</param>
    public static string? NeverDrain(in DrainFacts facts) =>
        facts.Worn ? "worn"
        : facts.QuestItem ? "a quest item"
        : facts.KeptAside ? "kept aside"
        : facts.KeptCaster ? "a caster she keeps"
        : facts.ArmourSet ? "a piece of her armour set"
        : facts.Attuned ? "attuned"
        : facts.Retained ? "retained (ACE refuses the drain)"
        : facts.Supply ? "a supply ware"
        : facts.Kept is { } rule ? "the pack rules keep it (" + rule + ")"
        : !facts.MainPack ? "not in the main pack"
        : facts.Item.Mana <= 0 || facts.Item.MaxMana <= 0 ? "no mana"
        : null;

    /// <summary>
    /// Picks the capture: the empty stone of the highest efficiency on the loot item whose captured point costs the fewest pyreals (the payout forgone and the stone's expected loss, over <c>round(efficiency x mana)</c>); among equals the
    /// one with more mana. Null with no empty stone of a known efficiency or no item the never-list lets through.
    /// </summary>
    /// <param name="drains">The pack's items with mana.</param>
    /// <param name="stones">The stones held.</param>
    /// <param name="failed">The stones and items a use of this visit did nothing with, or null.</param>
    /// <param name="decisions">Filled with one line per item weighed or refused, or null.</param>
    public static ManaDrain? PickDrain(IReadOnlyList<DrainFacts> drains, IReadOnlyList<HeldStone> stones, IReadOnlySet<uint>? failed = null, List<string>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(drains);
        ArgumentNullException.ThrowIfNull(stones);
        var stone = stones.Where(s => !s.Charged && s.Efficiency > 0 && failed?.Contains(s.Guid) != true).OrderByDescending(s => s.Efficiency).ThenBy(s => s.Guid).FirstOrDefault();
        if (stone.Guid == 0)
        {
            return null;
        }

        var loss = stone.DestroyChance * stone.Value;
        ManaDrain? best = null;
        foreach (var f in drains)
        {
            if (failed?.Contains(f.Item.Guid) == true)
            {
                continue;
            }

            if (NeverDrain(f) is { } never)
            {
                decisions?.Add(Text($"gear mana: refill: never drained: '{f.Item.Name}' 0x{f.Item.Guid:X8} ({f.Item.Mana} mana): {never}"));
                continue;
            }

            var captured = (int)Math.Round(stone.Efficiency * f.Item.Mana, MidpointRounding.ToEven);
            if (captured <= 0)
            {
                continue;
            }

            var cost = f.Item.Forgone + loss;
            var pick = new ManaDrain(stone, f.Item, captured, cost, Text($"'{stone.Name}' 0x{stone.Guid:X8} (efficiency {stone.Efficiency:0.##}) on '{f.Item.Name}' 0x{f.Item.Guid:X8}: {f.Item.Mana} mana -> {captured} in the stone for a payout of {f.Item.Forgone} forgone{(loss > 0 ? $" and {loss:0} for the stone's {stone.DestroyChance * 100:0.#}% destroy chance" : string.Empty)}: {cost / captured:0.00} pyreals a point"));
            decisions?.Add("gear mana: refill: weigh " + pick.Why);
            if (best is not { } b || pick.CostPerPoint < b.CostPerPoint || (pick.CostPerPoint == b.CostPerPoint && captured > b.Captured))
            {
                best = pick;
            }
        }

        return best;
    }

    /// <summary>The cheapest pyreals a point of a ready charge on offer (<see cref="CasterManaRule.ChargeMana"/>, a price above 0), with its ware, or null when no profile vendor sells one.</summary>
    /// <param name="offers">The wares the profile vendors list.</param>
    public static (double PerPoint, ManaOffer Offer, int Each)? CheapestCharge(IReadOnlyList<ManaOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        (double PerPoint, ManaOffer Offer, int Each)? best = null;
        foreach (var o in offers)
        {
            if (o.Price > 0 && CasterManaRule.ChargeMana.TryGetValue(o.Wcid, out var each) && each > 0)
            {
                var per = (double)o.Price / each;
                if (best is not { } b || per < b.PerPoint || (per == b.PerPoint && o.Wcid < b.Offer.Wcid))
                {
                    best = (per, o, each);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Picks the charges to buy: the cheapest a point on offer, as many as carry every due item through two horizons (never over its maximum), at most <see cref="MaxChargesATrip"/> and what the purse pays. Null with no charge on offer
    /// or a purse short of one.
    /// </summary>
    /// <param name="due">The due items.</param>
    /// <param name="offers">The wares the profile vendors list.</param>
    /// <param name="purse">The pyreals the trip can spend.</param>
    /// <param name="horizonSeconds">The next visit plus the margin.</param>
    public static ManaBuy? PickCharges(IReadOnlyList<ManaDue> due, IReadOnlyList<ManaOffer> offers, long purse, double horizonSeconds)
    {
        ArgumentNullException.ThrowIfNull(due);
        if (due.Count == 0 || CheapestCharge(offers) is not { } cheapest || purse < cheapest.Offer.Price)
        {
            return null;
        }

        var need = 0;
        foreach (var d in due)
        {
            var twoHorizons = d.Item.ManaRate < 0 ? (int)Math.Ceiling(-d.Item.ManaRate * 2 * horizonSeconds) - d.Item.Mana : d.Need;
            need += Math.Clamp(twoHorizons, 1, d.Need);
        }

        var wanted = (int)Math.Ceiling((double)need / cheapest.Each);
        var count = (int)Math.Min(Math.Min(wanted, MaxChargesATrip), purse / cheapest.Offer.Price);
        return new ManaBuy(cheapest.Offer, count, cheapest.Each, need, Text($"{count} x wcid {cheapest.Offer.Wcid} ({cheapest.Each} mana each for {cheapest.Offer.Price}: {cheapest.PerPoint:0.00} pyreals a point) at {cheapest.Offer.VendorName}: the due items need {need} to last two horizons of {horizonSeconds / 60:0} min{(count < wanted ? $"; {wanted} wanted, the purse {purse} and the cap {MaxChargesATrip} give {count}" : string.Empty)}"));
    }

    /// <summary>The next step (see the class remarks).</summary>
    /// <param name="input">What is worn, held, drainable and on offer.</param>
    /// <param name="decisions">Filled with one line per loot item weighed or refused for a capture, or null.</param>
    public static ManaRefillStep Next(ManaRefillInput input, List<string>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var due = Due(input.Worn, input.NextVisitSeconds, input.MarginSeconds, input.Skip);
        var charged = input.Stones.Where(s => s.Charged && input.Failed?.Contains(s.Guid) != true).ToList();
        var cheapest = CheapestCharge(input.Offers);
        if (due.Count == 0)
        {
            var every = Text($"every worn item lasts past the next visit in {input.NextVisitSeconds / 60:0} min plus {input.MarginSeconds / 60:0} min; {charged.Count} charged stone(s) held");
            if (charged.Count < ReserveCharged && cheapest is { } price && PickDrain(input.Drains, input.Stones, input.Failed, decisions) is { } spare && spare.CostPerPoint <= price.PerPoint)
            {
                return new ManaRefillStep(ManaRefillAct.Capture, due, null, spare, null, Text($"the field's reserve ({charged.Count} charged of {ReserveCharged}): {spare.Why}, no dearer than a bought point ({price.PerPoint:0.00})"));
            }

            return new ManaRefillStep(ManaRefillAct.None, due, null, null, null, every);
        }

        var dues = string.Join("; ", due.Select(d => d.Why));
        if (charged.Count > 0)
        {
            var use = PickUse(due, input.Worn, charged, input.NextVisitSeconds, input.MarginSeconds, input.Skip);
            return new ManaRefillStep(ManaRefillAct.Use, due, use, null, null, Text($"{use.Why} (due: {dues})"));
        }

        if (input.BuyPlanned)
        {
            return new ManaRefillStep(ManaRefillAct.None, due, null, null, null, Text($"{due.Count} item(s) still due ({dues}); the charges on this trip's buy list are used once she holds them (the field refill, else the next visit)"));
        }

        var drain = PickDrain(input.Drains, input.Stones, input.Failed, decisions);
        var buy = PickCharges(due, input.Offers, input.Purse, input.NextVisitSeconds + input.MarginSeconds);
        if (drain is { } d && (buy is null || cheapest is not { } bought || d.CostPerPoint <= bought.PerPoint))
        {
            return new ManaRefillStep(ManaRefillAct.Capture, due, null, d, null, Text($"{d.Why}{(cheapest is { } c ? (d.CostPerPoint <= c.PerPoint ? $", no dearer than a bought point ({c.PerPoint:0.00})" : $", dearer than a bought point ({c.PerPoint:0.00}) but no charge can be bought (purse {input.Purse})") : ", and no vendor here sells a charge")} (due: {dues})"));
        }

        if (buy is { Count: > 0 } b)
        {
            return new ManaRefillStep(ManaRefillAct.Buy, due, null, null, b, Text($"{b.Why}{(drain is { } dearer ? $"; the cheapest capture would cost {dearer.CostPerPoint:0.00} a point" : "; no empty stone with loot to drain is held")} (due: {dues})"));
        }

        var stones = input.Stones.Count(s => !s.Charged);
        var why = cheapest is { } offer ? Text($"the purse {input.Purse} is short of one charge at {offer.Offer.Price} ({offer.Offer.VendorName})") : "no vendor here sells a Mana Charge";
        return new ManaRefillStep(ManaRefillAct.None, due, null, null, null, Text($"{due.Count} item(s) due and NO MEANS (named: mana-refill-no-means): no charged stone, {stones} empty stone(s) with no loot to drain, and {why} ({dues})"));
    }

    /// <summary>
    /// The stones and charges held whose mana is known: an appraised stone (its ItemCurMana, null when empty; its efficiency and destroy chance from the appraisal, else the world SQL's tables), or an unappraised Mana Charge by its weenie
    /// (<see cref="CasterManaRule.ChargeMana"/>: sold full, destroyed by its one use). An unappraised stone is left out: used as an empty one while charged it would pour its mana into the loot item (ManaStone.cs:192-225).
    /// </summary>
    /// <param name="state">The read model.</param>
    public static List<HeldStone> StonesOf(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var list = new List<HeldStone>();
        foreach (var item in PackLayout.Items(state))
        {
            if ((item.ItemType & PackRules.ManaStoneType) == 0)
            {
                continue;
            }

            var value = state.Objects.TryGet(item.Guid, out var o) ? o.Weenie.Value ?? 0 : 0;
            var isCharge = CasterManaRule.ChargeMana.TryGetValue(item.Wcid, out var sold);
            if (state.Appraisals.TryGet(item.Guid, out var a) && !a.IsNotFound)
            {
                list.Add(new HeldStone(
                    item.Guid,
                    item.Name,
                    a.ItemMana,
                    a.Float(ItemEfficiencyFloat) ?? (isCharge ? 1.0 : ItemManaKeeper.EfficiencyByWcid.GetValueOrDefault(item.Wcid)),
                    a.Float(DestroyChanceFloat) ?? (isCharge ? 1.0 : DestroyChanceByWcid.GetValueOrDefault(item.Wcid)),
                    value));
            }
            else if (isCharge)
            {
                list.Add(new HeldStone(item.Guid, item.Name, sold, 1.0, 1.0, value));
            }
        }

        return list;
    }

    /// <summary>ACE's PropertyFloat ItemEfficiency (87), an assessment property.</summary>
    public const uint ItemEfficiencyFloat = 87;

    /// <summary>ACE's PropertyFloat ManaStoneDestroyChance (137), an assessment property.</summary>
    public const uint DestroyChanceFloat = 137;

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// M6 item 14: what the refill remembers across the run's scenarios (the home step lives in the resupply, the field refill in the autopilot's <see cref="WornManaWatch"/>), shared as <see cref="PackMemory.Shared"/> is: when she was last
/// home and the gaps between her visits (the time to her next one), and the worn pieces already worn again once to wake their spells.
/// </summary>
public sealed class WornManaMemory
{
    /// <summary>The gaps kept: the next visit is expected after the longest of them.</summary>
    public const int GapsKept = 3;

    /// <summary>A gap under this is the same visit (a trip run again by its counters): not a new gap. Lifestone's own.</summary>
    public const double SameVisitSeconds = 60;

    /// <summary>The longest gap the memory believes: a pause for a gate or a long quest is not her pace. Lifestone's own.</summary>
    public const double MaxVisitGapSeconds = 4 * 3600;

    /// <summary>The field refill's floor for the time to the next visit once she is overdue: an item with under ten minutes left is still refilled. Lifestone's own.</summary>
    public const double FieldFloorSeconds = 600;

    /// <summary>Seconds after the home step's last act in which the field refill stands back (the home step owns the stones then). Lifestone's own.</summary>
    public const double HomeHoldSeconds = 120;

    private readonly object _gate = new();
    private readonly List<double> _gaps = [];
    private readonly HashSet<uint> _reworn = [];
    private double _lastVisit = double.NaN;
    private double _homeAt = double.NaN;

    /// <summary>The run's one memory.</summary>
    public static WornManaMemory Shared { get; } = new();

    /// <summary>The expected seconds between two visits home: the longest of the last <see cref="GapsKept"/> gaps, <see cref="WornManaRefill.DefaultVisitGapSeconds"/> before any was measured.</summary>
    public double ExpectedGap
    {
        get
        {
            lock (_gate)
            {
                return _gaps.Count == 0 ? WornManaRefill.DefaultVisitGapSeconds : _gaps.Max();
            }
        }
    }

    /// <summary>Notes a visit home at <paramref name="now"/> (the session clock) and returns the expected gap to the next one. A clock that went back (a new session) forgets the gaps.</summary>
    /// <param name="now">The session clock.</param>
    public double NoteHomeVisit(double now)
    {
        lock (_gate)
        {
            if (!double.IsNaN(_lastVisit))
            {
                var gap = now - _lastVisit;
                if (gap < 0)
                {
                    _gaps.Clear();
                }
                else if (gap >= SameVisitSeconds)
                {
                    _gaps.Add(Math.Min(gap, MaxVisitGapSeconds));
                    if (_gaps.Count > GapsKept)
                    {
                        _gaps.RemoveAt(0);
                    }
                }
            }

            _lastVisit = _homeAt = now;
        }

        return ExpectedGap;
    }

    /// <summary>The home step is at work at <paramref name="now"/>: the field refill stands back for <see cref="HomeHoldSeconds"/>.</summary>
    /// <param name="now">The session clock.</param>
    public void TouchHome(double now)
    {
        lock (_gate)
        {
            _homeAt = now;
        }
    }

    /// <summary>True while the home step acted within the last <see cref="HomeHoldSeconds"/>.</summary>
    /// <param name="now">The session clock.</param>
    public bool HomeStepActive(double now)
    {
        lock (_gate)
        {
            return !double.IsNaN(_homeAt) && now >= _homeAt && now - _homeAt < HomeHoldSeconds;
        }
    }

    /// <summary>The seconds to her next expected visit home at <paramref name="now"/>: the expected gap less the time since the last visit, never under <see cref="FieldFloorSeconds"/>; the whole expected gap before any visit.</summary>
    /// <param name="now">The session clock.</param>
    public double SecondsToNextVisit(double now)
    {
        var gap = ExpectedGap;
        lock (_gate)
        {
            return double.IsNaN(_lastVisit) || now < _lastVisit ? gap : Math.Max(FieldFloorSeconds, gap - (now - _lastVisit));
        }
    }

    /// <summary>True when <paramref name="guid"/> was already worn again once to wake its spells.</summary>
    /// <param name="guid">The worn piece.</param>
    public bool RewearTried(uint guid)
    {
        lock (_gate)
        {
            return _reworn.Contains(guid);
        }
    }

    /// <summary>Notes that <paramref name="guid"/> was worn again to wake its spells.</summary>
    /// <param name="guid">The worn piece.</param>
    public void MarkRewear(uint guid)
    {
        lock (_gate)
        {
            _reworn.Add(guid);
        }
    }

    /// <summary>Forgets everything (a test's fresh start).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _gaps.Clear();
            _reworn.Clear();
            _lastVisit = _homeAt = double.NaN;
        }
    }
}
