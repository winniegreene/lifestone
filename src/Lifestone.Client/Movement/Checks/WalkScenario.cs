using System.Globalization;
using System.Numerics;
using Lifestone.Data.Physics;
using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Checks;

/// <summary>
/// One walk check: a stretch of walking, a jump or a walk in and out of a building, driven through the real controller's intents, with everything the
/// player did recorded frame by frame and judged at the end (<see cref="Verdict"/>). The suite ticks it once per frame after the controller's step.
/// </summary>
/// <remarks>
/// <para><b>What every scenario asserts</b> besides its own rules: each key change is one MoveToState and the run key sends none
/// (<see cref="AuditedIntents"/>); the AutonomousPosition cadence (<see cref="CadenceAudit"/>: only on walkable ground, about once a second while moving,
/// at once on a cell change); the player's cell holds the player (an EnvCell contains the player's foot sphere); the camera is never inside
/// geometry (the host's camera pose is in a room or above the terrain, with a clear path from the head); no frame is over
/// <see cref="SlowFrameSeconds"/>.</para>
/// <para><b>Phases.</b> The scenario waits for the host, places the player at <see cref="StartFrame"/> as a server teleport does, lets the
/// frames settle (<see cref="SettleFrames"/>), then runs. Its time is the host's monotonic clock in seconds.</para>
/// </remarks>
public abstract class WalkScenario
{
    /// <summary>Frames the scenario lets pass after placing the player, before it starts (the camera and the cells settle).</summary>
    public const int SettleFrames = 10;

    /// <summary>The frame time rule (m2-plan: no frame over 50 ms).</summary>
    public const double SlowFrameSeconds = 0.050;

    private const int MaxProblems = 20;
    private const float PathStepMetres = 0.1f;

    private readonly double _timeoutSeconds;
    private readonly List<WalkSample> _samples = [];
    private readonly List<string> _problems = [];
    private Phase _phase;
    private int _settled;
    private double _startTime;
    private int _firstMessage;
    private int _cameraFrames;
    private int _cameraBlocked;
    private int _cameraCellLabelOutside;
    private int _slowFrames;
    private double _maxFrame;
    private AuditedIntents? _intents;

    private enum Phase
    {
        Waiting,
        Settling,
        Running,
        Done,
    }

    /// <summary>Makes a scenario that gives up after <paramref name="timeoutSeconds"/> of running.</summary>
    protected WalkScenario(string name, double timeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        _timeoutSeconds = timeoutSeconds;
    }

    /// <summary>The check's name.</summary>
    public string Name { get; }

    /// <summary>True once <see cref="Verdict"/> is set.</summary>
    public bool Finished => _phase == Phase.Done;

    /// <summary>The outcome, once <see cref="Finished"/>.</summary>
    public WalkVerdict Verdict { get; private set; }

    /// <summary>The longest frame the scenario ran in, in seconds.</summary>
    public double MaxFrameSeconds => _maxFrame;

    /// <summary>The frames over <see cref="SlowFrameSeconds"/>.</summary>
    public int SlowFrames => _slowFrames;

    /// <summary>Where the player is placed before the scenario starts, or null to start where it stands.</summary>
    public virtual CellFrame? StartFrame => null;

    /// <summary>True to run the scenario in first person.</summary>
    public virtual bool FirstPerson => false;

    /// <summary>Every frame since the scenario started.</summary>
    public IReadOnlyList<WalkSample> Samples => _samples;

    /// <summary>The intents the scenario drives the controller with (audited); valid from <see cref="OnStart"/> on.</summary>
    protected AuditedIntents Intents => _intents ?? throw new InvalidOperationException("the scenario has not started");

    /// <summary>The first message of this scenario in the host's trace.</summary>
    public int FirstMessage => _firstMessage;

    /// <summary>When the scenario started running (the host's clock).</summary>
    protected double StartTime => _startTime;

    /// <summary>Called once, when the player is placed and the frames have settled.</summary>
    protected abstract void OnStart(IWalkHost host, double now);

    /// <summary>Called every frame after the controller's step; true when the scenario is over.</summary>
    protected abstract bool OnTick(IWalkHost host, double now);

