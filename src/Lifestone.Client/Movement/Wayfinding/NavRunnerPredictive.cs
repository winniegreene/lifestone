using System.Globalization;
using System.Numerics;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// The predictive steering of a dungeon's indoor route (the owner's ruling, 2026-11-15: flawless-looking walking indoors and out under legal physics, because it is just math;
/// <see cref="NavRunnerOptions.PredictiveSteering"/>; docs/wayfinding.md "Predictive steering"). The keys are the player's own (forward, turn left, turn right, run), pressed through
/// the client's movement and collision code; nothing is written to the server.
/// <para>The body's motion is known, and measured from its own moves (<see cref="NotePredictMotion"/>): the physics moves it once an update (when 1/30 s or more of the host's clock has
/// passed: an offline walk at 30 steps a second updates every other step, at 60 every second or third), a key pressed between two updates acts for the whole of the next, a held turn
/// key turns it at a fixed rate (running and standing measured apart: the walker's run turns 9 degrees an update at 1/15 s), and forward ramps its speed up and down with a time
/// constant of about 0.1 s (a run let go coasts about 0.8 m). So the steering does not react to where the body went: after each update it plays the body's motion forward, update by
/// update, over <see cref="PredictHorizonSeconds"/>, for each plan of keys it could hold (<see cref="PredictPlan"/>: a turn now for so many updates with forward held or let go, or none;
/// a straight run; a second turn that lines the heading up with the route's leg there or the next, with forward held or let go; a straight run to the horizon), and keeps the cheapest:
/// off the route's line past a band, the heading off the route ahead at the end, the ground gained, a stopping run's miss of the goal, and every key change on the way (a reversal, a
/// flip and a stutter costing more). A body standing still turns past <see cref="PredictStandMinDegrees"/> in place before it runs. A turn now must beat holding on by <see cref="PredictTurnMargin"/> (no taps); a turn is held to the heading it was planned to; while the body is
/// where the plan put it, the plan is followed without planning again (<see cref="PredictFollowUpdates"/>). The cheapest plans' tracks are swept at the body's radius through the
/// route's cells, the statics the room plans go round and the physics: a near block refuses a plan, a far one costs. The route's line is simplified first (doorway pairs, a staircase's
/// samples, a doorway's jog where the straight line keeps to the cells). A stopping run lets forward go where its coast carries the body through the goal.</para>
/// <para>The ordinary steering stays the fallback: when no plan is clear (after holding on a moment), a route shorter than <see cref="PredictShortRouteMetres"/>, a dungeon's route with a leg
/// within <see cref="PredictClutterMetres"/> of a static the room plans go round, a route ahead passing over or under itself, a hairpin or a U in a few metres, the body in the air,
/// a stop's coast that fell short within <see cref="PredictWalkInMetres"/> of a goal ahead, a second of no ground gained (<see cref="PredictStallSeconds"/>), or a runner without the
/// physics to sweep with. A handed-over step is the ordinary steering's, every update.</para>
/// <para>Outdoors (<see cref="NavRunnerOptions.PredictiveOutdoors"/>) the same plans run over the land's routes: off the route's line by more than <see cref="PredictBandMetres"/>
/// a track must stand on the land map at the straight run's clearance, off water, slopes and steep edges the route does not cross, and off the run's scars
/// (<see cref="TrackGrazeOutdoors"/>); the free band is the straight run's own drift (<see cref="CrossTrackMetres"/>); the window reaches a horizon past the next vertex (a land
/// leg is long); a start from a standstill, a route into or out of a building and a body standing short of its stop are the ordinary steering's; an object goal's coast aims inside
/// its range.</para>
/// <para>Not retail: Lifestone's own.</para>
/// </summary>
public sealed partial class NavRunner
{
    /// <summary>How far ahead the predicted tracks run, in seconds. Not retail: Lifestone's own.</summary>
    public const double PredictHorizonSeconds = 1.5;

    /// <summary>The physics update before one is measured, in seconds (a host at 60 frames a second updates every 2nd or 3rd frame).</summary>
    public const double PredictDefaultUpdateSeconds = 0.05;

    /// <summary>How far off the route's line the body runs free of cost, in metres. Not retail: Lifestone's own.</summary>
    public const float PredictBandMetres = 0.4f;

    /// <summary>How far before a stopping run's goal the route's line is held to <see cref="PredictGoalBandMetres"/> (and costs twice), in metres. Not retail: Lifestone's own.</summary>
    public const float PredictGoalLineMetres = 4f;

    /// <summary>How far off the route's line the body runs free of cost in a stopping run's last <see cref="PredictGoalLineMetres"/>, in metres. Not retail: Lifestone's own.</summary>
    public const float PredictGoalBandMetres = 0.15f;

    /// <summary>The cost of a squared metre off the route's line past the band, per second. Not retail: Lifestone's own.</summary>
    public const float PredictLateralWeight = 4.5f;

    /// <summary>The cost of a squared radian off the route's heading at the horizon's end, past half an update's turn. Not retail: Lifestone's own.</summary>
    public const float PredictHeadingWeight = 30f;

    /// <summary>The most the heading at the horizon's end costs (the turn that would take it back: a press and a release). Not retail: Lifestone's own.</summary>
    public const float PredictHeadingCap = 1.2f;

    /// <summary>The reward of a metre of route gained over the horizon. Not retail: Lifestone's own.</summary>
    public const float PredictProgressWeight = 1f;

    /// <summary>The cost of a key change (a press or a release) on the predicted track. Not retail: Lifestone's own.</summary>
    public const float PredictKeyWeight = 0.5f;

    /// <summary>The cost of forward pressed again after it was let go (a false start). Not retail: Lifestone's own.</summary>
    public const float PredictRepressWeight = 1f;

    /// <summary>The cost of a turn pressed the other way within <see cref="ReversalCountSeconds"/> of letting one go (a wiggle). Not retail: Lifestone's own.</summary>
    public const float PredictReversalWeight = 1f;

    /// <summary>The cost of a turn pressed the other way within <see cref="FlipCountSeconds"/> of letting one go (the aim jumping). Not retail: Lifestone's own.</summary>
    public const float PredictFlipWeight = 3f;

    /// <summary>How far along the route ahead of a plan's end its heading is judged against, in metres (the heading that runs at the route this far on). Not retail: Lifestone's own.</summary>
    public const float PredictAimMetres = 6f;

    /// <summary>How long, in seconds, the keys are held as they are when no plan keeps to the cells, before the ordinary steering takes the step (a single step of it between plans
    /// pressed against them). Not retail: Lifestone's own.</summary>
    public const double PredictHoldSeconds = 0.2;

    /// <summary>How far off the steering's line a predicted point may stray before the plan costs <see cref="PredictOffRouteWeight"/> an update, in metres (a metre inside
    /// the runner's <see cref="OffRouteMetres"/>). Not retail: Lifestone's own.</summary>
    public const float PredictOffRouteMetres = OffRouteMetres - 1f;

    /// <summary>The cost of an update of a plan past <see cref="PredictOffRouteMetres"/>. Not retail: Lifestone's own.</summary>
    public const float PredictOffRouteWeight = 10f;

    /// <summary>How much dearer than the cheapest plan of all the cheapest clear plan may be before the step is left to the ordinary steering. Not retail: Lifestone's own.</summary>
    public const float PredictGiveUpCost = 4f;

    /// <summary>How many updates of a turn's length (and of the run before the second turn) the plans try one by one; past it, every other. Not retail: Lifestone's own.</summary>
    public const int PredictFineQuanta = 8;

    /// <summary>The least turn, in degrees, a running body's plans try with forward let go (a turn in place's), and the most a standing body's plans turn with forward held
    /// (<see cref="PredictStandingSpeed"/>). Not retail: Lifestone's own.</summary>
    public const double PredictStandMinDegrees = 30.0;

    /// <summary>The speed below which the body is standing, in metres a second: from there a turn of more than <see cref="PredictStandMinDegrees"/> is taken in place before forward is
    /// pressed in a dungeon (the walk from a standstill gathers speed slower than the ramp while a turn is held, measured in the Burrow: the body turned 90 degrees and ran 2.4 m wide
    /// of a short leg); outdoors there is room to turn on the run.
    /// Not retail: Lifestone's own.</summary>
    public const float PredictStandingSpeed = 0.5f;

    /// <summary>How near the goal, in metres (and within <see cref="PredictStandMinDegrees"/> of the heading), a stop's coast that fell short leaves the rest to the ordinary steering's
    /// walk near a stop (the Tusker Cavern's leg 0x0130, its coast 0.66 m short on a slope: the run restarted overshot and turned back, a rung). Not retail: Lifestone's own.</summary>
    public const float PredictWalkInMetres = 1f;

    /// <summary>How many of the route's legs from the one a predicted point was measured against the next point is measured against (forward only).</summary>
    public const int PredictSegmentReach = 2;

    /// <summary>The cost of a turn pressed the same way within <see cref="PredictStutterSeconds"/> of letting it go (one turn made in two presses). Not retail: Lifestone's own.</summary>
    public const float PredictStutterWeight = 1f;

    /// <summary>How soon after a release a press the same way is a stutter, in seconds. Not retail: Lifestone's own.</summary>
    public const double PredictStutterSeconds = 0.5;

    /// <summary>How much cheaper a turn pressed now must be than holding on. Not retail: Lifestone's own.</summary>
    public const float PredictTurnMargin = 0.3f;

    /// <summary>The cost of a squared metre off the goal past <see cref="PredictStopLineMetres"/> where a stopping track ends. Not retail: Lifestone's own.</summary>
    public const float PredictStopWeight = 20f;

    /// <summary>The cost of a plan that comes to rest short of the goal (a coast let go too soon or off line). Not retail: Lifestone's own.</summary>
    public const float PredictShortStopWeight = 5f;

    /// <summary>How far off the goal a stopping track may pass at no cost, in metres (inside the goal's stop tolerance).</summary>
    public const float PredictStopLineMetres = 0.35f;

    /// <summary>How many of the cheapest predicted tracks are swept, the costliest test. Not retail: Lifestone's own.</summary>
    public const int PredictCellChecks = 16;

    /// <summary>The time constant of the body's speed towards the speed its forward key asks, up and down, in seconds (fitted to the walker: 0.32, 0.62, 0.75 and 1.0 of the run in
    /// the first four 1/15 s updates after forward; about 0.8 m coasted from 8 m/s). Not retail: Lifestone's own fit.</summary>
    public const double SpeedRampSeconds = 0.1;

    /// <summary>The turn rate before one is measured, in degrees a second (the walker's run: 134.5 running, 128.9 standing).</summary>
    public const double PredictTurnDegreesPerSecond = 130.0;

    /// <summary>How long a body that has not moved or turned is still before a step counts as an update, in seconds. Not retail: Lifestone's own.</summary>
    private const double MotionStillSeconds = 0.25;

    /// <summary>Steps the predictive steering left to the ordinary steering because none of its cheapest tracks kept to the route's cells. Report-only.</summary>
    public int PredictFallbacks { get; private set; }

    [ThreadStatic]
    private static Action<string>? s_predictTrace;

    /// <summary>A trace of each predictive decision on this thread, for the steering's own tests and the world walk's <c>--trace-leg</c> (null: none).</summary>
    internal static Action<string>? PredictTrace
    {
        get => s_predictTrace;
        set => s_predictTrace = value;
    }

