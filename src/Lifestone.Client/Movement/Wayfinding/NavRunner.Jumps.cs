using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// M6 14 pits (<c>TODO(m6-abode-pits)</c>): the runner's jump over a pit a walking body cannot cross (<see cref="PitJump"/>; the plan's take-off waypoint carries it, <see cref="NavWaypoint.Jump"/>).
/// </summary>
/// <remarks>
/// <para><b>What a player does.</b> Stand at the run-up point, face the far side, hold the run forward and the jump key, and let the key go at the brink with a full bar: the keys and the jump bar a person
/// uses (<see cref="MoveIntents.ChargeJump"/>, <see cref="MoveIntents.Jump"/>; <c>ClientCombatSystem::CommenceJump</c> 11.6096 0x0056BCD0 and <c>DoJump</c> 0x0056BE50). The bar fills in a second
/// (<see cref="JumpCharge.FillSeconds"/>); a body that reaches the brink first is held there by retail's edge slide (<c>CTransition::edge_slide</c>, 0x0050BEA0) with the run still its motion, so the jump
/// leaves with the run's speed (<c>get_leave_ground_velocity</c>, 0x00528CD0, reads the state velocity, not the body's). Nothing else moves the body: no teleport, no server help.</para>
/// <para><b>Check, act, check</b> (D141's rule). Before: the body stands in the near room by the run-up and its own full-bar flight (<see cref="INavBody.JumpReachMetres"/>: its Run and Jump skills with the
/// buffs) is at least the jump's need, else the run fails <c>jump-reach</c> without jumping. After: the body stands again past the far brink on the far floor (within the step-up of the landing point), else the run fails by name:
/// <c>jump-short</c> (on the brink, or back), <c>jump-fell</c> (into the pit: its cell and height, and the way out), <c>jump-no-takeoff</c> or <c>jump-no-landing</c>. Before the release the jump bar must be full
/// (<see cref="INavBody.JumpBarLevel"/>), else <c>jump-bar</c> without jumping: a release on an empty bar is a 0.35 m hop that falls in. Each jump logs its run-up, its take-off and its landing.</para>
/// <para>A landed jump replans from where the body stands (<c>why=jumped</c>): the rest of the walk is the far room's. Not retail: the thresholds are Lifestone's own.</para>
/// </remarks>
public sealed partial class NavRunner
{
    /// <summary>The body starts a jump within this of its run-up point (or on the line from it to the brink), in metres. Not retail: Lifestone's own.</summary>
    public const float JumpTriggerMetres = 1.5f;

    /// <summary>The heading error to the far side within which the body starts its run, in degrees: the pit is 10 m wide, so 3 degrees is 0.6 m sideways over the jump. Not retail: Lifestone's own.</summary>
    public const double JumpAlignDegrees = 3.0;

    /// <summary>The longest the turn to the far side may take, in seconds. Not retail: Lifestone's own.</summary>
    public const double JumpAlignSeconds = 6.0;

    /// <summary>The body lets the jump key go within this of the brink on the jump's line (or held at the brink by the edge slide), in metres. Not retail: Lifestone's own.</summary>
    public const float JumpReleaseMetres = 0.35f;

    /// <summary>Frames the body stands still at the brink (the edge slide holding it) before the release counts it there. Not retail: Lifestone's own.</summary>
    public const int JumpStallFrames = 3;

    /// <summary>The jump bar's level the release waits for (<see cref="INavBody.JumpBarLevel"/>): full, less a rounding. Not retail: Lifestone's own.</summary>
    public const double JumpFullBar = 0.999;

    /// <summary>How long after the jump key went down a bar that is not full is a refused charge, in seconds (twice <see cref="JumpCharge.FillSeconds"/>). Not retail: Lifestone's own.</summary>
    public const double JumpBarSeconds = 2.0 * JumpCharge.FillSeconds;

    /// <summary>The longest the run from the run-up to the release may take, in seconds. Not retail: Lifestone's own.</summary>
    public const double JumpRunSeconds = 5.0;

    /// <summary>The longest the body may take to leave the ground after the release, in seconds (<c>JumpLeg.TakeOffSeconds</c>'s rule, shorter: the run is already under way). Not retail: Lifestone's own.</summary>
    public const double JumpTakeOffSeconds = 1.0;

    /// <summary>The longest a jump may stay in the air, in seconds: a 10 m pit's flight is 1.5 s, a fall to its floor 2.6 s. Not retail: Lifestone's own.</summary>
    public const double JumpAirSeconds = 4.0;

    /// <summary>A landing this far under the take-off's height is a fall into the pit, in metres. Not retail: Lifestone's own.</summary>
    public const float JumpFallMetres = 3.0f;

    /// <summary>
    /// M6 14 holding plates: how far a standing body may move from where it charged the jump bar before the release, in metres: the standing long jump holds her still with run forward down, so more is a run that would carry her onto the
    /// plates. Not retail: Lifestone's own.
    /// </summary>
    public const float StandStillMetres = 0.1f;

    /// <summary>M6 14 holding plates: a standing jump's body stands within this of its take-off before it turns and charges, in metres (the check before the charge, <c>TakeOffFault</c>, judges the place she stands on). Not retail: Lifestone's own.</summary>
    public const float StandToleranceMetres = 0.6f;

