using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What a journey host's body offers a leg on foot (M4 P5b J5b): the headless host's own <see cref="HeadlessBody"/> or the Godot host's player. The leg
/// (<see cref="JourneyRunLeg"/>) and its planning (<see cref="JourneyLegPlanner"/>) are the same for both.
/// </summary>
public interface IJourneyLegBody
{
    /// <summary>The log category of the leg's lines.</summary>
    string LogCategory { get; }

    /// <summary>How long a leg waits for <see cref="WaitReason"/> to clear before it fails with <see cref="WaitFailure"/>.</summary>
    double WaitSeconds { get; }

    /// <summary>The seconds between the runner's route being fed to the collision streamer's corridor.</summary>
    double CorridorSeconds { get; }

    /// <summary>The nav map the outdoor planner reads (null before the body has one; the den needs none).</summary>
    NavMap? Map { get; }

    /// <summary>The body's position (read once <see cref="WaitReason"/> is null).</summary>
    CellFrame Position { get; }

    /// <summary>True while the body stands where the server has the player.</summary>
    bool Placed { get; }

    /// <summary>The server's moves of the player (teleports, recalls, places) seen so far; a change under a run fails the leg <c>travel:run:moved</c>.</summary>
    int Moves { get; }

    /// <summary>One frame of the body (the streamer's pump, the server's moves applied), first in every step of a leg.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The frame time.</param>
    void Frame(IGameSessionView session, double now);

    /// <summary>Null once a leg may plan (the player present, the body placed where the server has it, the collision and the tiles round it built); else the named reason it waits.</summary>
    /// <param name="session">The session.</param>
    string? WaitReason(IGameSessionView session);

    /// <summary>The leg's named failure when <see cref="WaitReason"/> did not clear within <see cref="WaitSeconds"/>.</summary>
    /// <param name="session">The session.</param>
    /// <param name="reason">The last wait reason.</param>
    string WaitFailure(IGameSessionView session, string reason);

    /// <summary>A runner over the body through the player's keys.</summary>
    /// <param name="session">The session (its <see cref="IActionApi.Move"/>).</param>
    /// <param name="source">The path source.</param>
    /// <param name="name">The runner's name.</param>
    NavRunner NewRunner(IGameSessionView session, INavPathSource source, string name);

    /// <summary>Feeds the streamer the landblocks ahead on the runner's route.</summary>
    /// <param name="runner">The runner.</param>
    void FollowRoute(NavRunner runner);

    /// <summary>
    /// A door Setup's extent by its id (<see cref="DoorCrossings.SetupExtents"/> over the host's dats), the size of a door the leg's runner maps to its doorway; null for a host that gives none
    /// (every door then assumes <see cref="DoorCrossings.UnknownRadiusMetres"/>, logged once). The two hosts return <c>DoorCrossings.SetupExtents(dats)</c> here (<c>TODO(door-extent-host)</c> is closed).
    /// </summary>
    Func<uint, float?>? SetupExtent => null;

    /// <summary>
    /// M6 14 hub plan: runs work on one of the host's workers and gives its task, or null for a host that plans on its own thread alone (the headless host: its steps are not frames anyone sees). The Godot host gives its
    /// worker pool, and a leg in the rooms of one landblock first works its rooms out there (<see cref="JourneyLegPlanner.RoomsWarmUp"/>), so its plans on the game's thread hold no frame (live 2026-11-16 16:54:52 UTC:
    /// the first walk in the Town Network held the game's frames 12 s).
    /// </summary>
    Func<Action, Task>? OffThread => null;
}

