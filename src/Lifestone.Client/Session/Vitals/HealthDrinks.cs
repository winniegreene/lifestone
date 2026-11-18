using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One drink that restores health: its weenie, its name and what one drink restores (ACE <c>Food.BoostVital</c>: the weenie's BoostValue, PropertyInt 90, on the vital of BoosterEnum 2, PropertyInt 89).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="Boost">The health one drink restores.</param>
public readonly record struct HealthDrink(uint Wcid, string Name, uint Boost);

/// <summary>What the fight's emergency does with the health drinks she holds (<see cref="HealthDrinks.Emergency"/>).</summary>
public enum HealthDrinkCall
{
    /// <summary>No health drink is held.</summary>
    NoneHeld,

    /// <summary>Every drink held would overflow: none is drunk.</summary>
    NoneFits,

    /// <summary>The mana pays a heal and the next hit does not kill: the heal goes first and the drinks are kept for when it cannot.</summary>
    HealFirst,

    /// <summary>The mana pays no heal: a drink, the only heal she has.</summary>
    NoHealPaid,

    /// <summary>The next hit kills: a drink, which neither fizzles nor is resisted.</summary>
    NextHitKills,

    /// <summary>The hits of the last seconds would kill her before one heal could land (<see cref="HealthDrinks.IsDesperate"/>): a drink before the heal.</summary>
    Desperate,
}

/// <summary>
/// M6 14 T (the owner, 2026-11-11: carry health drinks; several of the day's deaths had no way to heal once the mana was gone): the health drinks a character may hold, and the two rules over them.
/// <para><b>The pick</b> (<see cref="Fitting"/>, the owner's rule for mana applied to health): a drink that would overflow is not drunk, and of those that fit whole in the missing health the biggest is taken.</para>
/// <para><b>The emergency's call</b> (<see cref="Emergency"/>): the drinks are the reserve. A drink goes first when the mana pays no heal, when the next hit kills, or when the hits of the last seconds would kill her before one heal could land (<see cref="IsDesperate"/>); else the heal goes first. A health drink is a desperation move (the owner, 2026-11-11). One of the day's rounds had 81 emergency
/// heals in 77 minutes, so a drink at every one would empty any stock in minutes and leave none for the moment the mana is gone; a drain is also the faster heal while the mana pays it (33 to 48 health a cast against 25 a drink of 2.5 s
/// from the magic stance).</para>
/// The rows are the world database's (weenie_properties_int types 89 BoosterEnum = 2 and 90 BoostValue, the pinned world SQL v0.9.296), biggest first.
/// </summary>
public static class HealthDrinks
{
    /// <summary>The Health Tincture (50 health; no healer of Holtburg sells it).</summary>
    public const uint TinctureWcid = 27319;

    /// <summary>
    /// The desperation threshold, in seconds: the cast of ONE heal (the emergency's measured Heal Self and Drain Health Other casts take 2 to 3 s, 2.5 taken) and ONE drink (2.5 s from the magic stance, measured live). When the hits of
    /// the last seconds would take all her health within it, the heal she could cast would not land in time to matter and a drink goes first (<see cref="IsDesperate"/>). Small on purpose: a health drink is a desperation move.
    /// </summary>
    public const double DesperateSeconds = 5;

    /// <summary>True when the incoming hits would take all of <paramref name="health"/> within <see cref="DesperateSeconds"/> with no heal.</summary>
    /// <param name="health">Her health.</param>
    /// <param name="incomingPerSecond">The hits of the last seconds, a second (the fight's <c>IncomingRead.PerSecond</c>).</param>
    public static bool IsDesperate(uint health, double incomingPerSecond) => incomingPerSecond > 0 && incomingPerSecond * DesperateSeconds >= health;

    /// <summary>The drinks, biggest first.</summary>
    public static IReadOnlyList<HealthDrink> All { get; } =
    [
        new(27318, "Health Philtre", 100),
        new(27320, "Health Tonic", 85),
        new(2458, "Health Elixir", 65),
        new(TinctureWcid, "Health Tincture", 50),
        new(FighterKit.PotionWcid, "Potion of Healing", 25),
        new(VitalsMeans.HealthDraughtWcid, "Health Draught", 10),
    ];

    /// <summary>The index in <see cref="All"/> of the weenie <paramref name="wcid"/>, or -1 when it is no health drink.</summary>
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
    /// <param name="missing">The health's maximum less the health now.</param>
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

    /// <summary>The emergency's call (see the class): what is held and fits, whether the mana pays a heal, and whether the next hit kills.</summary>
    /// <param name="heldAll">The health drinks held, of every kind.</param>
    /// <param name="fits">One of them fits whole in the missing health.</param>
    /// <param name="healPaid">The mana pays a heal or a drain the fight may cast now.</param>
    /// <param name="nextHitKills">The biggest hit seen takes all the health left.</param>
    /// <param name="desperate">The hits of the last seconds would kill her before one heal could land (<see cref="IsDesperate"/>).</param>
    public static HealthDrinkCall Emergency(int heldAll, bool fits, bool healPaid, bool nextHitKills, bool desperate = false) =>
        heldAll <= 0 ? HealthDrinkCall.NoneHeld
        : !fits ? HealthDrinkCall.NoneFits
        : nextHitKills ? HealthDrinkCall.NextHitKills
        : !healPaid ? HealthDrinkCall.NoHealPaid
        : desperate ? HealthDrinkCall.Desperate
        : HealthDrinkCall.HealFirst;

    /// <summary>True when <paramref name="call"/> drinks now.</summary>
    /// <param name="call">The emergency's call.</param>
    public static bool Drinks(HealthDrinkCall call) => call is HealthDrinkCall.NoHealPaid or HealthDrinkCall.NextHitKills or HealthDrinkCall.Desperate;

    /// <summary>The call in the log's words.</summary>
    /// <param name="call">The emergency's call.</param>
    public static string Words(HealthDrinkCall call) => call switch
    {
        HealthDrinkCall.NoneHeld => "no health drink is held",
        HealthDrinkCall.NoneFits => "every drink held would overflow",
        HealthDrinkCall.HealFirst => "the mana pays a heal, the next hit does not kill and she outlasts one heal's cast: the heal goes first, the drinks are kept",
        HealthDrinkCall.Desperate => "the hits of the last seconds would kill her before one heal could land: a drink before the heal",
        HealthDrinkCall.NoHealPaid => "the mana pays no heal: the drink is the heal",
        _ => "the next hit kills: a drink neither fizzles nor is resisted",
    };

    /// <summary>
    /// Reads the pack (the main pack and the side packs) and picks (<see cref="Fitting"/>): true with the first stack of the picked drink, the drink and how many of it are held; false when none fits. <paramref name="heldAll"/> is every health
    /// drink held, fitting or not. Allocation-free.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="missing">The health's maximum less the health now.</param>
    /// <param name="guid">The stack to drink from.</param>
    /// <param name="drink">The picked drink.</param>
    /// <param name="held">How many of the picked drink are held.</param>
    /// <param name="heldAll">How many health drinks are held, of every kind.</param>
    public static bool Find(GameState state, List<uint> scratch, uint missing, out uint guid, out HealthDrink drink, out int held, out int heldAll)
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

        var pick = Fitting(counts, missing);
        if (pick < 0)
        {
            (guid, drink, held) = (0, default, 0);
            return false;
        }

        (guid, drink, held) = (first[pick], all[pick], counts[pick]);
        return true;
    }
}