    /// <summary>M6 14 holding plates: the walk to the take-off lets go within this of it (the walk's coast carries her on), in metres. Not retail: Lifestone's own.</summary>
    public const float SettleCoastMetres = 0.5f;

    /// <summary>M6 14 holding plates: the heading error to the take-off within which the body walks to it, in degrees. Not retail: Lifestone's own.</summary>
    public const double SettleAlignDegrees = 10.0;

    /// <summary>M6 14 holding plates: the longest the settle at the take-off may take, in seconds. Not retail: Lifestone's own.</summary>
    public const double JumpSettleSeconds = 10.0;

    /// <summary>M6 14 holding plates: how long after a landing the runner still reads the jump's witness (<see cref="JumpWitness"/>), in seconds: the server's sweep to her landing report and the chat line's way back. Not retail: Lifestone's own.</summary>
    public const double PlateWatchSeconds = 2.0;

    /// <summary>The failure name of a jump over plates after which a plate's spell reached her (M6 14 holding plates).</summary>
    public const string PlateFiredFailure = "jump-plate-fired";

    private readonly List<PlannedJump> _plannedJumps = [];
    private JumpRun? _jumpRun;
    private (DirectedJump Jump, double Until)? _plateWatch;

    private enum JumpPhase
    {
        Align,
        Run,
        Air,

        /// <summary>M6 14 holding plates: a standing jump's bar filling while she stands still at the take-off.</summary>
        Charge,

        /// <summary>M6 14 holding plates: run forward is down on a standing long jump; the release follows at the jump's bar if she stands still.</summary>
        Press,

        /// <summary>M6 14 holding plates: a standing jump's walk to its take-off (short steps at a walk until she stands within <see cref="StandToleranceMetres"/>).</summary>
        Settle,
    }

    /// <summary>Jumps released in this run (each logged). Report-only.</summary>
    public int JumpsTaken { get; private set; }

    /// <summary>Jumps that landed on the far floor in this run. Report-only.</summary>
    public int JumpsLanded { get; private set; }

    /// <summary>The last jump's line (its take-off, landing and outcome), or null before the first.</summary>
    public string? LastJump { get; private set; }

    /// <summary>The body's full-bar running jump now (<see cref="INavBody.JumpReachMetres"/>), for a caller that plans on the same graph before the run does (the fight walk's leg choice).</summary>
    public float? BodyJumpReachMetres => _body.JumpReachMetres;

    /// <summary>
    /// M6 14 airborne (live 2026-11-13, the Tusker Abode's pit: self-defence paused the run in the air and the landing check never ran): true from the jump's take-off (the release, or the body leaving the ground on its run
    /// to the brink) until its landing check has run (<see cref="Land"/>: landed on the far floor, or failed by name). The body is then in the air on the jump's line and a player can do nothing useful: ACE refuses a cast
    /// while the body is off walkable ground (0x04EB, <c>Player.cs</c>:49-62 and <c>Player_Magic.cs</c>:105-109 @95ef426d), and retail's movement keys change no motion without contact but a turn
    /// (<c>CMotionInterp::contact_allows_move</c>, 11.6096 0x00528E50). A caller holds every rule that would
    /// pause or drop the run while this is true (the fight's, <c>FightScenario.Airborne.cs</c>); a <see cref="Pause"/> that comes anyway is logged, named.
    /// </summary>
    public bool JumpInFlight => _jumpRun is { Phase: JumpPhase.Air };

    /// <summary>
    /// M6 14 pit-hold (live 2026-11-13 14:53:48 UTC, the Tusker Abode, <c>cache/m6/14/live/abode5</c>): true from the run for the brink with the jump key down (the charge) until the landing check has run: <see cref="JumpInFlight"/> and
    /// the run before it. The fight's sight rule dropped the walk 0.8 s into that run, before the bar was full: the body ran on to the brink, where the edge slide held it with its centre past the doorway, in the floorless pit cell
    /// 0x5C4502A9, and no walk could be planned from there (the pit is in no storey). A run once charged ends only by its release or by a named failure (<c>jump-bar</c>, <c>jump-run-up</c>), each bounded
    /// (<see cref="JumpBarSeconds"/>, <see cref="JumpRunSeconds"/>), so a caller that holds its drop rules while this is true holds them at most a few seconds. The turn at the run-up (<c>Align</c>) is not committed: a drop there
    /// leaves the body on the near floor.
    /// </summary>
    public bool JumpCommitted => _jumpRun is { Phase: JumpPhase.Run or JumpPhase.Air or JumpPhase.Charge or JumpPhase.Press };

    /// <summary>The jumps of the current plan, in route order (M6 14 pits).</summary>
    private void NoteJumps(NavPath path)
    {
        _plannedJumps.Clear();
        for (var i = 0; i < path.Waypoints.Count - 1; i++)
        {
            if (path.Waypoints[i].Jump is { } jump)
            {
                _plannedJumps.Add(new PlannedJump(i, jump));
            }
        }

        if (_plannedJumps.Count > 0)
        {
            _log.Info(
                LogCategory,
                "the plan jumps a pit",
                new LogField("name", Options.Name),
                new LogField("jumps", string.Join(" ", _plannedJumps.Select(j => j.Jump.Name))),
                new LogField("need_m", _plannedJumps[0].Jump.NeedMetres.ToString("0.00", CultureInfo.InvariantCulture)),
                new LogField("reach_m", _body.JumpReachMetres is { } reach ? reach.ToString("0.00", CultureInfo.InvariantCulture) : "unknown"));
        }
    }

