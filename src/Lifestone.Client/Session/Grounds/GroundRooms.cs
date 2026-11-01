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

    /// <summary>The ground's walkable cells out of a loaded landblock: the cells <see cref="HuntingGround.IsWalkable"/> accepts.</summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static LandblockCellSet FloorOf(LandblockCellSet all, HuntingGround ground)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(ground);
        return LandblockCellSet.FromCells(all.Structures, all.Coord, all.Cells.Where(c => ground.IsWalkable(c.Id)));
    }

    /// <summary>The ground's room graph over a loaded landblock: the walkable cells at the ground's doorway lean.</summary>
    /// <param name="all">Every cell of the ground's landblock.</param>
    /// <param name="ground">The ground.</param>
    public static RoomGraph GraphOf(LandblockCellSet all, HuntingGround ground) => new(FloorOf(all, ground), ground.Walk.DoorwayLean);

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