/// <summary>
/// One leg on foot, the same in both hosts (M4 P5b J5b, from the headless host's leg of J4/F1): every step runs the body's frame; until the body may plan
/// (<see cref="IJourneyLegBody.WaitReason"/>) the leg waits, bounded by <see cref="IJourneyLegBody.WaitSeconds"/>; then it plans once with the shared
/// <see cref="JourneyLegPlanner"/> (goal and source from where the body stands), runs a <see cref="NavRunner"/>, feeds the route's corridor and ends with
/// <see cref="JourneyLegPlanner.Arrived"/> or a named failure (<see cref="JourneyLegPlanner.FailureOf"/>; <c>travel:run:moved</c> when the server moved the
/// player under the run; <c>travel:run:no-path-source</c> when no source could be built).
/// </summary>
/// <param name="planner">The planner.</param>
/// <param name="body">The body.</param>
/// <param name="request">The request.</param>
/// <param name="name">The leg's name.</param>
/// <param name="startedAt">The session clock when the leg was asked for.</param>
/// <param name="log">The log.</param>
public sealed class JourneyRunLeg(JourneyLegPlanner planner, IJourneyLegBody body, TravelRequest request, string name, double startedAt, ILogSink log) : ITravelLeg
{
    /// <summary>How far from the straight line to the goal a portal is carried on the run's list (M6 7 AI), in metres: a landblock; a route wanders less than that off its line. Not retail: Lifestone's own.</summary>
    private const float KeepOffReachMetres = 192f;

    /// <summary>
    /// M6 item 14 (<c>m6-vendor-portal-keep-off</c>; live 2026-11-13: the first walk from Uziz's Network drop to its archmage ran into the Haven Residential Halls Portal, which stands outdoors on the way and is in no travel record;
    /// it took the body on touch and the stop, the rung change and the run ended): the portals a walk to a shopkeeper keeps clear of when its request names none: every portal object the client holds a place for
    /// (<see cref="WorldContentCensus.Classify"/>: the Portal bit of the object's description, what the client draws), read when the leg plans. A portal within the clearance of the walk's own end is left out, so an end beside a
    /// portal is never refused (as <c>RungPortal.KeepOffTheOthers</c> leaves the walk's own portal out). Null when no such portal is held. The rule itself is <see cref="PortalKeepOff"/>'s: a plan that cannot keep clear fails by name.
    /// Every walk now keeps these too (<see cref="LegKeepOff"/>, M6 14 m6-14-plaza: closes <c>TODO(m6-live-portal-keep-off-all)</c>).
    /// </summary>
    /// <param name="session">The session (its objects).</param>
    /// <param name="goal">The walk's end, global metres.</param>
    internal static PortalKeepOff? LivePortals(IGameSessionView session, Vector2 goal)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<KeptPortal>? held = null;
        foreach (var o in session.State.Objects.All)
        {
            if (o.IsSelf || o.Position is not { } p || WorldContentCensus.Classify(o.Weenie) != WorldContentKind.Portal)
            {
                continue;
            }

            var at = new NavPoint(p.CellId & 0xFFFF0000u, p.Frame.Origin).Global;
            if (Vector2.Distance(at, goal) <= PortalKeepOff.ClearanceMetres)
            {
                continue;
            }

            (held ??= []).Add(new KeptPortal(o.Name, at, p.Frame.Origin.Z, (p.CellId & 0xFFFF) < 0x100));
        }