    /// <summary>True while a jump drives the body (this frame is the jump's); starts one when the body reaches the run-up of the plan's next jump.</summary>
    private bool StepJump(double now, in CellFrame position, NavPoint here)
    {
        if (WatchPlate(now))
        {
            return true; // a plate's spell reached her after a jump over plates: the run ended by name
        }

        if (_jumpRun is { } run)
        {
            StepJumpRun(run, now, position, here);
            return true;
        }

        if (_route is null || _plannedJumps.Count == 0 || _confirming)
        {
            return false;
        }

        var jump = _plannedJumps[0].Jump;
        if ((!jump.Crossing.Standing && position.CellId != jump.From.Cell) || !AtRunUp(jump, here)) // a standing take-off by a doorway is reached from either room (M6 14 holding plates)
        {
            return false;
        }

        // Check before: the body's own flight against the jump's need (a standing jump over plates: its flight at the jump's bar, M6 14 holding plates).
        var atBar = _body.JumpReachMetres is { } full && jump.Crossing.Bar < 1f && _body.RunSpeedMetresPerSecond is { } speed
            ? PlateJumps.FlightAt(full, PlateJumps.ApexMetres(full, speed), jump.Crossing.Bar)
            : _body.JumpReachMetres;
        if (atBar is not { } reach || reach < jump.NeedMetres)
        {
            var why = string.Create(
                CultureInfo.InvariantCulture,
                $"jump-reach: the jump {jump.Name} needs a flight of {jump.NeedMetres:0.00} m and the body's full-bar running jump flies {(_body.JumpReachMetres is { } r ? r.ToString("0.00", CultureInfo.InvariantCulture) + " m" : "an unknown distance (no character data)")}: not jumped");
            LastJump = why;
            End(NavRunState.Failed, why, now);
            return true;
        }

        ReleaseAll();
        _jumpRun = new JumpRun(jump, reach) { Since = now, Started = now, Last = here.Global, Phase = jump.Crossing.Standing ? JumpPhase.Settle : JumpPhase.Align };
        _log.Info(
            LogCategory,
            jump.Crossing.Standing ? "jump: at the take-off, stopping and turning to the far side (a standing long jump over plates)" : "jump: at the run-up, turning to the far side",
            new LogField("name", Options.Name),
            new LogField("jump", jump.Name),
            new LogField("at", position.ToString()),
            new LogField("reach_m", reach.ToString("0.00", CultureInfo.InvariantCulture)),
            new LogField("need_m", jump.NeedMetres.ToString("0.00", CultureInfo.InvariantCulture)));
        _lastProgressTime = now;
        return true;
    }

    /// <summary>True when the body stands within <see cref="JumpTriggerMetres"/> of the jump's run-up, or on the jump's line between the run-up and the brink.</summary>
    private static bool AtRunUp(DirectedJump jump, NavPoint here)
    {
        var block = NavPoint.BlockOrigin(jump.From.Cell & 0xFFFF0000u);
        var runUp = block + new Vector2(jump.From.RunUp.X, jump.From.RunUp.Y);
        var brink = block + new Vector2(jump.From.Brink.X, jump.From.Brink.Y);
        var body = here.Global;
        if (Vector2.Distance(body, runUp) <= JumpTriggerMetres)
        {
            return true;
        }

        var line = brink - runUp;
        var length = line.Length();
        if (length < 1e-4f)
        {
            return false;
        }

        var along = Vector2.Dot(body - runUp, line / length);
        var aside = MathF.Abs((line.X * (body.Y - runUp.Y) - line.Y * (body.X - runUp.X)) / length);
        return along >= 0f && along <= length && aside <= JumpTriggerMetres;
    }

