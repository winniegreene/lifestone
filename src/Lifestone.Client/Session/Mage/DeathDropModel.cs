using System.Collections.Frozen;
using System.Globalization;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// The categories ACE sorts a death's candidate items into (a local mirror of ACE's <c>DeathItemCategory</c>, ACE 95ef426d <c>Entity/DeathItem.cs</c>; the order is ACE's, which is the order of the first sort). <see cref="None"/> never drops.
/// </summary>
public enum DeathItemCategory
{
    /// <summary>An unknown type, a container, a creature, money, a useless item, a portal: never a candidate.</summary>
    None,

    /// <summary>ItemType MeleeWeapon.</summary>
    MeleeWeapon,

    /// <summary>ItemType MissileWeapon.</summary>
    MissileWeapon,

    /// <summary>ItemType Caster or MagicWieldable.</summary>
    MagicCaster,

    /// <summary>ItemType Armor.</summary>
    Armor,

    /// <summary>ItemType Clothing.</summary>
    Clothing,

    /// <summary>ItemType Jewelry.</summary>
    Jewelry,

    /// <summary>ItemType Food.</summary>
    Food,

    /// <summary>ItemType Gem.</summary>
    Gem,

    /// <summary>ItemType SpellComponents.</summary>
    SpellComponent,

    /// <summary>ItemType ManaStone.</summary>
    ManaStone,

    /// <summary>The five craft types (CraftAlchemyBase, CraftAlchemyIntermediate, CraftCookingBase, CraftFletchingBase, CraftFletchingIntermediate).</summary>
    CraftingIngredient,

    /// <summary>ItemType Writable.</summary>
    ParchmentBook,

    /// <summary>ItemType Key.</summary>
    Key,

    /// <summary>ItemType PromissoryNote.</summary>
    TradeNote,

    /// <summary>ItemType Misc.</summary>
    Misc,
}

/// <summary>One carried item as the death rule reads it: the weenie, its ItemType, the Value and the stack size the server sent, whether it is wielded and whether it is bonded.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="ItemType">ACE's <c>ItemType</c> bits, tested for equality (ACE's <c>GetCategory</c> is a <c>switch</c>, not a flag test).</param>
/// <param name="Value">The Value of the whole stack (ACE's <c>wo.Value</c>); 0 when the item has none.</param>
/// <param name="StackSize">The stack size (1 for an item that does not stack).</param>
/// <param name="Wielded">True when the item is worn or held (a candidate from level 35).</param>
/// <param name="Bonded">True when the item is bonded (ACE's PropertyInt 33 is not 0): never a candidate.</param>
public readonly record struct CarriedItem(uint Wcid, uint ItemType, int Value, int StackSize, bool Wielded, bool Bonded);

/// <summary>One candidate after ACE's sort, halving and variance, as bounds (see <see cref="DeathDropModel.Rank"/>).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Category">The death category.</param>
/// <param name="UnitValue">The per-unit value (the stack's Value over its stack size).</param>
/// <param name="HalvedValue">The value after the halving (the first of its category keeps <paramref name="UnitValue"/>; every later one is halved once).</param>
/// <param name="MinFinal">The lowest final value the variance can give (the halved value at -10%).</param>
/// <param name="MaxFinal">The highest final value the variance can give (the halved value at +10%); a needed item counts at its UNHALVED value at +10%.</param>
/// <param name="Needed">True when the item is in the needed set.</param>
public readonly record struct DeathCandidate(uint Wcid, DeathItemCategory Category, int UnitValue, int HalvedValue, int MinFinal, int MaxFinal, bool Needed);

/// <summary>
/// What <see cref="DeathDropModel.Survives(IReadOnlyList{CarriedItem}, IReadOnlySet{uint}, int)"/> found.
/// </summary>
/// <param name="Level">The level the rule was taken at.</param>
/// <param name="MinDrops">The fewest items a death at that level drops.</param>
/// <param name="MaxDrops">The most items a death at that level drops (N_max).</param>
/// <param name="Candidates">How many carried items are candidates.</param>
/// <param name="Above">How many NOT-needed candidates have a minimum final value above the best needed item's maximum (they outrank it in every corner of the variance).</param>
/// <param name="BestNeededWcid">The needed candidate with the highest maximum, or 0 when no needed item is a candidate.</param>
/// <param name="BestNeededMax">That item's maximum final value (unhalved, +10%), or 0.</param>
/// <param name="WeakestAboveMin">The lowest minimum among the items above (0 when none is).</param>
/// <param name="Survives">True when <paramref name="Above"/> is at least <paramref name="MaxDrops"/> (or no needed item is a candidate): no needed item can be among the dropped.</param>
public readonly record struct DropVerdict(int Level, int MinDrops, int MaxDrops, int Candidates, int Above, uint BestNeededWcid, int BestNeededMax, int WeakestAboveMin, bool Survives)
{
    /// <summary>One line: <c>survives=yes level=126 drops=6..8 candidates=N above=9 best_needed=8552 best_needed_max=5940 weakest_above_min=7200</c> (no name, no account).</summary>
    public string Describe() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"survives={(Survives ? "yes" : "no")} level={Level} drops={MinDrops}..{MaxDrops} candidates={Candidates} above={Above} best_needed={BestNeededWcid} best_needed_max={BestNeededMax} weakest_above_min={WeakestAboveMin}");
}

