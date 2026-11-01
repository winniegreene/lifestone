using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Data.Dat;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Physics;

/// <summary>Counters of a <see cref="ServerObjectPhysicsHost"/>; every create it keeps nothing for is counted.</summary>
public sealed class ServerObjectPhysicsStats
{
    /// <summary>Physics objects made (recreates included).</summary>
    public long Created { get; internal set; }

    /// <summary>Creates whose Setup has no collision shape (no physics object, no cost).</summary>
    public long WithoutCollision { get; internal set; }

    /// <summary>Creates whose Setup does not exist or does not load (logged once per Setup).</summary>
    public long SetupMissing { get; internal set; }

    /// <summary>Creates skipped because the object is the player's own, or its id is taken in the world (logged).</summary>
    public long Skipped { get; internal set; }

    /// <summary>Server moves of an object already in the world (and forced placements) applied with <c>ForceIntoCell</c>.</summary>
    public long Moves { get; internal set; }

    /// <summary>
    /// Objects put into the world by retail's sliding placement (<c>enter_world</c>: a create, a recreate, a return after leaving
    /// the world), and the forced placements below: the count a player's host watches to place the player again
    /// (<c>OfflineWalker.ReplaceAfterObjectsEntered</c>, M5 item 5 F1).
    /// </summary>
    public long EnteredWorld { get; internal set; }

    /// <summary>
    /// Of <see cref="EnteredWorld"/>, sliding placements that found no valid position, so the object was forced in at the
    /// server's position (<c>ForceIntoCell</c>; logged once per object, <c>TODO(m5-create-forced)</c>).
    /// </summary>
    public long EntriesForced { get; internal set; }

    /// <summary>
    /// Objects found out of every cell after their landblock attached (the world's sliding <c>reenter_visibility</c> failed),
    /// forced into their cell at the server's position by <see cref="ServerObjectPhysicsHost.Tick"/> (logged once per object,
    /// <c>TODO(m5-reenter-forced)</c>).
    /// </summary>
    public long ReentriesForced { get; internal set; }

    /// <summary>Placements and moves into a landblock not attached yet (the lost cell).</summary>
    public long MovesPending { get; internal set; }

    /// <summary>State changes applied.</summary>
    public long StateChanges { get; internal set; }

    /// <summary>Times an object left the world (pickup, parenting, out of its cell).</summary>
    public long LeftWorld { get; internal set; }

    /// <summary>Transitions run for avatars' steps (M5 item 5 t1, <see cref="ServerObjectPhysicsHost.Transition"/>).</summary>
    public long Transitions { get; internal set; }

    /// <summary>Of <see cref="Transitions"/>, those that found no valid position (the object only turned).</summary>
    public long TransitionsBlocked { get; internal set; }

    /// <summary>Sliding placements run for avatars (teleports, far positions, blips: <see cref="ServerObjectPhysicsHost.Place(uint, in CellFrame, out CellFrame)"/>).</summary>
    public long Placements { get; internal set; }

    /// <summary>Of <see cref="Placements"/>, those that found no valid position (the object stayed where it was).</summary>
    public long PlacementsRefused { get; internal set; }

    /// <summary>Physics objects destroyed.</summary>
    public long Removed { get; internal set; }
}