    // The measures, between the body's own moves (a step that finds the body where it was is no information).
    private bool _physicsStepped = true;
    private bool _pmStarted;
    private double _pmTime;
    private Vector2 _pmGlobal;
    private double _pmHeading;
    private int _pmTurn;
    private bool _pmForward;
    private int _pmTurn2;
    private bool _pmForward2;
    private bool _pmMoving;
    private double _pUpdate = PredictDefaultUpdateSeconds;
    private double _pRunRate = PredictTurnDegreesPerSecond;
    private double _pStandRate = PredictTurnDegreesPerSecond;
    private float _pTop = float.NaN;
    private float _pSpeed;
    private double _pDelay;
    private double _pVModel;
    private Vector2 _pVelocity;
    private int _pSlow;
    private bool _pPressPending;

    // The decision's state.
    private bool _commitActive;
    private int _commitSide;
    private double _commitHeading;
    private bool _commitForwardAfter;
    private NavRoute? _commitRoute;
    private bool _pStopping;
    private double? _pHoldSince;
    private double _pStopAt;

    // The route's window (the segments the tracks are measured against).
    private Vector2[] _wA = new Vector2[16];
    private Vector2[] _wD = new Vector2[16];
    private float[] _wL = new float[16];
    private float[] _wS = new float[16];
    private float[] _wZ0 = new float[16];
    private float[] _wZ1 = new float[16];
    private double[] _wB = new double[16];
    private int _wN;
    private int _wBehind;
    private int _wLast;
    private string _clearFail = "";
    private readonly Vector2[] _trackPoints = new Vector2[128];
    private readonly float[] _trackZ = new float[128];
    private readonly bool[] _trackFirm = new bool[128];
    private readonly double[] _trackHeading = new double[128];

    /// <summary>Movement key changes sent on a step the predictive steering drove (of <see cref="KeyChanges"/>; report-only).</summary>
    public int PredictKeyChanges { get; private set; }

    /// <summary>Ground the body covered over the run, in metres (report-only).</summary>
    public double MovedMetres { get; private set; }

    /// <summary>
    /// Ground covered in straight runs of <see cref="LongStraightRunMetres"/> or more: forward held with no turn key, from the key that began it to the one that ended it
    /// (report-only; the owner's wish for longer straight runs of 2026-11-16).
    /// </summary>
    public double LongStraightMetres { get; private set; }

    /// <summary>The length a straight run must reach to count in <see cref="LongStraightMetres"/>.</summary>
    public const float LongStraightRunMetres = 10f;

    private bool _pDriving;
    private float _straightRun;

    /// <summary>A turn pressed or forward let go: the straight run under way ends (<see cref="LongStraightMetres"/>).</summary>
    private void EndStraightRun()
    {
        if (_straightRun >= LongStraightRunMetres)
        {
            LongStraightMetres += _straightRun;
        }

        _straightRun = 0f;
    }

    /// <summary>The motion measures (update interval, speed, turn rates running and standing, a key's delay), from the body's own moves; called every step.</summary>
    private void NotePredictMotion(double now, Vector2 global, float headingDegrees)
    {
        if (!_pmStarted)
        {
            if (_body.TurnRate is var said && said > 0f)
            {
                _pRunRate = _pStandRate = said; // the body's own word until its turns are measured
            }

            _pmStarted = true;
            _pmTime = now;
            _pmGlobal = global;
            _pmHeading = headingDegrees;
            _pmTurn = _pmTurn2 = _turn;
            _pmForward = _pmForward2 = _forward;
            _physicsStepped = true;
            return;
        }

        var dt = now - _pmTime;
        var moved = Vector2.Distance(global, _pmGlobal);
        var outdoorsNow = LandDefs.IsOutdoorCell(_body.Position.CellId);
        if (_pTopOutdoors is { } was && was != outdoorsNow)
        {
            _pTop = float.NaN; // through a doorway: the top speed measured indoors is not the land's (a hall's doorway walk): it starts again from the body's run speed
            _pTopFromRun = true;
        }

        _pTopOutdoors = outdoorsNow;
        if (dt > 0 && moved < 5f)
        {
            MovedMetres += moved; // not a teleport
            _straightRun = _forward && _turn == 0 ? _straightRun + moved : 0f;
        }

        var turned = Normalize(headingDegrees - _pmHeading);
        var moving = moved > 1e-4f || Math.Abs(turned) > 1e-3;
        _physicsStepped = dt > 0 && (moving || dt >= MotionStillSeconds);
        if (!_physicsStepped)
        {
            return;
        }

        var speedNow = (float)(moved / dt);
        var velocityNow = (global - _pmGlobal) / (float)dt;
        _pVelocity = moving && _pmMoving ? _pVelocity + (0.5f * (velocityNow - _pVelocity)) : velocityNow;
        var ramp = 1.0 - Math.Exp(-dt / SpeedRampSeconds);
        _pVModel += ((_forward ? PredictTop() : 0.0) - _pVModel) * ramp;
        _pSlow = Math.Abs(speedNow - _pVModel) > 2.0 ? _pSlow + 1 : 0;
        if (_pSlow >= 2)
        {
            _pVModel = speedNow; // the body is not moving as its keys say (a wall, a stair, a slide): its own speed
        }

        _pSpeed = moving && _pmMoving ? _pSpeed + (0.35f * (speedNow - _pSpeed)) : speedNow;
        if (moving && _pmMoving && dt <= 0.2)
        {
            _pUpdate += 0.2 * (dt - _pUpdate);
            var steady = _turn == _pmTurn && _forward == _pmForward && _turn == _pmTurn2 && _forward == _pmForward2;
            var top = PredictTop();
            if (steady && _turn != 0)
            {
                var rate = Math.Abs(turned) / dt;
                if (_forward && _pSpeed > 0.6f * top)
                {
                    _pRunRate += 0.3 * (rate - _pRunRate);
                }
                else if (!_forward && _pSpeed < 0.3f)
                {
                    _pStandRate += 0.3 * (rate - _pStandRate);
                }
            }

            if (steady && _forward && _turn == 0 && now - _forwardSince > 0.6 && _body.RunSpeed is var run && run > 0f && _pSpeed > 0.5f * run && _pSpeed < 1.5f * run)
            {
                _pTop = float.IsNaN(_pTop) ? (_pTopFromRun ? run : _pSpeed) : _pTop + (0.05f * (_pSpeed - _pTop));
            }
        }

        if (_pPressPending && moving)
        {
            _pPressPending = false;
            var expected = (_forward ? _pRunRate : _pStandRate) * dt;
            _pDelay += 0.3 * ((Math.Abs(turned) > 0.3 * expected ? 0.0 : 1.0) - _pDelay);
        }

        _pmMoving = moving;
        _pmTime = now;
        _pmGlobal = global;
        _pmHeading = headingDegrees;
        _pmTurn2 = _pmTurn;
        _pmForward2 = _pmForward;
        _pmTurn = _turn;
        _pmForward = _forward;
    }

    /// <summary>The body's speed now: the ramp's own (from the forward key's times and the run speed), unless the body has moved much slower than it for two updates (a wall, a stair).</summary>
    private float PredictSpeed() => (float)_pVModel;

    private float PredictTop() => !float.IsNaN(_pTop) ? _pTop : _body.RunSpeed is var r && !float.IsNaN(r) && r > 0f ? r : MathF.Max(_speed, 1f);