/// <summary>
/// ACE's death-drop rule, re-expressed (M4 3.1 d0, D20; ACE 95ef426d, read and never pasted: <c>Entity/DeathItem.cs</c> the whole file, <c>WorldObjects/Player_Death.cs</c> 481-603 which items drop (the bonded filter, <c>Bonded == 0</c>, at 502; the "destroy" bonded items at 504-505; the "slippery" ones at 560; the bonded items' comment at 441-446) and 629-673 how many; retail's own rule is
/// the developer spec ACE's comments quote). Pure and engine-free: it reads <see cref="CarriedItem"/> values and answers whether the death loop's needed items can be among the items a death drops.
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The candidates are everything carried except pyreals (<see cref="CoinWcid"/>), bonded items, items of category <see cref="DeathItemCategory.None"/> (a container, a creature, money, a useless item, a portal: packs never drop) and,
/// below level <see cref="WieldedDropLevel"/>, wielded items. A stack counts at its per-unit value (the stack's Value over its stack size, integer division) and a death drops one unit of it. The candidates are sorted by category then by value, highest first;
/// every item after the first of its category has its value halved once (not progressively, integer division); then every value gets a random variance of up to plus or minus 10%, the list is sorted again and the first N drop, with N
/// <see cref="MaxDrops"/> at most (<see cref="MinDrops"/> at least): 0 up to level 10; 0 or 1 for levels 11 to 20; level / 20 plus 0 to 2 (inclusive) above, at most <see cref="MaxItemsDropped"/>. Bonded "slippery" items always drop and "destroy" items are
/// destroyed (<c>HandleDestroyBonded</c>, <c>GetSlipperyItems</c>): the mage carries none of either.</para>
/// <para><b>The variance as bounds.</b> The halving is decided before the variance, so the variance never changes who is first of a category: each candidate's final value lies in <c>[MinFinal, MaxFinal]</c> (the halved value at -10% and +10%, in float arithmetic with the
/// truncation ACE's <c>GetValueWithVariance</c> has), and a needed item survives every draw when at least N other candidates have a MinFinal above its MaxFinal: they then rank above it in every corner of the variance, so it is never among the first N.
/// A needed item counts at its UNHALVED best case (its value at +10%), whether or not another item of its category is above it now: the kit changes while the loop runs (a spent stack, a dropped item), and the answer must not rest on a halving that may go.</para>
/// </remarks>
public static class DeathDropModel
{
    /// <summary>ACE's <c>coinstack</c> (273): pyreals are excluded from the candidates (their own half-loss is separate).</summary>
    public const uint CoinWcid = 273;

    /// <summary>The level from which wielded items are candidates (<c>Player_Death.cs</c> <c>canDropWielded = level >= 35</c>).</summary>
    public const int WieldedDropLevel = 35;

    /// <summary>ACE's <c>Player.MaxItemsDropped</c>.</summary>
    public const int MaxItemsDropped = 14;

    /// <summary>ACE's <c>DeathItems.MaxVariance</c>: the variance is a random value between minus and plus this share.</summary>
    public const float MaxVariance = 0.10f;

    /// <summary>The Mana Scarab (component 193, the level VIII spells' scarab): the M4 fixture's valuable carried item. Since M5 3 d4 nothing special-cases it: it is needed exactly when the mage's kit holds it as a stock line.</summary>
    public const uint ManaScarabWcid = 37155;

    /// <summary>
    /// The death loop's needed set is the mage's own kit (M5 3 d4, D51; it was the retired loot gear, the Healer's Heart 8552 and three armour pieces, and every component the pinned map names): every line of <paramref name="kit"/>, the wielded
    /// wand, the worn pieces, the four foci and the stock lines (the tapers, the potions, the carried scarabs), by weenie class. A line the profile marks bonded (the Platinum Scarab) is in the set and never a candidate. A kit built for another ground
    /// has another set: the set follows the kit the scenario was made with, never a constant.
    /// </summary>
    /// <param name="kit">The mage's kit (<see cref="GearKit.Build"/> of its ground).</param>
    public static IReadOnlySet<uint> NeededOf(GearKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return kit.Items.Select(i => i.Wcid).ToFrozenSet();
    }

