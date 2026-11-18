using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.World;

/// <summary>
/// The collision world: the landblocks whose collision is attached, and the cells and objects in them. Replaces retail's
/// static <c>LScape</c> and EnvCell tables with an instance, so tests and hosts can each have their own.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> Main thread (or the headless tick) only; no locks. <see cref="LandblockCollision"/>s are built
/// on workers and attached here.</para>
/// <para><b>Unattached landblocks.</b> A cell of a landblock that is not attached is null, as when retail has not loaded
/// it: placement into it waits (PhysicsObj's PlacementPending), and a probe there answers false.</para>
/// <para><b>Across landblock edges.</b> A static object whose shadow falls into a neighbour's cell (retail
/// <c>CLandCell::add_cell_block</c>, decision table row 6) collides from that cell while both landblocks are attached.
/// <see cref="PhysicsCompat.AddCellBlockSameLandblockOnly"/> reproduces ACE, which drops such shadows.</para>
/// <para><b>Transitions.</b> The world owns retail's pool of ten (<c>CTransition::makeTransition</c>).</para>
/// <para><b>Ground probe</b> (<see cref="TryGroundZ(in CellFrame, float, out float, out Plane)"/>, step PH8): the
/// walkable ground under a remote object, for the avatars track's clamp of interpolated objects (owner decision D24, a
/// flagged deviation; docs/physics.md §13).</para>
/// <para><b>Moving objects</b> (<see cref="PhysicsObj"/>, PH5) live in a world: it holds what retail keeps in statics
/// and the object maintainer (<c>Timer::cur_time</c> as <see cref="CurrentTime"/>, <c>CPhysicsObj::player_object</c> as
/// <see cref="Player"/>, the objects by id, the shadows they cast into cells, and the lost cell of objects whose
/// landblock is not attached, which <see cref="Attach"/> brings back).</para>
/// </remarks>
public sealed class PhysicsWorld : IGroundProbe, IPhysicsCellSource
{
    /// <summary>
    /// The transition flags of retail's camera sweep (<c>SmartBox::update_viewer</c>, 11.4186 0x00453CE0, 11.6096
    /// 0x00453D80, passes 92): IsViewer, PathClipped, FreeRotate and PerfectClip.
    /// </summary>
    internal const ObjectInfoState ViewerState =
        ObjectInfoState.IsViewer | ObjectInfoState.PathClipped | ObjectInfoState.FreeRotate | ObjectInfoState.PerfectClip;

    /// <summary>
    /// How far above an object's origin <see cref="TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/> starts its
    /// search: the human Setup's step-up height, the highest a walker climbs onto in one step (a Lifestone choice for the
    /// D24 deviation; retail steps remote objects through full transitions).
    /// </summary>
    public const float GroundProbeRise = 0.6f;

    /// <summary>
    /// How far below an object's origin <see cref="TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/> still finds
    /// ground (a Lifestone choice, as <see cref="GroundProbeRise"/>): an interpolated object further above its ground is
    /// not clamped, so a creature on a bridge is not pulled down to the terrain under it. An object below the ground is
    /// lifted from up to a tenth of the whole step (0.12 m) further down still (retail's step-down allowance,
    /// <c>OBJECTINFO::validate_walkable</c>'s walk interpolation down to -0.1).
    /// </summary>
    public const float GroundProbeDrop = 0.6f;

    /// <summary>The ground probe's transition flags: an object standing on walkable ground that creatures do not stop.</summary>
    private const ObjectInfoState GroundProbeState = ObjectInfoState.Contact | ObjectInfoState.OnWalkable | ObjectInfoState.IgnoreCreatures;

    /// <summary>The moving object the camera sweep reports (retail passes the player; cells only need one to exist).</summary>
    private static readonly object ViewerObject = new();

    private static readonly object BoltObject = new();

    /// <summary>M6 8 L: a target id no object has: <see cref="SweepBolt"/>'s missile passes every creature (the target ends the path before its body would be met).</summary>
    private const uint NoBoltTarget = 0xFFFFFFFF;