        return held is null ? null : new PortalKeepOff(held);
    }

    /// <summary>
    /// M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:29:41 UTC: the walk to the Yinar Portal's front kept off its own portal alone, the plaza's other eighteen being in no travel record, and Sand's Edge Portal took her): the list every walk keeps,
    /// whatever its purpose. The request's own list first (a hop's: its own portal and the others, <see cref="RungPortal.KeepOffForFront"/>), then every portal the travel data and the world know (<see cref="RungPortal.AllKnown"/>, the placed
    /// portals of <see cref="Lifestone.Client.Session.Travel.WorldPortals"/>) and every portal object the client holds (<see cref="LivePortals"/>: a portal no file places). A portal already on the request's list (within 0.5 m) is not added twice, and one the request did not
    /// name that stands within the clearance of the walk's end is left out, so an end beside a portal (a seek's own portal, a front 3 m off it) is never refused. Null when no portal is near (the runner's <see cref="PortalKeepOff.Near"/> trims the rest).
    /// </summary>
    /// <param name="session">The session (its objects).</param>
    /// <param name="request">The leg's request (its list, or none).</param>
    /// <param name="from">The walk's start, global metres.</param>
    /// <param name="goal">The walk's end, global metres.</param>
    internal static PortalKeepOff? LegKeepOff(IGameSessionView session, TravelRequest request, Vector2 from, Vector2 goal)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kept = new List<KeptPortal>(request.KeepOff?.Near(from, goal, KeepOffReachMetres).Portals ?? []);
        var live = LivePortals(session, goal)?.Portals ?? [];
        foreach (var p in RungPortal.AllKnown.Concat(live))
        {
            if (SegmentMetres(p.Global, from, goal) > KeepOffReachMetres || Vector2.Distance(p.Global, goal) <= PortalKeepOff.ClearanceMetres || kept.Exists(q => Vector2.Distance(q.Global, p.Global) <= SamePortalMetres))
            {
                continue;
            }

            kept.Add(p);
        }

        return kept.Count == 0 ? null
            : request.KeepOff is { } own ? new PortalKeepOff(kept, own.Clearance, own.OwnClearance)
            : new PortalKeepOff(kept);
    }

    private static float SegmentMetres(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var length2 = ab.LengthSquared();
        return Vector2.Distance(p, a + (ab * (length2 <= 1e-8f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / length2, 0f, 1f))));
    }

    /// <summary>How near two records of a portal stand to be the same object (<c>RungPortal</c>'s rule), in metres. Not retail: Lifestone's own.</summary>
    private const float SamePortalMetres = 0.5f;

    private NavRunner? _runner;
    private TravelStatus _status = TravelStatus.Running;
    private double _nextCorridor;
    private int _moves;
    private uint _startCell;
    private HashSet<(string Name, Vector2 At)>? _unkeptNamed;
    private Vector2 _goalGlobal;

    /// <summary>
    /// M6 14 (m6-14-plaza): the list is made when the leg plans (<see cref="LegKeepOff"/>); a portal object the client comes to hold later on the way, which no file places (a summoned portal), is not on it: named once by a warning,
    /// <c>TODO(m6-live-portal-mid-run)</c>, never a silent miss (the runner has no replan for a changed list).
    /// </summary>
    private void NoteUnkeptLivePortal(IGameSessionView session)
    {
        if (_runner?.KeepOff is not { } keep || LivePortals(session, _goalGlobal) is not { } live)
        {
            return;
        }

        foreach (var p in live.Portals)
        {
            if (keep.Portals.Any(q => Vector2.Distance(q.Global, p.Global) <= SamePortalMetres) || Vector2.Distance(p.Global, NavMap.FromCellFrame(body.Position).Global) > KeepOffReachMetres || !(_unkeptNamed ??= []).Add((p.Name, p.Global)))
            {
                continue;
            }

            log.Warn(body.LogCategory, "TODO(m6-live-portal-mid-run): a portal the client came to hold on the way is not on the leg's list (it was made when the leg planned; no file places this portal)", new LogField("leg", name), new LogField("portal", p.Name), new LogField("at", p.Global));
        }
    }
    private RaisedStart? _stepOff;
    private bool _stepOffTried;
    private Task? _warm;
    private bool _warmTried;
    private bool _warmed;
    private double _warmStarted;

    /// <inheritdoc />
    public TravelMeans Means => TravelMeans.Run;

    /// <inheritdoc />
    public string? Failure { get; private set; }

    /// <inheritdoc />
    public string Fields => _runner?.VerdictFields() ?? "route_m=0.0 run_s=0.0";

    /// <inheritdoc />
    public bool ArrivedWithoutSight { get; private set; }

    /// <summary>The runner, once the leg planned.</summary>
    public NavRunner? Runner => _runner;

    /// <summary>The leg's name.</summary>
    public string Name => name;

    /// <summary>The last reason the leg waited before it planned, or null.</summary>
    public string? Waited { get; private set; }

    /// <inheritdoc />
    public TravelStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_status != TravelStatus.Running)
        {
            return _status;
        }

        body.Frame(session, now);
        if (_runner is null)
        {
            if (body.WaitReason(session) is { } reason)
            {
                Waited = reason;
                return session.Clock.Now - startedAt > body.WaitSeconds ? Fail(body.WaitFailure(session, reason)) : TravelStatus.Running;
            }

            if (_stepOff is { } off)
            {
                if (body.Moves != _moves || !body.Placed)
                {
                    off.Release();
                    return Fail("travel:run:moved: the server moved the player during the step off a raised start");
                }

                switch (off.Step(now))
                {
                    case RaisedStartState.Running:
                        return TravelStatus.Running;
                    case RaisedStartState.Failed:
                        // Loud, not fatal: the leg plans from where the body stands, and a run the counter still holds fails by the runner's own name (a false raised start on a stair must not fail a leg that runs).
                        log.Warn(body.LogCategory, "the leg " + name + " plans from the raised start: " + off.Failure);
                        break;
                }

                _stepOff = null;
            }

            var goal = planner.GoalOf(session, request);
            var from = body.Position;
            if (WarmUp(session, from, goal) is { } warming)
            {
                return warming;
            }

            _startCell = from.CellId;
            INavPathSource source;
            try
            {
                var why = "the authored way of " + request.Way?.Count + " points (M6 8 B1)";
                source = request.Way is { Count: > 0 } way ? new AuthoredWaySource(way) : planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), body.Map, out why);
                log.Info(body.LogCategory, "the leg's path source", new LogField("leg", name), new LogField("from", $"0x{from.CellId:X8}"),
                    new LogField("to", $"0x{JourneyLegPlanner.GoalCellOf(goal):X8}"), new LogField("rooms", source is INavRoomSource r ? $"0x{r.Rooms.LandblockId >> 16:X4}" : null), new LogField("why", why));
            }
            catch (Exception e) when (e is InvalidOperationException or AggregateException)
            {
                return Fail($"travel:run:no-path-source: {e.GetBaseException().Message}");
            }

            // FC-a2: a body raised on a counter in a building (ACE's walk-to-use carried it up) steps down to the room's floor first, once per leg; the plan is made again from the floor. Not in the den
            // (its room source): a dungeon's ramps are not counters.
            if (!_stepOffTried && source is IndoorOutdoorPathSource indoors && RaisedStart.FloorBelow(indoors.Plan(from, goal, new NavScars()), from, out var next, indoors.Rooms.RoomFloorUnder(from.CellId, from.Origin + new Vector3(0f, 0f, RaisedStart.FloorToleranceMetres))) is { } floor)
            {
                _stepOffTried = true;
                _moves = body.Moves;
                _stepOff = new RaisedStart(session.Actions.Move, () => body.Position, floor, next, log, body.LogCategory);
                return TravelStatus.Running;
            }

            _runner = body.NewRunner(session, source, "journey-" + name);
            _runner.Doors = new SessionDoorFeed(session, log, body.SetupExtent); // M5 item 5, d1: a closed door on the route is opened (purpose=door), not walked through
            _runner.UseTarget = request.Use; // the hub walk: hand over to the server's walk-to-use once the line is clear
            // M6 14 (m6-14-plaza): every walk keeps clear of every portal near its line: the request's list, the known and placed portals, and the ones the client holds (LegKeepOff)
            _goalGlobal = goal.Point.Global;
            if (LegKeepOff(session, request, NavMap.FromCellFrame(from).Global, goal.Point.Global) is { } keep)
            {
                _runner.KeepOff = keep.Near(NavMap.FromCellFrame(from).Global, goal.Point.Global, KeepOffReachMetres); // M6 7 AI
                log.Info(body.LogCategory, "the leg keeps clear of portals", new LogField("leg", name), new LogField("portals", _runner.KeepOff.Portals.Count), new LogField("of", keep.Portals.Count), new LogField("clearance_m", keep.Clearance), new LogField("near", string.Join(", ", _runner.KeepOff.Portals.Select(p => p.Name))));
            }

            _moves = body.Moves;
            _runner.Start(goal, now);
            log.Info(body.LogCategory, "a leg on foot begins", JourneyLegPlanner.BeginFields(name, request, goal, from.CellId));
            if (_warmed)
            {
                return TravelStatus.Running; // M6 14 hub plan: the runner's first plan in the next frame, not in this one with the source's
            }
        }

        if (body.Moves != _moves || !body.Placed)
        {
            return Fail("travel:run:moved: the server moved the player during the run");
        }

        if (session.Clock.Now >= _nextCorridor)
        {
            _nextCorridor = session.Clock.Now + body.CorridorSeconds;
            body.FollowRoute(_runner);
            NoteUnkeptLivePortal(session);
        }

        var state = _runner.Step(now);
        if (!_runner.IsDone)
        {
            return TravelStatus.Running;
        }

        if (JourneyLegPlanner.Arrived(state, request))
        {
            log.Info(body.LogCategory, "the leg arrived", new LogField("leg", name), new LogField("fields", _runner.VerdictFields()));
            return _status = TravelStatus.Arrived;
        }

        ArrivedWithoutSight = state == NavRunState.ArrivedWithoutSight; // the request needed the line (else Arrived above): the typed end (P5b-R5)
        return Fail(JourneyLegPlanner.FailureOf(state, _runner.FailureReason, _startCell));
    }

    /// <inheritdoc />
    public void Pause()
    {
        _stepOff?.Release();
        _runner?.Pause();
    }

    /// <inheritdoc />
    public void Resume(double now) => _runner?.Resume(now);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_status == TravelStatus.Running)
        {
            _status = TravelStatus.Failed;
            Failure ??= "travel:run:abandoned";
        }

        _stepOff?.Release();
        _runner?.Dispose(); // no key stays held
    }

    /// <summary>
    /// M6 14 hub plan, before the leg plans: with a host that has workers (<see cref="IJourneyLegBody.OffThread"/>) and a leg in the rooms of one landblock (<see cref="JourneyLegPlanner.RoomsWarmUp"/>), the rooms are worked out
    /// on a worker first, once a leg, while the game's frames go on; the leg plans when the work is done (Running until then). Check after: a server move of the player while the worker ran fails the leg
    /// <c>travel:run:moved</c>, as under a run; a worker that failed is logged loudly and the leg plans on the game's thread as before. Null: plan now.
    /// </summary>
    private TravelStatus? WarmUp(IGameSessionView session, in CellFrame from, NavGoal goal)
    {
        if (_warm is { } warm)
        {
            if (!warm.IsCompleted)
            {
                return TravelStatus.Running;
            }

            _warm = null;
            var ms = new LogField("ms", (session.Clock.Now - _warmStarted) * 1000.0); // the session's clock
            if (warm.Exception?.GetBaseException() is { } error)
            {
                log.Warn(body.LogCategory, "the leg's rooms could not be worked out on a worker: the leg plans them on the game's thread", new LogField("leg", name), ms, new LogField("error", error.Message));
            }
            else
            {
                log.Info(body.LogCategory, "the leg's rooms were worked out on a worker", new LogField("leg", name), ms);
            }

            return body.Moves != _moves || !body.Placed ? Fail("travel:run:moved: the server moved the player while the leg's rooms were worked out") : null;
        }

        if (_warmTried || request.Way is { Count: > 0 } || body.OffThread is not { } offThread)
        {
            return null;
        }

        _warmTried = true;
        var keep = LegKeepOff(session, request, NavMap.FromCellFrame(from).Global, goal.Point.Global)?
            .Near(NavMap.FromCellFrame(from).Global, goal.Point.Global, KeepOffReachMetres).WithOwnClearance(PortalKeepOff.OwnReach(NavRunner.StopMetres(goal.StopTolerance))); // the list the runner will keep (ResolveKeepOff)
        if (planner.RoomsWarmUp(from, goal, keep) is not { } work)
        {
            return null;
        }

        _moves = body.Moves;
        _warmed = true;
        _warmStarted = session.Clock.Now;
        _warm = offThread(work);
        log.Info(body.LogCategory, "the leg's rooms are worked out on a worker before it plans", new LogField("leg", name), new LogField("from", $"0x{from.CellId:X8}"),
            new LogField("to", $"0x{JourneyLegPlanner.GoalCellOf(goal):X8}"), new LogField("rooms", $"0x{from.CellId >> 16:X4}"), new LogField("kept_portals", keep?.Portals.Count ?? 0));
        return TravelStatus.Running;
    }

    private TravelStatus Fail(string why)
    {
        _stepOff?.Release();
        _runner?.Dispose();
        Failure = why;
        log.Warn(body.LogCategory, $"the leg {name} failed: {why}");
        return _status = TravelStatus.Failed;
    }
}