    private void StepJumpRun(JumpRun run, double now, in CellFrame position, NavPoint here)
    {
        _lastProgressTime = now; // the stuck ladder is not the jump's: it has its own bounds
        var jump = run.Jump;
        var block = NavPoint.BlockOrigin(jump.From.Cell & 0xFFFF0000u);
        var dir = jump.Direction;
        var body = here.Global;
        var moved = Vector2.Distance(body, run.Last);
        run.Last = body;
        switch (run.Phase)
        {
            case JumpPhase.Settle:
            {
                // M6 14 holding plates: to the take-off at a walk, short steps, until she stands within StandToleranceMetres of it.
                var stand = block + new Vector2(jump.From.Brink.X, jump.From.Brink.Y);
                var toStand = stand - body;
                var distance = toStand.Length();
                run.Stalled = moved < 0.01f && _body.OnGround ? run.Stalled + 1 : 0;
                if (now - run.Started > JumpSettleSeconds)
                {
                    FailJump(run, now, position, here, "jump-settle", string.Create(CultureInfo.InvariantCulture, $"she did not come within {StandToleranceMetres:0.00} m of the take-off in {JumpSettleSeconds:0} s ({distance:0.00} m from it): not jumped"));
                    break;
                }

                var radians = RetailFrames.GetHeading(position.Frame) * (Math.PI / 180.0);
                var ahead = new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
                if (_forward)
                {
                    if (distance <= SettleCoastMetres || Vector2.Dot(toStand, ahead) <= 0f)
                    {
                        SetForward(false); // let go at the stand (the walk's coast carries her the rest) or once past it
                    }

                    break;
                }

                if (run.Stalled < JumpStallFrames)
                {
                    SetTurn(0);
                    break; // she comes to rest first
                }

                if (distance <= StandToleranceMetres)
                {
                    SetTurn(0);
                    run.Phase = JumpPhase.Align;
                    run.Since = now;
                    run.Stalled = 0;
                    break;
                }

                var off = Normalize(Bearing(body, stand) - RetailFrames.GetHeading(position.Frame));
                if (Math.Abs(off) > SettleAlignDegrees)
                {
                    SetTurn(off > 0 ? 1 : -1);
                    run.Stalled = JumpStallFrames; // turning in place: she stays at rest
                    break;
                }

                SetTurn(0);
                SetRun(false);
                SetForward(true);
                break;
            }

            case JumpPhase.Align:
            {
                SetForward(false);
                SetBack(false);
                SetSide(0);
                var target = block + new Vector2(jump.To.Landing.X, jump.To.Landing.Y);
                var error = Normalize(Bearing(body, target) - RetailFrames.GetHeading(position.Frame));
                var magnitude = Math.Abs(error);
                var side = error > 0 ? 1 : -1;
                var release = Math.Max(TurnReleaseDegrees, _turnRate * PhysicsGlobals.MinQuantum);
                var turn = _turn != 0 ? (side == _turn && magnitude > release ? _turn : 0) : (magnitude > JumpAlignDegrees ? side : 0);
                var turnUp = _turn == 0; // the turn key was up before this frame: the motion is no longer turning (retail's is_standing_still asks no turn)
                SetTurn(turn);
                run.Stalled = moved < 0.01f && _body.OnGround ? run.Stalled + 1 : 0;
                if (jump.Crossing.Standing && turn == 0 && turnUp && magnitude <= JumpAlignDegrees && run.Stalled >= JumpStallFrames)
                {
                    // M6 14 holding plates. Check before: from where she stands, the flight at the jump's bar keeps off every plate and lands past the walls; act: the jump key alone, standing still (the standing long jump).
                    if (TakeOffFault(jump, here, body) is { } fault)
                    {
                        FailJump(run, now, position, here, "jump-take-off", fault + ": not jumped");
                        break;
                    }

                    Send(_move.ChargeJump(ActionSource.Automation));
                    run.ChargedAt = now;
                    run.Stand = body;
                    run.Phase = JumpPhase.Charge;
                    run.Since = now;
                    _log.Info(
                        LogCategory,
                        "jump: standing at the take-off, the jump bar filling",
                        new LogField("name", Options.Name),
                        new LogField("jump", jump.Name),
                        new LogField("at", position.ToString()),
                        new LogField("heading_error_deg", error.ToString("0.0", CultureInfo.InvariantCulture)),
                        new LogField("to_far_edge_m", ToFarEdge(jump, block, body).ToString("0.00", CultureInfo.InvariantCulture)));
                }
                else if (!jump.Crossing.Standing && turn == 0 && magnitude <= JumpAlignDegrees)
                {
                    // Act: the run and the jump bar at once (a person holds both keys).
                    SetRun(true);
                    SetForward(true);
                    Send(_move.ChargeJump(ActionSource.Automation));
                    run.ChargedAt = now;
                    run.Phase = JumpPhase.Run;
                    run.Since = now;
                    _log.Info(
                        LogCategory,
                        "jump: running for the brink with the jump bar filling",
                        new LogField("name", Options.Name),
                        new LogField("jump", jump.Name),
                        new LogField("heading_error_deg", error.ToString("0.0", CultureInfo.InvariantCulture)),
                        new LogField("to_brink_m", ToBrink(jump, block, body).ToString("0.00", CultureInfo.InvariantCulture)));
                }
                else if (now - run.Since > JumpAlignSeconds)
                {
                    FailJump(run, now, position, here, "jump-align", string.Create(CultureInfo.InvariantCulture, $"the turn to the far side took over {JumpAlignSeconds:0} s (heading error {error:0.0} degrees{(jump.Crossing.Standing ? $", still for {run.Stalled} frames" : string.Empty)})"));
                }

                break;
            }

            case JumpPhase.Charge:
            {
                // M6 14 holding plates: once the jump key's charge has reached the controller (the bar filling). Check: it is a standing long jump (she stood still), so run forward does not move her; act: run forward down while the bar fills.
                var filling = _body.JumpBarLevel is not { } level || level > 0.0;
                if (Vector2.Distance(body, run.Stand) > StandStillMetres)
                {
                    FailJump(run, now, position, here, "jump-ran", string.Create(CultureInfo.InvariantCulture, $"she moved {Vector2.Distance(body, run.Stand):0.00} m from her stand as the bar began to fill: not jumped"));
                }
                else if (_body.StandingLongJump is not true && !filling)
                {
                    if (now - run.ChargedAt >= JumpBarSeconds)
                    {
                        FailJump(run, now, position, here, "jump-bar", string.Create(CultureInfo.InvariantCulture, $"the jump bar did not start within {JumpBarSeconds:0} s of the jump key (the charge was refused): not jumped"));
                    }
                }
                else if (_body.StandingLongJump is false)
                {
                    FailJump(run, now, position, here, "jump-not-standing", string.Create(CultureInfo.InvariantCulture, $"the bar fills but it is no standing long jump (she was not standing still when the key went down): run forward would carry her onto the plates (on the ground {_body.OnGround}); not jumped"));
                }
                else
                {
                    SetRun(true);
                    SetForward(true);
                    run.Phase = JumpPhase.Press;
                    run.Since = now;
                }

                break;
            }

            case JumpPhase.Press:
            {
                // Check before the release: run forward is down and the standing long jump still holds her at her stand.
                if (Vector2.Distance(body, run.Stand) > StandStillMetres || !_body.OnGround)
                {
                    ReleaseAll();
                    FailJump(run, now, position, here, "jump-ran", string.Create(CultureInfo.InvariantCulture, $"run forward moved her {Vector2.Distance(body, run.Stand):0.00} m from her stand before the release (no standing long jump held her): not jumped"));
                    break;
                }

                // The bar the derivation chose (a full bar, or the highest arc under the ceilings: PitJump.Bar), let go at the first frame that reaches it.
                var bar = _body.JumpBarLevel;
                var target = jump.Crossing.Bar;
                var reached = target >= 1f
                    ? now - run.ChargedAt >= JumpCharge.FillSeconds && (bar is not { } level || level >= JumpFullBar)
                    : bar is { } partial ? partial >= target : now - run.ChargedAt >= target * JumpCharge.FillSeconds;
                if (!reached)
                {
                    if (now - run.ChargedAt >= JumpBarSeconds)
                    {
                        ReleaseAll();
                        FailJump(run, now, position, here, "jump-bar", string.Create(CultureInfo.InvariantCulture, $"the jump bar stood at {(bar is { } b ? b.ToString("0.00", CultureInfo.InvariantCulture) : "unknown")} {JumpBarSeconds:0} s after the jump key went down, short of {target:0.00} (the charge was refused): not jumped"));
                    }

                    break;
                }

                Send(_move.Jump(ActionSource.Automation));
                JumpsTaken++;
                run.TakeOffAt = position;
                run.TakeOffTime = now;
                run.Released = true;
                run.Phase = JumpPhase.Air;
                run.Since = now;
                JumpWitness?.TookOff(jump);
                _plateWatch = null;
                _log.Info(
                    LogCategory,
                    "jump: take-off",
                    new LogField("name", Options.Name),
                    new LogField("jump", jump.Name),
                    new LogField("standing", true),
                    new LogField("at", position.ToString()),
                    new LogField("to_far_edge_m", ToFarEdge(jump, block, body).ToString("0.00", CultureInfo.InvariantCulture)),
                    new LogField("charge_s", (now - run.ChargedAt).ToString("0.00", CultureInfo.InvariantCulture)),
                    new LogField("bar", bar is { } atRelease ? atRelease.ToString("0.00", CultureInfo.InvariantCulture) : "unknown"),
                    new LogField("bar_target", target.ToString("0.00", CultureInfo.InvariantCulture)),
                    new LogField("heading", RetailFrames.GetHeading(position.Frame).ToString("0.0", CultureInfo.InvariantCulture)),
                    new LogField("reach_m", run.Reach.ToString("0.00", CultureInfo.InvariantCulture)),
                    new LogField("need_m", jump.NeedMetres.ToString("0.00", CultureInfo.InvariantCulture)));
                break;
            }

            case JumpPhase.Run:
            {
                run.Stalled = moved < 0.01f ? run.Stalled + 1 : 0;
                var toBrink = ToBrink(jump, block, body);
                var bar = _body.JumpBarLevel;
                var full = now - run.ChargedAt >= JumpCharge.FillSeconds && (bar is not { } level || level >= JumpFullBar);
                if (!full && now - run.ChargedAt >= JumpBarSeconds)
                {
                    // Check before the release: a bar that never filled is a charge the client refused (CMotionInterp::charge_jump); a release now would hop and fall in.
                    FailJump(run, now, position, here, "jump-bar", string.Create(CultureInfo.InvariantCulture, $"the jump bar stood at {(bar is { } b ? b.ToString("0.00", CultureInfo.InvariantCulture) : "unknown")} {JumpBarSeconds:0} s after the jump key went down (the charge was refused): not jumped"));
                    break;
                }

                if (!_body.OnGround)
                {
                    // Off the edge before the release: the fall is the outcome the landing names.
                    run.Phase = JumpPhase.Air;
                    run.Since = now;
                    run.AirAt = now;
                    run.TakeOffAt = position;
                    run.Released = false;
                    break;
                }

                if (full && (toBrink <= JumpReleaseMetres || run.Stalled >= JumpStallFrames))
                {
                    Send(_move.Jump(ActionSource.Automation));
                    JumpsTaken++;
                    run.TakeOffAt = position;
                    run.TakeOffTime = now;
                    run.Released = true;
                    run.Phase = JumpPhase.Air;
                    run.Since = now;
                    _log.Info(
                        LogCategory,
                        "jump: take-off",
                        new LogField("name", Options.Name),
                        new LogField("jump", jump.Name),
                        new LogField("at", position.ToString()),
                        new LogField("to_brink_m", toBrink.ToString("0.00", CultureInfo.InvariantCulture)),
                        new LogField("held_at_brink", run.Stalled >= JumpStallFrames),
                        new LogField("charge_s", (now - run.ChargedAt).ToString("0.00", CultureInfo.InvariantCulture)),
                        new LogField("bar", bar is { } atRelease ? atRelease.ToString("0.00", CultureInfo.InvariantCulture) : "unknown"),
                        new LogField("heading", RetailFrames.GetHeading(position.Frame).ToString("0.0", CultureInfo.InvariantCulture)),
                        new LogField("reach_m", run.Reach.ToString("0.00", CultureInfo.InvariantCulture)),
                        new LogField("need_m", jump.NeedMetres.ToString("0.00", CultureInfo.InvariantCulture)));
                }
                else if (now - run.Since > JumpRunSeconds)
                {
                    FailJump(run, now, position, here, "jump-run-up", string.Create(CultureInfo.InvariantCulture, $"no release within {JumpRunSeconds:0} s of the run's start ({toBrink:0.00} m from the brink, bar {(full ? "full" : "filling")})"));
                }

                break;
            }

            case JumpPhase.Air:
            {
                run.Highest = MathF.Max(run.Highest, here.Z);
                if (double.IsNaN(run.AirAt))
                {
                    if (!_body.OnGround)
                    {
                        run.AirAt = now;
                    }
                    else if (now - run.Since > JumpTakeOffSeconds)
                    {
                        FailJump(run, now, position, here, "jump-no-takeoff", string.Create(CultureInfo.InvariantCulture, $"the body did not leave the ground within {JumpTakeOffSeconds:0.0} s of the release (a jump refused for the load, the stamina or the position)"));
                    }

                    break;
                }

                if (_body.OnGround)
                {
                    Land(run, now, position, here);
                }
                else if (now - run.AirAt > JumpAirSeconds)
                {
                    FailJump(run, now, position, here, "jump-no-landing", string.Create(CultureInfo.InvariantCulture, $"the body did not come down within {JumpAirSeconds:0} s"));
                }

                break;
            }
        }
    }