    /// <summary>
    /// The predictive steering's keys for this step: turn and forward from the cheapest predicted plan that keeps to the route's cells and clear of its statics; false to leave the
    /// step to the ordinary steering.
    /// </summary>
    private bool PredictiveSteer(NavRoute route, NavPoint here, in CellFrame position, int next)
    {
        var vertices = route.Vertices;
        if (next >= vertices.Count || ReferenceEquals(route, _pGaveUpOn))
        {
            return false;
        }

        _pOutdoors = LandDefs.IsOutdoorCell(position.CellId);
        if (!ReferenceEquals(route, _sRoute))
        {
            SimplifyRoute(route);
        }

        if ((_sRouteCluttered && !_pOutdoors) || route.ArcLength < PredictShortRouteMetres)
        {
            PredictHandedSteps++;
            return false; // indoors a route by statics, or a short one: the ordinary steering's, every step of it (outdoors the land map holds the statics' shapes)
        }

        if (!_physicsStepped && (_turn != 0 || _forward) && !_pHandedLast)
        {
            return true; // the keys held until the physics moves the body again (the predictive steering decides on the body's own moves)
        }

        var handedBefore = _pHandedLast;
        _pHandedLast = false;

        // A run that has made no ground along the route for PredictStallSeconds (pressed to something the sweeps do not see, or turning about) leaves the rest of the route to
        // the ordinary steering, before the stuck ladder would climb a rung.
        if (!ReferenceEquals(route, _pStallRoute) || _steerProgress > _pStallArc + PredictStallMetres || !_forward || _turn != 0 || _commitActive)
        {
            _pStallRoute = route;
            _pStallArc = _steerProgress;
            _pStallSince = _lastTime;
        }
        else if (_lastTime - _pStallSince > PredictStallSeconds)
        {
            _pGaveUpOn = route;
            PredictGaveUp++;
            _commitActive = false;
            _pStopping = false;
            return false;
        }

        var headingDegrees = (double)RetailFrames.GetHeading(position.Frame);
        var delay = _pDelay >= 0.5 ? 1 : 0;
        var dtU = Math.Clamp(_pUpdate, 1.0 / 60.0, 0.1);

        if (_pStopping)
        {
            if (_pSpeed < 0.2f && _lastTime - _pStopAt > 0.6)
            {
                _pStopping = false; // coasted to a stop short of the goal (a slope, a step): the run goes on
                var toGoal = vertices[^1].Global - here.Global;
                var goalBearing = Math.Atan2(toGoal.X, toGoal.Y) * (180.0 / Math.PI);
                if (toGoal.Length() <= PredictWalkInMetres && Math.Abs(Normalize(goalBearing - headingDegrees)) <= PredictStandMinDegrees)
                {
                    _pGaveUpOn = route;
                    PredictHandedSteps++;
                    return false; // the goal a metre ahead: the ordinary steering's walk near a stop takes the rest (a run restarted there overshoots and turns back)
                }
            }
            else
            {
                SetTurn(0);
                SetForward(false);
                return true;
            }
        }

        // A turn under way is held to the heading it was planned to: the key goes up on the update after which the turn still to come lands within half an update's turn of it (one
        // update earlier when the host's keys wait an update). No decision is made mid-turn.
        if (_commitActive && (_turn != _commitSide || !ReferenceEquals(route, _commitRoute)))
        {
            _commitActive = false; // its key let go by another hand (a rung, the ordinary steering, a new plan): no turn is held that is not pressed
        }

        if (_commitActive)
        {
            var perUpdate = (_forward ? _pRunRate : _pStandRate) * dtU;
            var remaining = Normalize(_commitHeading - headingDegrees) * _commitSide;
            if (remaining > perUpdate * (0.5 + delay))
            {
                return true; // keys as they are
            }

            _commitActive = false;
            SetTurn(0);
            if (_commitForwardAfter)
            {
                SetForward(true);
            }

            return true;
        }

        var top = PredictTop();
        BuildWindow(route, next, top);
        _pAirSince = _body.OnGround ? null : _pAirSince ?? _lastTime;
        var airborne = _pAirSince is { } air && _lastTime - air > PredictAirSeconds;
        if (_pOutdoors && DoorWithin(route, next, PredictDoorHandOverMetres))
        {
            PredictHandedSteps++;
            _pHandedLast = true;
            _commitActive = false;
            return false; // outdoors near a building's door: the ordinary steering's doorway rules take the approach and the doorway
        }

        if (airborne || WindowLayered(_wRawFrom, _wRawTo) || WindowHairpin())
        {
            if (PredictTrace is not null)
            {
                Trace(string.Create(CultureInfo.InvariantCulture, $"t={_lastTime:F2} handed {(airborne ? "air" : WindowLayered(_wRawFrom, _wRawTo) ? "layered" : "hairpin")} window={_wRawFrom}-{_wRawTo} next={next}"));
            }

            PredictHandedSteps++;
            _pHandedLast = true;
            _commitActive = false;
            return false; // in the air, a route ahead that passes over or under itself, or a hairpin: the ordinary steering takes the step
        }

        var start = ProjectBody(here.Global, here.Z);
        var goalArc = _wS[_wN - 1] + _wL[_wN - 1];
        var atRouteEnd = _wLast == vertices.Count - 1;
        var speed = PredictSpeed();
        if (_pOutdoors && !_pStopping && (speed < PredictStandingSpeed || (!_forward && _turn != 0 && handedBefore)))
        {
            PredictHandedSteps++;
            _pHandedLast = true;
            _commitActive = false;
            return false; // standing outdoors: the ordinary steering's start (a turn in place to within TurnInPlaceDegrees, then a run that turns the rest); the plans take the run
        }

        // The stop: forward goes up where the coast from the speed now (the update under way first, and a key's delay) carries the body to the goal, straight on along its
        // heading and through the goal's stop tolerance.
        var stops = Options.Stop && atRouteEnd;
        if (stops)
        {
            if (speed < PredictStandingSpeed && Vector2.Distance(here.Global, vertices[^1].Global) <= (_pOutdoors ? WalkNearStopMetres : PredictStopLineMetres))
            {
                _pGaveUpOn = route;
                PredictHandedSteps++;
                _commitActive = false;
                return false; // standing on the route's end (outdoors within the walk near a stop) and not arrived (an object goal's range ends short of it): the ordinary steering's arrival rules take the rest
            }

            var remaining = goalArc - start.Arc;
            var coast = (speed * SpeedRampSeconds) + (speed * dtU * (0.5 + delay));
            // An object goal's route ends where its range begins (RouteConfirmer.ArcBeyondRange): the coast aims a coast's length and half a metre inside the range, so the
            // body is still running when it enters the range and the runner's arrival lets the keys go there, as the ordinary steering's run does.
            var coastMetres = (float)(speed * (SpeedRampSeconds + (dtU * (0.5 + delay))));
            var stopAt = vertices[^1].Global;
            if (_goal!.IsObject)
            {
                var g = _goal.Point.Global;
                var off = Vector2.Distance(stopAt, g);
                var inside = MathF.Max(0f, _goal.Range - 0.5f - coastMetres);
                if (off > inside && off > 1e-3f)
                {
                    stopAt = g + ((stopAt - g) * (inside / off));
                }
            }

            if (CoastMeetsGoal(here.Global, headingDegrees * (Math.PI / 180.0), speed * (SpeedRampSeconds + (dtU * (0.5 + delay))), stopAt))
            {
                SetTurn(0);
                SetForward(false);
                _pStopping = true;
                _pStopAt = _lastTime;
                if (PredictTrace is not null)
                {
                    Trace(string.Create(CultureInfo.InvariantCulture, $"t={_lastTime:F2} stop remaining={remaining:F2} coast={coast:F2} v={speed:F2}"));
                }

                return true;
            }
        }

        // Following the plan: while the body is where the last plan put it (each update's place and heading, within PredictFollowMetres and a degree) and the plan holds its keys
        // for updates more, the keys are held without planning again; a deviation, a route changed or the plan's next key ends it.
        if (_followCount > 0 && ReferenceEquals(route, _followRoute) && _turn == 0 && _forward && _physicsStepped)
        {
            if (Vector2.Distance(here.Global, _followAt[_followIndex]) <= PredictFollowMetres && Math.Abs(Normalize(headingDegrees - _followHeading[_followIndex])) <= 1.0)
            {
                _followIndex++;
                if (_followIndex < _followCount)
                {
                    PredictFollowed++;
                    return true; // keys as they are
                }
            }
        }

        _followCount = 0;

        var steps = Math.Clamp((int)Math.Ceiling(PredictHorizonSeconds / dtU), 8, _trackPoints.Length);
        var runRate = _pRunRate * (Math.PI / 180.0);
        var standRate = _pStandRate * (Math.PI / 180.0);
        var heading = headingDegrees * (Math.PI / 180.0);
        var maxQuanta = Math.Clamp((int)Math.Ceiling(Math.PI / (Math.Min(runRate, standRate) * dtU)), 1, steps - delay - 1);
        var ctx = new PredictContext(here.Global, heading, speed, top, dtU, runRate, standRate, delay, steps, start.Arc, goalArc, stops, vertices[^1].Global, SpeedRampSeconds + (dtU * (0.5 + delay)), _goal!.StopTolerance - 0.05f, start.Segment, start.Arc >= goalArc - 0.05f);

        SetUpdateConstants(ctx);
        NoteDrift(heading, speed);
        Span<PredictPlan> best = stackalloc PredictPlan[PredictCellChecks];
        var bestCount = 0;
        var keepBest = new PredictPlan(0, 0, true, 1, 0, 0, true, float.MaxValue);
        EnumeratePlans(ctx, 0, 0, true, best, ref bestCount, ref keepBest);
        if (ctx.Stops)
        {
            EnumeratePlans(ctx, 0, 0, false, best, ref bestCount, ref keepBest); // near the goal: forward let go now, the coast to the goal
        }

        for (var side = -1; side <= 1; side += 2)
        {
            if ((_turn != 0 && side == -_turn) || (_turn == 0 && side == -_releasedTurn && _lastTime - _turnReleasedAt < FlipCountSeconds))
            {
                continue; // never the other way within a flip's time of a release (D134's flips: the aim jumping)
            }

            // Every turn's length to a few updates, then every other (a long turn's last update is refined by the plans after it); a turn with forward let go only where it
            // is a turn in place's (the body slow, or a turn of PredictStandMinDegrees or more).
            for (var quanta = 1; quanta <= maxQuanta; quanta += quanta < PredictFineQuanta ? 1 : 2)
            {
                var standingTurn = !_pOutdoors && speed < PredictStandingSpeed && quanta * standRate * dtU > PredictStandMinDegrees * (Math.PI / 180.0);
                if (!standingTurn)
                {
                    EnumeratePlans(ctx, side, quanta, true, best, ref bestCount, ref keepBest); // a body standing still turns past PredictStandMinDegrees in place before it runs
                }

                if (ctx.Stops || speed < 0.3f * top || quanta * standRate * dtU >= PredictStandMinDegrees * (Math.PI / 180.0))
                {
                    EnumeratePlans(ctx, side, quanta, false, best, ref bestCount, ref keepBest);
                }
            }
        }

        // The cheapest plan that keeps to the cells and clear of the statics: one that only brushes them at the body's radius (the physics slides it along) costs
        // PredictGrazeWeight; the checks stop once no cheaper plan is left (a brush only adds).
        var found = -1;
        var foundCost = float.MaxValue;
        var keepAdjusted = float.NaN;
        for (var i = 0; i < bestCount; i++)
        {
            var candidate = best[i];
            if (candidate.Cost >= foundCost)
            {
                break;
            }

            SimulatePlan(ctx, candidate, record: true, out var count);
            var graze = TrackGraze(position, count, dtU);
            if (graze == 2)
            {
                continue;
            }

            var adjusted = candidate.Cost + GrazeCost(graze);
            if (candidate.S1 == 0)
            {
                keepAdjusted = Math.Min(float.IsNaN(keepAdjusted) ? float.MaxValue : keepAdjusted, adjusted);
            }

            if (adjusted < foundCost)
            {
                found = i;
                foundCost = adjusted;
            }
        }

        if (found >= 0 && bestCount > 0 && foundCost > best[0].Cost + PredictGiveUpCost)
        {
            found = -1; // every sensible plan was refused and only a detour is clear (a turn away from a goal by a wall): the ordinary steering's close rules take the step
        }

        if (found >= 0 && best[found].S1 == 0 && !best[found].F1 && speed < 0.5f)
        {
            found = -1; // stopped short and nothing clear but standing: the ordinary steering walks the last of it (a goal in a static's margin, the platform at the Facility Hub's drop)
        }

        var keepChosen = false;
        if (found >= 0 && best[found].S1 != 0)
        {
            // A turn now that is not clearly cheaper than holding on is not pressed (no taps), when holding on keeps to the cells.
            if (float.IsNaN(keepAdjusted) && keepBest.Cost < float.MaxValue)
            {
                SimulatePlan(ctx, keepBest, record: true, out var count);
                var graze = TrackGraze(position, count, dtU);
                keepAdjusted = graze == 2 ? float.NaN : keepBest.Cost + GrazeCost(graze);
            }

            keepChosen = !float.IsNaN(keepAdjusted) && foundCost > keepAdjusted - PredictTurnMargin;
        }

        var straightOn = false;
        if (found == -1 && _turn == 0 && _forward)
        {
            // Holding on is only for a block further on than the next plans' firm reach: running straight into a near one is no hold.
            SimulatePlan(ctx, new PredictPlan(0, 0, true, steps, 0, 0, true, 0f), record: true, out var count);
            straightOn = TrackGraze(position, count, dtU) != 2;
        }

        if (found == -1 && _pHoldSince is null && straightOn)
        {
            _pHoldSince = _lastTime;
        }

        if (found == -1 && straightOn && _pHoldSince is { } holding && _lastTime - holding < PredictHoldSeconds)
        {
            if (PredictTrace is not null)
            {
                Trace(string.Create(CultureInfo.InvariantCulture, $"t={_lastTime:F2} hold hd={headingDegrees:F1} at=({position.Origin.X:F2},{position.Origin.Y:F2}) why={_clearFail}"));
            }

            return true; // no plan keeps to the cells this update: the run holds on as it is a moment (the next plans see the body nearer), no single step of the ordinary steering's keys between plans
        }

        if (found >= 0)
        {
            _pHoldSince = null;
        }

        if (found == -1)
        {
            PredictFallbacks++;
            _pHandedLast = true;
            _commitActive = false;
            if (PredictTrace is not null)
            {
                Trace(string.Create(CultureInfo.InvariantCulture, $"t={_lastTime:F2} fallback hd={headingDegrees:F1} off={start.Distance:F2} v={speed:F2} at=({position.Origin.X:F2},{position.Origin.Y:F2}) cell={position.CellId & 0xFFFF:X4} why={_clearFail}"));
            }

            return false; // no plan keeps to the cells: the ordinary steering takes this step
        }

        var chosen = keepChosen || (best[found].S1 == 0 && best[found].F1) ? keepBest with { S1 = 0, N1 = 0, F1 = true } : best[found];
        if (PredictTrace is not null)
        {
            var parts = new System.Text.StringBuilder();
            for (var i = 0; i < Math.Min(bestCount, 5); i++)
            {
                var b = best[i];
                parts.Append(CultureInfo.InvariantCulture, $" [{b.S1},{b.N1},{(b.F1 ? "f" : "s")},{b.W},{b.S2},{b.N2}:{b.Cost:F2}]");
            }

            Trace(string.Create(
                CultureInfo.InvariantCulture,
                $"t={_lastTime:F2} hd={headingDegrees:F1} leg={start.Bearing * 180 / Math.PI:F1} off={start.Distance:F2} v={speed:F2} top={top:F2} dt={dtU:F3} delay={delay} rates={_pStandRate:F0}/{_pRunRate:F0} held=({_turn},{_forward}) keep={keepBest.Cost:F2}/{keepAdjusted:F2} best{parts} chosen=({chosen.S1},{chosen.N1},{chosen.F1},{chosen.W},{chosen.S2},{chosen.N2})"));
        }

        SetBack(false);
        SetSide(0);
        SetRun(true);
        SetForward(chosen.F1);
        _steerCorner = 0;
        if (chosen.S1 != 0)
        {
            var rate = chosen.F1 ? _pRunRate : _pStandRate;
            var turned = chosen.S1 * chosen.N1 * rate * dtU;
            _commitHeading = ((headingDegrees + turned) % 360.0 + 360.0) % 360.0;
            if (TurnsAtCorner(start.Segment, heading, (headingDegrees + turned) * (Math.PI / 180.0), rate * dtU * (Math.PI / 180.0))
                || TurnsIntoBend(start, chosen.S1, chosen.N1))
            {
                _steerCorner = ++_cornerSerial; // a deliberate turn onto the route's next leg (D134's corner counters: a right-then-left round the den's grid is no swing)
                _steerCornerRunning = true;
            }

            if (PredictTrace is not null)
            {
                Trace(string.Create(CultureInfo.InvariantCulture, $"t={_lastTime:F2} press side={chosen.S1} n={chosen.N1} corner={_steerCornerRunning} seg={start.Segment} legs={_wB[start.Segment] * 180 / Math.PI:F0}>{(start.Segment + 1 < _wN ? _wB[start.Segment + 1] * 180 / Math.PI : double.NaN):F0} off={start.Signed:F2}"));
            }

            _commitSide = chosen.S1;
            _commitForwardAfter = !chosen.F1;
            _commitActive = true;
            _commitRoute = route;
            if (_turn != chosen.S1)
            {
                _pPressPending = true;
            }
        }

        SetTurn(chosen.S1);
        _steerCorner = 0;
        _steerCornerRunning = false;
        if (chosen.S1 == 0 && chosen.W >= 2 && _forward && delay == 0)
        {
            SimulatePlan(ctx, chosen, record: true, out var count);
            _followCount = Math.Min(Math.Min(chosen.W - 1, PredictFollowUpdates), count);
            _followIndex = 0;
            _followRoute = route;
            for (var i = 0; i < _followCount; i++)
            {
                _followAt[i] = _trackPoints[i];
                _followHeading[i] = _trackHeading[i];
            }
        }

        return true;
    }

