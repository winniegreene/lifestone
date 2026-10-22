using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Data.Magic;
using Lifestone.Data.Motion;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// One decoy: an ordinary, light, expensive item the death loop's mage carries so that a death drops it instead of the gear the loops need (M4 3.1 d0, D20 and D22).
/// </summary>
/// <param name="Wcid">The weenie class id (the <c>item</c> verb's argument).</param>
/// <param name="Name">The weenie's name.</param>
/// <param name="WeenieType">ACE's <c>WeenieType</c> (the read-only SELECT's <c>weenie_type</c>).</param>
/// <param name="ItemType">ACE's <c>ItemType</c> bits.</param>
/// <param name="Category">The death category of the ItemType (<see cref="DeathDropModel.Category"/>); the decoys are the first of their category among the mage's items, so none is halved.</param>
/// <param name="UnitValue">The Value of one item (PropertyInt 19).</param>
/// <param name="Burden">The burden of one item.</param>
/// <param name="VendorSold">True when the pinned world database lists a vendor that sells it (a retail player could buy one); false for an item only a monster or a chest gives.</param>
public sealed record DecoyItem(uint Wcid, string Name, uint WeenieType, uint ItemType, DeathItemCategory Category, int UnitValue, int Burden, bool VendorSold)
{
    /// <summary>The kit line for the item: <see cref="GearKind.Decoy"/>, stack 1, no valid location (it is never wielded), no maximum stack.</summary>
    public GearItem ToGearItem() => new(Wcid, Name, GearKind.Decoy, WeenieType, ItemType, EquipMask.None, 1, null, Burden);
}

/// <summary>What the decoy check found over a live inventory.</summary>
/// <param name="Verdict">The model's verdict (<see cref="DeathDropModel.Survives(IReadOnlyList{CarriedItem}, int)"/>); <see cref="DropVerdict.Survives"/> is false when the check could not be made.</param>
/// <param name="Made">True when the check ran over a known inventory at a known level; false when it could not (the text says why).</param>
/// <param name="Carried">How many held objects were read (a candidate or not).</param>
/// <param name="Unsettled">How many held objects have no world object yet (read as nothing: the answer is provisional when above 0).</param>
/// <param name="Text">The one log line: <c>decoy check: N items above the wand ...</c>.</param>
public readonly record struct DecoyCheck(DropVerdict Verdict, bool Made, int Carried, int Unsettled, string Text)
{
    /// <summary>The scenario's field: <c>yes</c> when the check was made and the needed items cannot drop, else <c>no</c>.</summary>
    public string Field => Made && Verdict.Survives ? "yes" : "no";
}

/// <summary>The mage's live load (M4 3.1 fix, review R7): the burden over the capacity, as the inventory page computes it (<see cref="DecoyKit.LoadOf"/>).</summary>
/// <param name="Burden">The burden now (the <c>EncumbranceVal</c> quality), 0 before the description arrives.</param>
/// <param name="Capacity">The burden capacity (<see cref="EncumbranceSystem.EncumbranceCapacity"/> of the Strength, as <see cref="LootContext"/> reads it).</param>
/// <param name="Percent">The load in percent (<see cref="EncumbranceSystem.Load"/> times 100; <c>gmBackpackUI::SetLoadLevel</c> 11.6096 0x004A7210 shows it rounded down); 300 without a capacity, as <c>Load</c> answers.</param>
public readonly record struct LoadShare(int Burden, int Capacity, double Percent)
{
    /// <summary>True when the load is over <see cref="DecoyKit.LoadWarnPercent"/>.</summary>
    public bool Over => Percent > DecoyKit.LoadWarnPercent;
}