/// <summary>
/// The rule a body that belongs to the play (the Godot host's player) waits on before a leg plans (M4 P5b J5b, the headless body's
/// <see cref="HeadlessStreamer.IsFocusSettled"/> rule): the player present, the body placed within <see cref="HeadlessJourneyRunnerHost.MovedMetres"/> of where
/// the server has it, and the focus round the body's cell settled (the collision and the nav tiles built). Never a plan over missing tiles (live: <c>no-tile</c>,
/// <c>start-not-attached</c>); a wait past <see cref="Seconds"/> fails by the name <c>travel:run:not-settled:&lt;cell&gt;</c>.
/// </summary>
public static class JourneySettleGate
{
    /// <summary>How long a leg waits (the headless body's wait, <see cref="WayfindScenario.SettleSeconds"/>; Lifestone's own).</summary>
    public const double Seconds = WayfindScenario.SettleSeconds;

    /// <summary>The failure's name.</summary>
    public const string NotSettled = "travel:run:not-settled";

    /// <summary>Null once a leg may plan; else the named reason (<c>not-present</c>, <c>not-placed:&lt;cell&gt;</c>, <c>focus-not-settled:&lt;cell&gt;</c>).</summary>
    /// <param name="session">The session (where the server has the player).</param>
    /// <param name="body">The body's position when it is placed, else null.</param>
    /// <param name="focusSettled">True once the focus round a cell has settled (the host's streamer; called only for a placed body).</param>
    public static string? Reason(IGameSessionView session, CellFrame? body, Func<uint, bool> focusSettled)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(focusSettled);
        var self = session.State.Self;
        if (self.Travel != SelfTravel.Present || self.Position is not { } server)
        {
            return "not-present: the player is not present in the world (portal space or a login)";
        }

        if (body is not { } at || JourneyLegPlanner.Moved(server, at, HeadlessJourneyRunnerHost.MovedMetres))
        {
            return $"not-placed:0x{server.CellId:X8}: the player's body is not placed where the server has the player yet";
        }

        return focusSettled(at.CellId) ? null : $"focus-not-settled:0x{at.CellId:X8}: the collision and the nav tiles round the player's cell are not built yet";
    }

    /// <summary>The failure after the wait: <c>travel:run:not-settled:&lt;cell&gt;</c> with the last reason.</summary>
    /// <param name="cell">The body's cell, else the server's.</param>
    /// <param name="reason">The last reason.</param>
    public static string Failure(uint cell, string reason) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{NotSettled}:0x{cell:X8}: not ready within {Seconds:0} s ({reason})");
}
