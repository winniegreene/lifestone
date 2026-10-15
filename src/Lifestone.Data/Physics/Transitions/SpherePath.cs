// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/SpherePath.cs (AGPL-3.0)
// with changes: fixed arrays of two spheres and CellFrame positions instead of lists and Position objects, so nothing
// allocates; the current centres are points, as retail keeps them; retail fixes (docs/third-party/physics.md):
// step_up_slide clears step_up (ACE leaves it set) and check_walkables halves the stored walkable sphere's radius in place,
// as retail does (ACE halves a copy).

using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// The moving object's path through one transition (retail <c>SPHEREPATH</c>): its (at most two) spheres in local,
/// global and cell-local space, the begin, current and check positions, and the state of the step being tested.
/// </summary>
/// <remarks>One per <see cref="Transition"/>, reused; reset by <see cref="Init"/>. Not thread-safe.</remarks>
internal sealed class SpherePath
{
    /// <summary>A path follows at most two spheres (retail <c>init_sphere</c> caps the count at 2).</summary>
    public const int MaxSpheres = 2;

    /// <summary>How many spheres the object has (0 to 2).</summary>
    public int NumSphere;

    /// <summary>The object's spheres in its own frame, scaled.</summary>
    public readonly PhysSphere[] LocalSphere = new PhysSphere[MaxSpheres];

    /// <summary>The bottom of the first local sphere.</summary>
    public Vector3 LocalLowPoint;

    /// <summary>The spheres at the check position, in its landblock space.</summary>
    public readonly PhysSphere[] GlobalSphere = new PhysSphere[MaxSpheres];

    /// <summary>The bottom of the first global sphere.</summary>
    public Vector3 GlobalLowPoint;

    /// <summary>The spheres at the check position in the space of the object being tested against.</summary>
    public readonly PhysSphere[] LocalSpaceSphere = new PhysSphere[MaxSpheres];

    /// <summary>The bottom of the first local-space sphere along <see cref="LocalSpaceZ"/>.</summary>
    public Vector3 LocalSpaceLowPoint;

    /// <summary>The sphere centres at the current position in the space of the object being tested against.</summary>
    public readonly Vector3[] LocalSpaceCurrCenter = new Vector3[MaxSpheres];

    /// <summary>The sphere centres at the current position, in the check position's landblock space.</summary>
    public readonly Vector3[] GlobalCurrCenter = new Vector3[MaxSpheres];

    /// <summary>The position of the object being tested against (<see cref="CacheLocalSpaceSphere"/>).</summary>
    public CellFrame LocalSpacePos = new(0, AcFrame.Identity);

    /// <summary>World up in the local space of <see cref="LocalSpacePos"/>.</summary>
    public Vector3 LocalSpaceZ;

    /// <summary>The cell the path begins in.</summary>
    public PhysCell? BeginCell;

    /// <summary>True when the path has a begin position (a transition rather than a placement).</summary>
    public bool HasBeginPos;

    /// <summary>The begin position (valid when <see cref="HasBeginPos"/>).</summary>
    public CellFrame BeginPos = new(0, AcFrame.Identity);

    /// <summary>The end position.</summary>
    public CellFrame EndPos = new(0, AcFrame.Identity);

    /// <summary>The cell of the current position.</summary>
    public PhysCell? CurCell;

    /// <summary>The last accepted position along the path.</summary>
    public CellFrame CurPos = new(0, AcFrame.Identity);

    /// <summary>The offset of the step being tested.</summary>
    public Vector3 GlobalOffset;

    /// <summary>True while a step up is being tried.</summary>
    public bool StepUp;

    /// <summary>The collision normal that started the step up.</summary>
    public Vector3 StepUpNormal;

    /// <summary>True after a collision in this step (set by <see cref="SetCollide"/>).</summary>
    public bool Collide;

    /// <summary>The cell of the check position.</summary>
    public PhysCell? CheckCell;

    /// <summary>The position being tested.</summary>
    public CellFrame CheckPos = new(0, AcFrame.Identity);

    /// <summary>How the path is inserted.</summary>
    public InsertType InsertType;

    /// <summary>True while a step down is being tried.</summary>
    public bool StepDown;

    /// <summary>The insert type saved while placing.</summary>
    public InsertType Backup;

    /// <summary>The cell of <see cref="BackupCheckPos"/>.</summary>
    public PhysCell? BackupCell;

    /// <summary>The check position saved by <see cref="SaveCheckPos"/> and <see cref="SetCollide"/>.</summary>
    public CellFrame BackupCheckPos = new(0, AcFrame.Identity);

    /// <summary>True when an ethereal obstruction is being tested.</summary>
    public bool ObstructionEthereal;