/// <summary>
/// The decoys of the death loop (M4 3.1 d0, D20 and D22): eight ordinary items, one in each of eight death categories, each worth 8,000 or more and light, so that a death at BSD Mage's level (126, up to eight drops) drops them instead of what the loops
/// need (<see cref="DeathDropModel"/>). The rows are the lead's read-only SELECTs of the pinned world database (2026-10-21; <c>cache/m4/3-1/decoys.tsv</c>) and ACE's <c>Entity/DeathItem.cs</c> (the category of each ItemType): wcid, name, weenie type, ItemType, per-unit Value,
/// burden and whether a vendor sells it (the SELECT's vendor counts: 11, 14, 40 and 1 vendors for the first four; none for the last four), in the file's order. None is rare, bonded or attuned.
/// </summary>
/// <remarks>
/// <para><b>How many.</b> <see cref="CountFor"/> asks for as many decoys as the most items a death at the level can drop (<see cref="DeathDropModel.MaxDrops"/>, level / 20 + 2): the decoys alone outrank the wand in every corner of the variance, and the unneeded Mana Scarab (15,000 each,
/// <see cref="DeathDropModel.ManaScarabWcid"/>) is one more above the wand beyond them (nine items above the wand at level 126 against at most eight drops). The kit never carries more than the eight it has; a level that needs more is reported by
/// <see cref="DeathDropModel.Survives(IReadOnlyList{CarriedItem}, int)"/> (above below N_max), never guessed. Why they work: the needed gear tops out at the wand (5,400; 5,940 at +10%), every decoy is the first of its category among the items the mage carries (no halving) and
/// its worst case after -10% is at least 7,200.</para>
/// </remarks>
public static class DecoyKit
{
    /// <summary>The check's line names the bonded gate's gap every time (<c>TODO(m4-decoy-bonded-flag)</c>; review R3): the check cannot see a bonded flag, so it takes the kit's three known bonded weenies as the only bonded items.</summary>
    internal const string BondedGapText = "TODO(m4-decoy-bonded-flag): the bonded gate rests on the kit's three known bonded weenies, any other bonded item counts as a candidate";

    /// <summary>The load, in percent of the capacity, over which the gear stage warns (M4 3.1 fix, review R7: d0's kit with its decoys sat at 94.9%, and every dropped kit item comes back twice after the regear before the trip).</summary>
    public const int LoadWarnPercent = 95;

    /// <summary>The eight decoys, in the order of <c>decoys.tsv</c> (the order a kit takes them in).</summary>
    public static IReadOnlyList<DecoyItem> All { get; } =
    [
        new(27329, "Massive Mana Charge", 37, 0x80000, DeathItemCategory.ManaStone, 65_000, 50, true), // 11 vendors
        new(9295, "Intricate Carving Tool", 44, 0x80, DeathItemCategory.Misc, 10_000, 40, true), // 14 vendors
        new(41396, "Handle Mold", 51, 0x4000000, DeathItemCategory.CraftingIngredient, 10_000, 100, true), // 40 vendors (CraftAlchemyIntermediate)
        new(22565, "Tusker Island Shirt", 2, 0x4, DeathItemCategory.Clothing, 9_000, 57, true), // 1 vendor
        new(25522, "Skull Cap", 2, 0x2, DeathItemCategory.Armor, 8_500, 125, false),
        new(11007, "Virindi Scalpel", 6, 0x1, DeathItemCategory.MeleeWeapon, 8_420, 30, false),
        new(11129, "Healing Chocolate Olthoi Cake", 38, 0x20, DeathItemCategory.Food, 8_000, 25, false),
        new(27771, "Entrancing Adjanite Gem", 38, 0x800, DeathItemCategory.Gem, 15_000, 25, false),
    ];

    /// <summary>The burden of the eight decoys together (452: 50 + 40 + 100 + 57 + 125 + 30 + 25 + 25).</summary>
    public static int TotalBurden { get; } = SumBurden();

    private static int SumBurden()
    {
        var total = 0;
        foreach (var item in All)
        {
            total += item.Burden;
        }

        return total;
    }

    /// <summary>
    /// How many decoys the kit carries at <paramref name="level"/>: <see cref="DeathDropModel.MaxDrops"/> (level / 20 + 2 from level 21; 1 for levels 11 to 20; none up to 10), never more than the <see cref="All"/> it has: 8 at level 126, and 8 at level 400 (where ACE's cap of 14 asks for more than the
    /// kit has: <see cref="DeathDropModel.Survives(IReadOnlyList{CarriedItem}, int)"/> reports it).
    /// </summary>
    /// <param name="level">The character's level.</param>
    public static int CountFor(int level) => Math.Min(All.Count, DeathDropModel.MaxDrops(level));

