using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Data.Physics;

namespace Lifestone.Client.Movement.Routes;

/// <summary>
/// M6 14 holding plates (the owner, 2026-11-14: where a plate blocks the way, she can jump): the standing long jumps over the pressure plates a walk keeps off (<see cref="PlateKeepOff"/>), derived from the plates' places, the rooms'
/// floors and ceilings and her live flight and speed; nothing of it is a profile number. Each is a one-way <see cref="PitJump"/> with <see cref="PitJump.Standing"/> set, an edge of the room graph from the take-off's room to the
/// landing's (<see cref="RoomGraph.PlateJumpsFrom"/>), walked by the runner's jump (<c>NavRunner.Jumps</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a standing jump.</b> A plate fires on the server's sweep of her body, not her own (ACE @95ef426d, <see cref="PlateKeepOff"/>'s remarks), and ACE flies a jump from where it last knew her: the Jump message carries no
/// position (<c>JumpPack</c>), <c>HandleActionJump</c> (Player.cs:866-935) sets the velocity on the server's body, and a non-PK player's body moves on the server only by her reports (<c>FastTick</c> is <c>IsPKType</c>,
/// Player_Tick.cs:154), which go out on a key change or once a second while she stands on the ground (<c>CommandInterpreter::ShouldSendPositionEvent</c>, 11.6096 0x006B5520). A running jump leaves the server up to a second of
/// her run behind (7.7 m at the climber's speed): its flight lands short of hers and its sweep to her landing crosses the plate. Retail's standing long jump takes that away: she stands at the take-off, so every report says
/// where she is, charges the bar standing still (<c>CMotionInterp::charge_jump</c>, 11.6096 0x00528DD0), and holds run forward, which the motion keeps in its interpreted state without moving her
/// (<c>DoInterpretedMotion</c> with the standing long jump); the release leaves with the run's speed (<c>get_leave_ground_velocity</c>, 0x00528CD0, reads the state velocity), so the flight is her running jump's at the bar's
/// level (<see cref="JumpReach"/>) and the server's copy starts where hers does.</para>
/// <para><b>The line.</b> For each group of plates whose walls leave no way between them (<see cref="Groups"/>) and each axis through its middle (the dungeon's corridors run on the axes), the take-off stands on the line short of
/// every wall the line crosses and the landing past every one, each by <see cref="MarginMetres"/>; a plate within its touch of the line must also lie past the arc's low ends (<see cref="LowEndMetres"/>). The line from the middle
/// to each end runs on the floors of the walk's rooms through doorways along it (<see cref="LineStepMetres"/>), within the step-up of the plates' floor.</para>
/// <para><b>The bar.</b> A full bar's arc (2.72 m over the floor at Jump 178) puts her head into the Holding's 3.7 m corridor ceilings (live on the dats: the jump bonked and came down among the plates), so the bar is the highest
/// whose arc, <see cref="BarSlackLevel"/> higher (a late frame's overshoot), keeps her head (<see cref="BodyHeightMetres"/>) <see cref="HeadMarginMetres"/> under the rooms' volumes along the line, less that slack; its flight must
/// reach the landing (<see cref="PitJump.NeedMetres"/>). The height of a jump is linear in the bar (<c>GetJumpHeight</c>, 11.6096 0x006B1880, at least 0.35 m), its flight goes with the root of the height. Not retail: the thresholds
/// are Lifestone's own.</para>
/// </remarks>
public static class PlateJumps
{
    /// <summary>The margin past the walls, in metres. Not retail: Lifestone's own.</summary>
    public const float MarginMetres = 0.3f;

    /// <summary>The step the line and the arc are read at, in metres. Not retail: Lifestone's own.</summary>
    public const float LineStepMetres = 0.25f;

    /// <summary>A doorway the line crosses has its centre within this of the line, in metres (the corridors' doorways are centred on them). Not retail: Lifestone's own.</summary>
    public const float DoorwayAsideMetres = 1.0f;

    /// <summary>Her height (Setup 0x02000001's height, 1.835 m, over her upper sphere's top at 1.35 + 0.48 m; the dat test reads it).</summary>
    public const float BodyHeightMetres = 1.835f;

