namespace Lifestone.Data.Physics.Objects;

/// <summary>
/// A physics object's transient state (retail <c>CPhysicsObj::transient_state</c>): what the last step learned about
/// contact and motion. The bits are the ones the 11.4186 decompile tests: <c>CPhysicsObj::SetPositionInternal</c>
/// (11.4186 0x00515330, 11.6096 0x00515E30) writes Contact 1, OnWalkable 2, Sliding 4 and WaterContact 8;
/// <c>CPhysicsObj::handle_all_collisions</c> (0x00514780, 0x00515280) writes the stationary bits 0x10-0x40;
/// <c>CPhysicsObj::set_active</c> (0x0050FC40, 0x00510710) writes Active 0x80; <c>CPhysicsObj::set_ethereal</c>
/// (0x00511C40, 0x00512710) writes CheckEthereal 0x100. The names are ACE's (<c>TransientStateFlags</c>).
/// </summary>
[Flags]
public enum TransientState : uint
{
    /// <summary>No bit.</summary>
    None = 0,

    /// <summary>The object touches a contact plane.</summary>
    Contact = 0x1,

    /// <summary>The contact plane is walkable (its normal's Z is at least <see cref="PhysicsGlobals.FloorZ"/>).</summary>
    OnWalkable = 0x2,

    /// <summary>The object slides along a sliding normal.</summary>
    Sliding = 0x4,

    /// <summary>The contact plane is water.</summary>
    WaterContact = 0x8,

    /// <summary>One step without movement while falling (the stationary-fall failsafe's first count).</summary>
    StationaryFall = 0x10,

    /// <summary>Two steps without movement while falling.</summary>
    StationaryStop = 0x20,

    /// <summary>Three or more steps without movement while falling.</summary>
    StationaryStuck = 0x40,

    /// <summary>The object is updated (<c>CPhysicsObj::UpdateObjectInternal</c> does nothing else without it).</summary>
    Active = 0x80,

    /// <summary>The object wants to stop being ethereal once nothing overlaps it.</summary>
    CheckEthereal = 0x100,

    /// <summary>The three stationary-fall bits (<c>handle_all_collisions</c> clears them together).</summary>
    StationaryMask = StationaryFall | StationaryStop | StationaryStuck,
}

/// <summary>
/// How <see cref="PhysicsObj.SetPosition"/> places an object (retail <c>SetPositionStruct::flags</c>). The values are the
/// ones the 11.4186 decompile tests and passes: <c>CPhysicsObj::SetPositionInternal(p, sps, transit)</c> reads Slide 0x10
/// and DontCreateCells 0x20, <c>SetPositionInternal(sps, transit)</c> reads Scatter 0x100 and RandomScatter 0x200,
/// <c>enter_world</c> passes Placement (1) or Placement|Slide (0x11), <c>SetPositionSimple</c> Teleport|SendPositionEvent
/// (0x1002) or with Slide (0x1012). The names are ACE's (<c>SetPositionFlags</c>).
/// </summary>
[Flags]
public enum SetPositionFlags : uint
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A placement (entering the world).</summary>
    Placement = 0x1,

    /// <summary>A teleport.</summary>
    Teleport = 0x2,

    /// <summary>A restore.</summary>
    Restore = 0x4,

    /// <summary>The placement may slide the object away from the requested position; without it the result must stay within 0.05 m.</summary>
    Slide = 0x10,

    /// <summary>The cell search keeps to the start cell's visible cells (retail <c>do_not_load_cells</c>).</summary>
    DontCreateCells = 0x20,

    /// <summary>Try the exact position, then scattered ones.</summary>
    Scatter = 0x100,

    /// <summary>Scattered positions only.</summary>
    RandomScatter = 0x200,

    /// <summary>A line placement (not used by the client).</summary>
    Line = 0x400,

    /// <summary>The move is reported to the server (the caller's business; physics only carries the flag).</summary>
    SendPositionEvent = 0x1000,
}

/// <summary>
/// What <see cref="PhysicsObj.SetPosition"/> answers (retail's return codes of <c>CPhysicsObj::SetPositionInternal</c>
/// and <c>SetPosition</c>; the names are ACE's <c>SetPositionError</c>).
/// </summary>
public enum SetPositionError
{
    /// <summary>Placed; or, for a cell whose landblock is not attached, stored and waiting (<see cref="PhysicsObj.PlacementPending"/>).</summary>
    OK = 0,

    /// <summary>The object's own bookkeeping failed (retail returns 1; also when no transition was free).</summary>
    GeneralFailure = 1,

    /// <summary>No valid position was found at the requested place.</summary>
    NoValidPosition = 2,

    /// <summary>The placement found a position but no cell.</summary>
    NoCell = 3,

    /// <summary>No valid position, and the attempt collided with something that reported it.</summary>
    Collided = 4,

    /// <summary>The cell id is not an outdoor cell, an EnvCell or a landblock id (retail <c>AdjustPosition</c> refuses it).</summary>
    InvalidArguments = 0x100,
}
