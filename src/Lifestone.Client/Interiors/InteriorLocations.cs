using System.Numerics;
using Lifestone.Client.World;

namespace Lifestone.Client.Interiors;

/// <summary>What kind of indoor place a curated location is.</summary>
public enum InteriorKind
{
    /// <summary>A dungeon landblock: no buildings, no cell sees outside; reached by portal (teleport).</summary>
    Dungeon,

    /// <summary>The inside of a town building (its cells are seen_outside).</summary>
    BuildingInterior,
}

/// <summary>A curated indoor place: a key for <c>--start=</c>, a display name and the retail position.</summary>
/// <param name="Key">Lower-case key accepted by <see cref="InteriorLocations.TryGet"/> (and so by <c>--start=</c>).</param>
/// <param name="Kind">Dungeon or building interior.</param>
/// <param name="Location">Cell id, landblock-local origin (the floor: feet, not eyes) and facing.</param>
/// <param name="Source">Where the position comes from.</param>
public sealed record InteriorLocation(string Key, InteriorKind Kind, WorldLocation Location, string Source)
{
    public string Name => Location.Name;

    public uint CellId => Location.Position.CellId;
}

/// <summary>
/// Well-known dungeons and a town building interior for the teleport panel and <c>--start=</c>
/// (docs/interiors.md §6).
/// </summary>
/// <remarks>
/// <para>Dungeon positions are entries of ACViewer's <c>ACViewer/Data/Locations.txt</c> (GPL-3.0, @c5c54fb; the same
/// file and commit core's <see cref="WorldLocations"/> uses for the towns): a cell id, a landblock-local origin and a
/// quaternion (w x y z, rotation about Z only), the drop points of the retail portals. Each is checked against the
/// user's dats by <c>InteriorLocationsTests</c>: the cell exists, it is a dungeon cell (not seen_outside) and the point
/// lies inside it. ACViewer's "Hotel Swank" entry fails that check (its point is not in its cell) and is left out.</para>
/// <para>Building interiors have no retail teleport target (portals drop players outdoors), and the dats name no
/// buildings: the two Holtburg ones are named by what stands inside them, and their points are checked the same way
/// (inside a cell of that building, over its floor). The Training Academy is ACE's character start position.</para>
/// </remarks>
public static class InteriorLocations
{
    private const string AcViewer = "ACViewer Data/Locations.txt @c5c54fb";

    /// <summary>
    /// The Town Network ("subway"), landblock 0x0007: 498 cells, 171 static lights; ACViewer "Town Network | POI";
    /// ACE <c>GameConfiguration</c> lists landblock 0x0007 as "Town Network".
    /// </summary>
    public static readonly InteriorLocation TownNetwork = Dungeon(
        "town-network", "Town Network", 0x00070143, new Vector3(70f, -60f, 0.005f), 0.0, -1.0);

    /// <summary>Holtburg Redoubt, landblock 0x0163 (178 cells): the classic dungeon near Holtburg.</summary>
    public static readonly InteriorLocation HoltburgRedoubt = Dungeon(
        "holtburg-redoubt", "Holtburg Redoubt", 0x016301AC, new Vector3(140f, -40f, 0f), -0.707107, -0.707107);

    /// <summary>The Marketplace, landblock 0x016C (536 cells; ACViewer "Marketplace | POI").</summary>
    public static readonly InteriorLocation Marketplace = Dungeon(
        "marketplace", "Marketplace", 0x016C01BC, new Vector3(49.206f, -31.935f, 0.005f), 0.707107, -0.707107);

    /// <summary>Mines of Despair, landblock 0x0188 (537 cells).</summary>
    public static readonly InteriorLocation MinesOfDespair = Dungeon(
        "mines-of-despair", "Mines of Despair", 0x01880307, new Vector3(30f, -70f, 0f), 1.0, 0.0);

    /// <summary>Glenden Wood Dungeon, landblock 0x01E3 (749 cells).</summary>
    public static readonly InteriorLocation GlendenWoodDungeon = Dungeon(
        "glenden-wood-dungeon", "Glenden Wood Dungeon", 0x01E3033D, new Vector3(161f, -150f, 0f), -0.983255, -0.182236);

    /// <summary>
    /// The Aluvian Black Spawn Den, landblock 0x0105: the arrival of the den's portal (wcid 7242, world database
    /// v0.9.296, <c>Session/Mage/BlackSpawnDen.cs</c>), where the M3 fight loop (3.1) fights Tusker Slaves on the
    /// arrival's floor.
    /// </summary>
    public static readonly InteriorLocation BlackSpawnDenAluvian = Dungeon(
        "black-spawn-den-aluvian",
        "Black Spawn Den (Aluvian)",
        0x01050375,
        new Vector3(100f, -210f, 0f),
        0.707107,
        -0.707107,
        "ACE world database v0.9.296: portal 7242's destination, docs/milestones/m3-lights-and-sound.md section 5.1");

