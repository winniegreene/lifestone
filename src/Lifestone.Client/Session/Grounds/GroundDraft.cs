using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// The dats' part of a dungeon's draft (<see cref="GroundDraft.Cells"/>): the cells the arrival reaches over the doorways the room graph accepts at the ground's lean, the storeys they make, and the
/// cells left out (floorless, or holding a floor the arrival does not reach).
/// </summary>
/// <param name="Landblock">The landblock (its high word, for example 0x0105).</param>
/// <param name="Arrival">The arrival cell (a full cell id).</param>
/// <param name="DoorwayLean">The doorway lean the walk used (<see cref="RoomGraph.IsPassable"/>).</param>
/// <param name="Storeys">The walkable cells grouped by origin z, highest band first; each band is named by its z (<c>"z -6"</c>).</param>
/// <param name="Floorless">The cells with no floor polygon (none is walkable), reached or not, in id order.</param>
/// <param name="Pockets">The cells with a floor that the arrival does not reach, in id order.</param>
/// <param name="Total">Every EnvCell of the landblock.</param>
public sealed record DraftCells(ushort Landblock, uint Arrival, float DoorwayLean, IReadOnlyList<GroundStorey> Storeys, IReadOnlyList<uint> Floorless, IReadOnlyList<uint> Pockets, int Total)
{
    /// <summary>Every walkable cell (the storeys' cells), in id order.</summary>
    public IReadOnlyList<uint> Walkable { get; } = Storeys.SelectMany(s => s.Cells.Cells).Order().ToArray();
}

/// <summary>One generator of a draft: its instance, where it stands, its delay from its profile rows and its tick from <c>RegenerationInterval</c> (null when the dump has none, named).</summary>
/// <param name="Guid">The generator instance's guid.</param>
/// <param name="Wcid">The generator's weenie class id.</param>
/// <param name="Cell">The cell it stands in.</param>
/// <param name="DelaySeconds">The delay of its first profile row, or null when that row's delay is NULL.</param>
/// <param name="TickSeconds">Its weenie's <c>RegenerationInterval</c> (PropertyFloat 41), or null when it has none.</param>
public sealed record DraftGenerator(uint Guid, uint Wcid, uint Cell, double? DelaySeconds, double? TickSeconds);

/// <summary>One slot of a draft: what a generator keeps standing where (a link child, or a profile row's creature at its cell).</summary>
/// <param name="Generator">The generator's guid.</param>
/// <param name="Guid">The link child's instance guid, or 0 for a slot from a profile row.</param>
/// <param name="Cell">The cell.</param>
/// <param name="Wcid">The weenie class id.</param>
public sealed record DraftSlot(uint Generator, uint Guid, uint Cell, uint Wcid);

/// <summary>A weenie the slots hold: its name (PropertyString 1) and level (PropertyInt 25, null when none).</summary>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Name">The name, or <c>"(no name)"</c>.</param>
/// <param name="Level">The level, or null.</param>
public sealed record DraftCreature(uint Wcid, string Name, int? Level)
{
    /// <summary>M6 3 a1: the six attributes' current values (weenie_properties_attribute init_Level + level_From_C_P), or null when a row is missing (named in the notes).</summary>
    public GroundAttributes? Attributes { get; init; }

    /// <summary>M6 3 a1: ACE's current maximum health (<see cref="GroundDraft.MaxHealthOf"/>), or null when the vital or Endurance row is missing (named).</summary>
    public int? MaxHealth { get; init; }

    /// <summary>M6 3 a1: ACE's current Magic Defense (<see cref="GroundDraft.MagicDefenseOf"/>), or null when the skill row is missing or the skill is not trained (named).</summary>
    public int? MagicDefense { get; init; }

    /// <summary>M6 3 a1: the OR of the d_Type of its body parts with a d_Val (weenie_properties_body_part), or null when it has none (named).</summary>
    public uint? DamageTypes { get; init; }
}

/// <summary>A portal instance of the landblock and where it leads (its weenie's Destination, PositionType 2).</summary>
/// <param name="Guid">The instance guid.</param>
/// <param name="Wcid">The portal's weenie class id.</param>
/// <param name="Cell">The cell it stands in.</param>
/// <param name="Destination">The destination cell.</param>
public sealed record DraftPortal(uint Guid, uint Wcid, uint Cell, uint Destination);

/// <summary>Where a portal into the landblock puts the player in the arrival cell (a Destination row, PositionType 2, whose cell is the arrival).</summary>
/// <param name="Wcid">The weenie whose Destination it is.</param>
/// <param name="Origin">The point in the landblock.</param>
/// <param name="HeadingW">The quaternion's w.</param>
/// <param name="HeadingZ">The quaternion's z.</param>
public sealed record DraftArrival(uint Wcid, Vector3 Origin, float HeadingW, float HeadingZ)
{
    /// <summary>The arrival cell (a full cell id).</summary>
    public uint Cell { get; init; }
}

/// <summary>A portal weenie whose Destination (PositionType 2) lies in the dungeon's landblock: an entry into it (M5 item 2 g8).</summary>
/// <param name="Wcid">The portal's weenie class id.</param>
/// <param name="Name">Its name (PropertyString 1).</param>
/// <param name="Cell">The destination cell, which is the arrival when this entry is taken.</param>
/// <param name="Origin">The destination point.</param>
/// <param name="HeadingW">The destination quaternion's w.</param>
/// <param name="HeadingZ">The destination quaternion's z.</param>
/// <param name="MinLevel">The portal's MinLevel (PropertyInt 86), or null when it has none (the profile's <c>entry.min_level</c>, M6 3 a1).</param>
public sealed record DraftEntry(uint Wcid, string Name, uint Cell, Vector3 Origin, float HeadingW, float HeadingZ, int? MinLevel)
{
    /// <summary>M6 3 a1: the portal's MaxLevel (PropertyInt 87), or null when it has none.</summary>
    public int? MaxLevel { get; init; }

    /// <summary>M6 3 a1: the portal's PortalBitmask (PropertyInt 111), or null when it has none (ACE then reads Unrestricted, Portal_Properties.cs:10 @95ef426d).</summary>
    public int? PortalBitmask { get; init; }

    /// <summary>M6 3 a1: false when the bitmask carries NoRecall (0x20), which is ACE's <c>Portal.NoTie</c> (Portal_Properties.cs:14-16 @95ef426d).</summary>
    public bool Tieable => PortalBitmask is not { } bits || (bits & GroundDraft.NoRecallBit) == 0;
}

/// <summary>A world object at a place: a landblock_instance row, with the cell its point lies in (an outdoor row carries cell 0xLLLL0000; <see cref="GroundDraft.OutdoorCell"/>).</summary>
/// <param name="Guid">The instance guid.</param>
/// <param name="Wcid">The weenie class id.</param>
/// <param name="Cell">The cell the point lies in (an indoor row's own cell).</param>
/// <param name="Point">The landblock-local point.</param>
public sealed record DraftPlace(uint Guid, uint Wcid, uint Cell, Vector3 Point)
{
    /// <summary>M6 3 a1: the row's quaternion w (angles_W; 1 when not read).</summary>
    public float HeadingW { get; init; } = 1f;

    /// <summary>M6 3 a1: the row's quaternion z (angles_Z).</summary>
    public float HeadingZ { get; init; }
}

/// <summary>A lifestone instance and its distance to the entry portal in world metres.</summary>
/// <param name="Place">The lifestone's instance.</param>
/// <param name="Metres">The straight-line distance to the entry portal (landblock x and y times 192 plus the point).</param>
public sealed record DraftLifestone(DraftPlace Place, double Metres);

/// <summary>A ware of a vendor's shop (a create-list row with the Shop destination) with its price at the vendor's own sell multiplier.</summary>
/// <param name="Wcid">The ware's weenie class id.</param>
/// <param name="Name">Its name (PropertyString 1).</param>
/// <param name="Value">Its Value (PropertyInt 19; 0 when the dump has none).</param>
/// <param name="Price">What one costs: <c>max(1, ceil(m x Value - 0.1))</c> in float arithmetic (<see cref="GroundDraft.PriceOf"/>), 0 when the vendor has no sell multiplier.</param>
public sealed record DraftWare(uint Wcid, string Name, long Value, int Price);

/// <summary>A vendor near the town's lifestone.</summary>
/// <param name="Place">The vendor's instance (guid, cell, point).</param>
/// <param name="Name">Its name (PropertyString 1).</param>
/// <param name="BuysItemTypes">What it buys: MerchandiseItemTypes (PropertyInt 74).</param>
/// <param name="SellMultiplier">The vendor's own SellPrice (PropertyFloat 38), or null when it has none (ACE disables such a vendor).</param>
/// <param name="Metres">The straight-line distance to the lifestone.</param>
/// <param name="InThreeByThree">True when the vendor's landblock is the lifestone's or one of the eight around it.</param>
/// <param name="Wares">What it sells: every shop row.</param>
/// <param name="StopPoint">The point in a stop room in front of the vendor the walk ends at (the profile's <c>stop_point</c>, <see cref="GroundVendor.StopPoint"/>); the draft never finds one (the stop cell it writes is the vendor's own), so it is null unless a caller sets it, and the writer then writes the field.</param>
public sealed record DraftVendor(DraftPlace Place, string Name, uint BuysItemTypes, double? SellMultiplier, double Metres, bool InThreeByThree, IReadOnlyList<DraftWare> Wares, Vector3? StopPoint = null);

/// <summary>The town half of a draft (M5 item 2 g8): the entry portal's instance(s), the lifestones nearest the portal and the vendors near the first of them.</summary>
/// <param name="Entry">The entry portal taken.</param>
/// <param name="Portals">Every instance of the entry portal's weenie in the world, by guid (the first is the portal).</param>
/// <param name="Lifestones">The lifestones nearest the first portal, nearest first (the first is the home).</param>
/// <param name="Vendors">The vendors near the home lifestone, nearest first.</param>
public sealed record DraftTown(DraftEntry Entry, IReadOnlyList<DraftPlace> Portals, IReadOnlyList<DraftLifestone> Lifestones, IReadOnlyList<DraftVendor> Vendors)
{
    /// <summary>The portal: the first instance, or null when the world has none.</summary>
    public DraftPlace? Portal => Portals.Count > 0 ? Portals[0] : null;

    /// <summary>The home lifestone: the nearest, or null when there is none.</summary>
    public DraftLifestone? Lifestone => Lifestones.Count > 0 ? Lifestones[0] : null;
}

/// <summary>A creature whose Death or Use emote casts a teleport spell (or teleports): killing or using it takes the player out, an exit that is not a portal object (M5 item 2 g8, Tusker Temple's Shrine Statue).</summary>
/// <param name="Wcid">The creature's weenie class id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Category">The emote's EmoteCategory: 3 Death, 7 Use.</param>
/// <param name="ActionType">The emote action's EmoteType: 14 CastSpell, 19 CastSpellInstant, 99 TeleportTarget, 100 TeleportSelf.</param>
/// <param name="Spell">The spell id, or 0 for a teleport action.</param>
/// <param name="SpellName">The spell's name (the dump's spell table), or the action's name.</param>
/// <param name="Destination">Where it leads: the spell's or the action's destination cell.</param>
/// <param name="Cells">The cells of the creature's slots (proposed no-go).</param>
public sealed record DraftEmoteExit(uint Wcid, string Name, uint Category, uint ActionType, uint Spell, string SpellName, uint Destination, IReadOnlyList<uint> Cells)
{
    /// <summary>The category's name.</summary>
    public string CategoryName => Category switch { 3 => "Death", 7 => "Use", _ => "category " + Category };
}

