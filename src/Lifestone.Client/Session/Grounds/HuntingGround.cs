using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Grounds;

/// <summary>What kind of place a hunting ground is.</summary>
public enum GroundKind
{
    /// <summary>A dungeon landblock reached by portal: one or more storeys of indoor cells, over one or more landblocks.</summary>
    Dungeon,

    /// <summary>Outdoor cells of one or more landblocks: <see cref="HuntingGround.Outdoor"/> holds the cells and the creature table.</summary>
    Outdoor,
}

/// <summary>What a ground's creature of a weenie class is to the loop.</summary>
public enum GroundRole
{
    /// <summary>The loop fights it.</summary>
    Target,

    /// <summary>The loop never fights it (and keeps out of where it lives: the no-go cells).</summary>
    Avoid,
}

/// <summary>What the dats say of an outdoor cell's ground (the same reading as M4's <c>HuntTerrain</c>; <c>HuntingGroundDatTests</c> pins it, item 2 g3).</summary>
public enum GroundTerrain
{
    /// <summary>Not read.</summary>
    Unread,

    /// <summary>No water at the cell's corners and every sampled surface normal walkable.</summary>
    Dry,

    /// <summary>A corner of the cell is water.</summary>
    Water,

    /// <summary>A sampled surface normal is too steep to stand on.</summary>
    Steep,
}

