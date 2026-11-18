using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Geometry;
using Lifestone.Data.Models;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Physics.Primitives;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Routes;

/// <summary>A path through a landblock's rooms: the cells in order and the points to walk through.</summary>
/// <param name="Cells">The cells from the start cell to the target cell, each next one through a portal of the one before.</param>
/// <param name="Waypoints">
/// Two points per doorway: one in front of it in the room being left (<see cref="CellRoutePlanner.ApproachMetres"/>
/// before the portal) and one beyond it in the room entered (<see cref="CellRoutePlanner.PushIntoCellMetres"/> past it),
/// each at the portal's centre over the floor.
/// </param>
public sealed record CellRoute(IReadOnlyList<uint> Cells, IReadOnlyList<CellFrame> Waypoints)
{
    /// <summary>M6 14 pits: the jumps of the route (<see cref="CellRoutePlanner.JumpsFrom"/>), each at the index of its take-off waypoint (the next waypoint is its landing); empty for a walk.</summary>
    public IReadOnlyList<RouteJump> Jumps { get; init; } = [];
}

/// <summary>M6 14 pits: a jump of a <see cref="CellRoute"/>: <see cref="CellRoute.Waypoints"/>[<paramref name="TakeOff"/>] is the brink and the waypoint after it the landing.</summary>
/// <param name="TakeOff">The take-off waypoint's index.</param>
/// <param name="Jump">The jump.</param>
public readonly record struct RouteJump(int TakeOff, DirectedJump Jump);

/// <summary>A portal's doorway in landblock space (<see cref="CellRoutePlanner.DoorwayOf"/>).</summary>
/// <param name="Centre">The polygon's centroid.</param>
/// <param name="Sill">The height of the polygon's lowest corner.</param>
/// <param name="Outward">The unit horizontal direction toward the neighbour cell (outside, for an outside portal).</param>
public readonly record struct Doorway(Vector3 Centre, float Sill, Vector3 Outward);

/// <summary>
/// A walk across a portal on the floor (<see cref="CellRoutePlanner.FloorCrossingOf"/>): the two cells' walkable floor polygons meet at an edge
/// on the portal, whatever the portal's own shape (a ramp's top under a hole in the floor above, a corridor whose cells part along a slanted
/// plane, a step), landblock space.
/// </summary>
/// <param name="From">The shared edge's middle on the floor of the cell being left.</param>
/// <param name="To">The same point on the floor of the cell entered (at most <see cref="CellRoutePlanner.StepUpMetres"/> above or below <paramref name="From"/>).</param>
/// <param name="Into">The unit horizontal direction across the edge into the cell entered.</param>
/// <param name="Width">The length of the shared edge, in metres (at least the human's width).</param>
public readonly record struct FloorCrossing(Vector3 From, Vector3 To, Vector3 Into, float Width);

/// <summary>
/// A climb through a level portal on a stair or ramp standing under it (<see cref="CellRoutePlanner.StairOf"/>), landblock space.
/// </summary>
/// <param name="Foot">Where the stair meets the floor of the cell below.</param>
/// <param name="BeforeFoot">A point on that floor <see cref="CellRoutePlanner.ApproachMetres"/> on from the foot, away from the stair (the foot when the floor does not go on).</param>
/// <param name="Landing">Where a body first stands over the portal's plane, in the cell above.</param>
/// <param name="PastLanding">A point <see cref="CellRoutePlanner.PushIntoCellMetres"/> on from the landing (the landing when the ground does not go on level).</param>
/// <param name="Length">The run from the foot to the landing through the stair's top, in metres.</param>
/// <param name="Up">True when the walk from the portal's own cell climbs (the cell lies below the portal).</param>
/// <param name="Climb">The points from the foot up to the landing, every <see cref="CellRoutePlanner.StairWaypointMetres"/>, each in the cell a body standing there is in (the
/// runner's confirmer places a sample in its nearer waypoint's cell, so a climb through two cells needs its own points).</param>
public readonly record struct Stair(Vector3 Foot, CellFrame BeforeFoot, Vector3 Landing, CellFrame PastLanding, float Length, bool Up, IReadOnlyList<CellFrame> Climb);

/// <summary>
/// One walk on a static object's floor from a cell that has no floor polygon to a cell that has (<see cref="CellRoutePlanner.RunsFrom"/>, M6 7 J), one way: a run is listed from both its ends.
/// </summary>
/// <param name="FromCell">The cell the run leaves.</param>
/// <param name="ToCell">The cell it ends in, however many cells it passes over.</param>
/// <param name="Start">Where it starts, on the static's floor or on the floor at its foot.</param>
/// <param name="Points">The points walked between <paramref name="Start"/> and <paramref name="End"/>.</param>
/// <param name="End">Where it ends.</param>
/// <param name="Length">Its length seen from above, in metres.</param>
public sealed record StaticRun(uint FromCell, uint ToCell, CellFrame Start, IReadOnlyList<CellFrame> Points, CellFrame End, float Length);

/// <summary>
/// Plans a walk between two EnvCells of one landblock: a breadth-first search over the cells' portals
/// (<see cref="EnvCellData.Portals"/>, <see cref="CellPortalData.OtherCellId"/>) that a standing human can walk through,
/// and a pair of waypoints at each doorway (m2-plan PH9, the Academy exit route).
/// </summary>
/// <remarks>
/// <para><b>Doorways.</b> A portal is walked through only when it is upright (its plane's normal within 0.1 of
/// horizontal) and a standing human fits through it: the points 0.2 m and 1.8 m above its sill, and 0.48 m either side
/// of its centre 1 m above the sill, all lie in its polygon (the criterion of the Academy portal test,
/// InteriorCollisionTests). Portals in floors and ceilings and the slivers beside arches are left out; outside portals
/// too (the planner stays in one landblock).</para>
/// <para><b>Waypoints.</b> The portal polygon's centroid (in landblock space), moved along the portal's normal
/// <see cref="ApproachMetres"/> back into the room being left and <see cref="PushIntoCellMetres"/> into the room
/// entered, and dropped onto that room's floor (<see cref="CellStructure.Floors"/>: the highest floor polygon under it,
/// else the sill's height). Walking straight from one to the next crosses each doorway square to it. A point that would
/// fall outside its room (a room thinner than the distance) moves half as far, and an approach point that still does is
/// left out; the point beyond a doorway then falls back to the doorway's centre.</para>
/// <para><b>Lean.</b> A doorway's plane may lean: the Black Spawn Den's plazas are octagons whose doorways lean 11 degrees (the
/// portal normal has a vertical part of 0.196). <c>maxLean</c> (constructor; default 0, today's rule) is the largest
/// vertical part of the normal accepted; the rule never goes below the upright 0.1. The fit test is projected on the flat
/// normal (the horizontal part, renormalised), so a leaning doorway is judged by the width and height of the opening a
/// human walks through it, not of the tilted polygon (P5a: closes <c>m3-fight-planner-lean</c>; the den's own planner is
/// gone). Not retail: Lifestone's own constant.</para>
/// <para><b>Floor crossings</b> (M6 4 W4: <c>m6-cave-doorway-0113-0114</c>, <c>m6-burrow-walk</c>). A portal that is no doorway by the
/// test above is still walked when the two cells' floors meet on it (<see cref="FloorCrossingOf"/>): a walkable floor polygon of each
/// (normal Z at least <see cref="CellStructure.FloorZ"/>, retail <c>PhysicsGlobals::floor_z</c>, the walkable test of
/// <c>OBJECTINFO::validate_walkable</c>, 11.6096 0x0050DAE0) share an edge seen from above, on opposite sides of it, at most
/// <see cref="StepUpMetres"/> apart in height along it (the human Setup's step-up, <c>CTransition::step_up</c>, 11.6096 0x0050C0E0),
/// at least the human's width long, and on the portal's polygon; a standing human's column over the edge (the probes of the fit test)
/// lies in one cell or the other. Tusker Cave's ramp rises under a hole in the floor above (0x5B460113 to 0x5B460114, a level portal);
/// Tusker Burrow's corridors part along slanted planes over a level floor (0x5748020E to 0x5748020F, normal Z 0.857). Its waypoints
/// lie on the floors either side of the edge's middle. The rule reads the geometry only (no per-dungeon value); the runner's physics
/// confirmation still checks every walk.</para>
/// <para><b>Floors below, stairs and pits</b> (M6 4 W6b: <c>m6-abode-pits</c>, <c>m6-holtburg-upper-floor</c>). A cell with no floor polygon of its own stands on
/// the floor of the cell under its floor portal when that floor lies within the step-up under the portal (Tusker Abode's corridor 0x5C4501AE to 0x5C4501B2 over its
/// bridge); further down is a drop, and a real cell with no physics polygons at all is no floor (the Abode's crossroads 0x5C4501B7 over a 6 m pit). A level portal a
/// stair or ramp climbs through is walked by <see cref="StairOf"/>, which asks the landblock's physics (Holtburg's inner stair 0xA9B30103 to 0xA9B3010A, Archmage
/// Cindrue's 0xA9B40117 to 0xA9B4011B); its points are walked between the approach and the push (<see cref="StairClimb"/>). A drop is never walked: retail's edge slide
/// keeps a walking body on the brink (<c>CTransition::edge_slide</c>, 11.6096 0x0050BEA0), and only a jump goes over: a ground's profile names the pits a running jump crosses
/// (<see cref="Jumps"/>, <see cref="PitJump"/>; M6 14 pits), each an edge of its own between the rooms either side (<see cref="JumpsFrom"/>), taken only within the planning body's flight
/// (<see cref="JumpReachMetres"/>). A pit no profile names stays the loud stub <c>TODO(m6-abode-pits)</c> (<see cref="NotePitEdge"/>).</para>
/// <para><b>Wide openings</b> (M6 7 G). A doorway is walked through its polygon's centre unless a static object stands there: an opening of two rooms can be metres wide with a pillar in its middle (Holtburg's 0xA9B30100 and 0xA9B30103,
/// Ecutha's lobby). <see cref="CrossingOffsets"/> asks the physics and gives the nearest clear crossing either side; <see cref="DoorwayPairs"/> gives the room graph both.</para>
/// <para><b>Floors a static carries</b> (M6 7 J, <c>m6-facility-hub-rooms</c>). A cell can have no floor polygon and a walkable floor all the same, when a static object standing in the cell under its floor portal rises to that portal: the Facility Hub's
/// landings and ramps (0x02000623, a block 6 m high with a ramp running 12 m off it, twice: 0x8A020211 over 0x8A0201C9, 0x8A0201C1 over 0x8A02015F). A doorway into such a cell has a floor at its sill when the physics finds one (<see cref="HasFloorsAtSill"/>), and a ramp that ends past
/// its hole's own cells is a <see cref="StaticRun"/> (<see cref="RunsFrom"/>): an edge of the walk that joins the floorless cell to the cell at its foot directly, as the doorways of the cells it passes over lead under the ramp, not onto it.</para>
/// <para>Not retail: the client has no path planner; this drives the scenarios the way a player walks.</para>
/// </remarks>
public sealed class CellRoutePlanner
{
    /// <summary>How far past a doorway the waypoint in the room entered lies (m2-plan PH9: 0.5 m).</summary>
    public const float PushIntoCellMetres = 0.5f;

    /// <summary>How far before a doorway the waypoint in the room being left lies.</summary>
    public const float ApproachMetres = 0.7f;

    /// <summary>The widest a standing human is: the human Setup's sphere radius (0.48 m) either side.</summary>
    public const float HumanHalfWidth = 0.48f;

    /// <summary>The largest vertical part of an upright doorway's normal (the Academy portal test's rule); <c>maxLean</c> raises it, never lowers it.</summary>
    public const float UprightNormalZ = 0.1f;

    /// <summary>The height of a standing human the fit test probes under a doorway's lintel (the human Setup's two spheres: 0.48 m radius).</summary>
    public const float HumanHeight = 1.8f;

    /// <summary>
    /// How far a floor may step up or down where two cells' floors meet (<see cref="FloorCrossingOf"/>): the human Setup's step-up height,
    /// 0.6 m (<c>CPhysicsObj::GetStepUpHeight</c>, 11.6096 0x0050F4D0; the confirmer's step window, docs/wayfinding.md "The confirmer").
    /// </summary>
    public const float StepUpMetres = 0.6f;

    /// <summary>How far apart (horizontally) two floor edges may lie and still be one edge. Not retail: Lifestone's own.</summary>
    public const float FloorEdgeToleranceMetres = 0.05f;

    /// <summary>How far a floor crossing's edge may lie from the portal's polygon. Not retail: Lifestone's own.</summary>
    public const float FloorCrossingPortalMetres = 0.1f;

    /// <summary>How far either side of a floor crossing's edge the headroom probes stand. Not retail: Lifestone's own.</summary>
    public const float FloorCrossingProbeMetres = 0.25f;

    /// <summary>
    /// How many answers of the physics probes (<see cref="StraightWalks"/>, <see cref="GroundAlong"/>, <see cref="Via"/>) the planner's <see cref="Memo"/> keeps of each kind before it forgets them all: their points are mostly the
    /// rooms' own (doorway pairs, floor corners), but a body's start is new at every plan, so the memo stays bounded. Not retail: Lifestone's own.
    /// </summary>
    public const int MaxProbeMemo = 100_000;