/// <summary>
/// The server-object physics host (M5 item 5, D70; docs/physics.md §10 "Other objects", owner decision D25): one
/// <see cref="PhysicsObj"/> in the player's <see cref="PhysicsWorld"/> for every server object whose Setup has collision
/// (physics BSP parts, cylinder-spheres or spheres: doors, lifestones, NPCs, creatures), so the player's transitions meet
/// it as retail's do (<c>CPhysicsObj::FindObjCollisions</c>, 11.6096 0x0050FB20). An object without a collision shape
/// gets nothing and costs nothing.
/// </summary>
/// <remarks>
/// <para><b>Placing and moving.</b> A new object (a create, a recreate, the return of one that left the world) enters with
/// retail's placement (<c>CPhysicsObj::enter_world</c>, 11.4186 0x00516170, 11.6096 0x00516C70: Placement with Slide), so it is
/// never made inside a static or another object. The client's own player does not stop it
/// (<see cref="PhysicsObj.EnterWorldPastPlayer"/>): retail creates a cell's objects before it places the player, so the
/// player's host places the player again when <see cref="ServerObjectPhysicsStats.EnteredWorld"/> rises, and the object stays
/// where the server and its drawn avatar have it. A sliding placement that finds no valid position falls back to
/// <see cref="PhysicsObj.ForceIntoCell"/> at the server's position (counted, logged once per object:
/// <c>TODO(m5-create-forced)</c>). A server move of an object already in the world is not stepped here (D24; slice t1 runs
/// the avatars' transitions), so the host puts it where the server or its avatar says with
/// <see cref="PhysicsObj.ForceIntoCell"/> (retail <c>CPhysicsObj::ForceIntoCell</c>, 11.4186 0x00515660, 11.6096 0x00516160),
/// which recomputes the shadows only when the cell changes; after a move within the cell the host recomputes them itself
/// (<see cref="PhysicsObj.RecalcCrossCells"/>, "on demand", so a creature walking to its cell's edge still reaches the
/// neighbouring cell). A placement into a landblock that is not attached sends the object to the world's lost cell, and
/// <see cref="PhysicsWorld.Attach"/> places it again (<c>reenter_visibility</c>), as the world does for its own objects; an
/// object that placement leaves out of every cell is forced into its cell at the server's position on the next
/// <see cref="Tick"/> (counted, logged once per object: <c>TODO(m5-reenter-forced)</c>), so a door or a vendor is never
/// silently not solid. A move to the same position (<see cref="SamePlace"/>) does nothing (no allocation).</para>
/// <para><b>State.</b> The create's and SetState's PhysicsState bits are the object's (<see cref="PhysicsObj.SetState"/>,
/// retail <c>set_state</c>: Hidden hides it and turns on IgnoreCollisions), except two: HasPhysicsBsp stays the Setup's
/// (retail <c>CacheHasPhysicsBSP</c>), and Ethereal goes through <see cref="PhysicsObj.SetEthereal"/> (retail
/// <c>set_ethereal</c>, 11.6096 0x00512710), whose return to solid fails while something stands in the object and is
/// retried on each <see cref="Tick"/> (CheckEthereal), as retail's update of the object retries it.</para>
/// <para><b>Weenie.</b> Each object has a <see cref="ServerObjectWeenie"/> for retail's collision rules: a creature from
/// the create's ItemType, a player from BF_PLAYER. Player-killer status and impenetrability are not read
/// (<c>TODO(m5-object-pk-status)</c>, logged once when a player object is made): only another player passing through
/// the player depends on them.</para>
/// <para><b>Leaving.</b> A pickup, a parent (a wielded object has no collision of its own) or leaving the cell takes the
/// object out of the world (<see cref="PhysicsObj.LeaveWorld"/>, retail <c>leave_world</c>, 11.6096 0x005160A0); a later
/// move puts it back. A delete destroys it (<see cref="PhysicsObj.Destroy"/>), returning the world's tables to what
/// they were. A recreate replaces the object.</para>
/// <para><b>Transitions</b> (slice t1, owner decision D45): as <see cref="IAvatarTransitions"/> the host runs each avatar's step
/// through its object (<see cref="PhysicsObj.TryTransitionTo"/>: retail <c>CPhysicsObj::transition</c> and
/// <c>SetPositionInternal(transit)</c>) and its teleports, far positions and blips as retail's sliding
/// <c>SetPositionSimple</c>, so creatures stop at each other, the player, doors and lifestones. The object never meets itself
/// (<c>PhysCell</c>'s object shadows skip the mover, as retail's <c>CObjCell::find_obj_collisions</c> does). The object's kept
/// position follows, so the avatar set's feed of the same position afterwards does nothing.</para>
/// <para>Calls come from the thread that owns the world; nothing is locked.</para>
/// </remarks>
public sealed class ServerObjectPhysicsHost : IServerObjectPhysics, IAvatarTransitions
{
    /// <summary>The ObjectDescriptionFlags bit of a player (<c>BF_PLAYER</c>, 0x8; as <c>WorldObjectTable.PlayerFlag</c>).</summary>
    public const uint PlayerFlag = 0x8;