    /// <summary>True when the path hits an interior cell.</summary>
    public bool HitsInteriorCell;

    /// <summary>True while a building is being checked.</summary>
    public bool BuildingCheck;

    /// <summary>The smallest contact-normal Z that counts as walkable in this step.</summary>
    public float WalkableAllowance;

    /// <summary>How much of the step remains (1 at its start).</summary>
    public float WalkInterp;

    /// <summary>
    /// How far the step down goes, a positive height (retail <c>CTransition::step_down</c> stores its <c>step_down_ht</c>
    /// argument); the primitives scale it by <see cref="WalkInterp"/>.
    /// </summary>
    public float StepDownAmt;

    /// <summary>The walkable polygon's sphere, in the polygon's object space (retail <c>walkable_check_pos</c>).</summary>
    public PhysSphere WalkableCheckPos;

    /// <summary>The walkable polygon the object last stood on, or null (retail <c>walkable</c>).</summary>
    public PhysPolygon? Walkable;

    /// <summary>True while the walkable check runs.</summary>
    public bool CheckWalkable;

    /// <summary>World up in the walkable polygon's object space.</summary>
    public Vector3 WalkableUp;

    /// <summary>The position of the object that owns <see cref="Walkable"/>.</summary>
    public CellFrame WalkablePos = new(0, AcFrame.Identity);

    /// <summary>The scale of the object that owns <see cref="Walkable"/>.</summary>
    public float WalkableScale;

    /// <summary>True while the cell array matches the check position.</summary>
    public bool CellArrayValid;

    /// <summary>True when a negative-side polygon hit asks for a step up.</summary>
    public bool NegStepUp;

    /// <summary>The reversed normal of a negative-side polygon hit.</summary>
    public Vector3 NegCollisionNormal;

    /// <summary>True after a negative-side polygon hit.</summary>
    public bool NegPolyHit;

    /// <summary>True when placement may slide the object (retail's default).</summary>
    public bool PlacementAllowsSliding = true;