    /// <summary>
    /// The Training Academy, landblock 0x8602: where a new character from Holtburg's start area appears. ACE places a new
    /// character at <c>CharGen.StarterAreas[startArea].Locations[0]</c> (ACE.Server/Factories/PlayerFactory.cs, ACE
    /// @95ef426d), which the end-of-retail portal dat (CharGen 0x0E000002, start area 0, "Holtburg") gives as cell
    /// 0x860201AD at (12.3199, -28.482, 0.005), rotation (x 0, y 0, z -0.9408059, w -0.3389459)
    /// (<c>InteriorLocationsTests</c> checks it against the dat). M1 used 0x860301AD from ACE's older
    /// <c>CharacterPositionExtensions.StartingPosition</c>: another of the same start area's locations (the same room in
    /// landblock 0x8603), not the first, which is the one ACE spawns at (m2-plan §1.6 item 26; fixed in M2 step ST12).
    /// </summary>
    public static readonly InteriorLocation TrainingAcademy = new(
        "training-academy",
        InteriorKind.Dungeon,
        new WorldLocation("Training Academy", new CellPosition(0x860201AD, new Vector3(12.3199f, -28.482f, 0.005f)), true,
            WorldLocations.HeadingFromQuaternion(-0.3389459, -0.9408059)),
        "the portal dat's CharGen 0x0E000002 StarterAreas[0].Locations[0], where ACE's PlayerFactory places a new character (ACE @95ef426d)");

    /// <summary>
    /// Inside the Holtburg shop with the counter (LandBlockInfo 0xA9B4 building 7, GfxObj 0x01000C17), facing the
    /// counter from its west door. The building is identified by what is inside (a counter with potions and bottles);
    /// the dats carry no names. The point is on the floor of cell 0xA9B40163 (checked by the tests).
    /// </summary>
    public static readonly InteriorLocation HoltburgShop = new(
        "holtburg-shop",
        InteriorKind.BuildingInterior,
        new WorldLocation("Holtburg shop (interior)", new CellPosition(0xA9B40163, new Vector3(100f, 36f, 94f)), true, 90.0),
        "chosen inside LandBlockInfo 0xA9B4 building 7 (doorway cells 0xA9B40164/0xA9B40165)");

    /// <summary>
    /// Inside the Holtburg smithy (LandBlockInfo 0xA9B4 building 0, GfxObj 0x01000C1E: weapons and shields on the walls,
    /// a quenching tub and a grindstone): the centroid of the largest floor polygon of its doorway cell 0xA9B40100
    /// (<see cref="Lifestone.Data.World.Cells.CellDiscovery.FindBuildingEntry"/>, a heuristic), facing north.
    /// </summary>
    public static readonly InteriorLocation HoltburgSmithy = new(
        "holtburg-smithy",
        InteriorKind.BuildingInterior,
        new WorldLocation("Holtburg smithy (interior)", new CellPosition(0xA9B40100, new Vector3(88.8429f, 131.542f, 66f)), true, 0.0),
        "CellDiscovery.FindBuildingEntry (heuristic) for LandBlockInfo 0xA9B4 building 0");

    /// <summary>Every curated location, dungeons first.</summary>
    public static IReadOnlyList<InteriorLocation> All { get; } =
    [
        TownNetwork,
        HoltburgRedoubt,
        Marketplace,
        MinesOfDespair,
        GlendenWoodDungeon,
        BlackSpawnDenAluvian,
        TrainingAcademy,
        HoltburgShop,
        HoltburgSmithy,
    ];

    /// <summary>Looks a location up by key (case-insensitive; spaces, underscores and dashes are interchangeable).</summary>
    public static bool TryGet(string? key, out WorldLocation location)
    {
        location = WorldLocations.Holtburg;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalised = key.Trim().Replace(' ', '-').Replace('_', '-');
        foreach (var entry in All)
        {
            if (string.Equals(entry.Key, normalised, StringComparison.OrdinalIgnoreCase))
            {
                location = entry.Location;
                return true;
            }
        }

        return false;
    }

    private static InteriorLocation Dungeon(string key, string name, uint cellId, Vector3 origin, double w, double z, string? source = null) =>
        new(
            key,
            InteriorKind.Dungeon,
            new WorldLocation(name, new CellPosition(cellId, origin), true, WorldLocations.HeadingFromQuaternion(w, z)),
            source ?? $"{AcViewer}: \"{name}\" 0x{cellId:X8} [{origin.X} {origin.Y} {origin.Z}] {w} 0 0 {z}");
}
