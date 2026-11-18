using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The object a walk ends by Using (a portal hop's walk to the front and its seek; <see cref="NavRunner.UseTarget"/>): where the server's walk-to-use goes and how near it must bring her.
/// </summary>
/// <param name="Guid">The object's id (0 while the client has not found it yet: a hop's walk to the front).</param>
/// <param name="At">Where the server walks her: ACE's <c>MoveToPosition(target.Location)</c> for a portal (<c>Player_Move.cs</c> <c>CreateMoveToChain</c>).</param>
/// <param name="UseRadius">How near the object the server's chain must bring her before the Use acts, in metres.</param>
public sealed record NavUseTarget(uint Guid, CellFrame At, float UseRadius);

/// <summary>
/// The hub walk's hand-over (the owner's go-ahead of 2026-11-18: hand a portal walk to the game's own walk once the way is clear): ACE answers a Use from beyond the use radius with its own walk (<c>CreateMoveToChain</c>,
/// <c>Player_Move.cs</c>: <c>MoveToPosition(target.Location)</c> for a portal, <c>MoveToObject</c> for anything else), which the client runs straight to the target, the server checking the use radius every
/// 0.1 s. As a player clicks, the walk hands over to it as soon as that straight line is one the body runs: <see cref="Refusal"/> names what stands in the way. Not retail: retail has no automation.
/// </summary>
public static class UseHandOver
{
    /// <summary>The spacing of the floor samples along the line, in metres (the creature layer's and the indoor line's sample). Not retail: Lifestone's own.</summary>
    public const float SampleMetres = NavCreatures.SampleMetres;

    /// <summary>How far the body moves between two checks of the line, in metres (a check sweeps the physics): a few a second at a run. Not retail: Lifestone's own.</summary>
    public const float CheckEveryMetres = 1f;

    /// <summary>
    /// Why the straight line from the body to the target is not one to hand over on, or null when it is. Cheapest first: the server's walk must end inside ACE's MoveTo timeout at her speed
    /// (<see cref="NavRunner.YieldLimitSeconds"/>); no hostile creature's awareness disc touches the line or her (<paramref name="creatureNear"/>); the line meets no other portal's keep-off disc at its storey
    /// (the own portal, <see cref="KeptPortal.Own"/>, is the line's end) and no plate's wall; no closed door stands on it; it stays on one floor, every <see cref="SampleMetres"/> a floor within a step
    /// (<see cref="CellRoutePlanner.StepUpMetres"/>) of the last and out of the statics (<paramref name="floorAt"/>), its ends within <see cref="NavRunner.ArrivalHeightMetres"/> of each other; and the body,
    /// swept at its width through the physics, meets no wall or door frame (<paramref name="bodyClear"/>, asked last: the dearest).
    /// </summary>
    /// <param name="from">The body, global metres.</param>
    /// <param name="fromZ">Its height.</param>
    /// <param name="to">The target, global metres.</param>
    /// <param name="toZ">Its height.</param>
    /// <param name="runSpeed">Her run speed, metres a second (NaN: not known, no limit by time).</param>
    /// <param name="creatureNear">True when a hostile creature's awareness reaches her or the line.</param>
    /// <param name="keep">The portals the walk keeps off, or null.</param>
    /// <param name="plate">True when the line meets a plate's wall (null: none).</param>
    /// <param name="closedDoor">A closed door on the line, or null.</param>
    /// <param name="floorAt">The floor under a global point near a height, or null where no room holds it or a static stands there.</param>
    /// <param name="bodyClear">True when the body swept along the line meets nothing.</param>
    public static string? Refusal(Vector2 from, float fromZ, Vector2 to, float toZ, float runSpeed, bool creatureNear, PortalKeepOff? keep, Func<bool>? plate, uint? closedDoor, Func<Vector2, float, float?> floorAt, Func<bool> bodyClear)
    {
        ArgumentNullException.ThrowIfNull(floorAt);
        ArgumentNullException.ThrowIfNull(bodyClear);
        var length = Vector2.Distance(from, to);
        if (!float.IsNaN(runSpeed) && runSpeed > 0f && length / runSpeed >= NavRunner.YieldLimitSeconds)
        {
            return "too-far";
        }

        if (creatureNear)
        {
            return "creature";
        }

        if (keep is not null)
        {
            foreach (var portal in keep.Portals)
            {
                if (!portal.Own && MathF.Abs(portal.Z - fromZ) <= PortalKeepOff.StoreyMetres && SegmentDistance(portal.Global, from, to) < keep.ClearanceOf(portal))
                {
                    return "other-portal:" + portal.Name.Replace(' ', '_');
                }
            }
        }

        if (plate?.Invoke() == true)
        {
            return "plate";
        }

        if (closedDoor is { } door)
        {
            return string.Create(CultureInfo.InvariantCulture, $"closed-door:0x{door:X8}");
        }

        if (MathF.Abs(toZ - fromZ) > NavRunner.ArrivalHeightMetres)
        {
            return "level-change";
        }

        var samples = Math.Max(1, (int)MathF.Ceiling(length / SampleMetres));
        var z = fromZ;
        for (var i = 1; i <= samples; i++)
        {
            if (floorAt(Vector2.Lerp(from, to, (float)i / samples), z) is not { } floor)
            {
                return "off-floor";
            }

            if (MathF.Abs(floor - z) > CellRoutePlanner.StepUpMetres)
            {
                return "level-change";
            }

            z = floor;
        }

        return bodyClear() ? null : "walls";
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }
}

/// <summary>The hub walk's hand-over to the server's walk-to-use (<see cref="UseHandOver"/>).</summary>
public sealed partial class NavRunner
{
    private Vector2 _handOverCheckedAt = new(float.NaN, float.NaN);

    /// <summary>The object this walk ends by Using, or null: with one, the run ends <see cref="NavRunState.Arrived"/> with <see cref="HandedToUse"/> as soon as the straight line to it is clear (<see cref="UseHandOver"/>), for the caller to send the Use.</summary>
    public NavUseTarget? UseTarget { get; set; }

    /// <summary>True when the run ended by handing over to the server's walk-to-use: the caller sends the Use now.</summary>
    public bool HandedToUse { get; private set; }

    /// <summary>The last check's refusal (<see cref="UseHandOver.Refusal"/>), or null: report-only.</summary>
    public string? HandOverRefusal { get; private set; }

    /// <summary>
    /// Ends the run handing over to the server's walk when the line to <see cref="UseTarget"/> is clear, checked each <see cref="UseHandOver.CheckEveryMetres"/> the body moves. Indoors over the rooms with the physics
    /// to sweep: outdoors, with no rooms or no confirmer, the walk goes on as before. Not during a rung, a jump or a door's step.
    /// </summary>
    private bool TryHandOver(double now, in CellFrame position, NavPoint here)
    {
        if (UseTarget is not { } use || _rung != Rung.None || _jumpRun is not null || _confirmer is null || _rooms is not { } rooms || LandDefs.IsOutdoorCell(position.CellId) || LandDefs.IsOutdoorCell(use.At.CellId)
            || (position.CellId >> 16) != (use.At.CellId >> 16) || (!float.IsNaN(_handOverCheckedAt.X) && Vector2.Distance(_handOverCheckedAt, here.Global) < UseHandOver.CheckEveryMetres))
        {
            return false;
        }

        _handOverCheckedAt = here.Global;
        return HandOverNow(now, position, here, use, rooms);
    }

    /// <summary><see cref="TryHandOver"/>'s check of the line, apart from its gate: its closures are made only when a check is due (a warm step allocates nothing).</summary>
    private bool HandOverNow(double now, in CellFrame position, NavPoint here, NavUseTarget use, RoomGraph rooms)
    {
        var target = NavMap.FromCellFrame(use.At);
        var distance = here.HorizontalDistance(target);
        if (distance <= use.UseRadius)
        {
            return false; // within reach already: the walk's own end
        }

        var origin = NavPoint.BlockOrigin(position.CellId & 0xFFFF0000u);
        var world = _confirmer!.World;
        var lift = new Vector3(0f, 0f, IndoorLineLiftMetres);
        var from = new CellFrame(position.CellId, new AcFrame(position.Origin + lift, Quaternion.Identity));
        var to = new CellFrame(use.At.CellId, new AcFrame(use.At.Origin + lift, Quaternion.Identity));
        var hint = position.CellId;
        float? FloorAt(Vector2 global, float z)
        {
            var at = new Vector3(global.X - origin.X, global.Y - origin.Y, z);
            if (rooms.Cells.FindCell(at with { Z = z + CurveCellLiftMetres }, hint) is not { } cell || rooms.RoomFloorUnder(cell.Id, at with { Z = z + CellRoutePlanner.StepUpMetres }) is not { } floor
                || rooms.Planner.InStatic(cell, at with { Z = floor }))
            {
                return null;
            }

            hint = cell.Id;
            return floor;
        }

        var a = new Vector3(position.Origin.X, position.Origin.Y, position.Origin.Z);
        var b = use.At.Origin;
        var why = UseHandOver.Refusal(
            here.Global,
            here.Z,
            target.Global,
            target.Z,
            _body.RunSpeed,
            Creatures.Count > 0 && Creatures.ExposureAlong(here.Global, here.Z, target.Global, target.Z, use.Guid) > 0f,
            _keep ?? KeepOff,
            () => rooms.MeetsPlate(a, b),
            ClosedDoorBetween(position, use.At),
            FloorAt,
            () => !world.SweepViewer(from, to, NavCreatures.PlayerRadiusMetres).Blocked);
        HandOverRefusal = why;
        if (why is not null)
        {
            return false;
        }

        // The line's clearance for the log: the widest of the body, a quarter metre and a half metre more that the sweep still passes.
        var clear = NavCreatures.PlayerRadiusMetres;
        foreach (var more in (ReadOnlySpan<float>)[0.25f, 0.5f])
        {
            if (world.SweepViewer(from, to, NavCreatures.PlayerRadiusMetres + more).Blocked)
            {
                break;
            }

            clear = NavCreatures.PlayerRadiusMetres + more;
        }

        HandedToUse = true;
        _log.Info(
            LogCategory,
            "handing over to the game's walk: the line to the target is clear",
            new LogField("name", Options.Name),
            new LogField("target", string.Create(CultureInfo.InvariantCulture, $"0x{use.Guid:X8}")),
            new LogField("distance_m", distance.ToString("F1", CultureInfo.InvariantCulture)),
            new LogField("clear_m", clear.ToString("F2", CultureInfo.InvariantCulture)),
            new LogField("why", "a straight line on one floor, the body's width clear of walls and door frames, no other portal's disc, no plate, no closed door, no hostile creature near"),
            new LogField("at", position.ToString()));
        End(NavRunState.Arrived, null, now);
        return true;
    }
}
