using Lifestone.Data.Physics.Objects;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// Where a transition finds cells (retail's <c>LScape::get_landcell</c> for land cells and <c>CEnvCell::GetVisible</c>
/// for EnvCells) and the static objects that other landblocks put into them.
/// </summary>
/// <remarks>
/// Implemented by <see cref="PhysicsWorld"/> (the attached landblocks) and, while a landblock's collision is built, by
/// that landblock alone (its neighbours' cells are then unknown, as they are when retail creates a block's objects before
/// its neighbour is loaded).
/// </remarks>
internal interface IPhysicsCellSource
{
    /// <summary>The cell with this full id, or null when its landblock is not attached or the cell does not exist.</summary>
    PhysCell? GetCell(uint cellId);

    /// <summary>
    /// The static objects of other landblocks whose shadows fall into <paramref name="cellId"/> (retail adds them to the
    /// cell's shadow list across landblock edges, <c>CLandCell::add_cell_block</c>), or null when there are none.
    /// </summary>
    List<PhysStaticObject>? GetForeignShadows(uint cellId);

    /// <summary>
    /// The moving objects whose shadows fall into <paramref name="cellId"/> (retail <c>CPhysicsObj::add_shadows_to_cells</c>
    /// adds them to the cell's shadow list after its statics), or null when there are none.
    /// </summary>
    List<PhysicsObj>? GetObjectShadows(uint cellId);

    /// <summary>
    /// A player's transition reached <paramref name="cellId"/>, which a housing barrier guards
    /// (<see cref="PhysCell.RestrictionObject"/>), and was let in without the check retail makes there (TODO(physics) in
    /// <c>PhysCell.CheckEntryRestrictions</c>). The world counts it so the client can say so; a landblock being built has
    /// no players to report.
    /// </summary>
    void NoteUnenforcedRestriction(uint cellId);
}

/// <summary>How much of a land cell or landblock is water (retail <c>LandDefs::WaterType</c>).</summary>
public enum WaterType
{
    /// <summary>No corner is water.</summary>
    NotWater = 0,

    /// <summary>Some corners are water.</summary>
    PartiallyWater = 1,

    /// <summary>Every corner is water (for a landblock: every cell).</summary>
    EntirelyWater = 2,
}