    /// <summary>M6 8 L: how near the end a bolt's sweep must come to have reached it, squared (a centimetre: the steps land on it unless something stopped them).</summary>
    private const float BoltReachedSquared = 1e-4f;

    /// <summary>The moving object the ground probe reports (cells refuse a transition without one).</summary>
    private static readonly object GroundProbeObject = new();

    private readonly Dictionary<LandblockCoord, LandblockCollision> _blocks = [];
    private readonly Dictionary<uint, List<PhysStaticObject>> _foreignShadows = [];
    private readonly Dictionary<uint, List<PhysicsObj>> _objectShadows = [];
    private readonly List<PhysicsObj> _objects = [];
    private readonly Dictionary<uint, PhysicsObj> _objectsById = [];
    private readonly List<PhysicsObj> _lost = [];
    private readonly List<PhysicsObj> _reentering = [];
    private PhysicsClock? _clock;

    /// <summary>Creates an empty world.</summary>
    /// <param name="compat">ACE-compatibility switches for tests (<see cref="PhysicsCompat"/>); null is <see cref="PhysicsCompat.Retail"/>.</param>
    public PhysicsWorld(PhysicsCompat? compat = null)
    {
        Compat = compat ?? PhysicsCompat.Retail;
        Transitions = new TransitionPool(this, Compat);
    }

    /// <summary>The behaviour switches this world runs with (<see cref="PhysicsCompat.Retail"/> outside tests).</summary>
    public PhysicsCompat Compat { get; }

    /// <summary>The landblocks attached now.</summary>
    public int AttachedCount => _blocks.Count;

    /// <summary>The world's transitions (retail's pool of ten).</summary>
    internal TransitionPool Transitions { get; }

    /// <summary>
    /// The host's monotonic clock in seconds as of the last <see cref="UseTime"/> (retail <c>Timer::cur_time</c>, which
    /// <c>CPhysicsObj::update_object</c> steps objects to and <c>set_active</c> stamps them with).
    /// </summary>
    public double CurrentTime { get; private set; }

    /// <summary>The client's own player (retail <c>CPhysicsObj::player_object</c>), or null.</summary>
    public PhysicsObj? Player { get; private set; }

    /// <summary>The moving objects in the world, in the order they were made.</summary>
    public IReadOnlyList<PhysicsObj> Objects => _objects;

    /// <summary>The objects waiting for their landblock (retail's lost cells).</summary>
    public IReadOnlyList<PhysicsObj> LostObjects => _lost;

    /// <summary>
    /// How often a player's transition was let into a cell that a housing barrier guards
    /// (<see cref="PhysCell.RestrictionObject"/>) without the check retail makes there: TODO(physics), M2 has no house
    /// weenie to ask, so such a cell admits everyone where retail refuses a player until the barrier object is loaded and
    /// then asks it (<c>CObjCell::check_entry_restrictions</c>, 11.4186 0x0052B6D0, 11.6096 0x0052C2E0). Each restricted
    /// cell a transition step reaches counts once. The client logs the first one (<c>PlayerMovementController</c>).
    /// </summary>
    public long UnenforcedRestrictionEntries { get; private set; }

    /// <summary>The cell of the latest of <see cref="UnenforcedRestrictionEntries"/>, or 0 before the first.</summary>
    public uint LastUnenforcedRestrictionCell { get; private set; }

    /// <summary>The cell array of <c>CPhysicsObj::calc_cross_cells</c> (a static in retail).</summary>
    internal CellArray CrossCells { get; } = new();