    /// <summary>How far under a ceiling her head stays at the top of the arc, in metres. Not retail: Lifestone's own.</summary>
    public const float HeadMarginMetres = 0.1f;

    /// <summary>How far past the planned level the bar may stand at the release, a late frame's fill (80 ms of the bar's second). Not retail: Lifestone's own.</summary>
    public const float BarSlackLevel = 0.08f;

    /// <summary>The bar's step when the highest arc under the ceilings is sought. Not retail: Lifestone's own.</summary>
    private const float BarStep = 0.01f;

    private const float ProbeMetres = 0.5f;

    /// <summary>The least height of a jump (<c>GetJumpHeight</c>'s floor, 11.6096 0x006B1880).</summary>
    private const float LeastHeightMetres = 0.35f;

    private static readonly (Vector2 Direction, string Name)[] Axes = [(Vector2.UnitY, "north"), (-Vector2.UnitY, "south"), (Vector2.UnitX, "east"), (-Vector2.UnitX, "west")];

    /// <summary>The apex of a running jump of flight <paramref name="flight"/> at run speed <paramref name="runSpeed"/>, in metres: the vertical speed is <c>flight g / (2 v)</c> and its height <c>vz^2 / (2 g)</c>.</summary>
    /// <param name="flight">The level-ground flight, metres.</param>
    /// <param name="runSpeed">The run speed, metres a second.</param>
    public static float ApexMetres(float flight, float runSpeed) => runSpeed <= 0f ? 0f : flight * flight * -PhysicsGlobals.Gravity / (8f * runSpeed * runSpeed);

    /// <summary>How far from the take-off (and short of the landing) her feet stay under <paramref name="top"/>, in metres, on a parabola of <paramref name="flight"/> and <paramref name="apex"/>; infinite when the apex is not over it.</summary>
    /// <param name="flight">The flight, metres.</param>
    /// <param name="apex">The apex, metres.</param>
    /// <param name="top">The height to clear, metres.</param>
    public static float LowEndMetres(float flight, float apex, float top) =>
        top >= apex ? float.PositiveInfinity : flight * 0.5f * (1f - MathF.Sqrt(1f - (top / apex)));

    /// <summary>The apex at bar level <paramref name="bar"/> of a jump whose full bar's apex is <paramref name="fullApex"/>: linear in the bar, at least 0.35 m.</summary>
    /// <param name="fullApex">The full bar's apex, metres.</param>
    /// <param name="bar">The bar's level.</param>
    public static float ApexAt(float fullApex, float bar) => MathF.Max(LeastHeightMetres, fullApex * Math.Clamp(bar, 0f, 1f));

    /// <summary>The flight at bar level <paramref name="bar"/> of a jump whose full bar flies <paramref name="fullFlight"/> to <paramref name="fullApex"/>: with the root of the apex.</summary>
    /// <param name="fullFlight">The full bar's flight, metres.</param>
    /// <param name="fullApex">The full bar's apex, metres.</param>
    /// <param name="bar">The bar's level.</param>
    public static float FlightAt(float fullFlight, float fullApex, float bar) => fullApex <= 0f ? 0f : fullFlight * MathF.Sqrt(ApexAt(fullApex, bar) / fullApex);

    /// <summary>The plates in groups: two plates at a storey whose walls overlap or leave no body's width between them are one group (union of pairs).</summary>
    /// <param name="walls">The plates and their walls.</param>
    public static IReadOnlyList<IReadOnlyList<KeptPlate>> Groups(PlateKeepOff walls)
    {
        ArgumentNullException.ThrowIfNull(walls);
        var plates = walls.Plates;
        var parent = Enumerable.Range(0, plates.Count).ToArray();
        int Root(int i)
        {
            while (parent[i] != i)
            {
                i = parent[i] = parent[parent[i]];
            }

            return i;
        }

        for (var i = 0; i < plates.Count; i++)
        {
            for (var j = i + 1; j < plates.Count; j++)
            {
                var (a, b) = (plates[i], plates[j]);
                var apart = Vector2.Distance(new Vector2(a.Point.X, a.Point.Y), new Vector2(b.Point.X, b.Point.Y));
                if (MathF.Abs(a.Point.Z - b.Point.Z) <= PlateKeepOff.StoreyMetres && apart < walls.ReachOf(a) + walls.ReachOf(b) + (2f * PlateKeepOff.BodyRadiusMetres))
                {
                    parent[Root(i)] = Root(j);
                }
            }
        }

        return [.. Enumerable.Range(0, plates.Count).GroupBy(Root).OrderBy(g => g.Min()).Select(g => (IReadOnlyList<KeptPlate>)[.. g.Select(i => plates[i])])];
    }

