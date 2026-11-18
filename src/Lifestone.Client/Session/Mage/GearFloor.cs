using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Mage;

/// <summary>What a gear floor line buys.</summary>
public enum GearFloorKind
{
    /// <summary>A caster: the cheapest one a profile vendor sells, one line per caster missing under <see cref="GearFloor.CastersWanted"/>.</summary>
    Caster,

    /// <summary>A robe: the cheapest chest-covering clothing a profile vendor sells, when no chest-covering piece is worn.</summary>
    Robe,

    /// <summary>A mana charge: the held caster's want (<see cref="CasterManaRule"/>'s <see cref="CasterManaAct.WantStone"/>).</summary>
    ManaCharge,

    /// <summary>M6 7 AF: a pair of gauntlets, the cheapest hand armour a profile vendor sells, when no worn piece covers the hands.</summary>
    Gauntlets,
}

/// <summary>One line of the gear floor: a ware of a profile vendor, how many and why. It joins the resupply's buy list BESIDE the kit, never in <c>supplies.kit</c> (<see cref="GearKit.HasNoKit"/>).</summary>
/// <param name="Kind">What it buys.</param>
/// <param name="Wcid">The ware's weenie.</param>
/// <param name="Name">The ware's name.</param>
/// <param name="Count">How many.</param>
/// <param name="Price">The price of one at the vendor (the profile's).</param>
/// <param name="Vendor">The profile vendor who sells it.</param>
/// <param name="Why">Why, with the numbers.</param>
public sealed record GearFloorLine(GearFloorKind Kind, uint Wcid, string Name, int Count, int Price, GroundVendor Vendor, string Why)
{
    /// <summary>The log line: <c>gear floor: &lt;kind&gt;: buy &lt;n&gt; x &lt;name&gt; (weenie &lt;wcid&gt;) at &lt;vendor&gt; for &lt;price&gt; each: &lt;why&gt;</c>.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"gear floor: {Kind}: buy {Count} x {Name} (weenie {Wcid}) at {Vendor.Name} for {Price} each: {Why}");
}

/// <summary>What the floor is asked about: the character's level and what it holds and wears, and the pyreals the trip can spend.</summary>
/// <param name="Level">The character's level.</param>
/// <param name="Casters">The casters held, wielded and in the packs (<see cref="GearFloor.CastersHeld"/>).</param>
/// <param name="ChestCovered">True when a worn piece covers the chest (<see cref="GearFloor.ChestPiece"/>).</param>
/// <param name="ChestPiece">The worn chest-covering piece's name, or null.</param>
/// <param name="Purse">The pyreals the trip can spend (the coin and the held notes' face: a buy changes a note when the coin is short).</param>
/// <param name="HandsCovered">M6 7 AF: true when a worn piece covers the hands (<see cref="GearFloor.HandsCoveredBy"/>); the default true asks nothing of the hands.</param>
/// <param name="Bare">M6 item 14 (armour on every part she can cover): the parts of the body no worn piece covers (<see cref="GearCoverage.Gaps"/> of the worn pieces' coverage), 0 for none and for a state built with no worn read (the default).</param>
public sealed record GearFloorState(int Level, int Casters, bool ChestCovered, string? ChestPiece, long Purse, bool HandsCovered = true, uint Bare = 0);

/// <summary>The floor's plan: the lines to buy, the casters held, the loud stop when a missing caster cannot be bought, and the named notes (a robe or a charge that cannot be bought).</summary>
/// <param name="Lines">The lines, casters first, then the robe, then the mana charge.</param>
/// <param name="Casters">The casters held before the trip.</param>
/// <param name="Stop">The stop's reason (<see cref="GearFloor.NoCaster"/>), or null.</param>
/// <param name="Notes">The named lines that are not a stop.</param>
/// <param name="Why">The plan in one line.</param>
public sealed record GearFloorPlan(IReadOnlyList<GearFloorLine> Lines, int Casters, string? Stop, IReadOnlyList<string> Notes, string Why)
{
    /// <summary>True when the floor stops by name: a caster is missing and cannot be bought.</summary>
    public bool Stopped => Stop is not null;

