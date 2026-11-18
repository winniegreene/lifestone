using System.Globalization;

namespace Lifestone.Client.Session.Mage;

/// <summary>What the caster's own mana asks for next.</summary>
public enum CasterManaAct
{
    /// <summary>Nothing is due (including a caster with no item mana, such as a plain or Training Wand).</summary>
    None,

    /// <summary>The held caster has fewer hours left than the horizon (or is out) and a charged stone or charge is held: use it on the player.</summary>
    RefillDue,

    /// <summary>The held caster is out of mana and a charged spare caster is held: wield the spare.</summary>
    SwapToSpare,

    /// <summary>The held caster has mana again but its spells dropped: wield it again (a refill does not restore them).</summary>
    Rewield,

    /// <summary>A refill or a swap is due and nothing charged is held: the kit wants a mana charge (a floor line for the resupply to buy).</summary>
    WantStone,
}

/// <summary>A ware a profile vendor sells that could be a mana want: the vendor, the weenie, its price.</summary>
/// <param name="VendorWcid">The vendor's weenie (Archmage Cindrue is 2304).</param>
/// <param name="VendorName">The vendor's name.</param>
/// <param name="Wcid">The ware's weenie.</param>
/// <param name="Price">The price in pyreals at the vendor, 0 when not known.</param>
public readonly record struct ManaOffer(uint VendorWcid, string VendorName, uint Wcid, int Price = 0);

/// <summary>What the rule is asked about: the held caster, whether its spells dropped, the spare casters held and what the ground's profile vendors sell.</summary>
/// <param name="Held">The held caster's own mana (null: nothing is held; a caster with no maximum has no item mana).</param>
/// <param name="SpellsDropped">True when the held caster's spells are off although it may hold mana: the "out of Mana" line was seen for it (ACE's <c>HandleManaDepleted</c>) and it has not been wielded since.</param>
/// <param name="Spares">The spare casters held in the packs, with their mana.</param>
/// <param name="Offers">The wares the ground's profile vendors list (the caller filters nothing: the rule picks the charges).</param>
public sealed record CasterManaInput(WornMana? Held, bool SpellsDropped, IReadOnlyList<WornMana> Spares, IReadOnlyList<ManaOffer> Offers);

/// <summary>One decision of the rule, with the numbers the log line prints.</summary>
/// <param name="Act">What.</param>
/// <param name="Caster">The held caster the decision is about (0 when none is held).</param>
/// <param name="Name">The caster's name.</param>
/// <param name="Mana">Its mana now.</param>
/// <param name="MaxMana">Its maximum.</param>
/// <param name="Hours">The hours left at its rate (infinity when it has no rate or no maximum).</param>
/// <param name="Stone">RefillDue: the charged stone or charge to use on the player.</param>
/// <param name="Spare">SwapToSpare: the spare caster to wield.</param>
/// <param name="WantWcid">WantStone: the charge to buy (0 when no profile vendor sells one).</param>
/// <param name="WantVendor">WantStone: the vendor's weenie who sells it (0 when none).</param>
/// <param name="WantCount">WantStone: how many.</param>
/// <param name="Todo">A loud named TODO the decision carries (null when none).</param>
/// <param name="Reason">Why, with the numbers.</param>
public readonly record struct CasterManaDecision(
    CasterManaAct Act,
    uint Caster,
    string Name,
    int Mana,
    int MaxMana,
    double Hours,
    uint Stone,
    uint Spare,
    uint WantWcid,
    uint WantVendor,
    int WantCount,
    string? Todo,
    string Reason)
{
    /// <summary>The log line: <c>caster mana: &lt;name&gt; &lt;cur&gt;/&lt;max&gt; &lt;hours&gt;h -&gt; &lt;decision&gt; (&lt;reason&gt;)</c>.</summary>
    public override string ToString()
    {
        var hours = double.IsInfinity(Hours) ? "inf " : Hours.ToString("0.##", CultureInfo.InvariantCulture);
        return string.Create(CultureInfo.InvariantCulture, $"caster mana: {Name} {Mana}/{MaxMana} {hours}h -> {Act} ({Reason})");
    }
}