    /// <summary>
    /// Advances <see cref="CurrentTime"/> to <paramref name="now"/> and answers retail <c>CPhysics::UseTime</c>'s gate
    /// (11.4186 0x00509950, 11.6096 0x0050A420; <see cref="PhysicsClock"/>): true when at least 1/30 s has passed since
    /// physics last ran, and the host should now call <see cref="PhysicsObj.UpdateObject"/> on the objects it steps
    /// (retail steps every object of the object table; Lifestone's host steps the player, owner decision D24). The first
    /// call starts the clock and answers false.
    /// </summary>
    public bool UseTime(double now)
    {
        CurrentTime = now;
        if (_clock is null)
        {
            _clock = new PhysicsClock(now);
            return false;
        }

        return _clock.UseTime(now, out _);
    }

    /// <summary>
    /// Sets <see cref="CurrentTime"/> without the gate (hosts and tests that step objects themselves; retail's timer
    /// advances every frame whether physics runs or not).
    /// </summary>
    public void SetTime(double now) => CurrentTime = now;

    /// <summary>Retail <c>CPhysicsObj::SetPlayer</c> (11.4186 0x0050F4C0, 11.6096 0x0050FF90): the client's own player.</summary>
    /// <exception cref="ArgumentException"><paramref name="player"/> belongs to another world.</exception>
    public void SetPlayer(PhysicsObj? player)
    {
        if (player is not null && !ReferenceEquals(player.World, this))
        {
            throw new ArgumentException("the player must be an object of this world", nameof(player));
        }

        Player = player;
    }

    /// <summary>The object with this id (retail <c>CObjectMaint::GetObjectA</c>), or null; 0 finds nothing.</summary>
    public PhysicsObj? GetObject(uint id) => id != 0 ? _objectsById.GetValueOrDefault(id) : null;

    /// <summary>Adds a new object (from <see cref="PhysicsObj"/>'s constructor).</summary>
    /// <exception cref="InvalidOperationException">Another object has the same non-zero id.</exception>
    internal void Register(PhysicsObj obj)
    {
        if (obj.Id != 0 && !_objectsById.TryAdd(obj.Id, obj))
        {
            throw new InvalidOperationException($"An object with id 0x{obj.Id:X8} is already in the world.");
        }

        _objects.Add(obj);
    }

    /// <summary>Removes a destroyed object.</summary>
    internal void Unregister(PhysicsObj obj)
    {
        _objects.Remove(obj);
        if (obj.Id != 0)
        {
            _objectsById.Remove(obj.Id);
        }

        _lost.Remove(obj);
        if (ReferenceEquals(Player, obj))
        {
            Player = null;
        }
    }

    /// <summary>Retail <c>CObjectMaint::GotoLostCell</c> (11.4186 0x00508210, 11.6096 0x00508CE0): the object waits for its landblock.</summary>
    internal void GotoLostCell(PhysicsObj obj)
    {
        if (!_lost.Contains(obj))
        {
            _lost.Add(obj);
        }
    }

    /// <summary>Retail <c>CObjectMaint::RemoveFromLostCell</c> (11.4186 0x00507FB0, 11.6096 0x00508A80).</summary>
    internal void RemoveFromLostCell(PhysicsObj obj) => _lost.Remove(obj);

    /// <summary>
    /// Retail <c>CObjCell::add_shadow_object</c> (11.4186 0x0052B280, 11.6096 0x0052BE90): the object's shadow joins the
    /// end of the cell's list.
    /// </summary>
    internal void AddObjectShadow(uint cellId, PhysicsObj obj)
    {
        if (!_objectShadows.TryGetValue(cellId, out var list))
        {
            _objectShadows.Add(cellId, list = []);
        }

        list.Add(obj);
    }

    /// <summary>
    /// Retail <c>CObjCell::remove_shadow_object</c> (11.4186 0x0052B2D0, 11.6096 0x0052BEE0): the last shadow of the
    /// cell's list takes the removed one's place. An emptied list is kept for the cell's next shadow.
    /// </summary>
    internal void RemoveObjectShadow(uint cellId, PhysicsObj obj)
    {
        if (!_objectShadows.TryGetValue(cellId, out var list))
        {
            return;
        }

        var index = list.IndexOf(obj);
        if (index < 0)
        {
            return;
        }

        var last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
    }

