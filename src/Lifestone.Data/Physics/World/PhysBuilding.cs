// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/BuildingObj.cs (find_building_collisions,
// find_building_transit_cells) and BldPortal.cs (GetOtherCell) (AGPL-3.0)
// with changes: immutable; the doorways are the cells track's BuildingPortalData; checked against the retail functions
// cited below.

using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// A building of a landblock (retail <c>CBuildingObj</c>): one part (the building GfxObj at its frame) and the doorways
/// into its EnvCells. It collides only from the land cell that holds its origin (retail <c>CSortCell::building</c>).
/// </summary>
public sealed class PhysBuilding
{
    private readonly BuildingPortalData[] _portals;

    internal PhysBuilding(uint modelId, CellFrame position, PhysPart part, BuildingPortalData[] portals)
    {
        ModelId = modelId;
        Position = position;
        Part = part;
        _portals = portals;
    }

    /// <summary>The building GfxObj.</summary>
    public uint ModelId { get; }

    /// <summary>The land cell that holds the building and its landblock-local frame.</summary>
    public CellFrame Position { get; }

    /// <summary>The building's single part (its level-0 GfxObj at the building frame).</summary>
    public PhysPart Part { get; }

    /// <summary>The doorways (retail <c>CBldPortal</c>s) in stored order.</summary>
    public IReadOnlyList<BuildingPortalData> Portals => _portals;

    /// <inheritdoc/>
    public override string ToString() => $"PhysBuilding 0x{ModelId:X8} at {Position}";

    /// <summary>
    /// Retail <c>CBuildingObj::find_building_collisions</c> (11.4186 0x006B5300): the part's collision with the building
    /// check set; a building that stops an object without contact is an environment collision.
    /// </summary>
    /// <remarks>TODO(physics): 11.6096 address not in the symbol table.</remarks>
    internal TransitionState FindBuildingCollisions(Transition transition)
    {
        transition.SpherePath.BuildingCheck = true;
        var state = Part.FindObjCollisions(transition);
        transition.SpherePath.BuildingCheck = false;
        if (state != TransitionState.OK && (transition.ObjectInfo.State & ObjectInfoState.Contact) == 0)
        {
            transition.CollisionInfo.CollidedWithEnvironment = true;
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CBuildingObj::find_building_transit_cells(p, num_sphere, sphere, cell_array, path)</c> (11.4186
    /// 0x006B5230): the entry cell of each doorway whose cell is attached
    /// (<c>CBldPortal::GetOtherCell</c>, 0x0053BC30, 0x0053C990), when a sphere reaches into it
    /// (<see cref="PhysEnvCell.CheckBuildingTransit(int, ReadOnlySpan{PhysSphere}, CellArray, SpherePath?)"/>).
    /// </summary>
    /// <remarks>TODO(physics): 11.6096 address not in the symbol table.</remarks>
    internal void FindBuildingTransitCells(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray, SpherePath? path)
    {
        _ = position;
        foreach (var portal in _portals)
        {
            if (cells.GetCell(portal.OtherCellId) is PhysEnvCell entry)
            {
                entry.CheckBuildingTransit(portal.OtherPortalIndex, spheres, cellArray, path);
            }
        }
    }

    /// <summary>
    /// Retail <c>CBuildingObj::find_building_transit_cells(num_parts, parts, cell_array)</c> (11.4186 0x006B52A0): the same
    /// for a static object's parts.
    /// </summary>
    /// <remarks>TODO(physics): 11.6096 address not in the symbol table.</remarks>
    internal void FindBuildingTransitCells(IPhysicsCellSource cells, ReadOnlySpan<PhysPart> parts, CellArray cellArray)
    {
        foreach (var portal in _portals)
        {
            if (cells.GetCell(portal.OtherCellId) is PhysEnvCell entry)
            {
                entry.CheckBuildingTransit(cells, portal.OtherPortalIndex, parts, cellArray);
            }
        }
    }
}
