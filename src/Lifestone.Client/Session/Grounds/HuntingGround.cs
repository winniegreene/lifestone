using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
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
/// <para>M6 item 3 a1 (<c>m6-ground-fields</c>) built the entry gate (<see cref="Entry"/>, <see cref="AdmitsLevel"/>), the exit's kind (<see cref="Exit"/>), the kill-first role (<see cref="GroundTarget.KillFirst"/>,
/// <see cref="IsKillFirst"/>) and the target stats (<see cref="GroundTarget"/>'s health, Magic Defense, attributes, damage types and swing). TODO(m6-ground-fields): the rest M6's grounds may need and M5's do not
/// (m5-plan.md D43, section 9) is not built: variants, damage multipliers, spawn models beyond the generator slot, hazards, interactables, vendor roles and the return route. A profile that needs one adds it here when it does.</para>
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

    /// <summary>
    /// M6 item 3 a1: true when the creature of weenie class <paramref name="wcid"/> is a kill-first target (the profile's role <c>kill-first</c>, which loads as <see cref="GroundRole.Target"/> with
    /// <see cref="GroundTarget.KillFirst"/>); false for a plain target, an avoided creature or one the profile does not list. Allocates nothing.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public bool IsKillFirst(uint wcid)
    {
        for (var i = 0; i < Targets.Count; i++)
        {
            if (Targets[i].Wcid == wcid)
            {
                return Targets[i].KillFirst && Targets[i].Role == GroundRole.Target; // only a target is kill-first, even in a ground built by hand
            }
        }

        return false;
    }

    /// <summary>
    /// M6 14 holding safety (the owner, 2026-11-13: every number about how she fights is computed from her live state and the creatures' facts): the place's hardest hitter, read from the facts its targets carry (<see cref="GroundTarget.DVal"/>,
    /// <see cref="GroundTarget.DVar"/> and <see cref="GroundAttributes.Strength"/>): the creature whose largest uncritical hit (<c>Vitals.FightLines.MaxHit</c>) is the largest, or null when no creature of the profile carries its attack's facts
    /// (the derived fight line then cannot be computed here, and the reader says so). <paramref name="damageTypes"/> is that creature's <see cref="GroundTarget.DamageTypes"/> (0: the profile does not say).
    /// </summary>
    /// <param name="damageTypes">The hitter's damage type bits.</param>
    public LadderHitter? HardestHitter(out uint damageTypes)
    {
        LadderHitter? best = null;
        var bestHit = 0.0;
        damageTypes = 0;
        for (var i = 0; i < Targets.Count; i++)
        {
            var t = Targets[i];
            if (t.DVal is not { } dVal || t.DVar is not { } dVar || t.Attributes is not { } a)
            {
                continue;
            }

            var hitter = new LadderHitter(t.Name, t.Wcid, dVal, dVar, a.Strength);
            var hit = Vitals.FightLines.MaxHit(hitter);
            if (best is null || hit > bestHit)
            {
                (best, bestHit, damageTypes) = (hitter, hit, t.DamageTypes);
            }
        }

        return best;
    }

    /// <summary>
    /// M6 item 3 a1: the ground's entry portal with its level gate and whether it can be tied (the profile's optional <c>entry</c> section), or null when the profile has none (then every level is admitted).
    /// </summary>
    public GroundEntry? Entry { get; init; }

    /// <summary>M6 item 3 a1: how the ground is left (the profile's optional <c>exit</c> section): by an exit portal, by recall or on foot; null when the profile does not say.</summary>
    public GroundExitSection? Exit { get; init; }

    /// <summary>
    /// M6 14 pits (<c>TODO(m6-abode-pits)</c>): the pits of the ground a running jump crosses (the profile's optional <c>jumps</c> section, each crossing with its source), the room graph's jump edges
    /// (<see cref="GroundRooms.GraphOf"/>); empty for a ground without one.
    /// </summary>
    public IReadOnlyList<Lifestone.Client.Movement.Routes.PitJump> Jumps { get; init; } = [];

    /// <summary>
    /// M6 redoubt: the ground's galleries (the profile's optional <c>galleries</c> section), or null for a ground without one: floorless cells a body stands in, on floors that other cells hold. The gallery cells are storey cells
    /// (<see cref="IsWalkable"/>); the cells that hold their floors join the room graph only (<see cref="GroundRooms.FloorOf"/>).
    /// </summary>
    public GroundGalleries? Galleries { get; init; }

    /// <summary>M6 redoubt: true when the full cell id holds a gallery's floor (<see cref="GroundGalleries.Floors"/>): a cell of the room graph that is no storey's.</summary>
    /// <param name="cell">The full cell id.</param>
    public bool IsGalleryFloor(uint cell) => Galleries is { } galleries && galleries.Floors.Contains(cell);

    /// <summary>
    /// M6 14 holding plates (the owner, 2026-11-14): the ground's pressure plates (the profile's optional <c>plates</c> section: the weenie, its shape and spells, and each plate's place), walls of every walk over the ground's rooms
    /// (<see cref="GroundRooms.GraphOf"/>, <see cref="Lifestone.Client.Movement.Wayfinding.PlateKeepOff"/>) and jumped where one blocks the way; null for a ground without plates.
    /// </summary>
    public GroundPlates? Plates { get; init; }

    /// <summary>
    /// M6 14 holding plates: <see cref="Plates"/> as the room graph's walls (empty without plates), each plate's shape read from its Setup in <paramref name="dats"/> (<see cref="Lifestone.Client.Movement.Wayfinding.PlateKeepOff.ShapeOf(DatSet, uint)"/>).
    /// </summary>
    /// <param name="dats">The dats; null only for a ground without plates.</param>
    /// <exception cref="InvalidOperationException">The ground has plates and no dats were given to read their shape from.</exception>
    public IReadOnlyList<Lifestone.Client.Movement.Wayfinding.KeptPlate> KeptPlatesOf(DatSet? dats)
    {
        if (Plates is not { } plates)
        {
            return [];
        }

        if (dats is null)
        {
            throw new InvalidOperationException($"ground '{Key}' has {plates.Points.Count} plates of Setup 0x{plates.SetupId:X8} and no dats to read their shape from");
        }

        var (shape, top) = Lifestone.Client.Movement.Wayfinding.PlateKeepOff.ShapeOf(dats, plates.SetupId);
        return [.. plates.Points.Select(p => new Lifestone.Client.Movement.Wayfinding.KeptPlate(p.Name, p.Cell, p.Point, shape, top))];
    }

    /// <summary>True when the entry portal admits a character of <paramref name="level"/> (its MinLevel and MaxLevel, each when present); true when the profile has no <c>entry</c>. Pure; allocates nothing.</summary>
    /// <param name="level">The character's level.</param>
    public bool AdmitsLevel(int level) => Entry is null || Entry.Admits(level);

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
    /// M6 14 holding safety: a number sized for one character; where the targets carry their attack's facts (<see cref="HardestHitter"/>) the line derived on her live state wins (<c>Vitals.LiveFightLine</c>) and this value is logged as ignored.
    /// </summary>
    public int? FightRetreatHealthPercent { get; init; }

    /// <summary>
    /// M6 7 AB: the rest before the next pull (the profile's optional <c>fight.pull_mana_percent</c>, <c>pull_health_percent</c>, <c>pull_rest_until_percent</c>, <c>pull_rest_posture</c>, <c>pull_aggro_metres</c>; <see cref="Vitals.GroundPullRest"/>).
    /// Null (the fields are absent): no rest rule; the den at level 80 is fed by the dance (D88) and keeps its numbers.
    /// </summary>
    public Vitals.GroundPullRest? PullRest { get; init; }

    /// <summary>M6 7 AR: what the plain hunt leaves alone (the profile's optional <c>hunt</c> section, <see cref="GroundHunt"/>). Null: everything walkable is hunted.</summary>
    public GroundHunt? Hunt { get; init; }

    /// <summary>M6 7 AY: where a corpse on this ground is safe to reach for its coin (the profile's optional <c>corpse_fetch</c>, <see cref="GroundCorpseFetch"/>). Null: nowhere.</summary>
    public GroundCorpseFetch? CorpseFetch { get; init; }

    /// <summary>M6 14 entry recharge: the hostiles on the way to the ground's entry (the profile's optional <c>approach</c>, <see cref="GroundApproach"/>), facts with their sources. Null: none recorded.</summary>
    public GroundApproach? Approach { get; init; }

    /// <summary>M6 14 entry recharge: the approach when <paramref name="cell"/> is an outdoor cell of one of its landblocks (a drop among its hostiles), else null. Allocates nothing.</summary>
    /// <param name="cell">The cell (a portal's drop).</param>
    public GroundApproach? ApproachAt(uint cell)
    {
        if (Approach is not { } approach || (cell & 0xFFFF) >= 0x100)
        {
            return null;
        }

        var landblock = (ushort)(cell >> 16);
        for (var i = 0; i < approach.Landblocks.Count; i++)
        {
            if (approach.Landblocks[i] == landblock)
            {
                return approach;
            }
        }

        return null;
    }

    /// <summary>
    /// M6 14 T: the stock bought in another town on the way out (the profile's optional <c>town_stock</c>, <see cref="TownStockLine"/>): no line of <see cref="GroundSupplies.Stock"/>, sold by no home vendor, never a stop and never a trip of
    /// its own (<c>Mage.TownStock</c>). Empty when the section is absent.
    /// </summary>
    public IReadOnlyList<TownStockLine> TownStock { get; init; } = [];

    /// <summary>
    /// M6 item 14 (m6-goldscarab): the town stock line of the ware <paramref name="wcid"/>, or null. A spell component bought this way (the Gold and the Pyreal Scarab at Uziz) is no stock line and has no stop line: the pack may hold none, and
    /// every reader that asks "does the ground carry this component" (<c>FightScenario.WarKitCarries</c>, <c>GearKit.CastBurns</c>, the profile loader's plan check) asks this beside <see cref="GroundSupplies.StockOf"/>. Allocates nothing.
    /// </summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public TownStockLine? TownStockOf(uint wcid)
    {
        var lines = TownStock;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Wcid == wcid)
            {
                return lines[i];
            }
        }

        return null;
    }

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

    /// <summary>M6 8 B1: the spells the ground wants the character to know (the profile's <c>wants_known</c>; empty on every other ground): the plan's war line, the lines for a target the plan's element does not hurt, the fight's protections; a line the book lacks is a loud line at the fight's start (<see cref="GroundWants"/>).</summary>
    public IReadOnlyList<GroundWant> WantsKnown { get; init; } = [];

    /// <summary>M5 7 F22: the fewest tuskers within the Ring of Fire's reach before the war picker weighs it (the profile's optional <c>fight.ring_min_targets</c>, 1 to 9); null keeps the default of three (<see cref="Lifestone.Client.Session.Mage.WarPickSettings.DefaultRingMinTargets"/>).</summary>
    public int? FightRingMinTargets { get; init; }

    /// <summary>
    /// The home vendor whose <see cref="GroundVendor.Sells"/> lists the ware <paramref name="wcid"/>, or null when no vendor of the profile does (a carried line: the mage holds it and nothing here restocks it, D8).
    /// The loader allows a supply line's ware under one vendor only, so there is one answer. Allocates nothing.
    /// </summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public GroundVendor? VendorSelling(uint wcid) => Selling(Vendors, wcid) ?? Selling(Scriveners, wcid); // M6 1 c: the home town's scriveners after the profile's vendors

    /// <summary>
    /// M6 item 1 c (D40): the scrivener catalog's scriveners of the home town (<see cref="ScrivenerCatalog.VendorsIn"/>), merged at load by <see cref="GroundCatalog"/> after the profile's own vendors (additive: a scrivener the profile
    /// lists is the profile's): <see cref="VendorSelling"/>, <see cref="PriceOf"/> and <see cref="VendorOf"/> see them, so the resupply's buy path buys a scroll there. Each buys nothing (mask 0: no sale goes to them) and is not in
    /// <see cref="Vendors"/>, so the loot buyers and the profile's checks are unchanged. Empty for a town with no catalog scrivener.
    /// </summary>
    public IReadOnlyList<GroundVendor> Scriveners { get; init; } = [];

    private static GroundVendor? Selling(IReadOnlyList<GroundVendor> vendors, uint wcid)
    {
        for (var i = 0; i < vendors.Count; i++)
        {
            var sells = vendors[i].Sells;
            for (var j = 0; j < sells.Count; j++)
            {
                if (sells[j].Wcid == wcid)
                {
                    return vendors[i];
                }
            }
        }

        return null;
    }

    /// <summary>The price in pyreals of the ware <paramref name="wcid"/> at its home vendor (from <see cref="GroundVendor.Sells"/>), or null when no vendor of the profile lists it (carried). Allocates nothing.</summary>
    /// <param name="wcid">The ware's weenie class id.</param>
    public int? PriceOf(uint wcid)
    {
        if (VendorSelling(wcid) is not { } vendor)
        {
            return null;
        }

        var sells = vendor.Sells;
        for (var j = 0; j < sells.Count; j++)
        {
            if (sells[j].Wcid == wcid)
            {
                return sells[j].Price;
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

        for (var i = 0; i < Scriveners.Count; i++)
        {
            if (Scriveners[i].Wcid == wcid)
            {
                return Scriveners[i];
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
/// <param name="MaxHealth">
/// The creature's maximum health (M5 7 F7: the drain picker's measure of what a Drain Health Other takes; 0 when the profile does not say: the pick then assumes a full-sized target and says so). A profile value, read from the pinned
/// world SQL: ACE's current value, <c>CreatureVital.MaxValue</c> (CreatureVital.cs:104-131 @95ef426d): the vital's start (weenie_properties_attribute_2nd type 1 init_Level) plus its ranks (level_From_C_P) plus the attribute part,
/// Endurance / 2 rounded half up (AttributeFormula.cs:55-71), with no enchantment on a fresh spawn; ACE spawns a creature at this maximum whatever the row's current_Level says (Creature.cs:132-135), so the den's Tusker Guard 260 (its current_Level) is not ACE's 400.
/// </param>
/// <param name="MagicDefence">
/// The creature's Magic Defense skill (M5 7 F7: the landing chance of a resistable spell; 0 when the profile does not say). ACE's current value, <c>CreatureSkill.Current</c> (CreatureSkill.cs:154-163 @95ef426d): the skill's
/// init_Level plus its ranks (weenie_properties_skill type 15 level_From_P_P) plus, for a trained or specialised skill, (Focus + Self) / 7 rounded half up (the SkillTable's formula).
/// </param>
/// <param name="Opener">M5 7 F8 (D89): the spell line the fight opens this creature with before the bolts (the profile's optional <c>opener</c>, a profile line's name such as "Fire Vulnerability Other"), or null for bolts only. Only a target may name one.</param>
public sealed record GroundTarget(uint Wcid, string Name, int Level, GroundRole Role, int MaxHealth = 0, int MagicDefence = 0, string? Opener = null)
{
    /// <summary>
    /// M6 item 3 a1: a kill-first target (the profile's role <c>kill-first</c>): <see cref="Role"/> is <see cref="GroundRole.Target"/> and the pick orders it first (the readers are the fight's). Only a target may be kill-first.
    /// </summary>
    public bool KillFirst { get; init; }

    /// <summary>M6 item 3 a1: the creature's six attributes as its weenie's rows give them (the profile's optional <c>attributes</c>), or null when the profile does not say.</summary>
    public GroundAttributes? Attributes { get; init; }

    /// <summary>
    /// M6 item 3 a1: the DamageType bits of the creature's body-part attacks (the profile's optional <c>damage_types</c>, a hex mask: the OR of weenie_properties_body_part's d_Type over the parts with a d_Val, the parts ACE's
    /// <c>GetAttackPart</c> picks from, Monster_Melee.cs:590-615 @95ef426d); 0 when the profile does not say. A wielded weapon's damage type is not in it.
    /// </summary>
    public uint DamageTypes { get; init; }

    /// <summary>
    /// M6 item 3 a1: the seconds between a melee creature's swings (the profile's optional <c>swing_s</c>), or null when the profile does not say. ACE times the next swing as the swing animation's length at the creature's
    /// attack speed plus a random delay up to its PowerupTime (Monster_Melee.cs:151-156 @95ef426d), so a value here is a measure or a mean, named in its source; the draft writes none (<c>TODO(f11-swing-seconds)</c>).
    /// </summary>
    public double? SwingSeconds { get; init; }

    /// <summary>
    /// M6 14 holding safety: the largest <c>d_Val</c> of the creature's attacking body parts (the profile's optional <c>d_val</c>, weenie_properties_body_part; ACE's <c>BaseDamage.MaxDamage</c>, Monster_Melee.cs:385-404 @95ef426d), or null when the
    /// profile does not say. With <see cref="DVar"/> and <see cref="Attributes"/> it is the fact the derived fight line reads (<see cref="HuntingGround.HardestHitter"/>); the row's <c>attack_source</c> names the SELECT.
    /// </summary>
    public int? DVal { get; init; }

    /// <summary>M6 14 holding safety: that part's <c>d_Var</c> (the profile's optional <c>d_var</c>; a hit is uniform between <c>d_Val x (1 - d_Var)</c> and <c>d_Val</c>, Entity/BaseDamage.cs), or null when the profile does not say.</summary>
    public double? DVar { get; init; }

    /// <summary>
    /// M6 14 approach cost: the experience a kill gives (the profile's optional <c>xp</c>: weenie_properties_int 146, XpOverride, which ACE awards for the kill), or null when the profile does not say. A rung's planned experience rate is its
    /// kills-an-hour floor times its ground's targets' mean (<see cref="Vitals.ApproachCost.RungRate"/>).
    /// </summary>
    public long? Xp { get; init; }

    /// <summary>
    /// M6 14 drain resistance (live 2026-11-14 19:42 UTC, the Tusker Armory: three Drain Health Others on three Armored Tuskers each gave 0 and the climber died): the share of a Drain Health Other's (and a Harm Other's) take the
    /// creature lets through, the profile's optional <c>resist_health_drain</c>: its <c>ResistHealthDrain</c> (weenie_properties_float type 125; ACE's <c>GetResistanceMod(HealthDrain)</c>, Creature_Properties.cs:207-208 @95ef426d,
    /// times a natural resistance of 1 and no life-resist rating on a creature). 1 when the profile does not say (ACE's own default); 0 takes nothing. Read through <see cref="Vitals.DrainResist"/>.
    /// </summary>
    public double ResistHealthDrain { get; init; } = 1.0;
}

/// <summary>M6 item 3 a1: a creature's six attributes, each its weenie's weenie_properties_attribute row's current value (init_Level plus level_From_C_P, ACE's <c>CreatureAttribute.Base</c>, CreatureAttribute.cs:93-97 @95ef426d).</summary>
/// <param name="Strength">Strength (PropertyAttribute 1).</param>
/// <param name="Endurance">Endurance (2).</param>
/// <param name="Coordination">Coordination (4).</param>
/// <param name="Quickness">Quickness (3).</param>
/// <param name="Focus">Focus (5).</param>
/// <param name="Self">Self (6).</param>
public sealed record GroundAttributes(int Strength, int Endurance, int Coordination, int Quickness, int Focus, int Self);

/// <summary>
/// M6 item 3 a1: the ground's entry portal (the profile's optional <c>entry</c> section): where it stands, the point in front of it, its level gate and whether it can be tied. The level gate is the portal's MinLevel and
/// MaxLevel (PropertyInt 86 and 87), each null when the weenie has none; <see cref="Tieable"/> is false when its PortalBitmask (PropertyInt 111) carries NoRecall (0x20, ACE.Entity/Enum/PortalBitmask.cs:15 @95ef426d), which ACE's
/// <c>Portal.NoTie</c> reads (ACE.Server/WorldObjects/Portal_Properties.cs:14-16: NoTie is NoRecall; a weenie with no bitmask is Unrestricted, line 10).
/// </summary>
/// <param name="Wcid">The portal's weenie class id.</param>
/// <param name="Guid">The portal instance's guid.</param>
/// <param name="Cell">The cell it stands in (an outdoor row's by the x/24*8 + y/24 + 1 rule).</param>
/// <param name="Origin">Its landblock-local origin.</param>
/// <param name="FrontCell">The cell of the point in front of it.</param>
/// <param name="Front">The point in front of it, 3 m along its facing (the same rule as <see cref="GroundPortal.Front"/>).</param>
/// <param name="MinLevel">The lowest level it admits, or null.</param>
/// <param name="MaxLevel">The highest level it admits, or null.</param>
/// <param name="Tieable">False when the portal cannot be tied (NoRecall).</param>
public sealed record GroundEntry(uint Wcid, uint Guid, uint Cell, Vector3 Origin, uint FrontCell, Vector3 Front, int? MinLevel, int? MaxLevel, bool Tieable)
{
    /// <summary>True when <paramref name="level"/> is within the gate (each bound when present). Allocates nothing.</summary>
    /// <param name="level">The character's level.</param>
    public bool Admits(int level) => (MinLevel is not { } min || level >= min) && (MaxLevel is not { } max || level <= max);
}

/// <summary>M6 item 3 a1: how a ground is left.</summary>
public enum GroundExitKind
{
    /// <summary>By an exit portal object inside the dungeon (<see cref="GroundExitSection.Wcid"/> and its place).</summary>
    Portal,

    /// <summary>No exit portal: the character leaves by a recall spell.</summary>
    Recall,

    /// <summary>On foot: an outdoor ground.</summary>
    Walk,
}

/// <summary>
/// M6 item 3 a1: the ground's exit (the profile's optional <c>exit</c> section). For <see cref="GroundExitKind.Portal"/> the exit portal's weenie class, cell, origin and destination cell are set; for the other kinds they are 0 and null.
/// </summary>
/// <param name="Kind">Portal, recall or walk.</param>
/// <param name="Wcid">The exit portal's weenie class id (0 unless a portal).</param>
/// <param name="Cell">The exit portal's cell, one of the ground's (0 unless a portal).</param>
/// <param name="Origin">The exit portal's landblock-local origin (null unless a portal).</param>
/// <param name="Destination">The cell the exit portal leads to (0 unless a portal).</param>
/// <param name="FromArrivalMetres">A hint: the straight-line distance from the arrival to the exit portal, in metres (the profile's optional <c>from_arrival_m</c>; a walk's length over the room graph is travel's, item 4), or null.</param>
public sealed record GroundExitSection(GroundExitKind Kind, uint Wcid, uint Cell, Vector3? Origin, uint Destination, double? FromArrivalMetres);

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

    /// <summary>M6 7 AT: the Value of the loot carried above which a sale trip is called while stock or scrolls are wanted and the coin cannot pay them (the profile's <c>loot.sale_trip_value</c>; <see cref="DefaultSaleTripValue"/> when absent).</summary>
    public int SaleTripValue { get; init; } = DefaultSaleTripValue;

    /// <summary>M6 7 AT: the sale trip's default line, in Value (the live stretch carried a pack of unsold loot for 70 minutes).</summary>
    public const int DefaultSaleTripValue = 15_000;

    /// <summary>M6 7 AR: the farthest corpse of a kill the loot step walks to on this ground, in metres; 0 keeps <c>--fight-loot-radius-m</c>.</summary>
    public int RadiusMetres { get; init; }

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

/// <summary>M6 8 B1: one line of a ground's <c>wants_known</c>.</summary>
/// <param name="SpellId">The spell.</param>
/// <param name="Name">Its name, for the line.</param>
/// <param name="Why">Why the ground wants it.</param>
public sealed record GroundWant(uint SpellId, string Name, string Why);

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

/// <summary>M6 14 T: one ware a ground buys in another town on the way out (the profile's <c>town_stock</c>; <c>Mage.TownStock</c> is its rule).</summary>
/// <param name="Wcid">The ware's weenie class id.</param>
/// <param name="Name">The ware's name.</param>
/// <param name="Keep">The count the stop restocks to.</param>
/// <param name="TripUnder">The count under which the stop is made.</param>
/// <param name="Price">The vendor's price of one (ACE's <c>Vendor.GetSellCost</c> on the vendor's SellPrice).</param>
/// <param name="Burden">The burden of one.</param>
/// <param name="Town">The vendor's town.</param>
/// <param name="Vendor">The vendor: his landblock_instance row; the walk's goal is his own cell and point.</param>
public sealed record TownStockLine(uint Wcid, string Name, int Keep, int TripUnder, int Price, int Burden, string Town, GroundVendor Vendor)
{
    /// <summary>D138: the level from which the buff bot's fee asks for this ware (<see cref="BuffBotFee"/>), whatever the spellbook; null for a line the fee does not ask for.</summary>
    public int? FeeFromLevel { get; init; }

    /// <summary>D138: true for the line the fee alone added (no profile names the ware): under <see cref="FeeFromLevel"/> nothing asks for it.</summary>
    public bool FeeOnly { get; init; }
}

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
/// <param name="TripSpendPercent">M6 7 V: the most of the purse (coin and note faces) one trip's stock buys may spend, in percent (the profile's <c>trip_spend_percent</c>, 1 to 100, default 100 = no budget): a rung that earns little keeps the rest for the next trip.</param>
public sealed record GroundPurse(int Coin, IReadOnlyList<PurseNote> Notes, int MinNote = GroundPurse.DefaultMinNote, int TripSpendPercent = 100)
{
    /// <summary>M6 7 AT: the coin (and note faces) at which a learn trip goes though the next two scrolls are not both paid (the profile's <c>learn_trip_coin</c>, default <see cref="DefaultLearnTripCoin"/>).</summary>
    public int LearnTripCoin { get; init; } = DefaultLearnTripCoin;

    /// <summary>M6 7 AT: the default of <see cref="LearnTripCoin"/>.</summary>
    public const int DefaultLearnTripCoin = 15_000;

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

/// <summary>
/// M6 7 AR: what the plain hunt of a ground leaves alone, any ground's data: below <see cref="UnderLevel"/> no pick, no tour walk and no schedule slot takes a creature in <see cref="KeepOut"/>; an attacker from there is still fought (M5 D88), and the walk may cross the cells (they are not no-go).
/// </summary>
/// <param name="KeepOut">The cells kept out of the hunt.</param>
/// <param name="UnderLevel">The keep-out holds for a character under this level.</param>
/// <param name="Pulls">The data choice of the holder to pull for a wanted trophy (<see cref="GroundPull"/>).</param>
/// <param name="DropBackKeepOut">M6 7 AY: the cells held out while two deaths to big packs have dropped her back (<see cref="Mage.BigPackRule"/>), at any level; empty: no drop-back on this ground. It holds <see cref="KeepOut"/> too (the loader checks).</param>
public sealed record GroundHunt(CellSet KeepOut, int UnderLevel, IReadOnlyList<GroundPull> Pulls, CellSet? DropBackKeepOut = null)
{
    /// <summary>The cells of the drop-back, never null.</summary>
    public CellSet DropBack => DropBackKeepOut ?? CellSet.Empty;

    /// <summary>True when this ground has a drop-back.</summary>
    public bool HasDropBack => DropBack.Cells.Count > 0;

    /// <summary>True when the keep-out holds for a character of <paramref name="level"/>.</summary>
    /// <param name="level">The character's level.</param>
    public bool HoldsAt(int level) => level < UnderLevel;
}

/// <summary>
/// M6 14 entry recharge (the owner, 2026-11-13: a ground profile holds facts only): the outdoor approach to a ground's entry, where its hostiles stand: the landblocks (the drop of the portal that brings her and the walk to the entry's
/// front) and the creatures the world spawns there, each by weenie and name. Facts with their source, never a fight number: a hop whose drop lies here is a drop among hostiles, so she tops up before it where nothing is in reach.
/// </summary>
/// <param name="Landblocks">The outdoor landblocks of the approach.</param>
/// <param name="Hostiles">The creatures that stand there.</param>
/// <param name="Source">Where the facts come from.</param>
public sealed record GroundApproach(IReadOnlyList<ushort> Landblocks, IReadOnlyList<GroundApproachHostile> Hostiles, string Source)
{
    /// <summary>The hostiles' names, comma separated (the log's).</summary>
    public string Names => string.Join(", ", Hostiles.Select(h => h.Name).Distinct(StringComparer.Ordinal));

    /// <summary>
    /// M6 14 approach cost: the encounter generators over the approach's landblocks (the profile's optional <c>encounters</c>: the world's <c>encounter</c> rows there), or null when the profile does not say. With <see cref="PerRoll"/> and the
    /// hostiles' crossing facts it is what the crossing's cost reads (<see cref="Vitals.ApproachCost"/>); <see cref="CostSource"/> names the SELECTs.
    /// </summary>
    public int? Encounters { get; init; }

    /// <summary>M6 14 approach cost: the creatures one roll of those generators stands up at once, every kind counted (the profile's optional <c>per_roll</c>), or null; the share the listed hostiles do not hold is the unlisted creatures'.</summary>
    public double? PerRoll { get; init; }

    /// <summary>M6 14 approach cost: where the crossing's facts come from (the profile's <c>cost_source</c>), or null.</summary>
    public string? CostSource { get; init; }

    /// <summary>The outdoor area of the approach's landblocks in square metres (192 m a side each).</summary>
    public double AreaSquareMetres => Landblocks.Count * 192.0 * 192.0;
}

/// <summary>
/// M6 14 entry recharge: one creature of a ground's approach (<see cref="GroundApproach"/>). M6 14 retreat attackers (live 2026-11-14 10:59:18 UTC: two Infected Assailers killed the climber on the Holding's approach while the run leg's
/// retreat line modelled the Holding's Rampagers): with its attack's facts where the profile gives them (all or none, each from the pinned world SQL and named in its <c>attack_source</c>): its level, its health, the
/// DamageType bits of its attacks, the hardest attack's <c>d_val</c> and <c>d_var</c>, the attribute that scales it, and the most of it one camp holds at once. Facts only: the line and the check are derived from them at run time
/// (<see cref="Vitals.CreatureFacts"/>, <see cref="Vitals.ApproachCheck"/>).
/// </summary>
/// <param name="Wcid">Its weenie class id.</param>
/// <param name="Name">Its name.</param>
public sealed record GroundApproachHostile(uint Wcid, string Name)
{
    /// <summary>Its level (weenie_properties_int type 25), or null when the profile gives no facts.</summary>
    public int? Level { get; init; }

    /// <summary>Its maximum health (the vital's init_Level plus ranks plus Endurance / 2, as <see cref="GroundTarget.MaxHealth"/>), or null.</summary>
    public int? MaxHealth { get; init; }

    /// <summary>The DamageType bits of its attacks: its body parts' and a wielded weapon's (0: not given).</summary>
    public uint DamageTypes { get; init; }

    /// <summary>The hardest attack's <c>d_Val</c>: a body part's, or a thrown weapon's Damage (PropertyInt 44) when that hits harder, or null.</summary>
    public int? DVal { get; init; }

    /// <summary>That attack's variance: a body part's <c>d_Var</c> or the weapon's DamageVariance (PropertyFloat 22), or null.</summary>
    public double? DVar { get; init; }

    /// <summary>
    /// The attribute that scales the hit: Strength for a body part and a thrown weapon (ACE <c>Creature.GetAttributeMod</c>: Coordination only for a bow or a finesse weapon, Creature_Combat.cs:412-420 @95ef426d), or null.
    /// </summary>
    public int? Strength { get; init; }

    /// <summary>The most of it one camp of the approach's generators holds at once (the camp generator's rows' MaxCreate under its MaxGeneratedObjects, PropertyInt 81); 1 when it spawns alone.</summary>
    public int Pack { get; init; } = 1;

    /// <summary>The attack as the fight line reads it, or null when the profile gives no attack facts.</summary>
    public LadderHitter? Hitter => DVal is { } dVal && DVar is { } dVar && Strength is { } strength ? new LadderHitter(Name, Wcid, dVal, dVar, strength) : null;

    /// <summary>
    /// M6 14 approach cost (the owner, 2026-11-14: she was fighting things with high magic resistance on the Holding's approach): its Magic Defense (ACE's <c>CreatureSkill.Current</c>, as <see cref="GroundTarget.MagicDefence"/>), or null when
    /// the profile gives no crossing facts (all or none, with the approach's <see cref="GroundApproach.CostSource"/>).
    /// </summary>
    public int? MagicDefence { get; init; }

    /// <summary>M6 14 approach cost: its resistance multipliers by element (weenie_properties_float 64 to 70; the keys are <see cref="Mage.SpellPlan.BoltLineNames"/>'), or null.</summary>
    public IReadOnlyDictionary<string, double>? Resist { get; init; }

    /// <summary>M6 14 approach cost: how near it notices a player (VisualAwarenessRange, weenie_properties_float 31), metres, or null.</summary>
    public double? AwarenessMetres { get; init; }

    /// <summary>M6 14 approach cost: how many of it one roll of the approach's encounter generators stands up at once (<see cref="GroundApproach.PerRoll"/>'s share), or null.</summary>
    public double? PerRoll { get; init; }

    /// <summary>M6 14 approach cost: the experience its kill gives (XpOverride, weenie_properties_int 146), or null.</summary>
    public long? Xp { get; init; }

    /// <summary>True when the crossing's facts are given (<see cref="MagicDefence"/>, <see cref="Resist"/>, <see cref="AwarenessMetres"/>, <see cref="PerRoll"/>, <see cref="Xp"/>) and its health is known.</summary>
    public bool HasCrossingFacts => MagicDefence is not null && Resist is not null && AwarenessMetres is not null && PerRoll is not null && Xp is not null && MaxHealth is > 0;

    /// <summary>
    /// M6 14 run past (D137): its Run skill (ACE's <c>CreatureSkill.Current</c> of Run: weenie_properties_skill type 24 init_Level + level_From_P_P + Quickness / 1, CreatureSkill.cs:154-163 @95ef426d), from the profile's optional
    /// <c>pace</c> (all of <see cref="RunSkill"/>, <see cref="MotionTable"/> and <see cref="Scale"/>, or none), or null. Its speed is derived at run time (<see cref="Vitals.CreaturePace"/>).
    /// </summary>
    public int? RunSkill { get; init; }

    /// <summary>M6 14 run past: its motion table (weenie_properties_d_i_d type 2), whose RunForward cycle the dats give the run of, or null.</summary>
    public uint? MotionTable { get; init; }

    /// <summary>M6 14 run past: its ObjScale (weenie_properties_float type 39; 1 when the weenie has none).</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>M6 14 run past: its ranged attack (a thrown weapon it wields), or null: it attacks in melee only, as far as the facts say.</summary>
    public GroundRanged? Ranged { get; init; }

    /// <summary>M6 14 drain resistance: its <c>ResistHealthDrain</c> (the profile's optional <c>resist_health_drain</c>, as <see cref="GroundTarget.ResistHealthDrain"/>); 1 when the profile does not say.</summary>
    public double ResistHealthDrain { get; init; } = 1.0;
}

/// <summary>
/// M6 14 run past (D137): a creature's ranged attack, facts only (the profile's optional <c>ranged</c> of an approach hostile): the thrown weapon's Damage and DamageVariance, the DamageType it deals and the attribute that scales it (ACE's
/// <c>GetMissileDamage</c> and <c>GetAttributeMod</c>: Strength for a thrown weapon, Monster_Melee.cs:385-388 and Creature_Combat.cs:412-420 @95ef426d), and the range it throws from (<c>GetMaxMissileRange</c>: the weapon's MaximumVelocity
/// squared x 0.102, at most 77.7 m, Creature_Missile.cs:402-416).
/// </summary>
/// <param name="DVal">The thrown weapon's Damage (weenie_properties_int type 44).</param>
/// <param name="DVar">Its DamageVariance (weenie_properties_float type 22).</param>
/// <param name="DamageTypes">The DamageType bits it deals (type 45).</param>
/// <param name="Strength">The thrower's Strength.</param>
/// <param name="RangeMetres">The range it throws from.</param>
public sealed record GroundRanged(int DVal, double DVar, uint DamageTypes, int Strength, double RangeMetres)
{
    /// <summary>The share of its attacks that are this one (1: every attack; a caster's war spells: the sum of their spell book chances, ACE's <c>probability</c> over 2).</summary>
    public double Share { get; init; } = 1.0;

    /// <summary>True for a war spell (a spell takes no armour, only her resistance: ACE <c>SpellProjectile.CalculateDamage</c>), false for a thrown weapon.</summary>
    public bool Spell { get; init; }

    /// <summary>The throw as the fight line reads a hit (<see cref="LadderHitter"/>).</summary>
    /// <param name="name">The thrower's name.</param>
    /// <param name="wcid">Its weenie class id.</param>
    public LadderHitter Hitter(string name, uint wcid) => new(name, wcid, DVal, DVar, Strength);
}

/// <summary>
/// M6 redoubt (the Holtburg Redoubt, landblock 0x0163): a dungeon's galleries. A gallery is a walkway or a landing whose floor polygons belong to cells of the room under it, while a body on it stands in the floorless cell above
/// them (the dats; the physics body's cell on the dat walk, <c>HoltburgRedoubtDatTests</c>). The draft's rule leaves a floorless cell out of the storeys and never reaches the cells under it (their one portal is the opening
/// overhead), so without this section the rooms on the two sides of a gallery are not joined. Facts only, each from the dats, named in <paramref name="Source"/>.
/// </summary>
/// <param name="Cells">The floorless cells a body stands in on a gallery: storey cells, though they hold no floor polygon.</param>
/// <param name="Floors">The cells that hold the galleries' floor polygons: in the room graph, on no storey (no body's cell is one of them).</param>
/// <param name="Source">Where the facts come from.</param>
public sealed record GroundGalleries(CellSet Cells, CellSet Floors, string Source);

/// <summary>M6 7 AY: where a corpse is safe to reach for its coin (the profile's optional <c>corpse_fetch</c>): anywhere on an outdoor ground, or a list of a dungeon's front cells. Absent: nowhere.</summary>
/// <param name="Anywhere">True: every cell of the ground.</param>
/// <param name="Cells">The cells, when not <paramref name="Anywhere"/>.</param>
public sealed record GroundCorpseFetch(bool Anywhere, CellSet Cells)
{
    /// <summary>True when <paramref name="cell"/> is on the list.</summary>
    /// <param name="cell">The corpse's cell.</param>
    public bool Holds(uint cell) => Anywhere || Cells.Contains(cell);
}

/// <summary>M6 7 AR: for a wanted trophy, the cells of the holders to pull, a data choice. M6 7 AX: with a <see cref="Stand"/> it is a named job (<see cref="Autopilot.RungOwner"/>) and a pull (<c>FightScenario.Pull.cs</c>); a pull with none is still <c>TODO(m6-tusk-pull)</c>.</summary>
/// <param name="Item">The trophy's weenie class.</param>
/// <param name="Name">The trophy's name.</param>
/// <param name="Cells">The cells of the chosen holders.</param>
public sealed record GroundPull(uint Item, string Name, IReadOnlyList<uint> Cells)
{
    /// <summary>M6 7 AX: where she shoots from and at whom, or null.</summary>
    public PullStand? Stand { get; init; }
}

/// <summary>M6 7 AX: the pull's data: the job's quest and levels, the stand point, the holder and the limits.</summary>
/// <param name="Quest">The quest's key: the job is due while this quest is (the existing quest timers) and its trophy is not held.</param>
/// <param name="FromLevel">The first level of the job.</param>
/// <param name="ToLevel">The last level of the job.</param>
/// <param name="Cell">The stand point's cell.</param>
/// <param name="Point">The stand point, landblock-local.</param>
/// <param name="HolderWcid">The holder's weenie class.</param>
/// <param name="HolderCell">The holder's cell.</param>
/// <param name="WaitSeconds">The named limit of the wait for the holder's respawn.</param>
/// <param name="Tries">The shots a session spends on it.</param>
public sealed record PullStand(string Quest, int FromLevel, int ToLevel, uint Cell, System.Numerics.Vector3 Point, uint HolderWcid, uint HolderCell, double WaitSeconds, int Tries);

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
    /// <summary>M6 7 AR: the creature's maximum health (the profile's optional <c>max_health</c>, the pinned world's), 0 when not listed: the picker's number before the first health push.</summary>
    public int MaxHealth { get; init; }

    /// <summary>
    /// The spawn mass (item 2 g3, D42): the chance, 0 to 1, that one roll of a spot's generator spawns this creature (the generator's cumulative-probability rows, nested generators and camps multiplied; for a
    /// ground whose spots roll different generators, the mean over its cells). The profile's <c>spawn_mass</c> is required and the loader reads it, so a profile that lacks it fails at load; the default
    /// here is only for code that builds a creature by hand. A hunt's expected usable creatures a spot is the sum over the creatures it will fight (<c>HuntGroundRow.ExpectedYield</c>).
    /// </summary>
    public double SpawnMass { get; init; }

    /// <summary>M6 14 drain resistance: its <c>ResistHealthDrain</c> (the profile's optional <c>resist_health_drain</c>, as <see cref="GroundTarget.ResistHealthDrain"/>); 1 when the profile does not say.</summary>
    public double ResistHealthDrain { get; init; } = 1.0;
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

/// <summary>M6 14 holding plates: a ground's pressure plates (the profile's <c>plates</c> section), every fact sourced in its <c>source</c>.</summary>
/// <param name="Wcid">The plates' weenie.</param>
/// <param name="Name">Its name, as the server says it (the chat line of a plate that fires names it).</param>
/// <param name="SetupId">Its Setup (the world SQL's <c>weenie_properties_d_i_d</c> type 1), whose shape in the dats ACE fires the plate on; the walls read it (<see cref="HuntingGround.KeptPlatesOf"/>).</param>
/// <param name="Spells">The spells a plate casts on the player who touches it.</param>
/// <param name="Points">Each plate.</param>
/// <param name="Source">Where the facts come from.</param>
public sealed record GroundPlates(uint Wcid, string Name, uint SetupId, IReadOnlyList<GroundPlateSpell> Spells, IReadOnlyList<GroundPlate> Points, string Source);

/// <summary>M6 14 holding plates: a spell a plate casts.</summary>
/// <param name="Id">The spell id.</param>
/// <param name="Name">Its name (the dats').</param>
public sealed record GroundPlateSpell(uint Id, string Name);

/// <summary>M6 14 holding plates: one plate.</summary>
/// <param name="Guid">Its world guid (landblock_instance).</param>
/// <param name="Cell">The cell it stands in.</param>
/// <param name="Point">Its origin, landblock-local.</param>
public sealed record GroundPlate(uint Guid, uint Cell, Vector3 Point)
{
    /// <summary>The plate's name for the logs: its guid and cell.</summary>
    public string Name => $"the plate 0x{Guid:X8} in 0x{Cell:X8}";
}