/// <summary>
/// M6 2 c (m6-atlan-mana-recharge, first half; m5-stone-short; the mage half of m4-gear-mana-reactivate): the held caster's own mana kept by rule. Pure over appraisals; the keeper (<see cref="ItemManaKeeper"/>) asks it before its worn-items refill.
/// </summary>
/// <remarks>
/// <para><b>The drain.</b> ACE burns a wielded caster's ItemCurMana at <c>-ManaRate</c> a second while it is affecting (<c>ManaConsumersTick</c>, Player_Tick.cs:624-660 @95ef426d; an item with no ManaRate or no maximum is skipped, :633-634).
/// At 0 it says "Your X is out of Mana." and removes the item's spells (<c>HandleManaDepleted</c>, Player_Tick.cs:681-699: the spell removal is :693-696, <c>OnSpellsDeactivated</c> :699).</para>
/// <para><b>Why a rewield.</b> A stone or charge only adds mana (ManaStone.cs:119-190; it never sets IsAffecting). The spells come back only at the wield: <c>if (item.ItemCurMana &gt; 0) TryActivateSpells(item)</c> (Player_Inventory.cs:313-315), which
/// creates the item spells and calls <c>OnSpellsActivated</c> (:324-366, WorldObject_Magic.cs:2191-2196). So a caster that ran dry is refilled, then wielded again: <see cref="CasterManaAct.Rewield"/>.</para>
/// <para><b>Order.</b> No item mana (a plain or Training Wand has no ItemMaxMana) is None. Mana 0: a charged spare is wielded (the swap costs nothing), else a charged stone is used, else the kit wants a charge. Mana over 0 with the spells dropped is a rewield.
/// Otherwise under <see cref="ItemManaKeeper.RefillHorizonSeconds"/> left at the rate is a refill when a charged stone is held and a want when not; over the horizon (or with no ManaRate: nothing drains it) is None.</para>
/// <para><b>The want.</b> A profile vendor's charge, never a stone: a stone is sold empty (Minor 27331 and Lesser 2434 carry no ItemCurMana in the pinned world SQL, <c>cache/m6/2/sel-stones.tsv</c>), so it would not satisfy a want, while the Mana Charges 4612-4615 are sold full.
/// The smallest charge that covers the deficit is wanted, else the largest on offer, as many as the deficit needs. With no charge on offer the want is <c>TODO(m6-no-stone-vendor)</c>, loud, and names no ware.</para>
/// <para><b>Not retail:</b> the horizon is the keeper's (Lifestone's own, no new constant, D26); the mana of each charge is ACE's data (<see cref="ChargeMana"/>).</para>
/// </remarks>
public static class CasterManaRule
{
    /// <summary>The Mana Charges' ItemCurMana by weenie (PropertyInt 107, the pinned world SQL v0.9.296: <c>SELECT object_Id, value FROM weenie_properties_int WHERE type = 107 AND object_Id IN (4612, 4613, 4614, 4615, 4616, 20179, 9060, 27329, 30254)</c>, kept in <c>cache/m6/2/sel-stones.tsv</c>): Tiny 100, Small 175, Moderate 350, High 500, Great 1000, Superb 2000, Titan 5000, Massive 10000, Eternal 10000.</summary>
    public static IReadOnlyDictionary<uint, int> ChargeMana { get; } = new Dictionary<uint, int>
    {
        [4612] = 100, [4613] = 175, [4614] = 350, [4615] = 500, [4616] = 1000, [20179] = 2000, [9060] = 5000, [27329] = 10000, [30254] = 10000,
    };

    /// <summary>Decides what the held caster's mana asks for.</summary>
    /// <param name="input">The held caster, its spell state, the spares and the profile's wares.</param>
    /// <param name="stones">The stones and charges held (only the charged ones count).</param>
    public static CasterManaDecision Decide(CasterManaInput input, IReadOnlyList<HeldStone> stones)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(stones);
        if (input.Held is not { } held)
        {
            return new CasterManaDecision(CasterManaAct.None, 0, "(no caster)", 0, 0, double.PositiveInfinity, 0, 0, 0, 0, 0, null, "no caster is held");
        }

        if (held.MaxMana <= 0)
        {
            return Make(held, CasterManaAct.None, "no item mana (no ItemMaxMana: a plain or Training Wand burns none, so nothing is kept)");
        }

