// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Transition.cs (AGPL-3.0)
// with changes: checked function by function against the 11.4186 decompile (CTransition.c); no per-step allocation (the
// cell array, positions and spheres are reused; ACE's LINQ ElementAt and new Position copies are gone); the cell world is
// an IPhysicsCellSource (ACE's static LScape). Retail fixes (docs/third-party/physics.md, decision table docs/physics.md
// §2): check_walkable keeps its own backup of the check position (row 2: ACE overwrites the path's), no step cap (row 3:
// ACE gives up above 1,000 steps), edge_slide restores the check position before it cliff-slides or precipice-slides
// (ACE slides first and the restore discards the slide), validate_placement_transition clears the collision record
// whatever the target (ACE: only without a target), calc_num_steps divides by the radius as retail does, and placement
// spirals use retail's pi constant 3.1415999.

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// One collision transition (retail <c>CTransition</c>): moves an object's spheres from a begin position towards an end
/// position (or places them at a position) through the cells of a <see cref="IPhysicsCellSource"/>, stepping, sliding
/// and stopping as the environment and the static objects require.
/// </summary>
/// <remarks>
/// <para><b>Use.</b> Take one from a <see cref="TransitionPool"/> (retail <c>CTransition::makeTransition</c>), call
/// <see cref="InitObject"/>, <see cref="InitSphere"/> and <see cref="InitPath"/> (and optionally the contact-plane
/// initialisers), then <see cref="FindValidPosition"/>; on success the result is <see cref="SpherePath"/>'s current
/// position and cell, with the contact plane in <see cref="CollisionInfo"/> and the touched cells in <see cref="CellArray"/>.
/// Return it to the pool afterwards (retail <c>cleanupTransition</c>).</para>
/// <para>Retail functions (11.4186 PORTAL/cphysics/CTransition.c; re-expressed, nothing copied) are cited on each member
/// with their 11.4186 and 11.6096 addresses. Not thread-safe; nothing allocates once the cell array has grown.</para>
/// </remarks>
internal sealed class Transition
{
    /// <summary>Retail <c>CTransition::find_placement_pos</c>'s pi (the float literal 3.1415999).</summary>
    private const float PlacementPi = 3.1415999f;

    /// <summary>Creates a transition in its initial state (retail <c>CTransition::CTransition</c>, 11.4186 0x0050A980, 11.6096 0x0050B450).</summary>
    /// <param name="cells">The cells it moves through (retail's <c>LScape</c> and EnvCell tables); null for tests of the primitives alone.</param>
    /// <param name="compat">ACE-compatibility switches; null is <see cref="PhysicsCompat.Retail"/>.</param>
    public Transition(IPhysicsCellSource? cells = null, PhysicsCompat? compat = null)
    {
        Cells = cells;
        Compat = compat ?? PhysicsCompat.Retail;
        Init();
    }

    /// <summary>The moving object.</summary>
    public ObjectInfo ObjectInfo { get; } = new();

    /// <summary>The moving object's path.</summary>
    public SpherePath SpherePath { get; } = new();

    /// <summary>What the transition ran into.</summary>
    public CollisionInfo CollisionInfo { get; } = new();

    /// <summary>The cells the check position touches (retail <c>cell_array</c>).</summary>
    public CellArray CellArray { get; } = new();

    /// <summary>The cells the transition moves through.</summary>
    public IPhysicsCellSource? Cells { get; set; }

    /// <summary>The behaviour switches this transition runs with.</summary>
    public PhysicsCompat Compat { get; set; }

    /// <summary>
    /// Retail <c>CTransition::init</c> (11.4186 0x00509DD0, 11.6096 0x0050A8A0): clears the object, the path, the
    /// collision record and the cell array.
    /// </summary>
    public void Init()
    {
        ObjectInfo.Object = null;
        ObjectInfo.State = ObjectInfoState.None;
        ObjectInfo.TargetId = 0;
        ObjectInfo.VelocityKilled = false;
        SpherePath.Init();
        CollisionInfo.Init();
        CellArray.Reset();
        CellArray.DoNotLoadCells = false;
    }

    /// <summary>Retail <c>CTransition::init_object</c> through <see cref="ObjectInfo.Init"/>.</summary>
    public void InitObject(object? obj, ObjectInfoState state, PhysicsState objectState, float scale, float stepUpHeight, float stepDownHeight) =>
        ObjectInfo.Init(obj, state, objectState, scale, stepUpHeight, stepDownHeight);

    /// <summary>Retail <c>CTransition::init_sphere</c> (11.4186 0x00509E50, 11.6096 0x0050A920).</summary>
    public void InitSphere(ReadOnlySpan<PhysSphere> spheres, float scale) => SpherePath.InitSphere(spheres, scale);

    /// <summary>Retail <c>CTransition::init_path</c> (11.4186 0x00509E60, 11.6096 0x0050A930).</summary>
    public void InitPath(PhysCell? beginCell, CellFrame? beginPos, in CellFrame endPos) => SpherePath.InitPath(beginCell, beginPos, endPos);

    /// <summary>
    /// Retail <c>CTransition::init_contact_plane</c> (11.4186 0x0050E850, 11.6096 0x0050F320): the object starts on this
    /// plane (and knows it as its last one).
    /// </summary>
    public void InitContactPlane(uint cellId, in Plane plane, bool isWater)
    {
        InitLastKnownContactPlane(cellId, plane, isWater);
        CollisionInfo.ContactPlaneValid = true;
        CollisionInfo.ContactPlane = plane;
        CollisionInfo.ContactPlaneIsWater = isWater;
        CollisionInfo.ContactPlaneCellId = cellId;
    }

    /// <summary>Retail <c>CTransition::init_last_known_contact_plane</c> (11.4186 0x0050E8E0, 11.6096 0x0050F3B0).</summary>
    public void InitLastKnownContactPlane(uint cellId, in Plane plane, bool isWater)
    {
        CollisionInfo.LastKnownContactPlaneValid = true;
        CollisionInfo.LastKnownContactPlane = plane;
        CollisionInfo.LastKnownContactPlaneIsWater = isWater;
        CollisionInfo.LastKnownContactPlaneCellId = cellId;
    }