    /// <summary>How many updates at most the keys are held on the last plan without planning again (<see cref="PredictFollowMetres"/>). Not retail: Lifestone's own.</summary>
    public const int PredictFollowUpdates = 3;

    /// <summary>How near the place the last plan put the body it must be for the plan to be followed without planning again, in metres. Not retail: Lifestone's own.</summary>
    public const float PredictFollowMetres = 0.15f;

    /// <summary>Updates the predictive steering held its keys on the last plan, the body where the plan said, without planning again. Report-only.</summary>
    public int PredictFollowed { get; private set; }

    private readonly Vector2[] _followAt = new Vector2[PredictFollowUpdates];
    private readonly double[] _followHeading = new double[PredictFollowUpdates];
    private int _followCount;
    private int _followIndex;
    private NavRoute? _followRoute;

    /// <summary>
    /// The least bend of the steering's line, in degrees, whose turn onto the next leg is a corner's (a deliberate turn, D134's corner counters): the den's grid steps, straightened
    /// by the shortcuts (<see cref="PredictShortcutMetres"/>), bend 42 to 48 degrees. Not retail: Lifestone's own.
    /// </summary>
    public const double PredictCornerMinDegrees = 30.0;

    /// <summary>How near the next bend of the steering's line a turn its way of two updates or more is a corner's turn (part of rounding it), in metres. Not retail: Lifestone's own.</summary>
    public const float PredictBendReachMetres = 6f;

    /// <summary>
    /// Whether a turn of <paramref name="quanta"/> updates to <paramref name="side"/> rounds the steering line's next bend (<see cref="PredictCornerMinDegrees"/> or more, its vertex
    /// within <see cref="PredictBendReachMetres"/> on): two updates or more, the bend's way. A route that steps round the den's grid is run as a weave of such turns, each a corner's.
    /// </summary>
    private bool TurnsIntoBend((float Distance, float Signed, float Arc, double Bearing, int Segment, float Along, float Z) at, int side, int quanta)
    {
        if (quanta < 2)
        {
            return false;
        }

        var ahead = _wL[at.Segment] - at.Along;
        for (var j = at.Segment + 1; j < _wN && ahead <= PredictBendReachMetres; j++)
        {
            var bend = NormalizeRadians(_wB[j] - _wB[j - 1]);
            if (Math.Abs(bend) >= PredictCornerMinDegrees * (Math.PI / 180.0))
            {
                return Math.Sign(bend) == side;
            }

            ahead += _wL[j];
        }

        return false;
    }

    /// <summary>Turn presses the predictive steering made onto the route's next leg at a corner (<see cref="PredictCornerMinDegrees"/> or more), running or standing. Report-only.</summary>
    public int PredictCornerTurns { get; private set; }

    private bool _steerCornerRunning;

    /// <summary>
    /// Whether a turn from <paramref name="startHeading"/> to <paramref name="endHeading"/> (radians) lines the body up, within half an update's turn, with the leg it is on or one ahead,
    /// the turn or the leg's bend from <paramref name="segment"/> being <see cref="PredictCornerMinDegrees"/> or more.
    /// </summary>
    private bool TurnsAtCorner(int segment, double startHeading, double endHeading, double perUpdate)
    {
        var corner = PredictCornerMinDegrees * (Math.PI / 180.0);
        var turnsEnough = Math.Abs(NormalizeRadians(endHeading - startHeading)) >= corner;
        for (var j = segment; j < _wN; j++)
        {
            if ((turnsEnough || (j > segment && Math.Abs(NormalizeRadians(_wB[j] - _wB[segment])) >= corner))
                && Math.Abs(NormalizeRadians(endHeading - _wB[j])) <= Math.Max(0.5 * perUpdate, 10.0 * (Math.PI / 180.0)))
            {
                return true; // a turn of a corner's size onto a leg (the one the body is on, after a cut corner, or one ahead), or onto a leg ahead that bends a corner's size
            }
        }

        return false;
    }

    private static void Trace(string line) => PredictTrace?.Invoke(line);

    /// <summary>What every candidate plan starts from.</summary>
    private readonly record struct PredictContext(Vector2 At, double Heading, float Speed, float Top, double Update, double RunRate, double StandRate, int Delay, int Steps, float StartArc, float GoalArc, bool Stops, Vector2 Goal, double CoastSeconds, float Arrive, int StartSegment, bool StartPast);

    /// <summary>
    /// A plan of keys: turn <see cref="S1"/> (-1 left, 1 right, 0 none) for <see cref="N1"/> updates with forward held or not (<see cref="F1"/>), then run straight
    /// <see cref="W"/> updates, then turn <see cref="S2"/> for <see cref="N2"/> updates with forward held or not (<see cref="F2"/>), then run straight to the horizon; and its cost.
    /// </summary>
    private readonly record struct PredictPlan(int S1, int N1, bool F1, int W, int S2, int N2, bool F2, float Cost);

    /// <summary>The body's predicted state, an update at a time.</summary>
    private struct PredictState
    {
        public Vector2 At;
        public double Heading;
        public double Speed;
        public Vector2 Drift;
        public float Cost;
        public float Arc;
        public double Bearing;
        public bool Stopped;
        public bool Coasting;
        public float GoalMin;
        public int Segment;
        public double Sin;
        public double Cos;
    }

    // The update's constants, once a decision (a turn of an update rotates the heading's sine and cosine; no trigonometry an update).
    private double _kBlend;
    private double _kRunCos;
    private double _kRunSin;
    private double _kRunHalfCos;
    private double _kRunHalfSin;
    private double _kStandCos;
    private double _kStandSin;
    private double _kStandHalfCos;
    private double _kStandHalfSin;

    private void SetUpdateConstants(in PredictContext c)
    {
        _kBlend = 1.0 - Math.Exp(-c.Update / SpeedRampSeconds);
        (_kRunSin, _kRunCos) = Math.SinCos(c.RunRate * c.Update);
        (_kRunHalfSin, _kRunHalfCos) = Math.SinCos(c.RunRate * c.Update * 0.5);
        (_kStandSin, _kStandCos) = Math.SinCos(c.StandRate * c.Update);
        (_kStandHalfSin, _kStandHalfCos) = Math.SinCos(c.StandRate * c.Update * 0.5);
    }

    /// <summary>
    /// The plans that begin with the first turn (<paramref name="side"/> for <paramref name="quanta"/> updates, forward as <paramref name="forward"/> says; a side of 0: no turn), each
    /// with the straight run after it and a second turn that lines the heading up with the route's leg there or the leg after it (or none): the cheapest few into
    /// <paramref name="best"/>, and the cheapest that holds on now into <paramref name="keepBest"/>. The first turn and the straight run are played once and shared.
    /// </summary>
    private void EnumeratePlans(in PredictContext c, int side, int quanta, bool forward, Span<PredictPlan> best, ref int bestCount, ref PredictPlan keepBest)
    {
        var s = Begin(c);
        if (side == 0 && !forward)
        {
            // The coast: forward let go now and the keys left up (a stopping plan's), the coast carrying the body as far as it goes.
            var coastKeys = PlanKeyChanges(_turn, _forward, 0, false, c.Delay * c.Update);
            s.Coasting = true;
            for (var q = 0; q < c.Steps && !s.Stopped; q++)
            {
                Advance(c, ref s, 0, false);
            }

            Offer(new PredictPlan(0, 0, false, 0, 0, 0, true, Finish(c, s) + (PredictKeyWeight * coastKeys)), best, ref bestCount, ref keepBest);
            return;
        }

        var keys = 0f;
        var k = 0;
        for (; k < c.Delay; k++)
        {
            Advance(c, ref s, _turn, _forward);
        }

        keys += PlanKeyChanges(_turn, _forward, side, forward, c.Delay * c.Update);
        for (var q = 0; q < quanta && k < c.Steps; q++, k++)
        {
            Advance(c, ref s, side, forward);
        }

        if (side != 0)
        {
            keys += 1f; // the first turn's release
        }

        if (!forward)
        {
            keys += 1f + (PredictRepressWeight / PredictKeyWeight); // forward pressed again after the turn in place
        }

        var releasedAt = (k * c.Update) - c.Update;
        var lastReleased = side != 0 ? side : (_turn != 0 ? 0 : _releasedTurn);
        var firstAtCorner = side != 0 && TurnsAtCorner(c.StartSegment, c.Heading, s.Heading, c.RunRate * c.Update);
        if (side == 0)
        {
            releasedAt = _turnReleasedAt - _lastTime;
        }

        // The straight run, with a branch at each update for the second turn (a plan that holds on now runs straight at least one update).
        var first = k;
        if (side == 0 && k < c.Steps)
        {
            Advance(c, ref s, 0, true);
            k++;
        }

        while (true)
        {
            if (k >= c.Steps || s.Stopped)
            {
                // No second turn: straight to the horizon.
                var cost = Finish(c, s) + (PredictKeyWeight * keys);
                Offer(new PredictPlan(side, quanta, forward, k - first, 0, 0, true, cost), best, ref bestCount, ref keepBest);
                break;
            }

            var p = Project(s.At, s.Segment, PredictSegmentReach);
            var branchHere = k - first < PredictFineQuanta || (k - first) % 2 == 0; // the second turn's start to a few updates, then every other
            for (var option = 0; option < 2 && branchHere; option++)
            {
                var target = option == 0 ? _wB[p.Segment] : p.Segment + 1 < _wN ? _wB[p.Segment + 1] : double.NaN;
                if (double.IsNaN(target) || (option == 1 && Math.Abs(NormalizeRadians(_wB[p.Segment] - target)) < 1e-3))
                {
                    continue;
                }

                var error = NormalizeRadians(target - s.Heading);
                var n2 = (int)Math.Round(Math.Abs(error) / (c.RunRate * c.Update));
                var s2 = Math.Sign(error);
                if (n2 == 0 || (side != 0 && k == first && s2 == side) || (s2 == -lastReleased && (k * c.Update) - releasedAt < FlipCountSeconds)
                    || (side != 0 && s2 == -side && !firstAtCorner && option == 1 && Math.Abs(NormalizeRadians(target - _wB[p.Segment])) >= PredictCornerMinDegrees * (Math.PI / 180.0)))
                {
                    continue; // no turn, the first turn made longer (its own plan), a flip, or a swing out the other way before a corner (D134: no swing at a junction)
                }

                for (var held = 0; held < 2; held++)
                {
                    var forward2 = held == 0;
                    if (!forward2 && n2 * c.RunRate * c.Update < 1.0)
                    {
                        continue; // forward let go for a sharp turn only (a corner the run's radius cannot take)
                    }

                    var branch = s;
                    var branchKeys = keys + 2f + (forward2 ? 0f : 2f + (PredictRepressWeight / PredictKeyWeight));
                    var gap = (k * c.Update) - releasedAt;
                    if (s2 == -lastReleased && gap <= ReversalCountSeconds)
                    {
                        branchKeys += (gap < FlipCountSeconds ? PredictFlipWeight : PredictReversalWeight) / PredictKeyWeight;
                    }
                    else if (s2 == lastReleased && gap <= PredictStutterSeconds)
                    {
                        branchKeys += PredictStutterWeight / PredictKeyWeight;
                    }

                    var kk = k;
                    var pruned = false;
                    for (var q = 0; q < n2 && kk < c.Steps; q++, kk++)
                    {
                        Advance(c, ref branch, s2, forward2);
                    }

                    for (; kk < c.Steps && !branch.Stopped; kk++)
                    {
                        if ((kk & 3) == 0 && CannotWin(c, branch, branchKeys, kk, side, best, bestCount, keepBest))
                        {
                            pruned = true;
                            break;
                        }

                        Advance(c, ref branch, 0, true);
                    }

                    if (pruned)
                    {
                        continue;
                    }

                    var branchCost = Finish(c, branch) + (PredictKeyWeight * branchKeys);
                    Offer(new PredictPlan(side, quanta, forward, k - first, s2, n2, forward2, branchCost), best, ref bestCount, ref keepBest);
                }
            }

            Advance(c, ref s, 0, true);
            k++;
        }
    }