    /// <summary>Check after: the body stands past the far brink on the far floor, or the jump failed by name.</summary>
    private void Land(JumpRun run, double now, in CellFrame position, NavPoint here)
    {
        ReleaseAll();
        var jump = run.Jump;
        var block = NavPoint.BlockOrigin(jump.From.Cell & 0xFFFF0000u);
        var dir = jump.Direction;
        var body = here.Global;
        var farBrink = block + new Vector2(jump.To.Brink.X, jump.To.Brink.Y);
        var beyond = Vector2.Dot(body - farBrink, dir);
        var rise = here.Z - jump.To.Landing.Z;
        if (beyond >= 0f && MathF.Abs(rise) <= CellRoutePlanner.StepUpMetres && position.CellId != jump.Crossing.PitCell && run.Released)
        {
            if (jump.Crossing.Standing && JumpWitness?.Fired() is { } fired)
            {
                FailJump(run, now, position, here, PlateFiredFailure, PlateFired(jump, fired));
                return;
            }

            if (jump.Crossing.Standing && PlateWallHolding(here) is { } wall)
            {
                // Check after: past the far edge and on the floor, yet inside another plate's wall: the walk replans out of it (a step that leaves a wall is walked), logged
                _log.Warn(LogCategory, "jump: landed inside a plate's wall", new LogField("name", Options.Name), new LogField("jump", jump.Name), new LogField("plate", wall.Plate.Name), new LogField("metres", wall.Metres), new LogField("reach_m", wall.Reach));
            }

            if (jump.Crossing.Standing)
            {
                _plateWatch = (jump, now + PlateWatchSeconds);
            }

            JumpsLanded++;
            LastJump = JumpLine(run, now, position, here, "landed");
            _log.Info(LogCategory, "jump: landed on the far floor", JumpFields(run, now, position, here, "landed"));
            _plannedJumps.Clear();
            _jumpRun = null;
            _route = null;
            Plan(now, position, replan: true, "jumped"); // the rest of the walk is the far room's
            return;
        }

        if (here.Z < jump.From.Brink.Z - JumpFallMetres)
        {
            FailJump(run, now, position, here, "jump-fell", string.Create(
                CultureInfo.InvariantCulture,
                $"the body fell into the pit and stands on its floor in 0x{position.CellId:X8} at z {here.Z:0.00} ({jump.From.Brink.Z - here.Z:0.0} m under the brink); the pit's floor is in no storey of the ground, so no walk leads out: the way out is Lifestone Recall (TODO(m6-abode-pit-fall): the fight ends on this failure by name; that the autopilot then recalls and comes back is not proven)"));
            return;
        }

        FailJump(run, now, position, here, "jump-short", string.Create(
            CultureInfo.InvariantCulture,
            $"the body came down {(beyond < 0f ? $"{-beyond:0.00} m short of" : $"{beyond:0.00} m past")} the far brink at z {here.Z:0.00} in 0x{position.CellId:X8} (the far floor is z {jump.To.Landing.Z:0.00}){(run.Released ? string.Empty : "; it left the ground before the release")}"));
    }

