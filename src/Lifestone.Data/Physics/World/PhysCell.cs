// Partly ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Common/ObjCell.cs (FindObjCollisions,
// get_water_depth, check_entry_restrictions) (AGPL-3.0)
// with changes: immutable once its landblock is built; shadow objects of other landblocks and of moving objects come
// from the cell source (PhysicsWorld) at query time; the entry restriction follows retail (no object: refused; housing
// barriers wait for a house weenie, a named TODO counted on the world); ACE's per-cell locks and voyeurs are not needed
// for collision.

using System.Numerics;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// A cell of the physics world, as retail <c>CObjCell</c>: an outdoor land cell (<see cref="PhysLandCell"/>,
/// <c>CLandCell</c>) or an EnvCell (<see cref="PhysEnvCell"/>, <c>CEnvCell</c>), with the static objects that reach into it.
/// </summary>
/// <remarks>Immutable once its <see cref="LandblockCollision"/> is built; shared by every transition.</remarks>
public abstract class PhysCell
{
    private PhysStaticObject[] _shadows = [];

    private protected PhysCell(uint id, CellFrame position)
    {
        Id = id;
        Position = position;
    }

    /// <summary>The full cell id (<c>0xXXYY00nn</c> outdoors, <c>0xXXYY0100</c>-<c>0xXXYYFFFD</c> for an EnvCell).</summary>
    public uint Id { get; }

    /// <summary>
    /// The cell's position (retail <c>CObjCell::pos</c>): an EnvCell's frame in its landblock, or a land cell's centre at
    /// height 0 with no rotation.
    /// </summary>
    public CellFrame Position { get; }

    /// <summary>How much of the cell is water (land cells; an EnvCell is never water).</summary>
    public virtual WaterType WaterType => WaterType.NotWater;

    /// <summary>The landblock the cell belongs to (retail <c>myLandBlock_</c>); null for cells built in code.</summary>
    public LandblockCollision? Landblock { get; internal set; }

    /// <summary>
    /// The housing barrier guarding the cell (retail <c>restriction_obj</c>: a land cell's from the LandBlockInfo's
    /// restriction table, an EnvCell's from its own record), or 0.
    /// </summary>
    public uint RestrictionObject { get; internal set; }

    /// <summary>The static objects of the cell's own landblock that reach into it (retail shadow objects), in creation order.</summary>
    public IReadOnlyList<PhysStaticObject> StaticObjects => _shadows;

    /// <summary>
    /// Retail <c>point_in_cell</c>: whether the landblock-local <paramref name="point"/> (in this cell's landblock space)
    /// lies in the cell.
    /// </summary>
    public abstract bool PointInCell(Vector3 point);

    /// <inheritdoc/>
    public override string ToString() => $"{GetType().Name} 0x{Id:X8}";

    /// <summary>The cell's full collision pass (retail <c>find_collisions</c>): environment, then buildings, then static objects.</summary>
    internal abstract TransitionState FindCollisions(Transition transition);

    /// <summary>Retail <c>find_env_collisions</c>: the cell's own geometry (terrain or room shape).</summary>
    internal abstract TransitionState FindEnvCollisions(Transition transition);

    /// <summary>
    /// Retail <c>find_transit_cells(p, num_sphere, sphere, cell_array, path)</c>: adds the cells the spheres (in
    /// <paramref name="position"/>'s landblock space) reach from this one.
    /// </summary>
    internal abstract void FindTransitCells(IPhysicsCellSource cells, in CellFrame position, ReadOnlySpan<PhysSphere> spheres, CellArray cellArray, SpherePath? path);

    /// <summary>Retail <c>find_transit_cells(num_parts, parts, cell_array)</c>: adds the cells a static object's parts reach from this one.</summary>
    internal abstract void FindTransitCells(IPhysicsCellSource cells, ReadOnlySpan<PhysPart> parts, CellArray cellArray);

    /// <summary>Gives the cell its own landblock's static objects (while the landblock is built).</summary>
    internal void SetShadows(PhysStaticObject[] shadows) => _shadows = shadows;

