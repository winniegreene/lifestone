using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>A worn piece's own mana as an appraisal read it.</summary>
/// <param name="Guid">The piece.</param>
/// <param name="Name">Its name.</param>
/// <param name="Mana">ItemCurMana (PropertyInt 107).</param>
/// <param name="MaxMana">ItemMaxMana (PropertyInt 108).</param>
/// <param name="ManaRate">ManaRate (PropertyFloat 5, negative: mana a second), 0 when the appraisal has none.</param>
public readonly record struct WornMana(uint Guid, string Name, int Mana, int MaxMana, double ManaRate)
{
    /// <summary>The seconds until empty at the rate (ACE burns <c>-ManaRate</c> a second while the spells are on), or infinity with no rate.</summary>
    public double SecondsLeft => ManaRate < 0 ? Mana / -ManaRate : double.PositiveInfinity;
}

/// <summary>A mana stone the mage holds: its own mana (null when empty: ACE removes ItemCurMana when a stone is spent), its efficiency (PropertyFloat 87) and its destroy chance (PropertyFloat 137) as an appraisal read them.</summary>
/// <param name="Guid">The stone.</param>
/// <param name="Name">Its name.</param>
/// <param name="Mana">ItemCurMana, or null for an empty stone.</param>
/// <param name="Efficiency">ItemEfficiency (PropertyFloat 87, an assessment property): the share of a drained item's mana the stone takes (ManaStone.cs:108); 0 when not known.</param>
public readonly record struct HeldStone(uint Guid, string Name, int? Mana, double Efficiency = 0)
{
    /// <summary>True when it holds mana.</summary>
    public bool Charged => Mana is > 0;
}

/// <summary>A main-pack item a stone could drain: its mana (ItemCurMana), its sale payout forgone and whether ACE keeps it (Retained refuses the drain).</summary>
/// <param name="Guid">The item.</param>
/// <param name="Name">Its name.</param>
/// <param name="Mana">ItemCurMana.</param>
/// <param name="MaxMana">ItemMaxMana.</param>
/// <param name="Value">Its Value.</param>
/// <param name="Payout">M5 7 S2b: what the best buyer of the ground pays for it (D96's payout, <c>max(1, floor(rate x Value + 0.1))</c>, Vendor.cs 591-598): the pyreals a drain gives up; null reads the Value.</param>
public readonly record struct DrainTarget(uint Guid, string Name, int Mana, int MaxMana, long Value, long? Payout = null)
{
    /// <summary>The pyreals the drain gives up.</summary>
    public long Forgone => Payout ?? Value;
}

/// <summary>What the keeper does next.</summary>
public enum ItemManaAct
{
    /// <summary>Nothing is due.</summary>
    None,

    /// <summary>A charged stone is used on the player: ACE pours it into every equipped item under its maximum.</summary>
    Refill,

    /// <summary>An empty stone is used on a junk magic item of the main pack: ACE drains the item's mana into the stone and destroys the item.</summary>
    Capture,
}

/// <summary>One step of the keeper: what, which stone, which target, and why with the numbers.</summary>
/// <param name="Act">What.</param>
/// <param name="Stone">The stone used.</param>
/// <param name="Target">The target (the player for a refill, the item for a capture).</param>
/// <param name="Why">Why.</param>
public readonly record struct ItemManaStep(ItemManaAct Act, uint Stone, uint Target, string Why)
{
    /// <summary>The step as the log prints it.</summary>
    public override string ToString() => Act switch
    {
        ItemManaAct.Refill => string.Create(CultureInfo.InvariantCulture, $"gear mana: refill: the stone 0x{Stone:X8} on the player: {Why}"),
        ItemManaAct.Capture => string.Create(CultureInfo.InvariantCulture, $"gear mana: capture: the empty stone 0x{Stone:X8} on 0x{Target:X8}: {Why}"),
        _ => "gear mana: nothing due: " + Why,
    };
}