    private void FailJump(JumpRun run, double now, in CellFrame position, NavPoint here, string outcome, string why)
    {
        _jumpRun = null;
        if (run.Jump.Crossing.Standing && run.Released && outcome != PlateFiredFailure && JumpWitness?.Fired() is { } fired)
        {
            why += "; " + PlateFired(run.Jump, fired); // M6 14 holding plates: a failed jump over plates that a plate fired on is refused too
        }
        else if (run.Jump.Crossing.Standing && run.Released && outcome != PlateFiredFailure)
        {
            _plateWatch = (run.Jump, now + PlateWatchSeconds);
        }

        LastJump = JumpLine(run, now, position, here, outcome);
        _log.Warn(LogCategory, "jump: " + outcome, JumpFields(run, now, position, here, outcome));
        End(NavRunState.Failed, string.Create(CultureInfo.InvariantCulture, $"{outcome}: the jump {run.Jump.Name}: {why}"), now);
    }

    private LogField[] JumpFields(JumpRun run, double now, in CellFrame position, NavPoint here, string outcome) =>
    [
        new("name", Options.Name),
        new("jump", run.Jump.Name),
        new("outcome", outcome),
        new("take_off", run.TakeOffAt.CellId == 0 ? "none" : run.TakeOffAt.ToString()),
        new("landing", position.ToString()),
        new("flight_m", Flight(run, here).ToString("0.00", CultureInfo.InvariantCulture)),
        new("air_s", (double.IsNaN(run.AirAt) ? 0 : now - run.AirAt).ToString("0.00", CultureInfo.InvariantCulture)),
        new("apex_z", (run.Highest == float.MinValue ? here.Z : run.Highest).ToString("0.00", CultureInfo.InvariantCulture)),
        new("reach_m", run.Reach.ToString("0.00", CultureInfo.InvariantCulture)),
        new("need_m", run.Jump.NeedMetres.ToString("0.00", CultureInfo.InvariantCulture)),
    ];