    /// <summary>
    /// Whether a plan under way at update <paramref name="k"/> cannot end cheaper than the plans kept (and, holding on now, than the cheapest that holds on): its cost so far and
    /// its keys, less all the ground it could still gain at the run's top speed, already exceed them (every other term only adds). Allocates nothing.
    /// </summary>
    private static bool CannotWin(in PredictContext c, in PredictState s, float keys, int k, int side, ReadOnlySpan<PredictPlan> best, int bestCount, in PredictPlan keepBest)
    {
        if (bestCount < best.Length)
        {
            return false;
        }

        var reach = (s.Arc - c.StartArc) + (float)(Math.Max(s.Speed, c.Top) * c.Update * (c.Steps - k));
        var bound = s.Cost + (PredictKeyWeight * keys) - (PredictProgressWeight * reach);
        var bar = best[bestCount - 1].Cost;
        if (side == 0)
        {
            bar = Math.Max(bar, keepBest.Cost);
        }

        return bound >= bar;
    }

    /// <summary>One plan's track and cost, played again in full (<paramref name="record"/>: its points kept in <see cref="_trackPoints"/>, one an update).</summary>
    private void SimulatePlan(in PredictContext c, in PredictPlan plan, bool record, out int count)
    {
        var s = Begin(c);
        s.Coasting = plan.S1 == 0 && !plan.F1; // the coast plan (EnumeratePlans)
        count = 0;
        var k = 0;
        for (; k < c.Steps && !s.Stopped; k++)
        {
            int turn;
            bool forward;
            var j = k - c.Delay;
            if (k < c.Delay)
            {
                (turn, forward) = (_turn, _forward);
            }
            else if (plan.S1 != 0 && j < plan.N1)
            {
                (turn, forward) = (plan.S1, plan.F1);
            }
            else if (plan.S2 != 0 && j >= plan.N1 + plan.W && j < plan.N1 + plan.W + plan.N2)
            {
                (turn, forward) = (plan.S2, plan.F2);
            }
            else
            {
                (turn, forward) = (0, true);
            }

            Advance(c, ref s, turn, forward);
            if (record && count < _trackPoints.Length)
            {
                _trackHeading[count] = s.Heading * (180.0 / Math.PI);
                _trackPoints[count++] = s.At;
            }
        }
    }

    private PredictState Begin(in PredictContext c)
    {
        var (sin, cos) = Math.SinCos(c.Heading);
        return new() { At = c.At, Heading = c.Heading, Speed = c.Speed, Arc = c.StartArc, GoalMin = float.MaxValue, Segment = c.StartSegment, Sin = sin, Cos = cos, Drift = _pDrift };
    }

    private Vector2 _pDrift;
    private float _pDriftKeep;

    /// <summary>
    /// The body's measured motion that is not its run along its heading (a body still sliding from where it was placed, or falling: the world walk's leg starts), in metres a
    /// second; none below <see cref="PredictDriftMetres"/> a second. The plans carry it on, fading with the speed's ramp on the ground and kept in the air.
    /// </summary>
    private void NoteDrift(double heading, float speed)
    {
        var along = new Vector2((float)Math.Sin(heading), (float)Math.Cos(heading)) * speed;
        var drift = _pVelocity - along;
        _pDrift = drift.Length() >= PredictDriftMetres ? drift : Vector2.Zero;
        _pDriftKeep = _body.OnGround ? (float)(1.0 - _kBlend) : 1f;
    }

    /// <summary>The least drift (<see cref="NoteDrift"/>) the plans carry, in metres a second. Not retail: Lifestone's own.</summary>
    public const float PredictDriftMetres = 1f;

    /// <summary>One update of the body's motion under the keys, and its cost (off the route's line past the band; a stopping plan's miss of the goal, where it reaches it).</summary>
    private void Advance(in PredictContext c, ref PredictState s, int turn, bool forward)
    {
        if (s.Stopped)
        {
            return;
        }

        if (s.Coasting)
        {
            (turn, forward) = (0, false); // the stop's coast: keys up, as the steering lets them go
        }

        var rate = forward ? c.RunRate : c.StandRate;
        var vNext = s.Speed + (((forward ? c.Top : 0.0) - s.Speed) * _kBlend);
        var midSin = s.Sin;
        var midCos = s.Cos;
        if (turn != 0)
        {
            var halfSin = turn * (forward ? _kRunHalfSin : _kStandHalfSin);
            var halfCos = forward ? _kRunHalfCos : _kStandHalfCos;
            midSin = (s.Sin * halfCos) + (s.Cos * halfSin);
            midCos = (s.Cos * halfCos) - (s.Sin * halfSin);
            var fullSin = turn * (forward ? _kRunSin : _kStandSin);
            var fullCos = forward ? _kRunCos : _kStandCos;
            (s.Sin, s.Cos) = ((s.Sin * fullCos) + (s.Cos * fullSin), (s.Cos * fullCos) - (s.Sin * fullSin));
            s.Heading += turn * rate * c.Update;
        }

        var step = (s.Speed + vNext) * 0.5 * c.Update;
        s.At += new Vector2((float)(midSin * step), (float)(midCos * step));
        if (s.Drift != Vector2.Zero)
        {
            s.At += s.Drift * (float)c.Update;
            s.Drift *= _pDriftKeep;
        }

        s.Speed = vNext;
        var p = Project(s.At, s.Segment, PredictSegmentReach);
        s.Segment = p.Segment;
        s.Arc = p.Arc;
        s.Bearing = p.Bearing;
        var nearGoal = c.Stops && c.GoalArc - p.Arc < PredictGoalLineMetres;
        var excess = p.Distance - (nearGoal ? PredictGoalBandMetres : _pOutdoors ? CrossTrackMetres : PredictBandMetres); // outdoors the straight run's own drift before it tracks back
        if (excess > 0f)
        {
            s.Cost += PredictLateralWeight * (nearGoal ? 2f : 1f) * (float)c.Update * excess * excess; // the goal's last metres on its line: a run that comes in off it cannot stop in its tolerance
        }

        if (p.Distance > PredictOffRouteMetres)
        {
            s.Cost += PredictOffRouteWeight; // the runner plans again past OffRouteMetres: a plan that strays this far is no plan
        }

        if (c.Stops)
        {
            // The goal as the run meets it: arrived within the stop tolerance; else forward let go where the coast ends at the goal (as the steering does), and the plan's miss
            // of the goal where it comes to rest or runs past it costs.
            var toGoal = Vector2.Distance(s.At, c.Goal); // where the update ends, as the run's arrival is tested
            s.GoalMin = MathF.Min(s.GoalMin, toGoal);
            if (toGoal <= c.Arrive)
            {
                s.Stopped = true;
                return;
            }

            if (!s.Coasting && CoastMeetsGoal(s.At, s.Sin, s.Cos, s.Speed * c.CoastSeconds, c.Goal))
            {
                s.Coasting = true;
            }

            if (s.Coasting && s.Speed < 0.3)
            {
                var miss = s.GoalMin - c.Arrive;
                if (miss > 0f)
                {
                    s.Cost += PredictShortStopWeight + (PredictStopWeight * miss * miss); // come to rest short of the goal: forward again, dearer than any press
                }

                s.Stopped = true;
            }
        }
    }

    /// <summary>
    /// Whether a coast of <paramref name="coast"/> metres from <paramref name="at"/> along <paramref name="heading"/> (radians) reaches <paramref name="goal"/>: the goal no further on
    /// than the coast's end, not behind the body, and within <see cref="PredictStopLineMetres"/> of its line (inside the goal's stop tolerance).
    /// </summary>
    private static bool CoastMeetsGoal(Vector2 at, double heading, double coast, Vector2 goal)
    {
        var (sin, cos) = Math.SinCos(heading);
        return CoastMeetsGoal(at, sin, cos, coast, goal);
    }

    private static bool CoastMeetsGoal(Vector2 at, double sin, double cos, double coast, Vector2 goal)
    {
        var dir = new Vector2((float)sin, (float)cos);
        var to = goal - at;
        var along = Vector2.Dot(to, dir);
        var across = MathF.Abs((dir.X * to.Y) - (dir.Y * to.X));
        return along <= coast + 0.1 && along >= -0.2f && across <= PredictStopLineMetres + 0.1f;
    }

    /// <summary>A plan's cost at its end: the track's, the heading off the route's leg there (past half an update's turn; none for a plan that reached the goal), less the ground gained.</summary>
    private float Finish(in PredictContext c, in PredictState s)
    {
        var cost = s.Cost;
        if (!s.Stopped)
        {
            var ahead = PointAt(s.Arc + PredictAimMetres) - s.At;
            var bearing = ahead.LengthSquared() > 1e-4f ? Math.Atan2(ahead.X, ahead.Y) : s.Bearing;
            var off = Math.Abs(NormalizeRadians(s.Heading - bearing)) - (0.5 * c.RunRate * c.Update);
            if (off > 0.0)
            {
                cost += (float)Math.Min(PredictHeadingCap, PredictHeadingWeight * off * off);
            }
        }

        return cost - (PredictProgressWeight * (s.Arc - c.StartArc));
    }