    /// <summary>True when <paramref name="wcid"/> is a line of <paramref name="kit"/> (a needed item: <see cref="NeededOf"/>).</summary>
    /// <param name="kit">The mage's kit.</param>
    /// <param name="wcid">The weenie class id of a listed corpse entry.</param>
    public static bool IsNeeded(GearKit kit, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(kit);
        return kit.ItemOf(wcid) is not null;
    }

    /// <summary>ACE's <c>DeathItem.GetCategory</c>: the category of an ItemType (an exact match of one type; a combination of types, a container, a creature, money and every other type are <see cref="DeathItemCategory.None"/>).</summary>
    /// <param name="itemType">ACE's <c>ItemType</c> bits.</param>
    public static DeathItemCategory Category(uint itemType) => itemType switch
    {
        0x00000001 => DeathItemCategory.MeleeWeapon,
        0x00000100 => DeathItemCategory.MissileWeapon,
        0x00008000 or 0x00200000 => DeathItemCategory.MagicCaster, // Caster, MagicWieldable
        0x00000002 => DeathItemCategory.Armor,
        0x00000004 => DeathItemCategory.Clothing,
        0x00000008 => DeathItemCategory.Jewelry,
        0x00000020 => DeathItemCategory.Food,
        0x00000800 => DeathItemCategory.Gem,
        0x00001000 => DeathItemCategory.SpellComponent,
        0x00080000 => DeathItemCategory.ManaStone,
        0x00800000 or 0x04000000 or 0x00400000 or 0x02000000 or 0x08000000 => DeathItemCategory.CraftingIngredient, // CraftAlchemyBase, CraftAlchemyIntermediate, CraftCookingBase, CraftFletchingBase, CraftFletchingIntermediate
        0x00002000 => DeathItemCategory.ParchmentBook, // Writable
        0x00004000 => DeathItemCategory.Key,
        0x00040000 => DeathItemCategory.TradeNote, // PromissoryNote
        0x00000080 => DeathItemCategory.Misc,
        _ => DeathItemCategory.None, // Container 0x200 ("containers don't drop?"), Creature, Money, Useless, Portal and the rest
    };

    /// <summary>The most items a death at <paramref name="level"/> drops (ACE <c>GetNumItemsDropped</c>): 0 up to level 10, 1 for levels 11 to 20, level / 20 + 2 above, at most <see cref="MaxItemsDropped"/>.</summary>
    /// <param name="level">The character's level.</param>
    public static int MaxDrops(int level) => level <= 10 ? 0 : level <= 20 ? 1 : Math.Min(MaxItemsDropped, (level / 20) + 2);

    /// <summary>The fewest items a death at <paramref name="level"/> drops: 0 up to level 20, level / 20 above (the random part rolled 0), at most <see cref="MaxItemsDropped"/>.</summary>
    /// <param name="level">The character's level.</param>
    public static int MinDrops(int level) => level <= 20 ? 0 : Math.Min(MaxItemsDropped, level / 20);

    /// <summary>True when ACE would consider <paramref name="item"/> for a death at <paramref name="level"/>: not pyreals, not bonded, wielded only from level 35, and a category other than <see cref="DeathItemCategory.None"/>.</summary>
    /// <param name="item">The carried item.</param>
    /// <param name="level">The character's level.</param>
    public static bool IsCandidate(in CarriedItem item, int level) =>
        item.Wcid != CoinWcid && !item.Bonded && (!item.Wielded || level >= WieldedDropLevel) && Category(item.ItemType) != DeathItemCategory.None;

    /// <summary>The per-unit value ACE sorts on: the stack's Value over its stack size (integer division), or the Value itself for a single item.</summary>
    /// <param name="item">The carried item.</param>
    public static int UnitValue(in CarriedItem item) => item.StackSize > 1 ? item.Value / item.StackSize : item.Value;

    /// <summary>A value at the bottom of the variance (-10%), as ACE's <c>GetValueWithVariance</c> computes it (float arithmetic, truncated).</summary>
    /// <param name="value">The value before the variance.</param>
    public static int VarianceMin(int value) => (int)(value + (value * -MaxVariance));

    /// <summary>A value at the top of the variance (+10%), as ACE's <c>GetValueWithVariance</c> computes it (float arithmetic, truncated).</summary>
    /// <param name="value">The value before the variance.</param>
    public static int VarianceMax(int value) => (int)(value + (value * MaxVariance));