    /// <inheritdoc/>
    List<PhysicsObj>? IPhysicsCellSource.GetObjectShadows(uint cellId) => _objectShadows.GetValueOrDefault(cellId);

    /// <inheritdoc/>
    void IPhysicsCellSource.NoteUnenforcedRestriction(uint cellId)
    {
        UnenforcedRestrictionEntries++;
        LastUnenforcedRestrictionCell = cellId;
    }

    /// <summary>
    /// Attaches a landblock's collision, replacing an earlier one for the same landblock, and registers the shadows its
    /// static objects cast into other landblocks' cells.
    /// </summary>
    public void Attach(LandblockCollision block)
    {
        ArgumentNullException.ThrowIfNull(block);
        Detach(block.Coord);
        _blocks[block.Coord] = block;
        foreach (var shadow in block.ForeignShadows)
        {
            if (shadow.FromCellBlock && Compat.AddCellBlockSameLandblockOnly)
            {
                continue;
            }

            if (!_foreignShadows.TryGetValue(shadow.CellId, out var list))
            {
                _foreignShadows.Add(shadow.CellId, list = []);
            }

            list.Add(shadow.Object);
        }

        ReenterLostObjects(block.Coord);
    }

    /// <summary>
    /// Detaches a landblock's collision; false when it was not attached. Objects in its cells leave visibility for the
    /// lost cell (retail <c>CPhysicsObj::leave_visibility</c>) and come back when it is attached again.
    /// </summary>
    public bool Detach(LandblockCoord coord)
    {
        if (!_blocks.TryGetValue(coord, out var block))
        {
            return false;
        }

        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            var obj = _objects[i];
            if (obj.Cell is not null && LandblockCoord.FromId(obj.Cell.Id) == coord)
            {
                obj.LeaveVisibility();
            }
        }

        _blocks.Remove(coord);
        foreach (var cell in block.LandCells)
        {
            _objectShadows.Remove(cell.Id);
        }

        foreach (var id in block.EnvCells.Keys)
        {
            _objectShadows.Remove(id);
        }

        foreach (var shadow in block.ForeignShadows)
        {
            if (_foreignShadows.TryGetValue(shadow.CellId, out var list))
            {
                list.Remove(shadow.Object);
                if (list.Count == 0)
                {
                    _foreignShadows.Remove(shadow.CellId);
                }
            }
        }

