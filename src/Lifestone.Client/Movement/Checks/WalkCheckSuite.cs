using System.Globalization;
using Lifestone.Client.Movement.Routes;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Checks;

/// <summary>
/// The offline player's walk checks (<c>--offline-player-checks</c>), one after the other through the real controller: a lap of Holtburg's ring, a standing and
/// a running jump, a walk into a wall, stairs up into a building and back out, a cellar's stairs down and back up, the last two also in first person.
/// The suite is the same code in the explore mode (<c>PlayerWalkChecks</c>) and in the headless tests (<see cref="IWalkHost"/>).
/// </summary>
/// <remarks>
/// <para><b>Where.</b> Holtburg, landblock 0xA9B4, from the town's start (the lifestone). The doorways are cells of the end-of-retail dats: the stairs up are
/// the shop-side building with doorway cell 0xA9B40179 (its sill is 2 m above the street) into room 0xA9B4017A; the cellar's door is cell
/// 0xA9B40177, its stairs go down into room 0xA9B40176; the wall is the west wall of the shop of doorway cell 0xA9B40165.</para>
/// <para><b>Planning.</b> <see cref="Plan"/> builds the routes (the ring is validated against the collision, which takes a few frames' time), so a host
/// plans before it starts counting frame times.</para>
/// </remarks>
public sealed class WalkCheckSuite
{
    /// <summary>The doorway cell of the stairs up and the room they lead to.</summary>
    public const uint StairsDoorCell = 0xA9B40179;

    /// <summary>The room up the stairs.</summary>
    public const uint StairsRoomCell = 0xA9B4017A;

    /// <summary>The cellar's doorway cell.</summary>
    public const uint CellarDoorCell = 0xA9B40177;

    /// <summary>The cellar room.</summary>
    public const uint CellarRoomCell = 0xA9B40176;

    /// <summary>The shop's doorway cell whose wall the player runs into.</summary>
    public const uint WallDoorCell = 0xA9B40165;

    private readonly IWalkHost _host;
    private readonly List<WalkScenario> _scenarios;
    private readonly List<WalkVerdict> _verdicts = [];
    private int _index;

    private WalkCheckSuite(IWalkHost host, List<WalkScenario> scenarios)
    {
        _host = host;
        _scenarios = scenarios;
    }

    /// <summary>The scenarios, in the order they run.</summary>
    public IReadOnlyList<WalkScenario> Scenarios => _scenarios;

    /// <summary>The verdicts so far, in order.</summary>
    public IReadOnlyList<WalkVerdict> Verdicts => _verdicts;

    /// <summary>True when every scenario has a verdict.</summary>
    public bool Done => _index >= _scenarios.Count;

    /// <summary>The scenario running, or null when done.</summary>
    public WalkScenario? Current => Done ? null : _scenarios[_index];

    /// <summary>
    /// Plans the suite: the Holtburg ring around the player's place (<see cref="RouteLibrary.HoltburgLaps"/>, one lap), the building routes and the wall.
    /// The player stands at the town's start.
    /// </summary>
    /// <exception cref="InvalidOperationException">A route cannot be planned (the collision or the cells are not as expected); the message says which.</exception>
    public static WalkCheckSuite Plan(IWalkHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var controller = host.Controller;
        var world = host.World;
        var cells = host.Cells;
        var ring = RouteLibrary.HoltburgLaps(controller.Position, world, controller.Player.Setup!, laps: 1);
        var start = ring.Start!.Value;

        // The running jump: the first four points of the ring at a run, a stop at the fourth.
        var leg = ring.Goals.Take(4).ToList();
        leg[^1] = leg[^1] with { Tolerance = RouteLibrary.StopTolerance, Stop = true, DwellSeconds = RouteLibrary.DwellSeconds };
        var runLeg = new ScriptedRoute("holtburg-run-jump", leg) { Start = start };

        var stairs = BuildingRoutes.InAndOut("holtburg-stairs", cells, world, StairsDoorCell, StairsRoomCell, startMetres: 7.0f);
        var cellar = BuildingRoutes.InAndOut("holtburg-cellar", cells, world, CellarDoorCell, CellarRoomCell);
        var wall = BuildingRoutes.WallBump("holtburg-wall", cells, world, WallDoorCell);
        var scenarios = new List<WalkScenario>
        {
            new RouteWalk("holtburg-lap", ring, RouteStatus.Done) { Expect = Lap },
            new StandingJump("jump-standing", start),
            new RunningJump("jump-running", runLeg),
            new RouteWalk("wall-bump", wall, RouteStatus.Stuck, 40.0) { Expect = Wall },
            new RouteWalk("stairs-up-chase", stairs, RouteStatus.Done) { Expect = Stairs },
            new RouteWalk("stairs-up-first-person", stairs, RouteStatus.Done) { Expect = Stairs, InFirstPerson = true },
            new RouteWalk("cellar-down-chase", cellar, RouteStatus.Done) { Expect = Cellar },
            new RouteWalk("cellar-down-first-person", cellar, RouteStatus.Done) { Expect = Cellar, InFirstPerson = true },
        };
        return new WalkCheckSuite(host, scenarios);
    }

    /// <summary>One frame of the suite (after the controller's step): ticks the running scenario, and starts the next when it ends.</summary>
    public void Tick(double now)
    {
        if (Done)
        {
            return;
        }

        var scenario = _scenarios[_index];
        scenario.Tick(_host, now);
        if (scenario.Finished)
        {
            _verdicts.Add(scenario.Verdict);
            _index++;
        }
    }