    /// <summary>
    /// The candidates of <paramref name="carried"/> at <paramref name="level"/> (pyreals, bonded items, containers and the like, and below level 35 the wielded items left out), in the order given, with their category and per-unit value;
    /// the halving and the bounds are not applied (see <see cref="Rank"/>).
    /// </summary>
    /// <param name="carried">What the character carries.</param>
    /// <param name="level">The character's level.</param>
    public static List<CarriedItem> Candidates(IReadOnlyList<CarriedItem> carried, int level)
    {
        ArgumentNullException.ThrowIfNull(carried);
        var list = new List<CarriedItem>(carried.Count);
        for (var i = 0; i < carried.Count; i++)
        {
            if (IsCandidate(carried[i], level))
            {
                list.Add(carried[i]);
            }
        }

        return list;
    }

    /// <summary>
    /// ACE's list after its sort and its halving, with the variance as bounds: the candidates sorted by category (ACE's order) then by per-unit value, highest first (a tie keeps the carried order, as ACE's stable sort does), every item after the first of its category
    /// halved once, and each item's <see cref="DeathCandidate.MinFinal"/> and <see cref="DeathCandidate.MaxFinal"/> (a needed item's maximum is its unhalved value at +10%). One array per call, no delegates, no LINQ.
    /// </summary>
    /// <param name="carried">What the character carries.</param>
    /// <param name="needed">The weenie class ids that must not drop.</param>
    /// <param name="level">The character's level.</param>
    public static DeathCandidate[] Rank(IReadOnlyList<CarriedItem> carried, IReadOnlySet<uint> needed, int level)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(needed);
        var list = new DeathCandidate[carried.Count];
        var n = 0;
        for (var i = 0; i < carried.Count; i++)
        {
            var item = carried[i];
            if (!IsCandidate(item, level))
            {
                continue;
            }

            list[n++] = new DeathCandidate(item.Wcid, Category(item.ItemType), UnitValue(item), 0, 0, 0, needed.Contains(item.Wcid));
        }

        // A stable insertion sort (the list is a few dozen items): category ascending, then value descending.
        for (var i = 1; i < n; i++)
        {
            var x = list[i];
            var j = i - 1;
            while (j >= 0 && (x.Category < list[j].Category || (x.Category == list[j].Category && x.UnitValue > list[j].UnitValue)))
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = x;
        }

        var previous = DeathItemCategory.None;
        for (var i = 0; i < n; i++)
        {
            var c = list[i];
            var halved = c.Category == previous ? c.UnitValue / 2 : c.UnitValue;
            previous = c.Category;
            list[i] = c with { HalvedValue = halved, MinFinal = VarianceMin(halved), MaxFinal = VarianceMax(c.Needed ? c.UnitValue : halved) };
        }

        return n == list.Length ? list : list[..n];
    }

    /// <summary>
    /// Can a needed item be among the items a death at <paramref name="level"/> drops? True when, in every corner of the variance, at least <see cref="MaxDrops"/> NOT-needed candidates have a minimum final value above the maximum final value of the best needed candidate
    /// (a needed item at its unhalved +10%): they outrank every needed item whatever the draw, so the first N never reach one. When no needed item is a candidate there is nothing to lose and the answer is yes.
    /// </summary>
    /// <param name="carried">What the character carries.</param>
    /// <param name="needed">The weenie class ids that must not drop (<see cref="NeededOf"/> for the death loop).</param>
    /// <param name="level">The character's level.</param>
    public static DropVerdict Survives(IReadOnlyList<CarriedItem> carried, IReadOnlySet<uint> needed, int level)
    {
        var ranked = Rank(carried, needed, level);
        var maxDrops = MaxDrops(level);
        uint bestWcid = 0;
        var bestMax = 0;
        var anyNeeded = false;
        foreach (var c in ranked)
        {
            if (c.Needed && (!anyNeeded || c.MaxFinal > bestMax))
            {
                anyNeeded = true;
                bestWcid = c.Wcid;
                bestMax = c.MaxFinal;
            }
        }

        var above = 0;
        var weakest = int.MaxValue;
        if (anyNeeded)
        {
            foreach (var c in ranked)
            {
                if (!c.Needed && c.MinFinal > bestMax)
                {
                    above++;
                    weakest = Math.Min(weakest, c.MinFinal);
                }
            }
        }

        return new DropVerdict(level, MinDrops(level), maxDrops, ranked.Length, above, bestWcid, bestMax, above == 0 ? 0 : weakest, !anyNeeded || above >= maxDrops);
    }

    /// <summary><see cref="Survives(IReadOnlyList{CarriedItem}, IReadOnlySet{uint}, int)"/> with the mage's kit as the needed set (<see cref="NeededOf"/>).</summary>
    /// <param name="carried">What the character carries.</param>
    /// <param name="kit">The mage's kit.</param>
    /// <param name="level">The character's level.</param>
    public static DropVerdict Survives(IReadOnlyList<CarriedItem> carried, GearKit kit, int level) => Survives(carried, NeededOf(kit), level);
}