/// <summary>
/// M5 7 D125: the worn items' own mana, kept as retail plays it with mana stones. Pure over appraisals; one step at a time (the caller sends it, appraises again and asks for the next).
/// </summary>
/// <remarks>
/// <para><b>The drain.</b> ACE burns a worn item's mana while its spells are on, <c>-ManaRate</c> a second accumulated on the ~5 s heartbeat (<c>ManaConsumersTick</c>, Player_Tick.cs:624-660 @95ef426d), warns "Your X is low on Mana." under 120 s
/// left (<c>CheckLowMana</c>, :662-679) and at 0 says "Your X is out of Mana." and removes the item's spells (<c>HandleManaDepleted</c>, :681-699). A stone gives mana and nothing else: a piece whose spells dropped wakes only when worn again
/// (<c>TryActivateSpells</c> runs at the wield, Player_Inventory.cs:313-315; the fighters' <see cref="Lifestone.Client.Session.Fighter.GearRecharge"/> re-wears it).</para>
/// <para><b>The refill</b> (ManaStone.cs:119-190): a charged stone used on the player pours its mana into every equipped item under its maximum, rationed evenly; afterwards the stone is destroyed on a roll under its ManaStoneDestroyChance
/// (:235-252; the Greater Mana Stone's is 0.2, the pinned world SQL's weenie 2436), else it is left empty for another capture. Due when a worn piece is under <see cref="RefillBelow"/> of its maximum or has under
/// <see cref="RefillHorizonSeconds"/> left at its rate. The stone with the least mana that covers the need is used, else the fullest.</para>
/// <para><b>The capture</b> (ManaStone.cs:84-117): an empty stone used on an item of the inventory that has mana and a maximum takes <c>round(ItemEfficiency x ItemCurMana)</c> (the Greater Mana Stone's efficiency 2.0) and destroys the item; a
/// Retained item is refused. Due while the mage wears a piece with its own mana, the charged stones are under <see cref="ChargedWanted"/> and an empty one is held (no loot is destroyed for mana nothing worn needs); the most efficient empty stone is used on the main pack's junk item whose captured mana is worth the most pyreals over its payout, and only when it is worth more than the payout (<see cref="Weigh"/>, every item weighed is logged).
/// Never a worn piece, a review or kit-pack item or one the keep-aside rule kept: the caller passes the main pack's sale items only.</para>
/// <para><b>Not retail:</b> the thresholds (<see cref="RefillBelow"/>, <see cref="RefillHorizonSeconds"/>, <see cref="ChargedWanted"/>) are Lifestone's own; the stone, its use and its effect are ACE's.</para>
/// </remarks>
public static class ItemManaKeeper
{
    /// <summary>A worn piece under this share of its maximum is refilled (Lifestone's own; the fighters' <c>GearRecharge.RechargeBelow</c>).</summary>
    public const double RefillBelow = 0.5;

    /// <summary>A worn piece with fewer seconds left at its rate is refilled: two hours, two sell trips at the den's pace (a trip every two buff cycles of at most 30 minutes). Lifestone's own.</summary>
    public const double RefillHorizonSeconds = 7200;

    /// <summary>The charged stones the kit keeps: captures run while fewer are held and an empty stone is (Lifestone's own).</summary>
    public const int ChargedWanted = 2;

    /// <summary>M5 7 S2b: the mana stones, empty or charged, the kit keeps: the <see cref="ChargedWanted"/> charged ones and as many empty ones to capture with. Lifestone's own.</summary>
    public const int StoneTarget = 4;

    /// <summary>M5 7 S2b: the stones the kit still wants: <see cref="StoneTarget"/> less the mana stones held (anywhere: the packs and the side packs).</summary>
    public static int StonesWanted(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var held = 0;
        foreach (var item in PackLayout.Items(state))
        {
            held += (item.ItemType & PackRules.ManaStoneType) != 0 ? 1 : 0;
        }

        return Math.Max(0, StoneTarget - held);
    }

