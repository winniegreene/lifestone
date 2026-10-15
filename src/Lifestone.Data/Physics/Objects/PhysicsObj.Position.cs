using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.Transitions;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Data.Physics.Objects;

/// <summary>Placement of a <see cref="PhysicsObj"/>: <c>SetPosition</c>, the cells it is in and the shadows it casts.</summary>
public sealed partial class PhysicsObj
{
    /// <summary>
    /// Retail <c>CPhysicsObj::SetPosition</c> (11.4186 0x005160C0, 11.6096 0x00516BC0) and
    /// <c>SetPositionInternal(sps, transit)</c> (0x00516040, 0x00516B40): places the object at
    /// <paramref name="position"/> with a placement transition (the object's spheres, no carried contact).
    /// <list type="bullet">
    /// <item>The cell is found from the position (<c>AdjustPosition</c>): the EnvCell holding the first sphere's centre,
    /// or the outdoor cell under it.</item>
    /// <item>When its landblock is not attached the object leaves the world for the lost cell, keeping the position, and
    /// the answer is <see cref="SetPositionError.OK"/> with <see cref="PlacementPending"/> set; the world places it again
    /// when the landblock is attached.</item>
    /// <item>Otherwise the placement transition must find a valid position (within 0.05 m of the requested one unless
    /// <see cref="SetPositionFlags.Slide"/>, which may also push the object aside), and the object takes it
    /// (<see cref="SetPositionInternal(Transition)"/>).</item>
    /// </list>
    /// </summary>
    /// <returns><see cref="SetPositionError.OK"/>; <see cref="SetPositionError.NoValidPosition"/> (or
    /// <see cref="SetPositionError.Collided"/> when the attempt reported a collision) when nothing fits;
    /// <see cref="SetPositionError.NoCell"/> when the placement found no cell; <see cref="SetPositionError.InvalidArguments"/>
    /// for a cell id that is neither an outdoor cell, an EnvCell nor a landblock (retail would lose the object).</returns>
    /// <exception cref="NotImplementedException"><see cref="SetPositionFlags.Scatter"/> or
    /// <see cref="SetPositionFlags.RandomScatter"/>: retail scatters with <c>Random::RollDice</c>, which is not
    /// re-expressed (no client placement scatters).</exception>
    public SetPositionError SetPosition(in CellFrame position, SetPositionFlags flags)
    {
        if ((flags & (SetPositionFlags.Scatter | SetPositionFlags.RandomScatter)) != 0)
        {
            throw new NotImplementedException(
                "TODO(physics): CPhysicsObj::SetScatterPositionInternal (11.4186 0x00515F00, 11.6096 0x00516A00) scatters with Random::RollDice, which is not re-expressed.");
        }

        if (!LandDefs.IsValidCellId(position.CellId))
        {
            return SetPositionError.InvalidArguments;
        }

        var transition = World.Transitions.Rent();
        try
        {
            InitObjectInfo(transition, ObjectInfoState.None);
            return SetPositionInternal(position, flags, transition);
        }
        finally
        {
            World.Transitions.Return(transition);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::SetPositionSimple</c> (11.4186 0x005162B0, 11.6096 0x00516DB0): a teleport (flags Teleport
    /// and SendPositionEvent, with Slide when <paramref name="sliding"/>).
    /// </summary>
    public SetPositionError SetPositionSimple(in CellFrame position, bool sliding) =>
        SetPosition(
            position,
            SetPositionFlags.Teleport | SetPositionFlags.SendPositionEvent | (sliding ? SetPositionFlags.Slide : SetPositionFlags.None));

    /// <summary>
    /// Retail <c>CPhysicsObj::enter_world(p)</c> (11.4186 0x00516310, 11.6096 0x00516E10) and <c>enter_world(slide)</c>
    /// (0x00516170, 0x00516C70): stores <paramref name="position"/>, restarts the clock and places the object there
    /// (Placement, with Slide when <paramref name="slide"/>); placed, a dynamic object becomes active and its motion
    /// table drops its link animations (<c>CPartArray::HandleEnterWorld</c>, 11.4186 0x00517D70, 11.6096 0x005188A0,
    /// folded with <c>RemoveLinkAnimations</c>; <c>MovementManager::HandleEnterWorld</c> does nothing the interpreter
    /// needs).
    /// </summary>
    /// <returns>True when placed (or waiting for its landblock).</returns>
    public bool EnterWorld(in CellFrame position, bool slide = true)
    {
        StorePosition(position);
        UpdateTime = World.CurrentTime;
        var flags = SetPositionFlags.Placement | (slide ? SetPositionFlags.Slide : SetPositionFlags.None);
        if (SetPosition(Position, flags) != SetPositionError.OK)
        {
            return false;
        }

        if ((State & PhysicsState.Static) == 0)
        {
            SetActiveFlag();
        }

        MotionPlayer?.RemoveLinkAnimations();
        return true;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::ForceIntoCell</c> (11.4186 0x00515660, 11.6096 0x00516160): puts the object at
    /// <paramref name="position"/> without a transition; when the cell changes, its shadows are recomputed
    /// (<c>calc_cross_cells</c>). For server objects that are moved by the avatars track (owner decision D24) and must
    /// still stop the player (D25); call <see cref="RecalcCrossCells"/> after a move within a cell.
    /// </summary>
    /// <returns><see cref="SetPositionError.OK"/>, or <see cref="SetPositionError.NoCell"/> when the cell is not attached.</returns>
    public SetPositionError ForceIntoCell(in CellFrame position)
    {
        var cell = World.GetCell(position.CellId);
        if (cell is null)
        {
            return SetPositionError.NoCell;
        }

        if (Cell is null)
        {
            PrepareToEnterWorld();
        }

        Position = new CellFrame(position.CellId, position.Frame);
        UpdateParts();
        if (!ReferenceEquals(Cell, cell))
        {
            ChangeCell(cell);
            CalcCrossCells();
        }

        return SetPositionError.OK;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::recalc_cross_cells</c> (11.4186 0x00515A30, 11.6096 0x00516530): recomputes the cells the
    /// object's shadows are in, from its position (an object outside the world casts none).
    /// </summary>
    public void RecalcCrossCells()
    {
        if (Position.CellId != 0 && Cell is not null)
        {
            CalcCrossCells();
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::leave_world</c> (11.4186 0x005155A0, 11.6096 0x005160A0): ends its collisions, leaves its
    /// cell and shadows, forgets its cell id and contact (a walkable contact ends with <c>LeaveGround</c>).
    /// </summary>
    public void LeaveWorld()
    {
        ReportCollisionEnd(forceEnd: true);
        World.RemoveFromLostCell(this);
        PlacementPending = false;
        TransientState &= ~TransientState.Active;
        RemoveShadowsFromCells();
        LeaveCell();
        Position = new CellFrame(0, Position.Frame);
        ClearTransientStates();
    }

    /// <summary>
    /// Leaves the world and the world's object table for good, as retail <c>CObjectMaint::DeleteObject</c> (11.4186
    /// 0x00508460, 11.6096 0x00508F30) does: <see cref="ExitWorld"/>, <see cref="LeaveWorld"/>, then out of the table
    /// (and <c>CPhysicsObj::Destroy</c>, 0x00513AD0 / 0x005145D0, as far as physics goes). The object must not be used
    /// afterwards.
    /// </summary>
    public void Destroy()
    {
        ExitWorld();
        LeaveWorld();
        World.Unregister(this);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::exit_world</c> (11.4186 0x00514E60, 11.6096 0x00515960), as an object goes away
    /// (<see cref="Destroy"/>):
    /// <list type="number">
    /// <item>The motion table completes its pending animations as failures (<c>CPartArray::HandleExitWorld</c>, 0x00517D90 /
    /// 0x005188C0, → <c>MotionTableManager::HandleExitWorld</c>, 0x0051BDA0 / 0x0051C8D0), then the interpreter its pending
    /// motions (<c>MovementManager::HandleExitWorld</c>, 0x00524350 / 0x00524F50).</item>
    /// <item>The object unsticks; its target is cleared and the objects that target it are told it left
    /// (<see cref="TargetStatus.ExitWorld"/>, which clears their target).</item>
    /// <item>Its collisions end. (Detection is not physics.)</item>
    /// </list>
    /// </summary>
    /// <exception cref="InvalidOperationException">The motion table's pending count never drains (it was driven outside
    /// <see cref="MotionPlayer"/>).</exception>
    public void ExitWorld()
    {
        if (MotionPlayer is { } parts)
        {
            // CPartArray::HandleExitWorld: AnimationDone(false) until nothing is pending, without RemoveLinkAnimations's
            // drop of the link animations. MotionPlayer has no HandleExitWorld yet (requested of the avatars track), so
            // this drives its manager directly, which its remarks forbid. That works only because MotionPlayer counts
            // PendingAnimationCount down from the manager's MotionDone event, which each AnimationDone raises: if
            // MotionPlayer ever counts another way, this loop must move into it (MotionPlayer.HandleExitWorld).
            // Each AnimationDone advances the motion table's animation counter by one, so the queue empties after at most
            // its total animation count; the guard makes a miscount loud instead of a hang (as MotionPlayer's own loop).
            for (var guard = 0; parts.PendingAnimationCount > 0; guard++)
            {
                if (guard > 1_000_000)
                {
                    throw new InvalidOperationException(
                        $"{this}: {parts.PendingAnimationCount} pending motions of MotionTable 0x{parts.Table.Id:X8} never completed at exit_world");
                }

                parts.Manager.AnimationDone(success: false);
            }
        }

        Movement?.HandleExitWorld();
        Positions?.UnStick();
        if (_targets is { } targets)
        {
            targets.ClearTarget();
            targets.NotifyVoyeurOfEvent(TargetStatus.ExitWorld);
        }

        ReportCollisionEnd(forceEnd: true);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::leave_visibility</c> (11.4186 0x00514220, 11.6096 0x00514D20): out of its cell and shadows,
    /// into the lost cell with its position kept, inactive (the landblock went away; <see cref="PhysicsWorld.Detach"/>).
    /// </summary>
    internal void LeaveVisibility()
    {
        PrepareToLeaveVisibility();
        StorePosition(Position);
        GotoLostCell();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::reenter_visibility</c> (11.4186 0x00516250, 11.6096 0x00516D50): back from the lost cell
    /// with a sliding placement at the stored position (<see cref="PhysicsWorld.Attach"/>).
    /// </summary>
    internal bool ReenterVisibility()
    {
        PrepareToEnterWorld();
        return SetPosition(Position, SetPositionFlags.Placement | SetPositionFlags.Slide) == SetPositionError.OK;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::SetPositionInternal(p, sps, transit)</c> (11.4186 0x00515BD0, 11.6096 0x005166D0).
    /// </summary>
    private SetPositionError SetPositionInternal(in CellFrame requested, SetPositionFlags flags, Transition transition)
    {
        if (Cell is null)
        {
            PrepareToEnterWorld();
        }

        var position = requested;
        var cell = World.AdjustPosition(ref position, transition.SpherePath.LocalSphere[0].Center, searchCells: true);
        if (cell is null)
        {
            PrepareToLeaveVisibility();
            StorePosition(position);
            GotoLostCell();
            return SetPositionError.OK;
        }

        // Retail forces a weenie for which its vtable slots 29, 30 or 31 answer true straight into the cell
        // (ForceIntoCell); no M2 weenie answers them (they are not part of IPhysicsWeenie).
        if ((flags & SetPositionFlags.DontCreateCells) != 0)
        {
            transition.CellArray.DoNotLoadCells = true;
        }

        if (!CheckPositionInternal(cell, ref position, transition, flags))
        {
            return HandleAllCollisions(transition.CollisionInfo, prevHasContact: false, prevOnWalkable: false)
                ? SetPositionError.Collided
                : SetPositionError.NoValidPosition;
        }

        if (transition.SpherePath.CurCell is null)
        {
            return SetPositionError.NoCell;
        }

        SetPositionInternal(transition);
        return SetPositionError.OK;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::CheckPositionInternal</c> (11.4186 0x00511E90, 11.6096 0x00512960): the placement
    /// transition at <paramref name="position"/> in <paramref name="cell"/>. Without Slide the placement may not slide,
    /// and the found position must be in the same cell and no more than 0.05 m east and north of the requested one (the
    /// decompile compares the signed differences, so a placement pushed west or south passes); the position then takes
    /// the found origin.
    /// </summary>
    private static bool CheckPositionInternal(PhysCell cell, ref CellFrame position, Transition transition, SetPositionFlags flags)
    {
        transition.InitPath(cell, null, position);
        var slide = (flags & SetPositionFlags.Slide) != 0;
        if (!slide)
        {
            transition.SpherePath.PlacementAllowsSliding = false;
        }

        if (!transition.FindValidPosition())
        {
            return false;
        }

        if (slide)
        {
            return true;
        }

        var found = transition.SpherePath.CurPos;
        if (found.Frame.Origin.X - position.Frame.Origin.X < 0.050000001f
            && found.Frame.Origin.Y - position.Frame.Origin.Y < 0.050000001f
            && found.CellId == cell.Id)
        {
            position = position with { Frame = position.Frame with { Origin = found.Frame.Origin } };
            return true;
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::SetPositionInternal(transit)</c> (11.4186 0x00515330, 11.6096 0x00515E30): the object takes
    /// the transition's result.
    /// <list type="number">
    /// <item>Its cell (<c>change_cell</c>) and frame.</item>
    /// <item>The contact plane and its cell; Contact and WaterContact; OnWalkable from the plane (<c>set_on_walkable</c>:
    /// landing calls the motion's <c>HitGround</c>), or, without contact, OnWalkable cleared (leaving walkable ground calls
    /// <c>LeaveGround</c>); the acceleration.</item>
    /// <item>The sliding normal; the collisions (<see cref="HandleAllCollisions"/>).</item>
    /// <item>Its shadows: an object with physics BSPs finds its cells from its parts' boxes, any other takes the
    /// transition's cell array.</item>
    /// </list>
    /// Without a cell the object leaves the world for the lost cell at the transition's position.
    /// </summary>
    private void SetPositionInternal(Transition transition)
    {
        var prevOnWalkable = OnWalkable;
        var prevContact = HasContact;
        var path = transition.SpherePath;
        var collisions = transition.CollisionInfo;
        var newCell = path.CurCell;
        if (newCell is null)
        {
            PrepareToLeaveVisibility();
            StorePosition(path.CurPos);
            GotoLostCell();
            return;
        }

        if (ReferenceEquals(Cell, newCell))
        {
            Position = new CellFrame(path.CurPos.CellId, Position.Frame);
        }
        else
        {
            ChangeCell(newCell);
        }

        SetFrame(path.CurPos.Frame);
        ContactPlaneCellId = collisions.ContactPlaneCellId;
        ContactPlane = collisions.ContactPlane;
        TransientState = collisions.ContactPlaneValid
            ? TransientState | TransientState.Contact
            : TransientState & ~TransientState.Contact;
        CalcAcceleration();
        TransientState = collisions.ContactPlaneIsWater
            ? TransientState | TransientState.WaterContact
            : TransientState & ~TransientState.WaterContact;
        if (HasContact)
        {
            SetOnWalkable(ContactPlane.Normal.Z >= PhysicsGlobals.FloorZ);
        }
        else
        {
            var wasOnWalkable = OnWalkable;
            TransientState &= ~TransientState.OnWalkable;
            if (wasOnWalkable)
            {
                Movement?.LeaveGround();
            }

            CalcAcceleration();
        }

        SlidingNormal = collisions.SlidingNormal;
        TransientState = collisions.SlidingNormalValid
            ? TransientState | TransientState.Sliding
            : TransientState & ~TransientState.Sliding;
        HandleAllCollisions(collisions, prevContact, prevOnWalkable);
        if (Cell is null)
        {
            return;
        }

        if ((State & PhysicsState.HasPhysicsBsp) != 0)
        {
            CalcCrossCells();
        }
        else if (transition.CellArray.Count > 0)
        {
            RemoveShadowsFromCells();
            AddShadowsToCells(transition.CellArray);
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::set_on_walkable</c> (11.4186 0x00511310, 11.6096 0x00511DE0): OnWalkable, with the movement
    /// manager's <c>LeaveGround</c> when walkable ground ends and <c>HitGround</c> when it starts
    /// (<see cref="MovementManager.LeaveGround"/>, the interpreter's; <see cref="MovementManager.HitGround"/>, the
    /// interpreter's then the MoveTo manager's, which resumes its steps; 11.4186 0x00524320 and 0x00524300, 11.6096
    /// 0x00524F20 and 0x00524F00), then the acceleration.
    /// </summary>
    private void SetOnWalkable(bool onWalkable)
    {
        var wasOnWalkable = OnWalkable;
        TransientState = onWalkable ? TransientState | TransientState.OnWalkable : TransientState & ~TransientState.OnWalkable;
        if (wasOnWalkable)
        {
            if (!onWalkable)
            {
                Movement?.LeaveGround();
            }
        }
        else if (onWalkable)
        {
            Movement?.HitGround();
        }

        CalcAcceleration();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::clear_transient_states</c> (11.4186 0x00511BF0; TODO(physics): 11.6096 not in the symbol table): contact,
    /// walkable (with <c>LeaveGround</c>), sliding, water and the stationary bits cleared; Active and CheckEthereal kept.
    /// </summary>
    private void ClearTransientStates()
    {
        TransientState &= ~TransientState.Contact;
        CalcAcceleration();
        var wasOnWalkable = OnWalkable;
        TransientState &= ~(TransientState.OnWalkable | TransientState.WaterContact);
        if (wasOnWalkable)
        {
            Movement?.LeaveGround();
        }

        CalcAcceleration();
        TransientState &= ~(TransientState.Sliding | TransientState.StationaryMask);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::prepare_to_enter_world</c> (11.4186 0x00511FA0, 11.6096 0x00512A70): out of the lost cell,
    /// the clock restarted, and a dynamic object active.
    /// </summary>
    private void PrepareToEnterWorld()
    {
        UpdateTime = World.CurrentTime;
        World.RemoveFromLostCell(this);
        PlacementPending = false;
        if ((State & PhysicsState.Static) == 0)
        {
            SetActiveFlag();
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::prepare_to_leave_visibility</c> (11.4186 0x00511F40, 11.6096 0x00512A10): out of its shadows,
    /// the lost cell and its cell.
    /// </summary>
    private void PrepareToLeaveVisibility()
    {
        RemoveShadowsFromCells();
        World.RemoveFromLostCell(this);
        PlacementPending = false;
        LeaveCell();
    }

    /// <summary>The lost cell (<c>CObjectMaint::GotoLostCell</c>) and inactive, as each retail caller does.</summary>
    private void GotoLostCell()
    {
        World.GotoLostCell(this);
        PlacementPending = true;
        TransientState &= ~TransientState.Active;
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::store_position</c> (11.4186 0x005141A0, 11.6096 0x00514CA0): the position, an outdoor one
    /// moved into the landblock that holds its origin (<c>LandDefs::adjust_to_outside</c>).
    /// </summary>
    private void StorePosition(in CellFrame position)
    {
        var cellId = position.CellId;
        var origin = position.Frame.Origin;
        if ((cellId & 0xFFFF) < 0x100 && !LandDefs.AdjustToOutside(ref cellId, ref origin))
        {
            cellId = 0;
        }

        Position = new CellFrame(cellId, position.Frame with { Origin = origin });
        UpdateParts();
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::change_cell</c> (11.4186 0x00513390, 11.6096 0x00513E90): leaves the old cell
    /// (<c>leave_cell</c>, 0x00510F50 / 0x00511A20) and enters the new one (<c>enter_cell</c>, 0x00510ED0 / 0x005119A0),
    /// taking its id.
    /// </summary>
    private void ChangeCell(PhysCell? newCell)
    {
        LeaveCell();
        Cell = newCell;
        Position = new CellFrame(newCell?.Id ?? 0, Position.Frame);
    }

    private void LeaveCell() => Cell = null;

    /// <summary>
    /// Retail <c>CPhysicsObj::calc_cross_cells</c> (11.4186 0x00515230, 11.6096 0x00515D30): the cells the object reaches
    /// (its parts' boxes with physics BSPs, else its cylinder-spheres, else its sorting sphere), searched from its
    /// position into the world's shared cell array, become its shadows.
    /// </summary>
    private void CalcCrossCells()
    {
        var cells = World.CrossCells;
        cells.Reset();
        cells.DoNotLoadCells = false;
        if ((State & PhysicsState.HasPhysicsBsp) != 0)
        {
            CellLists.FindBboxCellList(World, Position.CellId, Cell, _parts, cells);
        }
        else if (Setup is not null && Setup.CylSpheres.Count > 0)
        {
            CellLists.FindCellList(World, Position, Setup.CylSphereSpan, cells);
        }
        else
        {
            var sorting = Setup?.SortingSphere ?? DummySpheres[0];
            Span<PhysSphere> sphere = [new PhysSphere(Position.Frame.LocalToGlobal(sorting.Center), sorting.Radius)];
            PhysCell? ignored = null;
            CellLists.FindCellList(World, Position, sphere, cells, ref ignored, wantCurrCell: false, path: null);
        }

        RemoveShadowsFromCells();
        AddShadowsToCells(cells);
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::add_shadows_to_cells</c> (11.4186 0x00514AE0, 11.6096 0x005155E0): a shadow per cell of
    /// <paramref name="cells"/>, added to the cells that exist.
    /// </summary>
    private void AddShadowsToCells(CellArray cells)
    {
        if (_shadowIds.Length < cells.Count)
        {
            Array.Resize(ref _shadowIds, cells.Count);
            Array.Resize(ref _shadowCells, cells.Count);
        }

        _shadowCount = cells.Count;
        for (var i = 0; i < cells.Count; i++)
        {
            _shadowIds[i] = cells.IdAt(i);
            _shadowCells[i] = cells.CellAt(i);
        }

        for (var i = 0; i < _shadowCount; i++)
        {
            if (_shadowCells[i] is not null)
            {
                World.AddObjectShadow(_shadowIds[i], this);
            }
        }
    }

    /// <summary>
    /// Retail <c>CPhysicsObj::remove_shadows_from_cells</c> (11.4186 0x00511230, 11.6096 0x00511D00): every shadow leaves
    /// its cell.
    /// </summary>
    private void RemoveShadowsFromCells()
    {
        for (var i = 0; i < _shadowCount; i++)
        {
            if (_shadowCells[i] is not null)
            {
                World.RemoveObjectShadow(_shadowIds[i], this);
                _shadowCells[i] = null;
            }
        }

        _shadowCount = 0;
    }
}
