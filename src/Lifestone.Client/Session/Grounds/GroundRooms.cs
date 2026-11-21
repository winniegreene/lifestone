using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Grounds;

/// <summary>
/// A hunting ground's rooms for the walks (M5 item 2 g2b, D46): the ground's walkable cells (<see cref="HuntingGround.IsWalkable"/>: the storeys without the no-go cells) as a <see cref="RoomGraph"/> at the ground's own
/// doorway lean (<see cref="WalkModel.DoorwayLean"/>). Every host loads its graph through here, whatever the ground: the fight's and the death's walks, the journey's legs and the Godot runners. Some dungeons' doorways lean
/// (the Black Spawn Den's plazas lean 0.196, which the upright rule, normal Z under 0.1, refuses), so the lean is a profile value, not a constant.
/// </summary>
/// <remarks>
/// <para><c>TODO(ground-multi-landblock)</c>: a <see cref="RoomGraph"/> covers one landblock, so a ground of several landblocks needs a graph per landblock and their crossings; <see cref="Load(DatSet, HuntingGround)"/> and <see cref="CoordOf"/> refuse it, naming the ground.</para>
/// </remarks>
public static class GroundRooms
{
    /// <summary>The coordinate of the ground's one landblock.</summary>
    /// <param name="ground">The ground.</param>
    /// <exception cref="NotSupportedException">The ground spans several landblocks, or none (<c>TODO(ground-multi-landblock)</c>).</exception>
    public static LandblockCoord CoordOf(HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Landblocks.Count == 1
            ? LandblockCoord.FromId((uint)ground.Landblocks[0] << 16)
            : throw new NotSupportedException($"ground '{ground.Key}' lies in {ground.Landblocks.Count} landblocks; the room graph covers one (TODO(ground-multi-landblock))");
    }

    /// <summary>The ground's walkable cells out of a loaded landblock: the cells <see cref="HuntingGround.IsWalkable"/> accepts, and (M6 redoubt) the cells that hold its galleries' floors (<see cref="HuntingGround.IsGalleryFloor"/>).</summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static LandblockCellSet FloorOf(LandblockCellSet all, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(ground);
        return LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => ground.IsWalkable(c.Id) || ground.IsGalleryFloor(c.Id))); // M6 redoubt: a gallery's floor is held by cells of no storey
    }

    /// <summary>The ground's room graph over a loaded landblock: the walkable cells at the ground's doorway lean.</summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static RoomGraph GraphOf(LandblockCellSet all, HuntingGround ground) =>
        new(FloorOf(all, ground), ground.Walk.DoorwayLean) { Jumps = ground.Jumps, PlateList = ground.KeptPlatesOf(all.Structures.Dats) }; // M6 14 pits: the profile's jumps are edges; M6 14 holding plates: its plates are walls, their shape the dats'

    /// <summary>True when <paramref name="cell"/> is the ground's exit portal's cell (<see cref="GroundExitSection.Cell"/>).</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="cell">The full cell id.</param>
    public static bool IsExitCell(HuntingGround ground, uint cell)
    {
        ArgumentNullException.ThrowIfNull(ground);
        return ground.Exit is { Kind: GroundExitKind.Portal } exit && exit.Cell == cell;
    }

    /// <summary>
    /// M6 7 U (r1f-climb's <c>rooms-no-path</c>, 0x57480210 to the Burrow's exit front 0x57480100): the graph of a journey leg with an end in the exit portal's cell. The exit's cell is the profile's no-go cell (the fight never walks onto the portal),
    /// and <see cref="GraphOf"/> leaves it out, so no leg could ever reach the exit: the way home by portals stopped on every ground of the catalog with a portal exit. This graph is <see cref="GraphOf"/> with that one cell back, and (M6 item 14) the fewest no-go cells that join it to the rooms where it touches none (<see cref="ExitWay"/>); every other no-go
    /// cell stays out (a statue's room, a hall of guards), and the fight's and the tour's walks keep <see cref="GraphOf"/>. Pinned on the dats: <c>IslandDungeonWalkDatTests.The_walk_home_reaches_a_ground_s_exit_portal_cell_from_its_arrival</c>.
    /// </summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static RoomGraph LegGraphOf(LandblockCellSet all, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(ground);
        var way = ExitWay(all, ground);
        return new RoomGraph(LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => ground.IsWalkable(c.Id) || ground.IsGalleryFloor(c.Id) || IsExitCell(ground, c.Id) || way.Contains(c.Id))), ground.Walk.DoorwayLean) { Jumps = ground.Jumps, PlateList = ground.KeptPlatesOf(all.Structures.Dats) };
    }

    /// <summary>
    /// M6 item 14 (the den run): the no-go cells that join the exit portal's cell to the ground's rooms, the fewest (a breadth-first walk over the cells' portals from the exit's cell, through the profile's no-go cells alone, lower cell ids first, to the first walkable cell);
    /// empty when the exit's cell touches a room (every island ground) or the ground has no exit portal. The Black Spawn Dens keep their first exit behind no-go cells (its room 0x0342-0x0343 and the doorway 0x035C, the exclusion the
    /// fight loop measured), so <see cref="LegGraphOf"/> with the one cell back joined nothing and a leg to the den's own exit could never be planned (<c>IslandDungeonWalkDatTests</c>, the Sho den's case). Only these cells come back, and
    /// only for a leg to or from the exit; the fight's and the tour's walks keep <see cref="GraphOf"/>.
    /// </summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static IReadOnlySet<uint> ExitWay(LandblockCellSet all, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(ground);
        var way = new HashSet<uint>();
        if (ground.Exit is not { Kind: GroundExitKind.Portal } exit || all.GetCell(exit.Cell) is null)
        {
            return way;
        }

        var adjacency = all.PortalAdjacency();
        var cameFrom = new Dictionary<uint, uint> { [exit.Cell] = 0 };
        var queue = new Queue<uint>([exit.Cell]);
        while (queue.TryDequeue(out var cell))
        {
            if (!adjacency.TryGetValue(cell, out var neighbours))
            {
                continue;
            }

            foreach (var next in neighbours.Order())
            {
                if (cameFrom.ContainsKey(next))
                {
                    continue;
                }

                if (ground.IsWalkable(next))
                {
                    for (var back = cell; back != exit.Cell && back != 0; back = cameFrom[back])
                    {
                        way.Add(back);
                    }

                    return way;
                }

                if (!ground.IsNoGo(next))
                {
                    continue; // only the profile's own no-go cells come back: a cell of no storey (a pit, a floorless ramp) is no way
                }

                cameFrom[next] = cell;
                queue.Enqueue(next);
            }
        }

        return way; // no room is reached from the exit's cell: the leg's own plan says so by name
    }

    /// <summary>Loads the ground's landblock from the dats and builds its room graph (a few seconds).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="ground">The ground.</param>
    public static RoomGraph Load(DatSet dats, HuntingGround ground) => Load(new CellStructureLibrary(dats), ground);

    /// <summary>Loads the ground's landblock through a cell structure library already made (shared with the caller) and builds its room graph.</summary>
    /// <param name="structures">The library.</param>
    /// <param name="ground">The ground.</param>
    public static RoomGraph Load(CellStructureLibrary structures, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(structures);
        return GraphOf(LandblockCellSet.Load(structures, CoordOf(ground)), ground);
    }
}