/// <summary>The pinned world SQL's part of a draft (<see cref="GroundDraft.World(Stream, ushort, uint?, ushort?)"/>).</summary>
/// <param name="Generators">Every generator instance in the landblock, by guid.</param>
/// <param name="Slots">Every slot, by generator then link child guid (profile-row slots last, in row order).</param>
/// <param name="Creatures">The slots' weenies, by weenie class id.</param>
/// <param name="Exits">The portals whose destination lies outside the landblock (the draft's no-go cells), by guid.</param>
/// <param name="Arrival">The entrance's destination in the arrival cell, or null when no portal names it.</param>
/// <param name="Notes">The named TODOs the draft met (an empty list for a ground it fully understood).</param>
public sealed record DraftWorld(IReadOnlyList<DraftGenerator> Generators, IReadOnlyList<DraftSlot> Slots, IReadOnlyList<DraftCreature> Creatures, IReadOnlyList<DraftPortal> Exits, DraftArrival? Arrival, IReadOnlyList<string> Notes)
{
    /// <summary>Every portal whose Destination lies in the landblock (an entry), by weenie class id; the arrival is the first's unless the caller named one.</summary>
    public IReadOnlyList<DraftEntry> Entries { get; init; } = [];

    /// <summary>The town half (null when no entry portal leads into the landblock).</summary>
    public DraftTown? Town { get; init; }

    /// <summary>The creatures of the slots whose Death or Use emote teleports (exits that are not portal objects).</summary>
    public IReadOnlyList<DraftEmoteExit> EmoteExits { get; init; } = [];

    /// <summary>M6 3 a1: each exit portal's landblock-local origin (its landblock_instance row), by the exit's guid.</summary>
    public IReadOnlyDictionary<uint, Vector3> ExitOrigins { get; init; } = new Dictionary<uint, Vector3>();

    /// <summary>The exits' cells, distinct, in id order: the exit portals' and the emote exits' slot cells (the draft's no-go cells).</summary>
    public IReadOnlyList<uint> ExitCells => Exits.Select(e => e.Cell).Concat(EmoteExits.SelectMany(e => e.Cells)).Distinct().Order().ToArray();
}

/// <summary>
/// The draft builder of a dungeon's hunting-ground profile (M5 item 2 g2, D46): from the dats, the walkable cells and storeys of any dungeon landblock given its arrival cell and doorway lean;
/// from the pinned world SQL (parsed read-only, never loaded into a database), its generators with their own delay and tick, their slots, the slots' creatures, the exits (portal objects, and creatures whose Death or Use
/// emote teleports), the entry portals and the arrival they give, and the town half (the entry portal's instance, the lifestone nearest it, the vendors near that lifestone with what they buy, sell and charge).
/// <see cref="Json"/> writes the draft profile; a person adds the hand-set fields (the design's avoided roles, the no-go cells the geometry or the design asks for, the tour, which vendors and wares a ground uses, the town's name,
/// the spell plan, the components, the walk's stop rules), each a <c>TODO(hand-set)</c> value that <see cref="GroundCatalog"/> refuses until it is set. Nothing here is one dungeon's.
/// </summary>
public static class GroundDraft
{
    /// <summary>The marker of a value a person sets: the draft writes it in place of the value, and the catalog refuses it at load naming the field.</summary>
    public const string HandSet = "TODO(hand-set)";

    /// <summary>PropertyFloat RegenerationInterval (ACE.Entity/Enum/Properties/PropertyFloat.cs @95ef426d; the pinned dump's weenie_properties_float type 41).</summary>
    internal const int RegenerationIntervalProperty = 41;

    /// <summary>PropertyInt Level.</summary>
    internal const int LevelProperty = 25;

    /// <summary>PropertyString Name.</summary>
    internal const int NameProperty = 1;

    /// <summary>PositionType Destination (a portal's).</summary>
    internal const int DestinationPosition = 2;

    /// <summary>WeenieType Creature (ACE.Entity/Enum/WeenieType.cs @95ef426d; the pinned dump's <c>weenie.type</c>): a slot is a creature's.</summary>
    internal const int CreatureType = 10;

    /// <summary>RegenLocationType's Contain (0x8), Shop (0x10) and Treasure (0x40) bits (ACE.Entity/Enum/RegenLocationType.cs @95ef426d): a profile row with one makes loot in a container, not a creature that stands (the den's chests' rows are where_Create 72).</summary>
    internal const long NotStandingLocations = 0x08 | 0x10 | 0x40;

    /// <summary>
    /// The walkable cells of a dungeon landblock: every cell <paramref name="arrival"/> reaches over the inside doorways <see cref="RoomGraph.IsPassable"/> accepts at
    /// <paramref name="doorwayLean"/> (a breadth-first walk with no z filter and no exclusion), less the floorless ones, grouped by origin z into storeys (the bands named by z, highest first).
    /// </summary>
    /// <param name="all">Every cell of the landblock (<see cref="LandblockCellSet.Load"/>).</param>
    /// <param name="arrival">The arrival cell (a full cell id in the landblock).</param>
    /// <param name="doorwayLean">The ground's doorway lean.</param>
    /// <param name="jumps">M6 14 pits: the pits a running jump crosses (the profile's <c>jumps</c>, <see cref="HuntingGround.Jumps"/>), each an edge both ways between its two rooms; null or empty: walks only.</param>
    /// <exception cref="ArgumentException">The arrival is not a cell of the landblock.</exception>
    public static DraftCells Cells(LandblockCellSet all, uint arrival, float doorwayLean, IReadOnlyList<Lifestone.Client.Movement.Routes.PitJump>? jumps = null)
    {
        ArgumentNullException.ThrowIfNull(all);
        if (all.GetCell(arrival) is null)
        {
            throw new ArgumentException($"the arrival 0x{arrival:X8} is not a cell of landblock 0x{all.Coord.X:X2}{all.Coord.Y:X2} ({all.Count} cells)", nameof(arrival));
        }

        var graph = new RoomGraph(all, doorwayLean) { Jumps = jumps ?? [] };
        var reached = new HashSet<uint> { arrival };
        var queue = new Queue<uint>([arrival]);
        while (queue.Count > 0)
        {
            var cell = all.GetCell(queue.Dequeue())!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || all.GetCell(portal.OtherCellId) is not { } other || reached.Contains(other.Id) || !graph.IsPassable(cell, portal))
                {
                    continue;
                }

                reached.Add(other.Id);
                queue.Enqueue(other.Id);
            }