    /// <summary>The casters the trip will hold if every line is bought.</summary>
    public int CastersAfter => Casters + Lines.Where(l => l.Kind == GearFloorKind.Caster).Sum(l => l.Count);
}

/// <summary>
/// M6 2 b (the gear floor, M6 spec "gearing": the spells are the armour, two casters always): what a mage must hold and wear from the floor level up, bought from the ground's own profile vendors. Pure over the state's numbers and the
/// profile (<see cref="HuntingGround.Vendors"/>, their <see cref="GroundVendor.Sells"/> and prices). Below the floor level nothing; from it: under <see cref="CastersWanted"/> casters, the cheapest caster a profile vendor sells, one line per
/// missing caster; no chest-covering piece worn, the cheapest robe a profile vendor sells; the held caster's mana want (<see cref="CasterManaRule"/>), the charge line.
/// </summary>
/// <remarks>
/// <para><b>The floor level</b> is the ladder's: the start rung's <c>move_on.level</c> (the rung of level 1, <c>academy-and-chest</c> in <c>ladder/war-mage.json</c>, 5 today), read through <see cref="FloorLevel"/>; no constant (M6 D26).
/// The Academy's quests run below it, with the Training Wand the creation gives.</para>
/// <para><b>Which ware is a caster or a robe</b> is the supply weenie's (<see cref="GearKit.FloorWeenieOf"/>: ItemType Caster 0x8000; clothing or armour whose ValidLocations hold ChestArmor 0x200), read from the pinned world SQL; a
/// ware not in that table is never a floor line.</para>
/// <para><b>The stop.</b> A caster missing under <see cref="CastersWanted"/> that cannot be bought (no profile vendor sells one, or the purse is short of its price) is the loud stop <see cref="NoCaster"/> (<c>TODO(m5-no-caster)</c>'s
/// name) with the reason. A robe or a charge that cannot be bought is a named note, not a stop.</para>
/// <para><b>Never the kit.</b> The lines are the resupply's, beside the kit: <c>supplies.kit</c> stays as the profile writes it, so <see cref="GearKit.HasNoKit"/> stays true for the outskirts and the kit-less death keeps
/// <c>rearm=corpse</c> or <c>none</c> (a bought spare in the pack makes it <c>rearm=pack</c>, <see cref="DeathHandler"/>).</para>
/// </remarks>
public static class GearFloor
{
    /// <summary>The casters a mage holds from the floor level: one wielded and one spare (the owner's M6 direction of two casters always; a character rule, not a ground's).</summary>
    public const int CastersWanted = 2;

    /// <summary>The loud stop's name (<c>m5-no-caster</c>).</summary>
    public const string NoCaster = "no-caster";

    /// <summary>The ChestArmor bit of an EquipMask (a robe's ValidLocations 0x7F00 holds it).</summary>
    public const uint ChestArmor = (uint)EquipMask.ChestArmor;

    /// <summary>The HandWear bit of an EquipMask (a pair of gauntlets' ValidLocations is 0x20).</summary>
    public const uint HandWear = (uint)EquipMask.HandWear;

    /// <summary>ACE's CoverageMask OuterwearChest (0x400): a worn piece's ClothingPriority that covers the chest (the robes' 0x7F00 and 0x17F00 hold it).</summary>
    public const uint OuterwearChest = 0x400;

