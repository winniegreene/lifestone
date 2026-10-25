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
    private NavRunner? _runner;
    private TravelStatus _status = TravelStatus.Running;
    private double _nextCorridor;
    private int _moves;
    private uint _startCell;
    private RaisedStart? _stepOff;
    private bool _stepOffTried;

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
            _startCell = from.CellId;
            INavPathSource source;
            try
            {
                source = planner.SourceFor(from.CellId, JourneyLegPlanner.GoalCellOf(goal), body.Map);
            }
            catch (Exception e) when (e is InvalidOperationException or AggregateException)
            {
                return Fail($"travel:run:no-path-source: {e.GetBaseException().Message}");
            }

            // FC-a2: a body raised on a counter in a building (ACE's walk-to-use carried it up) steps down to the room's floor first, once per leg; the plan is made again from the floor. Not in the den
            // (its room source): a dungeon's ramps are not counters.
            if (!_stepOffTried && source is IndoorOutdoorPathSource && RaisedStart.FloorBelow(source.Plan(from, goal, new NavScars()), from, out var next) is { } floor)
            {
                _stepOffTried = true;
                _moves = body.Moves;
                _stepOff = new RaisedStart(session.Actions.Move, () => body.Position, floor, next, log, body.LogCategory);
                return TravelStatus.Running;
            }

            _runner = body.NewRunner(session, source, "journey-" + name);
            _moves = body.Moves;
            _runner.Start(goal, now);
            log.Info(body.LogCategory, "a leg on foot begins", JourneyLegPlanner.BeginFields(name, request, goal, from.CellId));
        }

        if (body.Moves != _moves || !body.Placed)
        {
            return Fail("travel:run:moved: the server moved the player during the run");
        }

        if (session.Clock.Now >= _nextCorridor)
        {
            _nextCorridor = session.Clock.Now + body.CorridorSeconds;
            body.FollowRoute(_runner);
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