    private readonly LandblockCellSet _cells;
    private readonly float _limit;
    private readonly Memo _memo;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), FloorCrossing?> _crossings;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), bool> _walkable;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, StaticBody[]> _statics;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, StaticBody[]> _reaching;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> _ledges;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, int X, int Y, int Z), bool> _footStands;
    private PhysicsSetup? _human;
    private PhysicsObj? _standProbe;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), Stair?> _stairs;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), float[]> _offsets;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 Centre, float Side), IReadOnlyList<CellFrame>?> _steps;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other, float Offset), (CellFrame? Approach, CellFrame Push)> _pairs;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CellStructureLibrary, Dictionary<LandblockCoord, PhysicsWorld>> Worlds = [];
    private Dictionary<uint, List<StaticRun>>? _runs;

    /// <summary>A planner over <paramref name="cells"/> that walks upright doorways only.</summary>
    public CellRoutePlanner(LandblockCellSet cells)
        : this(cells, 0f, 0f)
    {
    }

    /// <summary>A planner over <paramref name="cells"/> that also walks doorways leaning up to <paramref name="maxLean"/> (the vertical part of the portal normal; 0 keeps the upright rule).</summary>
    public CellRoutePlanner(LandblockCellSet cells, float maxLean)
        : this(cells, maxLean, 0f)
    {
    }

    /// <summary>
    /// A planner as above whose fit test probes <paramref name="headSlack"/> metres under <see cref="HumanHeight"/>: the shops' doorways
    /// are exactly 1.8 m tall (Renald's room 0xA9B40141 is reached through two), so the probe at the lintel's own height fails by no margin
    /// at all (0 keeps the Academy rule). Not retail: Lifestone's own.
    /// </summary>
    public CellRoutePlanner(LandblockCellSet cells, float maxLean, float headSlack)
        : this(cells, maxLean, headSlack, memo: null)
    {
    }

    /// <summary>
    /// A planner as above that answers from <paramref name="memo"/> (<see cref="Memo"/>, M6 14 hub plan): what the planners of the same cells, lean and head slack have already worked out of the geometry and the physics is not worked out again.
    /// A memo made for other cells (another <see cref="LandblockCellSet"/> object, a subset left without some cells), another lean or another head slack is refused.
    /// </summary>
    /// <param name="cells">The cells planned over.</param>
    /// <param name="maxLean">The lean accepted.</param>
    /// <param name="headSlack">The fit test's head slack.</param>
    /// <param name="memo">The memo to share, or null for a planner's own.</param>
    /// <exception cref="ArgumentException">The memo is another cell set's, lean's or head slack's.</exception>
    public CellRoutePlanner(LandblockCellSet cells, float maxLean, float headSlack, Memo? memo)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLean);
        ArgumentOutOfRangeException.ThrowIfNegative(headSlack);
        if (memo is not null && !memo.Serves(cells, maxLean, headSlack))
        {
            throw new ArgumentException("the memo is another cell set's, lean's or head slack's", nameof(memo));
        }

        _cells = cells;
        MaxLean = maxLean;
        HeadSlack = headSlack;
        _limit = MathF.Max(UprightNormalZ, maxLean);
        _memo = memo ?? new Memo(cells, maxLean, headSlack);
        (_crossings, _walkable, _statics, _reaching, _ledges, _footStands) = (_memo.Crossings, _memo.Walkable, _memo.Statics, _memo.Reaching, _memo.Ledges, _memo.FootStands);
        (_stairs, _offsets, _steps, _pairs) = (_memo.Stairs, _memo.Offsets, _memo.Steps, _memo.Pairs);
    }

    /// <summary>
    /// What a planner has worked out of one cell set's geometry and its landblock's fixed physics (M6 14 hub plan): every answer here is the cells', the lean's and the head slack's alone, never a run's
    /// (not the profile's <see cref="Jumps"/>, the body's <see cref="JumpReachMetres"/>, a creature, a kept portal), so the planners of the same cells share it (<see cref="CellRoutePlanner(LandblockCellSet, float, float, Memo?)"/>):
    /// the journey makes a room graph per leg, and every leg in the Town Network (0x0007) worked the same doorways and probed the same steps again, 7 to 8 s of the first plan of each hub pass (live 2026-11-16 16:54:52 to 16:55:04 UTC).
    /// Safe on several threads, as the planner's caches always were.
    /// </summary>
    public sealed class Memo
    {
        private readonly LandblockCellSet _cells;
        private readonly float _lean;
        private readonly float _slack;

        /// <summary>An empty memo for the planners of <paramref name="cells"/> at <paramref name="maxLean"/> and <paramref name="headSlack"/>.</summary>
        /// <param name="cells">The cells.</param>
        /// <param name="maxLean">The lean.</param>
        /// <param name="headSlack">The head slack.</param>
        public Memo(LandblockCellSet cells, float maxLean, float headSlack)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
            (_lean, _slack) = (maxLean, headSlack);
        }

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), FloorCrossing?> Crossings { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), bool> Walkable { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<uint, StaticBody[]> Statics { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<uint, StaticBody[]> Reaching { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<uint, bool> Ledges { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, int X, int Y, int Z), bool> FootStands { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), Stair?> Stairs { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other), float[]> Offsets { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 Centre, float Side), IReadOnlyList<CellFrame>?> Steps { get; } = new();

        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, ushort Polygon, uint Other, float Offset), (CellFrame? Approach, CellFrame Push)> Pairs { get; } = new();

        /// <summary>The pre-check's probe of a straight step (<see cref="StraightWalks"/>), by its cell and ends.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), bool> Straight { get; } = new();

        /// <summary>The foot's ground along a straight step (<see cref="GroundAlong"/>), by its cell and ends.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), bool> Ground { get; } = new();

        /// <summary>The floor test of a straight step (<see cref="StaysOnFloor"/>) as <see cref="FloorDetour"/> asks it, by its cell and ends.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), bool> Floors { get; } = new();

        /// <summary>Whether a detour may pass a point (<see cref="Via"/>), by its cell and point.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 Point), bool> Vias { get; } = new();

        /// <summary>The room's detour of a straight walk with no step refused (<see cref="FloorDetour"/> with no refusal) and whether it is left unwalked (<see cref="JudgedFloorDetour"/>), by its cell and ends.</summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), (Vector3[] Vias, bool Unwalked)> Detours { get; } = new();

        /// <summary>
        /// M6 14 (m6-14-plaza): the rooms' detours a room graph judged with a list of kept portals and nothing else refusing a step (no plate, no avoid box), by the list's content (<c>RoomGraph</c>): the hop's leg works its rooms out on a
        /// worker with the list it will keep, and the runner's graph, given a list of the same portals and clearances, takes those detours instead of searching them again on the game's thread (the Town Network's leg keeps 54 portals since every
        /// placed portal is on the lists). An answer here is the list's, never another's: a list of other content has its own.
        /// </summary>
        internal System.Collections.Concurrent.ConcurrentDictionary<object, System.Collections.Concurrent.ConcurrentDictionary<(uint Cell, Vector3 From, Vector3 To), (IReadOnlyList<CellFrame> Vias, bool Unwalked)>> KeptDetours { get; } = new();

        /// <summary>
        /// False: <see cref="JudgedFloorDetour"/> searches every walk with refusals afresh, as before the memo (the tests' reference for the reuse it otherwise makes). The probes' answers are kept either way: they read no refusal.
        /// </summary>
        internal bool ReuseBareDetours { get; init; } = true;

        /// <summary>The cells the memo is of.</summary>
        public LandblockCellSet Cells => _cells;

        /// <summary>The lean the memo is of.</summary>
        public float MaxLean => _lean;

        /// <summary>The head slack the memo is of.</summary>
        public float HeadSlack => _slack;

        /// <summary>True when the memo is of <paramref name="cells"/> (the same object) at this lean and head slack.</summary>
        /// <param name="cells">The cells.</param>
        /// <param name="maxLean">The lean.</param>
        /// <param name="headSlack">The head slack.</param>
        public bool Serves(LandblockCellSet cells, float maxLean, float headSlack) => ReferenceEquals(cells, _cells) && maxLean == _lean && headSlack == _slack;

        /// <summary>The answer of <paramref name="key"/> in <paramref name="memo"/>, worked out by <paramref name="work"/> once (the memo forgotten past <see cref="MaxProbeMemo"/>).</summary>
        internal static TValue Answer<TKey, TValue, TState>(System.Collections.Concurrent.ConcurrentDictionary<TKey, TValue> memo, TKey key, Func<TKey, TState, TValue> work, TState state)
            where TKey : notnull
        {
            if (memo.TryGetValue(key, out var known))
            {
                return known;
            }

            if (memo.Count > MaxProbeMemo)
            {
                memo.Clear();
            }

            return memo.GetOrAdd(key, work, state);
        }
    }

    /// <summary>The memo this planner answers from (share it with another planner of the same cells: <see cref="CellRoutePlanner(LandblockCellSet, float, float, Memo?)"/>).</summary>
    public Memo Shared => _memo;

    /// <summary>The lean accepted beyond the upright rule: the largest vertical part of a doorway's normal (0: upright only).</summary>
    public float MaxLean { get; }

    /// <summary>How far under <see cref="HumanHeight"/> the fit test probes (0: at the full height).</summary>
    public float HeadSlack { get; }

    /// <summary>The cells planned over.</summary>
    public LandblockCellSet Cells => _cells;

    /// <summary>
    /// M6 14 pits: the pits a running jump crosses (the ground's profile, <c>jumps</c>; <see cref="PitJump"/>), each an edge both ways between its two rooms when both are planned over. Empty: no jump, today's walk.
    /// </summary>
    public IReadOnlyList<PitJump> Jumps { get; set; } = [];

    /// <summary>
    /// M6 14 pits: the planning body's flight (<see cref="JumpReach.FlightMetres(Lifestone.Data.Physics.Objects.IPhysicsWeenie)"/>), set by a run at each plan (<c>NavRunner</c>, as the creature layer): a jump whose need is longer is no edge
    /// (logged once a run by <see cref="NotePitReach"/>). Null: no body is planning (a draft, a tour's walk lengths, a test), and the profile's jumps are taken as its data says; the runner checks the body's flight before it jumps.
    /// </summary>
    public float? JumpReachMetres { get; set; }

    /// <summary>
    /// M6 14 pit-hold: the pit and its side when <paramref name="cellId"/> is one of <see cref="Jumps"/>' floorless pit cells and <paramref name="point"/> (landblock-local) stands on a side's lip (<see cref="PitJump.LipSideOf"/>): a body held
    /// at the brink by the edge slide with its centre past the doorway, which the physics puts in the pit's cell. Null otherwise (not a pit cell, or a body down in the pit).
    /// </summary>
    /// <param name="cellId">The cell the body stands in.</param>
    /// <param name="point">Where it stands, landblock-local.</param>
    public (PitJump Pit, PitJumpSide Side)? LipOf(uint cellId, Vector3 point)
    {
        foreach (var crossing in Jumps)
        {
            if (crossing.PitCell == cellId && crossing.LipSideOf(point) is { } side)
            {
                return (crossing, side);
            }
        }

        return null;
    }

    /// <summary>M6 14 pits: the jumps a body plans from <paramref name="cellId"/> (both rooms planned over, the need within <see cref="JumpReachMetres"/> when it is set).</summary>
    /// <param name="cellId">The room jumped from.</param>
    public IReadOnlyList<DirectedJump> JumpsFrom(uint cellId)
    {
        if (Jumps.Count == 0)
        {
            return [];
        }

        List<DirectedJump>? found = null;
        foreach (var crossing in Jumps)
        {
            foreach (var jump in crossing.Directions)
            {
                if (jump.From.Cell != cellId || _cells.GetCell(jump.To.Cell) is null)
                {
                    continue;
                }

                if (JumpReachMetres is { } reach && reach < jump.NeedMetres)
                {
                    NotePitReach(jump, reach);
                    continue;
                }

                (found ??= []).Add(jump);
            }
        }

        return found is null ? [] : found;
    }

    private static int _pitReach;

    /// <summary>How many jumps the planners have left out for a body's flight since the last <see cref="ResetPitLog"/> (the first is the one logged).</summary>
    public static int PitJumpsShort => Volatile.Read(ref _pitReach);

    /// <summary>
    /// The loud line for a jump left out for the body's flight (M6 14 pits), once a run (<see cref="ResetPitLog"/>): the jump, its need and the body's reach, so a walk to the far side that ends no-path says why.
    /// </summary>
    private static void NotePitReach(DirectedJump jump, float reach)
    {
        if (Interlocked.Increment(ref _pitReach) != 1)
        {
            return;
        }

        var message = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"the jump {jump.Name} needs a flight of {jump.NeedMetres:0.00} m and the body's full-bar running jump flies {reach:0.00} m (its Run and Jump skills, buffs counted): no jump is planned, so a walk to the far side ends no-path");
        if (PitLog is Lifestone.Client.Diagnostics.NullLogSink)
        {
            Console.Error.WriteLine("[route] " + message);
        }
        else
        {
            Lifestone.Client.Diagnostics.LogSinkExtensions.Warn(PitLog, PitLogCategory, message);
        }
    }

    /// <summary>
    /// The shortest walk (fewest doorways) from <paramref name="fromCellId"/> to <paramref name="toCellId"/>, or null when
    /// either cell is not in the landblock or no walkable doorways join them.
    /// </summary>
    public CellRoute? Plan(uint fromCellId, uint toCellId)
    {
        var path = FindPath(fromCellId, toCellId);
        if (path is null)
        {
            return null;
        }

        var waypoints = new List<CellFrame>();
        var jumps = new List<RouteJump>();
        for (var i = 0; i + 1 < path.Count; i++)
        {
            var cell = _cells.GetCell(path[i])!;
            if (FindWalkablePortal(cell, path[i + 1]) is null && JumpsFrom(cell.Id).FirstOrDefault(j => j.To.Cell == path[i + 1]) is { Crossing: not null } jump)
            {
                waypoints.Add(jump.RunUp);
                jumps.Add(new RouteJump(waypoints.Count, jump));
                waypoints.Add(jump.TakeOff);
                waypoints.Add(jump.Landing);
                continue;
            }

            if (FindWalkablePortal(cell, path[i + 1]) is not { } portal)
            {
                var run = RunsFrom(cell.Id).First(r => r.ToCell == path[i + 1]);
                waypoints.Add(run.Start);
                waypoints.AddRange(run.Points);
                waypoints.Add(run.End);
                continue;
            }

            var (approach, push) = DoorwayPair(cell, portal);
            if (approach is { } before)
            {
                waypoints.Add(before);
            }

            waypoints.AddRange(StairClimb(cell, portal));
            waypoints.Add(push);
        }

        return new CellRoute(path, waypoints) { Jumps = jumps };
    }

    /// <summary>
    /// The cells of the shortest walk (breadth first, portals in stored order) from <paramref name="fromCellId"/> to
    /// <paramref name="toCellId"/> through walkable doorways (<see cref="IsWalkable"/>), or null.
    /// </summary>
    public IReadOnlyList<uint>? FindPath(uint fromCellId, uint toCellId)
    {
        if (_cells.GetCell(fromCellId) is null || _cells.GetCell(toCellId) is null)
        {
            return null;
        }

        var previous = new Dictionary<uint, uint> { [fromCellId] = 0 };
        var queue = new Queue<uint>();
        queue.Enqueue(fromCellId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == toCellId)
            {
                var path = new List<uint>();
                for (var at = toCellId; at != 0; at = previous[at])
                {
                    path.Add(at);
                    if (at == fromCellId)
                    {
                        break;
                    }
                }

                path.Reverse();
                return path;
            }

            var cell = _cells.GetCell(id)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || previous.ContainsKey(portal.OtherCellId) || _cells.GetCell(portal.OtherCellId) is null
                    || !IsWalkable(cell, portal))
                {
                    continue;
                }

                previous[portal.OtherCellId] = id;
                queue.Enqueue(portal.OtherCellId);
            }

            foreach (var run in RunsFrom(id))
            {
                if (previous.TryAdd(run.ToCell, id))
                {
                    queue.Enqueue(run.ToCell);
                }
            }

            foreach (var jump in JumpsFrom(id))
            {
                if (previous.TryAdd(jump.To.Cell, id))
                {
                    queue.Enqueue(jump.To.Cell);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// True when a standing human can walk through <paramref name="portal"/> of <paramref name="cell"/>: an inside portal
    /// in a polygon wide and tall enough (see <see cref="Fits"/> and the remarks), or one the two cells' floors meet on
    /// (<see cref="FloorCrossingOf"/>).
    /// </summary>
    public bool IsWalkable(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return !portal.LeadsOutside
            && _walkable.GetOrAdd((cell.Id, portal.PolygonId, portal.OtherCellId), _ =>
            {
                if (IsLedge(cell) || (_cells.GetCell(portal.OtherCellId) is { } other && IsLedge(other)))
                {
                    return false;
                }

                if (IsDoorway(cell, portal) || FloorCrossingOf(cell, portal) is not null || StairOf(cell, portal) is not null)
                {
                    return true;
                }

                NotePitEdge(cell, portal);
                return false;
            });
    }

    /// <summary>The category of the pit stub's log line.</summary>
    public const string PitLogCategory = "route";

    private static int _pitEdges;

    /// <summary>How many pit edges the planners have refused since the last <see cref="ResetPitLog"/> (the first is the one logged).</summary>
    public static int PitEdgesRefused => Volatile.Read(ref _pitEdges);

    /// <summary>
    /// Where the pit stub is written (<c>TODO(m6-abode-pits)</c>). The default is none: <see cref="NotePitEdge"/> then writes to the standard error stream, so a headless run is never silent.
    /// A host with a log sets it once at start.
    /// </summary>
    public static Lifestone.Client.Diagnostics.ILogSink PitLog { get; set; } = Lifestone.Client.Diagnostics.NullLogSink.Instance;

    /// <summary>Forgets the pit edges already logged, so the next run logs them afresh (a run's start, and the tests).</summary>
    public static void ResetPitLog()
    {
        Volatile.Write(ref _pitEdges, 0);
        Volatile.Write(ref _pitReach, 0);
    }

    /// <summary>
    /// The loud stub for a pit edge (M6 4 F2, review 4-R5): a doorway-shaped portal the room graph refuses because one side has no floor to stand on (a drop: nothing at or above the sill less the step-up
    /// under the doorway's beside: Tusker Abode and Holding's 0x5C4502A9, 10 m wide, 12 m deep onto a bridge) while the other side does. A walking body stops at the brink (retail's edge slide,
    /// <c>CTransition::edge_slide</c>, 11.6096 0x0050BEA0) and only a jump crosses, so a walk to the far half ends <c>no path</c>: the line names the stub and the two cells of the first one, once a run (the count of the rest is <see cref="PitEdgesRefused"/>). Not logged for a threshold that sits higher than its neighbours (Holtburg's 0xA9B40140: its floor is above the sill, not below it).
    /// </summary>
    private void NotePitEdge(EnvCellData cell, CellPortalData portal)
    {
        if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other || !Fits(cell, portal) || DoorwayOf(cell, portal) is not { } doorway)
        {
            return;
        }

        var here = HasFloorAt(cell, doorway, -1f);
        var there = HasFloorAt(other, doorway, 1f);
        if (here == there)
        {
            return; // both stand (not refused for a floor) or neither does (no edge of a floor to speak of)
        }

        var (floorless, side) = here ? (other, 1f) : (cell, -1f);
        if (Jumps.Any(j => j.PitCell == floorless.Id))
        {
            return; // M6 14 pits: the profile's jump crosses this pit (JumpsFrom): the doorway stays refused as a walk, and no stub is due
        }

        var at = doorway.Centre + (doorway.Outward * (0.25f * side));
        var below = FloorUnder(floorless, new Vector3(at.X, at.Y, doorway.Sill + 50f));
        if (below is { } z && z >= doorway.Sill - StepUpMetres)
        {
            return; // a floor at or above the sill: a high threshold, not a drop
        }

        if (Interlocked.Increment(ref _pitEdges) != 1)
        {
            return;
        }

        var drop = below is { } floor ? $"{doorway.Sill - floor:0.0} m" : "no floor at all";
        var message = $"TODO(m6-abode-pits): the room graph refuses the pit edge 0x{cell.Id:X8} to 0x{portal.OtherCellId:X8} (sill {doorway.Sill:0.00}, the drop {drop}): a walking body stops at the brink and only a jump crosses, and this graph has no jump over this pit (a ground profile's jumps section), so a walk to the far side ends no-path";
        if (PitLog is Lifestone.Client.Diagnostics.NullLogSink)
        {
            Console.Error.WriteLine("[route] " + message);
        }
        else
        {
            Lifestone.Client.Diagnostics.LogSinkExtensions.Warn(PitLog, PitLogCategory, message);
        }
    }

    /// <summary>
    /// True when <paramref name="portal"/> is walked as a doorway: a standing human fits through it (<see cref="Fits"/>) and the floors either side meet its sill
    /// (<see cref="HasFloorsAtSill"/>).
    /// </summary>
    public bool IsDoorway(EnvCellData cell, CellPortalData portal) => Fits(cell, portal) && HasFloorsAtSill(cell, portal);

    /// <summary>
    /// True when both cells of <paramref name="portal"/> have a walkable floor within <see cref="StepUpMetres"/> of the doorway's sill just beside it (0.1 m to
    /// 1 m from the polygon's centre, there and 0.48 m either side; the cell being left before it, the cell entered past it; M6 7 J: or a floor a static carries, <see cref="StaticFloorPoint"/>), so a body steps over the sill
    /// both ways (M6 4 W4; <c>TODO(room-steps)</c>'s rule). A doorway is a hole in a wall, and where a room has no floor at its sill's height the body has
    /// nothing to step from or onto: Tusker Burrow's 0x574801F0 opens at y -55 into two cells through one polygon, the corridor 0x574801A5 at the sill's
    /// z -12 and the ramp room 0x574801A6 whose floors there are 3 m up; the Tusker Abode's 0x5C4502A9 and the Academy's 0x860202C4 and 0x860202C5 (the
    /// Sentry's shafts down to 0x8602013B and 0x8602013C) have no floor at all; Holtburg's 0xA9B40140, the threshold of Renald's back room, sits 1 m over the
    /// floors of 0xA9B4013F and 0xA9B40141 either side. True also for an outside portal, a neighbour not in the set, and a structure with no physics
    /// polygons at all (the unit tests' synthetic rooms give no evidence of their floors).
    /// </summary>
    public bool HasFloorsAtSill(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other)
        {
            return true;
        }

        if (DoorwayOf(cell, portal) is not { } doorway)
        {
            return false;
        }

        return HasFloorAt(cell, doorway, -1f) && HasFloorAt(other, doorway, 1f);
    }

    /// <summary>True when <paramref name="floorCell"/> has a walkable floor within <see cref="StepUpMetres"/> of <paramref name="doorway"/>'s sill beside it, on the side <paramref name="side"/> (1: past it, -1: before it).</summary>
    private bool HasFloorAt(EnvCellData floorCell, Doorway doorway, float side)
    {
        if (_cells.GetStructure(floorCell).PhysicsPolygonCount == 0 && _cells.Structures.Dats is null)
        {
            return true; // a synthetic room of the unit tests: no evidence of its floor (a real cell with no physics polygons stands on a floor below, or on none)
        }

        var along = new Vector3(-doorway.Outward.Y, doorway.Outward.X, 0f);
        foreach (var distance in (ReadOnlySpan<float>)[0.1f, 0.25f, PushIntoCellMetres, 1f])
        {
            foreach (var offset in (ReadOnlySpan<float>)[0f, -HumanHalfWidth, HumanHalfWidth])
            {
                var at = doorway.Centre + (doorway.Outward * (distance * side)) + (along * offset);
                if (FloorUnder(floorCell, new Vector3(at.X, at.Y, doorway.Sill + StepUpMetres)) is { } z && z >= doorway.Sill - StepUpMetres)
                {
                    return true;
                }
            }
        }

        return StaticFloorPoint(floorCell, doorway, side) is not null || StepsDownPast(floorCell, doorway, side) is not null;
    }

    /// <summary>How far past a doorway (and either side of its centre) a static object of the room must stand to be asked as the doorway's steps (<see cref="StepsDownPast"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float DoorwayStepsReachMetres = 2f;

    /// <summary>How far past the doorway the descent of <see cref="StepsDownPast"/> starts, in metres: on the sill's lip, inside the room. Not retail: Lifestone's own.</summary>
    public const float DoorwayStepsStartMetres = 0.1f;

    /// <summary>
    /// M6 14 guard task (closes <c>TODO(m6-lin-shoichi-walk)</c>; the doorway half of <c>TODO(room-steps)</c>): the walk down a doorway's steps into <paramref name="floorCell"/>, or null. A doorway may open over a room whose own floor lies more than
    /// <see cref="StepUpMetres"/> under its sill when steps made of static objects run down from the sill to that floor: Shoichi's house in Lin (landblock 0xDA3B), whose street door 0xDA3B0109 (sill z 8.00 at x 117.5) opens over the entry
    /// 0xDA3B0118 (floor z 6.80), and the three steps 0x01000ACD (z 7.60), 0x01000AC5 (7.20) and 0x01000ACA (6.80) run west from the sill, 0.5 m apart. The floor polygons alone call that a pit edge, so the room graph refused every walk
    /// to him. The rule: <paramref name="floorCell"/> has floor polygons (a room with none is <see cref="StaticFloorPoint"/>'s), a static object of the room stands within <see cref="DoorwayStepsReachMetres"/> past the doorway (the cheap
    /// evidence before the physics), and the landblock's fixed world gives a ground on the sill's lip (<see cref="DoorwayStepsStartMetres"/> past it, within the step-up of the sill) from which a walk of samples, each within the step-up of
    /// the last and never rising (<see cref="Follow"/>, retail's step down, <c>CTransition::step_down</c>, 11.6096 0x0050BD70), reaches the room's own floor. A drop with no steps has no such walk (retail's edge slide keeps a walking body at
    /// the brink), so it stays the pit stub's. The runner's physics confirms every walk. Computed once per doorway.
    /// </summary>
    /// <param name="floorCell">The room past the doorway.</param>
    /// <param name="doorway">The doorway.</param>
    /// <param name="side">1: the room lies past the doorway's outward direction; -1: before it.</param>
    private IReadOnlyList<CellFrame>? StepsDownPast(EnvCellData floorCell, Doorway doorway, float side)
    {
        if (_cells.Structures.Dats is null || _cells.GetStructure(floorCell).Floors.Count == 0)
        {
            return null;
        }

        return _steps.GetOrAdd((floorCell.Id, doorway.Centre, side), _ => FindStepsDown(floorCell, doorway, side));
    }

    private IReadOnlyList<CellFrame>? FindStepsDown(EnvCellData floorCell, Doorway doorway, float side)
    {
        var outward = doorway.Outward * side;
        var along = new Vector3(-outward.Y, outward.X, 0f);
        var near = floorCell.StaticObjects.Any(o =>
        {
            var d = o.Frame.Origin - doorway.Centre;
            var past = Vector3.Dot(d, outward);
            return past >= 0f && past <= DoorwayStepsReachMetres && MathF.Abs(Vector3.Dot(d, along)) <= DoorwayStepsReachMetres && o.Frame.Origin.Z <= doorway.Sill + StepUpMetres;
        });
        if (!near || World() is not { } world)
        {
            return null;
        }

        lock (world)
        {
            var lip = (doorway.Centre + (outward * DoorwayStepsStartMetres)) with { Z = doorway.Sill };
            if (Ground(world, lip, floorCell.Id) is not { } top || MathF.Abs(top.Point.Z - doorway.Sill) > StepUpMetres)
            {
                return null;
            }

            var foot = Follow(world, top, stack: null, descend: true, (p, c) => c == floorCell.Id && p.Z < doorway.Sill - StepUpMetres && FootRestOn(floorCell, p + new Vector3(0f, 0f, 0.05f)) is { } f && p.Z <= f + 0.05f);
            if (foot is not { } end)
            {
                return null;
            }

            var every = Math.Max(1, (int)MathF.Round(StairWaypointMetres / StairSampleMetres));
            return [.. end.Samples.Where((_, i) => i > 0 && (i % every == 0 || i == end.Samples.Count - 1)).Select(s => new CellFrame(s.Cell, new AcFrame(s.Point, Quaternion.Identity)))];
        }
    }

    /// <summary>How far past a doorway the physics is asked for a static's floor (<see cref="StaticFloorPoint"/>), in metres: the human's radius and a margin, so the foot does not rest on the doorway's own floor. Not retail: Lifestone's own.</summary>
    public const float StaticFloorProbeMetres = 1f;

    /// <summary>The longest walk on a static's floor <see cref="RunsFrom"/> follows, in metres: the Facility Hub's two are 15 m. Not retail: Lifestone's own.</summary>
    public const float StaticRunMaxMetres = 30f;

    /// <summary>
    /// The floor a static carries <see cref="StaticFloorProbeMetres"/> past <paramref name="doorway"/> on the side <paramref name="side"/>, or null: <paramref name="floorCell"/> has no floor polygon, a static object rises under its floor portal
    /// (<see cref="StaticUnderFloorPortal"/>) and the physics finds a floor there within <see cref="StepUpMetres"/> of the sill (the remarks' "Floors a static carries"). A real room has its polygons; nothing else builds the physics here.
    /// </summary>
    private Vector3? StaticFloorPoint(EnvCellData floorCell, Doorway doorway, float side)
    {
        if (_cells.GetStructure(floorCell).Floors.Count != 0 || !StaticUnderFloorPortal(floorCell) || World() is not { } world)
        {
            return null;
        }

        var at = (doorway.Centre + (doorway.Outward * (StaticFloorProbeMetres * side))) with { Z = doorway.Sill };
        lock (world)
        {
            return world.TryFixedGroundZ(new CellFrame(floorCell.Id, new AcFrame(at, Quaternion.Identity)), StairFoot, out var z, out _) && MathF.Abs(z - doorway.Sill) <= StepUpMetres ? at with { Z = z } : null;
        }
    }

    /// <summary>True when a static object of a cell under a floor portal of <paramref name="cell"/> stands in that hole, its origin inside it seen from above (<see cref="FindStair"/>'s evidence): the cheap test before the physics.</summary>
    private bool StaticUnderFloorPortal(EnvCellData cell)
    {
        var structure = _cells.GetStructure(cell);
        foreach (var portal in cell.Portals)
        {
            if (portal.LeadsOutside || structure.GetPortal(portal.PolygonId) is not { } polygon || _cells.GetCell(portal.OtherCellId) is not { } other)
            {
                continue;
            }

            var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
            if ((portal.PortalSide ? normal.Z : -normal.Z) > -0.99f)
            {
                continue;
            }

            var corners = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
            if (other.StaticObjects.Any(o => InPolygon2D(corners, o.Frame.Origin.X, o.Frame.Origin.Y)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The walks on a static's floor out of <paramref name="cellId"/> (the remarks' "Floors a static carries"), each to the cell at its other end; empty for nearly every cell. A run starts past a doorway of a cell with no floor polygon, on the floor the physics
    /// finds there (<see cref="HasFloorsAtSill"/>), and follows the ground (<see cref="Follow"/>: a straight line of the probes every <see cref="StairSampleMetres"/>, each into the same cell or one its portals join) down to a cell's own floor more than <see cref="StepUpMetres"/>
    /// below the doorway: a level walk across such a cell is its doorways' own. Every run is listed from both ends. Named limit: a ramp that turns is no straight line, so it has no run. Computed once for the planner's cells. Not retail: Lifestone's own; the physics confirms every walk.
    /// </summary>
    public IReadOnlyList<StaticRun> RunsFrom(uint cellId)
    {
        var runs = _runs;
        if (runs is null)
        {
            runs = FindRuns();
            _runs = runs;
        }

        return runs.TryGetValue(cellId, out var list) ? list : [];
    }

    private Dictionary<uint, List<StaticRun>> FindRuns()
    {
        var runs = new Dictionary<uint, List<StaticRun>>();
        if (_cells.Structures.Dats is null)
        {
            return runs;
        }

        foreach (var id in _cells.Ids)
        {
            var cell = _cells.GetCell(id)!;
            if (_cells.GetStructure(cell).Floors.Count != 0 || !StaticUnderFloorPortal(cell))
            {
                continue;
            }

            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is null || !Fits(cell, portal) || DoorwayOf(cell, portal) is not { } doorway
                    || StaticFloorPoint(cell, doorway, -1f) is not { } start || !IsDoorway(cell, portal) || World() is not { } world)
                {
                    continue;
                }

                lock (world)
                {
                    if (FollowRun(world, cell, start) is { } run)
                    {
                        Add(run);
                        Add(new StaticRun(run.ToCell, run.FromCell, run.End, [.. run.Points.Reverse()], run.Start, run.Length));
                    }
                }
            }
        }

        return runs;

        void Add(StaticRun run)
        {
            if (!runs.TryGetValue(run.FromCell, out var list))
            {
                runs[run.FromCell] = list = [];
            }

            list.Add(run);
        }
    }

    /// <summary>The run of <see cref="RunsFrom"/> from <paramref name="start"/> in the floorless <paramref name="cell"/>, or null: the shortest walk down the ground to a floor of another cell.</summary>
    private StaticRun? FollowRun(PhysicsWorld world, EnvCellData cell, Vector3 start)
    {
        if (Ground(world, start, cell.Id) is not { } top)
        {
            return null;
        }

        var foot = Follow(world, top, stack: null, descend: true, (p, c) => p.Z < start.Z - StepUpMetres && FootRestOn(_cells.GetCell(c)!, p + new Vector3(0f, 0f, 0.05f)) is { } f && p.Z <= f + 0.05f, StaticRunMaxMetres);
        if (foot is not { } end || end.Beyond.CellId == cell.Id)
        {
            return null;
        }

        var every = Math.Max(1, (int)MathF.Round(StairWaypointMetres / StairSampleMetres));
        var points = end.Samples.Where((_, i) => i > 0 && i % every == 0).Select(c => new CellFrame(c.Cell, new AcFrame(c.Point, Quaternion.Identity))).ToList();
        return new StaticRun(cell.Id, end.Beyond.CellId, new CellFrame(top.Cell, new AcFrame(top.Point, Quaternion.Identity)), points, end.Beyond, end.Run);
    }

    /// <summary>
    /// The floor crossing of <paramref name="portal"/> of <paramref name="cell"/> (the remarks' "Floor crossings"), or null: an inside portal into
    /// a cell of the set where a walkable floor polygon of each cell shares an edge, seen from above, on opposite sides of it, at most
    /// <see cref="StepUpMetres"/> apart in height at both ends of the shared part, the shared part at least twice <see cref="HumanHalfWidth"/> long
    /// and within <see cref="FloorCrossingPortalMetres"/> of the portal's polygon, with a standing human's column (0.2 m, 1 m and the lintel
    /// probe's height over the floor, the middle and 0.48 m either side at 1 m) in one of the two cells
    /// <see cref="FloorCrossingProbeMetres"/> either side of the edge. The longest such edge wins. Computed once per portal.
    /// </summary>
    public FloorCrossing? FloorCrossingOf(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other)
        {
            return null;
        }

        return _crossings.GetOrAdd((cell.Id, portal.PolygonId, other.Id), _ => FindFloorCrossing(cell, portal, other));
    }

    /// <summary>
    /// True when <paramref name="portal"/>'s polygon stands upright enough to be a doorway: it exists in the cell's structure and its plane's
    /// normal leans no more than the limit (0.1, or <see cref="MaxLean"/>) from horizontal. Floors and ceilings are not.
    /// </summary>
    public bool IsUpright(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        return MathF.Abs(normal.Z) <= _limit && (normal.X * normal.X) + (normal.Y * normal.Y) >= 1e-6f;
    }

    /// <summary>
    /// True when a standing human fits through <paramref name="portal"/> of <paramref name="cell"/>, wherever it leads
    /// (an outside portal included): its plane's normal leans no more than the limit (0.1, or <see cref="MaxLean"/>), and
    /// the points 0.2 m and 1.8 m above its sill, and 0.48 m either side of its centre 1 m above the sill, all lie in its
    /// polygon seen along the flat normal.
    /// </summary>
    public bool Fits(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return false;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        if (MathF.Abs(normal.Z) > _limit)
        {
            return false;
        }

        var flat = new Vector3(normal.X, normal.Y, 0f);
        if (flat.LengthSquared() < 1e-6f)
        {
            return false; // a floor or a ceiling
        }

        Span<Vector3> corners = stackalloc Vector3[polygon.Vertices.Length];
        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        for (var i = 0; i < corners.Length; i++)
        {
            corners[i] = cell.Frame.LocalToParent(polygon.Vertices[i]);
            centre += corners[i];
            sill = MathF.Min(sill, corners[i].Z);
        }

        centre /= corners.Length;
        flat = Vector3.Normalize(flat);
        var along = new Vector3(-flat.Y, flat.X, 0f);
        var floor = new Vector3(centre.X, centre.Y, sill);
        return InPortal(corners, along, floor + new Vector3(0, 0, 0.2f))
            && InPortal(corners, along, floor + new Vector3(0, 0, HumanHeight - HeadSlack))
            && InPortal(corners, along, floor + new Vector3(0, 0, 1f) + (along * HumanHalfWidth))
            && InPortal(corners, along, floor + new Vector3(0, 0, 1f) - (along * HumanHalfWidth));
    }

    /// <summary>
    /// The doorway of <paramref name="portal"/> in landblock space: the polygon's centroid, its sill height and the unit
    /// horizontal direction toward the neighbour (outside, for an outside portal; <see cref="Waypoint(EnvCellData, CellPortalData, float)"/>'s rule), or null
    /// when the cell's structure has no such portal polygon or its normal is vertical.
    /// </summary>
    public Doorway? DoorwayOf(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return null;
        }

        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        foreach (var vertex in polygon.Vertices)
        {
            var corner = cell.Frame.LocalToParent(vertex);
            centre += corner;
            sill = MathF.Min(sill, corner.Z);
        }

        centre /= polygon.Vertices.Length;
        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        var into = portal.PortalSide ? normal : -normal;
        into.Z = 0f;
        if (into.LengthSquared() < 1e-6f)
        {
            return null;
        }

        return new Doorway(centre, sill, Vector3.Normalize(into));
    }

    /// <summary>
    /// The hub walk (2026-11-18): where along a flat doorway a walk may cross it square (the room graph's crossings beside a kept portal, <c>RoomGraph.Beside</c>): from the opening's centre (<see cref="DoorwayOf"/>, the unit
    /// along a quarter turn left of walking through) to each side as far as keeps the body's half width (<see cref="HumanHalfWidth"/>) and the room graph's doorway tolerance (<see cref="PushIntoCellMetres"/>, the runner's
    /// pass radius at a doorway vertex) off the opening's sides, at the narrowest of its widths at the body's feet, middle and head (<see cref="Fits"/>' heights: an arch narrows to its top). The Facility Hub's 10 m openings
    /// between the cells of one hall leave 4 m either way, a 2 m doorway none. Null for a portal that is no doorway (a floor crossing, a stair), opens outside, has a static by its crossing (<see cref="CrossingOffsets"/> other
    /// than the centre: that search placed it), or leaves no room to move.
    /// </summary>
    /// <param name="cell">The cell the walk leaves.</param>
    /// <param name="portal">The portal it crosses.</param>
    public (Vector3 Centre, Vector3 Along, float Low, float High)? CrossingSpan(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (portal.LeadsOutside || !IsDoorway(cell, portal) || CrossingOffsets(cell, portal) is not [0f] || DoorwayOf(cell, portal) is not { } doorway
            || _cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return null;
        }

        var along = AlongOf(doorway);
        var corners = polygon.Vertices.Select(v => cell.Frame.LocalToParent(v)).ToArray();
        var low = float.MinValue;
        var high = float.MaxValue;
        foreach (var height in (ReadOnlySpan<float>)[0.2f, 1f, HumanHeight - HeadSlack])
        {
            if (WidthAt(corners, along, doorway.Centre, doorway.Sill + height) is not { } width)
            {
                return null;
            }

            low = MathF.Max(low, width.Low);
            high = MathF.Min(high, width.High);
        }

        low += HumanHalfWidth + PushIntoCellMetres;
        high -= HumanHalfWidth + PushIntoCellMetres;
        return high > low ? (doorway.Centre, along, low, high) : null;
    }

    /// <summary>Where the polygon's sides cross the height <paramref name="z"/>, along <paramref name="along"/> from <paramref name="centre"/>: the outermost crossings either way, or null when the height misses it.</summary>
    private static (float Low, float High)? WidthAt(Vector3[] corners, Vector3 along, Vector3 centre, float z)
    {
        var low = float.MaxValue;
        var high = float.MinValue;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var (a, b) = (corners[j], corners[i]);
            if ((a.Z > z) == (b.Z > z))
            {
                continue;
            }

            var d = Vector3.Dot(a + ((b - a) * ((z - a.Z) / (b.Z - a.Z))) - centre, along);
            low = MathF.Min(low, d);
            high = MathF.Max(high, d);
        }

        return high > low ? (low, high) : null;
    }

    /// <summary>The hub walk (2026-11-18): the pair of a walk square through <paramref name="portal"/> at <paramref name="offset"/> metres along it from its centre (<see cref="CrossingOffsets"/>' sense), as the walk takes it (<see cref="SettledPairAt"/>).</summary>
    /// <param name="cell">The cell the walk leaves.</param>
    /// <param name="portal">The portal.</param>
    /// <param name="offset">Metres along the opening from its centre, a quarter turn left of walking through.</param>
    public (CellFrame? Approach, CellFrame Push) PairAtOffset(EnvCellData cell, CellPortalData portal, float offset)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return SettledPairAt(cell, portal, offset);
    }

    /// <summary>
    /// The pair of waypoints of a walk through <paramref name="portal"/>: the approach <see cref="ApproachMetres"/> before it in
    /// <paramref name="cell"/> (null when the room is too thin for it) and the push <see cref="PushIntoCellMetres"/> past it in the
    /// neighbour (the doorway's centre when neither lies inside the neighbour).
    /// </summary>
    /// <remarks>
    /// A portal walked as a floor crossing (it is no doorway by <see cref="Fits"/>; <see cref="FloorCrossingOf"/>) takes its pair from the shared
    /// edge's middle instead, square across the edge and dropped onto each cell's floor (<see cref="FloorPair"/>).
    /// </remarks>
    public (CellFrame? Approach, CellFrame Push) DoorwayPair(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (!IsDoorway(cell, portal) && FloorCrossingOf(cell, portal) is { } crossing)
        {
            return FloorPair(cell, _cells.GetCell(portal.OtherCellId)!, crossing);
        }

        if (!IsDoorway(cell, portal) && StairOf(cell, portal) is { } stair)
        {
            return StairPair(cell, stair);
        }

        return SettledPairAt(cell, portal, CrossingOffsets(cell, portal)[0]);
    }

    /// <summary>
    /// Every crossing of <paramref name="portal"/> a walk may take (<see cref="CrossingOffsets"/>): one pair, <see cref="DoorwayPair"/>'s, unless a static object stands in the middle of a wide
    /// opening; then the nearest clear crossing either side of it, so the room graph's search takes the side its walk lies on.
    /// </summary>
    public IReadOnlyList<(CellFrame? Approach, CellFrame Push)> DoorwayPairs(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (!IsDoorway(cell, portal))
        {
            return [DoorwayPair(cell, portal)];
        }

        return [.. CrossingOffsets(cell, portal).Select(offset => SettledPairAt(cell, portal, offset))];
    }

    private (CellFrame? Approach, CellFrame Push) PairAt(EnvCellData cell, CellPortalData portal, float offset) =>
        (InsideWaypoint(cell, portal, -ApproachMetres, offset), InsideWaypoint(cell, portal, PushIntoCellMetres, offset) ?? Waypoint(cell, portal, 0f, offset));

    /// <summary>
    /// <see cref="PairAt"/>'s pair as the walk takes it: its points moved where the foot stands (<see cref="Settled"/>), a move kept only when the crossing still walks (<see cref="PairWalks"/>): the move is blind to the
    /// line between the points, and a push moved round a statue's base sent the crossing over the base's edge (the world walk's rooms, W8: 0x7200014B to 0x72000149 and its 39 copies). The pair with both points moved,
    /// then with only the approach moved, then only the push, then the square pair; both moved when none walks. The crossing search (<see cref="CrossingOffsets"/>) reads the square pair.
    /// </summary>
    private (CellFrame? Approach, CellFrame Push) SettledPairAt(EnvCellData cell, CellPortalData portal, float offset) =>
        _pairs.GetOrAdd((cell.Id, portal.PolygonId, portal.OtherCellId, offset), _ =>
        {
            var square = PairAt(cell, portal, offset);
            var settled = Settled(square.Approach, square.Push);
            if (settled == square || World() is not { } world)
            {
                return settled;
            }

            lock (world)
            {
                foreach (var pair in (ReadOnlySpan<(CellFrame? Approach, CellFrame Push)>)[settled, (settled.Approach, square.Push), (square.Approach, settled.Push), square])
                {
                    if (PairWalks(world, cell, portal, offset, pair.Approach, pair.Push))
                    {
                        return pair;
                    }
                }
            }

            return settled;
        });

    /// <summary>The rings <see cref="Settled"/> searches round a doorway point the foot does not stand on, in metres. Not retail: Lifestone's own.</summary>
    private static readonly float[] SettleRingMetres = [0.25f, 0.5f];

    /// <summary>
    /// A doorway's pair with each point the foot does not stand on (the pre-check's ground probe) moved to the nearest point within <see cref="SettleRingMetres"/> in the same room where it does: the pair is
    /// drawn square through the doorway's polygon, blind to what stands behind it. The world walk's rooms (W8): a statue's base half a metre past a doorway (0x72000257, 0x7200014B), an approach in a wall's
    /// thickness where the room beyond the doorway is thin (0x011D010F); of the 2,220 walks the pre-check refused that the body walks, about 560 were refused at such a point. A point with no such place near
    /// is kept: left out, an approach sent the walk across the room to the push at a slant, over a wall's corner (0x003A0531, 0x00EA0236: 90 walks the world walk had walked clean). Unchanged without the dats.
    /// </summary>
    private (CellFrame? Approach, CellFrame Push) Settled(CellFrame? approach, CellFrame push)
    {
        if (World() is not { } world)
        {
            return (approach, push);
        }

        lock (world)
        {
            if (approach is { } a && !Stands(a, out _))
            {
                approach = StandingNear(a) ?? a;
            }

            return (approach, Stands(push, out _) ? push : StandingNear(push) ?? push);
        }

        bool Stands(CellFrame at, out float z) => world.TryFixedGroundZ(at, StairFoot, out z, out _) && MathF.Abs(z - at.Origin.Z) <= StepUpMetres;

        CellFrame? StandingNear(CellFrame at)
        {
            if (_cells.GetCell(at.CellId) is not { } room)
            {
                return null;
            }

            foreach (var ring in SettleRingMetres)
            {
                for (var i = 0; i < StandingDirections; i++)
                {
                    var angle = i * (2f * MathF.PI / StandingDirections);
                    var point = at.Origin + new Vector3(MathF.Cos(angle) * ring, MathF.Sin(angle) * ring, 0f);
                    var moved = new CellFrame(at.CellId, new AcFrame(point, Quaternion.Identity));
                    if (_cells.Contains(room, point + new Vector3(0f, 0f, 1f)) && Stands(moved, out var z))
                    {
                        return new CellFrame(at.CellId, new AcFrame(point with { Z = z }, Quaternion.Identity));
                    }
                }
            }

            return null;
        }
    }

    /// <summary>How far apart along a wide opening <see cref="CrossingOffsets"/> tries a crossing, in metres. Not retail: Lifestone's own.</summary>
    public const float CrossingStepMetres = 0.25f;

    /// <summary>
    /// How far along the portal (metres from its centre, in the direction a quarter turn left of walking through) a walk crosses it (M6 7 G, the walk out of Holtburg's scriveners' hall, gate A's rehearsal): a
    /// doorway is walked square through its polygon's centre, and an opening of two rooms can be many metres wide with something standing in its middle. Holtburg's 0xA9B30100 and 0xA9B30103 are one hall under
    /// a portal 9.6 m wide, and the pillar 0x0200020E stands at (182.08, 83.9), 0.1 m off the polygon's centre, so the pair through the centre has no floor for the foot: the walker's pre-check
    /// (<see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer"/>) refused every walk out of the hall's far side, though the room is open a metre either side of the pillar.
    /// {0} when the portal has nothing of the kind: no static object of either cell stands near the crossing (no physics is built), or the centre stands (<see cref="CrossingStands"/>), or the opening is too narrow
    /// to move in, or there are no dats. Else the nearest clear crossing either side of the centre, one <see cref="CrossingStepMetres"/> further when that stands too (the body is not a line): one or two
    /// offsets. The square pairs are tried first and, when none of them stands, the pairs with their points moved where the foot stands (<see cref="Settled"/>); {0} again when no crossing stands either way, for the
    /// runner's physics to name. Computed once per portal. Not retail: the client has no planner; the test is retail's own ground probe.
    /// </summary>
    public IReadOnlyList<float> CrossingOffsets(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other
            ? [0f]
            : _offsets.GetOrAdd((cell.Id, portal.PolygonId, other.Id), _ => FindCrossings(cell, portal, other));
    }

    private float[] FindCrossings(EnvCellData cell, CellPortalData portal, EnvCellData other)
    {
        if (DoorwayOf(cell, portal) is not { } doorway || _cells.Structures.Dats is null || _cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return [0f];
        }

        var along = AlongOf(doorway);
        var corners = polygon.Vertices.Select(v => Vector3.Dot(cell.Frame.LocalToParent(v) - doorway.Centre, along)).ToArray();
        var (low, high) = (corners.Min() + HumanHalfWidth, corners.Max() - HumanHalfWidth);
        if (high - low < CrossingStepMetres || !StaticNear(cell, other, doorway))
        {
            return [0f];
        }

        if (World() is not { } world)
        {
            return [0f];
        }

        lock (world)
        {
            // The square pairs first; when no crossing of them stands, the pairs as the walk takes them, their points moved where the foot stands (Settled): a statue's base just past a narrow
            // doorway leaves no square crossing, and one moved a quarter metre does (the world walk's rooms, W8: 0x72000149 to 0x7200014B and its 39 copies).
            foreach (var settled in (ReadOnlySpan<bool>)[false, true])
            {
                if (CrossingStands(world, cell, portal, 0f, settled))
                {
                    return [0f];
                }

                var found = new List<float>();
                foreach (var (limit, side) in (ReadOnlySpan<(float, float)>)[(high, 1f), (-low, -1f)])
                {
                    for (var offset = CrossingStepMetres; offset <= limit; offset += CrossingStepMetres)
                    {
                        if (CrossingStands(world, cell, portal, side * offset, settled))
                        {
                            var wider = offset + CrossingStepMetres;
                            found.Add(side * (wider <= limit && CrossingStands(world, cell, portal, side * wider, settled) ? wider : offset));
                            break;
                        }
                    }
                }

                if (found.Count > 0)
                {
                    return [.. found.OrderBy(MathF.Abs)];
                }
            }

            return [0f];
        }
    }

    /// <summary>How far past a static object's radius <see cref="StaticNear"/> looks from a doorway's centre, in metres: the body's width and a margin. Not retail: Lifestone's own.</summary>
    private const float CrossingStaticReachMetres = HumanHalfWidth + 0.5f;

    /// <summary>True when a static object of either cell stands within its radius and <see cref="CrossingStaticReachMetres"/> of the doorway's centre, seen from above, at a standing human's height: the cheap test before the physics.</summary>
    private bool StaticNear(EnvCellData cell, EnvCellData other, Doorway doorway)
    {
        var centre = new Vector2(doorway.Centre.X, doorway.Centre.Y);
        foreach (var room in (ReadOnlySpan<EnvCellData>)[cell, other])
        {
            foreach (var body in StaticsOf(room))
            {
                if (body.InTheWayAt(doorway.Sill) && Vector2.Distance(body.Centre, centre) <= body.Radius + CrossingStaticReachMetres)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// True when the pre-check's ground probe passes the walk from the pair's approach (the doorway itself when the room is too thin for one) to its push at <paramref name="offset"/>: the samples of
    /// <see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer"/> (one every <see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer.SampleMetres"/>, each from the height of the last, in its nearer end's cell, the half sample
    /// and the drop when it fails), so a low step a walker takes (the Town Network's stone rings at its doors) is no refusal here either.
    /// </summary>
    private bool CrossingStands(PhysicsWorld world, EnvCellData cell, CellPortalData portal, float offset, bool settled)
    {
        var (approach, push) = settled ? SettledPairAt(cell, portal, offset) : PairAt(cell, portal, offset);
        return PairWalks(world, cell, portal, offset, approach, push);
    }

    /// <summary><see cref="CrossingStands"/> for a given pair at <paramref name="offset"/>: the walk from its approach (the doorway itself when there is none) to its push passes the pre-check's probe (<see cref="ProbeWalks"/>).</summary>
    private bool PairWalks(PhysicsWorld world, EnvCellData cell, CellPortalData portal, float offset, CellFrame? approach, CellFrame push) =>
        ProbeWalks(world, approach ?? new CellFrame(cell.Id, new AcFrame(Waypoint(cell, portal, 0f, offset).Origin, Quaternion.Identity)), push, Body(world));

    /// <summary>
    /// The human body the planner places (<see cref="FootStands"/>, <see cref="ProbeWalks"/>), in <paramref name="world"/>; null without the dats. The caller holds the world's lock.
    /// </summary>
    private PhysicsObj? Body(PhysicsWorld world)
    {
        if (_cells.Structures.Dats is not { } dats)
        {
            return null;
        }

        _human ??= PhysicsSetup.Load(dats, HumanSetupId, new PhysicsGeometryLibrary(dats));
        return _human is null ? null : _standProbe ??= new PhysicsObj(world, _human, id: 0, new Wayfinding.RouteConfirmer.ProbeWeenie()) { FixedWorldOnly = true };
    }

    /// <summary>
    /// True when the pre-check's ground probe passes the straight walk from <paramref name="from"/> to <paramref name="push"/> as <see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer"/> samples it: the start
    /// stands, then a sample every <see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer.SampleMetres"/>, each from the height of the last, in its nearer end's cell, with the half sample and the drop when one
    /// fails. With <paramref name="body"/>, the body stands at every sample too, or steps there from where it last stood (the pre-check's placement probe:
    /// <see cref="Lifestone.Client.Movement.Wayfinding.RouteConfirmer.StandsOrSteps"/>): a straight step into a table's edge or a doorway's jamb, where the foot finds ground and the body does not
    /// fit, is refused here as the pre-check refuses it (the world walk's rooms, W8: 227 walks refused "no room to stand" that the body walks). The caller holds the world's lock.
    /// </summary>
    /// <param name="world">The fixed world (the caller holds its lock).</param>
    /// <param name="from">The walk's start.</param>
    /// <param name="push">Its end.</param>
    /// <param name="body">The body that stands at every sample too, or null for the foot alone.</param>
    /// <param name="reachEnd">
    /// True: the ground the samples carried to <paramref name="push"/> must also lie within the pre-check's rise (<see cref="PhysicsWorld.GroundProbeRise"/>) under <paramref name="push"/>'s own height, as the pre-check's
    /// next sample, taken from that carried height, needs it. A room detour's last step from a floor corner beside a staircase to a doorway's approach on its top (Uziz, 0xA2600100: the corner 2.8 m under the approach
    /// at 20.0) sampled the floor beside the stairs all the way, so the probe passed a step the pre-check then refused at the doorway (the merge of the world walk's room planner with M6 14's doorway steps).
    /// </param>
    private static bool ProbeWalks(PhysicsWorld world, CellFrame from, CellFrame push, PhysicsObj? body = null, bool reachEnd = false)
    {
        try
        {
            return GroundAndBody(world, from, push, body, reachEnd);
        }
        finally
        {
            if (body is { Cell: not null })
            {
                body.LeaveWorld();
            }
        }
    }

    private static bool GroundAndBody(PhysicsWorld world, CellFrame from, CellFrame push, PhysicsObj? body, bool reachEnd)
    {
        var stood = Lifestone.Client.Movement.Wayfinding.RouteConfirmer.Lifted(from);
        var length = Vector3.Distance(from.Origin, push.Origin);
        var z = from.Origin.Z;
        var along = 0f;
        if (!Foot(0f, z, ref z))
        {
            return false; // the walk starts here: the approach stands too (M6 7 Q: the Cave of Alabree's rock 0x020000A5 sits on 0x016201CF's approach, the samples past it clear)
        }

        while (along < length - 1e-4f)
        {
            var step = MathF.Min(Lifestone.Client.Movement.Wayfinding.RouteConfirmer.SampleMetres, length - along);
            if (!Foot(along + step, z, ref z)
                && !(step > 0.5f && Foot(along + (step * 0.5f), z, ref z) && Foot(along + step, z, ref z))
                && !Foot(along + step, z - PhysicsWorld.GroundProbeRise - PhysicsWorld.GroundProbeDrop, ref z)
                && !LineHeight(along + step, ref z))
            {
                return false;
            }

            along += step;
            if (body is not null && !Lifestone.Client.Movement.Wayfinding.RouteConfirmer.StandsOrSteps(body, At(along, z), ref stood))
            {
                return false;
            }
        }

        return !reachEnd || push.Origin.Z - z <= PhysicsWorld.GroundProbeRise;

        CellFrame At(float at, float height)
        {
            var t = length > 0f ? at / length : 1f;
            return new CellFrame(t < 0.5f ? from.CellId : push.CellId, new AcFrame(Vector3.Lerp(from.Origin, push.Origin, t) with { Z = height }, Quaternion.Identity));
        }

        // The pre-check's line-height sample (RouteConfirmer.TryLineHeight): climbed onto something beside the step, the sample is probed again from the step's own line.
        bool LineHeight(float at, ref float ground)
        {
            var line = Lifestone.Client.Movement.Wayfinding.RouteConfirmer.LineZ(from.Origin.Z, push.Origin.Z, length, at);
            var kept = ground;
            return ground - line > Lifestone.Client.Movement.Wayfinding.RouteConfirmer.LineClimbMetres && Foot(at, line, ref ground)
                && (ground >= kept - Lifestone.Client.Movement.Wayfinding.RouteConfirmer.MaxDropMetres || Reset(ref ground, kept));
        }

        static bool Reset(ref float ground, float kept)
        {
            ground = kept;
            return false;
        }

        bool Foot(float at, float height, ref float ground)
        {
            var t = length > 0f ? at / length : 1f;
            var sample = Vector3.Lerp(from.Origin, push.Origin, t) with { Z = height };
            var frame = new CellFrame(t < 0.5f ? from.CellId : push.CellId, new AcFrame(sample, Quaternion.Identity));
            if (!world.TryFixedGroundZ(frame, StairFoot, out var found, out _))
            {
                return false;
            }

            ground = found;
            return true;
        }
    }

    /// <summary>The unit horizontal direction a quarter turn left of walking through <paramref name="doorway"/>.</summary>
    private static Vector3 AlongOf(in Doorway doorway) => new(-doorway.Outward.Y, doorway.Outward.X, 0f);

    /// <summary>How far apart the samples of a stair's climb lie (<see cref="StairOf"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float StairSampleMetres = 0.25f;

    /// <summary>The longest climb or descent <see cref="StairOf"/> follows from a hole's plane, in metres. Not retail: Lifestone's own.</summary>
    public const float StairMaxRunMetres = 10f;

    /// <summary>How far apart the points of a stair's climb lie (<see cref="Stair.Climb"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float StairWaypointMetres = 0.5f;

    /// <summary>How many directions <see cref="StairOf"/> tries from a stair's top (evenly round the compass). Not retail: Lifestone's own.</summary>
    public const int StairDirections = 16;

    /// <summary>The height band within which <see cref="StairOf"/>'s tops count as equally near the hole's plane, then ordered by their distance from the hole's middle, in metres. Not retail: Lifestone's own (two stair samples' worth of floor tolerance, 0.05 m).</summary>
    public const float StairTopBandMetres = 0.05f;

    /// <summary>How many of the points nearest a hole's plane <see cref="StairOf"/> tries as a stair's top. Not retail: Lifestone's own.</summary>
    public const int StairTops = 6;

    /// <summary>The foot sphere of the stair's ground probes: the human Setup's lowest sphere (radius <see cref="HumanHalfWidth"/>, resting on the ground).</summary>
    private static readonly PhysSphere StairFoot = new(new Vector3(0f, 0f, HumanHalfWidth), HumanHalfWidth);

    /// <summary>
    /// The stair of <paramref name="portal"/> of <paramref name="cell"/>, or null: a level portal (a hole in a ceiling, or in a floor) that a stair or ramp standing under it
    /// climbs through (M6 4 W6b, <c>m6-holtburg-upper-floor</c>). Holtburg's 0xA9B30103 (the ground floor, 116.0) opens through its ceiling (118.8) into the slab
    /// 0xA9B3010F and on into 0xA9B3010A (the upper floor, 119.0); the stair is the static object Setup 0x020001B4 at (176.2, 85.35, 116.0), whose physics is a ramp
    /// rising about 1.03 m a metre (normal Z about 0.70, walkable over <see cref="CellStructure.FloorZ"/>) from y 83.8 to 86.7. Neither cell has a floor polygon on it, so
    /// the rule asks the physics: the landblock's fixed world (cells, buildings, statics; <see cref="PhysicsWorld.TryFixedGroundZ"/>, retail's own step down,
    /// <c>CTransition::step_down</c>, 11.6096 0x0050BD70, a window of the step-up either way) is probed over the hole for a walkable top within the step-up of its plane,
    /// then followed from there every <see cref="StairSampleMetres"/>, each sample within the step-up of the last (<c>CTransition::step_up</c>, 11.6096 0x0050C0E0), down
    /// to a floor of the cell below (the foot) and up to where a body stands over the plane in the cell above (the landing), each walk staying in the stack of cells
    /// the hole joins. Only a hole with a static object standing under it (its origin inside the hole seen from above, in the cell below or the cell under that) is
    /// probed: a stair is a static object. Needs the dats (null for the synthetic rooms of the unit tests). Computed once per portal.
    /// </summary>
    public Stair? StairOf(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other)
        {
            return null;
        }

        return _stairs.GetOrAdd((cell.Id, portal.PolygonId, other.Id), _ => FindStair(cell, portal, other));
    }

    private Stair? FindStair(EnvCellData cell, CellPortalData portal, EnvCellData other)
    {
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon || _cells.Structures.Dats is null)
        {
            return null;
        }

        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        var into = portal.PortalSide ? normal : -normal;
        if (MathF.Abs(into.Z) < 0.99f)
        {
            return null; // not a level portal
        }

        var corners = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
        var plane = corners.Average(c => c.Z);
        var (lower, upper) = into.Z > 0f ? (cell, other) : (other, cell);
        var below = new HashSet<uint> { lower.Id };
        var above = new HashSet<uint> { upper.Id };
        var centre = corners.Aggregate(Vector3.Zero, (sum, c) => sum + c) / corners.Length;
        foreach (var (under, _) in FloorPortalsUnder(lower, centre with { Z = plane }))
        {
            below.Add(under.Id);
        }

        foreach (var portalOver in upper.Portals)
        {
            if (!portalOver.LeadsOutside && _cells.GetCell(portalOver.OtherCellId) is { } over && over.Id != lower.Id
                && FloorPortalsUnder(over, centre with { Z = plane + HumanHeight }).Any(p => p.Other.Id == upper.Id))
            {
                above.Add(over.Id);
            }
        }

        var ramp = RampUnder(below, corners);
        if (!below.Any(id => _cells.GetCell(id)!.StaticObjects.Any(o => InPolygon2D(corners, o.Frame.Origin.X, o.Frame.Origin.Y))) && ramp is null)
        {
            return null;
        }

        if (_cells.GetStructure(lower).Floors.Count == 0)
        {
            // A slab with no floor of its own between two holes (Holtburg's 0xA9B3010F): its stair is the one through the hole under it, when that stair's landing
            // lies in the cell above this hole.
            foreach (var under in below.Where(id => id != lower.Id).Select(id => _cells.GetCell(id)!))
            {
                foreach (var up in under.Portals.Where(p => p.OtherCellId == lower.Id))
                {
                    if (StairOf(under, up) is { } stair && stair.Up && above.Contains(stair.PastLanding.CellId))
                    {
                        return stair with { Up = into.Z > 0f };
                    }
                }
            }

            return null;
        }

        var world = World();
        if (world is null)
        {
            return null;
        }

        lock (world)
        {
            return ClimbHole(world, corners, plane, lower, below, above, ramp) is { } stair ? stair with { Up = into.Z > 0f } : null;
        }
    }

    /// <summary>
    /// The largest normal Z of a ramp's floor polygon (<see cref="RampUnder"/>): under it the polygon slopes; at it or over it the floor is level (the level-portal test's
    /// 0.99 of <see cref="FindStair"/>). Not retail: Lifestone's own.
    /// </summary>
    public const float RampMaxNormalZ = 0.99f;

    /// <summary>
    /// The downhill direction (seen from above) of a sloping walkable floor polygon of a cell of <paramref name="below"/>, or null when there is none (its normal Z at least <see cref="CellStructure.FloorZ"/>, retail's walkable floor, and
    /// under <see cref="RampMaxNormalZ"/>) that reaches under the hole <paramref name="corners"/> seen from above (a corner of either inside the other), the first found: a ramp that is the cell's own
    /// floor, not a static object (M6 5 w, <c>m6-alabree-entry-walk</c>). The dungeon-entrance cave of environment 0x0D0003F2 (the Cave of Alabree's 0xA7B30100 to 0xA7B30104)
    /// descends from its mouth's floor 0xA7B30103 (z 30.12) through a hole into the slab 0xA7B30102 and on through a second hole onto the ramp floor of 0xA7B30101 (normal Z
    /// 0.79, z 30.12 down to 24.80 over 6.9 m), which ends at the doorway of the portal room 0xA7B30100. The physics walks it as it walks a static stair (retail's step down,
    /// <c>CTransition::step_down</c>, 11.6096 0x0050BD70), so <see cref="ClimbHole"/> probes it the same way.
    /// </summary>
    private Vector2? RampUnder(HashSet<uint> below, Vector3[] corners)
    {
        foreach (var id in below)
        {
            var cell = _cells.GetCell(id)!;
            foreach (var floor in _cells.GetStructure(cell).Floors)
            {
                var normalZ = cell.Frame.RotateToParent(floor.Plane.Normal).Z;
                if (normalZ < CellStructure.FloorZ || normalZ >= RampMaxNormalZ)
                {
                    continue;
                }

                var ramp = floor.Vertices.Select(cell.Frame.LocalToParent).ToArray();
                if (ramp.Any(v => InPolygon2D(corners, v.X, v.Y)) || corners.Any(c => InPolygon2D(ramp, c.X, c.Y)))
                {
                    var normal = cell.Frame.RotateToParent(floor.Plane.Normal);
                    return Vector2.Normalize(new Vector2(normal.X, normal.Y)); // a floor's normal leans toward its low side: downhill
                }
            }
        }

        return null;
    }

    private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

    /// <summary>The search of <see cref="FindStair"/> (the world locked): the tops over the hole, then from each the shortest descent and climb.</summary>
    private Stair? ClimbHole(PhysicsWorld world, Vector3[] corners, float plane, EnvCellData lower, HashSet<uint> below, HashSet<uint> above, Vector2? ramp)
    {
        var (min, max) = (corners.Aggregate(Vector3.Min), corners.Aggregate(Vector3.Max));
        var tops = new List<(Vector3 Point, uint Cell)>();
        for (var x = min.X + (StairSampleMetres * 0.5f); x < max.X; x += StairSampleMetres)
        {
            for (var y = min.Y + (StairSampleMetres * 0.5f); y < max.Y; y += StairSampleMetres)
            {
                if (InPolygon2D(corners, x, y) && Ground(world, new Vector3(x, y, plane), lower.Id) is { } top && MathF.Abs(top.Point.Z - plane) <= StepUpMetres * 0.5f)
                {
                    tops.Add(top);
                }
            }
        }

        Stair? best = null;
        // A ramp floor under the hole (M6 5 w): of tops equally near the plane (within StairTopBandMetres: the foot sphere resting on the hole's lip finds the lip's own height
        // all round it), the ones nearest the ramp's centre line (through the hole's middle, downhill) first, so the descent runs down the ramp's middle and not off the lip at
        // its side (the Cave of Alabree's ramp: the first tops in the grid's order lay 0.5 m from its south wall, and the body could not be placed there). A static stair keeps
        // the plane's order.
        var middle = new Vector2(corners.Average(c => c.X), corners.Average(c => c.Y));
        var ordered = ramp is { } downhill
            ? tops.OrderBy(t => MathF.Floor(MathF.Abs(t.Point.Z - plane) / StairTopBandMetres)).ThenBy(t => MathF.Abs(Cross(new Vector2(t.Point.X, t.Point.Y) - middle, downhill)))
            : tops.OrderBy(t => MathF.Abs(t.Point.Z - plane));
        foreach (var top in ordered.Take(StairTops))
        {
            var down = Follow(world, top, below.Concat(above).ToHashSet(), descend: true, (p, c) => below.Contains(c) && FootRestOn(_cells.GetCell(c)!, p + new Vector3(0f, 0f, 0.05f)) is { } f && p.Z <= f + 0.05f);
            var up = Follow(world, top, below.Concat(above).ToHashSet(), descend: false, (p, c) => above.Contains(c) && p.Z >= plane - 0.05f);
            if (down is { } foot && up is { } landing && (best is not { } known || foot.Run + landing.Run < known.Length))
            {
                var chain = foot.Samples.AsEnumerable().Reverse().Concat(landing.Samples.Skip(1)).ToList();
                var every = Math.Max(1, (int)MathF.Round(StairWaypointMetres / StairSampleMetres));
                var climb = chain.Where((_, i) => i % every == 0 || i == chain.Count - 1).Select(c => new CellFrame(c.Cell, new AcFrame(c.Point, Quaternion.Identity))).ToList();
                best = new Stair(foot.Point, foot.Beyond, landing.Point, landing.Beyond, foot.Run + landing.Run, Up: true, climb);
            }
        }

        return best;
    }

    /// <summary>
    /// From <paramref name="top"/> the shortest walk of samples, each within the step-up of the last and never rising (<paramref name="descend"/>) or never falling, in the
    /// cells <paramref name="stack"/> (null: from each cell into itself or a cell its portals join), to a sample <paramref name="done"/> accepts; with the point a further step on in the same direction (in an accepted
    /// cell, no step back down or up), or the sample itself when there is none. At most <paramref name="maxRun"/> metres.
    /// </summary>
    private (Vector3 Point, uint Cell, CellFrame Beyond, float Run, List<(Vector3 Point, uint Cell)> Samples)? Follow(PhysicsWorld world, (Vector3 Point, uint Cell) top, HashSet<uint>? stack, bool descend, Func<Vector3, uint, bool> done, float maxRun = StairMaxRunMetres)
    {
        (Vector3, uint, CellFrame, float Run, List<(Vector3 Point, uint Cell)>)? best = null;
        for (var k = 0; k < StairDirections; k++)
        {
            var angle = k * MathF.Tau / StairDirections;
            var step = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0f) * StairSampleMetres;
            var (at, cellId) = top;
            var samples = new List<(Vector3 Point, uint Cell)> { top };
            for (var run = 0f; run <= maxRun && (best is not { } known || run < known.Run); run += StairSampleMetres)
            {
                if (done(at, cellId))
                {
                    var beyondDistance = descend ? ApproachMetres : PushIntoCellMetres;
                    var beyond = new CellFrame(cellId, new AcFrame(at, Quaternion.Identity));
                    if (Ground(world, at + (step * (beyondDistance / StairSampleMetres)), cellId) is { } next && (descend ? next.Point.Z <= at.Z + 0.05f : next.Point.Z >= at.Z - 0.05f) && done(next.Point, next.Cell))
                    {
                        beyond = new CellFrame(next.Cell, new AcFrame(next.Point, Quaternion.Identity));
                    }

                    best = (at, cellId, beyond, run, samples);
                    break;
                }

                if (Ground(world, at + step, cellId) is not { } ahead || !(stack?.Contains(ahead.Cell) ?? Joined(cellId, ahead.Cell))
                    || (descend ? ahead.Point.Z > at.Z + 0.05f : ahead.Point.Z < at.Z - 0.05f))
                {
                    break;
                }

                (at, cellId) = ahead;
                samples.Add(ahead);
            }
        }

        return best;
    }

    /// <summary>True when <paramref name="to"/> is <paramref name="from"/> or a cell a portal of it joins (a step of <see cref="Follow"/> with no stack).</summary>
    private bool Joined(uint from, uint to) => from == to || _cells.GetCell(from)!.Portals.Any(p => !p.LeadsOutside && p.OtherCellId == to);

    /// <summary>
    /// The height a body standing on <paramref name="cell"/>'s own highest floor polygon under <paramref name="point"/> (and not above it) rests at, or null: the polygon's height
    /// under the point plus the lift of the foot sphere on its slope, <see cref="HumanHalfWidth"/> times (1 / normal Z - 1) (a sphere touching a plane rests that much over it
    /// at the point under its centre; 0 on a level floor). A static stair's foot is a level floor, where this is <see cref="FloorUnder(EnvCellData, Vector3)"/>'s own floor;
    /// the ramp of <see cref="RampUnder"/> is a floor whose slope lifts the sphere 0.13 m (normal Z 0.79), so its foot is found on the ramp (M6 5 w).
    /// </summary>
    private float? FootRestOn(EnvCellData cell, Vector3 point)
    {
        var local = cell.Frame.ParentToLocal(point);
        (float Z, float NormalZ)? best = null;
        foreach (var floor in _cells.GetStructure(cell).Floors)
        {
            if (MathF.Abs(floor.Plane.Normal.Z) < 1e-4f || !InPolygon2D(floor.Vertices, local.X, local.Y))
            {
                continue;
            }

            var z = -((floor.Plane.Normal.X * local.X) + (floor.Plane.Normal.Y * local.Y) + floor.Plane.D) / floor.Plane.Normal.Z;
            if (z <= local.Z && (best is null || z > best.Value.Z))
            {
                best = (z, MathF.Abs(cell.Frame.RotateToParent(floor.Plane.Normal).Z));
            }
        }

        return best is { } b ? cell.Frame.LocalToParent(new Vector3(local.X, local.Y, b.Z)).Z + (HumanHalfWidth * ((1f / b.NormalZ) - 1f)) : null;
    }

    /// <summary>The fixed world's walkable ground within the step-up of <paramref name="at"/> (its height as found, the cell a body standing on it is in), or null.</summary>
    private (Vector3 Point, uint Cell)? Ground(PhysicsWorld world, Vector3 at, uint hint)
    {
        var probe = _cells.FindCell(at + new Vector3(0f, 0f, 0.5f), hint)?.Id ?? hint;
        if (!world.TryFixedGroundZ(new CellFrame(probe, new AcFrame(at, Quaternion.Identity)), StairFoot, out var z, out _))
        {
            return null;
        }

        var point = at with { Z = z };
        return _cells.FindCell(point + new Vector3(0f, 0f, 0.5f), probe) is { } stands ? (point, stands.Id) : null;
    }

    /// <summary>
    /// The landblock's fixed world for the stair and crossing probes, built once from the dats (no scenery) for the planners of one structure library, as the world never changes and a graph is rebuilt for every cell a run leaves out
    /// (<see cref="Lifestone.Client.Movement.Wayfinding.INavCellAvoider"/>: the Town Network's 0x0007 costs about a second a build, on the game thread); null without the dats.
    /// </summary>
    private PhysicsWorld? World()
    {
        if (_cells.Structures.Dats is not { } dats)
        {
            return null;
        }

        var worlds = Worlds.GetOrCreateValue(_cells.Structures);
        lock (worlds)
        {
            if (!worlds.TryGetValue(_cells.Coord, out var world))
            {
                world = new PhysicsWorld();
                world.Attach(new LandblockCollisionBuilder(dats, new PhysicsGeometryLibrary(dats), _cells.Structures, includeScenery: false).Build(_cells.Coord));
                worlds[_cells.Coord] = world;
            }

            return world;
        }
    }

    /// <summary>
    /// The pair of waypoints of a walk through a stair's hole from <paramref name="cell"/>: up, the approach <see cref="ApproachMetres"/> before the foot in the cell
    /// below and the push <see cref="PushIntoCellMetres"/> past the landing; down, the landing's push point as the approach and the point beyond the foot as the push.
    /// A cell with no floor of its own (a slab between two holes, Holtburg's 0xA9B3010F) has no approach: the body is already past it.
    /// </summary>
    /// <summary>
    /// The points walked between a stair crossing's approach and push from <paramref name="cell"/> through <paramref name="portal"/> (<see cref="Stair.Climb"/>, reversed going
    /// down); empty for a doorway, a floor crossing, a cell with no floor of its own (the slab: its stair was walked from the cell below or above), or no stair.
    /// </summary>
    public IReadOnlyList<CellFrame> StairClimb(EnvCellData cell, CellPortalData portal)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (IsDoorway(cell, portal) || FloorCrossingOf(cell, portal) is not null || StairOf(cell, portal) is not { } stair || _cells.GetStructure(cell).Floors.Count == 0)
        {
            return [];
        }

        return stair.Up ? stair.Climb : [.. stair.Climb.Reverse()];
    }

    private (CellFrame? Approach, CellFrame Push) StairPair(EnvCellData cell, Stair stair)
    {
        var slab = _cells.GetStructure(cell).Floors.Count == 0;
        return stair.Up
            ? (slab ? null : stair.BeforeFoot, stair.PastLanding)
            : (slab ? null : stair.PastLanding, stair.BeforeFoot);
    }

    /// <summary>How far past a static object's radius a straight walk keeps (<see cref="InStatic"/>). Not retail: Lifestone's own.</summary>
    public const float StaticClearanceMetres = 0.3f;

    /// <summary>How far under the floor a static object's origin may lie and still stand in the way (<see cref="InStatic"/>). Not retail: Lifestone's own.</summary>
    public const float StaticBelowMetres = 0.5f;

    /// <summary>How far apart the samples of <see cref="StaysOnFloor"/> lie, in metres. Not retail: Lifestone's own.</summary>
    public const float FloorSampleMetres = 0.25f;

    /// <summary>How far from either end <see cref="StaysOnFloor"/> starts sampling (a doorway's point lies on the cells' boundary). Not retail: Lifestone's own.</summary>
    public const float FloorSampleEndMetres = 0.3f;

    /// <summary>
    /// True when the straight walk from <paramref name="from"/> to <paramref name="to"/> (landblock space) stays over a walkable floor of <paramref name="cell"/>:
    /// every <see cref="FloorSampleMetres"/> (but the first and last <see cref="FloorSampleEndMetres"/>) a floor lies under it within <see cref="StepUpMetres"/>
    /// of the floor found at the sample before. A room whose floor is not convex (a hole a ramp rises through, an L of a corridor, a ramp's corner) can hold two
    /// points whose straight line leaves its floor (M6 4 W4).
    /// </summary>
    public bool StaysOnFloor(EnvCellData cell, Vector3 from, Vector3 to)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var flat = new Vector2(to.X - from.X, to.Y - from.Y);
        var length = flat.Length();
        var z = from.Z;
        for (var s = FloorSampleEndMetres; s <= length - FloorSampleEndMetres; s += FloorSampleMetres)
        {
            var at = from + ((to - from) * (s / length));
            if (FloorUnder(cell, new Vector3(at.X, at.Y, z + StepUpMetres)) is not { } floor || floor < z - StepUpMetres || InStatic(cell, new Vector3(at.X, at.Y, floor)))
            {
                return false;
            }

            z = floor;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="point"/> (on a floor, landblock space) lies within a static object of <paramref name="cell"/>: within its Setup's radius (retail
    /// <c>CSetup::radius</c>, <see cref="ModelAssembly.Radius"/>; a GfxObj's physics sphere) plus <see cref="StaticClearanceMetres"/> of its origin, seen from above,
    /// the origin no more than <see cref="StaticBelowMetres"/> under the point nor above a standing human; or within a body's width of a cylinder or sphere of its Setup whose height meets a standing human's
    /// (<see cref="StaticBody"/>). The room graph's straight walks go round them (<see cref="FloorDetour"/>): Tusker Cave's 0x5B460185 has a Setup 0x020009A3 on the diagonal between two of its doorways,
    /// and the Cave of Alabree's pillars stand on its corridors' centre lines. Inside a disc of an object with physics BSP parts the disc is only a bound, and the point is in the static only where the foot finds no ground
    /// within the step-up (<see cref="FootStands"/>; with the dats). Not retail: the planner's own.
    /// </summary>
    public bool InStatic(EnvCellData cell, Vector3 point)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var probed = false;
        foreach (var body in StaticsOf(cell))
        {
            if (!body.InTheWayAt(point.Z))
            {
                continue;
            }

            var reach = Vector2.Distance(body.Centre, new Vector2(point.X, point.Y));
            if (!body.Probed && reach < body.Radius + StaticClearanceMetres)
            {
                return true;
            }

            probed |= body.Probed && reach < body.Radius + StaticClearanceMetres + ProbedReachMetres;
        }

        return probed && !FootStands(cell, point);
    }

    /// <summary>
    /// How far past the disc of an object with physics BSP parts <see cref="InStatic"/> asks the body (<see cref="FootStands"/>), in metres: the body's half-width, since the disc bounds the solid's centre-line reach, not the body's
    /// contact with it. Not retail: Lifestone's own. The world walk's rooms: Holtburg's 0xA9B3010A, a table 2 m long (Setup 0x02000272) whose foot-probe edge lay past its disc, so a straight walk across the landing grazed it unprobed.
    /// </summary>
    public const float ProbedReachMetres = HumanHalfWidth;

    /// <summary>How finely <see cref="FootStands"/> keeps its answers, in metres. Not retail: Lifestone's own.</summary>
    public const float FootStandsGridMetres = 0.05f;

    /// <summary>How far the placement of <see cref="FootStands"/> may move the body sideways and still count as standing at the point, in metres. Not retail: Lifestone's own.</summary>
    public const float BodyStandsSlackMetres = 0.1f;

    /// <summary>The human Setup (<c>OfflineWalker.HumanSetupId</c>) whose placement <see cref="FootStands"/> tests.</summary>
    private const uint HumanSetupId = 0x02000001;

    /// <summary>
    /// True when the body stands at <paramref name="point"/>, which judges a point inside the disc of an object with physics BSP parts (<see cref="StaticBody.Probed"/>): the foot finds ground within the step-up
    /// of its height and the human body is placed there (<c>CPhysicsObj::SetPosition</c> with placement, as the pre-check's placement probe) moving no more than <see cref="BodyStandsSlackMetres"/>: under a
    /// table or an arch the foot fits and the body does not (0x01060182: the world walk's rooms, the pre-check refused "no room to stand"). False without the dats, so the disc stands as before.
    /// </summary>
    private bool FootStands(EnvCellData cell, Vector3 point)
    {
        if (World() is not { } world || _cells.Structures.Dats is not { } dats)
        {
            return false;
        }

        var key = (cell.Id, (int)MathF.Round(point.X / FootStandsGridMetres), (int)MathF.Round(point.Y / FootStandsGridMetres), (int)MathF.Round(point.Z / FootStandsGridMetres));
        return _footStands.GetOrAdd(key, k =>
        {
            lock (world)
            {
                if (!world.TryFixedGroundZ(new CellFrame(cell.Id, new AcFrame(point, Quaternion.Identity)), StairFoot, out var z, out _) || MathF.Abs(z - point.Z) > StepUpMetres)
                {
                    return false;
                }

                if (Body(world) is not { } body)
                {
                    return true;
                }

                var at = new CellFrame(cell.Id, new AcFrame(point with { Z = z + Wayfinding.RouteConfirmer.PlacementLiftMetres }, Quaternion.Identity));
                var placed = body.SetPosition(at, SetPositionFlags.Placement) == SetPositionError.OK && body.Cell is not null;
                var moved = placed ? at.GetOffset(body.Position) : Vector3.Zero;
                if (body.Cell is not null)
                {
                    body.LeaveWorld();
                }

                return placed && new Vector2(moved.X, moved.Y).Length() <= BodyStandsSlackMetres;
            }
        });
    }

    /// <summary>
    /// A static object as the room graph's walks see it (M6 7 Q): a disc seen from above and the height it stands in the way at. <see cref="Z"/> is a Setup's or GfxObj's origin, read by the origin rule; <see cref="Bottom"/> and
    /// <see cref="Top"/> are a collision cylinder's or sphere's real height, NaN on an origin's disc. An object's origin need not be its foot: the Cave of Alabree's pillars (Setups 0x02000205, 0x02000208, 0x02000209: a CylSphere from 1.87 m under the
    /// origin, 3.75 m tall) stand floor to ceiling with the origin at their middle, 13.88 over the floor's 12.00, 0.08 m above a standing human, so the origin rule left them out: the plan ran down the corridor's centre line into one, which the
    /// body cannot pass (a walker stops at it) and the pre-check rightly refused ("no walkable ground", the live round's Blood Shreth walks at (50.00, -12.72) and (60.00, -39.16)). A shape's disc is the body's own contact: its
    /// radius and <see cref="FloorCornerClearanceMetres"/>, from which <see cref="InStatic"/>'s clearance is taken back.
    /// </summary>
    /// <param name="Centre">The disc's centre, landblock space.</param>
    /// <param name="Radius">The disc's radius (<see cref="InStatic"/> adds <see cref="StaticClearanceMetres"/>).</param>
    /// <param name="Z">The origin's height, or NaN.</param>
    /// <param name="Bottom">The lowest height of the collision shape, or NaN.</param>
    /// <param name="Top">The highest height of the collision shape, or NaN.</param>
    /// <param name="Exact">True for the shape of an object of shapes only, which is its whole solid (<see cref="OutOfStatics"/> steps out of these alone).</param>
    /// <param name="Probed">
    /// True for a disc of an object with physics BSP parts: retail tests those parts alone (<c>PhysStaticObject.FindObjCollisions</c>), so the disc only bounds the solid, and inside it the foot's ground probe
    /// judges (<see cref="FootStands"/>). The world walk's rooms: a staircase 0x010000D6 rising across a 10 m corridor (env 0x0D0000D8, repeated over 120 landblocks) has a physics sphere covering the whole
    /// room, so no detour stood clear of its disc and the walk ran straight into the stairs, past a lane 1.5 m wide beside them.
    /// </param>
    internal readonly record struct StaticBody(Vector2 Centre, float Radius, float Z, float Bottom = float.NaN, float Top = float.NaN, bool Exact = false, bool Probed = false)
    {
        /// <summary>True when a human standing on a floor at <paramref name="floor"/> meets this body's height (a comparison with NaN is false, so each disc answers by the rule it has).</summary>
        public bool InTheWayAt(float floor) =>
            (Z >= floor - StaticBelowMetres && Z <= floor + HumanHeight) || (Top > floor + StepUpMetres && Bottom < floor + HumanHeight);

        /// <summary>A collision shape from <paramref name="low"/> up <paramref name="height"/> of radius <paramref name="radius"/>, in the object's frame <paramref name="frame"/>, as a disc.</summary>
        public static StaticBody Shape(Placement frame, Vector3 low, float height, float radius, bool exact, bool probed)
        {
            var at = frame.LocalToParent(low);
            return new StaticBody(new Vector2(at.X, at.Y), radius + FloorCornerClearanceMetres - StaticClearanceMetres, float.NaN, at.Z, at.Z + height, exact, probed);
        }
    }

    /// <summary>
    /// The static objects a walk in the cell can meet, as discs (<see cref="StaticBody"/>): the cell's own and those of the cells through its portals whose discs reach a portal (<see cref="ReachesPortal"/>).
    /// Retail meets an object in every cell its shadow is in (<c>CPhysicsObj::add_shadows_to_cells</c>, 11.6096 0x005155E0), not only the cell that holds it: the world walk's rooms, 0x001501CC's
    /// Setup 0x02000274 (a solid 2 m long and 1 m tall) stands across its doorway to 0x001501CD 0.1 m past the portal, and the planner, reading 0x001501CD's own objects, did not see it (0x00CB0151 to 0x00CB015A, 0x8A030135 to 0x8A030137: <c>RoomStaticShapesDatTests</c>). Empty without the dats (the synthetic cells of the unit tests).
    /// </summary>
    private StaticBody[] StaticsOf(EnvCellData cell) => _reaching.GetOrAdd(cell.Id, static (_, state) => state.Self.GatherStatics(state.Cell), (Self: this, Cell: cell)); // no closure a call: the runner asks it every step

    private StaticBody[] OwnStatics(EnvCellData cell) =>
        cell.StaticObjects.Count == 0 ? [] : _statics.GetOrAdd(cell.Id, _ => ReadStatics(cell));

    private StaticBody[] GatherStatics(EnvCellData cell)
    {
        if (_cells.Structures.Dats is null)
        {
            return [];
        }

        var bodies = new List<StaticBody>(OwnStatics(cell));
        var structure = _cells.GetStructure(cell);
        foreach (var portal in cell.Portals)
        {
            if (portal.LeadsOutside || _cells.GetCell(portal.OtherCellId) is not { } other || other.StaticObjects.Count == 0
                || structure.GetPortal(portal.PolygonId) is not { } polygon)
            {
                continue;
            }

            var corners = new Vector2[polygon.Vertices.Length];
            for (var i = 0; i < corners.Length; i++)
            {
                var corner = cell.Frame.LocalToParent(polygon.Vertices[i]);
                corners[i] = new Vector2(corner.X, corner.Y);
            }

            foreach (var body in OwnStatics(other))
            {
                if (ReachesPortal(body, corners) && !bodies.Contains(body))
                {
                    bodies.Add(body);
                }
            }
        }

        return [.. bodies];
    }

    /// <summary>True when <paramref name="body"/>'s disc and <see cref="StaticClearanceMetres"/> come within reach of a portal's polygon seen from above (<paramref name="corners"/>, landblock space): an edge, or the polygon round its centre.</summary>
    private static bool ReachesPortal(StaticBody body, Vector2[] corners)
    {
        var reach = body.Radius + StaticClearanceMetres;
        var inside = false;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var (a, b) = (corners[j], corners[i]);
            var ab = b - a;
            var t = ab.LengthSquared() < 1e-8f ? 0f : Math.Clamp(Vector2.Dot(body.Centre - a, ab) / ab.LengthSquared(), 0f, 1f);
            if (Vector2.Distance(body.Centre, a + (ab * t)) <= reach)
            {
                return true;
            }

            if ((a.Y > body.Centre.Y) != (b.Y > body.Centre.Y) && body.Centre.X < a.X + ((body.Centre.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private StaticBody[] ReadStatics(EnvCellData cell)
    {
        if (_cells.Structures.Dats is not { } dats)
        {
            return [];
        }

        var discs = new List<StaticBody>();
        foreach (var o in cell.StaticObjects)
        {
            var model = o.IsSetup && dats.TryGetPortal<DatReaderWriter.DBObjs.Setup>(o.ModelId, out var setup) ? ModelAssembly.FromSetup(o.ModelId, setup) : null;
            var mesh = o.IsSetup ? null : GfxObjMeshBuilder.Load(dats, o.ModelId);
            var radius = o.IsSetup ? model?.Radius ?? 0f : mesh?.PhysicsSphere?.Radius ?? 0f;

            // Retail tests an object's physics BSP parts when it has any, else its cylinders, else its spheres (PhysStaticObject.FindObjCollisions). An object of shapes only
            // has no other solid, so it is read by those shapes alone and not by its Setup's radius round the origin: the pillar 0x02000248 (a cylinder 3 m wide in radius,
            // 10 m tall) has a Setup radius of over 5 m, which closed the whole 10 m room 0x00AF017A round it (the world walk's rooms).
            // Low clutter keeps the radius: a shape under the step-up does not stand in a human's way by the shape rule, but the foot cannot cross it either (0x02000175, a ball
            // 0.15 m across whose top is 0.31 m up, 0.5 m off a diagonal in a layout repeated over 83 landblocks).
            var parts = o.IsSetup ? model is not null && HasPhysicsParts(dats, model) : GfxObjHasPhysics(dats, o.ModelId);
            var shapesOnly = model is not null && (model.CylSpheres.Count > 0 || model.Spheres.Count > 0) && !parts
                && (model.CylSpheres.Any(c => c.LowPoint.Z + c.Height > StepUpMetres) || model.Spheres.Any(s => s.Center.Z + s.Radius > StepUpMetres));
            if (radius > 0f && !shapesOnly)
            {
                // A lone GfxObj with physics stands in the way by its solid's real height, not its origin's (the world walk, W8: 0x01004DAE in 0x59660204, its origin 2.3 m over the floor
                // and its solid reaching under it: the origin rule called it overhead, and walks across the room ran into it).
                var (bottom, top) = parts && mesh is not null ? Height(o.Frame, mesh.VertexBounds) : (float.NaN, float.NaN);
                discs.Add(new StaticBody(new Vector2(o.Frame.Origin.X, o.Frame.Origin.Y), radius, o.Frame.Origin.Z, bottom, top, Probed: parts));
            }
            else if (radius <= 0f && parts && model is not null && PartsReach(dats, model, o.Frame) is { } reach)
            {
                // A Setup with no radius of its own is solid only by its parts' physics BSP (the world walk, W8: a bed 0x02001051 3 m long across the doorway of 0x0043010C, in a
                // layout repeated over many dungeons): no disc at all let walks run straight across it. It stands in the way by its parts' real reach, and its foot probe judges inside.
                discs.Add(reach);
            }

            if (model is not null)
            {
                var frame = o.Frame;
                if (!shapesOnly || model.CylSpheres.Count > 0)
                {
                    discs.AddRange(model.CylSpheres.Select(c => StaticBody.Shape(frame, c.LowPoint, c.Height, c.Radius, shapesOnly, parts)));
                }

                if (!shapesOnly || model.CylSpheres.Count == 0)
                {
                    discs.AddRange(model.Spheres.Select(s => StaticBody.Shape(frame, s.Center - new Vector3(0f, 0f, s.Radius), 2f * s.Radius, s.Radius, shapesOnly, parts)));
                }
            }
        }

        return [.. discs];
    }

    /// <summary>
    /// The farthest, in metres, from the centre of the box of a Setup with no radius of its own to its corner, seen from above, that <see cref="PartsReach"/> reads as an object in the way: furniture and a
    /// staircase in a room (0x02000396, 5.7 m by 2 m: 3 m), not the Facility Hub's ramp (0x02000623, 10 m by 16 m: 9.3 m). Not retail: Lifestone's own.
    /// </summary>
    public const float PartsReachMostMetres = 4f;

    /// <summary>
    /// The disc round the parts with physics BSP of a Setup placed at <paramref name="frame"/>, in their rest pose: the box of their vertex bounds seen from above, its half-diagonal the radius, and its real
    /// height; null when no part has a mesh, the Setup has no rest pose, or the box reaches past <see cref="PartsReachMostMetres"/> (a ramp or a floor the body walks on).
    /// </summary>
    private static StaticBody? PartsReach(DatSet dats, ModelAssembly model, Placement frame)
    {
        Lifestone.Data.Models.PartPose[] pose;
        try
        {
            pose = model.ComputeRestPose();
        }
        catch (InvalidDataException)
        {
            return null;
        }

        var (min, max) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
        foreach (var part in model.Parts)
        {
            if (!part.IsGfxObj || part.Index >= pose.Length || !GfxObjHasPhysics(dats, part.GfxObjId) || GfxObjMeshBuilder.Load(dats, part.GfxObjId) is not { } mesh)
            {
                continue;
            }

            var bounds = mesh.VertexBounds;
            for (var k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) == 0 ? bounds.Min.X : bounds.Max.X, (k & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (k & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                var at = frame.LocalToParent(pose[part.Index].TransformPoint(corner));
                (min, max) = (Vector3.Min(min, at), Vector3.Max(max, at));
            }
        }

        if (min.X > max.X)
        {
            return null;
        }

        // Not a ramp: 0x02000623 in the Facility Hub is such a Setup too, and the body walks on it, not round it. A staircase in a room is read (the world walk's rooms, W8:
        // 0x02000396, 4.8 m tall, in a house layout repeated over a hundred landblocks): inside its disc the foot's probe judges, so its side stands in the way and its foot does not.
        var half = new Vector2(max.X - min.X, max.Y - min.Y) * 0.5f;
        if (half.Length() > PartsReachMostMetres)
        {
            return null;
        }

        return new StaticBody(new Vector2(min.X + half.X, min.Y + half.Y), half.Length(), frame.Origin.Z, min.Z, max.Z, Probed: true);
    }

    /// <summary>The lowest and highest height of a model's vertex bounds placed at <paramref name="frame"/> (its eight corners turned and moved).</summary>
    private static (float Bottom, float Top) Height(Placement frame, BoundingBox bounds)
    {
        var (bottom, top) = (float.MaxValue, float.MinValue);
        for (var k = 0; k < 8; k++)
        {
            var corner = new Vector3((k & 1) == 0 ? bounds.Min.X : bounds.Max.X, (k & 2) == 0 ? bounds.Min.Y : bounds.Max.Y, (k & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
            var z = frame.LocalToParent(corner).Z;
            (bottom, top) = (MathF.Min(bottom, z), MathF.Max(top, z));
        }

        return (bottom, top);
    }

    /// <summary>True when a part of <paramref name="model"/> has a physics BSP (the GfxObj's HasPhysics flag, as <see cref="PhysicsGeometryLibrary"/> reads a part's collision).</summary>
    private static bool HasPhysicsParts(DatSet dats, ModelAssembly model) =>
        model.Parts.Any(p => p.IsGfxObj && GfxObjHasPhysics(dats, p.GfxObjId));

    /// <summary>True when the GfxObj <paramref name="id"/> has a physics BSP (its HasPhysics flag).</summary>
    private static bool GfxObjHasPhysics(DatSet dats, uint id) =>
        dats.TryGetPortal<DatReaderWriter.DBObjs.GfxObj>(id, out var gfxObj) && gfxObj.Flags.HasFlag(DatReaderWriter.Enums.GfxObjFlags.HasPhysics);

    /// <summary>
    /// The points to walk through inside <paramref name="cell"/> from <paramref name="from"/> to <paramref name="to"/> when the straight walk leaves its floor
    /// (<see cref="StaysOnFloor"/>): the shortest chain over the floor polygons' centres and their corners drawn 0.5 m in, each straight step on the floor.
    /// Empty when the straight walk stays on the floor, or when no chain does (the walk is then left straight, as before, for the runner's physics to judge).
    /// </summary>
    /// <param name="cell">The room.</param>
    /// <param name="from">The walk's start, landblock space.</param>
    /// <param name="to">Its end.</param>
    /// <param name="refused">M6 7 AI: a straight step this says yes to is off the walk as one that leaves the floor is (a kept portal's disc, <see cref="Wayfinding.RoomGraph.KeepOff"/>), or null. The caller judges a chain left straight.</param>
    /// <param name="unwalked">True when the walk is left straight and the pre-check's probe refuses it (<see cref="ProbeWalks"/>): no way round was found inside the room.</param>
    /// <remarks>
    /// M6 14 hub plan: the detour with no step refused is worked out once and kept in the planner's <see cref="Memo"/>, and a walk with refusals takes it as it is when none of its steps is refused (from the start through
    /// every via to the end): a refusal only takes steps out of the search, so the shortest chain that keeps all its steps is still the shortest, and the straight walk, the end rings and the grid search
    /// (which reads no refusal) come out the same. Only a walk whose own steps are refused is searched again. The live hub's first plan searched every walk twice, once bare (the runner's raised-start check)
    /// and once with the leg's kept portals.
    /// </remarks>
    public IReadOnlyList<Vector3> JudgedFloorDetour(EnvCellData cell, Vector3 from, Vector3 to, Func<Vector3, Vector3, bool>? refused, out bool unwalked)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (refused is null || _memo.ReuseBareDetours)
        {
            var bare = Memo.Answer(_memo.Detours, (Cell: cell.Id, From: from, To: to), static (key, state) =>
            {
                var (self, cell) = state;
                var vias = self.FloorDetour(cell, key.From, key.To);
                return ([.. vias], vias.Count == 0 && self.World() is { } world && !self.StraightWalks(cell, key.From, key.To, world));
            }, (Self: this, Cell: cell));
            if (refused is null || !AnyStepRefused(from, bare.Vias, to, refused))
            {
                unwalked = bare.Unwalked;
                return bare.Vias;
            }
        }

        var kept = FloorDetour(cell, from, to, refused);
        unwalked = kept.Count == 0 && World() is { } world && !StraightWalks(cell, from, to, world);
        return kept;
    }

    /// <summary>True when <paramref name="refused"/> refuses a step of the walk from <paramref name="from"/> through <paramref name="vias"/> to <paramref name="to"/>.</summary>
    private static bool AnyStepRefused(Vector3 from, Vector3[] vias, Vector3 to, Func<Vector3, Vector3, bool> refused)
    {
        var at = from;
        foreach (var via in vias)
        {
            if (refused(at, via))
            {
                return true;
            }

            at = via;
        }

        return refused(at, to);
    }

    /// <summary>
    /// <see cref="JudgedFloorDetour"/> without the verdict on a straight walk left as it was.
    /// </summary>
    /// <param name="cell">The room.</param>
    /// <param name="from">The walk's start, landblock space.</param>
    /// <param name="to">Its end.</param>
    /// <param name="refused">As for <see cref="JudgedFloorDetour"/>.</param>
    public IReadOnlyList<Vector3> FloorDetour(EnvCellData cell, Vector3 from, Vector3 to, Func<Vector3, Vector3, bool>? refused = null)
    {
        ArgumentNullException.ThrowIfNull(cell);
        bool Walks(Vector3 a, Vector3 b) => refused?.Invoke(a, b) != true && Memo.Answer(_memo.Floors, (Cell: cell.Id, From: a, To: b), static (key, state) => state.Self.StaysOnFloor(state.Cell, key.From, key.To), (Self: this, Cell: cell)); // M6 14 hub plan: kept in the memo
        var world = World();

        // The floor test sees the floor polygons and the statics' discs only; a straight step over the floor can still meet a solid the foot meets (a statue's plinth by a doorway, a wall thicker than the
        // floor's edge), which the pre-check then refused while the body slid round it (the world walk's rooms, W8: 0x00440150 to 0x00440152). The straight step is asked the
        // pre-check's own probe (ProbeWalks). Every step of a detour is asked for ground along it (GroundAlong): a step from the start to a floor's corner the floor test passed crossed a table's
        // edge the same way (0x009C0191). The straight walk is kept when no detour is found, as before.
        if (Walks(from, to) && (world is null || StraightWalks(cell, from, to, world)))
        {
            return [];
        }

        // An end inside a static's disc (a body standing beside a pillar, a goal by a pot) leaves every step from it refused, so the walk was left straight through the static
        // (the world walk's rooms: 0x00AF017A's 3 m pillar 0x02000248 by 3 mm). Such an end first steps straight out of the disc.
        var head = OutOfStatics(cell, from, world);
        var tail = OutOfStatics(cell, to, world);
        var points = new List<Vector3> { head ?? from, tail ?? to };
        foreach (var floor in WalkableFloors(cell))
        {
            var centre = floor.Aggregate(Vector3.Zero, (sum, v) => sum + v) / floor.Length;
            if (Via(cell, centre, world))
            {
                points.Add(centre);
            }

            for (var i = 0; i < floor.Length; i++)
            {
                var inward = centre - floor[i];
                var reach = inward.Length();
                if (reach <= 1e-3f)
                {
                    continue;
                }

                var near = floor[i] + (inward * (MathF.Min(0.5f, reach * 0.5f) / reach));
                if (Via(cell, near, world))
                {
                    points.Add(near);
                }

                if (world is not null && DrawnIn(floor, i, near) is { } clear)
                {
                    if (Via(cell, clear, world))
                    {
                        points.Add(clear);
                    }
                    else if (DrawnIn(floor, i, near, FloorCornerDeepMetres, reach * 0.5f) is { } deep && Via(cell, deep, world))
                    {
                        // A wall thicker than the floor's edge holds the foot off the corner (0x0043010C: the room's floor runs 6 m to x 23, the foot touches its walls 0.5 m in,
                        // so no corner held it and walks round the bed in the middle ran straight across it): the corner is drawn in twice as far, at most halfway to the centre.
                        points.Add(deep);
                    }
                }
            }
        }

        var ringed = points.Count;
        int[] Search(int count)
        {
            var cost = new float[count];
            var previous = new int[count];
            var done = new bool[count];
            Array.Fill(cost, float.MaxValue);
            Array.Fill(previous, -1);
            cost[0] = 0f;
            for (var round = 0; round < count; round++)
            {
                var at = -1;
                for (var i = 0; i < count; i++)
                {
                    if (!done[i] && cost[i] < float.MaxValue && (at < 0 || cost[i] < cost[at]))
                    {
                        at = i;
                    }
                }

                if (at < 0 || at == 1)
                {
                    break;
                }

                done[at] = true;
                for (var i = 0; i < count; i++)
                {
                    var next = cost[at] + Vector3.Distance(points[at], points[i]);
                    if (!done[i] && next < cost[i] && Walks(points[at], points[i]) && GroundAlong(cell, points[at], points[i], world) && (world is null || StraightWalks(cell, points[at], points[i], world)))
                    {
                        (cost[i], previous[i]) = (next, at);
                    }
                }
            }

            return previous;
        }

        var previous = Search(ringed);
        if (previous[1] < 0)
        {
            // An end in a pocket beside a static (a moved standing place between a dais and a wall fitting, the world walk's rooms, W8: 0x720201F7) has no corner or centre near it that both
            // it and the rest of the room see: only then are points round each end tried too, so a chain that walks is kept as it was (taken first everywhere, they bent 146 walks the body
            // walked cleanly into a doorway's side). A step to or from one is asked for ground along it (GroundAlong), since such a point hugs the room's solids: one 1 m from the Tusker
            // Habitat's arrival (0x5B470177) led a step where the check finds no ground and the floor test sees nothing.
            foreach (var end in new[] { head ?? from, tail ?? to })
            {
                foreach (var ring in EndRingMetres)
                {
                    for (var k = 0; k < EndRingDirections; k++)
                    {
                        var angle = k * (2f * MathF.PI / EndRingDirections);
                        var near = end + new Vector3(MathF.Cos(angle) * ring, MathF.Sin(angle) * ring, 0f);
                        if (Via(cell, near, world))
                        {
                            points.Add(near);
                        }
                    }
                }
            }

            if (points.Count > ringed)
            {
                previous = Search(points.Count);
            }
        }

        if (previous[1] < 0)
        {
            // No chain over the floor's centres, corners or the rings round the ends: a room cluttered past what those points see (a body beside an object whose disc only bounds it, a gap of
            // half a metre between it and the wall; the world walk's rooms, W8: 0x720001AD and its 39 copies). The walk was left straight through the clutter, which the pre-check then refused
            // while the body slid round it. The foot's own probe then searches the room on a grid (GridDetour).
            return GridDetour(cell, head ?? from, tail ?? to, world) is { } grid ? Ended(grid) : [];
        }

        var chain = new List<Vector3>();
        for (var at = previous[1]; at > 0; at = previous[at])
        {
            chain.Add(points[at]);
        }

        chain.Reverse();
        return Ended(chain);

        List<Vector3> Ended(List<Vector3> chain)
        {
            if (head is { } first)
            {
                chain.Insert(0, first);
            }

            if (tail is { } last)
            {
                chain.Add(last);
            }

            return chain;
        }
    }

    /// <summary>True when the pre-check's probe passes the straight step from <paramref name="from"/> to <paramref name="to"/> in <paramref name="cell"/> (<see cref="ProbeWalks"/>).</summary>
    /// <remarks>Answered once a step and kept in the planner's <see cref="Memo"/> (M6 14 hub plan): the fixed world and the human body give the same answer every time.</remarks>
    private bool StraightWalks(EnvCellData cell, Vector3 from, Vector3 to, PhysicsWorld world) =>
        Memo.Answer(_memo.Straight, (Cell: cell.Id, From: from, To: to), static (key, state) =>
        {
            var (self, world) = state;
            lock (world)
            {
                return ProbeWalks(world, new CellFrame(key.Cell, new AcFrame(key.From, Quaternion.Identity)), new CellFrame(key.Cell, new AcFrame(key.To, Quaternion.Identity)), self.Body(world), reachEnd: true);
            }
        }, (Self: this, World: world));

    /// <summary>The spacing of <see cref="GridDetour"/>'s grid, in metres. Not retail: Lifestone's own.</summary>
    public const float GridDetourMetres = 0.25f;

    /// <summary>An end of a walk this far over its room's floor (on a counter, a table) is not searched from by <see cref="GridDetour"/>, in metres: the runner's step-off's own threshold. Not retail: Lifestone's own.</summary>
    public const float GridRaisedMetres = 0.5f;

    /// <summary>The most points <see cref="GridDetour"/> probes in one search (a floor of about 250 square metres at <see cref="GridDetourMetres"/>). Not retail: Lifestone's own.</summary>
    public const int GridDetourMostPoints = 4000;

    /// <summary>
    /// The points to walk through from <paramref name="from"/> to <paramref name="to"/> in <paramref name="cell"/>, found by the foot's own probe (the pre-check's) on a grid of <see cref="GridDetourMetres"/>: an A* over the points
    /// in the room where the foot finds ground within the step-up of the point before, eight ways, a diagonal past a blocked side only where the probe finds ground along it (a gap the body threads at a slant), the path then pulled straight wherever the probe finds ground along a straight step
    /// (<see cref="GroundAlong"/>). It sees every solid the physics holds, where the planner's discs only bound them. Null without the dats, when the search finds no way within <see cref="GridDetourMostPoints"/>,
    /// when an end stands over its room's floor (<see cref="GridRaisedMetres"/>), or when the straight walk already has ground (nothing to go round). Not retail: Lifestone's own.
    /// </summary>
    private List<Vector3>? GridDetour(EnvCellData cell, Vector3 from, Vector3 to, PhysicsWorld? world)
    {
        if (world is null || Raised(from) || Raised(to) || GroundAlong(cell, from, to, world))
        {
            return null;
        }

        // An end standing on a counter or a table is no start for a search over the room's floor: its probe finds the counter's top, and the plan would keep the body up there,
        // where the runner's step-off (RaisedStart, which reads the plan's first point under the start) brings it down first (Renald's counter, 0xA9B4013F).
        bool Raised(Vector3 end) => FloorUnder(cell, end + new Vector3(0f, 0f, 0.1f)) is { } floor && end.Z - floor > GridRaisedMetres;

        Vector3 At((int X, int Y) key, float z) => new(from.X + (key.X * GridDetourMetres), from.Y + (key.Y * GridDetourMetres), z);
        var heights = new Dictionary<(int X, int Y), float?> { [(0, 0)] = from.Z };
        float? Ground((int X, int Y) key, float fromZ)
        {
            if (heights.TryGetValue(key, out var known))
            {
                return known is { } z0 && MathF.Abs(z0 - fromZ) <= StepUpMetres ? z0 : null;
            }

            float? found = null;
            var at = At(key, fromZ);
            if (heights.Count < GridDetourMostPoints && _cells.Contains(cell, at + new Vector3(0f, 0f, 0.5f)))
            {
                lock (world)
                {
                    if (world.TryFixedGroundZ(new CellFrame(cell.Id, new AcFrame(at, Quaternion.Identity)), StairFoot, out var z, out _) && MathF.Abs(z - fromZ) <= StepUpMetres)
                    {
                        found = z;
                    }
                }
            }

            heights[key] = found;
            return found;
        }

        var goal = new Vector2(to.X, to.Y);
        var cost = new Dictionary<(int X, int Y), float> { [(0, 0)] = 0f };
        var previous = new Dictionary<(int X, int Y), (int X, int Y)>();
        var open = new PriorityQueue<(int X, int Y), float>();
        open.Enqueue((0, 0), Vector2.Distance(new Vector2(from.X, from.Y), goal));
        (int X, int Y)? reached = null;
        while (open.TryDequeue(out var key, out _))
        {
            var here = At(key, heights[key]!.Value);
            if (Vector2.Distance(new Vector2(here.X, here.Y), goal) <= GridDetourMetres * 1.5f && GroundAlong(cell, here, to, world))
            {
                reached = key;
                break;
            }

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if ((dx, dy) == (0, 0))
                    {
                        continue;
                    }

                    var next = (key.X + dx, key.Y + dy);
                    if (Ground(next, here.Z) is not { } z
                        || (dx != 0 && dy != 0 && (Ground((key.X + dx, key.Y), here.Z) is null || Ground((key.X, key.Y + dy), here.Z) is null) && !GroundAlong(cell, here, At(next, z), world)))
                    {
                        continue;
                    }

                    var step = cost[key] + (GridDetourMetres * ((dx != 0 && dy != 0) ? MathF.Sqrt(2f) : 1f));
                    if (!cost.TryGetValue(next, out var known) || step < known)
                    {
                        cost[next] = step;
                        previous[next] = key;
                        var there = At(next, z);
                        open.Enqueue(next, step + Vector2.Distance(new Vector2(there.X, there.Y), goal));
                    }
                }
            }
        }

        if (reached is not { } last)
        {
            return null;
        }

        var path = new List<Vector3> { to };
        for (var key = last; ; key = previous[key])
        {
            path.Add(At(key, heights[key]!.Value));
            if (key == (0, 0))
            {
                break;
            }
        }

        path[^1] = from;
        path.Reverse();
        var pulled = new List<Vector3>();
        var anchor = 0;
        while (anchor < path.Count - 1)
        {
            var far = anchor + 1;
            for (var j = path.Count - 1; j > anchor + 1; j--)
            {
                if (GroundAlong(cell, path[anchor], path[j], world))
                {
                    far = j;
                    break;
                }
            }

            if (far < path.Count - 1)
            {
                pulled.Add(path[far]);
            }

            anchor = far;
        }

        return pulled;
    }

    /// <summary>
    /// True when the foot finds ground every <see cref="FloorSampleMetres"/> along the straight step from <paramref name="a"/> to <paramref name="b"/>, each within the step-up of the one before (the pre-check's probe); true without the dats.
    /// A step from an end moved out of a static (<see cref="OutOfStatics"/>) is asked this, since the floor test sees no wall: the move put 0x001501CD's start 0.27 m nearer a doorway's corner, and the step from it grazed the corner by 5 cm.
    /// </summary>
    /// <remarks>Answered once a step and kept in the planner's <see cref="Memo"/> (M6 14 hub plan).</remarks>
    private bool GroundAlong(EnvCellData cell, Vector3 a, Vector3 b, PhysicsWorld? world) =>
        world is null || Memo.Answer(_memo.Ground, (Cell: cell.Id, From: a, To: b), static (key, world) => GroundAlongProbe(key.Cell, key.From, key.To, world), world);

    private static bool GroundAlongProbe(uint cellId, Vector3 a, Vector3 b, PhysicsWorld world)
    {
        var length = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        var z = a.Z;
        lock (world)
        {
            for (var s = 0f; s <= length + 1e-3f; s += FloorSampleMetres)
            {
                var at = Vector3.Lerp(a, b, length > 0f ? MathF.Min(s / length, 1f) : 1f);
                if (!world.TryFixedGroundZ(new CellFrame(cellId, new AcFrame(at with { Z = z }, Quaternion.Identity)), StairFoot, out var found, out _) || MathF.Abs(found - z) > StepUpMetres)
                {
                    return false;
                }

                z = found;
            }
        }

        return true;
    }

    /// <summary>How far an end of a walk inside a static's disc may be moved straight out of it (<see cref="FloorDetour"/>), in metres; an end deeper in stands in the static itself and is left. Not retail: Lifestone's own.</summary>
    public const float StaticEscapeMetres = 1f;

    /// <summary>
    /// The nearest point straight out of the static discs <paramref name="point"/> lies in (<see cref="InStatic"/>), just past their clearance, where a detour may pass (<see cref="Via"/>);
    /// null when the point is in none, lies deeper than <see cref="StaticEscapeMetres"/>, or the point out is not a via. Only exact discs are stepped out of: an object with physics BSP
    /// parts is a rough disc, and stepping out of one sent walks into its real solid where the straight walk had passed (0x00150174's wall fittings, 313 walks of the world walk's rooms); inside one the point is
    /// in its real solid only where the body does not stand (<see cref="FootStands"/>), and the nearest point that stands is searched instead (<see cref="NearestStanding"/>).
    /// </summary>
    private Vector3? OutOfStatics(EnvCellData cell, Vector3 point, PhysicsWorld? world)
    {
        if (!InStatic(cell, point))
        {
            return null;
        }

        var at = point;
        for (var round = 0; round < 4; round++)
        {
            var moved = false;
            foreach (var body in StaticsOf(cell))
            {
                var flat = new Vector2(at.X, at.Y) - body.Centre;
                var reach = body.Radius + StaticClearanceMetres;
                if (!body.InTheWayAt(at.Z) || flat.Length() >= reach)
                {
                    continue;
                }

                if (!body.Exact || flat.Length() < 1e-3f)
                {
                    return body.Probed ? NearestStanding(cell, point, world) : null;
                }

                var away = body.Centre + (Vector2.Normalize(flat) * (reach + 0.05f));
                at = new Vector3(away.X, away.Y, at.Z);
                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        return Vector2.Distance(new Vector2(at.X, at.Y), new Vector2(point.X, point.Y)) <= StaticEscapeMetres && Via(cell, at, world) ? at : null;
    }

    /// <summary>The rings <see cref="NearestStanding"/> searches, in metres out from the point (to <see cref="StaticEscapeMetres"/>). Not retail: Lifestone's own.</summary>
    private static readonly float[] StandingRings = [0.25f, 0.5f, 0.75f, StaticEscapeMetres];

    /// <summary>How many directions <see cref="NearestStanding"/> tries on each ring. Not retail: Lifestone's own.</summary>
    private const int StandingDirections = 16;

    /// <summary>
    /// The nearest point within <see cref="StaticEscapeMetres"/> of <paramref name="point"/> out of the statics where a detour may pass (<see cref="Via"/>), searched ring by ring: an end inside the disc of an object with
    /// physics BSP parts has no centre to step straight out from (the disc only bounds it). The world walk's rooms: a room's frame under a bunk's top board (0x01060182, GfxObj 0x01000A29 at 1.49 m), where the foot fits
    /// and the body does not; null when none.
    /// </summary>
    private Vector3? NearestStanding(EnvCellData cell, Vector3 point, PhysicsWorld? world)
    {
        foreach (var ring in StandingRings)
        {
            for (var i = 0; i < StandingDirections; i++)
            {
                var angle = i * (2f * MathF.PI / StandingDirections);
                var at = point + new Vector3(MathF.Cos(angle) * ring, MathF.Sin(angle) * ring, 0f);
                if (Via(cell, at, world))
                {
                    return at;
                }
            }
        }

        return null;
    }

    /// <summary>The rings round each end of a walk <see cref="FloorDetour"/> also tries as vias, in metres. Not retail: Lifestone's own.</summary>
    private static readonly float[] EndRingMetres = [1f, 1.5f];

    /// <summary>How many directions <see cref="FloorDetour"/> tries on each of <see cref="EndRingMetres"/>. Not retail: Lifestone's own.</summary>
    private const int EndRingDirections = 8;

    /// <summary>How far a corner of a floor is drawn in from both its edges for <see cref="FloorDetour"/>, in metres: the body's width and a margin. Not retail: Lifestone's own.</summary>
    public const float FloorCornerClearanceMetres = HumanHalfWidth + 0.1f;

    /// <summary>How far a corner is drawn in from both its edges when <see cref="FloorCornerClearanceMetres"/> does not hold the foot (a wall thicker than the floor's edge), in metres. Not retail: Lifestone's own.</summary>
    public const float FloorCornerDeepMetres = 2f * FloorCornerClearanceMetres;

    /// <summary>
    /// True when a detour may pass through <paramref name="point"/>: out of the room's static objects (<see cref="InStatic"/>) and, with the dats, where the foot stands (<paramref name="world"/>'s ground probe, the pre-check's): a floor's edge is a wall more often
    /// than not, and a corner drawn 0.5 m toward its floor's centre stood 0.26 m off the wall in Holtburg's 0xA9B30103, a room half as wide as it is long, so the plan by the back door had no floor for the foot at its own via (M6 7 G).
    /// </summary>
    /// <remarks>Answered once a point and kept in the planner's <see cref="Memo"/> (M6 14 hub plan).</remarks>
    private bool Via(EnvCellData cell, Vector3 point, PhysicsWorld? world) =>
        Memo.Answer(_memo.Vias, (Cell: cell.Id, Point: point), static (key, state) =>
        {
            var (self, cell, world) = state;
            if (self.InStatic(cell, key.Point))
            {
                return false;
            }

            if (world is null)
            {
                return true;
            }

            lock (world)
            {
                return world.TryFixedGroundZ(new CellFrame(key.Cell, new AcFrame(key.Point, Quaternion.Identity)), StairFoot, out var z, out _) && MathF.Abs(z - key.Point.Z) <= StepUpMetres;
            }
        }, (Self: this, Cell: cell, World: world));

    /// <summary>
    /// The second candidate of a floor's corner <paramref name="i"/>: on the bisector, <see cref="FloorCornerClearanceMetres"/> from both its edges, at most as far as twice <paramref name="near"/>, the first (a narrow floor); null where the
    /// bisector does not lead into the floor (a reflex corner, a point on an edge's line).
    /// </summary>
    private static Vector3? DrawnIn(Vector3[] floor, int i, Vector3 near) => DrawnIn(floor, i, near, FloorCornerClearanceMetres, 2f * (near - floor[i]).Length());

    /// <summary>A floor's corner <paramref name="i"/> drawn in on the bisector <paramref name="clearance"/> from both its edges, at most <paramref name="most"/> from the corner; null as above.</summary>
    private static Vector3? DrawnIn(Vector3[] floor, int i, Vector3 near, float clearance, float most)
    {
        var corner = floor[i];
        var previous = Vector3.Normalize(floor[(i + floor.Length - 1) % floor.Length] - corner);
        var next = Vector3.Normalize(floor[(i + 1) % floor.Length] - corner);
        var bisector = previous + next;
        var sinHalf = MathF.Sqrt(MathF.Max(0f, (1f - Vector3.Dot(previous, next)) * 0.5f));
        var inward = near - corner;
        if (bisector.LengthSquared() <= 1e-4f || sinHalf <= 1e-3f || Vector3.Dot(bisector, inward) <= 0f)
        {
            return null;
        }

        return corner + (Vector3.Normalize(bisector) * MathF.Min(clearance / sinHalf, most));
    }

    /// <summary>The steps past a floor crossing's edge tried for the push point, in metres: the doorway's 0.5 m, its half, then further while the cell
    /// entered still does not hold the point (a corridor whose cells part along a slanted plane holds a body's height only some way past the edge).</summary>
    private static ReadOnlySpan<float> FloorPushSteps => [PushIntoCellMetres, PushIntoCellMetres * 0.5f, 1f, 1.5f, 2f, 2.5f];

    /// <summary>
    /// The pair of waypoints of a floor crossing: the approach <see cref="ApproachMetres"/> (or half) back across the edge in <paramref name="cell"/>
    /// (null when neither point lies in it 1 m over its floor) and the push past the edge in <paramref name="other"/> (<see cref="FloorPushSteps"/>,
    /// the first that lies in it 1 m over its floor; the edge's middle on its floor when none does), each on the floor found under it.
    /// </summary>
    private (CellFrame? Approach, CellFrame Push) FloorPair(EnvCellData cell, EnvCellData other, FloorCrossing crossing)
    {
        CellFrame? approach = null;
        foreach (var distance in (ReadOnlySpan<float>)[ApproachMetres, ApproachMetres * 0.5f])
        {
            if (FloorPoint(cell, crossing.From - (crossing.Into * distance), crossing.From.Z) is { } point)
            {
                approach = point;
                break;
            }
        }

        foreach (var distance in FloorPushSteps)
        {
            if (FloorPoint(other, crossing.To + (crossing.Into * distance), crossing.To.Z) is { } point)
            {
                return (approach, point);
            }
        }

        return (approach, new CellFrame(other.Id, new AcFrame(crossing.To, Quaternion.Identity)));
    }

    /// <summary>The point <paramref name="at"/> (x, y) dropped onto <paramref name="cell"/>'s floor found under the edge's height plus 1 m, when the cell holds it 1 m over that floor; else null.</summary>
    private CellFrame? FloorPoint(EnvCellData cell, Vector3 at, float edgeZ)
    {
        if (FloorUnder(cell, new Vector3(at.X, at.Y, edgeZ + 1f)) is not { } z || !_cells.Contains(cell, new Vector3(at.X, at.Y, z + 1f)))
        {
            return null;
        }

        return new CellFrame(cell.Id, new AcFrame(new Vector3(at.X, at.Y, z), Quaternion.Identity));
    }

    /// <summary>The search of <see cref="FloorCrossingOf"/>: every pair of walkable floor edges of the two cells; the longest shared run that passes.</summary>
    private FloorCrossing? FindFloorCrossing(EnvCellData cell, CellPortalData portal, EnvCellData other)
    {
        if (_cells.GetStructure(cell).GetPortal(portal.PolygonId) is not { } polygon)
        {
            return null;
        }

        var portalCorners = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
        var portalNormal = Vector3.Normalize(cell.Frame.RotateToParent(polygon.Plane.Normal));
        var mine = WalkableFloors(cell);
        var theirs = WalkableFloors(other);
        FloorCrossing? best = null;
        foreach (var a in mine)
        {
            foreach (var b in theirs)
            {
                for (int i = 0, j = a.Length - 1; i < a.Length; j = i++)
                {
                    for (int k = 0, l = b.Length - 1; k < b.Length; l = k++)
                    {
                        if (SharedEdge(a, a[j], a[i], b, b[l], b[k]) is not { } shared
                            || (best is { } known && known.Width >= shared.Width)
                            || MathF.Min(DistanceToPolygon(portalCorners, portalNormal, shared.From), DistanceToPolygon(portalCorners, portalNormal, shared.To)) > FloorCrossingPortalMetres
                            || !HasHeadroom(cell, other, shared))
                        {
                            continue;
                        }

                        best = shared;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// True when <paramref name="cell"/> is a ledge, not a room: it has walkable floors, every one narrower than a body (<see cref="HumanHalfWidth"/> twice: the smallest extent across any of its edges),
    /// and the foot finds ground within the step-up nowhere on them (the pre-check's probe, every <see cref="LedgeSampleMetres"/> over each floor, <see cref="LedgeRimMetres"/> in from its edges). The world walk's rooms: a plinth 0.5 m wide and 0.5 m high along a room's walls
    /// (0x62440115 and 0118, in a layout repeated over hundreds of landblocks) had a floor and doorways, so legs were planned onto it, which no body stands on; 90% of the walks
    /// that failed with and without the check had a ledge at one end. A doorframe cell is as narrow but its floor holds the foot, so it stays, even where a static blocks its middle
    /// (0x01680137: the mesh 0x01000A69 in its mouth, a lane open beside it). False without the dats.
    /// </summary>
    public bool IsLedge(EnvCellData cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        return _ledges.GetOrAdd(cell.Id, id =>
        {
            var floors = WalkableFloors(cell);
            if (floors.Count == 0 || floors.Any(f => FloorWidth(f) >= 2f * HumanHalfWidth) || World() is not { } world)
            {
                return false;
            }

            lock (world)
            {
                foreach (var floor in floors)
                {
                    foreach (var point in FloorSamples(floor))
                    {
                        if (world.TryFixedGroundZ(new CellFrame(cell.Id, new AcFrame(point, Quaternion.Identity)), StairFoot, out var z, out _) && MathF.Abs(z - point.Z) <= StepUpMetres)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        });
    }

    /// <summary>How far apart <see cref="IsLedge"/>'s samples over a floor lie, in metres. Not retail: Lifestone's own.</summary>
    public const float LedgeSampleMetres = 0.25f;

    /// <summary>
    /// How far inside every edge of a floor <see cref="IsLedge"/>'s samples lie, in metres: a body whose centre is on a floor's rim stands mostly off it. Not retail: Lifestone's own. The world walk's rooms: at
    /// the outer corner of an L-shaped edging 0.5 m wide and 0.5 m high (0x00110107 and 0102, env 0x0D000599, in a layout repeated over dozens of dungeons) the body balanced on the corner with its centre on
    /// the floor's diagonal edge, so the edging counted as a room and walks were planned onto it.
    /// </summary>
    public const float LedgeRimMetres = 0.05f;

    /// <summary>The floor's centre, then points every <see cref="LedgeSampleMetres"/> at least <see cref="LedgeRimMetres"/> inside the convex floor, on its plane.</summary>
    private static IEnumerable<Vector3> FloorSamples(Vector3[] floor)
    {
        var centre = floor.Aggregate(Vector3.Zero, (sum, v) => sum + v) / floor.Length;
        yield return centre;
        var normal = Vector3.Cross(floor[1] - floor[0], floor[2] - floor[0]);
        if (MathF.Abs(normal.Z) < 1e-6f)
        {
            yield break;
        }

        float minX = floor.Min(v => v.X), maxX = floor.Max(v => v.X), minY = floor.Min(v => v.Y), maxY = floor.Max(v => v.Y);
        for (var x = minX + (LedgeSampleMetres * 0.5f); x < maxX; x += LedgeSampleMetres)
        {
            for (var y = minY + (LedgeSampleMetres * 0.5f); y < maxY; y += LedgeSampleMetres)
            {
                if (InsideConvex(floor, x, y, LedgeRimMetres))
                {
                    // On the floor's plane: n . (p - v0) = 0.
                    var z = floor[0].Z - (((normal.X * (x - floor[0].X)) + (normal.Y * (y - floor[0].Y))) / normal.Z);
                    yield return new Vector3(x, y, z);
                }
            }
        }
    }

    /// <summary>True when (x, y) lies inside the convex polygon seen from above (either winding), at least <paramref name="margin"/> from each of its edges.</summary>
    private static bool InsideConvex(Vector3[] polygon, float x, float y, float margin)
    {
        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var length = MathF.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
            if (length < 1e-4f)
            {
                continue;
            }

            // The signed distance from the edge's line, seen from above.
            var distance = (((b.X - a.X) * (y - a.Y)) - ((b.Y - a.Y) * (x - a.X))) / length;
            if (MathF.Abs(distance) < margin)
            {
                return false;
            }

            var side = distance > 0f ? 1 : -1;
            if (sign != 0 && side != sign)
            {
                return false;
            }

            sign = side;
        }

        return true;
    }

    /// <summary>A convex floor polygon's width seen from above: the smallest, over its edges, of the farthest corner from the edge's line.</summary>
    private static float FloorWidth(Vector3[] floor)
    {
        var width = float.MaxValue;
        for (var i = 0; i < floor.Length; i++)
        {
            var a = new Vector2(floor[i].X, floor[i].Y);
            var b = new Vector2(floor[(i + 1) % floor.Length].X, floor[(i + 1) % floor.Length].Y);
            if (Vector2.Distance(a, b) < 1e-4f)
            {
                continue;
            }

            var across = Vector2.Normalize(new Vector2(a.Y - b.Y, b.X - a.X));
            width = MathF.Min(width, floor.Max(v => MathF.Abs(Vector2.Dot(new Vector2(v.X, v.Y) - a, across))));
        }

        return width == float.MaxValue ? 0f : width;
    }

    /// <summary>The cell's walkable floor polygons (normal Z at least <see cref="CellStructure.FloorZ"/> in landblock space), as landblock-space corners.</summary>
    private List<Vector3[]> WalkableFloors(EnvCellData cell)
    {
        var floors = new List<Vector3[]>();
        foreach (var floor in _cells.GetStructure(cell).Floors)
        {
            if (cell.Frame.RotateToParent(floor.Plane.Normal).Z >= CellStructure.FloorZ)
            {
                floors.Add([.. floor.Vertices.Select(cell.Frame.LocalToParent)]);
            }
        }

        return floors;
    }

    /// <summary>
    /// The part of edge (<paramref name="a0"/>, <paramref name="a1"/>) of floor <paramref name="a"/> that edge (<paramref name="b0"/>, <paramref name="b1"/>)
    /// of floor <paramref name="b"/> also covers, seen from above, with the floors on opposite sides and their heights within <see cref="StepUpMetres"/> at both
    /// ends; null when there is none or it is narrower than the human.
    /// </summary>
    private static FloorCrossing? SharedEdge(Vector3[] a, Vector3 a0, Vector3 a1, Vector3[] b, Vector3 b0, Vector3 b1)
    {
        var run = new Vector2(a1.X - a0.X, a1.Y - a0.Y);
        var length = run.Length();
        if (length < 2f * HumanHalfWidth)
        {
            return null;
        }

        var d = run / length;
        var side = new Vector2(-d.Y, d.X);
        var origin = new Vector2(a0.X, a0.Y);
        var q0 = new Vector2(b0.X, b0.Y) - origin;
        var q1 = new Vector2(b1.X, b1.Y) - origin;
        if (MathF.Abs(Vector2.Dot(q0, side)) > FloorEdgeToleranceMetres || MathF.Abs(Vector2.Dot(q1, side)) > FloorEdgeToleranceMetres)
        {
            return null;
        }

        var (s0, s1) = (Vector2.Dot(q0, d), Vector2.Dot(q1, d));
        var lo = MathF.Max(0f, MathF.Min(s0, s1));
        var hi = MathF.Min(length, MathF.Max(s0, s1));
        if (hi - lo < 2f * HumanHalfWidth || MathF.Abs(s1 - s0) < 1e-4f)
        {
            return null;
        }

        // The floors lie on opposite sides of the edge (seen from above).
        var aSide = Vector2.Dot(Centroid2D(a) - origin, side);
        var bSide = Vector2.Dot(Centroid2D(b) - origin, side);
        if (aSide * bSide >= 0f)
        {
            return null;
        }

        float AZ(float s) => a0.Z + ((a1.Z - a0.Z) * (s / length));
        float BZ(float s) => b0.Z + ((b1.Z - b0.Z) * ((s - s0) / (s1 - s0)));
        if (MathF.Abs(AZ(lo) - BZ(lo)) > StepUpMetres || MathF.Abs(AZ(hi) - BZ(hi)) > StepUpMetres)
        {
            return null;
        }

        var mid = (lo + hi) * 0.5f;
        var at = origin + (d * mid);
        var into = bSide > 0f ? side : -side;
        return new FloorCrossing(new Vector3(at, AZ(mid)), new Vector3(at, BZ(mid)), new Vector3(into, 0f), hi - lo);
    }

    /// <summary>True when a standing human's column over the crossing's edge lies in one of the two cells, <see cref="FloorCrossingProbeMetres"/> either side of it.</summary>
    private bool HasHeadroom(EnvCellData cell, EnvCellData other, FloorCrossing crossing)
    {
        var along = new Vector3(-crossing.Into.Y, crossing.Into.X, 0f);
        Span<Vector3> probes =
        [
            new(0f, 0f, 0.2f),
            new(0f, 0f, 1f),
            new(0f, 0f, HumanHeight - HeadSlack),
            (along * HumanHalfWidth) + new Vector3(0f, 0f, 1f),
            (along * -HumanHalfWidth) + new Vector3(0f, 0f, 1f),
        ];
        foreach (var (foot, depth) in (ReadOnlySpan<(Vector3, float)>)[(crossing.From, -FloorCrossingProbeMetres), (crossing.To, FloorCrossingProbeMetres)])
        {
            foreach (var probe in probes)
            {
                var point = foot + (crossing.Into * depth) + probe;
                if (!_cells.Contains(cell, point) && !_cells.Contains(other, point))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static Vector2 Centroid2D(Vector3[] corners)
    {
        var sum = Vector2.Zero;
        foreach (var c in corners)
        {
            sum += new Vector2(c.X, c.Y);
        }

        return sum / corners.Length;
    }

    /// <summary>The distance from <paramref name="point"/> to the planar polygon <paramref name="corners"/> (unit <paramref name="normal"/>): to its plane over its inside, else to its nearest edge.</summary>
    private static float DistanceToPolygon(Vector3[] corners, Vector3 normal, Vector3 point)
    {
        var height = Vector3.Dot(point - corners[0], normal);
        var onPlane = point - (normal * height);
        var inside = true;
        var sign = 0;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var turn = Vector3.Dot(Vector3.Cross(corners[i] - corners[j], onPlane - corners[j]), normal);
            var s = MathF.Abs(turn) < 1e-6f ? 0 : MathF.Sign(turn);
            if (s != 0 && sign != 0 && s != sign)
            {
                inside = false;
                break;
            }

            sign = s != 0 ? s : sign;
        }

        if (inside)
        {
            return MathF.Abs(height);
        }

        var nearest = float.MaxValue;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var edge = corners[i] - corners[j];
            var t = Math.Clamp(Vector3.Dot(point - corners[j], edge) / MathF.Max(edge.LengthSquared(), 1e-8f), 0f, 1f);
            nearest = MathF.Min(nearest, Vector3.Distance(point, corners[j] + (edge * t)));
        }

        return nearest;
    }

    /// <summary>
    /// The point at <paramref name="portal"/>'s centre <paramref name="metres"/> beyond it (negative: before it, in
    /// <paramref name="cell"/>), over the floor of the cell it lies in.
    /// </summary>
    public CellFrame Waypoint(EnvCellData cell, CellPortalData portal, float metres) => Waypoint(cell, portal, metres, 0f);

    /// <summary><see cref="Waypoint(EnvCellData, CellPortalData, float)"/> at a crossing <paramref name="offset"/> metres along the portal from its centre (<see cref="CrossingOffsets"/>).</summary>
    private CellFrame Waypoint(EnvCellData cell, CellPortalData portal, float metres, float offset)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var polygon = _cells.GetStructure(cell).GetPortal(portal.PolygonId)
            ?? throw new InvalidDataException($"EnvCell 0x{cell.Id:X8} has no portal polygon {portal.PolygonId}.");
        var centre = Vector3.Zero;
        var sill = float.MaxValue;
        foreach (var vertex in polygon.Vertices)
        {
            var corner = cell.Frame.LocalToParent(vertex);
            centre += corner;
            sill = MathF.Min(sill, corner.Z);
        }

        centre /= polygon.Vertices.Length;

        // Retail portal_side: the cell lies on the negative side of the portal's plane when PortalSide, so the neighbour
        // lies along the normal; otherwise against it.
        var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
        var into = portal.PortalSide ? normal : -normal;
        into.Z = 0f;
        into = Vector3.Normalize(into);
        var point = centre + (into * metres) + (new Vector3(-into.Y, into.X, 0f) * offset);
        var target = metres >= 0f ? _cells.GetCell(portal.OtherCellId) : cell;
        var z = target is null ? sill : FloorUnder(target, new Vector3(point.X, point.Y, sill + 0.5f)) ?? sill;
        var cellId = target?.Id ?? cell.Id;
        return new CellFrame(cellId, new AcFrame(new Vector3(point.X, point.Y, z), Quaternion.Identity));
    }

    /// <summary>
    /// <see cref="Waypoint(EnvCellData, CellPortalData, float)"/> at <paramref name="metres"/>, or at half of it, whichever first lies inside the cell it is
    /// meant for (tested 1 m above the floor; a room thinner than the distance, such as a doorway's own cell, would put it
    /// in the next room); null when neither does.
    /// </summary>
    private CellFrame? InsideWaypoint(EnvCellData cell, CellPortalData portal, float metres, float offset)
    {
        var expected = metres >= 0f ? _cells.GetCell(portal.OtherCellId) : cell;
        if (expected is null)
        {
            return null;
        }

        CellFrame? inside = null;
        foreach (var distance in (ReadOnlySpan<float>)[metres, metres * 0.5f])
        {
            var point = Waypoint(cell, portal, distance, offset);
            if (_cells.Contains(expected, point.Frame.Origin + new Vector3(0f, 0f, 1f)))
            {
                if (metres <= 0f || PushStands(expected, Waypoint(cell, portal, 0f, offset), point))
                {
                    return point;
                }

                inside ??= point;
            }
        }

        return inside;
    }

    /// <summary>
    /// True when the foot finds ground every <see cref="PushSampleMetres"/> from the doorway <paramref name="sill"/> to the push <paramref name="point"/> in <paramref name="room"/>, each within the step-up of the one before
    /// (the pre-check's ground probe); true without the dats. A push past a doorway into a room no deeper than the body is wide (an arch through a thick wall) can land where the foot does not fit, or beyond a band
    /// of it a few centimetres deep, and the pre-check then refused every walk through it the body walks (the world walk's rooms, W8: 0x0287010E, 0x01680137); the push at half the distance is taken instead.
    /// </summary>
    private bool PushStands(EnvCellData room, in CellFrame sill, in CellFrame point)
    {
        if (World() is not { } world)
        {
            return true;
        }

        var (a, b) = (sill.Frame.Origin, point.Frame.Origin);
        var length = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        var z = a.Z;
        lock (world)
        {
            for (var s = 0f; s <= length + 1e-3f; s += PushSampleMetres)
            {
                var at = Vector3.Lerp(a, b, length > 0f ? MathF.Min(s / length, 1f) : 1f);
                if (!world.TryFixedGroundZ(new CellFrame(room.Id, new AcFrame(at with { Z = z }, Quaternion.Identity)), StairFoot, out var found, out _) || MathF.Abs(found - z) > StepUpMetres)
                {
                    return false;
                }

                z = found;
            }
        }

        return true;
    }

    /// <summary>How far apart <see cref="PushStands"/> probes the ground from a doorway to its push, in metres. Not retail: Lifestone's own.</summary>
    private const float PushSampleMetres = 0.1f;

    /// <summary>The first walkable portal of <paramref name="cell"/> into <paramref name="otherCellId"/>, or null.</summary>
    private CellPortalData? FindWalkablePortal(EnvCellData cell, uint otherCellId)
    {
        foreach (var portal in cell.Portals)
        {
            if (portal.OtherCellId == otherCellId && IsWalkable(cell, portal))
            {
                return portal;
            }
        }

        return null;
    }

    /// <summary>How many floor portals deep <see cref="FloorUnder(EnvCellData, Vector3)"/> looks for a floor below a cell (a slab cell between two portals is one). Not retail: Lifestone's own.</summary>
    public const int FloorPortalDepth = 2;

    /// <summary>
    /// The height (landblock space) of the highest walkable floor under <paramref name="point"/> and not above it that a body standing in <paramref name="cell"/>
    /// stands on, or null: a floor polygon of the cell's own, or one of a cell below it through a floor portal (a level portal polygon in the cell's floor, the
    /// neighbour beneath; <see cref="FloorPortalDepth"/> deep) that lies within <see cref="StepUpMetres"/> under that portal (M6 4 W6b). Tusker Abode's
    /// corridor 0x5C4501AE to 0x5C4501B2 has no floor polygon: its floor is a portal onto the bridge cells 0x5C450100 to 0x5C450118, whose floor lies on that
    /// portal at -12, and the walker stands on it there (the physics finds a sphere's contact in every cell it overlaps: <c>CTransition::step_down</c>,
    /// 11.6096 0x0050BD70). A floor further down is a drop, which retail's edge slide keeps a walking body out of (<c>CTransition::edge_slide</c>, 11.6096
    /// 0x0050BEA0): the bridge's channels 6 m under it, the crossroads 0x5C4501B7 over the pit 0x5C45011E.
    /// </summary>
    private float? FloorUnder(EnvCellData cell, Vector3 point) => FloorUnder(cell, point, FloorPortalDepth, float.MinValue);

    /// <summary>M6 8 M: <see cref="FloorUnder(EnvCellData, Vector3)"/> of the cell <paramref name="cellId"/> (the room's own floor polygons: a static such as a counter is not one), or null when the cell is not in the set or has no floor under the point.</summary>
    /// <param name="cellId">The room.</param>
    /// <param name="point">The point, landblock space.</param>
    public float? RoomFloorUnder(uint cellId, Vector3 point) => _cells.GetCell(cellId) is { } cell ? FloorUnder(cell, point) : null;

    private float? FloorUnder(EnvCellData cell, Vector3 point, int depth, float lowest)
    {
        var local = cell.Frame.ParentToLocal(point);
        float? best = null;
        foreach (var floor in _cells.GetStructure(cell).Floors)
        {
            if (MathF.Abs(floor.Plane.Normal.Z) < 1e-4f || !InPolygon2D(floor.Vertices, local.X, local.Y))
            {
                continue;
            }

            var z = -((floor.Plane.Normal.X * local.X) + (floor.Plane.Normal.Y * local.Y) + floor.Plane.D) / floor.Plane.Normal.Z;
            if (z <= local.Z && (best is null || z > best))
            {
                best = z;
            }
        }

        float? result = best is { } height ? cell.Frame.LocalToParent(new Vector3(local.X, local.Y, height)).Z : null;
        if (result < lowest)
        {
            result = null;
        }

        if (depth <= 0)
        {
            return result;
        }

        foreach (var (other, plane) in FloorPortalsUnder(cell, point))
        {
            if (FloorUnder(other, point with { Z = MathF.Min(point.Z, plane) }, depth - 1, MathF.Max(lowest, plane - StepUpMetres)) is { } below
                && (result is null || below > result))
            {
                result = below;
            }
        }

        return result;
    }

    /// <summary>
    /// The cells under <paramref name="cell"/>'s floor portals at <paramref name="point"/> (x, y), with each portal's height: a level portal polygon (normal Z 1 in
    /// landblock space) whose neighbour lies beneath it (<see cref="CellPortalData.PortalSide"/>), no higher than the point, the point inside it seen from above.
    /// </summary>
    private IEnumerable<(EnvCellData Other, float Plane)> FloorPortalsUnder(EnvCellData cell, Vector3 point)
    {
        var structure = _cells.GetStructure(cell);
        foreach (var portal in cell.Portals)
        {
            if (portal.LeadsOutside || structure.GetPortal(portal.PolygonId) is not { } polygon || _cells.GetCell(portal.OtherCellId) is not { } other)
            {
                continue;
            }

            var normal = cell.Frame.RotateToParent(polygon.Plane.Normal);
            var into = portal.PortalSide ? normal : -normal;
            if (into.Z > -0.99f)
            {
                continue; // not a floor portal
            }

            var corners = polygon.Vertices.Select(cell.Frame.LocalToParent).ToArray();
            var plane = corners.Average(c => c.Z);
            if (plane <= point.Z && InPolygon2D(corners, point.X, point.Y))
            {
                yield return (other, plane);
            }
        }
    }

    /// <summary>True when <paramref name="point"/> lies inside the polygon <paramref name="corners"/> seen along the horizontal axis <paramref name="along"/> and Z (a crossing test).</summary>
    private static bool InPortal(ReadOnlySpan<Vector3> corners, Vector3 along, Vector3 point)
    {
        var inside = false;
        var px = Vector3.Dot(point, along);
        var py = point.Z;
        for (int i = 0, j = corners.Length - 1; i < corners.Length; j = i++)
        {
            var xi = Vector3.Dot(corners[i], along);
            var yi = corners[i].Z;
            var xj = Vector3.Dot(corners[j], along);
            var yj = corners[j].Z;
            if ((yi > py) != (yj > py) && px < ((xj - xi) * (py - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>A crossing test of (<paramref name="x"/>, <paramref name="y"/>) against a polygon's X and Y.</summary>
    private static bool InPolygon2D(Vector3[] vertices, float x, float y)
    {
        var inside = false;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            if ((vertices[i].Y > y) != (vertices[j].Y > y)
                && x < ((vertices[j].X - vertices[i].X) * (y - vertices[i].Y) / (vertices[j].Y - vertices[i].Y)) + vertices[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