            foreach (var jump in graph.Planner.JumpsFrom(cell.Id))
            {
                if (reached.Add(jump.To.Cell))
                {
                    queue.Enqueue(jump.To.Cell); // M6 14 pits: the far side of a pit, by a running jump
                }
            }
        }

        var floorless = all.Cells.Where(c => all.GetStructure(c).Floors.Count == 0).Select(c => c.Id).Order().ToArray();
        var walkable = all.Cells.Where(c => reached.Contains(c.Id) && all.GetStructure(c).Floors.Count > 0).ToArray();
        var pockets = all.Cells.Where(c => !reached.Contains(c.Id) && all.GetStructure(c).Floors.Count > 0).Select(c => c.Id).Order().ToArray();
        var storeys = walkable
            .GroupBy(c => Band(c.Frame.Origin.Z))
            .OrderByDescending(g => g.Key)
            .Select(g => new GroundStorey(StoreyName(g.Key), g.Key, g.Key, CellSet.Of(g.Select(c => c.Id))))
            .ToArray();
        var landblock = (ushort)((all.Coord.X << 8) | all.Coord.Y);
        return new DraftCells(landblock, arrival, doorwayLean, storeys, floorless, pockets, all.Count);
    }

    /// <summary>
    /// The walkable cells a no-go set cuts off: those of <paramref name="walkable"/> the arrival no longer reaches over the room graph of the walkable cells less <paramref name="noGo"/> at
    /// <paramref name="doorwayLean"/> (a dead end behind an exit or a wing). A profile lists them as no-go too, so every walkable cell is reached from the arrival (DenRoomGraphTests).
    /// </summary>
    /// <param name="all">Every cell of the landblock.</param>
    /// <param name="arrival">The arrival cell.</param>
    /// <param name="doorwayLean">The ground's doorway lean.</param>
    /// <param name="walkable">The draft's walkable cells.</param>
    /// <param name="noGo">The no-go test of the cells set by hand.</param>
    /// <param name="jumps">M6 14 pits: the pits a running jump crosses (<see cref="Cells"/>); null or empty: walks only.</param>
    /// <returns>The cut-off cells, in id order.</returns>
    public static IReadOnlyList<uint> CutOff(LandblockCellSet all, uint arrival, float doorwayLean, IEnumerable<uint> walkable, Func<uint, bool> noGo, IReadOnlyList<Lifestone.Client.Movement.Routes.PitJump>? jumps = null)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(walkable);
        ArgumentNullException.ThrowIfNull(noGo);
        var keep = walkable.Where(c => !noGo(c)).ToHashSet();
        var graph = new RoomGraph(LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => keep.Contains(c.Id))), doorwayLean) { Jumps = jumps ?? [] };
        return keep.Where(c => c != arrival && graph.FindPath(arrival, c) is null).Order().ToArray();
    }

    /// <summary>A storey's name from its band's z: <c>"z 0"</c>, <c>"z -6"</c>, <c>"z -4.5"</c>.</summary>
    /// <param name="z">The band's origin z.</param>
    public static string StoreyName(float z) => "z " + z.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A cell origin's z band: the z to the centimetre (the dats' dungeon cells sit on whole storeys; a dungeon whose cells do not is named in the draft's storey count).</summary>
    private static float Band(float z) => MathF.Round(z * 100f) / 100f;

    /// <summary>WeenieType Vendor and LifeStone (ACE.Entity/Enum/WeenieType.cs @95ef426d, members Vendor and LifeStone; the pinned dump's <c>weenie.type</c> 12 and 25).</summary>
    internal const int VendorType = 12, LifestoneType = 25;

    /// <summary>PropertyInt Value (19), MerchandiseItemTypes (74) and MinLevel (86); PropertyFloat BuyPrice (37) and SellPrice (38) (ACE.Entity/Enum/Properties/PropertyInt.cs, PropertyFloat.cs @95ef426d).</summary>
    internal const int ValueProperty = 19, MerchandiseItemTypesProperty = 74, MinLevelProperty = 86, SellPriceProperty = 38;

    /// <summary>M6 3 a1: PropertyInt MaxLevel (87) and PortalBitmask (111) (ACE.Entity/Enum/Properties/PropertyInt.cs:127 and 163 @95ef426d).</summary>
    internal const int MaxLevelProperty = 87, PortalBitmaskProperty = 111;

    /// <summary>M6 3 a1: PortalBitmask.NoRecall (ACE.Entity/Enum/PortalBitmask.cs:15 @95ef426d), which ACE's <c>Portal.NoTie</c> also reads (Portal_Properties.cs:14-16).</summary>
    internal const int NoRecallBit = 0x20;

    /// <summary>M6 3 a1: PropertyAttribute Strength 1, Endurance 2, Quickness 3, Coordination 4, Focus 5, Self 6 (ACE.Entity/Enum/Properties/PropertyAttribute.cs:9-14 @95ef426d).</summary>
    internal const int StrengthAttribute = 1, EnduranceAttribute = 2, QuicknessAttribute = 3, CoordinationAttribute = 4, FocusAttribute = 5, SelfAttribute = 6;

    /// <summary>M6 3 a1: PropertyAttribute2nd MaxHealth (1) and Skill MagicDefense (15) (ACE.Entity/Enum/Properties/PropertyAttribute2nd.cs:9, ACE.Entity/Enum/Skill.cs @95ef426d).</summary>
    internal const int MaxHealthVital = 1, MagicDefenseSkill = 15;

    /// <summary>M6 3 a1: SkillAdvancementClass Trained (2); Specialized is 3 (ACE.Entity/Enum/SkillAdvancementClass.cs:6-9 @95ef426d): a trained skill adds its attribute formula (CreatureSkill.IsUsable, CreatureSkill.cs:48-53).</summary>
    internal const int TrainedSkill = 2;

    /// <summary>
    /// M6 3 a1: the retail formulas the draft's health and Magic Defense apply (<c>SkillFormula::Calculate</c>, 11.6096 0x00592660, rounded half up as ACE's AttributeFormula.cs:55-71 @95ef426d): MaxHealth is Endurance / 2 (the
    /// SecondaryAttributeTable's, the same as <c>PlayerQualities.TryGetVitalLevels</c>'s live-checked (Endurance + 1) / 2), Magic Defense (Focus + Self) / 7 (the SkillTable's skill 15; GroundDraftTownTests reads it from the dats).
    /// </summary>
    internal static readonly Lifestone.Data.CharacterCreation.SkillTableFormula MaxHealthFormula = new(0, 1, 0, 2, EnduranceAttribute, 0), MagicDefenseFormula = new(0, 1, 1, 7, SelfAttribute, FocusAttribute);

    /// <summary>M6 3 a1: how far in front of a portal the tie point stands, in metres, along its facing (the den's W0 stand read against its row lies 3 m along the facing to within 5 cm; tusker-temple-fort-tethana.json's portal source).</summary>
    internal const float TieFrontMetres = 3f;

    /// <summary>DestinationType.Shop (ACE.Entity/Enum/DestinationType.cs @95ef426d line 11): a create-list row with this bit is a vendor's ware.</summary>
    internal const int ShopDestination = 4;

    /// <summary>EmoteCategory Death (3) and Use (7) (ACE.Entity/Enum/EmoteCategory.cs @95ef426d lines 15 and 19): the emotes a player's own act (killing or using the creature) fires.</summary>
    internal const int DeathCategory = 3, UseCategory = 7;

    /// <summary>EmoteType CastSpell (14), CastSpellInstant (19), TeleportTarget (99) and TeleportSelf (100) (ACE.Entity/Enum/EmoteType.cs @95ef426d lines 23, 28, 108 and 109).</summary>
    internal const int CastSpellAction = 14, CastSpellInstantAction = 19, TeleportTargetAction = 99, TeleportSelfAction = 100;

    /// <summary>The global Primary Portal Tie and Primary Portal Recall spell ids (the pinned dump's spell table rows 47 and 48), which tie to and recall to any portal.</summary>
    internal const int PrimaryPortalTieSpell = 47, PrimaryPortalRecallSpell = 48;

    /// <summary>
    /// How far from the home lifestone a vendor is still the town's, in world metres (a draft heuristic, not a fact of any ground): Fort Tethana's three vendors stand 271 to 543 m from its lifestone,
    /// two to three landblocks away, so the 3x3 landblocks around the lifestone's (<see cref="DraftVendor.InThreeByThree"/>, also kept) would miss them all.
    /// </summary>
    internal const double TownRadiusMetres = 600;

    /// <summary>How many lifestones the town half lists (nearest first; the first is the home).</summary>
    internal const int ListedLifestones = 3;

    /// <summary>The tables and rows of the pinned world SQL a draft of <paramref name="landblock"/> reads (the first of the two passes over the dump).</summary>
    /// <param name="landblock">The landblock's high word.</param>
    internal static IReadOnlyDictionary<string, Func<SqlRow, bool>> WorldTables(ushort landblock) => new Dictionary<string, Func<SqlRow, bool>>(StringComparer.Ordinal)
    {
        ["weenie"] = _ => true,
        ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) == landblock,
        ["landblock_instance_link"] = _ => true,
        ["weenie_properties_generator"] = _ => true,
        ["weenie_properties_float"] = r => r.L("type") is RegenerationIntervalProperty or SellPriceProperty,
        ["weenie_properties_int"] = r => r.L("type") is LevelProperty or ValueProperty or MerchandiseItemTypesProperty or MinLevelProperty or MaxLevelProperty or PortalBitmaskProperty,
        ["weenie_properties_attribute"] = _ => true, // M6 3 a1: the target stats
        ["weenie_properties_attribute_2nd"] = r => r.L("type") == MaxHealthVital,
        ["weenie_properties_skill"] = r => r.L("type") == MagicDefenseSkill,
        ["weenie_properties_body_part"] = r => r.L("d_Val") != 0,
        ["weenie_properties_string"] = r => r.L("type") == NameProperty,
        ["weenie_properties_position"] = r => r.L("position_Type") == DestinationPosition,
        ["weenie_properties_create_list"] = r => (r.L("destination_Type") & ShopDestination) != 0,
        ["weenie_properties_emote"] = r => r.L("category") is DeathCategory or UseCategory,
        ["weenie_properties_emote_action"] = r => r.L("type") is CastSpellAction or CastSpellInstantAction or TeleportTargetAction or TeleportSelfAction,
        ["spell"] = r => r.L("position_Obj_Cell_ID") != 0,
    };

    /// <summary>The second pass: the landblock_instance rows of the town's weenies (entry portals, lifestones, vendors) outside the dungeon's landblock, which the first pass could not know before it read the weenie table.</summary>
    /// <param name="landblock">The dungeon's landblock (its rows came with the first pass).</param>
    /// <param name="weenies">The weenie class ids wanted.</param>
    internal static IReadOnlyDictionary<string, Func<SqlRow, bool>> InstanceTables(ushort landblock, IReadOnlySet<uint> weenies) => new Dictionary<string, Func<SqlRow, bool>>(StringComparer.Ordinal)
    {
        ["landblock_instance"] = r => (r.L("obj_Cell_Id") >> 16) != landblock && weenies.Contains((uint)r.L("weenie_Class_Id")),
    };

    /// <summary>The weenie class ids the second pass wants: the entry portals (Destination in the landblock), every lifestone and every vendor.</summary>
    internal static HashSet<uint> TownWeenies(IReadOnlyDictionary<string, SqlTable> tables, ushort landblock)
    {
        var wanted = new HashSet<uint>();
        if (tables.TryGetValue("weenie", out var weenie))
        {
            foreach (var r in weenie.View().Where(r => r.L("type") is VendorType or LifestoneType))
            {
                wanted.Add((uint)r.L("class_Id"));
            }
        }

        if (tables.TryGetValue("weenie_properties_position", out var position))
        {
            foreach (var r in position.View().Where(r => r.L("position_Type") == DestinationPosition && (r.L("obj_Cell_Id") >> 16) == landblock))
            {
                wanted.Add((uint)r.L("object_Id"));
            }
        }

        return wanted;
    }

    /// <summary>
    /// The SQL part of the draft from the pinned world dump's zip, read-only (two streaming passes; nothing is loaded into a database): the first reads the landblock's generators, creatures, entry portals and
    /// the town's tables, the second the town's instances in other landblocks.
    /// </summary>
    /// <param name="zip">The dump's zip as the host opened it (<see cref="WorldSqlDump.ZipParts"/> below the main checkout); seekable (the second pass rewinds it); left open.</param>
    /// <param name="landblock">The landblock's high word.</param>
    /// <param name="arrival">The arrival cell, or null to take the first entry portal's destination (<c>TODO(ground-draft-arrival)</c> names the others).</param>
    /// <param name="home">The landblock of the lifestone to take as the home instead of the one nearest the entry portal, or null.</param>
    public static DraftWorld World(Stream zip, ushort landblock, uint? arrival, ushort? home = null)
    {
        ArgumentNullException.ThrowIfNull(zip);
        var tables = WorldSqlDump.ReadZip(zip, WorldTables(landblock));
        var wanted = TownWeenies(tables, landblock);
        if (wanted.Count > 0)
        {
            if (!zip.CanSeek)
            {
                throw new ArgumentException("the town half reads the dump twice: the zip stream must be seekable", nameof(zip));
            }

            zip.Position = 0;
            var more = WorldSqlDump.ReadZip(zip, InstanceTables(landblock, wanted));
            if (more.TryGetValue("landblock_instance", out var extra) && tables.TryGetValue("landblock_instance", out var instances))
            {
                instances.Rows.AddRange(extra.Rows);
            }
        }

        return World(tables, landblock, arrival, home);
    }

    /// <summary>The outdoor cell a point lies in: <c>x/24*8 + y/24 + 1</c> with whole divisions, in the row's landblock; a row that carries a cell of its own (low word set) keeps it. An outdoor landblock_instance row carries 0xLLLL0000.</summary>
    /// <param name="cell">The row's cell id.</param>
    /// <param name="point">The row's landblock-local point.</param>
    public static uint OutdoorCell(uint cell, Vector3 point)
    {
        if ((cell & 0xFFFF) != 0)
        {
            return cell;
        }

        var gx = Math.Clamp((int)(point.X / 24f), 0, 7);
        var gy = Math.Clamp((int)(point.Y / 24f), 0, 7);
        return (cell & 0xFFFF0000u) | (uint)(gx * 8 + gy + 1);
    }

    /// <summary>A cell's place on the world's metre grid: each landblock is 192 m square (the cell's high two bytes are its x and y).</summary>
    /// <param name="cell">The full cell id.</param>
    /// <param name="point">The landblock-local point.</param>
    public static (double X, double Y) WorldMetres(uint cell, Vector3 point) => ((cell >> 24) * 192.0 + point.X, ((cell >> 16) & 0xFF) * 192.0 + point.Y);

    /// <summary>What one of a ware of Value <paramref name="value"/> costs at the sell multiplier <paramref name="multiplier"/>: <c>max(1, ceil(m x Value - 0.1))</c> with ACE's float arithmetic, as <c>VendorStock.PriceOfStack</c> (Vendor.cs @95ef426d line 583).</summary>
    /// <param name="multiplier">The vendor's SellPrice.</param>
    /// <param name="value">The ware's Value.</param>
    public static int PriceOf(double multiplier, long value)
    {
        var cost = Math.Ceiling((double)((float)multiplier * (float)value) - 0.1);
        return cost < 1 ? 1 : (int)Math.Min(cost, int.MaxValue);
    }

    /// <summary>The level band the targets make: the lowest to the highest level of the creatures a ground fights (not every slot's creature), or null for none.</summary>
    /// <param name="targetLevels">The levels of the target-role creatures.</param>
    public static LevelBand? LevelBandOf(IEnumerable<int> targetLevels)
    {
        ArgumentNullException.ThrowIfNull(targetLevels);
        var levels = targetLevels.ToArray();
        return levels.Length == 0 ? null : new LevelBand(levels.Min(), levels.Max());
    }

    /// <summary>The role the draft proposes for a slot creature: avoid when its Death or Use emote teleports (an exit), target for the rest (a design's avoided classes are set by hand).</summary>
    /// <param name="world">The SQL part.</param>
    /// <param name="wcid">The creature's weenie class id.</param>
    public static GroundRole ProposedRole(DraftWorld world, uint wcid)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.EmoteExits.Any(e => e.Wcid == wcid) ? GroundRole.Avoid : GroundRole.Target;
    }

    /// <summary>The SQL part of the draft from the tables <see cref="WorldTables"/> kept.</summary>
    /// <param name="tables">The kept tables (a missing table is an empty one).</param>
    /// <param name="landblock">The landblock's high word.</param>
    /// <param name="arrival">The arrival cell, or null to take the first entry portal's destination.</param>
    /// <param name="home">The landblock of the lifestone to take as the home, or null for the one nearest the entry portal.</param>
    internal static DraftWorld World(IReadOnlyDictionary<string, SqlTable> tables, ushort landblock, uint? arrival, ushort? home = null)
    {
        IEnumerable<SqlRow> Rows(string table) => tables.TryGetValue(table, out var t) ? t.View() : [];
        var notes = new List<string>();
        var instances = Rows("landblock_instance").Where(r => (r.L("obj_Cell_Id") >> 16) == landblock).ToDictionary(r => (uint)r.L("guid"));
        var profiles = Rows("weenie_properties_generator").GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.OrderBy(r => r.L("id")).ToArray());
        var ticks = Rows("weenie_properties_float").Where(r => r.L("type") == RegenerationIntervalProperty).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First().F("value"));
        var levels = Rows("weenie_properties_int").Where(r => r.L("type") == LevelProperty).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (int)g.First().L("value"));
        var names = Rows("weenie_properties_string").Where(r => r.L("type") == NameProperty).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First().S("value") ?? "(no name)");
        var types = Rows("weenie").ToDictionary(r => (uint)r.L("class_Id"), r => r.L("type"));
        bool IsCreature(uint wcid) => types.GetValueOrDefault(wcid) == CreatureType;
        var destinations = Rows("weenie_properties_position").Where(r => r.L("position_Type") == DestinationPosition).ToArray();
        var destinationOf = destinations.GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (uint)g.First().L("obj_Cell_Id"));
        var children = Rows("landblock_instance_link").Where(r => instances.ContainsKey((uint)r.L("parent_GUID")))
            .GroupBy(r => (uint)r.L("parent_GUID")).ToDictionary(g => g.Key, g => g.Select(r => (uint)r.L("child_GUID")).Order().ToArray());

        var generators = new List<DraftGenerator>();
        var slots = new List<DraftSlot>();
        foreach (var g in instances.Values.Where(r => profiles.ContainsKey((uint)r.L("weenie_Class_Id"))).OrderBy(r => r.L("guid")))
        {
            var guid = (uint)g.L("guid");
            var wcid = (uint)g.L("weenie_Class_Id");
            var cell = (uint)g.L("obj_Cell_Id");
            var rows = profiles[wcid];
            double? delay = rows[0].IsNull("delay") ? null : rows[0].F("delay");
            double? tick = ticks.TryGetValue(wcid, out var t) ? t : null;
            if (delay is null)
            {
                notes.Add($"TODO(ground-draft-delay): generator 0x{guid:X8} (wcid {wcid}): its first profile row's delay is NULL; the draft writes {HandSet}");
            }

            if (tick is null)
            {
                notes.Add($"TODO(ground-draft-tick): generator 0x{guid:X8} (wcid {wcid}) has no RegenerationInterval (PropertyFloat 41); the draft writes {HandSet}");
            }

            var before = slots.Count;
            if (children.TryGetValue(guid, out var linked))
            {
                if (rows.Select(r => r.IsNull("delay") ? -1 : r.F("delay")).Distinct().Count() > 1)
                {
                    notes.Add($"TODO(ground-draft-link-profile): generator 0x{guid:X8} (wcid {wcid}) has {rows.Length} profile rows of different delays; its links take the first row's");
                }

                foreach (var child in linked)
                {
                    if (!instances.TryGetValue(child, out var c))
                    {
                        notes.Add($"TODO(ground-draft-link-outside): generator 0x{guid:X8} links 0x{child:X8}, which is not an instance of landblock 0x{landblock:X4}; left out");
                        continue;
                    }

                    var childWcid = (uint)c.L("weenie_Class_Id");
                    if (profiles.ContainsKey(childWcid) || !IsCreature(childWcid))
                    {
                        continue; // a linked generator is a generator of its own, with its own slots; an object that is not a creature is no hunting slot
                    }

                    slots.Add(new DraftSlot(guid, child, (uint)c.L("obj_Cell_Id"), childWcid));
                }
            }

            foreach (var row in linked is null ? rows : [])
            {
                var made = (uint)row.L("weenie_Class_Id");
                if (made == 0 || (row.L("where_Create") & NotStandingLocations) != 0 || (!profiles.ContainsKey(made) && !IsCreature(made)))
                {
                    continue;
                }

                if (profiles.ContainsKey(made))
                {
                    notes.Add($"TODO(ground-draft-nested): generator 0x{guid:X8}'s profile row {row.L("id")} makes generator wcid {made}, whose slots exist only at run time; left out");
                    continue;
                }

                var rowCell = row.IsNull("obj_Cell_Id") ? 0u : (uint)row.L("obj_Cell_Id");
                if (rowCell == 0 || rowCell >> 16 != landblock)
                {
                    notes.Add($"TODO(ground-draft-scatter): generator 0x{guid:X8}'s profile row {row.L("id")} (wcid {made}) names no cell of the landblock; its slots take the generator's cell 0x{cell:X8}");
                    rowCell = cell;
                }

                for (var i = 0; i < Math.Max(1, row.L("max_Create")); i++)
                {
                    slots.Add(new DraftSlot(guid, 0, rowCell, made));
                }
            }

            if (slots.Count == before)
            {
                notes.Add($"generator 0x{guid:X8} (wcid {wcid}) in 0x{cell:X8} makes no creature (WeenieType {CreatureType}); left out");
                continue;
            }

            generators.Add(new DraftGenerator(guid, wcid, cell, delay, tick));
        }

        var creatures = CreatureStats(Rows, slots.Select(s => s.Wcid).Distinct().Order()
            .Select(w => new DraftCreature(w, names.GetValueOrDefault(w, "(no name)"), levels.TryGetValue(w, out var l) ? l : null)), notes);
        var exits = instances.Values
            .Where(r => destinationOf.TryGetValue((uint)r.L("weenie_Class_Id"), out var to) && to != 0 && to >> 16 != landblock)
            .OrderBy(r => r.L("guid"))
            .Select(r => new DraftPortal((uint)r.L("guid"), (uint)r.L("weenie_Class_Id"), (uint)r.L("obj_Cell_Id"), destinationOf[(uint)r.L("weenie_Class_Id")]))
            .ToArray();
        var exitOrigins = exits.ToDictionary(e => e.Guid, e => { var r = instances[e.Guid]; return new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z")); }); // M6 3 a1: the exit section's place
        Dictionary<uint, int> IntsOf(int type) => Rows("weenie_properties_int").Where(r => r.L("type") == type).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => (int)g.First().L("value"));
        var minLevels = IntsOf(MinLevelProperty);
        var maxLevels = IntsOf(MaxLevelProperty); // M6 3 a1: the entry gate's top and the tie rule
        var bitmasks = IntsOf(PortalBitmaskProperty);
        var entries = destinations
            .Where(r => ((uint)r.L("obj_Cell_Id") >> 16) == landblock)
            .OrderBy(r => r.L("object_Id")).ThenBy(r => r.L("id"))
            .Select(r =>
            {
                var wcid = (uint)r.L("object_Id");
                return new DraftEntry(
                    wcid,
                    names.GetValueOrDefault(wcid, "(no name)"),
                    (uint)r.L("obj_Cell_Id"),
                    new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z")),
                    (float)r.F("angles_W"),
                    (float)r.F("angles_Z"),
                    minLevels.TryGetValue(wcid, out var min) ? min : null)
                {
                    MaxLevel = maxLevels.TryGetValue(wcid, out var max) ? max : null,
                    PortalBitmask = bitmasks.TryGetValue(wcid, out var bits) ? bits : null,
                };
            })
            .ToArray();
        var chosen = arrival is { } want ? entries.FirstOrDefault(e => e.Cell == want) : entries.FirstOrDefault();
        if (arrival is null && entries.Length > 1)
        {
            notes.Add($"TODO(ground-draft-arrival): {entries.Length} portals lead into landblock 0x{landblock:X4} ({string.Join(", ", entries.Select(e => $"wcid {e.Wcid} {e.Name} to 0x{e.Cell:X8}"))}); the draft takes the first, wcid {chosen!.Wcid}; name another's arrival cell to draft from it");
        }

        if (chosen is null)
        {
            notes.Add(arrival is { } named
                ? $"TODO(ground-draft-arrival): no portal's Destination is the arrival cell 0x{named:X8}; the draft takes the cell's origin and heading north"
                : $"TODO(ground-draft-arrival): no portal's Destination lies in landblock 0x{landblock:X4}; name the arrival cell");
        }

        var arrivalPoint = chosen is null ? null : new DraftArrival(chosen.Wcid, chosen.Origin, chosen.HeadingW, chosen.HeadingZ) { Cell = chosen.Cell };
        var emoteExits = EmoteExits(Rows, slots, names);
        var town = chosen is null ? null : BuildTown(Rows, chosen, home, types, names, notes);
        return new DraftWorld(generators, slots, creatures, exits, arrivalPoint, notes) { Entries = entries, Town = town, EmoteExits = emoteExits, ExitOrigins = exitOrigins };
    }

    /// <summary>
    /// M6 3 a1: the slot creatures' stats from their weenies' rows: the six attributes (weenie_properties_attribute, init_Level + level_From_C_P), ACE's current maximum health and Magic Defense
    /// (<see cref="MaxHealthOf"/>, <see cref="MagicDefenseOf"/>) and the damage types of the body-part attacks (weenie_properties_body_part d_Type where d_Val is not 0). A missing row is a named note and no value, never a default.
    /// The seconds between swings are not drafted: <c>TODO(f11-swing-seconds)</c>, one note naming why.
    /// </summary>
    private static DraftCreature[] CreatureStats(Func<string, IEnumerable<SqlRow>> rows, IEnumerable<DraftCreature> creatures, List<string> notes)
    {
        var list = creatures.ToArray();
        var wanted = list.Select(c => c.Wcid).ToHashSet();
        var attributes = rows("weenie_properties_attribute").Where(r => wanted.Contains((uint)r.L("object_Id")))
            .GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.GroupBy(r => (int)r.L("type")).ToDictionary(t => t.Key, t => (uint)(t.First().L("init_Level") + t.First().L("level_From_C_P"))));
        var vitals = rows("weenie_properties_attribute_2nd").Where(r => r.L("type") == MaxHealthVital && wanted.Contains((uint)r.L("object_Id")))
            .GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => ((uint)g.First().L("init_Level"), (uint)g.First().L("level_From_C_P")));
        var skills = rows("weenie_properties_skill").Where(r => r.L("type") == MagicDefenseSkill && wanted.Contains((uint)r.L("object_Id")))
            .GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => ((uint)g.First().L("init_Level"), (uint)g.First().L("level_From_P_P"), (int)g.First().L("s_a_c")));
        var damage = rows("weenie_properties_body_part").Where(r => r.L("d_Val") != 0 && wanted.Contains((uint)r.L("object_Id")))
            .GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.Aggregate(0u, (m, r) => m | (uint)r.L("d_Type")));
        var result = new DraftCreature[list.Length];
        for (var i = 0; i < list.Length; i++)
        {
            var c = list[i];
            var own = attributes.GetValueOrDefault(c.Wcid) ?? [];
            uint? At(int type) => own.TryGetValue(type, out var v) ? v : null;
            var missing = new[] { StrengthAttribute, EnduranceAttribute, QuicknessAttribute, CoordinationAttribute, FocusAttribute, SelfAttribute }.Where(t => At(t) is null).ToArray();
            GroundAttributes? six = null;
            if (missing.Length > 0)
            {
                notes.Add($"TODO(ground-draft-attributes): creature {c.Wcid} {c.Name} has no weenie_properties_attribute row of type {string.Join(", ", missing)}; its attributes are not drafted");
            }
            else
            {
                six = new GroundAttributes((int)At(StrengthAttribute)!.Value, (int)At(EnduranceAttribute)!.Value, (int)At(CoordinationAttribute)!.Value, (int)At(QuicknessAttribute)!.Value, (int)At(FocusAttribute)!.Value, (int)At(SelfAttribute)!.Value);
            }

            int? health = null;
            if (vitals.TryGetValue(c.Wcid, out var vital) && At(EnduranceAttribute) is { } endurance)
            {
                health = MaxHealthOf(vital.Item1, vital.Item2, endurance);
            }
            else
            {
                notes.Add($"TODO(ground-draft-health): creature {c.Wcid} {c.Name} has no weenie_properties_attribute_2nd row of type {MaxHealthVital} (MaxHealth) or no Endurance row; its max_health is not drafted");
            }

            int? magicDefense = null;
            if (!skills.TryGetValue(c.Wcid, out var skill))
            {
                notes.Add($"TODO(ground-draft-magic-defense): creature {c.Wcid} {c.Name} has no weenie_properties_skill row of type {MagicDefenseSkill} (Magic Defense); its magic_defence is not drafted");
            }
            else if (skill.Item3 < TrainedSkill)
            {
                notes.Add($"TODO(ground-draft-magic-defense): creature {c.Wcid} {c.Name}: its Magic Defense is not trained (s_a_c {skill.Item3}); ACE adds the formula share only when the SkillTable says it is usable untrained (CreatureSkill.IsUsable, CreatureSkill.cs:48-63 @95ef426d), which the SQL does not say; its magic_defence is not drafted");
            }
            else if (At(FocusAttribute) is { } focus && At(SelfAttribute) is { } self)
            {
                magicDefense = MagicDefenseOf(skill.Item1, skill.Item2, focus, self);
            }

            uint? types = damage.TryGetValue(c.Wcid, out var d) && d != 0 ? d : null;
            if (types is null)
            {
                notes.Add($"TODO(ground-draft-damage-types): creature {c.Wcid} {c.Name} has no weenie_properties_body_part row with a d_Val; its damage_types are not drafted (a wielded weapon's damage is not in the body parts)");
            }

            result[i] = c with { Attributes = six, MaxHealth = health, MagicDefense = magicDefense, DamageTypes = types };
        }

        if (result.Length > 0)
        {
            notes.Add($"TODO(f11-swing-seconds): swing_s is not drafted for {string.Join(", ", result.Select(c => $"{c.Wcid} {c.Name}"))}: ACE times a monster's next melee swing as the swing animation's length plus a random delay up to its PowerupTime (PropertyFloat 34, 1 s when absent; Monster_Melee.cs:151-156 @95ef426d), the animation being MotionTable.GetAnimationLength over the creature's motion table for a maneuver its CombatTable picks at random by attack height (Monster_Melee.cs:164-215, 340-354) at GetAnimSpeed's Quickness-based speed (Creature_Combat.cs:510-521); the repository reads the motion table but not the combat maneuver table, so the draft cannot pick the maneuvers, and retail 11.6096's client does none of this (every attack roll and timing is server-side: docs/research/game-logic.md, the acclient-AI-RE reading); a profile's swing_s is a measure, named in its source");
        }

        return result;
    }

    /// <summary>M6 3 a1: ACE's current maximum health of a creature: the vital's start plus its ranks plus Endurance / 2 rounded half up (<see cref="MaxHealthFormula"/>; CreatureVital.cs:104-131 @95ef426d, with no enchantment).</summary>
    /// <param name="start">weenie_properties_attribute_2nd type 1 init_Level.</param>
    /// <param name="ranks">Its level_From_C_P.</param>
    /// <param name="endurance">The Endurance's current value.</param>
    public static int MaxHealthOf(uint start, uint ranks, uint endurance)
    {
        MaxHealthFormula.TryCalculate(endurance, 0, out var part);
        return checked((int)(start + ranks + part));
    }

    /// <summary>M6 3 a1: ACE's current Magic Defense of a creature whose skill is trained: init_Level plus its ranks plus (Focus + Self) / 7 rounded half up (<see cref="MagicDefenseFormula"/>; CreatureSkill.cs:154-163 @95ef426d).</summary>
    /// <param name="start">weenie_properties_skill type 15 init_Level.</param>
    /// <param name="ranks">Its level_From_P_P.</param>
    /// <param name="focus">The Focus's current value.</param>
    /// <param name="self">The Self's current value.</param>
    public static int MagicDefenseOf(uint start, uint ranks, uint focus, uint self)
    {
        MagicDefenseFormula.TryCalculate(focus, self, out var part);
        return checked((int)(start + ranks + part));
    }

    /// <summary>
    /// M6 3 a1: the tie point in front of a portal: <see cref="TieFrontMetres"/> along its facing (the +Y axis turned by the row's yaw quaternion), the z to the centimetre (the den's row z 30.005 stands at 30.01), in the
    /// outdoor cell the point lies in (<see cref="OutdoorCell"/>; an indoor row keeps its own cell, which a 3 m step may leave: named in the draft's source).
    /// </summary>
    /// <param name="portal">The portal's instance.</param>
    public static (uint Cell, Vector3 Point) FrontOf(DraftPlace portal)
    {
        ArgumentNullException.ThrowIfNull(portal);
        var yaw = 2.0 * Math.Atan2(portal.HeadingZ, portal.HeadingW);
        var x = portal.Point.X + (TieFrontMetres * -Math.Sin(yaw));
        var y = portal.Point.Y + (TieFrontMetres * Math.Cos(yaw));
        var z = (float)Math.Round((decimal)portal.Point.Z, 2, MidpointRounding.AwayFromZero);
        var point = new Vector3((float)x, (float)y, z);
        var rowCell = (portal.Cell & 0xFFFF) < 0x100 ? portal.Cell & 0xFFFF0000u : portal.Cell;
        return (OutdoorCell(rowCell, point), point);
    }

    /// <summary>The slot creatures whose Death or Use emote teleports: a CastSpell or CastSpellInstant action whose spell the dump's spell table gives a destination cell (a Portal Sending spell), or a TeleportTarget or TeleportSelf action.</summary>
    private static DraftEmoteExit[] EmoteExits(Func<string, IEnumerable<SqlRow>> rows, IReadOnlyList<DraftSlot> slots, IReadOnlyDictionary<uint, string> names)
    {
        var slotWcids = slots.Select(s => s.Wcid).ToHashSet();
        var emotes = rows("weenie_properties_emote").Where(r => slotWcids.Contains((uint)r.L("object_Id")) && r.L("category") is DeathCategory or UseCategory)
            .ToDictionary(r => (uint)r.L("id"), r => ((uint)r.L("object_Id"), (uint)r.L("category")));
        var spells = rows("spell").Where(r => r.L("position_Obj_Cell_ID") != 0).ToDictionary(r => (uint)r.L("id"), r => (Name: r.S("name") ?? "(no name)", Cell: (uint)r.L("position_Obj_Cell_ID")));
        var found = new List<DraftEmoteExit>();
        foreach (var a in rows("weenie_properties_emote_action").OrderBy(r => r.L("emote_Id")).ThenBy(r => r.L("order")))
        {
            if (!emotes.TryGetValue((uint)a.L("emote_Id"), out var emote))
            {
                continue;
            }

            var type = (uint)a.L("type");
            uint spell = 0, destination;
            string spellName;
            if (type is CastSpellAction or CastSpellInstantAction)
            {
                spell = (uint)a.L("spell_Id");
                if (!spells.TryGetValue(spell, out var s))
                {
                    continue; // a spell with no destination cell is no teleport (a bolt, a buff)
                }

                (spellName, destination) = s;
            }
            else
            {
                destination = a.IsNull("obj_Cell_Id") ? 0 : (uint)a.L("obj_Cell_Id");
                spellName = type == TeleportSelfAction ? "TeleportSelf" : "TeleportTarget";
            }

            var cells = slots.Where(s => s.Wcid == emote.Item1).Select(s => s.Cell).Distinct().Order().ToArray();
            found.Add(new DraftEmoteExit(emote.Item1, names.GetValueOrDefault(emote.Item1, "(no name)"), emote.Item2, type, spell, spellName, destination, cells));
        }

        return [.. found.DistinctBy(e => (e.Wcid, e.Category, e.ActionType, e.Spell, e.Destination)).OrderBy(e => e.Wcid).ThenBy(e => e.Category)];
    }

    /// <summary>
    /// The town half: the entry portal's instances (the first is the portal), the lifestones nearest it (outdoor rows only: an indoor row's landblock is a dungeon's or a building's, so its world distance means nothing;
    /// or, when <paramref name="home"/> names a landblock, that landblock's lifestones) and the vendors near the nearest lifestone, each with what it buys, what it sells and the prices at its own multiplier.
    /// </summary>
    private static DraftTown BuildTown(Func<string, IEnumerable<SqlRow>> rows, DraftEntry entry, ushort? home, IReadOnlyDictionary<uint, long> types, IReadOnlyDictionary<uint, string> names, List<string> notes)
    {
        var all = rows("landblock_instance").ToArray();
        DraftPlace PlaceOf(SqlRow r)
        {
            var point = new Vector3((float)r.F("origin_X"), (float)r.F("origin_Y"), (float)r.F("origin_Z"));
            return new DraftPlace((uint)r.L("guid"), (uint)r.L("weenie_Class_Id"), OutdoorCell((uint)r.L("obj_Cell_Id"), point), point) { HeadingW = (float)r.F("angles_W", 1), HeadingZ = (float)r.F("angles_Z") };
        }

        double Metres(DraftPlace from, DraftPlace to)
        {
            var (ax, ay) = WorldMetres(from.Cell, from.Point);
            var (bx, by) = WorldMetres(to.Cell, to.Point);
            return Math.Sqrt(((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by)));
        }

        var portals = all.Where(r => (uint)r.L("weenie_Class_Id") == entry.Wcid).OrderBy(r => r.L("guid")).Select(PlaceOf).ToArray();
        if (portals.Length == 0)
        {
            notes.Add($"TODO(ground-draft-portal): the entry portal wcid {entry.Wcid} has no landblock_instance row; the town half is empty");
            return new DraftTown(entry, [], [], []);
        }

        if (portals.Length > 1)
        {
            notes.Add($"TODO(ground-draft-portal): wcid {entry.Wcid} stands in {portals.Length} places ({string.Join(", ", portals.Select(p => $"0x{p.Guid:X8} in 0x{p.Cell:X8}"))}); the draft takes the first");
        }

        var portal = portals[0];
        var lifestones = all
            .Where(r => types.GetValueOrDefault((uint)r.L("weenie_Class_Id")) == LifestoneType)
            .Where(r => home is { } h ? ((uint)r.L("obj_Cell_Id") >> 16) == h : ((uint)r.L("obj_Cell_Id") & 0xFFFF) < 0x100)
            .Select(PlaceOf)
            .Select(p => new DraftLifestone(p, Metres(portal, p)))
            .OrderBy(l => l.Metres).ThenBy(l => l.Place.Guid)
            .Take(ListedLifestones)
            .ToArray();
        if (lifestones.Length == 0)
        {
            notes.Add(home is { } missing
                ? $"TODO(ground-draft-home): no lifestone stands in landblock 0x{missing:X4}"
                : $"TODO(ground-draft-home): no outdoor lifestone in the world; the town half has no home");
            return new DraftTown(entry, portals, [], []);
        }

        var lifestone = lifestones[0].Place;
        var lbx = lifestone.Cell >> 24;
        var lby = (lifestone.Cell >> 16) & 0xFF;
        var floats = rows("weenie_properties_float").Where(r => r.L("type") == SellPriceProperty).GroupBy(r => (uint)r.L("object_Id")).ToDictionary(g => g.Key, g => g.First().F("value"));
        var ints = rows("weenie_properties_int").Where(r => r.L("type") is ValueProperty or MerchandiseItemTypesProperty).ToLookup(r => (uint)r.L("object_Id"));
        long IntOf(uint wcid, int type) => ints[wcid].Where(r => r.L("type") == type).Select(r => r.L("value")).FirstOrDefault();
        var shops = rows("weenie_properties_create_list").Where(r => (r.L("destination_Type") & ShopDestination) != 0).OrderBy(r => r.L("id")).ToLookup(r => (uint)r.L("object_Id"));
        var vendors = new List<DraftVendor>();
        foreach (var r in all.Where(r => types.GetValueOrDefault((uint)r.L("weenie_Class_Id")) == VendorType).OrderBy(r => r.L("guid")))
        {
            var place = PlaceOf(r);
            var metres = Metres(lifestone, place);
            var near = Math.Abs((int)(place.Cell >> 24) - (int)lbx) <= 1 && Math.Abs((int)((place.Cell >> 16) & 0xFF) - (int)lby) <= 1;
            if (!near && metres > TownRadiusMetres)
            {
                continue;
            }

            double? multiplier = floats.TryGetValue(place.Wcid, out var m) ? m : null;
            if (multiplier is null)
            {
                notes.Add($"TODO(ground-draft-vendor-price): vendor wcid {place.Wcid} has no SellPrice (PropertyFloat 38); ACE disables such a vendor (Vendor.cs @95ef426d line 114); its prices are 0");
            }

            var wares = shops[place.Wcid].Select(w =>
            {
                var ware = (uint)w.L("weenie_Class_Id");
                var value = IntOf(ware, ValueProperty);
                return new DraftWare(ware, names.GetValueOrDefault(ware, "(no name)"), value, multiplier is { } mm ? PriceOf(mm, value) : 0);
            }).ToArray();
            vendors.Add(new DraftVendor(place, names.GetValueOrDefault(place.Wcid, "(no name)"), (uint)IntOf(place.Wcid, MerchandiseItemTypesProperty), multiplier, metres, near, wares));
        }

        if (vendors.Count == 0)
        {
            notes.Add($"TODO(ground-draft-vendors): no vendor within {TownRadiusMetres} m or the 3x3 landblocks of the lifestone 0x{lifestone.Guid:X8}");
        }

        return new DraftTown(entry, portals, lifestones, [.. vendors.OrderBy(v => v.Metres).ThenBy(v => v.Place.Guid)]);
    }

    /// <summary>
    /// A first-draft tour (the tour's own logic is the tour's, M5 item 2 g6): the given hunt cells, storey by storey in the storeys' order, each storey's cells nearest first by straight-line
    /// distance between cell origins, from the arrival and then from the previous storey's last cell. A hunt cell in no storey is left out.
    /// </summary>
    /// <param name="all">Every cell of the landblock (for the origins).</param>
    /// <param name="storeys">The storeys, in the tour's order.</param>
    /// <param name="arrival">The arrival cell.</param>
    /// <param name="huntCells">The cells to visit (repeats are visited once).</param>
    /// <param name="walkMetres">
    /// M6 14 pits: the walk between two cells (<see cref="GroundTour.RoomMetres"/> over the ground's room graph, jumps counted), or null: the straight line between cell origins (every draft before the Abode's
    /// jump). Across a pit the straight line is short and the walk is not, so a tour drafted on the line would cross back and forth; with the walk it ends one side before it jumps.
    /// </param>
    public static IReadOnlyList<uint> FirstTour(LandblockCellSet all, IReadOnlyList<GroundStorey> storeys, uint arrival, IEnumerable<uint> huntCells, Func<uint, uint, double>? walkMetres = null)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(storeys);
        Vector3 At(uint cell) => all.GetCell(cell)?.Frame.Origin ?? throw new ArgumentException($"0x{cell:X8} is not a cell of the landblock", nameof(huntCells));
        var wanted = huntCells.Distinct().ToHashSet();
        var tour = new List<uint>();
        var here = arrival;
        foreach (var storey in storeys)
        {
            var left = storey.Cells.Cells.Where(wanted.Contains).ToList();
            while (left.Count > 0)
            {
                var from = here;
                var next = walkMetres is null
                    ? left.OrderBy(c => Vector3.DistanceSquared(At(from), At(c))).ThenBy(c => c).First()
                    : left.OrderBy(c => walkMetres(from, c)).ThenBy(c => c).First();
                left.Remove(next);
                tour.Add(next);
                here = next;
            }
        }

        return tour;
    }

    /// <summary>
    /// The draft profile's JSON (the catalog's format, GroundCatalog's remarks): the dats' and the SQL's sections filled and sourced, the town half included (the arrival, the portal, the home lifestone and the vendors near it,
    /// each vendor with an empty <c>sells</c>: the wares are listed in <see cref="Summary"/>); every other hand-set value the <see cref="HandSet"/> marker, which the catalog refuses at load naming the field. The roles are
    /// proposed (<see cref="ProposedRole"/>: avoid for an exit by emote, target for the rest) and the level band is the proposed targets' levels only; the no-go cells are the exits' (portal objects and emote exits); the
    /// tour is <see cref="FirstTour"/> over every slot cell that is not an exit's.
    /// </summary>
    /// <param name="key">The profile's key (its file name without <c>.json</c>).</param>
    /// <param name="all">Every cell of the landblock.</param>
    /// <param name="cells">The dats' part.</param>
    /// <param name="world">The SQL's part.</param>
    public static string Json(string key, LandblockCellSet all, DraftCells cells, DraftWorld world)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(world);
        static string Hex(uint v) => $"0x{v:X8}";
        var lb = $"0x{cells.Landblock:X4}";
        var exits = world.ExitCells.ToHashSet();
        var tour = FirstTour(all, cells.Storeys, cells.Arrival, world.Slots.Select(s => s.Cell).Where(c => !exits.Contains(c)));
        var slotLevels = world.Creatures.Where(c => c.Level is not null).Select(c => c.Level!.Value).ToArray();
        var targetBand = LevelBandOf(world.Creatures.Where(c => c.Level is not null && ProposedRole(world, c.Wcid) == GroundRole.Target).Select(c => c.Level!.Value));
        var slotRange = slotLevels.Length == 0 ? "none" : $"{slotLevels.Min()} to {slotLevels.Max()}";
        var town = world.Town;
        static string Pt(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}");
        var exitText = world.Exits.Count == 0 ? "none in the landblock" : string.Join(", ", world.Exits.Select(e => $"{Hex(e.Cell)} (wcid {e.Wcid} to {Hex(e.Destination)})"));
        var emoteText = world.EmoteExits.Count == 0
            ? string.Empty
            : "; exits by emote (a creature whose Death or Use emote teleports, its slot cells): " + string.Join(", ", world.EmoteExits.Select(e => $"wcid {e.Wcid} {e.Name}, {e.CategoryName} emote casts {(e.Spell == 0 ? e.SpellName : $"'{e.SpellName}' (spell {e.Spell})")} to {Hex(e.Destination)}, in {string.Join("/", e.Cells.Select(Hex))}"));
        var origin = world.Arrival?.Origin ?? all.GetCell(cells.Arrival)!.Frame.Origin;
        var statsSource = "M6 3 a1, the target stats (each from the weenie's rows; a missing row is a draft note and no value): max_health is ACE's CreatureVital.MaxValue (CreatureVital.cs:104-131 @95ef426d), weenie_properties_attribute_2nd type 1 init_Level + level_From_C_P + Endurance / 2 rounded half up (AttributeFormula.cs:55-71), the health ACE spawns a creature with whatever the row's current_Level says (Creature.cs:132-135); magic_defence is ACE's CreatureSkill.Current (CreatureSkill.cs:154-163), weenie_properties_skill type 15 init_Level + level_From_P_P + (Focus + Self) / 7 for a trained skill (s_a_c 2 or 3); attributes are weenie_properties_attribute types 1 to 6, init_Level + level_From_C_P; damage_types is the OR of weenie_properties_body_part d_Type over the parts with a d_Val (Monster_Melee.cs:590-615); swing_s is not drafted (TODO(f11-swing-seconds), the notes)";
        var storeyNote = $"{cells.Walkable.Count} walkable cells in {cells.Storeys.Count} storeys of {cells.Total}; {cells.Floorless.Count} floorless and {cells.Pockets.Count} unreached pocket cells left out";

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("key", key);
            w.WriteString("name", $"{HandSet}: the ground's name (landblock {lb})");
            w.WriteString("kind", "dungeon");
            w.WriteStartObject("sources");
            w.WriteString("landblocks", $"the dats: landblock {lb} (GroundDraft)");
            w.WriteString("arrival", world.Arrival is { } a
                ? $"the pinned world SQL v0.9.296: weenie {a.Wcid}'s Destination (PositionType 2) is the arrival cell {Hex(cells.Arrival)} (GroundDraft)"
                : $"{HandSet}: no portal's Destination is {Hex(cells.Arrival)}; the draft wrote the cell's origin and heading north");
            w.WriteString("walk", $"the draft's doorway lean {cells.DoorwayLean.ToString("0.###", CultureInfo.InvariantCulture)}; the leg's stop rules are {HandSet}");
            w.WriteString("storeys", $"the dats (GroundDraft.Cells): every cell the arrival {Hex(cells.Arrival)} reaches over the doorways RoomGraph.IsPassable accepts at lean {cells.DoorwayLean.ToString("0.###", CultureInfo.InvariantCulture)}, grouped by origin z; {storeyNote}");
            w.WriteString("no_go", $"the pinned world SQL v0.9.296: the exit portals' cells (a portal whose Destination is outside {lb}): {exitText}{emoteText}; the rest is {HandSet}");
            w.WriteString("targets", $"the pinned world SQL v0.9.296: every slot's weenie with its Name (PropertyString 1) and Level (PropertyInt 25); the roles are proposed (GroundDraft.ProposedRole): avoid for a creature whose emote is an exit, target for the rest; the design's avoided classes and any kill-first role are {HandSet}; {statsSource}");
            w.WriteString("level_band", $"the pinned world SQL v0.9.296: the proposed targets' levels only ({(targetBand is { } tb ? $"{tb.Min} to {tb.Max}" : HandSet)}); the slot creatures' range is {slotRange}");
            w.WriteString("portal", town?.Portal is { } np && !town.Entry.Tieable
                ? $"none: the entry portal, weenie {town.Entry.Wcid} {town.Entry.Name} (guid {Hex(np.Guid)}), has PortalBitmask 0x{town.Entry.PortalBitmask:X} (PropertyInt 111), which carries NoRecall (0x20, ACE.Entity/Enum/PortalBitmask.cs:15 @95ef426d), and ACE's Portal.NoTie is NoRecall (Portal_Properties.cs:14-16), so it cannot be tied and the ground has no tie record (the entry section holds its place)"
                : town?.Portal is { } tp
                ? $"the pinned world SQL v0.9.296 (GroundDraft's town half): weenie {town.Entry.Wcid} {town.Entry.Name}'s landblock_instance row, guid {Hex(tp.Guid)} at ({Pt(tp.Point)}) in landblock 0x{tp.Cell >> 16:X4}, in the outdoor cell its point lies in, {Hex(tp.Cell)} (x/24*8 + y/24 + 1 with whole divisions); {town.Portals.Count} instance(s) of the weenie; Primary Portal Tie {PrimaryPortalTieSpell} and Recall {PrimaryPortalRecallSpell} are the global spell ids; {HandSet}: the tie and the recall are not measured live (TODO(m6-ground-live-facts)); wcid and origin are the row's; front_cell and front_point are the tie point 3 m along the portal's facing (the row's quaternion, the z to the centimetre; GroundDraft.FrontOf, M6 3 a1), derived the way the den's W0 stand reads against its row (3 m along the facing to within 5 cm)"
                : HandSet);
            w.WriteString("home", town?.Lifestone is { } tl
                ? $"the pinned world SQL v0.9.296 (GroundDraft's town half): the lifestone nearest the portal ({tl.Metres:0} m), guid {Hex(tl.Place.Guid)} at ({Pt(tl.Place.Point)}) in the outdoor cell its point lies in, {Hex(tl.Place.Cell)}; {HandSet}: the town's name, and whether this is the town the ground's characters live in; the point is the lifestone's own, not a standing point measured beside it (TODO(m6-ground-live-facts))"
                : HandSet);
            w.WriteString("vendors", town is { Vendors.Count: > 0 }
                ? $"{HandSet}: the vendors the ground uses; the draft lists the {town.Vendors.Count} vendors within {TownRadiusMetres:0} m of the lifestone or in the 3x3 landblocks around it (the pinned world SQL v0.9.296: each landblock_instance row's guid, cell and point, int property 74 MerchandiseItemTypes, the create-list rows with destination Shop, and the prices at the vendor's own float property 38 as max(1, ceil(m x Value - 0.1))); sells is empty: choose the wares from the summary's lists; a stop cell is the vendor's own cell, not walked (TODO(m6-ground-live-facts))"
                : HandSet);
            w.WriteString("spell_plan", HandSet);
            w.WriteString("components", HandSet);
            w.WriteString("supplies", $"{HandSet}: the stock lines (keep, trip_under, stop_under from the burn), the kit, the burden line and the purse (m5-plan.md D48, D49); a line's vendor and price are the vendors' sells, so choose the wares from the town's lists in the summary and add each to its vendor's sells");
            w.WriteString("hold_spots", HandSet);
            w.WriteString("respawn","the pinned world SQL v0.9.296: every generator instance of the landblock, its delay from its first profile row (weenie_properties_generator) and its tick from RegenerationInterval (PropertyFloat 41); its slots are its landblock_instance_link children (cell, wcid), or its profile rows' creatures");
            w.WriteString("tour", $"a first draft (GroundDraft.FirstTour): every slot cell but the exits', storey by storey, nearest first by cell origin; {HandSet}");
            w.WriteString("outdoor", "none: a dungeon");
            w.WriteEndObject();
            w.WriteStartArray("landblocks");
            w.WriteStringValue(lb);
            w.WriteEndArray();
            w.WriteStartObject("arrival");
            w.WriteString("cell", Hex(cells.Arrival));
            w.WriteStartObject("origin");
            w.WriteNumber("x", Math.Round(origin.X, 4));
            w.WriteNumber("y", Math.Round(origin.Y, 4));
            w.WriteNumber("z", Math.Round(origin.Z, 4));
            w.WriteEndObject();
            w.WriteStartObject("heading");
            w.WriteNumber("w", Math.Round(world.Arrival?.HeadingW ?? 1f, 6));
            w.WriteNumber("z", Math.Round(world.Arrival?.HeadingZ ?? 0f, 6));
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("walk");
            w.WriteNumber("doorway_lean", Math.Round(cells.DoorwayLean, 3));
            w.WriteString("stop_within_m", HandSet);
            w.WriteString("stop_dwell_s", HandSet);
            w.WriteString("leg_s", HandSet);
            w.WriteEndObject();
            w.WriteStartArray("storeys");
            foreach (var s in cells.Storeys)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                w.WriteNumber("z_min", s.ZMin);
                w.WriteNumber("z_max", s.ZMax);
                w.WriteStartArray("cells");
                foreach (var c in s.Cells.Cells)
                {
                    w.WriteStringValue(Hex(c));
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("no_go");
            foreach (var c in world.ExitCells)
            {
                w.WriteStringValue(Hex(c));
            }

            w.WriteEndArray();
            w.WriteStartArray("targets");
            foreach (var c in world.Creatures)
            {
                w.WriteStartObject();
                w.WriteNumber("wcid", c.Wcid);
                w.WriteString("name", c.Name);
                if (c.Level is { } level)
                {
                    w.WriteNumber("level", level);
                }
                else
                {
                    w.WriteString("level", $"{HandSet}: the dump has no Level");
                }

                w.WriteString("role", ProposedRole(world, c.Wcid) == GroundRole.Target ? "target" : "avoid");
                if (c.MaxHealth is { } health)
                {
                    w.WriteNumber("max_health", health);
                }

                if (c.MagicDefense is { } magicDefense)
                {
                    w.WriteNumber("magic_defence", magicDefense);
                }

                if (c.Attributes is { } six)
                {
                    w.WriteStartObject("attributes");
                    w.WriteNumber("strength", six.Strength);
                    w.WriteNumber("endurance", six.Endurance);
                    w.WriteNumber("coordination", six.Coordination);
                    w.WriteNumber("quickness", six.Quickness);
                    w.WriteNumber("focus", six.Focus);
                    w.WriteNumber("self", six.Self);
                    w.WriteEndObject();
                }

                if (c.DamageTypes is { } damage)
                {
                    w.WriteString("damage_types", $"0x{damage:X4}");
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("level_band");
            if (targetBand is { } band)
            {
                w.WriteNumber("min", band.Min);
                w.WriteNumber("max", band.Max);
            }
            else
            {
                w.WriteString("min", HandSet);
                w.WriteString("max", HandSet);
            }

            w.WriteEndObject();
            if (town?.Portal is not null && !town.Entry.Tieable)
            {
                w.WriteNull("portal"); // M6 3 a1: a NoRecall portal cannot be tied, so there is no tie record (the catalog refuses one beside entry.tieable false)
            }
            else if (town?.Portal is { } portal)
            {
                var (frontCell, front) = FrontOf(portal);
                w.WriteStartObject("portal");
                w.WriteString("guid", Hex(portal.Guid));
                w.WriteString("cell", Hex(portal.Cell));
                w.WriteNumber("tie_spell", PrimaryPortalTieSpell);
                w.WriteNumber("recall_spell", PrimaryPortalRecallSpell);
                w.WriteNumber("wcid", portal.Wcid);
                WritePoint(w, "origin", portal.Point);
                w.WriteString("front_cell", Hex(frontCell)); // M6 3 a1: the tie point 3 m along the portal's facing (FrontOf), as the hand-set profiles derived it
                WritePoint(w, "front_point", front);
                w.WriteEndObject();
            }
            else
            {
                w.WriteString("portal", HandSet);
            }

            WriteEntry(w, town);
            WriteExit(w, world, cells.Arrival, origin);

            if (town?.Lifestone is { } home)
            {
                w.WriteStartObject("home");
                w.WriteString("town", $"{HandSet}: the town's name");
                w.WriteString("lifestone_cell", Hex(home.Place.Cell));
                WritePoint(w, "lifestone_point", home.Place.Point);
                w.WriteEndObject();
            }
            else
            {
                w.WriteString("home", HandSet);
            }

            if (town is { Vendors.Count: > 0 })
            {
                w.WriteStartArray("vendors");
                foreach (var v in town.Vendors)
                {
                    w.WriteStartObject();
                    w.WriteString("name", v.Name);
                    w.WriteNumber("wcid", v.Place.Wcid);
                    w.WriteString("guid", Hex(v.Place.Guid));
                    w.WriteString("cell", Hex(v.Place.Cell));
                    WritePoint(w, "point", v.Place.Point);
                    w.WriteString("stop_cell", Hex(v.Place.Cell));
                    if (v.StopPoint is { } stop)
                    {
                        WritePoint(w, "stop_point", stop);
                    }

                    w.WriteString("buys_item_types", Hex(v.BuysItemTypes));
                    w.WriteStartArray("sells");
                    w.WriteEndArray();
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }
            else
            {
                w.WriteString("vendors", HandSet);
            }

            foreach (var name in new[] { "spell_plan", "components", "supplies", "hold_spots" })
            {
                w.WriteString(name, HandSet);
            }

            w.WriteStartObject("respawn");
            w.WriteStartArray("generators");
            foreach (var g in world.Generators)
            {
                w.WriteStartObject();
                w.WriteString("guid", Hex(g.Guid));
                w.WriteString("cell", Hex(g.Cell));
                if (g.DelaySeconds is { } delay)
                {
                    w.WriteNumber("delay_s", delay);
                }
                else
                {
                    w.WriteString("delay_s", HandSet);
                }

                if (g.TickSeconds is { } tick)
                {
                    w.WriteNumber("tick_s", tick);
                }
                else
                {
                    w.WriteString("tick_s", HandSet);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartArray("slots");
            foreach (var s in world.Slots)
            {
                w.WriteStartObject();
                w.WriteString("generator", Hex(s.Generator));
                w.WriteString("cell", Hex(s.Cell));
                w.WriteNumber("wcid", s.Wcid);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteStartArray("tour");
            foreach (var c in tour)
            {
                w.WriteStringValue(Hex(c));
            }

            w.WriteEndArray();
            w.WriteNull("outdoor");
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>M6 3 a1: the draft's <c>entry</c> section from the chosen entry and its instance (its place, the front point, the gate and the tie rule), each value's source in its <c>source</c>; the marker when the town half has no portal.</summary>
    private static void WriteEntry(Utf8JsonWriter w, DraftTown? town)
    {
        if (town?.Portal is not { } portal)
        {
            w.WriteString("entry", $"{HandSet}: the entry portal has no landblock_instance row (the draft's notes)");
            return;
        }

        var e = town.Entry;
        var (frontCell, front) = FrontOf(portal);
        w.WriteStartObject("entry");
        w.WriteNumber("wcid", e.Wcid);
        w.WriteString("guid", $"0x{portal.Guid:X8}");
        w.WriteString("cell", $"0x{portal.Cell:X8}");
        WritePoint(w, "origin", portal.Point);
        w.WriteString("front_cell", $"0x{frontCell:X8}");
        WritePoint(w, "front_point", front);
        if (e.MinLevel is { } min)
        {
            w.WriteNumber("min_level", min);
        }
        else
        {
            w.WriteNull("min_level");
        }

        if (e.MaxLevel is { } max)
        {
            w.WriteNumber("max_level", max);
        }
        else
        {
            w.WriteNull("max_level");
        }

        w.WriteBoolean("tieable", e.Tieable);
        var bits = e.PortalBitmask is { } b ? $"PortalBitmask 0x{b:X} (PropertyInt 111){((b & NoRecallBit) != 0 ? ", which carries NoRecall (0x20)" : ", no NoRecall bit")}" : "no PortalBitmask row (PropertyInt 111; ACE reads Unrestricted, Portal_Properties.cs:10 @95ef426d)";
        w.WriteString("source", string.Create(CultureInfo.InvariantCulture,
            $"the pinned world SQL v0.9.296 (GroundDraft, M6 3 a1): weenie {e.Wcid} {e.Name}, whose Destination (weenie_properties_position, PositionType 2) is 0x{e.Cell:X8}; its landblock_instance row guid 0x{portal.Guid:X8} at the row's origin, in the cell its point lies in (0x{portal.Cell:X8}, x/24*8 + y/24 + 1 for an outdoor row); the front point {TieFrontMetres:0} m along the row's facing (quaternion w {portal.HeadingW:0.######}, z {portal.HeadingZ:0.######}), z to the centimetre, in its cell (GroundDraft.FrontOf); min_level from weenie_properties_int type 86 (MinLevel){(e.MinLevel is null ? ", none" : string.Empty)}, max_level from type 87 (MaxLevel){(e.MaxLevel is null ? ", none" : string.Empty)}; tieable from {bits}: ACE's Portal.NoTie is NoRecall (ACE.Server/WorldObjects/Portal_Properties.cs:14-16 @95ef426d; the flag ACE.Entity/Enum/PortalBitmask.cs:15)"));
        w.WriteEndObject();
    }

    /// <summary>
    /// M6 3 a1: the draft's <c>exit</c> section from the exits by portal object: none is an exit by <c>recall</c> (the exits by emote are named in the source, avoided); one or more is a <c>portal</c> exit, the one nearest the arrival
    /// (the others named in the source and the notes), with its straight-line distance from the arrival as the <c>from_arrival_m</c> hint (a walk over the room graph is travel's, item 4).
    /// </summary>
    private static void WriteExit(Utf8JsonWriter w, DraftWorld world, uint arrivalCell, Vector3 arrival)
    {
        var emotes = world.EmoteExits.Count == 0
            ? "no exit by emote"
            : "the exits by emote are avoided creatures, not a way out the loop takes: " + string.Join(", ", world.EmoteExits.Select(e => $"wcid {e.Wcid} {e.Name} ({e.CategoryName} emote to 0x{e.Destination:X8})"));
        w.WriteStartObject("exit");
        if (world.Exits.Count == 0)
        {
            w.WriteString("kind", "recall");
            w.WriteString("source", $"the pinned world SQL v0.9.296 (GroundDraft, M6 3 a1): no landblock_instance row of the landblock is a portal whose Destination (weenie_properties_position, PositionType 2) lies outside it, so the ground is left by recall; {emotes}");
        }
        else
        {
            Vector3 At(DraftPortal e) => world.ExitOrigins.TryGetValue(e.Guid, out var o) ? o : throw new InvalidOperationException($"exit portal 0x{e.Guid:X8} has no origin in DraftWorld.ExitOrigins");
            var nearest = world.Exits.OrderBy(e => Vector3.Distance(arrival, At(e))).ThenBy(e => e.Guid).First();
            var metres = Math.Round(Vector3.Distance(arrival, At(nearest)), 1);
            var others = world.Exits.Where(e => e != nearest).Select(e => $"0x{e.Guid:X8} wcid {e.Wcid} in 0x{e.Cell:X8} to 0x{e.Destination:X8}").ToArray();
            w.WriteString("kind", "portal");
            w.WriteNumber("wcid", nearest.Wcid);
            w.WriteString("cell", $"0x{nearest.Cell:X8}");
            WritePoint(w, "origin", At(nearest));
            w.WriteString("destination", $"0x{nearest.Destination:X8}");
            w.WriteNumber("from_arrival_m", metres);
            w.WriteString("source", string.Create(CultureInfo.InvariantCulture,
                $"the pinned world SQL v0.9.296 (GroundDraft, M6 3 a1): the exit portal, landblock_instance guid 0x{nearest.Guid:X8} (wcid {nearest.Wcid}) in 0x{nearest.Cell:X8} at the row's origin, whose weenie's Destination (weenie_properties_position, PositionType 2) is 0x{nearest.Destination:X8}, outside the landblock; from_arrival_m {metres} is a hint, the straight-line distance from the arrival in 0x{arrivalCell:X8} to the portal's origin (a walk over the room graph is longer: item 4's){(others.Length == 0 ? string.Empty : $"; {HandSet}: {others.Length} other exit portal(s), the nearest taken: {string.Join(", ", others)} (TODO(ground-draft-exit))")}; {emotes}"));
        }

        w.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter w, string name, Vector3 p)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", Math.Round(p.X, 4));
        w.WriteNumber("y", Math.Round(p.Y, 4));
        w.WriteNumber("z", Math.Round(p.Z, 4));
        w.WriteEndObject();
    }

    /// <summary>A plain-text summary of a draft for the command's output: the counts, the storeys, the generators, the exits and the notes.</summary>
    /// <param name="cells">The dats' part.</param>
    /// <param name="world">The SQL's part.</param>
    public static string Summary(DraftCells cells, DraftWorld world)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(world);
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"landblock 0x{cells.Landblock:X4}: {cells.Total} cells; {cells.Walkable.Count} walkable from 0x{cells.Arrival:X8} at lean {cells.DoorwayLean}; {cells.Floorless.Count} floorless; {cells.Pockets.Count} unreached pocket cells\n");
        foreach (var s in cells.Storeys)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  storey {s.Name}: {s.Cells.Count} cells\n");
        }

        foreach (var g in world.Generators)
        {
            var mine = world.Slots.Where(s => s.Generator == g.Guid).ToArray();
            sb.Append(CultureInfo.InvariantCulture, $"  generator 0x{g.Guid:X8} wcid {g.Wcid} in 0x{g.Cell:X8}: delay {g.DelaySeconds?.ToString(CultureInfo.InvariantCulture) ?? "none"} s, tick {g.TickSeconds?.ToString(CultureInfo.InvariantCulture) ?? "none"} s, {mine.Length} slots ({string.Join(", ", mine.GroupBy(s => s.Wcid).Select(x => $"{x.Count()} x {x.Key}"))})\n");
        }

        foreach (var c in world.Creatures)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  creature {c.Wcid} {c.Name} level {c.Level?.ToString(CultureInfo.InvariantCulture) ?? "none"}: {world.Slots.Count(s => s.Wcid == c.Wcid)} slots; health {c.MaxHealth?.ToString(CultureInfo.InvariantCulture) ?? "none"}, Magic Defense {c.MagicDefense?.ToString(CultureInfo.InvariantCulture) ?? "none"}, attributes {(c.Attributes is { } at ? $"{at.Strength}/{at.Endurance}/{at.Coordination}/{at.Quickness}/{at.Focus}/{at.Self}" : "none")}, damage types {(c.DamageTypes is { } dt ? $"0x{dt:X4}" : "none")}\n");
        }

        foreach (var e in world.Exits)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  exit portal 0x{e.Guid:X8} wcid {e.Wcid} in 0x{e.Cell:X8} to 0x{e.Destination:X8}\n");
        }

        foreach (var e in world.EmoteExits)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  exit by emote (proposed no-go): wcid {e.Wcid} {e.Name}: its {e.CategoryName} emote ({e.ActionType}) casts {(e.Spell == 0 ? e.SpellName : $"'{e.SpellName}' (spell {e.Spell})")} to 0x{e.Destination:X8}; its slots stand in {string.Join(", ", e.Cells.Select(c => $"0x{c:X8}"))}\n");
        }

        var levels = world.Creatures.Where(c => c.Level is not null).Select(c => c.Level!.Value).ToArray();
        var band = LevelBandOf(world.Creatures.Where(c => c.Level is not null && ProposedRole(world, c.Wcid) == GroundRole.Target).Select(c => c.Level!.Value));
        sb.Append(CultureInfo.InvariantCulture, $"  levels: the slot creatures' {(levels.Length == 0 ? "none" : $"{levels.Min()} to {levels.Max()}")}; the proposed band, from the proposed targets only, {(band is { } b ? $"{b.Min} to {b.Max}" : "none")}\n");
        foreach (var e in world.Entries)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  entry portal wcid {e.Wcid} {e.Name} leads to 0x{e.Cell:X8} at ({e.Origin.X:0.####}, {e.Origin.Y:0.####}, {e.Origin.Z:0.####}), min level {e.MinLevel?.ToString(CultureInfo.InvariantCulture) ?? "none"}, max level {e.MaxLevel?.ToString(CultureInfo.InvariantCulture) ?? "none"}, PortalBitmask {(e.PortalBitmask is { } pb ? $"0x{pb:X}" : "none")}{(e.Tieable ? string.Empty : " (NoRecall: not tieable)")}{(e.Cell == world.Arrival?.Cell ? " (the arrival)" : string.Empty)}\n");
        }

        if (world.Town is { } town)
        {
            foreach (var p in town.Portals)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  town: the portal's instance 0x{p.Guid:X8} in 0x{p.Cell:X8} at ({p.Point.X:0.####}, {p.Point.Y:0.####}, {p.Point.Z:0.####})\n");
            }

            foreach (var l in town.Lifestones)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  town: lifestone 0x{l.Place.Guid:X8} in 0x{l.Place.Cell:X8} at ({l.Place.Point.X:0.####}, {l.Place.Point.Y:0.####}, {l.Place.Point.Z:0.####}), {l.Metres:0} m from the portal{(l == town.Lifestone ? " (the home)" : string.Empty)}\n");
            }

            foreach (var v in town.Vendors)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  town: vendor wcid {v.Place.Wcid} {v.Name}, guid 0x{v.Place.Guid:X8} in 0x{v.Place.Cell:X8} at ({v.Place.Point.X:0.####}, {v.Place.Point.Y:0.####}, {v.Place.Point.Z:0.####}), {v.Metres:0} m from the lifestone{(v.InThreeByThree ? ", in the 3x3" : string.Empty)}; buys 0x{v.BuysItemTypes:X8}; sells x{v.SellMultiplier?.ToString(CultureInfo.InvariantCulture) ?? "none"}, {v.Wares.Count} wares\n");
                foreach (var w in v.Wares)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"      ware {w.Wcid} {w.Name}, value {w.Value}, price {w.Price}\n");
                }
            }
        }

        foreach (var n in world.Notes)
        {
            sb.Append("  ").Append(n).Append('\n');
        }

        return sb.ToString();
    }
}