    /// <summary>The floor level of <paramref name="ladder"/>: its start rung's (the rung of level 1) <c>move_on.level</c>.</summary>
    /// <param name="ladder">The ladder (<see cref="LadderLoader.WarMage"/>).</param>
    public static int FloorLevel(Ladder ladder)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        return ladder.RungFor(1).MoveOn.Level;
    }

    /// <summary>
    /// The casters the character holds and can wield: wielded, in the main pack and in the side packs (a state read, not a tally). M6 2 f (2-R3): a caster counts when it is wielded, or its appraisal's requirements are met
    /// (<see cref="SelfEquipJudge.Unmet"/> is null), or it is not appraised yet (counted while unread, and named in <paramref name="unread"/>); a looted wand that needs a skill the character lacks is not the spare. Slice e prints it on the autopilot's cycle line.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="unread">Filled with the names of the counted casters whose appraisal is not read yet, or null.</param>
    public static int CastersHeld(GameState state, List<string>? unread = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var guids = new List<uint>();
        GearInventory.Collect(state, guids);
        var n = 0;
        foreach (var guid in guids)
        {
            if (state.Objects.TryGet(guid, out var item) && CountsAsCaster(state, item, out var isUnread))
            {
                n++;
                if (isUnread)
                {
                    unread?.Add(item.Name);
                }
            }
        }

        return n;
    }

    /// <summary>True when <paramref name="item"/> is a caster the character can use as one of the floor's two: wielded, or with its requirements met, or not appraised yet (<paramref name="unread"/>).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="item">The held object.</param>
    /// <param name="unread">True when the caster counts only because its appraisal is not read.</param>
    public static bool CountsAsCaster(GameState state, WorldObject item, out bool unread)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(item);
        unread = false;
        if ((item.ItemType & SelfEquipJudge.CasterType) == 0)
        {
            return false;
        }

        if (state.Self.Inventory.Contains(item.Guid) && state.Self.Inventory.Find(item.Guid).Kind == InventoryPlaceKind.Equipped)
        {
            return true; // wielded: its requirements were met when it went on
        }

        if (!state.Appraisals.TryGet(item.Guid, out var appraisal) || appraisal.IsNotFound)
        {
            unread = true;
            return true;
        }

        return SelfEquipJudge.Unmet(appraisal, new QualitiesSkills(state.Self.Qualities)) is null;
    }

    /// <summary>The worn piece that covers the chest (its location holds ChestArmor or its coverage OuterwearChest), or null.</summary>
    /// <param name="worn">The worn pieces (<see cref="SelfEquipJudge.Worn"/>).</param>
    public static WornPiece? ChestPiece(IReadOnlyList<WornPiece> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        return worn.FirstOrDefault(w => (w.Location & ChestArmor) != 0 || (w.Coverage & OuterwearChest) != 0);
    }

    /// <summary>M6 7 AF: true when a worn piece covers the hands (its coverage holds <see cref="GearCoverage.Hands"/>, or, with none read, its location holds HandWear 0x20).</summary>
    /// <param name="worn">The worn pieces (<see cref="SelfEquipJudge.Worn"/>).</param>
    public static bool HandsCoveredBy(IReadOnlyList<WornPiece> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        return worn.Any(w => (w.Coverage & GearCoverage.Hands) != 0 || (w.Coverage == 0 && (w.Location & HandWear) != 0));
    }

    /// <summary>The floor's state of <paramref name="state"/>: its level, casters, chest piece and <paramref name="purse"/>.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="purse">The pyreals the trip can spend.</param>
    public static GearFloorState StateOf(GameState state, long purse)
    {
        ArgumentNullException.ThrowIfNull(state);
        var worn = SelfEquipJudge.Worn(state);
        var chest = ChestPiece(worn);
        return new GearFloorState(state.Self.Qualities.Level, CastersHeld(state), chest is not null, chest?.Name, purse, HandsCoveredBy(worn), BareParts(worn));
    }

    /// <summary>M6 item 14: the parts of the body no worn piece covers (<see cref="GearCoverage.Gaps"/>): 0 with the Pathwarden plate set on, the hands (0x8000) under a robe alone.</summary>
    /// <param name="worn">The worn pieces (<see cref="SelfEquipJudge.Worn"/>).</param>
    public static uint BareParts(IReadOnlyList<WornPiece> worn)
    {
        ArgumentNullException.ThrowIfNull(worn);
        return GearCoverage.Gaps(worn.Select(w => GearCoverage.OfWorn(w.Coverage, w.Location))); // a piece whose ClothingPriority is not read covers the parts of its location: never read as bare for a header's gap
    }

    /// <summary>The parts of <paramref name="coverage"/> by name (<see cref="GearScore.BodyParts"/>), for a log line: <c>head, hand</c>.</summary>
    /// <param name="coverage">The coverage bits.</param>
    public static string PartNames(uint coverage) => string.Join(", ", GearScore.BodyParts.Where(p => (p.Coverage & GearCoverage.Body & coverage) != 0).Select(p => p.Part));

    /// <summary>The floor's lines for <paramref name="state"/> on <paramref name="ground"/> (see the class remarks).</summary>
    /// <param name="ground">The ground: its vendors and their prices.</param>
    /// <param name="state">The character's numbers.</param>
    /// <param name="floorLevel">The floor level (<see cref="FloorLevel"/>).</param>
    /// <param name="mana">The held caster's mana decision (a want becomes the charge line), or null.</param>
    /// <param name="worn">M6 2 f (2-R5): the worn pieces (<see cref="SelfEquipJudge.Worn"/>), or null; with the chest bare, a worn piece that covers part of the robe's coverage means the robe cannot go on (ACE's coverage rule, Creature_Equipment.cs:50-58, 93-105 @95ef426d), so no robe is bought and the note says so.</param>
    public static GearFloorPlan Plan(HuntingGround ground, GearFloorState state, int floorLevel, CasterManaDecision? mana = null, IReadOnlyList<WornPiece>? worn = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(state);
        var lines = new List<GearFloorLine>();
        var notes = new List<string>();
        if (state.Level < floorLevel)
        {
            return new GearFloorPlan(lines, state.Casters, null, notes, Text($"gear floor: level {state.Level} is under the floor level {floorLevel} (the ladder's start rung's move_on.level): nothing; casters={state.Casters}"));
        }

        var purse = state.Purse;
        string? stop = null;
        var missing = Math.Max(0, CastersWanted - state.Casters);
        if (missing > 0)
        {
            if (Cheapest(ground, IsCaster) is not { } caster)
            {
                stop = Text($"{NoCaster}: {state.Casters} caster(s) held, under {CastersWanted}, and no vendor of {ground.Home.Town} sells a caster (the profile's vendors list none)");
            }
            else
            {
                for (var i = 0; i < missing; i++)
                {
                    if (purse < caster.Ware.Price)
                    {
                        stop = Text($"{NoCaster}: {state.Casters + i} caster(s) after the trip, under {CastersWanted}: the purse {purse} is short of the {caster.Ware.Name}'s {caster.Ware.Price} at {caster.Vendor.Name}");
                        break;
                    }

                    purse -= caster.Ware.Price;
                    lines.Add(new GearFloorLine(GearFloorKind.Caster, caster.Ware.Wcid, caster.Ware.Name, 1, caster.Ware.Price, caster.Vendor, Text($"{state.Casters + i} caster(s) held, {CastersWanted} wanted: the cheapest caster a vendor of {ground.Home.Town} sells")));
                }
            }
        }

        if (!state.ChestCovered)
        {
            if (Cheapest(ground, IsRobe) is not { } robe)
            {
                notes.Add(Text($"gear floor: robe: no chest-covering piece is worn and no vendor of {ground.Home.Town} sells a robe (TODO(m6-no-robe-vendor))"));
            }
            else if (worn?.FirstOrDefault(w => (w.Coverage & RobeCoverage(robe.Ware.Wcid)) != 0) is { } blocker)
            {
                notes.Add(Text($"gear floor: robe: no chest-covering piece is worn, but the {robe.Ware.Name} (coverage 0x{RobeCoverage(robe.Ware.Wcid):X}) cannot go on: the worn '{blocker.Name}' 0x{blocker.Guid:X8} covers 0x{blocker.Coverage & RobeCoverage(robe.Ware.Wcid):X} of it: not bought"));
            }
            else if (purse < robe.Ware.Price)
            {
                notes.Add(Text($"gear floor: robe: no chest-covering piece is worn, but the purse {purse} is short of the {robe.Ware.Name}'s {robe.Ware.Price} at {robe.Vendor.Name}: not bought"));
            }
            else
            {
                purse -= robe.Ware.Price;
                lines.Add(new GearFloorLine(GearFloorKind.Robe, robe.Ware.Wcid, robe.Ware.Name, 1, robe.Ware.Price, robe.Vendor, Text($"no chest-covering piece is worn: the cheapest robe a vendor of {ground.Home.Town} sells")));
            }
        }

        if (!state.HandsCovered)
        {
            // M6 7 AF: the robe covers all of the body but the hands; the chest's pair is left by the burden gate when it does not close the gap, so the floor buys the cheapest hand armour a profile vendor sells.
            if (Cheapest(ground, IsHandWear) is not { } gloves)
            {
                notes.Add(Text($"gear floor: gauntlets: no piece covers the hands and no vendor of {ground.Home.Town} sells hand armour (TODO(m6-no-gauntlet-vendor))"));
            }
            else if (purse < gloves.Ware.Price)
            {
                notes.Add(Text($"gear floor: gauntlets: the purse {purse} is short of the {gloves.Ware.Name}'s {gloves.Ware.Price} at {gloves.Vendor.Name}: not bought"));
            }
            else
            {
                purse -= gloves.Ware.Price;
                lines.Add(new GearFloorLine(GearFloorKind.Gauntlets, gloves.Ware.Wcid, gloves.Ware.Name, 1, gloves.Ware.Price, gloves.Vendor, Text($"no worn piece covers the hands (coverage 0x{GearCoverage.Hands:X}): the cheapest hand armour a vendor of {ground.Home.Town} sells")));
            }
        }

        // M6 item 14 (the owner: armour on every part she can cover): a part of the body left bare beside the chest and the hands is said by name, every plan. No floor line buys it: the ladder's character wears the Pathwarden chest's set
        // (bonded: it never leaves her on a death), and a profile vendor's ware for the head, the legs or the feet is no floor ware yet.
        var otherBare = state.Bare & ~GearCoverage.Hands & (state.ChestCovered ? ~0u : 0u);
        if (otherBare != 0)
        {
            notes.Add(Text($"gear floor: armour: the worn pieces leave 0x{otherBare:X} of the body bare ({PartNames(otherBare)}) and no floor line buys a piece for those parts (TODO(m6-armour-floor-vendor): the chest's set is the armour; a vendor ware for them is not a floor ware)"));
        }

        if (mana is { Act: CasterManaAct.WantStone } want)
        {
            var vendor = want.WantWcid == 0 ? null : ground.Vendors.FirstOrDefault(v => v.Wcid == want.WantVendor && v.Sells.Any(w => w.Wcid == want.WantWcid));
            var ware = vendor?.Sells.First(w => w.Wcid == want.WantWcid);
            if (vendor is null || ware is null)
            {
                notes.Add(Text($"gear floor: mana charge: {want.Todo ?? "TODO(m6-no-stone-vendor)"}: the want names no ware a profile vendor sells ({want.Reason})"));
            }
            else
            {
                var count = ware.Price <= 0 ? want.WantCount : (int)Math.Min(want.WantCount, purse / ware.Price);
                if (count <= 0)
                {
                    notes.Add(Text($"gear floor: mana charge: the purse {purse} is short of one {ware.Name} at {ware.Price} ({vendor.Name}): not bought"));
                }
                else
                {
                    purse -= (long)count * ware.Price;
                    lines.Add(new GearFloorLine(GearFloorKind.ManaCharge, ware.Wcid, ware.Name, count, ware.Price, vendor, Text($"the held caster's mana: {want.Reason}{(count < want.WantCount ? $"; the purse pays {count} of {want.WantCount}" : string.Empty)}")));
                }
            }
        }

        var why = Text($"gear floor: level {state.Level} (floor {floorLevel}), casters={state.Casters}, chest {(state.ChestCovered ? $"'{state.ChestPiece}'" : "bare")}{(state.Bare == 0 ? string.Empty : $", bare parts 0x{state.Bare:X}")}, purse {state.Purse}: {(lines.Count == 0 ? "nothing to buy" : string.Join(", ", lines.Select(l => $"{l.Count} x {l.Name} at {l.Vendor.Name}")))}{(stop is null ? string.Empty : "; STOP " + stop)}");
        return new GearFloorPlan(lines, state.Casters, stop, notes, why);
    }

    /// <summary>True when the ware <paramref name="wcid"/> is one the floor buys: a caster or a robe of <see cref="GearKit.FloorWeenieOf"/>, or a Mana Charge (<see cref="CasterManaRule.ChargeMana"/>). The profile loader lets a vendor list it.</summary>
    /// <param name="wcid">The ware's weenie.</param>
    public static bool IsFloorWare(uint wcid) => CasterManaRule.ChargeMana.ContainsKey(wcid) || (GearKit.FloorWeenieOf(wcid) is { } w && (IsCaster(w) || IsRobe(w)));

    /// <summary>
    /// A floor robe's ClothingPriority (its coverage), as the pinned world SQL holds it (the sources in <see cref="GearKit.SupplyWeenies"/>): 5851 the hooded Faran Robe 0x17F00 (the head and the feet with the body), the other robes 0x7F00. The supply table
    /// keeps no coverage, so an unlisted robe reads its ValidLocations' high bits (<c>TODO(m6-robe-coverage)</c>: add the column when a profile sells another robe).
    /// </summary>
    internal static uint RobeCoverage(uint wcid) => wcid == 5851 ? 0x17F00u : GearKit.FloorWeenieOf(wcid) is { } w ? (uint)w.ValidLocations & 0xFFFFFF00u : 0x7F00u;

    private static bool IsCaster(GearKit.SupplyWeenie w) => (w.ItemType & SelfEquipJudge.CasterType) != 0;

    /// <summary>M6 7 AF: true when the ware <paramref name="wcid"/> is hand armour. Not part of <see cref="IsFloorWare"/>: the audit names the kit's gloves apart (StartupAudit), and the profile loader lists it only as a kit line today (TODO(m6-no-gauntlet-vendor): the lead lets the loader accept it for the outskirts).</summary>
    public static bool IsGauntletWare(uint wcid) => GearKit.FloorWeenieOf(wcid) is { } w && IsHandWear(w);

    private static bool IsHandWear(GearKit.SupplyWeenie w) => (w.ItemType & (GearKit.ItemTypeClothing | GearKit.ItemTypeArmor)) != 0 && ((uint)w.ValidLocations & HandWear) != 0 && ((uint)w.ValidLocations & ChestArmor) == 0;

    private static bool IsRobe(GearKit.SupplyWeenie w) => (w.ItemType & (GearKit.ItemTypeClothing | GearKit.ItemTypeArmor)) != 0 && ((uint)w.ValidLocations & ChestArmor) != 0;

    private static (GroundVendor Vendor, VendorWare Ware)? Cheapest(HuntingGround ground, Func<GearKit.SupplyWeenie, bool> kind)
    {
        (GroundVendor Vendor, VendorWare Ware)? best = null;
        foreach (var vendor in ground.Vendors)
        {
            foreach (var ware in vendor.Sells)
            {
                if (GearKit.FloorWeenieOf(ware.Wcid) is { } w && kind(w) && (best is null || ware.Price < best.Value.Ware.Price || (ware.Price == best.Value.Ware.Price && ware.Wcid < best.Value.Ware.Wcid)))
                {
                    best = (vendor, ware);
                }
            }
        }

        return best;
    }

    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