    private static void Lap(RouteWalk walk, IWalkHost host, List<string> facts, List<string> problems)
    {
        var start = walk.Route.Start!.Value;
        if (walk.FramesOffGround() > 0)
        {
            problems.Add($"{walk.FramesOffGround()} frames off the ground on the level ring");
        }

        if (walk.Stops.Count != 1)
        {
            problems.Add($"{walk.Stops.Count} stops reached (one expected)");
        }
        else if (walk.Stops[0].Position.GetOffset(start).Length() > 2.0f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the lap stopped {walk.Stops[0].Position.GetOffset(start).Length():F2} m from its start"));
        }

        // Every key change restarts the position timer (a MoveToState), so a steering walk sends fewer AutonomousPositions than seconds; the cadence audit
        // holds the gaps. Walking 20 m and more without one would show here too.
        if (host.Trace.Count(SentKind.AutonomousPosition, walk.FirstMessage) < 1)
        {
            problems.Add("the lap sent no AutonomousPosition");
        }
    }

    private static void Wall(RouteWalk walk, IWalkHost host, List<string> facts, List<string> problems)
    {
        if (walk.Samples.Count == 0)
        {
            return;
        }

        var begin = walk.Samples[0].Position;
        var end = walk.Samples[^1].Position;
        var goal = walk.Route.Goals[0].Target;
        var toGoal = end.GetOffset(goal);
        var travelled = end.GetOffset(begin).Length();
        facts.Add(string.Create(CultureInfo.InvariantCulture, $"stopped at ({end.Origin.X:F2}, {end.Origin.Y:F2}) in cell 0x{end.CellId:X8}, {toGoal.X:F2} m east and {toGoal.Y:F2} m north of the goal behind the wall; walked {travelled:F1} m"));
        if (end.IsEnvCell)
        {
            problems.Add($"the player went through the wall into cell 0x{end.CellId:X8}");
        }

        // The shop's west wall runs north to south: blocked means stopped well short of the goal behind it (east of the wall), slid means level with the
        // goal along the wall (the wall's normal is east-west, so a player that only stopped at the wall would be where it hit it).
        if (MathF.Abs(toGoal.X) < 3f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player stopped only {MathF.Abs(toGoal.X):F2} m short of the goal behind the wall (it should be stopped at the wall)"));
        }

        var slid = MathF.Abs(end.GetOffset(begin).Y);
        if (MathF.Abs(toGoal.Y) > 0.5f || slid < 1.0f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player did not slide along the wall to the point nearest the goal (slid {slid:F2} m, {toGoal.Y:F2} m off the goal along the wall)"));
        }

        if (walk.FramesOffGround() > 0)
        {
            problems.Add($"{walk.FramesOffGround()} frames off the ground while sliding along the wall");
        }
    }

    private static void Stairs(RouteWalk walk, IWalkHost host, List<string> facts, List<string> problems)
    {
        var heights = walk.Heights();
        var visited = walk.VisitedCells();
        facts.Add("cells " + string.Join(">", visited.Select(c => (c & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture))));
        if (heights.Max - heights.First < 1.0f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player climbed only {heights.Max - heights.First:F2} m (the stairs rise about 2 m)"));
        }

        InAndOutRules(StairsDoorCell, StairsRoomCell, visited, heights, facts, problems);
    }

    private static void Cellar(RouteWalk walk, IWalkHost host, List<string> facts, List<string> problems)
    {
        var heights = walk.Heights();
        var visited = walk.VisitedCells();
        facts.Add("cells " + string.Join(">", visited.Select(c => (c & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture))));
        if (heights.First - heights.Min < 3.0f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the player went down only {heights.First - heights.Min:F2} m (the cellar is about 5 m down)"));
        }

        InAndOutRules(CellarDoorCell, CellarRoomCell, visited, heights, facts, problems);
    }

    /// <summary>
    /// Cell tracking through the doorway and back: outdoors, the room, outdoors, with the doorway cell between them, and the same height at the end as at the
    /// start. The doorway cell is a thin one (a step of a run is 0.25 m and more), so a player crosses it between two physics steps now and then, as in retail,
    /// whose cell is the one that holds the sphere after the step: it must hold the player on at least one of the two crossings, and each skip is counted
    /// in the detail.
    /// </summary>
    private static void InAndOutRules(uint door, uint room, List<uint> visited, (float Min, float Max, float First, float Last) heights, List<string> facts, List<string> problems)
    {
        var roomVisits = visited.Count(cell => cell == room);
        var doorVisits = visited.Count(cell => cell == door);
        facts.Add($"the doorway cell held the player on {doorVisits} of 2 crossings (a skip is a thin cell crossed between two steps)");
        if (roomVisits == 0 || doorVisits == 0)
        {
            problems.Add($"the player's cells {string.Join(">", visited.Select(c => $"0x{c:X8}"))} do not go into the room 0x{room:X8} through the doorway cell 0x{door:X8}");
        }

        if (visited.Count > 0 && (visited[0] & 0xFFFF) >= 0x100)
        {
            problems.Add($"the walk started in cell 0x{visited[0]:X8}, not outdoors");
        }

        if (visited.Count > 0 && (visited[^1] & 0xFFFF) >= 0x100)
        {
            problems.Add($"the walk ended in cell 0x{visited[^1]:X8}, not outdoors");
        }

        if (MathF.Abs(heights.Last - heights.First) > 0.3f)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"the walk ended {heights.Last - heights.First:+0.00;-0.00} m from its starting height"));
        }
    }
}