    /// <summary>
    /// Retail <c>CObjCell::find_obj_collisions</c> (11.4186 0x0052B750, 11.6096 0x0052C360): the objects whose shadows are
    /// in the cell, in order (its landblock's statics, statics of other landblocks, then moving objects, each in the order
    /// it joined), until one stops the transition; nothing during an initial placement. The moving object itself is
    /// skipped.
    /// </summary>
    internal TransitionState FindObjCollisions(Transition transition)
    {
        if (transition.SpherePath.InsertType == InsertType.InitialPlacement)
        {
            return TransitionState.OK;
        }

        var self = transition.ObjectInfo.Object;
        foreach (var shadow in _shadows)
        {
            if (ReferenceEquals(shadow, self))
            {
                continue;
            }

            var state = shadow.FindObjCollisions(transition);
            if (state != TransitionState.OK)
            {
                return state;
            }
        }

        var foreign = transition.Cells?.GetForeignShadows(Id);
        if (foreign is not null)
        {
            for (var i = 0; i < foreign.Count; i++)
            {
                if (ReferenceEquals(foreign[i], self))
                {
                    continue;
                }

                var state = foreign[i].FindObjCollisions(transition);
                if (state != TransitionState.OK)
                {
                    return state;
                }
            }
        }

        var objects = transition.Cells?.GetObjectShadows(Id);
        if (objects is not null)
        {
            for (var i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (ReferenceEquals(obj, self))
                {
                    continue;
                }

                var state = obj.FindObjCollisions(transition);
                if (state != TransitionState.OK)
                {
                    return state;
                }
            }
        }

        return TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>CObjCell::check_entry_restrictions</c> (11.4186 0x0052B6D0, 11.6096 0x0052C2E0): a transition without an
    /// object may not enter. With an object that has a weenie and is a player, retail consults the cell's housing barrier
    /// (<see cref="RestrictionObject"/>): unless the mover's weenie slot 18 lets it bypass, the barrier object must exist
    /// and its weenie's slot 17 must admit the mover, else the cell refuses it.
    /// </summary>
    /// <remarks>TODO(physics): housing barriers. Their weenie (a server house object with its slots 17 and 18) does not
    /// exist on the client in M2, so a restricted cell admits everyone; retail refuses a player while the barrier object
    /// is not loaded. Each time a player (an object with a weenie and the IsPlayer flag, the case retail checks) is let in
    /// this way, the cell source counts it (<see cref="PhysicsWorld.UnenforcedRestrictionEntries"/>), and the client logs
    /// the first one.</remarks>
    internal TransitionState CheckEntryRestrictions(Transition transition)
    {
        var info = transition.ObjectInfo;
        if (info.Object is null)
        {
            return TransitionState.Collided;
        }

        if (RestrictionObject != 0
            && (info.State & ObjectInfoState.IsPlayer) != 0
            && info.Object is Objects.PhysicsObj { Weenie: not null })
        {
            transition.Cells?.NoteUnenforcedRestriction(Id);
        }

        return TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>CObjCell::get_block_water_type</c> (11.4186 0x0052AD90, 11.6096 0x0052B9A0): the landblock's water type,
    /// not water without one.
    /// </summary>
    internal WaterType BlockWaterType => Landblock?.WaterType ?? WaterType.NotWater;

    /// <summary>
    /// Retail <c>CObjCell::get_water_depth</c> (11.4186 0x0052B8A0, 11.6096 0x0052C4B0): how deep a sphere may sink at
    /// <paramref name="point"/>: nothing on dry ground, 0.9 m in a cell under water, and in a partly flooded cell the
    /// corner's depth (<see cref="LandblockCollision.CalcWaterDepth"/>; 0.1 m without a landblock).
    /// </summary>
    internal float GetWaterDepth(Vector3 point) => WaterType switch
    {
        WaterType.NotWater => 0.0f,
        WaterType.EntirelyWater => 0.89999998f,
        _ => Landblock?.CalcWaterDepth(Id, point) ?? 0.1f,
    };
}