    /// <summary>Called once at the end: adds what was measured to <paramref name="facts"/> and what did not hold to <paramref name="problems"/>.</summary>
    protected abstract void Evaluate(IWalkHost host, List<string> facts, List<string> problems);

    /// <summary>One frame of the scenario; call it once per frame after the controller's step, with the controller's clock.</summary>
    public void Tick(IWalkHost host, double now)
    {
        ArgumentNullException.ThrowIfNull(host);
        switch (_phase)
        {
            case Phase.Waiting:
                if (!host.IsReady)
                {
                    return;
                }

                host.FirstPerson = FirstPerson;
                if (StartFrame is { } frame)
                {
                    host.Teleport(frame);
                }

                _phase = Phase.Settling;
                _settled = 0;
                return;
            case Phase.Settling:
                if (host.IsReady && ++_settled >= SettleFrames)
                {
                    Begin(host, now);
                }

                return;
            case Phase.Running:
                Step(host, now);
                return;
        }
    }

    /// <summary>Marks a problem found while running.</summary>
    protected void Problem(string text)
    {
        if (_problems.Count < MaxProblems)
        {
            _problems.Add(text);
        }
    }

    /// <summary>The lowest and highest height of the player over the scenario, and the heights at the first and last frame.</summary>
    protected (float Min, float Max, float First, float Last) HeightRange()
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var sample in _samples)
        {
            var z = sample.Position.Origin.Z;
            min = MathF.Min(min, z);
            max = MathF.Max(max, z);
        }

        return _samples.Count == 0 ? default : (min, max, _samples[0].Position.Origin.Z, _samples[^1].Position.Origin.Z);
    }

    /// <summary>
    /// The fastest the player moved over any stretch of <paramref name="seconds"/> spent entirely on the ground (metres a second): the distance between where
    /// it was at the start of the stretch and at its end, so turning in place and standing still count as slow. 0 when no such stretch exists.
    /// </summary>
    public double PeakGroundSpeed(double seconds)
    {
        var peak = 0.0;
        var start = 0;
        var airborne = -1;
        for (var i = 0; i < _samples.Count; i++)
        {
            if (!_samples[i].OnGround)
            {
                airborne = i;
            }

            start = Math.Max(start, airborne + 1);
            while (start < i && _samples[i].Time - _samples[start + 1].Time >= seconds)
            {
                start++;
            }

            var span = _samples[i].Time - _samples[start].Time;
            if (start < i && span >= seconds)
            {
                peak = Math.Max(peak, _samples[i].Position.GetOffset(_samples[start].Position).Length() / span);
            }
        }

        return peak;
    }

    private void Begin(IWalkHost host, double now)
    {
        _firstMessage = host.Trace.Messages.Count;
        _startTime = now;
        _intents = new AuditedIntents(host.Controller, host.Trace);
        _phase = Phase.Running;
        OnStart(host, now);
    }

    private void Step(IWalkHost host, double now)
    {
        var controller = host.Controller;
        var sample = new WalkSample(controller.Now, controller.Position, controller.OnGround, host.FrameSeconds);
        _samples.Add(sample);
        if (host.FrameSeconds > 0)
        {
            _maxFrame = Math.Max(_maxFrame, host.FrameSeconds);
        }

        if (host.FrameSeconds > SlowFrameSeconds)
        {
            _slowFrames++;
            Problem(string.Create(CultureInfo.InvariantCulture, $"a frame took {host.FrameSeconds * 1000.0:F1} ms at {sample.Time - _startTime:F2} s into the scenario"));
        }

        CheckCell(host, sample);
        CheckCamera(host);
        var done = OnTick(host, now);
        if (!done && now - _startTime > _timeoutSeconds)
        {
            Problem(string.Create(CultureInfo.InvariantCulture, $"timed out after {_timeoutSeconds:F0} s"));
            done = true;
        }

        if (done)
        {
            Finish(host);
        }
    }

    private void Finish(IWalkHost host)
    {
        var intents = Intents;
        intents.Dispose();
        var firstPerson = host.FirstPerson;
        host.FirstPerson = false;
        var trace = host.Trace;
        var facts = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"{_samples.Count} frames, {(_samples.Count > 0 ? _samples[^1].Time - _samples[0].Time : 0):F1} s, {trace.Count(SentKind.MoveToState, _firstMessage)} MoveToState for {intents.Commands} key changes, {trace.Count(SentKind.AutonomousPosition, _firstMessage)} AutonomousPosition, {trace.Count(SentKind.Jump, _firstMessage)} Jump, max frame {_maxFrame * 1000.0:F1} ms"),
        };
        if (host.Camera is not null)
        {
            facts.Add(string.Create(CultureInfo.InvariantCulture, $"camera {(firstPerson ? "first person" : "chase")} {_cameraFrames} frames, {_cameraBlocked} swept short, {_cameraCellLabelOutside} in the open air with a room's cell id"));
        }

        var problems = new List<string>(_problems);
        problems.AddRange(intents.Problems);
        problems.AddRange(CadenceAudit.Verify(trace.Messages, _firstMessage, _samples));
        Evaluate(host, facts, problems);
        var detail = string.Join("; ", facts);
        if (problems.Count > 0)
        {
            detail += " | NOT HELD: " + string.Join("; ", problems.Take(8)) + (problems.Count > 8 ? $"; and {problems.Count - 8} more" : string.Empty);
        }

        Verdict = new WalkVerdict(Name, problems.Count == 0, detail);
        _phase = Phase.Done;
    }

    /// <summary>
    /// Cell tracking: an EnvCell the controller reports holds the player (the foot sphere's centre is inside it, or inside a room it sees), and a player
    /// reported outdoors is not inside a room.
    /// </summary>
    private void CheckCell(IWalkHost host, in WalkSample sample)
    {
        var position = sample.Position;
        var cells = host.Cells;
        if (LandblockCoord.FromId(position.CellId) != cells.Coord)
        {
            return;
        }

        var centre = position.Origin + new Vector3(0f, 0f, host.Controller.Player.Radius);
        if (position.IsEnvCell)
        {
            if (cells.GetCell(position.CellId) is not { } cell)
            {
                Problem($"the player is in cell 0x{position.CellId:X8}, which the landblock does not have");
            }
            else if (!cells.Contains(cell, centre) && cells.FindCell(centre, position.CellId) is null)
            {
                Problem(string.Create(CultureInfo.InvariantCulture, $"cell 0x{position.CellId:X8} holds the player at ({position.Origin.X:F2}, {position.Origin.Y:F2}, {position.Origin.Z:F2}) but no room contains it"));
            }
        }
        else if (cells.FindCell(centre) is { } room)
        {
            Problem(string.Create(CultureInfo.InvariantCulture, $"the player is reported outdoors (cell 0x{position.CellId:X8}) inside room 0x{room.Id:X8} at ({position.Origin.X:F2}, {position.Origin.Y:F2}, {position.Origin.Z:F2})"));
        }
    }

    /// <summary>
    /// Camera audit: the viewer has a clear straight path from the pivot (a sweep of the viewer sphere from the head reaches it), so it is never in a wall or a
    /// floor; and, judged by the rooms and the terrain alone and not by that sweep, the eye is in a room or above the terrain, and the path from the head
    /// does not cross a wall (<see cref="CheckPathThroughWalls"/>).
    /// </summary>
    private void CheckCamera(IWalkHost host)
    {
        if (host.Camera is not { } pose)
        {
            return;
        }

        _cameraFrames++;
        if (pose.Blocked)
        {
            _cameraBlocked++;
        }

        var viewer = pose.Viewer;
        var world = host.World;
        var cells = host.Cells;
        if (world.GetCell(viewer.CellId) is null)
        {
            Problem($"the camera is in cell 0x{viewer.CellId:X8}, which has no collision attached");
            return;
        }

        var sweep = world.SweepViewer(pose.Pivot, viewer, host.ViewerRadius);
        var gap = sweep.Position.GetOffset(viewer).Length();
        if (sweep.Blocked && gap > 0.05f)
        {
            Problem(string.Create(CultureInfo.InvariantCulture, $"the camera at ({viewer.Origin.X:F2}, {viewer.Origin.Y:F2}, {viewer.Origin.Z:F2}) in cell 0x{viewer.CellId:X8} has no clear path from the head: a sweep stops {gap:F2} m short"));
            return;
        }

        if (LandblockCoord.FromId(viewer.CellId) != cells.Coord)
        {
            return;
        }

        // The sweep above is the function that produced the pose, so what follows does not use it: the eye is judged by the landblock's room volumes and
        // terrain alone.
        var eye = viewer.Origin;
        if (cells.FindCell(eye) is null)
        {
            if (viewer.IsEnvCell)
            {
                // The sweep's cell is the player's cell carried to where the camera ended: the camera in the open air by a doorway can carry the doorway's
                // cell id although no room holds it. That is not inside geometry (the path is clear); the interiors' tracker finds the camera's cell by
                // geometry every frame, so it is counted here and reported, not failed.
                _cameraCellLabelOutside++;
            }

            CheckAboveTerrain(host, eye);
        }

        if (viewer.IsEnvCell || pose.Pivot.IsEnvCell)
        {
            CheckPathThroughWalls(host, pose.Pivot, viewer);
        }
    }

    /// <summary>An eye in no room is in the open air, so it must be above the terrain under it (found from its own position, not from the sweep's cell).</summary>
    private void CheckAboveTerrain(IWalkHost host, Vector3 eye)
    {
        var cells = host.Cells;
        if (CellIds.LandCellAt(cells.Coord, eye.X, eye.Y) is not { } landId
            || host.World.GetCell(landId) is not PhysLandCell land
            || land.FindTerrainPoly(eye) is not { } terrain
            || MathF.Abs(terrain.Plane.Normal.Z) <= 1e-4f)
        {
            return;
        }

        var plane = terrain.Plane;
        var ground = -((plane.Normal.X * eye.X) + (plane.Normal.Y * eye.Y) + plane.D) / plane.Normal.Z;
        if (eye.Z < ground + 0.05f)
        {
            Problem(string.Create(CultureInfo.InvariantCulture, $"the camera is in no room and {ground - eye.Z:F2} m under the terrain at ({eye.X:F2}, {eye.Y:F2}) in cell 0x{landId:X8}"));
        }
    }

    /// <summary>
    /// The straight path from the head to the eye, stepped every <see cref="PathStepMetres"/> against the room volumes: it never runs from one room into
    /// another through solid (two or more steps in no room between them), and it enters or leaves the building only through a doorway (a cell with a portal
    /// to the outside, or the room beside one; the doorway cells are thin, so a step can skip them).
    /// </summary>
    private void CheckPathThroughWalls(IWalkHost host, in CellFrame pivot, in CellFrame viewer)
    {
        var cells = host.Cells;
        if (LandblockCoord.FromId(pivot.CellId) != cells.Coord)
        {
            return;
        }

        var start = pivot.Origin;
        var end = viewer.Origin;
        var steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(start, end) / PathStepMetres));
        EnvCellData? last = null;
        var missed = 0;
        for (var i = 0; i <= steps; i++)
        {
            var point = Vector3.Lerp(start, end, i / (float)steps);
            var room = cells.FindCell(point, last?.Id);
            if (room is null)
            {
                missed++;
                continue;
            }

            if (last is not null && missed >= 2)
            {
                Problem(string.Create(CultureInfo.InvariantCulture, $"the camera's path from the head runs from room 0x{last.Id:X8} into room 0x{room.Id:X8} through {missed * PathStepMetres:F1} m of wall"));
                return;
            }

            if (last is null && i > 0 && !NearDoorway(cells, room))
            {
                Problem($"the camera's path from the head (outside) enters room 0x{room.Id:X8}, which has no doorway to the outside");
                return;
            }

            last = room;
            missed = 0;
        }

        if (last is not null && missed > 0 && !NearDoorway(cells, last))
        {
            Problem(string.Create(CultureInfo.InvariantCulture, $"the camera's path from the head leaves room 0x{last.Id:X8} (no doorway to the outside) and ends in the open air at ({end.X:F2}, {end.Y:F2}, {end.Z:F2})"));
        }
    }

    /// <summary>True for a cell with a portal to the outside, or one whose neighbour has one.</summary>
    private static bool NearDoorway(LandblockCellSet cells, EnvCellData room)
    {
        if (room.Portals.Any(p => p.LeadsOutside))
        {
            return true;
        }

        foreach (var portal in room.Portals)
        {
            if (cells.GetCell(portal.OtherCellId) is { } next && next.Portals.Any(p => p.LeadsOutside))
            {
                return true;
            }
        }

        return false;
    }
}