    /// <summary>Retail <c>SPHEREPATH::init</c> (11.4186 0x0050C330, 11.6096 0x0050CE00): clears the path's state.</summary>
    public void Init()
    {
        NumSphere = 0;
        BeginCell = null;
        HasBeginPos = false;
        CurCell = null;
        CheckCell = null;
        InsertType = InsertType.Transition;
        StepDown = false;
        StepUp = false;
        Collide = false;
        HitsInteriorCell = false;
        BuildingCheck = false;
        ObstructionEthereal = false;
        BackupCell = null;
        WalkableAllowance = 0f;
        Walkable = null;
        CheckWalkable = false;
        CellArrayValid = false;
        NegStepUp = false;
        NegPolyHit = false;
        PlacementAllowsSliding = true;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::init_sphere</c> (11.4186 0x0050C670, TODO(physics): 11.6096 not in the symbol table): takes the first two
    /// of <paramref name="spheres"/>, scaled, and the low point under the first.
    /// </summary>
    public void InitSphere(ReadOnlySpan<PhysSphere> spheres, float scale)
    {
        NumSphere = Math.Min(spheres.Length, MaxSpheres);
        for (var i = 0; i < NumSphere; i++)
        {
            LocalSphere[i] = new PhysSphere(spheres[i].Center * scale, spheres[i].Radius * scale);
        }

        LocalLowPoint = LocalSphere[0].Center;
        LocalLowPoint.Z -= LocalSphere[0].Radius;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::init_path</c> (11.4186 0x0050CE20, TODO(physics): 11.6096 not in the symbol table): a transition from
    /// <paramref name="beginPos"/> to <paramref name="endPos"/>, or a placement at <paramref name="endPos"/> when there is
    /// no begin position.
    /// </summary>
    public void InitPath(PhysCell? beginCell, CellFrame? beginPos, in CellFrame endPos)
    {
        BeginCell = beginCell;
        HasBeginPos = beginPos.HasValue;
        BeginPos = beginPos ?? new CellFrame(0, AcFrame.Identity);
        EndPos = endPos;
        CurPos = beginPos ?? endPos;
        CurCell = beginCell;
        CacheGlobalCurrCenter();
        InsertType = beginPos.HasValue ? InsertType.Transition : InsertType.Placement;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::add_offset_to_check_pos</c> (11.4186 0x00509D10, 11.6096 0x0050A7E0): moves the check
    /// position and its global spheres by <paramref name="offset"/>; the cell array must be rebuilt.
    /// </summary>
    public void AddOffsetToCheckPos(Vector3 offset)
    {
        CellArrayValid = false;
        CheckPos = CheckPos.AddOffset(offset);
        CacheGlobalSphere(offset);
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::add_offset_to_check_pos(offset, radius)</c> (11.4186 0x00536A60, 11.6096 0x005377A0): the
    /// same as <see cref="AddOffsetToCheckPos(Vector3)"/>; the radius is not used.
    /// </summary>
    public void AddOffsetToCheckPos(Vector3 offset, float radius)
    {
        _ = radius;
        AddOffsetToCheckPos(offset);
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::adjust_check_pos</c> (11.4186 0x0050CC00, 11.6096 0x0050D6D0): moves the check position into
    /// <paramref name="cellId"/>; for an outdoor cell the origin and spheres shift by the block offset first.
    /// </summary>
    public void AdjustCheckPos(uint cellId)
    {
        if ((cellId & 0xFFFF) < 0x100)
        {
            var offset = LandDefs.GetBlockOffset(cellId, CheckPos.CellId);
            CacheGlobalSphere(offset);
            CheckPos = CheckPos.AddOffset(offset);
        }

        CheckPos = CheckPos with { CellId = cellId };
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::cache_global_curr_center</c> (11.4186 0x0050C740, 11.6096 0x0050D210): the spheres' centres
    /// at the current position.
    /// </summary>
    public void CacheGlobalCurrCenter()
    {
        for (var i = 0; i < NumSphere; i++)
        {
            GlobalCurrCenter[i] = CurPos.Frame.LocalToGlobal(LocalSphere[i].Center);
        }
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::cache_global_sphere(null)</c> (11.4186 0x0050C810, 11.6096 0x0050D2E0): the spheres and the
    /// low point at the check position.
    /// </summary>
    public void CacheGlobalSphere()
    {
        for (var i = 0; i < NumSphere; i++)
        {
            GlobalSphere[i].Radius = LocalSphere[i].Radius;
            GlobalSphere[i].Center = CheckPos.Frame.LocalToGlobal(LocalSphere[i].Center);
        }

        GlobalLowPoint = CheckPos.Frame.LocalToGlobal(LocalLowPoint);
    }

    /// <summary>Retail <c>SPHEREPATH::cache_global_sphere(offset)</c>: moves the global spheres and the low point by <paramref name="offset"/>.</summary>
    public void CacheGlobalSphere(Vector3 offset)
    {
        for (var i = 0; i < NumSphere; i++)
        {
            GlobalSphere[i].Center += offset;
        }

        GlobalLowPoint += offset;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::cache_localspace_sphere</c> (11.4186 0x0050C9D0, 11.6096 0x0050D4A0): the spheres at the
    /// check and current positions in the space of an object at <paramref name="position"/>, divided by
    /// <paramref name="scale"/>, with world up and the low point in that space.
    /// </summary>
    public void CacheLocalSpaceSphere(in CellFrame position, float scale)
    {
        var inverse = 1.0f / scale;
        for (var i = 0; i < NumSphere; i++)
        {
            LocalSpaceSphere[i].Radius = inverse * LocalSphere[i].Radius;
            LocalSpaceSphere[i].Center = inverse * position.LocalToLocal(CheckPos, LocalSphere[i].Center);
            LocalSpaceCurrCenter[i] = inverse * position.LocalToLocal(CurPos, LocalSphere[i].Center);
        }

        LocalSpacePos = position;
        LocalSpaceZ = position.Frame.GlobalToLocalVec(Vector3.UnitZ);
        LocalSpaceLowPoint = LocalSpaceSphere[0].Center - (LocalSpaceZ * LocalSpaceSphere[0].Radius);
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::set_check_pos</c> (11.4186 0x0050A8B0, 11.6096 0x0050B380): tests from
    /// <paramref name="position"/> in <paramref name="cell"/>.
    /// </summary>
    public void SetCheckPos(in CellFrame position, PhysCell? cell)
    {
        CheckPos = position;
        CheckCell = cell;
        CellArrayValid = false;
        CacheGlobalSphere();
    }

    /// <summary>Retail <c>SPHEREPATH::save_check_pos</c> (11.4186 0x0050A880, 11.6096 0x0050B350).</summary>
    public void SaveCheckPos()
    {
        BackupCell = CheckCell;
        BackupCheckPos = CheckPos;
    }

    /// <summary>Retail <c>SPHEREPATH::restore_check_pos</c> (11.4186 0x0050B100, 11.6096 0x0050BBD0).</summary>
    public void RestoreCheckPos()
    {
        CheckPos = BackupCheckPos;
        CheckCell = BackupCell;
        CellArrayValid = false;
        CacheGlobalSphere();
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::set_collide</c> (11.4186 0x005378A0, 11.6096 0x005385E0): records a collision: the check
    /// position is saved, the normal kept for a step up, and the whole step is available again.
    /// </summary>
    public void SetCollide(Vector3 collisionNormal)
    {
        Collide = true;
        BackupCell = CheckCell;
        BackupCheckPos = CheckPos;
        StepUpNormal = collisionNormal;
        WalkInterp = 1.0f;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::set_neg_poly_hit</c> (11.4186 0x005398E0, 11.6096 0x0053A640): a polygon was touched from
    /// behind; the transition will step up (<paramref name="stepUp"/>) or slide along the reversed normal once the cells
    /// are done.
    /// </summary>
    public void SetNegPolyHit(bool stepUp, Vector3 collisionNormal)
    {
        NegStepUp = stepUp;
        NegPolyHit = true;
        NegCollisionNormal = -collisionNormal;
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::set_walkable</c> (11.4186 0x00539FC0, 11.6096 0x0053AD20): the polygon the object now stands
    /// on, with its sphere and up vector in the owning object's space, that object's position and scale.
    /// </summary>
    public void SetWalkable(in PhysSphere sphere, PhysPolygon polygon, Vector3 zAxis, in CellFrame localPos, float scale)
    {
        WalkableCheckPos = sphere;
        Walkable = polygon;
        WalkableUp = zAxis;
        WalkablePos = localPos;
        WalkableScale = scale;
    }

    /// <summary>Retail <c>SPHEREPATH::set_walkable_check_pos</c> (11.4186 0x00509CE0, 11.6096 0x0050A7B0).</summary>
    public void SetWalkableCheckPos(in PhysSphere sphere) => WalkableCheckPos = sphere;

    /// <summary>
    /// Retail <c>SPHEREPATH::check_walkables</c> (11.4186 0x0050C3E0, 11.6096 0x0050CEB0): true when there is no walkable
    /// polygon, or when the stored walkable sphere, its radius halved, still stands over it
    /// (<see cref="PhysPolygon.CheckWalkable(in PhysSphere, Vector3)"/>). Retail halves the stored sphere's radius in place,
    /// so each call halves it again until <see cref="SetWalkable"/> or <see cref="SetWalkableCheckPos"/> replaces it; ACE
    /// halves a copy.
    /// </summary>
    public bool CheckWalkables()
    {
        if (Walkable is null)
        {
            return true;
        }

        WalkableCheckPos.Radius *= 0.5f;
        return Walkable.CheckWalkable(WalkableCheckPos, WalkableUp);
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::precipice_slide</c> (11.4186 0x0050CC80, 11.6096 0x0050D750): the object is about to leave the
    /// walkable polygon; it slides along the edge it crossed (<see cref="PhysPolygon.FindCrossedEdge"/>, turned to face the
    /// move) instead. Without a crossed edge the step is refused.
    /// </summary>
    public TransitionState PrecipiceSlide(Transition transition)
    {
        if (!Walkable!.FindCrossedEdge(WalkableCheckPos, WalkableUp, out var normal))
        {
            Walkable = null;
            return TransitionState.Collided;
        }

        Walkable = null;
        StepUp = false;
        normal = WalkablePos.Frame.LocalToGlobalVec(normal);
        var offset = LandDefs.GetBlockOffset(CurPos.CellId, CheckPos.CellId) + (GlobalSphere[0].Center - GlobalCurrCenter[0]);
        if (Vector3.Dot(normal, offset) > 0.0f)
        {
            normal = -normal;
        }

        return GlobalSphere[0].SlideSphere(transition, ref normal, GlobalCurrCenter[0]);
    }

    /// <summary>
    /// Retail <c>SPHEREPATH::is_walkable_allowable</c> (11.4186 0x005368D0, 11.6096 0x00537610): a contact normal Z
    /// above the walkable allowance.
    /// </summary>
    public bool IsWalkableAllowable(float z) => z > WalkableAllowance;

    /// <summary>
    /// Retail <c>SPHEREPATH::get_curr_pos_check_pos_block_offset</c> (11.4186 0x00509FD0, 11.6096 0x0050AAA0): the block
    /// offset from the current position's landblock to the check position's.
    /// </summary>
    public Vector3 GetCurrPosCheckPosBlockOffset() => LandDefs.GetBlockOffset(CurPos.CellId, CheckPos.CellId);

    /// <summary>
    /// Retail <c>SPHEREPATH::step_up_slide</c> (11.4186 0x0050C3B0, 11.6096 0x0050CE80): a refused step up becomes a
    /// slide of the first sphere along the step-up normal, without a contact plane.
    /// </summary>
    public TransitionState StepUpSlide(Transition transition)
    {
        var collisions = transition.CollisionInfo;
        collisions.ContactPlaneValid = false;
        collisions.ContactPlaneIsWater = false;
        StepUp = false;
        return GlobalSphere[0].SlideSphere(transition, ref StepUpNormal, GlobalCurrCenter[0]);
    }
}