    /// <summary>The key changes, in key presses, from the keys held now to a plan's first keys (a turn the other way within <see cref="ReversalCountSeconds"/> of letting one go costs more).</summary>
    private float PlanKeyChanges(int heldTurn, bool heldForward, int turn, bool forward, double at)
    {
        var keys = 0f;
        if (turn != heldTurn)
        {
            keys += heldTurn != 0 && turn != 0 ? 2f : 1f;
            var lastReleased = heldTurn != 0 ? heldTurn : _releasedTurn;
            var releasedAt = heldTurn != 0 ? at : _turnReleasedAt - _lastTime;
            if (turn != 0 && turn == -lastReleased && at - releasedAt <= ReversalCountSeconds)
            {
                keys += (at - releasedAt < FlipCountSeconds ? PredictFlipWeight : PredictReversalWeight) / PredictKeyWeight;
            }
            else if (turn != 0 && turn == lastReleased && at - releasedAt <= PredictStutterSeconds)
            {
                keys += PredictStutterWeight / PredictKeyWeight; // the same way again straight after letting it go: one turn in two presses
            }
        }

        if (forward != heldForward)
        {
            keys += 1f;
            if (forward && _lastTime - _forwardOffAt <= ForwardRepressCountSeconds)
            {
                keys += PredictRepressWeight / PredictKeyWeight;
            }
        }

        return keys;
    }

    /// <summary>A plan into the cheapest few (kept in order), and the cheapest that holds on now (no first turn) into <paramref name="keepBest"/>.</summary>
    private static void Offer(in PredictPlan plan, Span<PredictPlan> list, ref int count, ref PredictPlan keepBest)
    {
        if (plan.S1 == 0 && plan.F1 && plan.Cost < keepBest.Cost)
        {
            keepBest = plan;
        }

        var at = count;
        while (at > 0 && list[at - 1].Cost > plan.Cost)
        {
            at--;
        }

        if (at >= list.Length)
        {
            return;
        }

        for (var k = Math.Min(count, list.Length - 1); k > at; k--)
        {
            list[k] = list[k - 1];
        }

        list[at] = plan;
        count = Math.Min(count + 1, list.Length);
    }

    private static double NormalizeRadians(double r)
    {
        r %= 2.0 * Math.PI;
        if (r > Math.PI)
        {
            r -= 2.0 * Math.PI;
        }
        else if (r < -Math.PI)
        {
            r += 2.0 * Math.PI;
        }

        return r;
    }

    /// <summary>How far off the route's line a vertex may lie and be dropped from the steering's line at no test, in metres (a doorway's pair pushed into its cells, which on a
    /// staircase steps back on itself: 8A02's 0x01C8). Not retail: Lifestone's own.</summary>
    public const float PredictSimplifyMetres = 0.15f;

    /// <summary>How far off the route's line a vertex may lie and be dropped from the steering's line when the shortcut, swept at the body's radius, keeps to the route's cells (a
    /// doorway's jog run straight through), in metres. Not retail: Lifestone's own.</summary>
    public const float PredictShortcutMetres = 0.6f;

    private NavRoute? _sRoute;
    private bool[] _sKeep = new bool[64];
    private readonly List<(int Over, int Under)> _sLayered = [];
    private bool[] _sCluttered = new bool[64];
    private bool _sRouteCluttered;
    private bool[] _sDoor = new bool[64];
    private bool? _pTopOutdoors;
    private bool _pTopFromRun;
    private bool _pHandedLast;
    private bool _pOutdoors;

    /// <summary>
    /// How far along the route before a doorway (or the first indoor point) an outdoor run leaves the steps to the ordinary steering, in metres: its doorway rules line the body up
    /// with the door and walk it through. Measured on Holtburg's walks out (2026-11-16): a whole route into or out of a building left to it pressed 4,556 keys over 106 walks; handed
    /// over 8 m out, 3,832 (6 m: a recovery move at Dryreach's scrivener). Not retail: Lifestone's own.
    /// </summary>
    public const float PredictDoorHandOverMetres = 8f;

    /// <summary>
    /// Steps the predictive steering left to the ordinary steering by rule: the body in the air (<see cref="PredictAirSeconds"/>), a route shorter than
    /// <see cref="PredictShortRouteMetres"/>, the route ahead passing over or under itself (a switchback ramp), a hairpin (<see cref="PredictHairpinDegrees"/>), a dungeon's route that
    /// passes statics (<see cref="PredictClutterMetres"/>), or outdoors a door ahead (<see cref="PredictDoorHandOverMetres"/>). Report-only.
    /// </summary>
    public int PredictHandedSteps { get; private set; }

    /// <summary>How long a predictive run may run (forward held, no turn under way) making no more than <see cref="PredictStallMetres"/> of ground along its route before it leaves the
    /// route to the ordinary steering, in seconds (a rung comes at the stuck ladder's 1.5 s). Not retail: Lifestone's own.</summary>
    public const double PredictStallSeconds = 1.0;

    /// <summary>The ground along the route that counts as progress for <see cref="PredictStallSeconds"/>, in metres. Not retail: Lifestone's own.</summary>
    public const float PredictStallMetres = 0.5f;

    /// <summary>Routes the predictive steering left to the ordinary steering because it made no ground along them for <see cref="PredictStallSeconds"/>. Report-only.</summary>
    public int PredictGaveUp { get; private set; }

    private NavRoute? _pGaveUpOn;
    private NavRoute? _pStallRoute;
    private float _pStallArc;
    private double _pStallSince;

    /// <summary>How long the body is off the ground before the predictive steering leaves the steps to the ordinary steering, in seconds (a fall, a jump's flight: the plans
    /// model a body on its feet; a stair's lip is shorter). Not retail: Lifestone's own.</summary>
    public const double PredictAirSeconds = 0.15;

    private double? _pAirSince;

    /// <summary>The shortest route the predictive steering runs, in metres (a step round a moved doorway point: its legs are shorter than an update's run). Not retail: Lifestone's own.</summary>
    public const float PredictShortRouteMetres = 3f;
    private int _wRawFrom;
    private int _wRawTo;

    /// <summary>
    /// The steering's line of a route, once a route: its vertices simplified (Douglas and Peucker's) to within <see cref="PredictSimplifyMetres"/>, or within
    /// <see cref="PredictShortcutMetres"/> where the shortcut keeps to the route's cells at the body's radius, so a doorway's pair of points, a staircase's half-metre samples or a
    /// door's jog are one leg. The route itself is unchanged (its progress, its arrival, the ordinary steering).
    /// </summary>
    private void SimplifyRoute(NavRoute route)
    {
        _sRoute = route;
        var count = route.Vertices.Count;
        if (_sKeep.Length < count)
        {
            _sKeep = new bool[Math.Max(count, _sKeep.Length * 2)];
        }

        Array.Clear(_sKeep, 0, count);
        _sKeep[0] = true;
        _sKeep[count - 1] = true;
        Simplify(route.Vertices, 0, count - 1);
        Array.Clear(_sDoor);
        if (_path is { } doors && doors.Waypoints.Count == count)
        {
            if (_sDoor.Length < count)
            {
                _sDoor = new bool[Math.Max(count, _sDoor.Length * 2)];
            }

            for (var k = 0; k < count; k++)
            {
                _sDoor[k] = doors.Waypoints[k].Doorway || doors.Waypoints[k].Indoor; // a route into or out of a building (PredictDoorHandOverMetres)
            }
        }

        // The pairs of legs where one passes over or under the other (within a body's width seen from above, a storey apart): Tusker Cave's switchback ramps (0x5B46). The
        // predicted tracks are flat, so a window holding both legs of a pair is left to the ordinary steering.
        _sLayered.Clear();
        var vertices = route.Vertices;
        if (_sCluttered.Length < count)
        {
            _sCluttered = new bool[Math.Max(count, _sCluttered.Length * 2)];
        }

        Array.Clear(_sCluttered, 0, count);
        _sRouteCluttered = false;
        if (_rooms is { } rooms && _path is { } path && path.Waypoints.Count == count)
        {
            // The legs that pass a static within PredictClutterMetres (a pot, a pillar, a dais): the room plans go round them by their discs, and a run's arc that cuts by them
            // meets them; such a stretch is the ordinary steering's.
            for (var i = 0; i + 1 < count && !_sRouteCluttered; i++)
            {
                _sCluttered[i] = LegCluttered(rooms, path.Waypoints[i].At, path.Waypoints[i + 1].At);
                _sRouteCluttered |= _sCluttered[i];
            }
        }
        for (var i = 0; i + 1 < count; i++)
        {
            for (var j = i + 2; j + 1 < count; j++)
            {
                if (MathF.Abs(vertices[i].Z - vertices[j].Z) > ArrivalHeightMetres && MathF.Abs(vertices[i + 1].Z - vertices[j + 1].Z) > ArrivalHeightMetres * 0.5f
                    && SegmentsNear(vertices[i].Global, vertices[i + 1].Global, vertices[j].Global, vertices[j + 1].Global, 2f * NavCreatures.PlayerRadiusMetres))
                {
                    _sLayered.Add((i, j));
                }
            }
        }
    }

    /// <summary>Whether segments <paramref name="a"/>-<paramref name="b"/> and <paramref name="c"/>-<paramref name="d"/> come within <paramref name="reach"/> of each other (seen from above).</summary>
    private static bool SegmentsNear(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float reach) =>
        SegmentDistance(a, c, d) <= reach || SegmentDistance(b, c, d) <= reach || SegmentDistance(c, a, b) <= reach || SegmentDistance(d, a, b) <= reach
        || SegmentsCross(a, b, c, d);