        var charged = stones.Where(s => s.Charged).OrderBy(s => s.Mana).ThenBy(s => s.Guid).ToList();
        var hours = held.SecondsLeft / 3600;
        var horizonHours = ItemManaKeeper.RefillHorizonSeconds / 3600;
        if (held.Mana <= 0)
        {
            var spare = input.Spares.Where(s => s.MaxMana > 0 && s.Mana > 0).OrderByDescending(s => s.Mana).ThenBy(s => s.Guid).FirstOrDefault();
            if (spare.Guid != 0)
            {
                return Make(held, CasterManaAct.SwapToSpare, string.Create(CultureInfo.InvariantCulture, $"out of mana, its spells are off; the spare '{spare.Name}' holds {spare.Mana}/{spare.MaxMana}"), spare: spare.Guid);
            }

            return charged.Count > 0
                ? Make(held, CasterManaAct.RefillDue, string.Create(CultureInfo.InvariantCulture, $"out of mana and no charged spare is held; the stone '{PickStone(charged, held).Name}' on the player, then wield it again"), stone: PickStone(charged, held).Guid)
                : Want(held, input, "out of mana, no charged spare and no charged stone is held");
        }

        if (input.SpellsDropped)
        {
            return Make(held, CasterManaAct.Rewield, "mana is back but its spells dropped when it ran dry (ACE removes them at 0, Player_Tick.cs:681-699; a refill does not restore them, only the wield does, Player_Inventory.cs:313-315)");
        }

        if (held.SecondsLeft >= ItemManaKeeper.RefillHorizonSeconds)
        {
            return Make(held, CasterManaAct.None, double.IsInfinity(hours)
                ? "no ManaRate: nothing drains it"
                : string.Create(CultureInfo.InvariantCulture, $"{hours:0.##} h left, over the {horizonHours:0.#} h horizon"));
        }

        var due = string.Create(CultureInfo.InvariantCulture, $"{hours:0.##} h left, under the {horizonHours:0.#} h horizon");
        return charged.Count > 0
            ? Make(held, CasterManaAct.RefillDue, string.Create(CultureInfo.InvariantCulture, $"{due}; the stone '{PickStone(charged, held).Name}' ({PickStone(charged, held).Mana}) on the player"), stone: PickStone(charged, held).Guid)
            : Want(held, input, due + ", no charged stone is held");
    }

    private static HeldStone PickStone(List<HeldStone> charged, WornMana held)
    {
        var need = held.MaxMana - held.Mana;
        foreach (var s in charged)
        {
            if (s.Mana >= need)
            {
                return s;
            }
        }

        return charged[^1];
    }

    private static CasterManaDecision Make(WornMana held, CasterManaAct act, string reason, uint stone = 0, uint spare = 0) =>
        new(act, held.Guid, held.Name, held.Mana, held.MaxMana, held.MaxMana > 0 ? held.SecondsLeft / 3600 : double.PositiveInfinity, stone, spare, 0, 0, 0, null, reason);

    private static CasterManaDecision Want(WornMana held, CasterManaInput input, string why)
    {
        var need = Math.Max(1, held.MaxMana - held.Mana);
        var charges = input.Offers.Where(o => ChargeMana.ContainsKey(o.Wcid)).OrderBy(o => ChargeMana[o.Wcid]).ThenBy(o => o.Wcid).ToList();
        var hours = held.SecondsLeft / 3600;
        if (charges.Count == 0)
        {
            var sells = input.Offers.Count == 0 ? "no ware" : string.Join(", ", input.Offers.Select(o => o.Wcid.ToString(CultureInfo.InvariantCulture)));
            const string Todo = "TODO(m6-no-stone-vendor)";
            return new CasterManaDecision(CasterManaAct.WantStone, held.Guid, held.Name, held.Mana, held.MaxMana, hours, 0, 0, 0, 0, 0, Todo, $"{why}; {Todo}: no profile vendor sells a Mana Charge ({sells} listed), so nothing can be bought and the ground's profile needs one");
        }

        var pick = charges.FirstOrDefault(o => ChargeMana[o.Wcid] >= need);
        if (pick.Wcid == 0)
        {
            pick = charges[^1];
        }

        var each = ChargeMana[pick.Wcid];
        var count = (int)Math.Ceiling((double)need / each);
        return new CasterManaDecision(
            CasterManaAct.WantStone,
            held.Guid,
            held.Name,
            held.Mana,
            held.MaxMana,
            hours,
            0,
            0,
            pick.Wcid,
            pick.VendorWcid,
            count,
            null,
            string.Create(CultureInfo.InvariantCulture, $"{why}; want {count} x wcid {pick.Wcid} ({each} mana each, {need} needed) from {pick.VendorName} ({pick.VendorWcid}){(pick.Price > 0 ? $" at {pick.Price}" : string.Empty)}"));
    }
}