    /// <summary>
    /// M5 7 D139 (d): the empty stones beyond the keep, to sell (or drop): of <see cref="StoneTarget"/> stones the charged ones count first (none of them is ever shed), and the rest of the keep is the empty ones with the highest
    /// efficiency, then the ones already in the kit pack (<paramref name="inKit"/>: no move), then the lowest guid. <paramref name="why"/> gives the counts. Lifestone's own rule over ACE's stones (round 13: 23 empty against the keep of 4).
    /// </summary>
    /// <param name="stones">The appraised stones held (an unappraised stone is not passed: it is kept until read).</param>
    /// <param name="inKit">True for a stone in the kit pack.</param>
    /// <param name="why">The keep and the shed with the numbers.</param>
    public static List<HeldStone> Shed(IReadOnlyList<HeldStone> stones, Func<uint, bool> inKit, out string why)
    {
        ArgumentNullException.ThrowIfNull(stones);
        ArgumentNullException.ThrowIfNull(inKit);
        var charged = stones.Count(s => s.Charged);
        var empty = stones.Where(s => !s.Charged).OrderByDescending(s => s.Efficiency).ThenByDescending(s => inKit(s.Guid)).ThenBy(s => s.Guid).ToList();
        var keepEmpty = Math.Max(0, StoneTarget - charged);
        var shed = empty.Skip(keepEmpty).ToList();
        why = string.Create(
            CultureInfo.InvariantCulture,
            $"keep {charged} charged and {Math.Min(keepEmpty, empty.Count)} empty of {empty.Count} (the keep is {StoneTarget} stones, the charged first); {(shed.Count == 0 ? "none to sell" : $"{shed.Count} empty to sell")}");
        return shed;
    }

