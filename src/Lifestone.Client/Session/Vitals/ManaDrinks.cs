using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One drink that restores mana: its weenie, its name and what one drink restores (ACE <c>Food.BoostVital</c>: BoostValue, PropertyInt 90, on the vital of BoosterEnum 6, PropertyInt 89).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="Boost">The mana one drink restores.</param>
public readonly record struct ManaDrink(uint Wcid, string Name, uint Boost);

/// <summary>
/// M6 14 T (the owner, 2026-11-11: Mana Tinctures too, since she shops in Dryreach anyway): the mana drinks a character may hold and the one pick over them, as <see cref="HealthDrinks"/> is for health. Every place that drinks for mana
/// finds its drink here (<see cref="VitalsMeans.FindManaPotions"/> is this pick), so no path assumes a drink of 25: the biggest drink held that fits whole in the missing mana (a Mana Tincture when 50 or more is missing, a Mana Potion
/// when 25 to 49 is, a Mana Draught under that); when none fits, the smallest held, and <see cref="Fits"/> says so (a caller that never overflows asks it first, as the pull rest does).
/// The rows are the world database's (weenie_properties_int types 89 BoosterEnum = 6 and 90 BoostValue), biggest first.
/// </summary>
public static class ManaDrinks
{
    /// <summary>The Mana Tincture (50 mana, burden 50; no healer of Holtburg sells it).</summary>
    public const uint TinctureWcid = 27322;

    /// <summary>The drinks, biggest first.</summary>
    public static IReadOnlyList<ManaDrink> All { get; } =
    [
        new(TinctureWcid, "Mana Tincture", 50),
        new(FighterKit.ManaPotionWcid, "Mana Potion", 25),
        new(VitalsMeans.ManaDraughtWcid, "Mana Draught", 10),
    ];

    /// <summary>The index in <see cref="All"/> of the weenie <paramref name="wcid"/>, or -1 when it is no mana drink.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static int IndexOf(uint wcid)
    {
        var all = All;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Wcid == wcid)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The pick: the index in <see cref="All"/> of the biggest drink held whose boost fits whole in <paramref name="missing"/>, or -1 when none is held or every one held would overflow.</summary>
    /// <param name="held">The count held of each drink, in <see cref="All"/>'s order.</param>
    /// <param name="missing">The mana's maximum less the mana now.</param>
    public static int Fitting(ReadOnlySpan<int> held, uint missing)
    {
        var all = All;
        for (var i = 0; i < all.Count && i < held.Length; i++)
        {
            if (held[i] > 0 && all[i].Boost <= missing)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The index of the smallest drink held, or -1 when none is held.</summary>
    /// <param name="held">The count held of each drink, in <see cref="All"/>'s order.</param>
    public static int Smallest(ReadOnlySpan<int> held)
    {
        for (var i = Math.Min(All.Count, held.Length) - 1; i >= 0; i--)
        {
            if (held[i] > 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Reads the pack and the mana and picks: true when a mana drink is held, with the first stack of the pick (<see cref="Fitting"/> on the missing mana; when none fits, or the mana is not read, the smallest held), the drink, whether it fits
    /// whole, and how many mana drinks are held of every kind. Allocation-free.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="guid">The stack to drink from.</param>
    /// <param name="drink">The picked drink.</param>
    /// <param name="fits">True when the pick fits whole in the missing mana.</param>
    /// <param name="heldAll">How many mana drinks are held, of every kind.</param>
    public static bool Find(GameState state, List<uint> scratch, out uint guid, out ManaDrink drink, out bool fits, out int heldAll)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scratch);
        var all = All;
        Span<int> counts = stackalloc int[all.Count];
        Span<uint> first = stackalloc uint[all.Count];
        counts.Clear();
        first.Clear();
        heldAll = 0;
        GearInventory.Collect(state, scratch);
        for (var i = 0; i < scratch.Count; i++)
        {
            if (!state.Objects.TryGet(scratch[i], out var o) || IndexOf(o.Weenie.WeenieClassId) is not (>= 0 and var kind))
            {
                continue;
            }

            var stack = state.Self.Inventory.TryGetStack(scratch[i], out var size, out _) ? (int)Math.Min(size, int.MaxValue) : o.Weenie.StackSize is { } s ? (int)Math.Min(s, int.MaxValue) : 1;
            if (stack <= 0)
            {
                continue;
            }

            if (first[kind] == 0)
            {
                first[kind] = scratch[i];
            }

            counts[kind] += stack;
            heldAll += stack;
        }

        var missing = state.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max) && max > mana ? max - mana : 0u;
        var pick = Fitting(counts, missing);
        fits = pick >= 0;
        if (pick < 0)
        {
            pick = Smallest(counts);
        }

        if (pick < 0)
        {
            (guid, drink) = (0, default);
            return false;
        }

        (guid, drink) = (first[pick], all[pick]);
        return true;
    }

    /// <summary>True when a mana drink is held and the pick fits whole in the missing mana (the owner's rule: a drink that would overflow is not drunk).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    public static bool Fits(GameState state, List<uint> scratch) => Find(state, scratch, out _, out _, out var fits, out _) && fits;
}