    private string JumpLine(JumpRun run, double now, in CellFrame position, NavPoint here, string outcome) => string.Create(
        CultureInfo.InvariantCulture,
        $"{run.Jump.Name} {outcome}: take-off {(run.TakeOffAt.CellId == 0 ? "none" : run.TakeOffAt.ToString())}, landing {position}, flight {Flight(run, here):0.00} m, air {(double.IsNaN(run.AirAt) ? 0 : now - run.AirAt):0.00} s, reach {run.Reach:0.00} m, need {run.Jump.NeedMetres:0.00} m");

    /// <summary>The flight so far along the jump's line from the take-off, in metres (0 before it).</summary>
    private static float Flight(JumpRun run, NavPoint here)
    {
        if (run.TakeOffAt.CellId == 0)
        {
            return 0f;
        }

        var from = NavMap.FromCellFrame(run.TakeOffAt).Global;
        return Vector2.Dot(here.Global - from, run.Jump.Direction);
    }

    /// <summary>The body's distance to the near brink along the jump's line (negative past it).</summary>
    private static float ToBrink(DirectedJump jump, Vector2 block, Vector2 body) =>
        Vector2.Dot(block + new Vector2(jump.From.Brink.X, jump.From.Brink.Y) - body, jump.Direction);

    /// <summary>M6 14 holding plates: the body's distance to the far brink along the jump's line (a standing jump's: the far edge of the plates' walls).</summary>
    private static float ToFarEdge(DirectedJump jump, Vector2 block, Vector2 body) =>
        Vector2.Dot(block + new Vector2(jump.To.Brink.X, jump.To.Brink.Y) - body, jump.Direction);