    /// <summary>Retail <c>CTransition::init_sliding_normal</c> (11.4186 0x0050FF20, 11.6096 0x005109F0).</summary>
    public void InitSlidingNormal(Vector3 normal)
    {
        CollisionInfo.SlidingNormalValid = true;
        CollisionInfo.SlidingNormal = normal;
        if (VectorMath.NormalizeCheckSmall(ref CollisionInfo.SlidingNormal))
        {
            CollisionInfo.SlidingNormal = Vector3.Zero;
        }
    }

    /// <summary>
    /// Retail <c>CTransition::check_collisions</c> (11.4186 0x0050AA00, 11.6096 0x0050B4D0): whether the object, placed at
    /// its current position (a placement at the path's current position and cell, the cell array to be rebuilt), runs
    /// into <paramref name="obj"/>.
    /// </summary>
    public bool CheckCollisions(Objects.PhysicsObj obj)
    {
        var path = SpherePath;
        path.InsertType = InsertType.Placement;
        path.CheckPos = path.CurPos;
        path.CheckCell = path.CurCell;
        path.CellArrayValid = false;
        path.CacheGlobalSphere();
        return obj.FindObjCollisions(this) != TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>CTransition::find_valid_position</c> (11.4186 0x0050C310, 11.6096 0x0050CDE0): a transition from the begin
    /// position, or a placement at the end position.
    /// </summary>
    public bool FindValidPosition() =>
        SpherePath.InsertType == InsertType.Transition ? FindTransitionalPosition() : FindPlacementPosition();

    /// <summary>
    /// Retail <c>CTransition::insert_into_cell</c> (11.4186 0x00509E70, 11.6096 0x0050A940): up to
    /// <paramref name="attempts"/> collision passes in one cell, until it answers OK or Collided. A slide drops the
    /// contact plane.
    /// </summary>
    public TransitionState InsertIntoCell(PhysCell? cell, int attempts)
    {
        if (cell is null)
        {
            return TransitionState.Collided;
        }

        var state = TransitionState.OK;
        for (var i = 0; i < attempts; i++)
        {
            state = cell.FindCollisions(this);
            switch (state)
            {
                case TransitionState.OK:
                case TransitionState.Collided:
                    return state;
                case TransitionState.Slid:
                    CollisionInfo.ContactPlaneValid = false;
                    CollisionInfo.ContactPlaneIsWater = false;
                    break;
            }
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CTransition::build_cell_array</c> (11.4186 0x00509EE0, 11.6096 0x0050A9B0): the cells around the check
    /// position, and with <paramref name="wantNewCell"/> the one that contains its first sphere's centre.
    /// </summary>
    public void BuildCellArray(ref PhysCell? newCell, bool wantNewCell)
    {
        SpherePath.CellArrayValid = true;
        SpherePath.HitsInteriorCell = false;
        CellLists.FindCellList(RequireCells(), CellArray, ref newCell, wantNewCell, SpherePath);
    }

    /// <summary>
    /// Retail <c>CTransition::calc_num_steps</c> (11.4186 0x0050A0B0, 11.6096 0x0050AB80): how many steps the move from
    /// the begin to the end position takes, each at most the first sphere's radius long. A viewer always takes
    /// <c>floor(distance / radius) + 1</c> steps of exactly one radius (the last is shortened in
    /// <see cref="FindTransitionalPosition"/>); a placement takes one step of nothing.
    /// </summary>
    public void CalcNumSteps(out Vector3 offset, out Vector3 offsetPerStep, out int numSteps)
    {
        var path = SpherePath;
        if (!path.HasBeginPos)
        {
            offset = Vector3.Zero;
            offsetPerStep = Vector3.Zero;
            numSteps = 1;
            return;
        }

        offset = path.BeginPos.GetOffset(path.EndPos);
        double radius = path.LocalSphere[0].Radius;
        var distance = Math.Sqrt(((double)offset.X * offset.X) + ((double)offset.Y * offset.Y) + ((double)offset.Z * offset.Z));
        if ((ObjectInfo.State & ObjectInfoState.IsViewer) != 0)
        {
            if (distance <= PhysicsGlobals.Epsilon)
            {
                offsetPerStep = Vector3.Zero;
                numSteps = 0;
                return;
            }

            var steps = distance / radius;
            var inverse = 1.0 / steps;
            offsetPerStep = new Vector3((float)(inverse * offset.X), (float)(inverse * offset.Y), (float)(inverse * offset.Z));
            numSteps = (int)Math.Floor(steps) + 1;
            return;
        }

        var ratio = distance / (1.0 * radius);
        if (ratio > 1.0)
        {
            var count = Math.Ceiling(ratio);
            var inverse = 1.0 / count;
            offsetPerStep = new Vector3((float)(inverse * offset.X), (float)(inverse * offset.Y), (float)(inverse * offset.Z));
            numSteps = (int)count;
        }
        else if (offset.X != 0.0f || offset.Y != 0.0f || offset.Z != 0.0f)
        {
            offsetPerStep = offset;
            numSteps = 1;
        }
        else
        {
            offsetPerStep = Vector3.Zero;
            numSteps = 0;
        }
    }

    /// <summary>
    /// Retail <c>CTransition::adjust_offset</c> (11.4186 0x0050A370, 11.6096 0x0050AE40): the step's offset bent by what
    /// the last step learned. A move into the sliding normal follows the edge between it and the contact plane (or loses
    /// its part along the normal without a plane); a move into the contact plane loses its part along the normal; a move
    /// away from it is snapped onto it. A sphere sunk into a solid contact plane is lifted back onto it.
    /// </summary>
    public Vector3 AdjustOffset(Vector3 offset)
    {
        var collisions = CollisionInfo;
        var slide = false;
        if (collisions.SlidingNormalValid)
        {
            if (Vector3.Dot(offset, collisions.SlidingNormal) < 0.0f)
            {
                slide = true;
            }
            else
            {
                collisions.SlidingNormalValid = false;
            }
        }

        if (!collisions.ContactPlaneValid)
        {
            if (slide)
            {
                offset -= collisions.SlidingNormal * Vector3.Dot(offset, collisions.SlidingNormal);
            }

            return offset;
        }

        var planeNormal = collisions.ContactPlane.Normal;
        var intoPlane = Vector3.Dot(offset, planeNormal);
        if (slide)
        {
            var edge = Vector3.Cross(planeNormal, collisions.SlidingNormal);
            offset = VectorMath.NormalizeCheckSmall(ref edge) ? Vector3.Zero : edge * Vector3.Dot(edge, offset);
        }
        else if (intoPlane <= 0.0f)
        {
            offset -= planeNormal * intoPlane;
        }
        else
        {
            PlaneMath.SnapToPlane(collisions.ContactPlane, ref offset);
        }

        if (!collisions.ContactPlaneIsWater && collisions.ContactPlaneCellId != 0)
        {
            var path = SpherePath;
            var blockOffset = LandDefs.GetBlockOffset(path.CheckPos.CellId, collisions.ContactPlaneCellId);
            ref readonly var sphere = ref path.GlobalSphere[0];
            var distance = collisions.ContactPlane.DistanceTo(sphere.Center - blockOffset);
            if (distance < sphere.Radius - PhysicsGlobals.Epsilon)
            {
                var lift = (sphere.Radius - distance) / planeNormal.Z;
                if (sphere.Radius > MathF.Abs(lift))
                {
                    path.AddOffsetToCheckPos(new Vector3(0f, 0f, lift));
                }
            }
        }

        return offset;
    }

    /// <summary>
    /// Retail <c>CTransition::cliff_slide</c> (11.4186 0x0050A6D0, 11.6096 0x0050B1A0): the object stood on
    /// <paramref name="contactPlane"/>'s steep neighbour; it is pushed back along the horizontal line where the two planes
    /// meet, and that line's normal becomes the collision normal. Planes that do not meet leave it alone.
    /// </summary>
    public TransitionState CliffSlide(in Plane contactPlane)
    {
        var line = Vector3.Cross(contactPlane.Normal, CollisionInfo.LastKnownContactPlane.Normal);
        var normal = new Vector3(-line.Y, line.X, 0f);
        if (VectorMath.NormalizeCheckSmall(ref normal))
        {
            return TransitionState.OK;
        }

        var path = SpherePath;
        var movement = path.GetCurrPosCheckPosBlockOffset() + (path.GlobalSphere[0].Center - path.GlobalCurrCenter[0]);
        var along = Vector3.Dot(movement, normal);
        if (along <= 0.0f)
        {
            path.AddOffsetToCheckPos(normal * along);
            CollisionInfo.SetCollisionNormal(normal);
        }
        else
        {
            path.AddOffsetToCheckPos(normal * -along);
            CollisionInfo.SetCollisionNormal(-normal);
        }

        return TransitionState.Adjusted;
    }

    /// <summary>
    /// Retail <c>CTransition::validate_transition</c> (11.4186 0x0050AA70, 11.6096 0x0050B540): accepts the step (the check
    /// position becomes current) or, after a collision, puts the object back at the current position, resting on the last
    /// known contact plane when it still touches it. Then the stationary-fall failsafe (an object with gravity that has
    /// not moved for three steps gets a horizontal floor under its sphere) and the contact and walkable flags.
    /// </summary>
    public TransitionState ValidateTransition(TransitionState state, out int redo)
    {
        redo = 0;
        var path = SpherePath;
        var collisions = CollisionInfo;
        var moved = true;
        if (state != TransitionState.OK || (path.CheckPos.CellId == path.CurPos.CellId && FrameIsEqual(path.CheckPos.Frame, path.CurPos.Frame)))
        {
            moved = false;
            if (state != TransitionState.OK)
            {
                if (state is > TransitionState.OK and <= TransitionState.Slid)
                {
                    if (collisions.LastKnownContactPlaneValid)
                    {
                        ObjectInfo.KillVelocity();
                        var distance = collisions.LastKnownContactPlane.DistanceTo(path.GlobalCurrCenter[0]);
                        if (path.GlobalSphere[0].Radius + PhysicsGlobals.Epsilon > MathF.Abs(distance))
                        {
                            collisions.SetContactPlane(collisions.LastKnownContactPlane, collisions.LastKnownContactPlaneIsWater);
                            collisions.ContactPlaneCellId = collisions.LastKnownContactPlaneCellId;
                            if ((ObjectInfo.State & ObjectInfoState.OnWalkable) != 0)
                            {
                                moved = true;
                            }
                        }
                    }

                    if (!collisions.CollisionNormalValid)
                    {
                        collisions.SetCollisionNormal(Vector3.UnitZ);
                    }

                    path.SetCheckPos(path.CurPos, path.CurCell);
                    PhysCell? ignored = null;
                    BuildCellArray(ref ignored, wantNewCell: false);
                    state = TransitionState.OK;
                }
            }
            else
            {
                SetCurrentCheckPos();
            }
        }
        else
        {
            SetCurrentCheckPos();
        }

        if (collisions.CollisionNormalValid)
        {
            collisions.SetSlidingNormal(collisions.CollisionNormal);
        }

        if ((ObjectInfo.State & ObjectInfoState.IsViewer) == 0 && (ObjectInfo.ObjectState & PhysicsState.Gravity) != 0)
        {
            if (moved)
            {
                collisions.FramesStationaryFall = 0;
            }
            else if (collisions.FramesStationaryFall == 0)
            {
                collisions.FramesStationaryFall = 1;
            }
            else if (collisions.FramesStationaryFall == 1)
            {
                collisions.FramesStationaryFall = 2;
            }
            else
            {
                collisions.FramesStationaryFall = 3;
                ref readonly var sphere = ref path.GlobalSphere[0];
                var floor = new Plane(Vector3.UnitZ, sphere.Radius - sphere.Center.Z);
                collisions.SetContactPlane(floor, isWater: false);
                collisions.ContactPlaneCellId = path.CheckPos.CellId;
                if ((ObjectInfo.State & ObjectInfoState.Contact) == 0)
                {
                    collisions.SetCollisionNormal(Vector3.UnitZ);
                    collisions.CollidedWithEnvironment = true;
                }
            }
        }

        collisions.LastKnownContactPlaneValid = collisions.ContactPlaneValid;
        if (collisions.ContactPlaneValid)
        {
            collisions.LastKnownContactPlane = collisions.ContactPlane;
            collisions.LastKnownContactPlaneCellId = collisions.ContactPlaneCellId;
            collisions.LastKnownContactPlaneIsWater = collisions.ContactPlaneIsWater;
            ObjectInfo.State |= ObjectInfoState.Contact;
            if (ObjectInfo.IsValidWalkable(collisions.ContactPlane.Normal))
            {
                ObjectInfo.State |= ObjectInfoState.OnWalkable;
            }
            else
            {
                ObjectInfo.State &= ~ObjectInfoState.OnWalkable;
            }
        }
        else
        {
            ObjectInfo.State &= ~(ObjectInfoState.Contact | ObjectInfoState.OnWalkable);
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CTransition::validate_placement_transition</c> (11.4186 0x0050ADC0, 11.6096 0x0050B890): an accepted
    /// placement step becomes current; a refused one, when placement may slide, clears the collision record. ACE clears it
    /// only for objects without a target.
    /// </summary>
    public TransitionState ValidatePlacementTransition(TransitionState state, out int redo)
    {
        redo = 0;
        var path = SpherePath;
        if (path.CheckCell is null)
        {
            return TransitionState.Collided;
        }

        if (state == TransitionState.OK)
        {
            path.CurPos = path.CheckPos;
            path.CurCell = path.CheckCell;
            path.CacheGlobalCurrCenter();
        }
        else if (state is > TransitionState.OK and <= TransitionState.Slid && path.PlacementAllowsSliding)
        {
            CollisionInfo.Init();
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CTransition::check_other_cells</c> (11.4186 0x0050AE50, 11.6096 0x0050B920): the collision pass in every
    /// other cell the check position touches; then the check position moves into the cell that contains it, or, outdoors,
    /// into the land cell under it. A step down that ends in no cell is refused.
    /// </summary>
    public TransitionState CheckOtherCells(PhysCell? currCell)
    {
        var path = SpherePath;
        var result = TransitionState.OK;
        PhysCell? newCell = null;
        path.CellArrayValid = true;
        path.HitsInteriorCell = false;
        CellLists.FindCellList(RequireCells(), CellArray, ref newCell, wantCurrCell: true, path);
        for (var i = 0; i < CellArray.Count; i++)
        {
            var cell = CellArray.CellAt(i);
            if (cell is null || ReferenceEquals(cell, currCell))
            {
                continue;
            }

            result = cell.FindCollisions(this);
            switch (result)
            {
                case TransitionState.Slid:
                    CollisionInfo.ContactPlaneValid = false;
                    CollisionInfo.ContactPlaneIsWater = false;
                    return result;
                case TransitionState.Collided:
                case TransitionState.Adjusted:
                    return result;
            }
        }

        path.CheckCell = newCell;
        if (newCell is not null)
        {
            path.AdjustCheckPos(newCell.Id);
            return result;
        }

        if (path.StepDown)
        {
            return TransitionState.Collided;
        }

        var cellId = path.CheckPos.CellId;
        var origin = path.CheckPos.Frame.Origin;
        if ((cellId & 0xFFFF) < 0x100)
        {
            LandDefs.AdjustToOutside(ref cellId, ref origin);
        }

        if (cellId == 0)
        {
            return TransitionState.Collided;
        }

        path.AdjustCheckPos(cellId);
        path.SetCheckPos(new CellFrame(cellId, path.CheckPos.Frame with { Origin = origin }), null);
        path.CellArrayValid = true;
        return result;
    }

    /// <summary>
    /// Retail <c>CTransition::check_walkable</c> (11.4186 0x0050AFF0, 11.6096 0x0050BAC0): whether there is walkable ground
    /// under an object on walkable ground: a trial step down (half the sphere for a tall step) must find some. Retail keeps
    /// the check position in a local and puts it back; <see cref="PhysicsCompat.CheckWalkableOverwritesPathBackup"/>
    /// reproduces ACE, which saves it in the path's own backup.
    /// </summary>
    public bool CheckWalkable(float zCheck)
    {
        var path = SpherePath;
        if ((ObjectInfo.State & ObjectInfoState.OnWalkable) == 0 || path.CheckWalkables())
        {
            return true;
        }

        var savedPos = path.CheckPos;
        var savedCell = path.CheckCell;
        if (Compat.CheckWalkableOverwritesPathBackup)
        {
            path.BackupCheckPos = savedPos;
            path.BackupCell = savedCell;
        }

        var stepHeight = ObjectInfo.StepDownHeight;
        path.WalkableAllowance = zCheck;
        path.CheckWalkable = true;
        ref readonly var sphere = ref path.GlobalSphere[0];
        if (path.NumSphere < 2 && stepHeight > sphere.Radius + sphere.Radius)
        {
            stepHeight = sphere.Radius * 0.5f;
        }

        if (stepHeight > sphere.Radius + sphere.Radius)
        {
            stepHeight *= 0.5f;
        }

        path.AddOffsetToCheckPos(new Vector3(0f, 0f, -stepHeight));
        var state = TransitionalInsert(1);
        path.CheckWalkable = false;
        path.SetCheckPos(savedPos, savedCell);
        return state != TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>CTransition::placement_insert</c> (11.4186 0x0050B1D0, 11.6096 0x0050BCA0): the placement pass in the
    /// check cell, then in the other cells.
    /// </summary>
    public TransitionState PlacementInsert()
    {
        var cell = SpherePath.CheckCell;
        if (cell is null)
        {
            return TransitionState.Collided;
        }

        var state = InsertIntoCell(cell, 3);
        return state == TransitionState.OK ? CheckOtherCells(SpherePath.CheckCell) : state;
    }

    /// <summary>
    /// Retail <c>CTransition::validate_placement</c> (11.4186 0x0050B210, 11.6096 0x0050BCE0): an accepted placement becomes
    /// current; an adjusted or slid one is placed once more (with <paramref name="adjust"/>).
    /// </summary>
    public TransitionState ValidatePlacement(TransitionState state, bool adjust)
    {
        var path = SpherePath;
        if (path.CheckCell is null)
        {
            return TransitionState.Collided;
        }

        if (state == TransitionState.OK)
        {
            path.CurPos = path.CheckPos;
            path.CurCell = path.CheckCell;
            path.CacheGlobalCurrCenter();
        }
        else if (state is > TransitionState.Collided and <= TransitionState.Slid && adjust)
        {
            return ValidatePlacement(PlacementInsert(), false);
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CTransition::step_down</c> (11.4186 0x0050B2A0, 11.6096 0x0050BD70): lowers the check position by
    /// <paramref name="stepDownHeight"/> (unless a step up already raised it) and lets the cells put it back on the ground.
    /// It succeeds when the ground found is at least <paramref name="zVal"/> steep, the walkable check (for edge-sliding
    /// objects) agrees, and the object can be placed there.
    /// </summary>
    public bool StepDown(float stepDownHeight, float zVal)
    {
        var path = SpherePath;
        path.NegPolyHit = false;
        path.StepDown = true;
        path.StepDownAmt = stepDownHeight;
        path.WalkInterp = 1.0f;
        if (!path.StepUp)
        {
            path.CellArrayValid = false;
            var offset = new Vector3(0f, 0f, -stepDownHeight);
            path.CheckPos = path.CheckPos.AddOffset(offset);
            path.CacheGlobalSphere(offset);
        }

        var state = TransitionalInsert(5);
        path.StepDown = false;
        if (state == TransitionState.OK
            && CollisionInfo.ContactPlaneValid
            && CollisionInfo.ContactPlane.Normal.Z >= zVal
            && ((ObjectInfo.State & ObjectInfoState.EdgeSlide) == 0 || path.StepUp || CheckWalkable(zVal)))
        {
            path.Backup = path.InsertType;
            path.InsertType = InsertType.Placement;
            state = TransitionalInsert(1);
            path.InsertType = path.Backup;
            return state == TransitionState.OK;
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CTransition::edge_slide</c> (11.4186 0x0050B3D0, 11.6096 0x0050BEA0): what an object that found no ground
    /// does. Without OnWalkable and EdgeSlide it simply goes on (and falls). Otherwise it slides along a cliff (a steep
    /// contact plane), along the edge of the walkable polygon it stood on, or, after a trial step down from its current
    /// centre, along the edge of the polygon that finds; with none of these it is stopped. Retail puts the check position
    /// back before each slide; ACE slides first and the restore undoes the slide. True when <paramref name="state"/> is the
    /// transition's answer.
    /// </summary>
    public bool EdgeSlide(ref TransitionState state, float stepDownHeight, float zVal)
    {
        var path = SpherePath;
        var collisions = CollisionInfo;
        if ((ObjectInfo.State & ObjectInfoState.OnWalkable) == 0 || (ObjectInfo.State & ObjectInfoState.EdgeSlide) == 0)
        {
            path.Walkable = null;
            path.RestoreCheckPos();
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            path.CellArrayValid = true;
            state = TransitionState.OK;
            return true;
        }

        if (collisions.ContactPlaneValid && collisions.ContactPlane.Normal.Z < zVal)
        {
            path.Walkable = null;
            path.RestoreCheckPos();
            state = CliffSlide(collisions.ContactPlane);
            collisions.EdgeRefused = true;
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            return false;
        }

        if (path.Walkable is not null)
        {
            path.RestoreCheckPos();
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            state = path.PrecipiceSlide(this);
            collisions.EdgeRefused = true;
            return state == TransitionState.Collided;
        }

        if (collisions.ContactPlaneValid)
        {
            path.Walkable = null;
            path.RestoreCheckPos();
            path.CellArrayValid = true;
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            state = TransitionState.OK;
            return true;
        }

        path.AddOffsetToCheckPos(path.GlobalCurrCenter[0] - path.GlobalSphere[0].Center);
        StepDown(stepDownHeight, zVal);
        collisions.ContactPlaneValid = false;
        collisions.ContactPlaneIsWater = false;
        path.RestoreCheckPos();
        if (path.Walkable is not null)
        {
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            path.CacheLocalSpaceSphere(path.WalkablePos, path.WalkableScale);
            path.SetWalkableCheckPos(path.LocalSpaceSphere[0]);
            collisions.EdgeRefused = true;
            state = path.PrecipiceSlide(this);
            return state == TransitionState.Collided;
        }

        path.Walkable = null;
        state = TransitionState.Collided;
        collisions.EdgeRefused = true;
        path.CellArrayValid = true;
        return true;
    }

    /// <summary>
    /// Retail <c>CTransition::step_up</c> (11.4186 0x0050B610, 11.6096 0x0050C0E0): tries to step the object up over what
    /// it hit (normal <paramref name="collisionNormal"/>): a step down by the step-up height (0.04 m off walkable ground)
    /// from where it stands now must find ground of the walkable steepness. On failure the check position is put back.
    /// </summary>
    public bool StepUp(Vector3 collisionNormal)
    {
        var path = SpherePath;
        CollisionInfo.ContactPlaneValid = false;
        CollisionInfo.ContactPlaneIsWater = false;
        path.StepUp = true;
        path.StepUpNormal = collisionNormal;
        var stepDownHeight = PhysicsGlobals.DefaultStepDownHeight;
        var zLanding = PhysicsGlobals.LandingZ;
        if ((ObjectInfo.State & ObjectInfoState.OnWalkable) != 0)
        {
            zLanding = ObjectInfo.GetWalkableZ();
            stepDownHeight = ObjectInfo.StepUpHeight;
        }

        path.WalkableAllowance = zLanding;
        path.BackupCell = path.CheckCell;
        path.BackupCheckPos = path.CheckPos;
        var stepped = StepDown(stepDownHeight, zLanding);
        path.StepUp = false;
        path.Walkable = null;
        if (!stepped)
        {
            CollisionInfo.StepRefused = true;
            path.RestoreCheckPos();
        }

        return stepped;
    }

    /// <summary>
    /// Retail <c>CTransition::transitional_insert</c> (11.4186 0x0050B6F0, 11.6096 0x0050C1C0): up to
    /// <paramref name="attempts"/> passes of the check position through its cell and the cells around it. After a clean
    /// pass: a pending collision tries to land the object (a walkable contact plane is placed, anything else puts it back
    /// and stops it), a polygon touched from behind steps up or slides, and an object in contact without a contact plane
    /// steps down onto the ground (two half steps for a tall step), or edge-slides when there is none.
    /// </summary>
    public TransitionState TransitionalInsert(int attempts)
    {
        var path = SpherePath;
        var collisions = CollisionInfo;
        if (path.CheckCell is null)
        {
            return TransitionState.OK;
        }

        if (attempts <= 0)
        {
            return TransitionState.Invalid;
        }

        var state = TransitionState.Invalid;
        for (var i = 0; i < attempts; i++)
        {
            state = InsertIntoCell(path.CheckCell, attempts);
            switch (state)
            {
                case TransitionState.OK:
                    state = CheckOtherCells(path.CheckCell);
                    if (state != TransitionState.OK)
                    {
                        path.NegPolyHit = false;
                    }

                    if (state == TransitionState.Collided)
                    {
                        return state;
                    }

                    break;
                case TransitionState.Collided:
                    path.NegPolyHit = false;
                    return state;
                case TransitionState.Adjusted:
                    path.NegPolyHit = false;
                    break;
                case TransitionState.Slid:
                    collisions.ContactPlaneValid = false;
                    collisions.ContactPlaneIsWater = false;
                    path.NegPolyHit = false;
                    break;
            }

            if (state != TransitionState.OK)
            {
                continue;
            }

            if (path.Collide)
            {
                path.Collide = false;
                var reset = false;
                if (collisions.ContactPlaneValid && CheckWalkable(PhysicsGlobals.LandingZ))
                {
                    path.Backup = path.InsertType;
                    path.InsertType = InsertType.Placement;
                    state = TransitionalInsert(attempts);
                    path.InsertType = path.Backup;
                    if (state != TransitionState.OK)
                    {
                        state = TransitionState.OK;
                        reset = true;
                    }
                }
                else
                {
                    reset = true;
                }

                path.Walkable = null;
                if (!reset)
                {
                    return state;
                }

                path.RestoreCheckPos();
                collisions.ContactPlaneValid = false;
                collisions.ContactPlaneIsWater = false;
                if (collisions.LastKnownContactPlaneValid)
                {
                    ObjectInfo.KillVelocity();
                    collisions.LastKnownContactPlaneValid = false;
                }
                else
                {
                    collisions.SetCollisionNormal(path.StepUpNormal);
                }

                return TransitionState.Collided;
            }

            if (path.NegPolyHit && !path.StepDown && !path.StepUp)
            {
                path.NegPolyHit = false;
                if (path.NegStepUp)
                {
                    if (!StepUp(path.NegCollisionNormal))
                    {
                        state = path.StepUpSlide(this);
                    }
                }
                else
                {
                    state = path.GlobalSphere[0].SlideSphere(this, ref path.NegCollisionNormal, path.GlobalCurrCenter[0]);
                }

                continue;
            }

            if (collisions.ContactPlaneValid
                || (ObjectInfo.State & ObjectInfoState.Contact) == 0
                || path.StepDown
                || path.CheckCell is null
                || !ObjectInfo.StepDown)
            {
                return TransitionState.OK;
            }

            var stepDownHeight = PhysicsGlobals.DefaultStepDownHeight;
            var zVal = PhysicsGlobals.LandingZ;
            if ((ObjectInfo.State & ObjectInfoState.OnWalkable) != 0)
            {
                zVal = ObjectInfo.GetWalkableZ();
                stepDownHeight = ObjectInfo.StepDownHeight;
            }

            path.WalkableAllowance = zVal;
            path.SaveCheckPos();
            var radius = path.GlobalSphere[0].Radius;
            if (path.NumSphere < 2 && radius + radius < stepDownHeight)
            {
                stepDownHeight = radius * 0.5f;
            }

            if (radius + radius >= stepDownHeight)
            {
                if (StepDown(stepDownHeight, zVal))
                {
                    path.Walkable = null;
                    return TransitionState.OK;
                }
            }
            else
            {
                stepDownHeight *= 0.5f;
                if (StepDown(stepDownHeight, zVal) || StepDown(stepDownHeight, zVal))
                {
                    path.Walkable = null;
                    return TransitionState.OK;
                }
            }

            if (EdgeSlide(ref state, stepDownHeight, zVal))
            {
                return state;
            }
        }

        return state;
    }

    /// <summary>
    /// Retail <c>CTransition::find_placement_pos</c> (11.4186 0x0050BA50, 11.6096 0x0050C520): places the object at its
    /// current position or, when it does not fit and may slide, at the first free spot on rings of growing radius (to
    /// 4 m, or 2 m for a sphere under 0.125 m; a sphere under 0.48 m spirals as one of 0.48 m).
    /// </summary>
    public bool FindPlacementPos()
    {
        var path = SpherePath;
        var collisions = CollisionInfo;
        path.SetCheckPos(path.CurPos, path.CurCell);
        collisions.SlidingNormalValid = false;
        collisions.ContactPlaneValid = false;
        collisions.ContactPlaneIsWater = false;
        if (ValidatePlacementTransition(TransitionalInsert(3), out _) == TransitionState.OK)
        {
            return true;
        }

        if (!path.PlacementAllowsSliding)
        {
            return false;
        }

        var sphereRadius = path.LocalSphere[0].Radius;
        var smallSphere = false;
        var adjustRadius = 4.0f;
        if (sphereRadius >= 0.125f)
        {
            if (sphereRadius < 0.48f)
            {
                sphereRadius = 0.48f;
            }
        }
        else
        {
            smallSphere = true;
            adjustRadius = 2.0f;
        }

        var steps = 4.0 / (1.0 * sphereRadius);
        if (smallSphere)
        {
            steps *= 0.5;
        }

        if (steps <= 1.0)
        {
            return false;
        }

        var stepCount = Math.Ceiling(steps);
        var distancePerStep = (float)(adjustRadius / stepCount);
        var numSteps = (int)stepCount;
        var radialPerStep = distancePerStep / sphereRadius * PlacementPi;
        var distance = 0.0f;
        var radial = 0.0f;
        for (var i = 0; i < numSteps; i++)
        {
            distance += distancePerStep;
            radial += radialPerStep;
            var numRadial = 2 * (int)Math.Ceiling(radial);
            var angleOffset = (float)(360.0 / numRadial);
            var offsetFrame = AcFrame.Identity;
            for (var j = 0; j < numRadial; j++)
            {
                path.SetCheckPos(path.CurPos, path.CurCell);
                offsetFrame = RetailHeadings.SetHeading(offsetFrame, j * angleOffset);
                var offset = RetailHeadings.GetVectorHeading(offsetFrame) * distance;
                path.GlobalOffset = AdjustOffset(offset);
                if (path.GlobalOffset.Length() < PhysicsGlobals.Epsilon)
                {
                    continue;
                }

                path.AddOffsetToCheckPos(path.GlobalOffset);
                collisions.SlidingNormalValid = false;
                collisions.ContactPlaneValid = false;
                collisions.ContactPlaneIsWater = false;
                if (ValidatePlacementTransition(TransitionalInsert(3), out _) == TransitionState.OK)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CTransition::find_transitional_position</c> (11.4186 0x0050BDF0, 11.6096 0x0050C8C0): the move in steps of
    /// at most a sphere radius (<see cref="CalcNumSteps"/>), each bent by <see cref="AdjustOffset"/>, inserted and validated.
    /// It stops early when a step is too short to matter, when a stationary fall is detected, or when a path-clipped
    /// object collides. Retail has no step cap; <see cref="PhysicsCompat.MaxTransitionSteps"/> reproduces ACE's.
    /// </summary>
    public bool FindTransitionalPosition()
    {
        var path = SpherePath;
        var collisions = CollisionInfo;
        if (path.BeginCell is null)
        {
            return false;
        }

        var state = TransitionState.OK;
        CalcNumSteps(out var offset, out var offsetPerStep, out var numSteps);
        if (Compat.MaxTransitionSteps is { } cap && numSteps > cap)
        {
            return false;
        }

        if ((ObjectInfo.State & ObjectInfoState.FreeRotate) != 0)
        {
            path.CurPos = path.CurPos with { Frame = path.CurPos.Frame with { Orientation = AcFrame.SetRotate(path.EndPos.Frame.Orientation, path.CurPos.Frame.Orientation) } };
        }

        path.SetCheckPos(path.CurPos, path.CurCell);
        if (numSteps <= 0)
        {
            if ((ObjectInfo.State & ObjectInfoState.FreeRotate) == 0)
            {
                path.CurPos = path.CurPos with { Frame = path.CurPos.Frame with { Orientation = AcFrame.SetRotate(path.EndPos.Frame.Orientation, path.CurPos.Frame.Orientation) } };
            }

            path.SetCheckPos(path.CurPos, path.CurCell);
            PhysCell? ignored = null;
            BuildCellArray(ref ignored, wantNewCell: false);
            return true;
        }

        for (var step = 0; step < numSteps; step++)
        {
            if ((ObjectInfo.State & ObjectInfoState.IsViewer) != 0 && step == numSteps - 1)
            {
                var length = Math.Sqrt(((double)offset.Z * offset.Z) + ((double)offset.Y * offset.Y) + ((double)offset.X * offset.X));
                if (length > PhysicsGlobals.Epsilon)
                {
                    var fraction = (length - (path.LocalSphere[0].Radius * (double)(numSteps - 1))) / length;
                    offsetPerStep = new Vector3((float)(offset.X * fraction), (float)(offset.Y * fraction), (float)(offset.Z * fraction));
                }
            }

            path.GlobalOffset = AdjustOffset(offsetPerStep);
            if ((ObjectInfo.State & ObjectInfoState.IsViewer) == 0
                && path.GlobalOffset.LengthSquared() < PhysicsGlobals.EpsilonSquared)
            {
                return step != 0 && state == TransitionState.OK;
            }

            if ((ObjectInfo.State & ObjectInfoState.FreeRotate) == 0)
            {
                var t = (float)((step + 1) / (double)numSteps);
                path.CheckPos = path.CheckPos with { Frame = path.CheckPos.Frame with { Orientation = InterpolateRotation(path.BeginPos.Frame.Orientation, path.EndPos.Frame.Orientation, t, path.CheckPos.Frame.Orientation) } };
            }

            collisions.SlidingNormalValid = false;
            collisions.ContactPlaneValid = false;
            collisions.ContactPlaneIsWater = false;
            if (path.InsertType != InsertType.Transition)
            {
                state = ValidatePlacementTransition(TransitionalInsert(3), out _);
                if (state == TransitionState.OK)
                {
                    return true;
                }

                if (!path.PlacementAllowsSliding)
                {
                    return false;
                }

                path.AddOffsetToCheckPos(path.GlobalOffset);
            }
            else
            {
                path.CellArrayValid = false;
                path.CheckPos = path.CheckPos.AddOffset(path.GlobalOffset);
                path.CacheGlobalSphere(path.GlobalOffset);
                state = ValidateTransition(TransitionalInsert(3), out _);
                if (collisions.FramesStationaryFall != 0)
                {
                    break;
                }
            }

            if (collisions.CollisionNormalValid && (ObjectInfo.State & ObjectInfoState.PathClipped) != 0)
            {
                break;
            }
        }

        return state == TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>CTransition::find_placement_position</c> (11.4186 0x0050C170, 11.6096 0x0050CC40): an initial placement
    /// in the current cell, then <see cref="FindPlacementPos"/>, then for objects that step down a landing step (two half
    /// steps for a tall one) so the object rests on the ground, without failing when there is none.
    /// </summary>
    public bool FindPlacementPosition()
    {
        var path = SpherePath;
        path.SetCheckPos(path.CurPos, path.CurCell);
        path.InsertType = InsertType.InitialPlacement;
        TransitionState state;
        if (path.CheckCell is not null)
        {
            state = InsertIntoCell(path.CheckCell, 3);
            if (state == TransitionState.OK)
            {
                state = CheckOtherCells(path.CheckCell);
            }
        }
        else
        {
            state = TransitionState.Collided;
        }

        if (ValidatePlacement(state, true) != TransitionState.OK)
        {
            return false;
        }

        path.InsertType = InsertType.Placement;
        if (!FindPlacementPos())
        {
            return false;
        }

        if (!ObjectInfo.StepDown)
        {
            return ValidatePlacement(TransitionState.OK, true) == TransitionState.OK;
        }

        var stepDownHeight = ObjectInfo.StepDownHeight;
        path.WalkableAllowance = PhysicsGlobals.LandingZ;
        path.SaveCheckPos();
        path.Backup = path.InsertType;
        path.InsertType = InsertType.Transition;
        var radius = path.GlobalSphere[0].Radius;
        if (path.NumSphere < 2 && radius + radius < stepDownHeight)
        {
            stepDownHeight = radius * 0.5f;
        }

        bool landed;
        if (radius + radius >= stepDownHeight)
        {
            landed = StepDown(stepDownHeight, PhysicsGlobals.LandingZ);
        }
        else
        {
            stepDownHeight *= 0.5f;
            landed = StepDown(stepDownHeight, PhysicsGlobals.LandingZ) || StepDown(stepDownHeight, PhysicsGlobals.LandingZ);
        }

        if (!landed)
        {
            path.RestoreCheckPos();
            CollisionInfo.ContactPlaneValid = false;
            CollisionInfo.ContactPlaneIsWater = false;
        }

        path.InsertType = path.Backup;
        path.Walkable = null;
        return ValidatePlacement(TransitionState.OK, true) == TransitionState.OK;
    }

    /// <summary>
    /// Retail <c>Frame::is_equal</c> (11.4186 0x00424C30, 11.6096 0x00424E90): <see cref="RetailFrames.FrameIsEqual"/>.
    /// </summary>
    internal static bool FrameIsEqual(in AcFrame a, in AcFrame b) => RetailFrames.FrameIsEqual(a, b);

    /// <summary>
    /// Retail <c>Frame::interpolate_rotation</c> (11.4186 0x00535390, 11.6096 0x005360D0): the slerp from
    /// <paramref name="from"/> to <paramref name="to"/> (the shorter way round), falling back to the linear blend when the
    /// two are within the epsilon or a weight leaves [0, 1]; renormalised by <c>Frame::set_rotate</c>.
    /// </summary>
    internal static Quaternion InterpolateRotation(Quaternion from, Quaternion to, float t, Quaternion previous)
    {
        var cosom = ((double)from.Y * to.Y) + ((double)from.W * to.W) + ((double)from.X * to.X) + ((double)from.Z * to.Z);
        var target = to;
        if (cosom < 0.0)
        {
            cosom = -cosom;
            target = new Quaternion(-to.X, -to.Y, -to.Z, -to.W);
        }

        double fromWeight;
        double toWeight;
        if (1.0 - cosom <= PhysicsGlobals.Epsilon)
        {
            fromWeight = 1.0 - t;
            toWeight = t;
        }
        else
        {
            var omega = Math.Acos(cosom);
            var inverseSin = 1.0 / Math.Sin(omega);
            fromWeight = inverseSin * Math.Sin(omega * (1.0 - t));
            toWeight = Math.Sin(t * omega) * inverseSin;
            if (fromWeight < 0.0 || fromWeight > 1.0 || toWeight < 0.0 || toWeight > 1.0)
            {
                fromWeight = 1.0 - t;
                toWeight = t;
            }
        }

        var blended = new Quaternion(
            (float)((from.X * fromWeight) + (target.X * toWeight)),
            (float)((from.Y * fromWeight) + (target.Y * toWeight)),
            (float)((target.Z * toWeight) + (from.Z * fromWeight)),
            (float)((fromWeight * from.W) + (target.W * toWeight)));
        return AcFrame.SetRotate(blended, previous);
    }

    /// <summary>
    /// Retail <c>CTransition::set_current_check_pos</c> as inlined in <c>validate_transition</c>: the check position becomes
    /// the current one, and testing restarts from it.
    /// </summary>
    private void SetCurrentCheckPos()
    {
        var path = SpherePath;
        path.CurPos = path.CheckPos;
        path.CurCell = path.CheckCell;
        path.CacheGlobalCurrCenter();
        path.SetCheckPos(path.CurPos, path.CurCell);
    }

    private IPhysicsCellSource RequireCells() =>
        Cells ?? throw new InvalidOperationException("The transition has no cells to move through (Transition.Cells is null).");
}