        return true;
    }

    /// <summary>True when the landblock's collision is attached.</summary>
    public bool IsAttached(LandblockCoord coord) => _blocks.ContainsKey(coord);

    /// <summary>
    /// Brings back the lost objects whose stored position is in <paramref name="coord"/> (retail
    /// <c>CPhysicsObj::reenter_visibility</c>, 11.4186 0x00516250, 11.6096 0x00516D50: a placement with sliding at the
    /// stored position). Which retail function calls it when a cell loads was not read; Lifestone calls it on attach.
    /// </summary>
    private void ReenterLostObjects(LandblockCoord coord)
    {
        _reentering.Clear();
        foreach (var obj in _lost)
        {
            if (LandblockCoord.FromId(obj.Position.CellId) == coord)
            {
                _reentering.Add(obj);
            }
        }

        foreach (var obj in _reentering)
        {
            obj.ReenterVisibility();
        }

        _reentering.Clear();
    }

    /// <summary>The attached landblock's collision, or null.</summary>
    public LandblockCollision? GetLandblock(LandblockCoord coord) => _blocks.GetValueOrDefault(coord);

    /// <summary>
    /// The cell with this full id (retail <c>LScape::get_landcell</c>, 11.4186 0x00505F10, 11.6096 0x00506970, for outdoor
    /// cells and <c>CEnvCell::GetVisible</c>, 0x0052DC10, 0x0052E870, for EnvCells), or null when its landblock is not
    /// attached or the cell does not exist.
    /// </summary>
    public PhysCell? GetCell(uint cellId) =>
        cellId != 0 && _blocks.TryGetValue(LandblockCoord.FromId(cellId), out var block) ? block.GetCell(cellId) : null;

    /// <summary>The static objects of other landblocks that reach into <paramref name="cellId"/>, or null.</summary>
    List<PhysStaticObject>? IPhysicsCellSource.GetForeignShadows(uint cellId) => _foreignShadows.GetValueOrDefault(cellId);

    /// <summary>
    /// Sweeps a viewer sphere of <paramref name="radius"/> from <paramref name="from"/> to <paramref name="to"/> through
    /// the attached collision, for the chase camera: the transition retail's <c>SmartBox::update_viewer</c> runs (flags
    /// IsViewer, PathClipped, FreeRotate and PerfectClip; one sphere at the frame's origin; steps of exactly one radius),
    /// so walls and floors stop the camera and its cell is known for the interiors. Retail's viewer sphere is 0.3 m.
    /// </summary>
    /// <returns>Where the sphere ended and whether collision stopped it short of <paramref name="to"/>; when the start cell
    /// is not attached or no position is valid, <paramref name="from"/> and blocked (retail then falls back to the
    /// player's own position).</returns>
    public ViewerSweepResult SweepViewer(in CellFrame from, in CellFrame to, float radius)
    {
        var beginCell = GetCell(from.CellId);
        if (beginCell is null)
        {
            return new ViewerSweepResult(from, Blocked: true);
        }

        var transition = Transitions.Rent();
        try
        {
            transition.InitObject(ViewerObject, ViewerState, PhysicsState.None, 1.0f, 0f, 0f);
            Span<PhysSphere> sphere = [new PhysSphere(Vector3.Zero, radius)];
            transition.InitSphere(sphere, 1.0f);
            transition.InitPath(beginCell, from, to);
            if (!transition.FindValidPosition())
            {
                return new ViewerSweepResult(from, Blocked: true);
            }

            var reached = transition.SpherePath.CurPos;
            var blocked = reached.GetOffset(to).LengthSquared() > PhysicsGlobals.EpsilonSquared;
            return new ViewerSweepResult(reached, blocked);
        }
        finally
        {
            Transitions.Return(transition);
        }
    }

    /// <summary>
    /// M6 8 L: whether a bolt can fly from <paramref name="start"/> to <paramref name="end"/>: ACE's launch test, <c>WorldObject.IsProjectileVisible</c> (@95ef426d WorldObjects/WorldObject.cs:402, called at WorldObject_Magic.cs:1835), which runs the projectile's own
    /// transition and ends the bolt at birth when the path does not reach the target. The same transition here: one sphere, the Missile object state with PathClipped (<c>PhysicsObj.get_object_info</c>, Physics/PhysicsObj.cs:2617), so the first contact ends it, no gravity,
    /// and every creature passed (<c>ObjectInfo.MissileIgnore</c>, Physics/ObjectInfo.cs:75-84: a missile with a target clips through creatures that are not the target; the target itself ends the path). Walls, floors, terrain, statics and doors stop it.
    /// </summary>
    /// <param name="start">Where the bolt starts; its cell is the caster's or one it sees (<see cref="AdjustPosition"/> finds which).</param>
    /// <param name="end">Where the path must reach.</param>
    /// <param name="sphere">The bolt's first collision sphere, already scaled.</param>
    /// <returns>Clear, blocked with where it stopped, or not attached (no answer: a landblock of the path is not streamed in).</returns>
    public BoltSweepResult SweepBolt(in CellFrame start, in CellFrame end, in PhysSphere sphere)
    {
        var begin = start;
        var beginCell = GetCell(start.CellId);
        if (beginCell is null || GetCell(end.CellId) is null)
        {
            return new BoltSweepResult(BoltLine.NotAttached, start);
        }

        if (AdjustPosition(ref begin, sphere.Center, searchCells: true) is { } found)
        {
            beginCell = found;
        }
        else
        {
            begin = start;
        }

        var transition = Transitions.Rent();
        try
        {
            transition.InitObject(BoltObject, ObjectInfoState.PathClipped | ObjectInfoState.FreeRotate, PhysicsState.Missile, 1.0f, 0f, 0f);
            transition.ObjectInfo.TargetId = NoBoltTarget;
            Span<PhysSphere> spheres = [sphere];
            transition.InitSphere(spheres, 1.0f);
            transition.InitPath(beginCell, begin, end);
            if (!transition.FindValidPosition())
            {
                return new BoltSweepResult(BoltLine.Blocked, begin);
            }

            var reached = transition.SpherePath.CurPos;
            var clear = !transition.CollisionInfo.CollidedWithEnvironment && reached.GetOffset(end).LengthSquared() <= BoltReachedSquared;
            return new BoltSweepResult(clear ? BoltLine.Clear : BoltLine.Blocked, reached);
        }
        finally
        {
            Transitions.Return(transition);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::AdjustPosition</c> (11.4186 0x00511D80, 11.6096 0x00512850): the cell for
    /// <paramref name="position"/>. Indoors, the EnvCell (or one it sees) that holds <paramref name="lowPoint"/> (the first
    /// sphere's centre, in the object's frame), else, when the EnvCell sees outside, the outdoor cell; outdoors, the
    /// outdoor cell under the origin. The position is moved into the found cell's landblock as needed. Null when the cell
    /// is not attached (or not found).
    /// </summary>
    internal PhysCell? AdjustPosition(ref CellFrame position, Vector3 lowPoint, bool searchCells)
    {
        var cellId = position.CellId;
        if ((cellId & 0xFFFF) >= 0x100)
        {
            if (GetCell(cellId) is not PhysEnvCell envCell)
            {
                return null;
            }

            var point = position.Frame.LocalToGlobal(lowPoint);
            var child = envCell.FindVisibleChildCell(this, point, searchCells);
            if (child is not null)
            {
                position = new CellFrame(child.Id, position.Frame);
                return child;
            }

            if (!envCell.SeenOutside)
            {
                return null;
            }
        }

        var origin = position.Frame.Origin;
        if (!LandDefs.AdjustToOutside(ref cellId, ref origin))
        {
            return null;
        }

        position = new CellFrame(cellId, position.Frame with { Origin = origin });
        return GetCell(cellId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The foot sphere is centred <paramref name="radius"/> above the origin, so on level ground <paramref name="z"/> is the
    /// ground's height; see <see cref="TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/>.
    /// </remarks>
    public bool TryGroundZ(in CellFrame at, float radius, out float z, out Plane contact) =>
        TryGroundZ(at, new PhysSphere(new Vector3(0f, 0f, radius), radius), out z, out contact);

    /// <summary>
    /// The walkable ground under an object whose lowest sphere is <paramref name="foot"/> (in the object's frame, taken
    /// upright at <paramref name="at"/>'s origin; the rotation is ignored): the origin height at which the sphere rests on
    /// it, and its plane. Step PH8, for the avatars track's ground clamp of interpolated remote objects (owner decision
    /// D24, a flagged deviation: retail steps every active object through full transitions).
    /// </summary>
    /// <remarks>
    /// <para><b>How.</b> Retail's own step down, <c>CTransition::step_down</c> (11.4186 0x0050B2A0, 11.6096 0x0050BD70):
    /// the sphere starts <see cref="GroundProbeRise"/> above the origin, in the cell <c>CPhysicsObj::AdjustPosition</c>
    /// finds for it, and steps down <see cref="GroundProbeRise"/> + <see cref="GroundProbeDrop"/>; the cells put it back on
    /// the first walkable surface the step meets, as they put a walker down: terrain, sinking by the water depth there
    /// (<c>OBJECTINFO::validate_walkable</c>), and EnvCell floors, stairs and the tops of static objects through their
    /// physics BSPs (<c>BSPTREE::step_sphere_down</c>). The surface must be walkable (normal Z at least
    /// <see cref="PhysicsGlobals.FloorZ"/>) and the sphere must fit there. Creatures, the player among them, are never
    /// ground.</para>
    /// <para><b>Answers</b> are in <paramref name="at"/>'s landblock space. False, with the outputs default, when the cell is
    /// not attached, the foot sphere is not usable, or no walkable ground lies within the window; the caller then keeps
    /// its own height.</para>
    /// <para>Main thread (or the headless tick) only. Allocation-free.</para>
    /// </remarks>
    public bool TryGroundZ(in CellFrame at, PhysSphere foot, out float z, out Plane contact) =>
        TryGroundZ(at, foot, fixedWorldOnly: false, out z, out contact);

    /// <summary>
    /// The route planner's ground probe: <see cref="TryGroundZ(in CellFrame, PhysSphere, out float, out Plane)"/> over the fixed world only
    /// (terrain, cells, buildings and landblock statics), passing every moving object's shadow (<see cref="ObjectInfo.FixedWorldOnly"/>): a
    /// closed door, an NPC or a creature standing on a sample is not a hole in the route, because the runner meets them at walk time (the
    /// door step, the creature layer, the stuck ladder). M5 item 5 fix F3. Not retail: Lifestone's own planner tool.
    /// </summary>
    /// <param name="at">Where to probe.</param>
    /// <param name="foot">The body's lowest sphere.</param>
    /// <param name="z">The origin height on the ground found.</param>
    /// <param name="contact">The ground's plane.</param>
    /// <returns>True when walkable ground lies within the window.</returns>
    public bool TryFixedGroundZ(in CellFrame at, PhysSphere foot, out float z, out Plane contact) =>
        TryGroundZ(at, foot, fixedWorldOnly: true, out z, out contact);

    private bool TryGroundZ(in CellFrame at, PhysSphere foot, bool fixedWorldOnly, out float z, out Plane contact)
    {
        z = 0f;
        contact = default;
        var origin = at.Frame.Origin;
        if (!(foot.Radius > 0f) || !float.IsFinite(foot.Radius) || !IsFinite(origin) || !IsFinite(foot.Center))
        {
            return false;
        }

        var start = new CellFrame(at.CellId, new AcFrame(origin + new Vector3(0f, 0f, GroundProbeRise), Quaternion.Identity));
        var cell = AdjustPosition(ref start, foot.Center, searchCells: true);
        if (cell is null)
        {
            return false;
        }

        var transition = Transitions.Rent();
        try
        {
            const float stepDown = GroundProbeRise + GroundProbeDrop;
            transition.InitObject(GroundProbeObject, GroundProbeState, PhysicsState.Gravity, 1.0f, 0f, stepDown);
            transition.ObjectInfo.FixedWorldOnly = fixedWorldOnly;
            Span<PhysSphere> sphere = [foot];
            transition.InitSphere(sphere, 1.0f);
            transition.InitPath(cell, start, start);
            var path = transition.SpherePath;
            path.SetCheckPos(start, cell);
            path.WalkableAllowance = PhysicsGlobals.FloorZ;
            path.SaveCheckPos();
            var collisions = transition.CollisionInfo;
            if (!transition.StepDown(stepDown, PhysicsGlobals.FloorZ) || !collisions.ContactPlaneValid)
            {
                return false;
            }

            var found = path.CheckPos;
            z = found.Frame.Origin.Z + LandDefs.GetBlockOffset(at.CellId, found.CellId).Z;
            var planeCell = collisions.ContactPlaneCellId != 0 ? collisions.ContactPlaneCellId : found.CellId;
            var plane = collisions.ContactPlane;
            contact = new Plane(plane.Normal, plane.D - Vector3.Dot(plane.Normal, LandDefs.GetBlockOffset(at.CellId, planeCell)));
            return true;
        }
        finally
        {
            Transitions.Return(transition);
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