    /// <summary>The next step.</summary>
    /// <param name="worn">The worn pieces with their mana (only the ones with a maximum).</param>
    /// <param name="stones">The stones held.</param>
    /// <param name="junk">The main pack's sale items with mana (from their appraisals).</param>
    /// <param name="player">The player's id (the refill's target).</param>
    /// <param name="decisions">M5 7 S2b: filled with one line per junk item weighed for a capture (the mana's worth against the payout), or null.</param>
    public static ItemManaStep Next(IReadOnlyList<WornMana> worn, IReadOnlyList<HeldStone> stones, IReadOnlyList<DrainTarget> junk, uint player, List<string>? decisions = null)
    {
        ArgumentNullException.ThrowIfNull(worn);
        ArgumentNullException.ThrowIfNull(stones);
        ArgumentNullException.ThrowIfNull(junk);
        var low = worn.Where(w => w.MaxMana > 0 && (w.Mana < w.MaxMana * RefillBelow || w.SecondsLeft < RefillHorizonSeconds)).ToList();
        var charged = stones.Where(s => s.Charged).ToList();
        if (low.Count > 0 && charged.Count > 0)
        {
            var need = worn.Where(w => w.MaxMana > 0).Sum(w => (long)Math.Max(0, w.MaxMana - w.Mana));
            var stone = charged.Where(s => s.Mana >= need).OrderBy(s => s.Mana).ThenBy(s => s.Guid).FirstOrDefault();
            if (stone.Guid == 0)
            {
                stone = charged.OrderByDescending(s => s.Mana).ThenBy(s => s.Guid).First();
            }

            var lows = string.Join(", ", low.Select(w => string.Create(CultureInfo.InvariantCulture, $"'{w.Name}' {w.Mana}/{w.MaxMana}{(w.ManaRate < 0 ? $" ({w.SecondsLeft / 60:0} min left at {-w.ManaRate:0.####}/s)" : string.Empty)}")));
            return new ItemManaStep(ItemManaAct.Refill, stone.Guid, player, string.Create(CultureInfo.InvariantCulture, $"{lows} (under {RefillBelow:P0} or {RefillHorizonSeconds / 3600:0.#} h); the worn items need {need}, the stone '{stone.Name}' holds {stone.Mana} ({charged.Count} charged)"));
        }

        if (worn.Any(w => w.MaxMana > 0) && charged.Count < ChargedWanted && stones.Where(s => !s.Charged).OrderByDescending(s => s.Efficiency).ThenBy(s => s.Guid).FirstOrDefault() is { Guid: not 0 } empty)
        {
            DrainWeighing? best = null;
            foreach (var j in junk)
            {
                if (j.Mana <= 0 || j.MaxMana <= 0)
                {
                    continue;
                }

                var weighed = Weigh(empty, j);
                decisions?.Add(weighed.ToString());
                if (weighed.Drain && (best is null || weighed.Margin > best.Value.Margin))
                {
                    best = weighed;
                }
            }

            if (best is { } pick)
            {
                return new ItemManaStep(ItemManaAct.Capture, empty.Guid, pick.Target.Guid, string.Create(CultureInfo.InvariantCulture, $"{charged.Count} charged of {ChargedWanted} wanted; the stone '{empty.Name}' (efficiency {empty.Efficiency:0.##}); {pick.Why}"));
            }
        }

        var why = low.Count > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{low.Count} worn piece(s) low but no charged stone is held (TODO(m5-stone-short))")
            : string.Create(CultureInfo.InvariantCulture, $"every worn piece over {RefillBelow:P0} and {RefillHorizonSeconds / 3600:0.#} h; {charged.Count} charged stone(s), {stones.Count - charged.Count} empty");
        return new ItemManaStep(ItemManaAct.None, 0, 0, why);
    }

    /// <summary>
    /// M5 7 S2b: the pyreals one point of item mana is worth to the mage: the market's price of mana, a Titan Mana Charge's Value 9,000 for its 5,000 points (weenie 9060, the pinned world SQL; <c>FighterKit.ManaChargeWcid</c>), 1.8.
    /// A vendor sells it dearer (his sell rate over the Value), so the drain is weighed against the cheapest the mana could cost. Lifestone's own measure from ACE's data.
    /// </summary>
    public const double PyrealsPerManaPoint = 9000.0 / 5000.0;

    /// <summary>The mana stones' ItemEfficiency by weenie (the pinned world SQL v0.9.296, PropertyFloat 87), for a stone whose appraisal lacks it: Minor 0.1, Lesser 0.25, Mana Stone 0.6, Moderate 1.2, Greater 2, Major 3.</summary>
    public static IReadOnlyDictionary<uint, double> EfficiencyByWcid { get; } = new Dictionary<uint, double>
    {
        [27331] = 0.1, [2434] = 0.25, [2435] = 0.6, [27330] = 1.2, [2436] = 2.0, [27328] = 3.0,
    };

    /// <summary>One junk item weighed for a capture: the mana the stone would take (<c>round(efficiency x ItemCurMana)</c>, ManaStone.cs:108) at <see cref="PyrealsPerManaPoint"/>, against the payout the drain gives up.</summary>
    /// <param name="Target">The item.</param>
    /// <param name="Captured">The mana the stone would hold.</param>
    /// <param name="Worth">Its worth in pyreals.</param>
    /// <param name="Drain">True when the worth is over the payout.</param>
    public readonly record struct DrainWeighing(DrainTarget Target, int Captured, long Worth, bool Drain)
    {
        /// <summary>The worth over the payout (negative: selling wins).</summary>
        public long Margin => Worth - Target.Forgone;

        /// <summary>The decision with both numbers.</summary>
        public string Why => string.Create(CultureInfo.InvariantCulture, $"'{Target.Name}' 0x{Target.Guid:X8}: {Target.Mana} mana -> {Captured} in the stone, worth {Worth} pyreals at {PyrealsPerManaPoint:0.##} a point, against a payout of {Target.Forgone}: {(Drain ? "drain" : "sell")}");

        /// <summary>The line the log prints for every item weighed.</summary>
        public override string ToString() => "gear mana: weigh " + Why;
    }

    /// <summary>Weighs one junk item for <paramref name="stone"/> (see <see cref="DrainWeighing"/>); a stone of unknown efficiency (0) never drains.</summary>
    public static DrainWeighing Weigh(in HeldStone stone, in DrainTarget target)
    {
        var captured = (int)Math.Round(stone.Efficiency * target.Mana, MidpointRounding.ToEven);
        var worth = (long)Math.Floor(captured * PyrealsPerManaPoint);
        return new DrainWeighing(target, captured, worth, stone.Efficiency > 0 && worth > target.Forgone);
    }

    /// <summary>The worn pieces' mana from their appraisals (a piece with no maximum is skipped).</summary>
    public static List<WornMana> WornOf(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var list = new List<WornMana>();
        foreach (var e in state.Self.Inventory.Equipped)
        {
            if (state.Appraisals.TryGet(e.Guid, out var a) && a.ItemMaxMana is > 0 && state.Objects.TryGet(e.Guid, out var o))
            {
                list.Add(new WornMana(e.Guid, o.Name, a.ItemMana ?? 0, a.ItemMaxMana.Value, a.Float(5) ?? 0.0));
            }
        }

        return list;
    }
}