    /// <summary>
    /// M6 14 holding plates (check before the charge): why the standing jump cannot go from where she stands, or null: she stands inside a plate's touch, a plate within touch of the flight's line lies under the arc's low end, or the
    /// flight at the jump's bar (her live flight and speed: <see cref="PlateJumps.FlightAt"/>) lands short of the far edge of the walls. Null without plates or without her flight and speed (the plan's geometry then stands).
    /// </summary>
    private string? TakeOffFault(DirectedJump jump, NavPoint here, Vector2 body)
    {
        if (_rooms?.Plates is not { } walls || here.LandblockId != _rooms.LandblockId || _body.JumpReachMetres is not { } full || _body.RunSpeedMetresPerSecond is not { } speed)
        {
            return null;
        }

        var block = NavPoint.BlockOrigin(jump.From.Cell & 0xFFFF0000u);
        var landing = block + new Vector2(jump.To.Landing.X, jump.To.Landing.Y);
        var u = landing - body;
        if (u.LengthSquared() < 1e-6f)
        {
            return "she stands on the landing";
        }

        u = Vector2.Normalize(u);
        var apex = PlateJumps.ApexMetres(full, speed);
        var f = PlateJumps.FlightAt(full, apex, jump.Crossing.Bar);
        var h = PlateJumps.ApexAt(apex, jump.Crossing.Bar);
        var local = new Vector2(here.Local.X, here.Local.Y);
        foreach (var p in walls.Plates)
        {
            if (MathF.Abs(p.Point.Z - here.Local.Z) > PlateKeepOff.StoreyMetres)
            {
                continue;
            }

            var rel = new Vector2(p.Point.X, p.Point.Y) - local;
            var along = Vector2.Dot(rel, u);
            var aside = MathF.Abs((u.X * rel.Y) - (u.Y * rel.X));
            var touch = PlateKeepOff.TouchOf(p);
            if (rel.Length() < touch)
            {
                return string.Create(CultureInfo.InvariantCulture, $"she stands {rel.Length():0.00} m from {p.Name}, inside its touch ({touch:0.00} m)");
            }

            if (aside < touch && along > -touch && along < f + touch)
            {
                var half = MathF.Sqrt((touch * touch) - (aside * aside));
                var low = PlateJumps.LowEndMetres(f, h, p.TopMetres);
                if (along < low + half || along > f - low - half)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"{p.Name} lies under the arc's low end ({along:0.00} m along a flight of {f:0.00} m)");
                }
            }
        }

        var past = Vector2.Dot(local + (u * f) - new Vector2(jump.To.Brink.X, jump.To.Brink.Y), jump.Direction);
        return past < 0f ? string.Create(CultureInfo.InvariantCulture, $"from where she stands the flight of {f:0.00} m at bar {jump.Crossing.Bar:0.00} lands {-past:0.00} m short of the far edge of the walls") : null;
    }

    /// <summary>M6 14 holding plates: the plate whose wall holds the body now (the room graph's walls at the run's speed), its planar metres and reach; null outside every wall or without plates.</summary>
    private (KeptPlate Plate, float Metres, float Reach)? PlateWallHolding(NavPoint here)
    {
        if (_rooms?.Plates is not { } walls || here.LandblockId != _rooms.LandblockId || walls.Holding(here.Local) is not { } plate)
        {
            return null;
        }

        return (plate, Vector2.Distance(new Vector2(here.Local.X, here.Local.Y), new Vector2(plate.Point.X, plate.Point.Y)), walls.ReachOf(plate));
    }

    /// <summary>
    /// M6 14 holding plates (check after): within <see cref="PlateWatchSeconds"/> of a jump over plates, a plate's spell the witness reports ends the run <see cref="PlateFiredFailure"/> by name and takes the jump out of the rest of the run's
    /// plans (<see cref="RoomGraph.RefusePlateJump"/>); true when it did.
    /// </summary>
    private bool WatchPlate(double now)
    {
        if (_plateWatch is not { } watch)
        {
            return false;
        }

        if (JumpWitness?.Fired() is { } fired)
        {
            _plateWatch = null;
            var why = PlateFired(watch.Jump, fired);
            LastJump = watch.Jump.Name + " " + PlateFiredFailure + ": " + fired;
            End(NavRunState.Failed, string.Create(CultureInfo.InvariantCulture, $"{PlateFiredFailure}: the jump {watch.Jump.Name}: {why}"), now);
            return true;
        }

        if (now > watch.Until)
        {
            _plateWatch = null;
        }

        return false;
    }

    /// <summary>M6 14 holding plates: refuses the jump on the room graph for the rest of the run and logs it; the failure's words.</summary>
    private string PlateFired(DirectedJump jump, string fired)
    {
        var refused = _rooms?.RefusePlateJump(jump.Crossing.Name) ?? false;
        _log.Warn(
            LogCategory,
            "jump: " + PlateFiredFailure,
            new LogField("name", Options.Name),
            new LogField("jump", jump.Name),
            new LogField("witness", fired),
            new LogField("refused_for_the_run", refused));
        return string.Create(CultureInfo.InvariantCulture, $"a plate's spell reached her ({fired}); the jump is out of the rest of the run's plans");
    }

    private readonly record struct PlannedJump(int TakeOff, DirectedJump Jump);

    private sealed class JumpRun(DirectedJump jump, float reach)
    {
        public DirectedJump Jump { get; } = jump;

        public float Reach { get; } = reach;

        public JumpPhase Phase { get; set; } = JumpPhase.Align;

        public double Since { get; set; }

        public double Started { get; set; }

        public double ChargedAt { get; set; } = double.NaN;

        public int Stalled { get; set; }

        public Vector2 Last { get; set; }

        /// <summary>M6 14 holding plates: where a standing jump's body stood when the bar began to fill.</summary>
        public Vector2 Stand { get; set; }

        public CellFrame TakeOffAt { get; set; }

        public double TakeOffTime { get; set; } = double.NaN;

        public bool Released { get; set; }

        public double AirAt { get; set; } = double.NaN;

        public float Highest { get; set; } = float.MinValue;
    }
}