/// <summary>
/// A hunting ground as data (M5 DoD 4, D22, D43): one embedded JSON profile under <c>Session/Grounds/profiles/</c>, read once by <see cref="GroundCatalog"/>, which fails at load on a missing,
/// unknown or out-of-range field (naming the file and the field's JSON path). A fact about one dungeon, town, vendor, spell list or route is a value here, never a constant in code (the spec's
/// data-first rule, section 4). Every section names its source in <see cref="Sources"/> (the dats, the pinned world SQL v0.9.296, or a measure of m5-plan.md section 8).
/// </summary>
/// <remarks>
/// <para>The per-frame lookups (<see cref="IsWalkable"/>, <see cref="IsNoGo"/>, <see cref="StoreyOf"/>, <see cref="RoleOf"/>, <see cref="InLandblocks"/>) allocate nothing: the cell sets are bit masks
/// per landblock (<see cref="CellSet"/>).</para>
/// <para>The walkable set is the storeys' cells without the no-go cells: the no-go set wins over a storey.</para>
/// <para>TODO(m6-ground-fields): the fields M6's grounds need and M5's do not (m5-plan.md D43, section 9) are not built: entry gates, variants, damage multipliers, target roles beyond
/// target and avoid, spawn models beyond the generator slot, hazards, interactables, exit kinds, vendor roles and the return route. A profile that needs one adds it here when it does.</para>
/// </remarks>
/// <param name="Key">The catalog key, also the profile file's name without <c>.json</c> (lower case, words joined by hyphens).</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">Dungeon or outdoor.</param>
/// <param name="Sources">The source of each section, by the section's JSON name (<see cref="SectionNames"/>; every one present).</param>
/// <param name="Landblocks">The landblocks the ground's own cells lie in (the high word of every cell id of the arrival, the storeys, the no-go set, the tour, the hold spots, the slots and the outdoor cells).</param>
/// <param name="Arrival">Where the ground's portal (or the place) puts the player.</param>
/// <param name="Walk">What the ground's room graph and walk need: the doorway lean and the leg's stop rules.</param>
/// <param name="Storeys">The storeys by z band, each with its cells (an outdoor ground may have none).</param>
/// <param name="NoGo">The cells the loop never enters.</param>
/// <param name="Targets">The creatures by weenie class id with name, level and role.</param>
/// <param name="LevelBand">The creature levels the ground's loop fights.</param>
/// <param name="Portal">The ground's portal with its tie and recall spells, or null when none is used.</param>
/// <param name="Home">The home town: its lifestone's cell and the point the recall-home station walks to.</param>
/// <param name="Vendors">The town's vendors the ground's kit and loot need.</param>
/// <param name="SpellPlan">The war spell by character level (D41), ranges that do not overlap.</param>
/// <param name="Components">The components the plan's spells burn, per spell level.</param>
/// <param name="HoldSpots">The stand-and-hold spots (empty is a value).</param>
/// <param name="Respawn">The generators (each with its own delay and tick) and their slots.</param>
/// <param name="Tour">The cells the walk visits, in order.</param>
/// <param name="Outdoor">The outdoor ground's cells and creature table, or null for a dungeon.</param>
public sealed record HuntingGround(
    string Key,
    string Name,
    GroundKind Kind,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyList<ushort> Landblocks,
    GroundArrival Arrival,
    WalkModel Walk,
    IReadOnlyList<GroundStorey> Storeys,
    CellSet NoGo,
    IReadOnlyList<GroundTarget> Targets,
    LevelBand LevelBand,
    GroundPortal? Portal,
    GroundHome Home,
    IReadOnlyList<GroundVendor> Vendors,
    IReadOnlyList<SpellPlanRow> SpellPlan,
    IReadOnlyList<GroundComponent> Components,
    IReadOnlyList<HoldSpot> HoldSpots,
    RespawnModel Respawn,
    IReadOnlyList<uint> Tour,
    OutdoorGround? Outdoor)
{
    /// <summary>The sections every profile names a source for, in the file's order (the keys of <see cref="Sources"/>).</summary>
    public static IReadOnlyList<string> SectionNames { get; } = Array.AsReadOnly(new[]
    {
        "landblocks", "arrival", "walk", "storeys", "no_go", "targets", "level_band", "portal", "home", "vendors", "spell_plan", "components", "supplies", "hold_spots", "respawn", "tour", "outdoor",
    });

    /// <summary>The storeys' cells without the no-go cells (built once).</summary>
    public CellSet Walkable { get; } = CellSet.Union(Storeys.Select(s => s.Cells), NoGo);

    /// <summary>True when the full cell id is in one of <see cref="Landblocks"/>. Allocates nothing.</summary>
    /// <param name="cell">The full cell id.</param>
    public bool InLandblocks(uint cell)
    {
        var landblock = (ushort)(cell >> 16);
        for (var i = 0; i < Landblocks.Count; i++)
        {
            if (Landblocks[i] == landblock)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the full cell id is one the loop may stand in: a storey's cell that is not no-go. Allocates nothing.</summary>
    /// <param name="cell">The full cell id.</param>
    public bool IsWalkable(uint cell) => Walkable.Contains(cell);

    /// <summary>True when the full cell id is a no-go cell. Allocates nothing.</summary>
    /// <param name="cell">The full cell id.</param>
    public bool IsNoGo(uint cell) => NoGo.Contains(cell);

    /// <summary>The index in <see cref="Storeys"/> of the storey holding the full cell id, or -1. Allocates nothing.</summary>
    /// <param name="cell">The full cell id.</param>
    public int StoreyOf(uint cell)
    {
        for (var i = 0; i < Storeys.Count; i++)
        {
            if (Storeys[i].Cells.Contains(cell))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The role of a creature of weenie class <paramref name="wcid"/>, or null when the profile does not list it. Allocates nothing.</summary>
    /// <param name="wcid">The weenie class id.</param>
    public GroundRole? RoleOf(uint wcid)
    {
        for (var i = 0; i < Targets.Count; i++)
        {
            if (Targets[i].Wcid == wcid)
            {
                return Targets[i].Role;
            }
        }

        return null;
    }

    /// <summary>The spell plan's row for a character level, or null when no row holds it. Allocates nothing.</summary>
    /// <param name="characterLevel">The character's level.</param>
    public SpellPlanRow? PlanFor(int characterLevel)
    {
        for (var i = 0; i < SpellPlan.Count; i++)
        {
            if (characterLevel >= SpellPlan[i].MinLevel && characterLevel <= SpellPlan[i].MaxLevel)
            {
                return SpellPlan[i];
            }
        }

        return null;
    }

    /// <summary>
    /// What the ground's loop buys, wears and carries (M5 item 3 m0, D48, D49): the stock lines, the vendor kit, the burden line and the purse. A line's vendor and price are not fields here: they are the home
    /// vendors' <see cref="GroundVendor.Sells"/> (<see cref="VendorSelling"/>, <see cref="PriceOf"/>), the one source of a price. The profile's <c>supplies</c> section sets it; a ground built by hand has
    /// <see cref="GroundSupplies.Empty"/> (the catalog's loader never leaves it so: the section is required).
    /// </summary>
    public GroundSupplies Supplies { get; init; } = GroundSupplies.Empty;

    /// <summary>
    /// M5 7 F16 (D91, the owner): the health share, in percent, under which the autopilot's upkeep stops the fight for the health (the profile's optional <c>fight.retreat_health_percent</c>). 0 means never: the fight does not retreat
    /// on this ground and the emergency drain (<c>FightEmergency</c>) holds the health instead. Null (the section is absent) keeps the derived line (<c>AutopilotOptions.HealthFloorPercent</c>). The other upkeep reasons (the buff margin, the logistics) are unchanged.
    /// </summary>
    public int? FightRetreatHealthPercent { get; init; }

    /// <summary>
    /// M5 7 P7i (D144, the owner): floor regions the walk's planner avoids (the profile's optional <c>avoid_regions</c>): each is seeded into the ground's room graph as a costly box (<c>RoomPatches.AddBox</c>), so plans go round it
    /// while another way exists, and the runner's stuck ladder never moves the mage into it. Empty when the section is absent.
    /// </summary>
    public IReadOnlyList<GroundAvoidRegion> AvoidRegions { get; init; } = [];

    /// <summary>
    /// M5 7 F28 (D95, the owner's rule: loot only the most valuable stuff to sell): the ground's loot setting (the profile's optional <c>loot</c> section), or null when the section is absent (the fight's loot rule then keeps today's floor of
    /// <see cref="GroundLoot.DefaultMinValuePerBurden"/> and every item type). Read by <c>FightOptions.EffectiveLootThresholds</c>.
    /// </summary>
    public GroundLoot? Loot { get; init; }

    /// <summary>
    /// M5 7 F28: the item types some vendor of the profile buys (the union of the vendors' <see cref="GroundVendor.BuysItemTypes"/>), which is what the loot rule may keep: an item of a type no vendor here buys cannot be sold, so it is left
    /// (logged "type not sold here"). 0 when the profile has no vendor. The same vendors the logistics trip sells at, so the keep and the sale never disagree.
    /// </summary>
    public uint SellableItemTypes
    {
        get
        {
            uint mask = 0;
            for (var i = 0; i < Vendors.Count; i++)
            {
                mask |= Vendors[i].BuysItemTypes;
            }

            return mask;
        }
    }

    /// <summary>M5 7 F29 (D96): the vendors' (mask, rate) pairs, which is how the loot rule reads the best buyer's BuyPrice for an item's type. A vendor whose mask is 0 buys nothing here (M5 7 P7j: the learn list's scrivener) and is no buyer.</summary>
    public IReadOnlyList<LootBuyer> LootBuyers
    {
        get
        {
            var list = new List<LootBuyer>(Vendors.Count);
            for (var i = 0; i < Vendors.Count; i++)
            {
                if (Vendors[i].BuysItemTypes != 0)
                {
                    list.Add(new LootBuyer(Vendors[i].BuysItemTypes, Vendors[i].BuyPrice));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// M5 7 P7j (D143, the owner): the spells the character learns by play on this ground's town trips (the profile's optional <c>learn</c> list): each names the spell, the scroll that teaches it and the vendor who sells the scroll (a
    /// vendor of <see cref="Vendors"/> whose <c>sells</c> lists it). The resupply buys the scroll while the spellbook lacks the spell and reads it; nothing is granted. Empty when the section is absent.
    /// </summary>
    public IReadOnlyList<LearnLine> Learn { get; init; } = [];

    /// <summary>M5 7 F22: the fewest tuskers within the Ring of Fire's reach before the war picker weighs it (the profile's optional <c>fight.ring_min_targets</c>, 1 to 9); null keeps the default of three (<see cref="Lifestone.Client.Session.Mage.WarPickSettings.DefaultRingMinTargets"/>).</summary>
    public int? FightRingMinTargets { get; init; }

    /// <summary>
    /// The home vendor whose <see cref="GroundVendor.Sells"/> lists the ware <paramref name="wcid"/>, or null when no vendor of the profile does (a carried line: the mage holds it and nothing here restocks it, D8).
    /// The loader allows a supply line's ware under one vendor only, so there is one answer. Allocates nothing.
    /// </summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public GroundVendor? VendorSelling(uint wcid)
    {
        for (var i = 0; i < Vendors.Count; i++)
        {
            var sells = Vendors[i].Sells;
            for (var j = 0; j < sells.Count; j++)
            {
                if (sells[j].Wcid == wcid)
                {
                    return Vendors[i];
                }
            }
        }

        return null;
    }

    /// <summary>The price in pyreals of the ware <paramref name="wcid"/> at its home vendor (from <see cref="GroundVendor.Sells"/>), or null when no vendor of the profile lists it (carried). Allocates nothing.</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public int? PriceOf(uint wcid)
    {
        for (var i = 0; i < Vendors.Count; i++)
        {
            var sells = Vendors[i].Sells;
            for (var j = 0; j < sells.Count; j++)
            {
                if (sells[j].Wcid == wcid)
                {
                    return sells[j].Price;
                }
            }
        }

        return null;
    }

    /// <summary>True when no vendor of the profile sells the ware <paramref name="wcid"/>: the mage carries it and its low count is a named stop, not a trip (D8, D48). Allocates nothing.</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public bool IsCarried(uint wcid) => VendorSelling(wcid) is null;

    /// <summary>The vendor of weenie class <paramref name="wcid"/>, or null.</summary>
    /// <param name="wcid">The vendor's weenie class id.</param>
    public GroundVendor? VendorOf(uint wcid)
    {
        for (var i = 0; i < Vendors.Count; i++)
        {
            if (Vendors[i].Wcid == wcid)
            {
                return Vendors[i];
            }
        }

        return null;
    }
}

/// <summary>Where the ground's portal or place puts the player: the cell, the landblock-local origin (the feet) and the heading as a yaw quaternion (w, z), as the world data gives it.</summary>
/// <param name="Cell">The full cell id.</param>
/// <param name="Origin">The landblock-local origin.</param>
/// <param name="HeadingW">The quaternion's w.</param>
/// <param name="HeadingZ">The quaternion's z (x and y are 0).</param>
public sealed record GroundArrival(uint Cell, Vector3 Origin, float HeadingW, float HeadingZ)
{
    /// <summary>The arrival as a <see cref="CellFrame"/>.</summary>
    public CellFrame Frame => new(Cell, new AcFrame(Origin, new Quaternion(0f, 0f, HeadingZ, HeadingW)));
}

/// <summary>One storey of a dungeon: a name, its z band (the cells' origin z, inclusive) and its cells.</summary>
/// <param name="Name">The storey's name.</param>
/// <param name="ZMin">The band's lowest z.</param>
/// <param name="ZMax">The band's highest z.</param>
/// <param name="Cells">The storey's cells (a cell is in at most one storey).</param>
public sealed record GroundStorey(string Name, float ZMin, float ZMax, CellSet Cells);

/// <summary>A creature of the ground by weenie class id.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name (for the logs).</param>
/// <param name="Level">The creature's level (int property 25; not on the wire, so the profile is its source).</param>
/// <param name="Role">Target or avoid.</param>
/// <param name="MaxHealth">The creature's maximum health (M5 7 F7: the drain picker's measure of what a Drain Health Other takes; 0 when the profile does not say: the pick then assumes a full-sized target and says so). A profile value, read from the pinned world SQL.</param>
/// <param name="MagicDefence">The creature's Magic Defense skill (M5 7 F7: the landing chance of a resistable spell; 0 when the profile does not say).</param>
/// <param name="Opener">M5 7 F8 (D89): the spell line the fight opens this creature with before the bolts (the profile's optional <c>opener</c>, a profile line's name such as "Fire Vulnerability Other"), or null for bolts only. Only a target may name one.</param>
public sealed record GroundTarget(uint Wcid, string Name, int Level, GroundRole Role, int MaxHealth = 0, int MagicDefence = 0, string? Opener = null);

/// <summary>M5 7 F8: lookup of a target's opener by weenie class.</summary>
public static class GroundOpeners
{
    /// <summary>The opener the profile names for a creature of weenie class <paramref name="wcid"/>, or null (not listed, an avoided creature, or a target with no opener). Allocates nothing.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="wcid">The weenie class id.</param>
    public static string? OpenerOf(this HuntingGround ground, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(ground);
        for (var i = 0; i < ground.Targets.Count; i++)
        {
            if (ground.Targets[i].Wcid == wcid)
            {
                return ground.Targets[i].Role == GroundRole.Target ? ground.Targets[i].Opener : null;
            }
        }

        return null;
    }
}

/// <summary>A level band, inclusive.</summary>
/// <param name="Min">The lowest level.</param>
/// <param name="Max">The highest level.</param>
public sealed record LevelBand(int Min, int Max)
{
    /// <summary>True when <paramref name="level"/> is in the band.</summary>
    /// <param name="level">A level.</param>
    public bool Holds(int level) => level >= Min && level <= Max;
}

/// <summary>
/// The ground's portal: the object's guid, weenie class and place, the point in front of it where the journey ties to it, and the spells that tie it and recall to it (the journeys' den-travel station reads all of it from here, M5 item 2 k2, D46).
/// </summary>
/// <param name="Guid">The portal's guid.</param>
/// <param name="Cell">The portal's cell.</param>
/// <param name="TieSpell">The tie spell's id.</param>
/// <param name="RecallSpell">The recall spell's id.</param>
/// <param name="Wcid">The portal's weenie class id (the object the station finds and uses).</param>
/// <param name="Origin">The portal's origin in its landblock's local metres (where the client puts it before it has seen the object).</param>
/// <param name="FrontCell">The cell the tie point lies in.</param>
/// <param name="Front">The tie point: where the journey stands to cast Primary Portal Tie, three metres in front of the portal, in its landblock's local metres.</param>
public sealed record GroundPortal(uint Guid, uint Cell, uint TieSpell, uint RecallSpell, uint Wcid, Vector3 Origin, uint FrontCell, Vector3 Front);

/// <summary>The home town: its lifestone's cell and the point beside it the recall-home station walks to (session.md section 41).</summary>
/// <param name="Town">The town's name.</param>
/// <param name="LifestoneCell">The lifestone's cell.</param>
/// <param name="LifestonePoint">The landblock-local point off the lifestone.</param>
public sealed record GroundHome(string Town, uint LifestoneCell, Vector3 LifestonePoint);

/// <summary>
/// The ground's loot setting (M5 7 F28, D95; the profile's optional <c>loot</c> section): the fewest pyreals of Value per burden unit an item must carry for the fight's loot step to keep it. A den fact is a profile value, never a constant
/// (D38): the Black Spawn Den's 10 comes from ACE's own loot generator run offline over 20,000 tusker corpses (cache/m5/7/loot-sim.md): it keeps armour, clothing, jewellery, essences, scrolls, wands, gems, components and mana stones and leaves
/// weapons, shields and cloaks.
/// </summary>
/// <param name="MinValuePerBurden">The least Value per burden unit kept (0 to 100000).</param>
public sealed record GroundLoot(double MinValuePerBurden)
{
    /// <summary>M5 7 F29 (D96): the least payout (the Value times the best buyer's BuyPrice) an item must fetch to be kept; 0 is off. Coins are always kept.</summary>
    public long MinPayout { get; init; }

    /// <summary>M5 7 F29 (D96, the owner's point that weight is bad): the heaviest single item kept, whatever its value; 0 is off.</summary>
    public int MaxItemBurden { get; init; }

    /// <summary>M5 7 F29 (D96): a sell trip once every this many buff cycles (not when the pack fills); 0 is off.</summary>
    public int TripEveryBuffCycles { get; init; }

    /// <summary>M5 7 F29 (D96): an early sell trip when the main pack's free slots fall under this; 0 is off.</summary>
    public int MinFreeSlots { get; init; }

    /// <summary>M5 7 F29 (D96): the load line, in percent of the capacity: a trip is due when the burden passes it and the loot rule keeps under it; 0 keeps the supplies' <c>burden_trip_percent</c> and the rule's default.</summary>
    public int LoadLinePercent { get; init; }

    /// <summary>M5 7 F30 (the owner): the loot kept aside whatever the floors say and never sold (<see cref="GroundKeepAside"/>); null is none.</summary>
    public GroundKeepAside? KeepAside { get; init; }

    /// <summary>What a ground without the section keeps: one pyreal of Value per burden unit (today's floor, <c>MageLootThresholds.DefaultMinValuePerBurden</c>).</summary>
    public const double DefaultMinValuePerBurden = 1.0;
}

/// <summary>
/// The loot a ground's mage keeps aside and never sells (M5 7 F30, the owner; the loot section's optional <c>keep_aside</c> block): a caster whose War Magic damage bonus (ACE PropertyFloat ElementalDamageMod, 1.03 is +3%) reaches
/// <see cref="MinWarBonusPercent"/>, and any wearable, jewellery or caster that casts a cantrip named by <see cref="Tiers"/> and <see cref="Cantrips"/> (the spell is "&lt;tier&gt; &lt;family&gt;", ACE's own names: "Major Focus").
/// </summary>
/// <param name="MinWarBonusPercent">The least elemental damage bonus of a caster, in percent (above 0).</param>
/// <param name="Tiers">The cantrip tiers that count (Minor, Major, Epic or Legendary).</param>
/// <param name="Cantrips">The cantrip families that count (the spell name without its tier).</param>
public sealed record GroundKeepAside(double MinWarBonusPercent, IReadOnlyList<string> Tiers, IReadOnlyList<string> Cantrips)
{
    /// <summary>The cantrip tiers a profile may name.</summary>
    public static readonly IReadOnlyList<string> KnownTiers = ["Minor", "Major", "Epic", "Legendary"];

    /// <summary>The spell names the block keeps for, tier by tier (for example "Major Focus").</summary>
    public IEnumerable<string> SpellNames() => Tiers.SelectMany(t => Cantrips.Select(c => t + " " + c));

    /// <inheritdoc/>
    public bool Equals(GroundKeepAside? other) =>
        other is not null && MinWarBonusPercent.Equals(other.MinWarBonusPercent) && Tiers.SequenceEqual(other.Tiers) && Cantrips.SequenceEqual(other.Cantrips);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(MinWarBonusPercent, Tiers.Count, Cantrips.Count);
}

/// <summary>M5 7 F29 (D96): one vendor as the loot rule sees it: the item types it buys and the share of an item's Value it pays (its BuyPrice).</summary>
/// <param name="ItemTypes">The ItemType mask the vendor buys.</param>
/// <param name="Rate">The vendor's BuyPrice.</param>
public readonly record struct LootBuyer(uint ItemTypes, double Rate);

/// <summary>A vendor the ground's loop visits: where it stands, where the walk stops, what it buys and what it sells the kit.</summary>
/// <param name="Name">The vendor's name.</param>
/// <param name="Wcid">The vendor's weenie class id.</param>
/// <param name="Guid">The vendor's guid (the world's).</param>
/// <param name="Cell">The vendor's own cell.</param>
/// <param name="Point">The vendor's own landblock-local point.</param>
/// <param name="StopCell">The cell the walk to the vendor ends in (a room the walk can reach in front of the vendor, which may differ from the vendor's own).</param>
/// <param name="StopPoint">
/// The landblock-local point in <paramref name="StopCell"/> the walk ends at when the stop cell is a room in front of the vendor (the profile's optional <c>stop_point</c>), or null when the walk ends at <paramref name="Point"/>
/// (the vendor's own point, which is where a vendor stands in his own stop cell). The room graph reaches the room, not the vendor's cell behind it (<c>TODO(room-steps)</c>): the station walks here, then opens the vendor and lets ACE's
/// move-to walk the last metres.
/// </param>
/// <param name="BuysItemTypes">The ItemType mask the vendor buys (ACE's <c>MerchandiseItemTypes</c>, int property 74).</param>
/// <param name="Sells">What the vendor sells that the ground's kit needs, with the price.</param>
public sealed record GroundVendor(string Name, uint Wcid, uint Guid, uint Cell, Vector3 Point, uint StopCell, Vector3? StopPoint, uint BuysItemTypes, IReadOnlyList<VendorWare> Sells)
{
    /// <summary>What a vendor pays of an item's Value when the profile names no <c>buy_price</c> (1: the whole Value).</summary>
    public const double DefaultBuyPrice = 1.0;

    /// <summary>M5 7 F29 (D96): the vendor's BuyPrice, the share of an item's Value it pays (ACE's float property 37; the profile's optional <c>buy_price</c>).</summary>
    public double BuyPrice { get; init; } = DefaultBuyPrice;

    /// <summary>True when the vendor buys an item of type <paramref name="itemType"/> (a bit of the mask).</summary>
    /// <param name="itemType">The item's ItemType.</param>
    public bool Buys(uint itemType) => (BuysItemTypes & itemType) != 0;
}

/// <summary>M5 7 P7j (D143): one spell the character learns by play: the spell, the scroll that teaches it (ACE's Scroll, <c>ActOnUse</c>) and the vendor of the profile who sells the scroll.</summary>
/// <param name="SpellId">The spell id.</param>
/// <param name="Name">The spell's name (the SpellTable's).</param>
/// <param name="ScrollWcid">The scroll's weenie class id.</param>
/// <param name="VendorWcid">The weenie class id of the profile's vendor who sells the scroll.</param>
public sealed record LearnLine(uint SpellId, string Name, uint ScrollWcid, uint VendorWcid);

/// <summary>An item a vendor sells, by weenie class id, with the price in pyreals.</summary>
/// <param name="Wcid">The item's weenie class id.</param>
/// <param name="Name">The item's name.</param>
/// <param name="Price">The price in pyreals.</param>
public sealed record VendorWare(uint Wcid, string Name, int Price);

/// <summary>One row of the spell plan (D41): the character levels it holds, the spell family and the highest spell level cast.</summary>
/// <param name="MinLevel">The lowest character level, inclusive.</param>
/// <param name="MaxLevel">The highest character level, inclusive.</param>
/// <param name="Family">The spell family (the spell profile line's name).</param>
/// <param name="SpellLevel">The highest spell level (1 to 8).</param>
public sealed record SpellPlanRow(int MinLevel, int MaxLevel, string Family, int SpellLevel);

/// <summary>A component the plan's spells burn: its weenie class id, name, the spell level it serves and how many one cast's formula holds.</summary>
/// <param name="Wcid">The component's weenie class id.</param>
/// <param name="Name">The component's name.</param>
/// <param name="SpellLevel">The spell level whose formula holds it.</param>
/// <param name="PerCast">How many one cast's formula holds.</param>
public sealed record GroundComponent(uint Wcid, string Name, int SpellLevel, int PerCast);

/// <summary>
/// The ground's logistics as data (M5 item 3 m0, D48, D49): what the loop keeps in stock, wears and wields from a vendor, when the burden calls a trip, and the purse that pays for it. The stations, the counters
/// and the build read only this and the vendors' rows: no vendor, price, threshold or target count lives in code. An outdoor ground, whose hunters shop at the journey's own stations, has the empty section.
/// </summary>
/// <param name="Stock">The consumable lines: each a ware to hold up to a count, to resupply under another and to stop under a third.</param>
/// <param name="Kit">The worn and wielded vendor items (a robe, a wand), bought once.</param>
/// <param name="BurdenTripPercent">A trip is due when the load passes this share (1 to 100) of the capacity.</param>
/// <param name="Purse">What the build grants for the buying: coin for one trip and trade notes for the rest.</param>
public sealed record GroundSupplies(IReadOnlyList<StockLine> Stock, IReadOnlyList<KitLine> Kit, int BurdenTripPercent, GroundPurse Purse)
{
    /// <summary>The section of a ground that buys nothing (the outdoor grounds): no lines, no kit, a purse of 0.</summary>
    public static GroundSupplies Empty { get; } = new([], [], 90, new GroundPurse(0, []));

    /// <summary>The stock line of the ware <paramref name="wcid"/>, or null. Allocates nothing.</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public StockLine? StockOf(uint wcid)
    {
        for (var i = 0; i < Stock.Count; i++)
        {
            if (Stock[i].Wcid == wcid)
            {
                return Stock[i];
            }
        }

        return null;
    }

    /// <summary>The kit line of the item <paramref name="wcid"/>, or null. Allocates nothing.</summary>
    /// <param name="wcid">The item's weenie class id.</param>
    public KitLine? KitOf(uint wcid)
    {
        for (var i = 0; i < Kit.Count; i++)
        {
            if (Kit[i].Wcid == wcid)
            {
                return Kit[i];
            }
        }

        return null;
    }
}

/// <summary>
/// One consumable the loop keeps in stock: the ware by weenie class id and three counts with <c>Keep &gt;= TripUnder &gt;= StopUnder &gt;= 0</c> (the loader checks): a trip buys up to <see cref="Keep"/>; a count
/// under <see cref="TripUnder"/> makes a trip due; a count under <see cref="StopUnder"/> stops the loop by name (for a carried line, which no trip restocks, the two are equal). The vendor and the price are the
/// vendors' <see cref="GroundVendor.Sells"/> (<see cref="HuntingGround.VendorSelling"/>); a line no vendor lists is carried (D8).
/// </summary>
/// <param name="Wcid">The ware's weenie class id.</param>
/// <param name="Name">The ware's name (for the logs; equal to the vendor's row when one lists it).</param>
/// <param name="Keep">The count a trip buys up to.</param>
/// <param name="TripUnder">The count under which a trip is due.</param>
/// <param name="StopUnder">The count under which the loop must stop.</param>
public sealed record StockLine(uint Wcid, string Name, int Keep, int TripUnder, int StopUnder);

/// <summary>Where a kit item goes.</summary>
public enum KitSlot
{
    /// <summary>Worn (a robe).</summary>
    Worn,

    /// <summary>Wielded (a wand).</summary>
    Wielded,

    /// <summary>M5 7 G3: carried, never worn or wielded: a side pack (weenie 136), <see cref="KitLine.Count"/> of them. A container takes one of the player's container slots, not one of the main pack's item slots.</summary>
    Pack,
}

/// <summary>A vendor item the build puts on the mage: the weenie class id, its name and whether it is worn or wielded. The vendor and the price are the vendors' <c>sells</c> or, for an item no vendor lists, carried.</summary>
/// <param name="Wcid">The item's weenie class id.</param>
/// <param name="Name">The item's name (for the logs; equal to the vendor's row when one lists it).</param>
/// <param name="Slot">Worn, wielded or (M5 7 G3) carried as a side pack.</param>
/// <param name="Count">How many the build grants: 1 for a worn or wielded line, up to <c>GearKit.ContainerCapacity</c> for a pack line.</param>
public sealed record KitLine(uint Wcid, string Name, KitSlot Slot, int Count = 1);

/// <summary>
/// The purse the build grants (D49): <see cref="Coin"/> pyreals for one trip and trade notes for the rest. ACE destroys the coin a death drops, while the notes are bonded and ACE never drops a bonded item at a death
/// (<c>Player_Death.cs:501-502</c> @95ef426d; D49 as corrected by m1), so the notes stay in the pack through a death; a vendor pays a note at its face value.
/// </summary>
/// <param name="Coin">The pyreals in coin.</param>
/// <param name="Notes">The trade notes.</param>
/// <param name="MinNote">M5 7 D136 follow-up: the smallest note face the trips buy with the coin above the floor (the profile's <c>min_note</c>, default <see cref="DefaultMinNote"/>): a note under one coin stack (25,000) saves no pack slot and costs 15%.</param>
public sealed record GroundPurse(int Coin, IReadOnlyList<PurseNote> Notes, int MinNote = GroundPurse.DefaultMinNote)
{
    /// <summary>One coin stack, the pyreal's MaxStackSize (ACE-World-Database v0.9.296, wcid 273, PropertyInt 11).</summary>
    public const int DefaultMinNote = 25_000;

    /// <summary>The face value of every note together, in pyreals.</summary>
    public long NotesValue => Notes.Sum(n => (long)n.Value * n.Count);

    /// <summary>The coin and the notes together, in pyreals.</summary>
    public long Total => Coin + NotesValue;
}

/// <summary>A trade note of the purse: the weenie class id, its name, the pyreals it is worth (its face value, which a vendor pays) and how many the purse holds.</summary>
/// <param name="Wcid">The note's weenie class id.</param>
/// <param name="Name">The note's name.</param>
/// <param name="Value">The pyreals one note is worth.</param>
/// <param name="Count">How many notes of this weenie the purse holds.</param>
public sealed record PurseNote(uint Wcid, string Name, int Value, int Count);

/// <summary>A stand-and-hold spot: a cell and a landblock-local point.</summary>
/// <param name="Cell">The full cell id.</param>
/// <param name="Point">The point.</param>
public sealed record HoldSpot(uint Cell, Vector3 Point);

/// <summary>The respawn model: the ground's generators, each with its own delay and tick, and their slots.</summary>
/// <param name="Generators">The generators (a ground may have several, with different delays and ticks).</param>
/// <param name="Slots">The generator slots (a profile may list none yet).</param>
public sealed record RespawnModel(IReadOnlyList<GeneratorModel> Generators, IReadOnlyList<RespawnSlot> Slots)
{
    /// <summary>The generator of guid <paramref name="guid"/>, or null. Allocates nothing.</summary>
    /// <param name="guid">The generator's guid.</param>
    public GeneratorModel? GeneratorOf(uint guid)
    {
        for (var i = 0; i < Generators.Count; i++)
        {
            if (Generators[i].Guid == guid)
            {
                return Generators[i];
            }
        }

        return null;
    }
}

/// <summary>One generator: its guid and cell, a slot's delay from a death to its next creature and the generator's tick (<c>RegenerationInterval</c>).</summary>
/// <param name="Guid">The generator's guid.</param>
/// <param name="Cell">The generator's cell.</param>
/// <param name="DelaySeconds">A slot's delay after a death, in seconds.</param>
/// <param name="TickSeconds">The generator's tick, in seconds: a slot due between two ticks spawns at the next.</param>
public sealed record GeneratorModel(uint Guid, uint Cell, double DelaySeconds, double TickSeconds);

/// <summary>One generator slot: the generator's guid (one of <see cref="RespawnModel.Generators"/>), the cell its creature spawns in and the creature's weenie class id.</summary>
/// <param name="Generator">The generator's guid.</param>
/// <param name="Cell">The slot's cell.</param>
/// <param name="Wcid">The creature's weenie class id.</param>
public sealed record RespawnSlot(uint Generator, uint Cell, uint Wcid);

/// <summary>What a ground's room graph and walk need: the doorway lean the room graph accepts and the rules that end a leg.</summary>
/// <param name="DoorwayLean">The largest vertical part of a doorway's normal the ground's room graph accepts (0 to 1).</param>
/// <param name="StopWithinMetres">The arrival radius of a leg's last stop, in metres.</param>
/// <param name="StopDwellSeconds">How long the walker stands at the end of a leg, in seconds.</param>
/// <param name="LegSeconds">The longest a leg may take, in seconds.</param>
public sealed record WalkModel(float DoorwayLean, float StopWithinMetres, double StopDwellSeconds, double LegSeconds);

/// <summary>An outdoor ground's cells and creatures: the cells with their map coordinates, terrain and spot point, the creature table, the acquire radius, the cell cap and the expected yield.</summary>
/// <param name="Cells">The cells in the hunt's order.</param>
/// <param name="Creatures">The creatures the ground's generators spawn, with their levels.</param>
/// <param name="AcquireRadiusMetres">How far from the spot a creature is sought, in metres.</param>
/// <param name="CellCap">The most cells one hunt visits.</param>
/// <param name="ExpectedYieldPerSpot">The expected creatures in the band per spot.</param>
public sealed record OutdoorGround(IReadOnlyList<OutdoorCell> Cells, IReadOnlyList<GroundCreature> Creatures, float AcquireRadiusMetres, int CellCap, double ExpectedYieldPerSpot);

/// <summary>An outdoor cell of a ground.</summary>
/// <param name="Cell">The outdoor cell id.</param>
/// <param name="North">Map north.</param>
/// <param name="East">Map east.</param>
/// <param name="Terrain">What the dats say of its ground.</param>
/// <param name="Spot">The landblock-local point the walk stops at.</param>
public sealed record OutdoorCell(uint Cell, double North, double East, GroundTerrain Terrain, Vector3 Spot);

/// <summary>One creature of an outdoor ground's table.</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name.</param>
/// <param name="Level">The level.</param>
public sealed record GroundCreature(uint Wcid, string Name, int Level)
{
    /// <summary>
    /// The spawn mass (item 2 g3, D42): the chance, 0 to 1, that one roll of a spot's generator spawns this creature (the generator's cumulative-probability rows, nested generators and camps multiplied; for a
    /// ground whose spots roll different generators, the mean over its cells). The profile's <c>spawn_mass</c> is required and the loader reads it, so a profile that lacks it fails at load; the default
    /// here is only for code that builds a creature by hand. A hunt's expected usable creatures a spot is the sum over the creatures it will fight (<c>HuntGroundRow.ExpectedYield</c>).
    /// </summary>
    public double SpawnMass { get; init; }
}

/// <summary>
/// An immutable set of full cell ids over a few landblocks: one 65,536-bit mask per landblock, so <see cref="Contains"/> is a scan of the landblocks and one bit test, allocating nothing.
/// </summary>
public sealed class CellSet
{
    private readonly ushort[] _landblocks;
    private readonly ulong[][] _bits;

    private CellSet(ushort[] landblocks, ulong[][] bits, uint[] cells)
    {
        _landblocks = landblocks;
        _bits = bits;
        Cells = Array.AsReadOnly(cells);
    }

    /// <summary>An empty set.</summary>
    public static CellSet Empty { get; } = new([], [], []);

    /// <summary>The cells, ascending.</summary>
    public IReadOnlyList<uint> Cells { get; }

    /// <summary>How many cells.</summary>
    public int Count => Cells.Count;

    /// <summary>True when the full cell id is in the set.</summary>
    /// <param name="cell">The full cell id.</param>
    public bool Contains(uint cell)
    {
        var landblock = (ushort)(cell >> 16);
        for (var i = 0; i < _landblocks.Length; i++)
        {
            if (_landblocks[i] == landblock)
            {
                var low = (int)(cell & 0xFFFF);
                return (_bits[i][low >> 6] & (1UL << (low & 63))) != 0;
            }
        }

        return false;
    }

    /// <summary>A set of the given full cell ids (duplicates fold).</summary>
    /// <param name="cells">The cells.</param>
    public static CellSet Of(IEnumerable<uint> cells)
    {
        var sorted = cells.Distinct().Order().ToArray();
        var landblocks = sorted.Select(c => (ushort)(c >> 16)).Distinct().ToArray();
        var bits = landblocks.Select(_ => new ulong[1024]).ToArray();
        foreach (var c in sorted)
        {
            var i = Array.IndexOf(landblocks, (ushort)(c >> 16));
            var low = (int)(c & 0xFFFF);
            bits[i][low >> 6] |= 1UL << (low & 63);
        }

        return new CellSet(landblocks, bits, sorted);
    }

    /// <summary>The union of <paramref name="sets"/> without the cells of <paramref name="without"/>.</summary>
    /// <param name="sets">The sets.</param>
    /// <param name="without">The cells left out.</param>
    internal static CellSet Union(IEnumerable<CellSet> sets, CellSet without) => Of(sets.SelectMany(s => s.Cells).Where(c => !without.Contains(c)));
}

/// <summary>M5 7 P7i (D144): a floor region of a ground the walk avoids: a box in the ground's landblock, landblock-local metres (its height the storey's).</summary>
/// <param name="Name">The region's name, for the log.</param>
/// <param name="Landblock">The landblock (0xXXYY).</param>
/// <param name="Min">The box's low corner.</param>
/// <param name="Max">The box's high corner.</param>
public sealed record GroundAvoidRegion(string Name, ushort Landblock, System.Numerics.Vector3 Min, System.Numerics.Vector3 Max);