    private const string LogCategory = "physics";

    private readonly Func<uint, PhysicsSetup?> _loadSetup;
    private readonly ILogSink _log;
    private readonly Dictionary<uint, PhysicsSetup?> _setups = [];
    private readonly Dictionary<uint, Entry> _entries = [];
    private readonly List<Entry> _order = [];
    private readonly HashSet<uint> _forcedLogged = [];
    private bool _flagsUnknownLogged;
    private bool _pkStatusLogged;

    /// <summary>A host over <paramref name="world"/> that loads Setups from the dats.</summary>
    /// <param name="world">The player's physics world.</param>
    /// <param name="dats">The dats.</param>
    /// <param name="geometry">The shared shape cache, or null for a new one.</param>
    /// <param name="log">The log, or none.</param>
    public ServerObjectPhysicsHost(PhysicsWorld world, DatSet dats, PhysicsGeometryLibrary? geometry = null, ILogSink? log = null)
        : this(world, LoadFrom(dats, geometry), log)
    {
    }

    /// <summary>A host over <paramref name="world"/> whose Setups come from <paramref name="loadSetup"/> (tests without the dats).</summary>
    /// <param name="world">The player's physics world.</param>
    /// <param name="loadSetup">The Setup's physics by id, or null when it does not exist or load; called once per id.</param>
    /// <param name="log">The log, or none.</param>
    public ServerObjectPhysicsHost(PhysicsWorld world, Func<uint, PhysicsSetup?> loadSetup, ILogSink? log = null)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        _loadSetup = loadSetup ?? throw new ArgumentNullException(nameof(loadSetup));
        _log = log ?? NullLogSink.Instance;
    }

    /// <summary>The player's physics world.</summary>
    public PhysicsWorld World { get; }

    /// <summary>The counters.</summary>
    public ServerObjectPhysicsStats Stats { get; } = new();

    /// <summary>The physics objects kept (in the world or out of it).</summary>
    public int Count => _order.Count;

    /// <summary>
    /// The physics object kept for <paramref name="guid"/> (in the world or out of it), for slice t1's transitions and the
    /// tests; false when the object has none.
    /// </summary>
    public bool TryGetObject(uint guid, [NotNullWhen(true)] out PhysicsObj? obj)
    {
        if (_entries.TryGetValue(guid, out var entry))
        {
            obj = entry.Obj;
            return true;
        }

        obj = null;
        return false;
    }

    /// <inheritdoc/>
    public bool Hosts(uint guid) => _entries.TryGetValue(guid, out var entry) && entry.InWorld && entry.Obj.Cell is not null;

    /// <summary>
    /// One step of <paramref name="guid"/>'s avatar through its object: the object is first put at <paramref name="from"/>
    /// when it is not there (a placement the feed has not passed on yet), then runs retail's transition to
    /// <paramref name="to"/> carrying <paramref name="contact"/> (M5 D72: the server's Grounded flag, with the object's own
    /// contact plane when it has one) and takes the result (<see cref="PhysicsObj.TryTransitionTo"/>).
    /// </summary>
    /// <returns><see cref="AvatarTransitionResult.Moved"/> with where the object is now;
    /// <see cref="AvatarTransitionResult.NoValidPosition"/> (it stayed at <paramref name="from"/>); or
    /// <see cref="AvatarTransitionResult.NotHosted"/> when the host keeps no object for it in a cell.</returns>
    public AvatarTransitionResult Transition(uint guid, in CellFrame from, in CellFrame to, bool contact, out CellFrame result)
    {
        result = default;
        LastObstacle = default;
        if (!_entries.TryGetValue(guid, out var entry) || !entry.InWorld || entry.Obj.Cell is null)
        {
            return AvatarTransitionResult.NotHosted;
        }

        var obj = entry.Obj;
        if (!SamePlace(obj.Position, from))
        {
            MoveInWorld(entry, from);
            if (obj.Cell is null)
            {
                return AvatarTransitionResult.NotHosted;
            }
        }

        Stats.Transitions++;
        if (!obj.TryTransitionTo(to, contact))
        {
            Stats.TransitionsBlocked++;
            entry.Position = obj.Position;
            LastObstacle = Classify(obj.LastTransitionObstacle);
            return AvatarTransitionResult.NoValidPosition;
        }

        LastObstacle = Classify(obj.LastTransitionObstacle);
        result = obj.Position;
        entry.Position = result;
        return AvatarTransitionResult.Moved;
    }

    /// <inheritdoc/>
    public AvatarObstacle LastObstacle { get; private set; }

    /// <summary>A transition's obstacle as the avatars name it: the client's own player or another player, a creature, any other object, the environment or a step.</summary>
    private static AvatarObstacle Classify(in TransitionObstacle obstacle)
    {
        switch (obstacle.Kind)
        {
            case TransitionObstacleKind.Object when obstacle.Object is { } other:
                var kind = other.IsThePlayer || (other.Weenie?.IsPlayer ?? false)
                    ? AvatarObstacleKind.Player
                    : (other.Weenie?.IsCreature ?? false) ? AvatarObstacleKind.Creature : AvatarObstacleKind.Object;
                return new AvatarObstacle(kind, other.Id, obstacle.CellId, obstacle.StepRefused);
            case TransitionObstacleKind.Environment:
                return new AvatarObstacle(AvatarObstacleKind.Environment, 0, obstacle.CellId, obstacle.StepRefused);
            case TransitionObstacleKind.Step:
                return new AvatarObstacle(AvatarObstacleKind.Step, 0, obstacle.CellId, true);
            default:
                return new AvatarObstacle(AvatarObstacleKind.None, 0, obstacle.CellId, obstacle.StepRefused);
        }
    }

    /// <summary>
    /// A teleport, far position or blip of <paramref name="guid"/>'s avatar: retail's sliding <c>SetPositionSimple</c>
    /// (11.4186 0x005162B0, 11.6096 0x00516DB0; <c>MoveOrTeleport</c>'s teleport flags 0x1012 are the same placement) through
    /// its object, which may push it aside of another object or the player.
    /// </summary>
    /// <returns><see cref="AvatarTransitionResult.Moved"/> with where it landed;
    /// <see cref="AvatarTransitionResult.NoValidPosition"/> when nothing fits (retail's <c>SetPosition</c> fails and the object
    /// stays); <see cref="AvatarTransitionResult.NotHosted"/> when the host keeps no object for it in a cell, or the position's
    /// landblock is not attached (the object waits in the lost cell and the avatar is placed as given).</returns>
    public AvatarTransitionResult Place(uint guid, in CellFrame position, out CellFrame result)
    {
        result = default;
        if (!_entries.TryGetValue(guid, out var entry) || !entry.InWorld || entry.Obj.Cell is null)
        {
            return AvatarTransitionResult.NotHosted;
        }

        var obj = entry.Obj;
        Stats.Placements++;
        if (obj.SetPositionSimple(position, sliding: true) != SetPositionError.OK)
        {
            Stats.PlacementsRefused++;
            return AvatarTransitionResult.NoValidPosition;
        }

        if (obj.Cell is null)
        {
            entry.Position = position;
            return AvatarTransitionResult.NotHosted;
        }

        result = obj.Position;
        entry.Position = result;
        return AvatarTransitionResult.Moved;
    }

    /// <inheritdoc/>
    public bool TryGetPosition(uint guid, out CellFrame position)
    {
        if (_entries.TryGetValue(guid, out var entry) && entry.InWorld && entry.Obj.Cell is not null)
        {
            position = entry.Obj.Position;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>True when <paramref name="guid"/>'s physics object is in the world (in a cell, or waiting in the lost cell).</summary>
    public bool IsInWorld(uint guid) => _entries.TryGetValue(guid, out var entry) && entry.InWorld;

    /// <inheritdoc/>
    public void Create(in ServerObjectDescription description, CellFrame? position)
    {
        if (_entries.ContainsKey(description.Guid))
        {
            Remove(description.Guid);
        }

        if (description.IsSelf)
        {
            Stats.Skipped++;
            return;
        }

        var setup = GetSetup(description.SetupId, description.Guid);
        if (setup is null)
        {
            Stats.SetupMissing++;
            return;
        }

        if (!HasCollision(setup))
        {
            Stats.WithoutCollision++;
            return;
        }

        if (World.GetObject(description.Guid) is not null)
        {
            Stats.Skipped++;
            Log(LogSeverity.Warning, description.Guid, "the world already has an object with this id (the player's own?); no server-object physics made for it");
            return;
        }

        var weenie = MakeWeenie(description);
        var obj = new PhysicsObj(World, setup, description.Guid, weenie, motionTable: null, scale: description.Scale > 0f ? description.Scale : 1f);
        var entry = new Entry(obj);
        _entries.Add(description.Guid, entry);
        _order.Add(entry);
        Stats.Created++;
        ApplyState(obj, (PhysicsState)description.PhysicsState);
        if (position is { CellId: not 0 } at)
        {
            Enter(entry, at);
        }
    }

    /// <inheritdoc/>
    public void Move(uint guid, in CellFrame position)
    {
        if (!_entries.TryGetValue(guid, out var entry))
        {
            return;
        }

        if (position.CellId == 0)
        {
            LeaveWorld(entry);
            return;
        }

        if (!entry.InWorld)
        {
            Enter(entry, position);
            return;
        }

        if (SamePlace(entry.Position, position))
        {
            return;
        }

        MoveInWorld(entry, position);
    }

    /// <inheritdoc/>
    public void SetState(uint guid, uint physicsState)
    {
        if (!_entries.TryGetValue(guid, out var entry))
        {
            return;
        }

        ApplyState(entry.Obj, (PhysicsState)physicsState);
        Stats.StateChanges++;
    }

    /// <inheritdoc/>
    public void LeaveWorld(uint guid)
    {
        if (_entries.TryGetValue(guid, out var entry))
        {
            LeaveWorld(entry);
        }
    }

    /// <inheritdoc/>
    public void Remove(uint guid)
    {
        if (!_entries.Remove(guid, out var entry))
        {
            return;
        }

        _order.Remove(entry);
        entry.Obj.Destroy();
        Stats.Removed++;
    }

    /// <summary>
    /// Once per frame: an object left out of every cell after its landblock attached (the world's sliding
    /// <c>reenter_visibility</c> found no valid position) is forced into its cell at the server's position (M5 item 5 F1,
    /// 5-R5; counted and logged once per object), and an ethereal object waiting to turn solid tries again (CheckEthereal).
    /// Allocates nothing when nothing changes.
    /// </summary>
    public void Tick()
    {
        for (var i = 0; i < _order.Count; i++)
        {
            var entry = _order[i];
            var obj = entry.Obj;
            if (entry.InWorld && obj.Cell is null && !obj.PlacementPending && World.GetCell(entry.Position.CellId) is not null)
            {
                Stats.ReentriesForced++;
                Stats.EnteredWorld++;
                LogForcedOnce(entry, "TODO(m5-reenter-forced): the world's sliding placement found no valid position for this object when its landblock attached; forced into its cell at the server's position (it may overlap what stands there)");
                MoveInWorld(entry, entry.Position);
            }

            if ((obj.TransientState & TransientState.CheckEthereal) != 0)
            {
                obj.SetEthereal(false);
            }
        }
    }

    /// <summary>
    /// True when a Setup can stop something: a physics BSP part, a cylinder-sphere or a sphere (as the statics' rule,
    /// docs/physics.md §9 step 4; <c>FindObjCollisions</c> tests them in that order).
    /// </summary>
    public static bool HasCollision(PhysicsSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        return setup.HasPhysicsBsp || setup.CylSpheres.Count > 0 || setup.Spheres.Count > 0;
    }

    /// <summary>
    /// True when <paramref name="b"/> is where <paramref name="a"/> is: the same cell, and every component of the origin
    /// and of the orientation within the epsilon (retail <c>AC1Legacy::Vector3::operator==</c>, 11.6096 0x0050A9E0, the
    /// test <c>UpdateObjectInternal</c> uses to decide whether an object moved). A standing avatar's frame drifts by far
    /// less each frame; the drift is taken once it adds up to more.
    /// </summary>
    public static bool SamePlace(in CellFrame a, in CellFrame b)
    {
        if (a.CellId != b.CellId)
        {
            return false;
        }

        var oa = a.Frame.Origin;
        var ob = b.Frame.Origin;
        var qa = a.Frame.Orientation;
        var qb = b.Frame.Orientation;
        return Near(oa.X, ob.X) && Near(oa.Y, ob.Y) && Near(oa.Z, ob.Z)
            && Near(qa.W, qb.W) && Near(qa.X, qb.X) && Near(qa.Y, qb.Y) && Near(qa.Z, qb.Z);
    }

    private static bool Near(float a, float b) => Math.Abs((double)a - b) <= PhysicsGlobals.Epsilon;

    private static Func<uint, PhysicsSetup?> LoadFrom(DatSet dats, PhysicsGeometryLibrary? geometry)
    {
        ArgumentNullException.ThrowIfNull(dats);
        geometry ??= new PhysicsGeometryLibrary(dats);
        return id => PhysicsSetup.Load(dats, id, geometry);
    }

    private PhysicsSetup? GetSetup(uint setupId, uint guid)
    {
        if (_setups.TryGetValue(setupId, out var cached))
        {
            return cached;
        }

        PhysicsSetup? setup;
        try
        {
            setup = setupId == 0 ? null : _loadSetup(setupId);
        }
        catch (InvalidDataException e)
        {
            Log(LogSeverity.Warning, guid, string.Create(CultureInfo.InvariantCulture, $"Setup 0x{setupId:X8} is malformed ({e.Message}); the object gets no server-object physics"));
            setup = null;
        }

        if (setup is null)
        {
            Log(LogSeverity.Warning, guid, string.Create(CultureInfo.InvariantCulture, $"Setup 0x{setupId:X8} does not exist or does not load; objects made from it get no server-object physics"));
        }

        _setups.Add(setupId, setup);
        return setup;
    }

    private ServerObjectWeenie MakeWeenie(in ServerObjectDescription description)
    {
        var isPlayer = false;
        if (description.DescriptionFlags is { } flags)
        {
            isPlayer = (flags & PlayerFlag) != 0;
        }
        else if (!_flagsUnknownLogged)
        {
            _flagsUnknownLogged = true;
            Log(
                LogSeverity.Info,
                description.Guid,
                "TODO(m5-object-description-flags): a create reached the host without its ObjectDescriptionFlags (the avatar set and the object table pass them; this feed did not), so the object is not known as a player and blocks the player as an NPC would, where retail lets two players pass");
        }

        if (isPlayer && !_pkStatusLogged)
        {
            _pkStatusLogged = true;
            Log(
                LogSeverity.Info,
                description.Guid,
                "TODO(m5-object-pk-status): a player object's IsPK, IsPKLite and IsImpenetrable are not read from its description (false), so two players always pass through each other");
        }

        return new ServerObjectWeenie(description.IsCreature, isPlayer);
    }

    /// <summary>
    /// A new object into the world: retail's sliding <c>enter_world</c> (0x00516C70) past the client's own player; when no
    /// position is valid, forced in at the server's position (counted, logged once per object).
    /// </summary>
    private void Enter(Entry entry, in CellFrame position)
    {
        var obj = entry.Obj;
        Stats.EnteredWorld++;
        if (obj.EnterWorldPastPlayer(position))
        {
            entry.InWorld = true;
            if (obj.Cell is null)
            {
                Stats.MovesPending++;
                entry.Position = position;
            }
            else
            {
                entry.Position = obj.Position;
            }

            return;
        }

        Stats.EntriesForced++;
        LogForcedOnce(entry, "TODO(m5-create-forced): retail's sliding placement (enter_world) found no valid position for this object; forced in at the server's position (it may overlap what stands there)");
        MoveInWorld(entry, position);
    }

    private void LogForcedOnce(Entry entry, string message)
    {
        if (_forcedLogged.Add(entry.Obj.Id))
        {
            Log(LogSeverity.Warning, entry.Obj.Id, message);
        }
    }

    /// <summary>A server move of an object in the world: <c>ForceIntoCell</c>, or the lost cell while its landblock is not attached.</summary>
    private void MoveInWorld(Entry entry, in CellFrame position)
    {
        var obj = entry.Obj;
        var before = obj.Cell;
        if (obj.ForceIntoCell(position) == SetPositionError.OK)
        {
            if (ReferenceEquals(before, obj.Cell))
            {
                obj.RecalcCrossCells();
            }

            Stats.Moves++;
        }
        else
        {
            obj.WaitForLandblock(position);
            Stats.MovesPending++;
        }

        entry.InWorld = true;
        entry.Position = position;
    }

    private void LeaveWorld(Entry entry)
    {
        if (!entry.InWorld)
        {
            return;
        }

        entry.Obj.LeaveWorld();
        entry.InWorld = false;
        Stats.LeftWorld++;
    }

    private static void ApplyState(PhysicsObj obj, PhysicsState requested)
    {
        const PhysicsState ClientOwned = PhysicsState.HasPhysicsBsp | PhysicsState.Ethereal;
        var next = (requested & ~ClientOwned) | (obj.State & ClientOwned);
        if (next != obj.State)
        {
            obj.SetState(next);
        }

        var ethereal = (requested & PhysicsState.Ethereal) != 0;
        var isEthereal = (obj.State & PhysicsState.Ethereal) != 0;
        if (ethereal != isEthereal || (!ethereal && (obj.TransientState & TransientState.CheckEthereal) != 0))
        {
            obj.SetEthereal(ethereal);
        }
    }

    private void Log(LogSeverity severity, uint guid, string message)
    {
        if (_log.IsEnabled(severity))
        {
            _log.Write(severity, LogCategory, message, [new LogField("guid", string.Create(CultureInfo.InvariantCulture, $"0x{guid:X8}"))]);
        }
    }

    private sealed class Entry(PhysicsObj obj)
    {
        public PhysicsObj Obj { get; } = obj;

        public bool InWorld { get; set; }

        public CellFrame Position { get; set; }
    }
}

/// <summary>
/// A server object's weenie as physics asks it (<see cref="IPhysicsWeenie"/>): a creature or not, a player or not, never a
/// player killer or impenetrable (<c>TODO(m5-object-pk-status)</c>), never the client's own player. Server objects have
/// no motion table in the host, so the motion questions answer "unknown".
/// </summary>
/// <param name="isCreature">ItemType has the creature bit (0x10).</param>
/// <param name="isPlayer">ObjectDescriptionFlags has BF_PLAYER (0x8).</param>
public sealed class ServerObjectWeenie(bool isCreature, bool isPlayer) : IPhysicsWeenie
{
    /// <inheritdoc/>
    public bool IsCreature { get; } = isCreature;

    /// <inheritdoc/>
    public bool IsThePlayer => false;

    /// <inheritdoc/>
    public bool IsPlayer { get; } = isPlayer;

    /// <inheritdoc/>
    public bool IsPK => false;

    /// <inheritdoc/>
    public bool IsPKLite => false;

    /// <inheritdoc/>
    public bool IsImpenetrable => false;

    /// <inheritdoc/>
    public bool TryGetRunRate(out float rate)
    {
        rate = 0f;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetJumpVelocity(float extent, out float velocityZ)
    {
        velocityZ = 0f;
        return false;
    }

    /// <inheritdoc/>
    public bool CanJump(float extent) => false;

    /// <inheritdoc/>
    public bool TryGetJumpStaminaCost(float extent, out int staminaCost)
    {
        staminaCost = 0;
        return false;
    }
}