    /// <summary>The first <see cref="CountFor"/> decoys of <see cref="All"/>.</summary>
    /// <param name="level">The character's level.</param>
    public static IReadOnlyList<DecoyItem> For(int level) => [.. All.Take(CountFor(level))];

    /// <summary>True when <paramref name="wcid"/> is one of the eight decoys' weenies.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public static bool IsDecoy(uint wcid)
    {
        foreach (var item in All)
        {
            if (item.Wcid == wcid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The name the check's line gives a needed item: "the wand", an armour piece or a component by name, else its weenie class id.</summary>
    internal static string NeededName(uint wcid)
    {
        if (wcid == GearKit.Wand.Wcid)
        {
            return "the wand";
        }

        foreach (var piece in GearKit.Armour)
        {
            if (piece.Wcid == wcid)
            {
                return piece.Name;
            }
        }

        foreach (var component in ComponentWeenies.All)
        {
            if (component.Wcid == wcid)
            {
                return component.Name;
            }
        }

        return "weenie " + wcid.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The decoy check over the live inventory at <paramref name="level"/>: every held object (the pack, the side packs and what is in them, what is equipped) is read as a <see cref="CarriedItem"/> (the weenie description's Value and ItemType, the stack the server last sent in a
    /// SetStackSize when there is one, else the description's, and whether it is equipped) and <see cref="DeathDropModel.Survives(IReadOnlyList{CarriedItem}, int)"/> answers for the death loop's needed set. Called once per gear stage (it allocates two small lists per call, never per frame)
    /// and never throws: an unknown inventory, an unknown level and a failure are answered as <see cref="DecoyCheck.Made"/> false with the reason in <see cref="DecoyCheck.Text"/>, never as a pass.
    /// <para><b><c>TODO(m4-decoy-bonded-flag)</c>.</b> The weenie description (<c>WeenieDesc</c>, ACE's <c>WorldObject_Networking</c> 855-883) carries Value, ItemType, stack size, burden and the object description flags, and no bonded flag: bonded (PropertyInt 33) reaches the client only in an appraisal.
    /// The check takes the kit's three known bonded weenies (<see cref="DeathDropModel.BondedKitWcids"/>) as bonded and every other item as not bonded, so an unknown bonded item among the carried would count as a candidate (it can only make the answer more hopeful if it is above the wand).
    /// A later item may read the appraisal. The check's line carries this TODO's name every time (M4 3.1 fix, review R3).</para>
    /// <para><b><c>TODO(m4-decoy-value-unknown)</c>.</b> An item whose description sends no Value counts as 0, as ACE's <c>wo.Value ?? 0</c> does; the check's line names this TODO and the count when any did (M4 3.1 fix, review R3).</para>
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="level">The character's level (<see cref="MageTemplate.TargetLevel"/> for BSD Mage).</param>
    public static DecoyCheck Check(GameState? state, int level)
    {
        try
        {
            return CheckCore(state, level);
        }
        catch (Exception e)
        {
            // Never throws (a gear stage must not die of its own report): a failed check is reported as not made, with the exception's type.
            return NotMade(level, "failed: " + e.GetType().Name);
        }
    }

    /// <summary>
    /// The live load share (M4 3.1 fix, review R7): <see cref="LootContext.Of"/> (the burden property over the capacity of the Strength with its enchantments and the carrying augmentations, as <see cref="LootRules"/> reads it, and
    /// the inventory page) through <see cref="EncumbranceSystem.Load"/>, in percent. Allocation-free.
    /// </summary>
    /// <param name="state">The session's state.</param>
    public static LoadShare LoadOf(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var context = LootContext.Of(state);
        return new LoadShare(context.Burden, context.Capacity, EncumbranceSystem.Load(context.Capacity, context.Burden) * 100.0);
    }

    /// <summary>
    /// The load's log line: <c>load: 1420 of 1500 burden, 94.7% of the capacity ...</c>; over <see cref="LoadWarnPercent"/> the warning <c>the decoy kit and duplicates leave the mage at N% of its capacity</c> (the regear before the corpse trip
    /// spawns every kit item a death dropped and the trip returns the other, so the duplicates are carried; over 100% the run slows and the trip's 90 s limit is at risk).
    /// </summary>
    /// <param name="load">The load.</param>
    internal static string LoadText(in LoadShare load) =>
        load.Over
            ? string.Create(CultureInfo.InvariantCulture, $"load: {load.Burden} of {load.Capacity} burden: the decoy kit and duplicates leave the mage at {load.Percent:0.#}% of its capacity (over {LoadWarnPercent}%; over 100% slows the run and the corpse trip)")
            : string.Create(CultureInfo.InvariantCulture, $"load: {load.Burden} of {load.Capacity} burden, {load.Percent:0.#}% of the capacity (the inventory page's load; warned over {LoadWarnPercent}%)");

    private static DecoyCheck NotMade(int level, string why) =>
        new(new DropVerdict(level, DeathDropModel.MinDrops(level), DeathDropModel.MaxDrops(level), 0, 0, 0, 0, 0, false), false, 0, 0, "decoy check: not made, " + why);

    private static DecoyCheck CheckCore(GameState? state, int level)
    {
        if (state is null)
        {
            return NotMade(level, "no state");
        }

        if (level < 1)
        {
            return NotMade(level, "the level is not known");
        }

        var inventory = state.Self.Inventory;
        if (!inventory.IsKnown)
        {
            return NotMade(level, "the inventory is not known yet");
        }

        var guids = new List<uint>();
        GearInventory.Collect(state, guids);
        var carried = new List<CarriedItem>(guids.Count);
        var unsettled = 0;
        var valueless = 0;
        foreach (var guid in guids)
        {
            if (!state.Objects.TryGet(guid, out var item))
            {
                unsettled++;
                continue;
            }

            var weenie = item.Weenie;
            int stack;
            int value;
            if (inventory.TryGetStack(guid, out var size, out var total))
            {
                stack = (int)size;
                value = (int)total;
            }
            else
            {
                stack = weenie.StackSize ?? 1;
                value = (int)(weenie.Value ?? 0);
                if (weenie.Value is null)
                {
                    valueless++; // TODO(m4-decoy-value-unknown): counted as 0, as ACE's wo.Value ?? 0 does; the check's line says so
                }
            }

            carried.Add(new CarriedItem(weenie.WeenieClassId, weenie.ItemType, value, Math.Max(1, stack), inventory.TryGetEquipped(guid, out _), DeathDropModel.BondedKitWcids.Contains(weenie.WeenieClassId)));
        }

        var verdict = DeathDropModel.Survives(carried, level);
        return new DecoyCheck(verdict, true, guids.Count, unsettled, Describe(verdict, unsettled, valueless));
    }

    /// <summary>
    /// The check's line: <c>decoy check: 9 items above the wand (best case 5940, the weakest of the 9 at least 7200); 6 to 8 drop at level 126 of 15 candidates: safe; TODO(m4-decoy-bonded-flag): ...</c>, then
    /// <c>; TODO(m4-decoy-value-unknown): N held object(s) sent no Value and count as 0</c> when some did (M4 3.1 fix, review R3: the two known gaps are logged, not only commented).
    /// </summary>
    internal static string Describe(in DropVerdict v, int unsettled, int valueless = 0)
    {
        var gaps = BondedGapText + (valueless > 0 ? string.Create(CultureInfo.InvariantCulture, $"; TODO(m4-decoy-value-unknown): {valueless} held object(s) sent no Value and count as 0") : string.Empty);
        var name = v.BestNeededWcid == 0 ? "the needed items" : NeededName(v.BestNeededWcid);
        var provisional = unsettled > 0 ? string.Create(CultureInfo.InvariantCulture, $" (provisional: {unsettled} held objects not yet known)") : string.Empty;
        return v.BestNeededWcid == 0
            ? string.Create(CultureInfo.InvariantCulture, $"decoy check: no needed item is a candidate at level {v.Level}; {v.MinDrops} to {v.MaxDrops} drop: safe{provisional}; {gaps}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"decoy check: {v.Above} items above {name} (best case {v.BestNeededMax}, the weakest of the {v.Above} at least {v.WeakestAboveMin}); {v.MinDrops} to {v.MaxDrops} drop at level {v.Level} of {v.Candidates} candidates: {(v.Survives ? "safe" : "NOT SAFE, the needed gear can drop")}{provisional}; {gaps}");
    }
}