    /// <summary>The group's name for the logs: its plate's own, or how many stand in its cell.</summary>
    /// <param name="group">A group of <see cref="Groups"/>.</param>
    public static string GroupName(IReadOnlyList<KeptPlate> group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.Count == 1 ? group[0].Name : string.Create(CultureInfo.InvariantCulture, $"the {group.Count} plates in 0x{group[0].Cell:X8}");
    }

    /// <summary>
    /// The standing jumps over <paramref name="walls"/> on the rooms of <paramref name="planner"/> for a body whose full-bar running jump flies <paramref name="flight"/> metres at <paramref name="runSpeed"/> metres a second (see the
    /// class's remarks); empty without plates or a flight.
    /// </summary>
    /// <param name="planner">The walk's rooms (the room graph's planner).</param>
    /// <param name="walls">The plates and their walls at her stop.</param>
    /// <param name="flight">Her full-bar running jump's level flight (<see cref="JumpReach"/>), metres.</param>
    /// <param name="runSpeed">Her run speed, metres a second.</param>
    public static IReadOnlyList<DirectedJump> Derive(CellRoutePlanner planner, PlateKeepOff walls, float flight, float runSpeed)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(walls);
        if (walls.Plates.Count == 0 || flight <= 0f || runSpeed <= 0f)
        {
            return [];
        }

        var apex = ApexMetres(flight, runSpeed);
        var found = new List<DirectedJump>();
        foreach (var group in Groups(walls))
        {
            var middle = group.Aggregate(Vector3.Zero, (s, p) => s + p.Point) / group.Count;
            var cell = group.Select(p => p.Cell).FirstOrDefault(c => planner.RoomFloorUnder(c, middle + new Vector3(0f, 0f, ProbeMetres)) is not null);
            if (cell == 0)
            {
                continue; // no room of the walk holds the group (the lower level's bridge)
            }

            foreach (var (d, direction) in Axes)
            {
                if (Over(planner, walls, group, cell, middle, d, flight, apex) is { } jump)
                {
                    var name = string.Create(CultureInfo.InvariantCulture, $"plates: {GroupName(group)} {direction}");
                    found.Add(new DirectedJump(jump with { Name = name }, jump.A, jump.B));
                }
            }
        }