    private static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 o, Vector2 p, Vector2 q) => ((p.X - o.X) * (q.Y - o.Y)) - ((p.Y - o.Y) * (q.X - o.X));
        var d1 = Cross(c, d, a);
        var d2 = Cross(c, d, b);
        var d3 = Cross(a, b, c);
        var d4 = Cross(a, b, d);
        return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
    }

    /// <summary>The sharpest bend of the steering's line the predictive steering runs, in degrees; a sharper one (a hairpin: into a doorway and straight back past a dais; a bend round something low the sweeps do not see) is left to the ordinary steering. Not retail: Lifestone's own.</summary>
    public const double PredictHairpinDegrees = 100.0;

    /// <summary>How far along the steering's line its bends add up to a U (<see cref="PredictUTurnDegrees"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float PredictUTurnMetres = 8f;

    /// <summary>The turn the steering's line may make within <see cref="PredictUTurnMetres"/> (a U round a dais in two or three bends) before the ordinary steering takes it, in degrees. Not retail: Lifestone's own.</summary>
    public const double PredictUTurnDegrees = 150.0;

    /// <summary>Whether the steering's line in the window bends by more than <see cref="PredictHairpinDegrees"/> anywhere, or turns through more than <see cref="PredictUTurnDegrees"/> within <see cref="PredictUTurnMetres"/>.</summary>
    private bool WindowHairpin()
    {
        for (var j = 1; j < _wN; j++)
        {
            if (Math.Abs(NormalizeRadians(_wB[j] - _wB[j - 1])) > PredictHairpinDegrees * (Math.PI / 180.0))
            {
                return true;
            }

            var turned = 0.0;
            var along = 0f;
            for (var k = j; k < _wN && along <= PredictUTurnMetres; k++)
            {
                turned += NormalizeRadians(_wB[k] - _wB[k - 1]);
                if (Math.Abs(turned) > PredictUTurnDegrees * (Math.PI / 180.0))
                {
                    return true;
                }

                along += _wL[k];
            }
        }

        return false;
    }

    /// <summary>How near a dungeon route's leg a static makes the whole route the ordinary steering's, in metres (a cave's pots and pillars, a hub's dais: mixing the two steerings on one
    /// route was worse than either, 2026-11-16 again: the stretch near the static alone handed over; and every route driven, a corner cut 1.5 m past the confirmed line ran into a static the
    /// sweeps did not see, Tusker Cave 0x011F, a rung). Outdoors the land map stamps the statics by their physics, and no route is handed over for them. Not retail: Lifestone's own.</summary>
    public const float PredictClutterMetres = 1.2f;

    /// <summary>Whether a static lies within <see cref="PredictClutterMetres"/> of the leg from <paramref name="a"/> to <paramref name="b"/> (sampled every metre, on the line and either side).</summary>
    private static bool LegCluttered(RoomGraph rooms, in CellFrame a, in CellFrame b)
    {
        if ((a.CellId >> 16) != (b.CellId >> 16))
        {
            return false;
        }

        var flat = new Vector2(b.Origin.X - a.Origin.X, b.Origin.Y - a.Origin.Y);
        var length = flat.Length();
        var side = length > 1e-3f ? new Vector2(-flat.Y, flat.X) / length * PredictClutterMetres : Vector2.Zero;
        var samples = Math.Max(1, (int)MathF.Ceiling(length));
        var hint = a.CellId;
        for (var k = 0; k <= samples; k++)
        {
            var on = Vector3.Lerp(a.Origin, b.Origin, (float)k / samples);
            for (var o = -1; o <= 1; o++)
            {
                var at = on + new Vector3(side.X * o, side.Y * o, 0f);
                if (rooms.Cells.FindCell(at with { Z = at.Z + CurveCellLiftMetres }, hint) is { } cell)
                {
                    hint = cell.Id;
                    if (rooms.Planner.InStatic(cell, at))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the window from vertex <paramref name="from"/> to <paramref name="to"/> holds both legs of a pair where one passes over or under the other (<see cref="SimplifyRoute"/>): its flat tracks could then be read on the
    /// wrong storey. The hub walk (2026-11-18): one leg of such a pair alone is no harm, and the rule handed a whole storey's run to the ordinary steering for a leg a storey down that the walk meets 100 m later (the
    /// Facility Hub's crossing from its drop to the Tusker Quarters' front: the middle hall at -6 m over the walk's last rooms at -12 m, 870 of its steps).
    /// </summary>
    private bool WindowLayered(int from, int to)
    {
        foreach (var (over, under) in _sLayered)
        {
            if (over >= from && over < to && under >= from && under < to)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a doorway or an indoor point of the route lies within <paramref name="metres"/> along it ahead of the body, from vertex <paramref name="next"/> on.</summary>
    private bool DoorWithin(NavRoute route, int next, float metres)
    {
        for (var k = next; k < route.Vertices.Count && k < _sDoor.Length; k++)
        {
            if (route.ArcLengthAt(k) - _steerProgress > metres)
            {
                return false;
            }

            if (_sDoor[k])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Douglas and Peucker's simplification of the route's vertices from <paramref name="first"/> to <paramref name="last"/> (kept ends), into <see cref="_sKeep"/>.</summary>
    private void Simplify(IReadOnlyList<NavPoint> vertices, int first, int last)
    {
        if (last - first < 2)
        {
            return;
        }

        var a = vertices[first].Global;
        var b = vertices[last].Global;
        var worst = -1f;
        var at = -1;
        for (var k = first + 1; k < last; k++)
        {
            var d = SegmentDistance(vertices[k].Global, a, b);
            if (d > worst)
            {
                worst = d;
                at = k;
            }
        }

        if (worst <= PredictSimplifyMetres || (worst <= PredictShortcutMetres && ShortcutClear(first, last)))
        {
            return;
        }

        _sKeep[at] = true;
        Simplify(vertices, first, at);
        Simplify(vertices, at, last);
    }

    /// <summary>Whether the straight line between route vertices <paramref name="first"/> and <paramref name="last"/> (indoors, on one floor), swept at the body's radius, keeps to the route's cells between them.</summary>
    private bool ShortcutClear(int first, int last)
    {
        var waypoints = _path!.Waypoints;
        if (last >= waypoints.Count || !waypoints[first].Indoor || !waypoints[last].Indoor || (waypoints[first].At.CellId >> 16) != (waypoints[last].At.CellId >> 16))
        {
            return false;
        }

        for (var k = first; k <= last; k++)
        {
            if (MathF.Abs(waypoints[k].At.Origin.Z - waypoints[first].At.Origin.Z) > 0.3f)
            {
                return false; // a stair or a ramp between: the straight line's height is not the floor's
            }
        }

        return SweepInRouteCells(waypoints[first].At.Origin, waypoints[last].At.Origin, first, last, waypoints[first].At.CellId);
    }

    /// <summary>
    /// The steering's line from two route vertices behind the body (a vertex the runner counts passed may still be ahead of it) to past the horizon's reach (<see cref="_wA"/>
    /// and its fellows), once a decision, over the route's simplified vertices (<see cref="SimplifyRoute"/>).
    /// </summary>
    private void BuildWindow(NavRoute route, int next, float top)
    {
        if (!ReferenceEquals(route, _sRoute))
        {
            SimplifyRoute(route);
        }

        var vertices = route.Vertices;
        var behind = Math.Max(0, next - 2);
        while (behind > 0 && !_sKeep[behind])
        {
            behind--;
        }

        // Outdoors from the next vertex (ahead of the body: a land leg may be 50 m long, and a window that ends at its end leaves the run nothing to turn onto); indoors from
        // the one before it, as measured.
        var reach = route.ArcLengthAt(_pOutdoors ? next : Math.Max(0, next - 1)) + (float)(PredictHorizonSeconds * top) + 6f;
        _wBehind = behind;
        _wRawFrom = behind;
        _wLast = behind;
        _wN = 0;
        var arc = route.ArcLengthAt(behind);
        var from = behind;
        for (var k = behind + 1; k < vertices.Count; k++)
        {
            if (!_sKeep[k])
            {
                continue;
            }

            _wLast = k;
            var a = vertices[from].Global;
            var b = vertices[k].Global;
            var length = Vector2.Distance(a, b);
            if (length >= 1e-3f)
            {
                if (_wN == _wA.Length)
                {
                    Array.Resize(ref _wA, _wN * 2);
                    Array.Resize(ref _wD, _wN * 2);
                    Array.Resize(ref _wL, _wN * 2);
                    Array.Resize(ref _wS, _wN * 2);
                    Array.Resize(ref _wZ0, _wN * 2);
                    Array.Resize(ref _wZ1, _wN * 2);
                    Array.Resize(ref _wB, _wN * 2);
                }

                _wA[_wN] = a;
                _wD[_wN] = (b - a) / length;
                _wL[_wN] = length;
                _wS[_wN] = arc;
                _wZ0[_wN] = vertices[from].Z;
                _wZ1[_wN] = vertices[k].Z;
                _wB[_wN] = Math.Atan2(b.X - a.X, b.Y - a.Y);
                _wN++;
                arc += length;
                from = k;
            }

            if (route.ArcLengthAt(k) > reach)
            {
                break;
            }
        }

        _wRawTo = _wLast;
        if (_wN == 0)
        {
            // A route of one point (or of vertices on one spot): a segment of no length along the body's way, so the projections have a line.
            var a = vertices[behind].Global;
            _wA[0] = a;
            _wD[0] = new Vector2(0f, 1f);
            _wL[0] = 1e-3f;
            _wS[0] = route.ArcLengthAt(behind);
            _wZ0[0] = _wZ1[0] = vertices[behind].Z;
            _wB[0] = 0.0;
            _wN = 1;
        }
    }

    /// <summary>The floor's height under <paramref name="point"/> from the route's own vertices in the window (a staircase's samples), not the simplified legs.</summary>
    private float RawZ(IReadOnlyList<NavPoint> vertices, Vector2 point)
    {
        var best = float.MaxValue;
        var z = vertices[_wRawFrom].Z;
        for (var k = _wRawFrom; k < _wRawTo; k++)
        {
            var a = vertices[k];
            var b = vertices[k + 1];
            var ab = b.Global - a.Global;
            var length2 = ab.LengthSquared();
            var t = length2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(point - a.Global, ab) / length2, 0f, 1f);
            var d = Vector2.DistanceSquared(point, a.Global + (ab * t));
            if (d < best)
            {
                best = d;
                z = a.Z + ((b.Z - a.Z) * t);
            }
        }

        return z;
    }

    /// <summary>The steering line's point at arc length <paramref name="arc"/> (clamped to the window's ends).</summary>
    private Vector2 PointAt(float arc)
    {
        for (var i = 0; i < _wN; i++)
        {
            if (arc <= _wS[i] + _wL[i] || i == _wN - 1)
            {
                return _wA[i] + (_wD[i] * Math.Clamp(arc - _wS[i], 0f, _wL[i]));
            }
        }

        return _wA[0];
    }

    /// <summary>A point's nearest place on the window's segments: its distance (and signed: positive left of the line), arc length, the segment's bearing in radians, the floor's height there.</summary>
    private (float Distance, float Signed, float Arc, double Bearing, int Segment, float Along, float Z) Project(Vector2 point) => Project(point, 0, _wN);

    /// <summary>
    /// A point's nearest place on the window's segments from <paramref name="from"/>, <paramref name="count"/> of them (a predicted track follows the route forward a leg at a
    /// time: on a route that steps round a grid the nearest leg of all jumps between the legs either side of a step).
    /// </summary>
    private (float Distance, float Signed, float Arc, double Bearing, int Segment, float Along, float Z) Project(Vector2 point, int from, int count)
    {
        var best = float.MaxValue;
        var bestIndex = from;
        var bestT = 0f;
        for (var i = from; i < Math.Min(_wN, from + count); i++)
        {
            var t = Math.Clamp(Vector2.Dot(point - _wA[i], _wD[i]), 0f, _wL[i]);
            var d = Vector2.DistanceSquared(point, _wA[i] + (_wD[i] * t));
            if (d < best - 1e-6f)
            {
                best = d;
                bestIndex = i;
                bestT = t;
            }
        }

        var dir = _wD[bestIndex];
        var rel = point - _wA[bestIndex];
        var signed = (dir.X * rel.Y) - (dir.Y * rel.X);
        var z = _wZ0[bestIndex] + ((_wZ1[bestIndex] - _wZ0[bestIndex]) * (bestT / _wL[bestIndex]));
        return (MathF.Sqrt(best), signed, _wS[bestIndex] + bestT, _wB[bestIndex], bestIndex, bestT, z);
    }

    /// <summary>
    /// The body's own place on the window's legs: the nearest, a storey's height counting as distance (<see cref="ArrivalHeightMetres"/>: a switchback ramp's legs lie over one
    /// another, Tusker Cave's 0x5B46), so the predicted tracks start on the leg the body is on.
    /// </summary>
    private (float Distance, float Signed, float Arc, double Bearing, int Segment, float Along, float Z) ProjectBody(Vector2 point, float z)
    {
        var best = float.MaxValue;
        var bestIndex = 0;
        for (var i = 0; i < _wN; i++)
        {
            var t = Math.Clamp(Vector2.Dot(point - _wA[i], _wD[i]), 0f, _wL[i]);
            var dz = z - (_wZ0[i] + ((_wZ1[i] - _wZ0[i]) * (t / _wL[i])));
            var d = Vector2.DistanceSquared(point, _wA[i] + (_wD[i] * t)) + (MathF.Abs(dz) > ArrivalHeightMetres ? dz * dz * 4f : 0f);
            if (d < best - 1e-6f)
            {
                best = d;
                bestIndex = i;
            }
        }

        return Project(point, bestIndex, 1);
    }

    /// <summary>The body's core, in metres: a track whose sweep at this radius leaves the route's cells or meets a static is refused; one that only meets them at the body's
    /// radius brushes them (<see cref="PredictGrazeWeight"/>). Not retail: Lifestone's own.</summary>
    public const float PredictCoreMetres = 0.3f;

    /// <summary>How far ahead a predicted block refuses a plan, in seconds at the run's top speed (along the track: a turn in place first does not put the run after it far
    /// ahead); one past it only costs (<see cref="PredictGrazeWeight"/>), since the plans that follow see it again with the body nearer. Not retail: Lifestone's own.</summary>
    public const double PredictFirmSeconds = 0.6;

    /// <summary>The least of <see cref="PredictFirmSeconds"/>' reach, in metres. Not retail: Lifestone's own.</summary>
    public const float PredictFirmMinMetres = 2.5f;

    /// <summary>The cost of a track that meets a wall or a static with the body's core further on than the firm reach (<see cref="PredictFirmSeconds"/>): dear enough that
    /// a run slows for a turn it cannot make at speed while there is still room to (a 3 m deep room entered and left by doors at a right angle). Not retail: Lifestone's own.</summary>
    public const float PredictFarBlockWeight = 6f;

    private static float GrazeCost(int graze) => graze switch { 1 => PredictGrazeWeight, 3 => PredictFarBlockWeight, _ => 0f };

    /// <summary>The cost of a track that brushes a wall or a static at the body's radius (the physics slides the body along it). Not retail: Lifestone's own.</summary>
    public const float PredictGrazeWeight = 1f;

    /// <summary>
    /// How the recorded track (<see cref="_trackPoints"/>) meets the route's cells and their statics: 0 clear, 1 a brush (clear at <see cref="PredictCoreMetres"/>, not at the
    /// body's radius), 2 blocked. The cells are swept from point to point over all of it; the walls and statics through the physics
    /// (<see cref="Lifestone.Data.Physics.World.PhysicsWorld.SweepViewer"/>, as the plan's room sweep).
    /// </summary>
    private int TrackGraze(in CellFrame position, int count, double update)
    {
        if (LandDefs.IsOutdoorCell(position.CellId) && _paths.Map is { } map)
        {
            return TrackGrazeOutdoors(map, position, count);
        }

        var vertices = _route!.Vertices;
        var firm = MathF.Max(PredictFirmMinMetres, PredictTop() * (float)PredictFirmSeconds);
        var travelled = 0f;
        var last = NavPoint.BlockOrigin(position.CellId & 0xFFFF0000u) + new Vector2(position.Origin.X, position.Origin.Y);
        for (var i = 0; i < count; i++)
        {
            _trackZ[i] = RawZ(vertices, _trackPoints[i]);
            travelled += Vector2.Distance(last, _trackPoints[i]);
            last = _trackPoints[i];
            _trackFirm[i] = travelled <= firm;
        }

        var origin = NavPoint.BlockOrigin(position.CellId & 0xFFFF0000u);
        var previous = position.Origin;
        var to = Math.Min(_path!.Waypoints.Count - 1, _wLast + 1);
        var graze = 0;
        var body = origin + new Vector2(position.Origin.X, position.Origin.Y);
        for (var i = 0; i < count; i++)
        {
            var at = new Vector3(_trackPoints[i].X - origin.X, _trackPoints[i].Y - origin.Y, _trackZ[i]);
            if (Vector2.DistanceSquared(_trackPoints[i], body) <= PredictStaticNearMetres * PredictStaticNearMetres)
            {
                previous = at;
                continue; // the body's own spot (pressed to a wall or a doorway's side, or strayed into a room off the route): the physics took it there; the track may leave it
            }

            if (graze == 0 && !SweepCells(previous, at, _wBehind, to, position.CellId, NavCreatures.PlayerRadiusMetres))
            {
                if (_trackFirm[i])
                {
                    _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"cells-body@{i}({at.X:F2},{at.Y:F2})");
                    return 2; // the body itself would touch a wall soon: refused (a run must not clip a jamb)
                }

                graze = 1;
            }

            if (graze == 1 && !SweepCells(previous, at, _wBehind, to, position.CellId, PredictCoreMetres))
            {
                _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"cells@{i}({at.X:F2},{at.Y:F2})");
                if (_trackFirm[i])
                {
                    return 2;
                }

                graze = 3; // far enough ahead that the next plans see it again: a cost, not a refusal
                break;
            }

            previous = at;
        }

        // The statics the plan's room walks go round (low ones too, which the physics' sphere at chest height passes over): a track point inside one is refused, unless the
        // body already stands in one (it may leave it).
        if (_rooms is { } rooms && rooms.Cells.GetCell(position.CellId) is { } bodyCell && !rooms.Planner.InStatic(bodyCell, position.Origin))
        {
            var holder = bodyCell.Id;
            for (var i = 0; i < count; i++)
            {
                var at = new Vector3(_trackPoints[i].X - origin.X, _trackPoints[i].Y - origin.Y, _trackZ[i]);
                if (!InRouteCell(at with { Z = at.Z + CurveCellLiftMetres }, ref holder, _wBehind, to, position.CellId) || rooms.Cells.GetCell(holder) is not { } cell)
                {
                    continue; // the cell sweep judges a point out of the route's rooms
                }

                if (Vector2.DistanceSquared(_trackPoints[i], origin + new Vector2(position.Origin.X, position.Origin.Y)) > PredictStaticNearMetres * PredictStaticNearMetres
                    && rooms.Planner.InStatic(cell, at) && !RouteInStatic(rooms, _trackPoints[i], origin, _trackZ[i], cell))
                {
                    _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"static@{i}({at.X:F2},{at.Y:F2})");
                    if (_trackFirm[i])
                    {
                        return 2; // a static is no wall to slide along
                    }

                    graze = 3; // further on: dear (PredictFarBlockWeight), and the next plans see it nearer
                    break;
                }
            }
        }

        if (_confirmer is null)
        {
            return graze;
        }

        var world = _confirmer.World;
        var lift = new Vector3(0f, 0f, IndoorLineLiftMetres);
        var from = new CellFrame(position.CellId, new AcFrame(position.Origin + lift, Quaternion.Identity));
        var sweepCount = count;
        var stride = Math.Max(1, sweepCount / 6);
        for (var i = stride - 1; i < sweepCount; i += stride)
        {
            var local = new Vector3(_trackPoints[i].X - origin.X, _trackPoints[i].Y - origin.Y, _trackZ[i]) + lift;
            var target = new CellFrame(from.CellId, new AcFrame(local, Quaternion.Identity));
            var result = world.SweepViewer(from, target, NavCreatures.PlayerRadiusMetres);
            if (result.Blocked && _trackFirm[i])
            {
                _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"phys-body@{i}({local.X:F2},{local.Y:F2})");
                return 2; // the body itself would meet a wall or a static soon: refused
            }

            if (result.Blocked)
            {
                result = world.SweepViewer(from, target, PredictCoreMetres);
                if (result.Blocked)
                {
                    _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"phys@{i}({local.X:F2},{local.Y:F2})->({result.Position.Origin.X:F2},{result.Position.Origin.Y:F2})");
                    if (_trackFirm[i])
                    {
                        return 2;
                    }

                    return 3;
                }

                graze = Math.Max(graze, 1);
            }

            from = result.Position;
        }

        return graze;
    }

    /// <summary>
    /// <see cref="TrackGraze"/> outdoors (<see cref="NavRunnerOptions.PredictiveOutdoors"/>), to the straight run's standard (<see cref="CanRunStraightTo"/>): within
    /// <see cref="PredictBandMetres"/> of the route's line the route's own check stands (the planner's lattice and the confirmer's placements passed it); further off, every
    /// <see cref="NavMap.SightSampleMetres"/> of the track must stand on the land map at the straightening's clearance (<see cref="PathPlanner.StraightenMinClearance"/>), off
    /// water, slopes and steep edges where the route's own ground there is not, and off the run's scars. A miss within the firm reach refuses the plan, one further on costs
    /// (<see cref="PredictFarBlockWeight"/>). No physics sweep: at chest height over rising ground it meets the ground itself, and the lattice stamps the statics by their physics.
    /// </summary>
    private int TrackGrazeOutdoors(NavMap map, in CellFrame position, int count)
    {
        var firm = MathF.Max(PredictFirmMinMetres, PredictTop() * (float)PredictFirmSeconds);
        var body = NavMap.FromCellFrame(position).Global;
        var scars = CopyScars();
        const NavFlags Avoided = NavFlags.Water | NavFlags.Slope | NavFlags.SteepEdge;
        var travelled = 0f;
        var previous = body;
        for (var i = 0; i < count; i++)
        {
            var at = _trackPoints[i];
            var length = Vector2.Distance(previous, at);
            var samples = Math.Max(1, (int)MathF.Ceiling(length / NavMap.SightSampleMetres));
            for (var k = 1; k <= samples; k++)
            {
                var q = Vector2.Lerp(previous, at, (float)k / samples);
                if (Vector2.DistanceSquared(q, body) <= PredictStaticNearMetres * PredictStaticNearMetres)
                {
                    continue; // the body's own spot: the physics took it there
                }

                var p = Project(q);
                if (p.Distance <= PredictBandMetres)
                {
                    continue; // on the route's own line: its plan and its confirmation stand
                }

                var on = _wA[p.Segment] + (_wD[p.Segment] * p.Along);
                var avoid = Avoided;
                if (map.TryGetNode(NavPoint.FromGlobal(on, 0f), out var routeNode))
                {
                    avoid &= ~routeNode.Flags; // what the route crosses (a wade, a costed slope), the track may
                }

                if (!map.Sight(q, q, PathPlanner.StraightenMinClearance, avoid, _scarBuffer, _scarIgnored, scars))
                {
                    _clearFail = PredictTrace is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"land@{i}({q.X:F2},{q.Y:F2})");
                    return travelled + (length * k / samples) <= firm ? 2 : 3; // refused soon; dear further on, and the next plans see it nearer
                }
            }

            travelled += length;
            previous = at;
        }

        return 0;
    }

    /// <summary>How near the body the statics' test lets a track point be in one, in metres (the body stands at a static's margin: the physics slides it). Not retail: Lifestone's own.</summary>
    public const float PredictStaticNearMetres = 0.6f;


    /// <summary>Whether the route's own line, where it passes nearest <paramref name="point"/>, lies in a static too (the plan went there: a goal by a statue, a doorway by a pillar).</summary>
    private bool RouteInStatic(RoomGraph rooms, Vector2 point, Vector2 origin, float z, EnvCellData near)
    {
        var p = Project(point);
        var on = _wA[p.Segment] + (_wD[p.Segment] * p.Along) - origin;
        var at = new Vector3(on.X, on.Y, z);
        return rooms.Cells.FindCell(at with { Z = at.Z + CurveCellLiftMetres }, near.Id) is { } cell && rooms.Planner.InStatic(cell, at);
    }

    /// <summary>Whether the line from <paramref name="a"/> to <paramref name="b"/> (landblock-local), swept at <paramref name="radius"/>, stays in the body's cell or the route's cells from <paramref name="behind"/> to <paramref name="to"/> (<see cref="SweepInRouteCells"/> at a radius).</summary>
    private bool SweepCells(Vector3 a, Vector3 b, int behind, int to, uint bodyCell, float radius)
    {
        var flat = new Vector2(b.X - a.X, b.Y - a.Y);
        var length = flat.Length();
        if (length < 1e-3f)
        {
            return true;
        }

        var side = new Vector2(-flat.Y, flat.X) * (radius / length);
        var across = new Vector3(side.X, side.Y, 0f);
        var samples = Math.Max(1, (int)MathF.Ceiling(length / IndoorLineSampleMetres));
        var hint = bodyCell;
        for (var i = 0; i <= samples; i++)
        {
            var centre = Vector3.Lerp(a, b, (float)i / samples) + new Vector3(0f, 0f, IndoorLineLiftMetres);
            if (!InRouteCell(centre, ref hint, behind, to, bodyCell)
                || !InRouteCell(centre + across, ref hint, behind, to, bodyCell)
                || !InRouteCell(centre - across, ref hint, behind, to, bodyCell))
            {
                return false;
            }
        }

        return true;
    }
}
