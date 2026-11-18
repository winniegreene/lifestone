using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 item 0 a5 (D33): the fight's walk on an outdoor ground. Each leg goes to the next cell of the ground's <see cref="OutdoorTour"/> (the profile's <c>outdoor.cells</c> in order) as a point goal at the cell's spot, at the
/// terrain's height plus <see cref="StandOffMetres"/>, through the travel seam with <see cref="TravelPurpose.Cell"/>: the same request the hunt's next cell makes (<c>HuntScenario.NextCell</c>), so a journey's seam walks it on
/// foot (the runner) and the default <see cref="PlaceTravel"/> places. A cell whose spot has no terrain height (water, steep, unread) is skipped, named.
/// </summary>
/// <remarks>
/// <para>Check, act, check (M5 D141): each leg logs where the player stands and the goal before the request, and on its arrival checks that the player is present on one of the ground's landblocks (a place leg's Arrived is only the
/// place's confirmation, so the leg waits for that check, as the hunt's does); a leg that fails, or does not end within the profile's <see cref="WalkModel.LegSeconds"/>, is abandoned and the walk goes on to the next cell
/// (<see cref="LegRecovery"/>, the count every ground walk shares). A cancelled leg (<see cref="Abandon"/>) logs its walking seconds, and every leg's seconds count in <see cref="WalkSecondsTotal"/> (<c>m6-walk-cancel-seconds</c>).</para>
/// <para>No physics: the walk has no line test of its own (<see cref="IFightWalk.HasClearLine"/> keeps its default, <c>TODO(m6-outdoor-line)</c>).</para>
/// <para>M6 6 B1 (the fight's walk seam): <see cref="BeginLegTo"/> is a point goal through the same travel seam (a failed spot leg ends Arrived, logged, and the tour is not told); <see cref="CellsOnLine"/> is the arithmetic of the
/// outdoor 8 x 8 cell grid along the line (no physics: there is no wall outdoors for it to meet, so a line is Traced inside one landblock and Stopped when it leaves it or an end is indoors); <see cref="FloorSpots"/> are the
/// profile's own encounter cells at their spots (the places the walk already goes to, with a terrain height), the smallest honest candidates for a short step: the open ground round the centre has no room graph to choose from
/// (<c>TODO(m6-outdoor-floor-spots)</c> if a step wants spots between the profile's cells).</para>
/// </remarks>
public sealed class OutdoorFightWalk : IFightWalk
{
    /// <summary>The height above the terrain of a cell's goal (<see cref="HuntGround.StopPoint"/>'s lift).</summary>
    public const float StandOffMetres = 0.5f;

    private const string LogCategory = "fight";

    private readonly HuntingGround _ground;
    private readonly IJourneyTravel _travel;
    private readonly Func<uint, float, float, float?> _height;
    private readonly ILogSink _log;
    private readonly OutdoorTour _tour;
    private readonly LegRecovery _recovery;
    private readonly LegPacing _pacing = new();
    private ITravelLeg? _leg;
    private uint _goal;
    private double _legStart;
    private bool _placeConfirmed;
    private bool _spot; // the leg under way is BeginLegTo's, not the tour's
    private IReadOnlyList<CellFrame>? _floorSpots;
    private readonly Func<uint, float, float, float?>? _terrain;

    /// <summary>The walk over <paramref name="ground"/>'s outdoor cells.</summary>
    /// <param name="ground">An outdoor ground with cells.</param>
    /// <param name="travel">The travel seam (the fight's own: <see cref="FightOptions.Travel"/>).</param>
    /// <param name="height">The terrain height at a spot (the cell id, x, y in its landblock), or null when it is water, steep or unread (<see cref="HuntGround.SeekHeight"/> over the dats).</param>
    /// <param name="log">Where the walk logs.</param>
    /// <param name="terrain">M6 7 AM: the raw terrain height at a spot (the cell id, x, y in its landblock; water and steep ground included, unlike <paramref name="height"/>), or null when the landblock is unread: the line test's ground. Null for the whole argument keeps the old answer, a clear line (<c>TODO(m6-outdoor-line)</c>).</param>
    public OutdoorFightWalk(HuntingGround ground, IJourneyTravel travel, Func<uint, float, float, float?> height, ILogSink log, Func<uint, float, float, float?>? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(travel);
        ArgumentNullException.ThrowIfNull(height);
        ArgumentNullException.ThrowIfNull(log);
        (_ground, _travel, _height, _log, _terrain) = (ground, travel, height, log, terrain);
        _tour = new OutdoorTour(ground);
        _recovery = new LegRecovery(log);
    }

    /// <summary>The walk with the dats' terrain heights (<see cref="HuntGround.SeekHeight"/>).</summary>
    /// <param name="dats">The dats.</param>
    /// <param name="ground">An outdoor ground with cells.</param>
    /// <param name="travel">The travel seam.</param>
    /// <param name="log">Where the walk logs.</param>
    public static OutdoorFightWalk Create(DatSet dats, HuntingGround ground, IJourneyTravel travel, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var samplers = new Dictionary<uint, TerrainSampler?>();
        float? Raw(uint cell, float x, float y)
        {
            var block = cell >> 16;
            if (!samplers.TryGetValue(block, out var sampler))
            {
                var terrain = dats.GetLandblockTerrain(LandblockCoord.FromId(cell));
                samplers[block] = sampler = terrain is null ? null : new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable);
            }

            return sampler is null || x < 0 || y < 0 || x >= 192 || y >= 192 ? null : sampler.GetHeight(x, y);
        }

        return new OutdoorFightWalk(ground, travel, (cell, x, y) => HuntGround.SeekHeight(dats, cell, x, y), log, Raw);
    }

    /// <summary>M6 7 AM: the metres between two samples of the line test's terrain walk.</summary>
    public const double LineStepMetres = 2.0;

    /// <summary>M6 7 AM: the metres at each end the line test leaves unjudged (the caster's and the target's own ground).</summary>
    public const double LineEndMetres = 2.0;

    /// <summary>
    /// M6 7 AM: how much room the line keeps over the ground, in metres: the bolt is a sphere (the sight sweep's 0.3 m, <c>TODO(m3-fight-los-radius)</c>) and a line that grazes the hill's back by less than that, plus 0.2 m for the
    /// 2 m sampling, is blocked. The live case's line passed 0.4 m over the hill at 30 m and the bolt died 44 m out (<c>OutdoorLineDatTests</c>).
    /// </summary>
    public const double TerrainClearanceMetres = 0.5;

    /// <summary>
    /// M6 7 AM: the height a bolt leaves from and aims at above the feet: ACE's <c>ProjHeight</c> (2/3, WorldObject_Magic.cs 1526 @95ef426d) of a 1.8 m stand-in body (<c>TODO(m3-fight-los-target-height)</c>, as <see cref="GameFightWalk.HasClearLine"/>).
    /// </summary>
    public const float BoltLiftMetres = 1.8f * 2f / 3f;

    /// <inheritdoc/>
    /// <remarks>
    /// M6 7 AM (live, stretch 3 of gate A: 11 of 20 bolts stopped short outdoors, each at a target 50 to 70 m away): the outdoor walk had no line test and answered true, so the pick took a target behind a hill. With the terrain
    /// it is the straight bolt line over the ground's own triangles, <see cref="ClearOverTerrain"/>. Trees, rocks and buildings are not in it (<c>TODO(m6-outdoor-line-objects)</c>): the bolt's wall hit and the fight's stay rule still meet those.
    /// </remarks>
    public bool HasClearLine(in CellFrame from, in CellFrame to) => _terrain is null || ClearOverTerrain(_terrain, in from, in to);

    /// <summary>
    /// True when no terrain triangle between <paramref name="from"/> and <paramref name="to"/> (both outdoors) rises above the straight line from the caster's bolt height to the target's, sampled every <see cref="LineStepMetres"/>
    /// and not within <see cref="LineEndMetres"/> of either end. An unread sample (<paramref name="terrain"/> null), an indoor end or the same spot is no evidence of a wall: clear.
    /// </summary>
    /// <param name="terrain">The raw terrain height at (cell id, x, y local to its landblock).</param>
    /// <param name="from">The caster's feet.</param>
    /// <param name="to">The target's feet.</param>
    public static bool ClearOverTerrain(Func<uint, float, float, float?> terrain, in CellFrame from, in CellFrame to)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (!from.IsOutdoor || !to.IsOutdoor)
        {
            return true;
        }

        var a = from.ToGlobal();
        var b = to.ToGlobal();
        var flat = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
        if (flat <= 2 * LineEndMetres)
        {
            return true;
        }

        var startZ = a.Z + BoltLiftMetres;
        var endZ = b.Z + BoltLiftMetres;
        for (var along = LineEndMetres; along <= flat - LineEndMetres; along += LineStepMetres)
        {
            var t = along / flat;
            var at = new GlobalPosition(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), 0);
            if (!AcCoordinates.TryGetOutdoorCell(at, out var cell) || terrain(cell.CellId, cell.Local.X, cell.Local.Y) is not { } ground)
            {
                continue;
            }

            if (ground + TerrainClearanceMetres > startZ + ((endZ - startZ) * t))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <summary>The legs begun so far, the recovery legs included.</summary>
    public int Legs { get; private set; }

    /// <inheritdoc/>
    public IGroundTour Tour => _tour;

    /// <inheritdoc/>
    public int? LegsRecovered => _recovery.Recovered;

    /// <inheritdoc/>
    public uint LegGoalCell => _leg is null ? 0 : _goal;

    /// <inheritdoc/>
    public double? WalkSecondsTotal => _pacing.TotalWalkSeconds;

    /// <inheritdoc/>
    public void Prepare(IGameSessionView session, double now)
    {
        // No body of its own: the travel seam's legs move the player.
    }

    /// <inheritdoc/>
    public bool BeginLeg(IGameSessionView session, double now, out string description)
    {
        ArgumentNullException.ThrowIfNull(session);
        EndLeg();
        _spot = false;
        var from = session.State.Self.Position?.CellId ?? 0;
        var skipped = 0;
        for (var tries = 0; tries < _tour.Waypoints; tries++)
        {
            if (_tour.Next(from, now) is not { } cell || _tour.CellOf(cell) is not { } spot)
            {
                break;
            }

            if (_height(cell, spot.Spot.X, spot.Spot.Y) is not { } height)
            {
                skipped++;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"outdoor walk: cell 0x{cell:X8} has no terrain height at its spot ({spot.Spot.X:0.#}, {spot.Spot.Y:0.#}) (water, steep or unread); skipped"));
                ((IGroundTour)_tour).LegFailed(cell, now);
                continue;
            }

            var point = new Vector3(spot.Spot.X, spot.Spot.Y, height + StandOffMetres);
            var name = string.Create(CultureInfo.InvariantCulture, $"outdoor cell 0x{cell:X8}");
            var destination = new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(cell, point), HasHeight: true, HeadingDegrees: 0), $"hunting ground '{_ground.Key}': {_tour.PurposeNote}");
            Legs++;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"outdoor walk: check before leg {Legs}: the player in 0x{from:X8}, travel {session.State.Self.Travel}; the goal 0x{cell:X8} at ({point.X:0.#}, {point.Y:0.#}, {point.Z:0.#}) ({_tour.PurposeNote})"));
            (_goal, _legStart, _placeConfirmed) = (cell, now, false);
            _pacing.BeginWalk(now);
            _leg = _travel.GoTo(session, new TravelRequest(destination, TravelPurpose.Cell), now);
            description = string.Create(CultureInfo.InvariantCulture, $"outdoor cell 0x{cell:X8} by {_leg.Means.ToString().ToLowerInvariant()} ({_tour.PurposeNote})");
            return true;
        }

        Failure = skipped > 0
            ? $"no outdoor cell of '{_ground.Key}' has a terrain height at its spot ({skipped} skipped; TODO(m6-outdoor-height))"
            : $"the outdoor cells of '{_ground.Key}' offer no waypoint besides the player's own cell 0x{from:X8}";
        description = Failure;
        return false;
    }

    /// <inheritdoc/>
    public bool BeginLegTo(IGameSessionView session, double now, in CellFrame goalIn, out string description)
    {
        ArgumentNullException.ThrowIfNull(session);
        var goal = goalIn;
        if (!goal.IsOutdoor || !_ground.InLandblocks(goal.CellId))
        {
            description = $"the spot's cell 0x{goal.CellId:X8} is not an outdoor cell of '{_ground.Key}'";
            return false;
        }

        EndLeg();
        if (float.IsNaN(goal.Origin.Z))
        {
            // M6 7 AM: a spot with no height asked for (the stay rule's, planned from two positions) stands on the terrain, as the tour's cells do; water or steep ground has no spot
            if (_height(goal.CellId, goal.Origin.X, goal.Origin.Y) is not { } floor)
            {
                description = $"the spot at ({goal.Origin.X:0.#}, {goal.Origin.Y:0.#}) in 0x{goal.CellId:X8} has no terrain height (water, steep or unread)";
                return false;
            }

            goal = new CellFrame(goal.CellId, goal.Frame with { Origin = new Vector3(goal.Origin.X, goal.Origin.Y, floor + StandOffMetres) });
        }

        var from = session.State.Self.Position?.CellId ?? 0;
        var name = string.Create(CultureInfo.InvariantCulture, $"outdoor spot in 0x{goal.CellId:X8}");
        var destination = new TeleportDestination(name, [], new WorldLocation(name, new CellPosition(goal.CellId, goal.Origin), HasHeight: true, HeadingDegrees: 0), $"hunting ground '{_ground.Key}': the fight's own spot");
        Legs++;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"outdoor walk: check before leg {Legs}: the player in 0x{from:X8}, travel {session.State.Self.Travel}; the spot 0x{goal.CellId:X8} at ({goal.Origin.X:0.#}, {goal.Origin.Y:0.#}, {goal.Origin.Z:0.#}) (the fight's own)"));
        (_goal, _legStart, _placeConfirmed, _spot) = (goal.CellId, now, false, true);
        _pacing.BeginWalk(now);
        _leg = _travel.GoTo(session, new TravelRequest(destination, TravelPurpose.Cell), now);
        description = string.Create(CultureInfo.InvariantCulture, $"leg {Legs} to the spot in 0x{goal.CellId:X8} by {_leg.Means.ToString().ToLowerInvariant()}");
        return true;
    }

    /// <inheritdoc/>
    public LineTrace CellsOnLine(in CellFrame from, in CellFrame to, List<uint> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        cells.Clear();
        cells.Add(from.CellId);
        if (!from.IsOutdoor || !to.IsOutdoor || from.CellId >> 16 != to.CellId >> 16)
        {
            return LineTrace.Stopped; // an indoor end or another landblock: the cells past the start are not known (TODO(ground-multi-landblock))
        }

        var length = Vector3.Distance(from.Origin, to.Origin);
        var hops = Math.Max(1, (int)Math.Ceiling(length / OutdoorCellMetres * 4));
        for (var i = 1; i <= hops; i++)
        {
            var at = Vector3.Lerp(from.Origin, to.Origin, (float)i / hops);
            var cell = (from.CellId & 0xFFFF0000u) | (uint)(1 + (Math.Clamp((int)(at.X / OutdoorCellMetres), 0, 7) * 8) + Math.Clamp((int)(at.Y / OutdoorCellMetres), 0, 7));
            if (cells[^1] != cell)
            {
                cells.Add(cell);
            }
        }

        return LineTrace.Traced;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CellFrame> FloorSpots => _floorSpots ??= BuildFloorSpots();

    private IReadOnlyList<CellFrame> BuildFloorSpots()
    {
        var spots = new List<CellFrame>();
        foreach (var cell in _ground.Outdoor!.Cells)
        {
            if (_height(cell.Cell, cell.Spot.X, cell.Spot.Y) is { } height)
            {
                spots.Add(new CellFrame(cell.Cell, new AcFrame(new Vector3(cell.Spot.X, cell.Spot.Y, height + StandOffMetres), Quaternion.Identity)));
            }
        }

        return spots;
    }

    /// <summary>The side of an outdoor cell.</summary>
    private const float OutdoorCellMetres = 24f;

    /// <inheritdoc/>
    public FightWalkStatus Tick(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_leg is not { } leg)
        {
            return FightWalkStatus.Idle;
        }

        var status = leg.Step(session, now);
        _pacing.Walked(leg.Means == TravelMeans.Run && status == TravelStatus.Running, now);
        if (status == TravelStatus.Failed)
        {
            return LegFailed(session, now, leg.Failure ?? "travel:" + leg.Means.ToString().ToLowerInvariant() + ": failed with no reason (TODO(m4-travel-failure-unnamed))");
        }

        if (status == TravelStatus.Arrived && leg.Means != TravelMeans.Place)
        {
            return Arrive(session, now, leg.Means, leg.Fields);
        }

        _placeConfirmed |= status == TravelStatus.Arrived; // a place's Arrived is its confirmation: the check below decides
        if (_placeConfirmed && session.State.Self.Travel == SelfTravel.Present && session.State.Self.Position is { } at && at.CellId >> 16 == _goal >> 16)
        {
            return Arrive(session, now, TravelMeans.Place, leg.Fields);
        }

        if (now - _legStart > _ground.Walk.LegSeconds)
        {
            leg.Pause();
            return LegFailed(session, now, string.Create(CultureInfo.InvariantCulture, $"the leg did not finish within {_ground.Walk.LegSeconds:0.#} s"));
        }

        return FightWalkStatus.Walking;
    }

    /// <inheritdoc/>
    public void Abandon()
    {
        if (_leg is not null)
        {
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"leg {Legs} to outdoor cell 0x{_goal:X8} cancelled after walk_s={_pacing.WalkSeconds:0.0} (counted in the walk's total {_pacing.TotalWalkSeconds:0.0} s: m6-walk-cancel-seconds)"));
            _leg.Pause();
        }

        EndLeg();
        _pacing.Stop();
    }

    /// <inheritdoc/>
    public void Dispose() => EndLeg();

    private FightWalkStatus Arrive(IGameSessionView session, double now, TravelMeans means, string fields)
    {
        var at = session.State.Self.Position;
        if (session.State.Self.Travel != SelfTravel.Present || at is not { } here || !_ground.InLandblocks(here.CellId))
        {
            return LegFailed(session, now, $"check after: the travel arrived by {means.ToString().ToLowerInvariant()} but the player is not present on the ground (travel {session.State.Self.Travel}, cell {(at is { } a ? "0x" + a.CellId.ToString("X8", CultureInfo.InvariantCulture) : "unknown")})");
        }

        if (!_spot)
        {
            ((IGroundTour)_tour).LegWalked(_goal, now, _pacing.WalkSeconds);
        }

        _recovery.Arrived();
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"leg {Legs} arrived at outdoor cell 0x{_goal:X8} by {means.ToString().ToLowerInvariant()}: check after: the player in 0x{here.CellId:X8} walk_s={_pacing.WalkSeconds:0.0}{(fields.Length == 0 ? string.Empty : " " + fields)}"));
        EndLeg();
        return FightWalkStatus.Arrived;
    }

    private FightWalkStatus LegFailed(IGameSessionView session, double now, string reason)
    {
        var goal = _goal;
        EndLeg();
        if (_spot)
        {
            // BeginLegTo's contract: a failed spot leg ends Arrived, logged; the fight reads where the player stands and the tour is not told.
            _spot = false;
            _pacing.Stop();
            _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"outdoor walk: the leg {Legs} to the spot in 0x{goal:X8} failed ({reason}); ended Arrived without recovery onto the tour"));
            return FightWalkStatus.Arrived;
        }

        ((IGroundTour)_tour).LegFailed(goal, now);
        if (_recovery.Failed(Legs, goal, reason) is { } ended)
        {
            Failure = ended;
            return FightWalkStatus.Failed;
        }

        if (!BeginLeg(session, now, out var next))
        {
            Failure = $"leg {Legs} failed ({reason}) and no leg to the next outdoor cell could begin: " + Failure;
            return FightWalkStatus.Failed;
        }

        _log.Info(LogCategory, "recovered: " + next);
        return FightWalkStatus.Walking;
    }

    private void EndLeg()
    {
        _leg?.Dispose();
        _leg = null;
    }
}