        return found;
    }

    /// <summary>The jump over <paramref name="group"/> along <paramref name="d"/> (its name left to the caller), or null when none lands past its walls under the rooms' ceilings.</summary>
    private static PitJump? Over(CellRoutePlanner planner, PlateKeepOff walls, IReadOnlyList<KeptPlate> group, uint cell, Vector3 middle, Vector2 d, float flight, float apex)
    {
        // Where the line enters the walls it crosses and where it leaves them; the plates within touch of it also need the arc's low ends kept off them.
        var (enter, leave, crossed) = (float.PositiveInfinity, float.NegativeInfinity, false);
        var touched = new List<(float Along, float Half, float Top)>();
        foreach (var p in walls.Plates)
        {
            if (MathF.Abs(p.Point.Z - middle.Z) > PlateKeepOff.StoreyMetres)
            {
                continue;
            }

            var rel = new Vector2(p.Point.X - middle.X, p.Point.Y - middle.Y);
            var along = Vector2.Dot(rel, d);
            var aside = MathF.Abs((d.X * rel.Y) - (d.Y * rel.X));
            var reach = walls.ReachOf(p);
            if (aside >= reach || (!group.Contains(p) && MathF.Abs(along) > (2f * flight) + reach))
            {
                continue;
            }

            var half = MathF.Sqrt((reach * reach) - (aside * aside));
            if (group.Contains(p))
            {
                (enter, leave, crossed) = (MathF.Min(enter, along - half), MathF.Max(leave, along + half), true);
            }

            var touch = PlateKeepOff.TouchOf(p);
            if (aside < touch)
            {
                touched.Add((along, MathF.Sqrt((touch * touch) - (aside * aside)), p.TopMetres));
            }
        }

        if (!crossed)
        {
            return null;
        }

        var takeOff = enter - MarginMetres;
        var farEdge = leave;
        var need = farEdge + MarginMetres - takeOff;
        Vector3 On(float at) => new(middle.X + (d.X * at), middle.Y + (d.Y * at), middle.Z);
        if (Line(planner, cell, middle, On(takeOff), d) is not { Count: > 0 } back)
        {
            return null; // the take-off is off the floor (a wall, a pit, a stair)
        }

        var (fromCell, fromFloor) = (back[^1].Cell, back[^1].Floor);
        var t0 = On(takeOff) with { Z = fromFloor };
        if (MathF.Abs(fromFloor - middle.Z) > CellRoutePlanner.StepUpMetres || walls.Holding(t0) is not null || !Standable(planner, fromCell, t0))
        {
            return null; // off the plates' storey, in another group's wall, or too near a wall for her body
        }

        // The highest bar whose arc keeps her head under the rooms along the line (and lands on a floor), then that bar less a late frame's overshoot; a full arc that fits is a full bar (the bar stops at full).
        var room = 1f;
        List<(float Along, uint Cell, float Floor)>? arc = null;
        while (room > 0f)
        {
            var f = FlightAt(flight, apex, room);
            arc = Line(planner, fromCell, t0, On(takeOff + f), d);
            if (arc is not null && Fits(planner, arc, t0, d, f, ApexAt(apex, room)))
            {
                break;
            }

            room -= BarStep;
        }

        var bar = room >= 1f ? 1f : room - BarSlackLevel;
        if (arc is null || room <= 0f || bar <= 0f || ApexAt(apex, bar) <= LeastHeightMetres)
        {
            return null; // no arc under the ceilings
        }

        var low = FlightAt(flight, apex, bar);
        var lowApex = ApexAt(apex, bar);
        if (low < need)
        {
            return null; // the arc under the ceilings lands short of the walls' far edge
        }

        // She lands running and stops within her stop (the runner lets go at the landing): the floor goes on that far past the longest landing (a late frame's bar), and her body's radius more.
        var longest = FlightAt(flight, apex, MathF.Min(1f, bar + BarSlackLevel));
        if (Line(planner, fromCell, t0, On(takeOff + longest + walls.StopMetres + PlateKeepOff.BodyRadiusMetres), d) is null)
        {
            return null;
        }

        foreach (var t in touched)
        {
            var end = LowEndMetres(low, lowApex, t.Top) + t.Half;
            if (t.Along - takeOff < end || takeOff + low - t.Along < end)
            {
                return null; // a plate within touch of the line lies under the arc's low end
            }
        }

        var landing = arc.Where(s => s.Along <= low).DefaultIfEmpty(arc[0]).Last();
        var l = On(takeOff + low) with { Z = landing.Floor };
        if (MathF.Abs(landing.Floor - middle.Z) > CellRoutePlanner.StepUpMetres || walls.Holding(l) is not null || !Standable(planner, landing.Cell, l))
        {
            return null;
        }

        var a = new PitJumpSide(fromCell, t0, t0, t0);
        var b = new PitJumpSide(landing.Cell, On(farEdge) with { Z = middle.Z }, l, l);
        return new PitJump(string.Empty, 0, farEdge - takeOff, need, a, b) { Standing = true, Bar = bar };
    }

    /// <summary>
    /// True when her body fits standing at <paramref name="point"/>: the floor reaches <see cref="PlateKeepOff.BodyRadiusMetres"/> and a hand's breadth round it on the axes, in <paramref name="cell"/> or a room its walkable doorways
    /// open onto (a take-off by a doorway).
    /// </summary>
    private static bool Standable(CellRoutePlanner planner, uint cell, Vector3 point)
    {
        var cells = planner.Cells;
        if (cells.GetCell(cell) is not { } here)
        {
            return false;
        }

        var near = new List<uint> { cell };
        near.AddRange(here.Portals.Where(p => !p.LeadsOutside && cells.GetCell(p.OtherCellId) is not null && planner.IsWalkable(here, p)).Select(p => p.OtherCellId));
        const float r = PlateKeepOff.BodyRadiusMetres + 0.05f;
        foreach (var offset in (ReadOnlySpan<Vector3>)[new(r, 0f, 0f), new(-r, 0f, 0f), new(0f, r, 0f), new(0f, -r, 0f)])
        {
            var probe = point + offset + new Vector3(0f, 0f, ProbeMetres);
            if (!near.Exists(c => planner.RoomFloorUnder(c, probe) is { } z && MathF.Abs(z - point.Z) <= CellRoutePlanner.StepUpMetres))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when the arc of flight <paramref name="f"/> and apex <paramref name="h"/> from <paramref name="t0"/> along <paramref name="d"/> keeps her head <see cref="HeadMarginMetres"/> inside the rooms of <paramref name="path"/>.</summary>
    private static bool Fits(CellRoutePlanner planner, List<(float Along, uint Cell, float Floor)> path, Vector3 t0, Vector2 d, float f, float h)
    {
        var cells = planner.Cells;
        var i = 0;
        for (var x = 0f; x <= f; x += LineStepMetres)
        {
            while (i + 1 < path.Count && path[i + 1].Along <= x)
            {
                i++;
            }

            var feet = 4f * h * x * (f - x) / (f * f);
            var head = new Vector3(t0.X + (d.X * x), t0.Y + (d.Y * x), t0.Z + feet + BodyHeightMetres + HeadMarginMetres);
            var inside = false;
            for (var k = Math.Max(0, i - 1); k <= Math.Min(path.Count - 1, i + 1) && !inside; k++)
            {
                inside = cells.GetCell(path[k].Cell) is { } c && cells.Contains(c, head);
            }

            if (!inside)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The rooms along the straight line from <paramref name="from"/> in <paramref name="start"/> to <paramref name="to"/> on the walk's floors, as (distance from <paramref name="from"/>, room, floor height) at each step: each step on the
    /// current room's floor, or across one of its walkable doorways that faces along the line within <see cref="DoorwayAsideMetres"/> of it onto the next room's floor, within the step-up; null when the line leaves the floors.
    /// </summary>
    private static List<(float Along, uint Cell, float Floor)>? Line(CellRoutePlanner planner, uint start, Vector3 from, Vector3 to, Vector2 d)
    {
        if (planner.RoomFloorUnder(start, from + new Vector3(0f, 0f, ProbeMetres)) is not { } z)
        {
            return null;
        }

        var current = start;
        var length = Vector2.Distance(new Vector2(from.X, from.Y), new Vector2(to.X, to.Y));
        var steps = Math.Max(1, (int)MathF.Ceiling(length / LineStepMetres));
        var cells = planner.Cells;
        var path = new List<(float Along, uint Cell, float Floor)> { (0f, current, z) };
        for (var i = 1; i <= steps; i++)
        {
            var p = Vector3.Lerp(from, to, (float)i / steps) with { Z = z + ProbeMetres };
            var along = length * i / steps;
            if (planner.RoomFloorUnder(current, p) is { } here && MathF.Abs(here - z) <= CellRoutePlanner.StepUpMetres)
            {
                z = here;
                path.Add((along, current, z));
                continue;
            }

            var moved = false;
            var cell = cells.GetCell(current)!;
            foreach (var portal in cell.Portals)
            {
                if (portal.LeadsOutside || cells.GetCell(portal.OtherCellId) is null || !planner.IsWalkable(cell, portal) || planner.DoorwayOf(cell, portal) is not { } door)
                {
                    continue;
                }

                var rel = new Vector2(door.Centre.X - from.X, door.Centre.Y - from.Y);
                if (MathF.Abs(Vector2.Dot(new Vector2(door.Outward.X, door.Outward.Y), d)) < 0.9f || MathF.Abs((d.X * rel.Y) - (d.Y * rel.X)) > DoorwayAsideMetres)
                {
                    continue;
                }

                if (planner.RoomFloorUnder(portal.OtherCellId, p) is { } there && MathF.Abs(there - z) <= CellRoutePlanner.StepUpMetres)
                {
                    (current, z, moved) = (portal.OtherCellId, there, true);
                    break;
                }
            }

            if (!moved)
            {
                return null;
            }

            path.Add((along, current, z));
        }

        return path;
    }
}
