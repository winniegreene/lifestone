using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.Motion;
using Lifestone.Data.Physics;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>Where a <see cref="NavRunner"/> stands.</summary>
public enum NavRunState
{
    /// <summary>Not started.</summary>
    Idle,

    /// <summary>Planning, confirming or running the route (the stuck ladder included).</summary>
    Running,

    /// <summary>Paused by its caller (<see cref="NavRunner.Pause"/>): keys released, the route kept.</summary>
    Waiting,

    /// <summary>The server drives the player (ACE's MoveTo, <see cref="INavBody.ServerDriving"/>): keys released, nothing pressed until it ends.</summary>
    Yielding,

    /// <summary>The goal reached (a point within its tolerance; an object within range and in sight, or no sight asked).</summary>
    Arrived,

    /// <summary>An object goal within range whose sight line every try found blocked.</summary>
    ArrivedWithoutSight,

    /// <summary>The run cannot go on: no path, the ladder's last plan, a route the physics refused, or an intent not taken (<see cref="NavRunner.FailureReason"/>).</summary>
    Blocked,

    /// <summary>A person pressed a movement key (R6: the human's key wins).</summary>
    Interrupted,

    /// <summary>The player was moved more than <see cref="NavRunner.TeleportMetres"/> in one step (a teleport or a server correction).</summary>
    Failed,
}

/// <summary>The runner's tuning; the defaults are the design's (P5a 3.2). Not retail: Lifestone's own.</summary>
public sealed record NavRunnerOptions
{
    /// <summary>The defaults.</summary>
    public static NavRunnerOptions Default { get; } = new();

    /// <summary>The confirmation budget per <see cref="NavRunner.Step"/> in milliseconds: unbounded headless, about 2 ms in the Godot host.</summary>
    public double ConfirmBudgetMilliseconds { get; init; } = double.PositiveInfinity;

    /// <summary>True to stop at a point goal (within its stop tolerance, keys released); false to pass it (within its pass tolerance, keys kept).</summary>
    public bool Stop { get; init; } = true;

    /// <summary>The name the runner's log lines and failure reasons carry.</summary>
    public string Name { get; init; } = "run";

    /// <summary>
    /// True to steer a dungeon's indoor route by predicting the body's motion for each key choice and keeping the best (<c>NavRunner.PredictiveSteer</c>, the owner's ruling that it is just math,
    /// 2026-11-15) instead of the pursuit's dead-bands and D134's stand-and-turn corners; the ordinary steering stays the fallback (no plan clear, a short route, a route by statics,
    /// a switchback, a hairpin, the body in the air, no physics to sweep with). Off by default: the hunting grounds' fight walks and the journeys turn it on, where it was measured to
    /// beat the ordinary steering (docs/wayfinding.md "Predictive steering"); the world walk's room legs keep the ordinary steering (<c>--predictive=on</c> to measure).
    /// </summary>
    public bool PredictiveSteering { get; init; }

    /// <summary>
    /// True to steer the outdoor straight run predictively too (<see cref="PredictiveSteering"/>'s controller over the land: its tracks off the route's line checked against the land
    /// map's blocked ground, steep edges, slopes, water and the run's scars at the straight run's clearance). A route into or out of a building, a start from a standstill and the
    /// last of a route to a stop stay the ordinary steering's. Off by default; the hunting grounds' fight walks and the journeys turn it on (the owner, 2026-11-16: fewer keys and
    /// longer straight runs look human, a slightly longer open-country run is fine). Measured: far fewer keys and reversals over open country and the outdoor hunting grounds,
    /// about even in Holtburg's streets (docs/wayfinding.md "Predictive steering").
    /// </summary>
    public bool PredictiveOutdoors { get; init; }

    /// <summary>The clock the run's scars live on (null: the system clock). The world walk gives its simulated time, so a scar's life does not depend on how fast the machine ran (W0).</summary>
    public TimeProvider? ScarClock { get; init; }

    /// <summary>
    /// How long a closed door may take to open after the Use, in seconds, before the leg fails <c>door-closed-timeout</c>. ACE's <c>Door.Open</c> sets Ethereal at once and broadcasts it
    /// (<c>Door.cs:111-130</c> @95ef426d), so the wait is one round trip; a door still animating ignores a Use (<c>IsBusy</c>, <c>Door.cs:90</c>), which the one retry
    /// (<see cref="DoorRetrySeconds"/>) covers. Not retail: Lifestone's own.
    /// </summary>
    public double DoorOpenSeconds { get; init; } = 5.0;

    /// <summary>The seconds after the first Use at which a door still shut is Used once more (a door that was animating ignored the first). Not retail: Lifestone's own.</summary>
    public double DoorRetrySeconds { get; init; } = 2.0;

    /// <summary>
    /// How near a shut door's doorway (straight line, the body's centre to where the route crosses it) a stalled body Uses the door at once instead of climbing the stuck ladder (M5 7 P7l, the owner's rule D141:
    /// check, act, check), in metres: ACE's use radius of the Aluvian house door (wcid 412, <c>weenie_properties_float</c> key 54 = 2; <c>IsWithinUseRadiusOf</c> measures the cylinder distance, the centres less both
    /// radii, <c>WorldObject_Use.cs:47-61</c> @95ef426d, so a centre distance of 2 m is inside it). Not retail: Lifestone's own.
    /// </summary>
    public float DoorUseReachMetres { get; init; } = 2f;

    /// <summary>How far before a closed door's doorway, in a straight line from where the route crosses it (M5 7 P7l: not along the route, <see cref="DoorCrossings.StepsOf"/>), the body stops to Use it, in metres: inside ACE's door use radius (2 m on the Aluvian house door, <c>weenie_properties_float</c> key 54) so the server does not walk the player to it, and outside contact: a walker stops 0.52 m short of a closed door's panel (the server-object host, docs/physics.md section 10), so a stop beyond the walker's radius plus 0.5 m keeps it from pressing on the panel, where it would stay stuck after the door turns Ethereal. Not retail: Lifestone's own.</summary>
    public float DoorStandOffMetres { get; init; } = 1.3f;
}

/// <summary>
/// The wayfinding runner (P5a design 3.2, docs/wayfinding.md): runs a planned path to a goal the way a player at the keyboard would,
/// acting only through <see cref="IActionApi.Move"/> (<see cref="MoveIntents"/>: R2) and reading only <see cref="INavBody"/>. A stepped
/// API: call <see cref="Step"/> once per frame after the physics step; nothing blocks.
/// </summary>
/// <remarks>
/// <para><b>Steering.</b> Outdoors over the lattice (nav-natural-steering), the straight run: the body aims at a route vertex, the farthest
/// one up to the first past max(<see cref="StraightLeadMinMetres"/>, <see cref="StraightLeadSpeedFactor"/> x speed) of arc length that it sees
/// (the lattice at the straightening's clearance, off water and slope), whose skipped vertices lie within <see cref="SkipOffRouteMetres"/> of the line
/// and whose line crosses no more of the creature layer than the route does; never past a doorway or an indoor vertex. No turn key is pressed
/// within <see cref="HeadingDeadBandDegrees"/> of heading error, so the body runs straight and turns at the route's corners. Aiming at the next
/// vertex, a body that drifts <see cref="CrossTrackMetres"/> off the route tracks back onto it (the route's point <see cref="TrackLeadSeconds"/> ahead,
/// <see cref="TrackDeadBandDegrees"/>) until within <see cref="TrackReleaseMetres"/>: the physics confirmed the route, not the drift; near a static
/// (<see cref="TightClearanceNodes"/>) it tracks always, with the pursuit's short lead. Indoors over a room path (nav-indoor-steering), the same straight
/// run and turn rules: the body aims at the farthest route vertex it may run straight to (<see cref="CanRunStraightIndoors"/>: every skipped doorway vertex
/// within <see cref="DoorwayPassMetres"/> of the line, so the body still passes each doorway; the line, swept at the body's radius, inside the cells the route
/// enters; with a <see cref="RouteConfirmer"/>, the physics' sweep between the route's vertices clear; and no more of the creature layer than the route).
/// Outdoors towards a doorway, P5a's pure pursuit: the lead point lies max(<see cref="LeadMinMetres"/>, <see cref="LeadSpeedFactor"/> x speed) of arc length
/// ahead of the body's nearest point on the route, never past the next doorway vertex, and, outdoors, clamped to the farthest route vertex the
/// lattice sees (<see cref="NavMap.LineOfSight"/>), with <see cref="HeadingToleranceDegrees"/>. Either way a turn key once pressed is held
/// until the heading arrives, let go half a physics step's turn early (the measured turn rate over <see cref="PhysicsGlobals.MinQuantum"/>,
/// <see cref="TurnReleaseQuanta"/>) so it never overshoots
/// into the other side's key, and a press waits <see cref="TurnKeyMinSeconds"/> after the last change. Forward is held up to
/// <see cref="TurnInPlaceDegrees"/> of heading error, else the body turns in place first; it walks within <see cref="WalkNearStopMetres"/> of a stop and <see cref="WalkNearDoorwayMetres"/> of a
/// doorway vertex (the thin-doorway rule, <c>physics-thin-doorway</c>; but between two rooms it runs through a doorway unless the route turns sharply there or the sweep stops short of it:
/// <see cref="RunsThroughDoorway"/>, <see cref="WalkSharpTurnDegrees"/>; and in a dungeon it walks only the last <see cref="WalkNearDungeonDoorwayMetres"/> before such a doorway: <see cref="WalksNearDoorway"/>) and runs elsewhere (<see cref="MoveIntents.SetRun"/>). A key is
/// sent only when it changes: every movement key change is one MoveToState (Lifestone's own budget is 20 a second, <see cref="MaxKeysPerSecond"/>; ACE @95ef426d has no such limit on MoveToState, it only caps the position broadcasts they cause to one a second, <c>Player_Tick.cs</c> 394-403 <c>MoveToState_UpdatePosition_Threshold</c>).</para>
/// <para><b>Creatures.</b> With an <see cref="INavCreatureFeed"/>, the run's creature layer (<see cref="Creatures"/>, nav-creature-avoid) is
/// refreshed every <see cref="CreatureRefreshSeconds"/> and given to the planner, and to a room source's graph (<see cref="RoomGraph.Creatures"/>,
/// nav-creatures-indoor), at each plan (the goal's own creature left out). A creature
/// the plan did not know there (new, or moved over <see cref="CreatureMovedMetres"/>) whose disc meets the route's next
/// <see cref="CreatureLookAheadMetres"/> is a replan, at most one each <see cref="CreatureReplanSeconds"/>; a creature whose disc already
/// holds the body is not (it has noticed the body).</para>
/// <para><b>Plans.</b> Off the route by <see cref="OffRouteMetres"/>: a replan. A horizon route (<see cref="NavPathStatus.Horizon"/>):
/// once <see cref="NavMap.Version"/> has changed and the body is within <see cref="HorizonReplanMetres"/> of its end, a plan to the real
/// goal (handoff-s2's contract); at its end with no new tiles the body stands. With a <see cref="RouteConfirmer"/>, every path is confirmed
/// by the physics before it is run; a refused sample scars its node and asks for a replan.</para>
/// <para><b>Stuck and the ladder.</b> Progress is arc length along the route. Under <see cref="StuckProgressMetres"/> in
/// <see cref="StuckSeconds"/> with forward held and on the ground is one rung: (1) back off <see cref="BackOffMetres"/> and re-aim;
/// (2) sidestep left <see cref="SidestepLeftMetres"/>, then right <see cref="SidestepRightMetres"/>; (3) a charged jump forward (the
/// JumpCharge path: <see cref="MoveIntents.ChargeJump"/>, then <see cref="MoveIntents.Jump"/> after <see cref="JumpCharge.FillSeconds"/>);
/// (4) scar the stuck node and a metre round it and replan; the fourth rung of the <see cref="MaxStuckPlans"/>th plan is
/// <see cref="NavRunState.Blocked"/>, naming the nearest static. Progress of <see cref="LadderResetMetres"/> past where the ladder began
/// ends it.</para>
/// <para><b>Ends.</b> An intent completing DryRun, Refused, Cancelled (the kill switch) or NotImplemented ends the run Blocked naming the
/// outcome. A person's movement key (<see cref="MoveIntents.HumanKey"/>) ends it Interrupted. A jump of over <see cref="TeleportMetres"/>
/// in one step is Failed, as <see cref="RouteDriver"/>'s.</para>
/// <para>Not retail: every constant here is Lifestone's own. Game thread only.</para>
/// </remarks>
public sealed partial class NavRunner : IDisposable
{
    /// <summary>How far past a doorway along the route, in metres, a door counts as behind the body (its step is done; a door that closes then is not the run's concern). Not retail: Lifestone's own.</summary>
    public const float DoorClearMetres = 0.5f;

    /// <summary>The shortest lead distance, in metres. Not retail: Lifestone's own.</summary>
    public const float LeadMinMetres = 2f;

    /// <summary>The lead distance per metre a second of speed. Not retail: Lifestone's own.</summary>
    public const float LeadSpeedFactor = 0.6f;

    /// <summary>
    /// How near the indoor straight run's line passes a doorway vertex it skips, in metres: the room graph's doorway tolerance
    /// (<see cref="CellRoutePlanner.PushIntoCellMetres"/>, the den's scripted legs' pass radius), so the body still goes through each doorway.
    /// </summary>
    public const float DoorwayPassMetres = CellRoutePlanner.PushIntoCellMetres;

    /// <summary>How high above the floor the indoor straight run's line is tested and swept, in metres (<see cref="RouteConfirmer.WallCheckHeightMetres"/>).</summary>
    public const float IndoorLineLiftMetres = RouteConfirmer.WallCheckHeightMetres;

    /// <summary>The sampling step of the indoor straight run's cell test, in metres (the creature layer's 0.5 m). Not retail: Lifestone's own.</summary>
    public const float IndoorLineSampleMetres = NavCreatures.SampleMetres;

    /// <summary>The heading error within which no turn key is held, in degrees (<see cref="RouteDriver"/>'s 5): the pursuit's (outdoors towards a doorway). Not retail: Lifestone's own.</summary>
    public const double HeadingToleranceDegrees = 5.0;

    /// <summary>
    /// The straight run's heading dead-band, in degrees: no turn key is pressed within it (nav-natural-steering; at 30 m a 6 degree error misses the
    /// aimed vertex by 3 m, and the error only grows near the vertex, where the aim moves on to the next corner). Not retail: Lifestone's own.
    /// </summary>
    public const double HeadingDeadBandDegrees = 6.0;

    /// <summary>
    /// Off the route by more than this while aiming at the next vertex, in metres, the straight run tracks back onto the route. The body turns in physics
    /// steps (<see cref="PhysicsGlobals.MinQuantum"/>: a running turn of about 200 degrees a second moves 6.8 degrees a step), so a heading is good to half a
    /// step, 3.4 degrees, which drifts about 6 m in 100 m; the physics confirmed the route's line, not the drift (the belt walk through the streamer ran into
    /// static 0x02000258 in 0xA8B3 2.9 m off its route). Not retail: Lifestone's own.
    /// </summary>
    public const float CrossTrackMetres = 1f;

    /// <summary>Back within this of the route, in metres, the straight run aims at its vertex again. Not retail: Lifestone's own.</summary>
    public const float TrackReleaseMetres = 0.3f;

    /// <summary>The tracking aim's shortest lead along the route, in metres (never past the aimed vertex). Not retail: Lifestone's own.</summary>
    public const float TrackLeadMinMetres = 10f;

    /// <summary>The tracking aim's lead per metre a second of speed (25 m at a fighter's 10 m/s: about 2 degrees of intercept a metre off). Not retail: Lifestone's own.</summary>
    public const float TrackLeadSeconds = 2.5f;

    /// <summary>The dead-band while tracking, in degrees (never under <see cref="DeadBandMinQuanta"/> of a physics step's turn). Not retail: Lifestone's own.</summary>
    public const double TrackDeadBandDegrees = 4.0;

    /// <summary>
    /// A lattice clearance (nodes) at most this at the body or <see cref="TightLookAheadMetres"/> ahead on the route is near a static: there the straight run
    /// tracks the route always, with the pursuit's lead (max(<see cref="LeadMinMetres"/>, <see cref="LeadSpeedFactor"/> x speed)), so the body keeps to the
    /// line the physics confirmed (the confirmer's placements allow 0.5 m sideways; the belt walk stuck 1.2 m off its route on static 0x02000258, whose
    /// collision reaches past its stamp, TODO(nav-static-footprint)). Not retail: Lifestone's own.
    /// </summary>
    public const int TightClearanceNodes = 3;

    /// <summary>How far ahead on the route the straight run looks for a static (<see cref="TightClearanceNodes"/>), in metres. Not retail: Lifestone's own.</summary>
    public const float TightLookAheadMetres = 6f;

    /// <summary>The straight run's shortest look-ahead for its aimed vertex, in metres. Not retail: Lifestone's own.</summary>
    public const float StraightLeadMinMetres = 10f;

    /// <summary>The straight run's look-ahead per metre a second of speed (30 m at a fighter's 10 m/s). Not retail: Lifestone's own.</summary>
    public const float StraightLeadSpeedFactor = 3f;

    /// <summary>
    /// Within this of its aimed vertex, the straight run aims at the vertex after it when the body sees it, so the turn is taken at the corner instead of
    /// chasing the vertex (whose bearing swings as the body reaches it): max(this, <see cref="CornerSwitchSeconds"/> x speed), in metres. Not retail: Lifestone's own.
    /// </summary>
    public const float CornerSwitchMetres = 3f;

    /// <summary>The corner switch's distance per metre a second of speed (4 m at a fighter's 10 m/s). Not retail: Lifestone's own.</summary>
    public const float CornerSwitchSeconds = 0.4f;

    /// <summary>The farthest a route vertex the straight run aims past may lie from the body's line to its aim, in metres (inside <see cref="OffRouteMetres"/>). Not retail: Lifestone's own.</summary>
    public const float SkipOffRouteMetres = 2f;

    /// <summary>
    /// How many physics steps of turning ahead a held turn key is let go (the measured turn rate times <see cref="PhysicsGlobals.MinQuantum"/>; the body
    /// turns only when the physics steps, 30 times a second): half a step, so the heading ends within half a step either side instead of passing into
    /// the other side's key. Not retail: Lifestone's own.
    /// </summary>
    public const double TurnReleaseQuanta = 0.5;

    /// <summary>The least dead-band in physics steps of turning (<see cref="TurnReleaseQuanta"/> plus a margin): a dead-band under half a step presses back and forth. Not retail: Lifestone's own.</summary>
    public const double DeadBandMinQuanta = 0.75;

    /// <summary>The turn rate assumed before one is measured, in degrees a second (a human's turn, about a quarter turn a second). Not retail: Lifestone's own.</summary>
    public const double DefaultTurnDegreesPerSecond = 90.0;

    /// <summary>How often the creature layer is refreshed from its feed, in seconds. Not retail: Lifestone's own.</summary>
    public const double CreatureRefreshSeconds = 0.5;

    /// <summary>How far from the body the feed is read, in metres (past the widest disc, 35 m and both bodies, plus the look-ahead). Not retail: Lifestone's own.</summary>
    public const float CreatureLookMetres = 120f;

    /// <summary>How much of the route ahead a creature's disc is tested against, in metres. Not retail: Lifestone's own.</summary>
    public const float CreatureLookAheadMetres = 60f;

    /// <summary>How far a creature may move from where the plan knew it before its disc on the route is a replan, in metres. Not retail: Lifestone's own.</summary>
    public const float CreatureMovedMetres = 3f;

    /// <summary>The least time between two replans for creatures, in seconds. Not retail: Lifestone's own.</summary>
    public const double CreatureReplanSeconds = 2.0;

    /// <summary>
    /// A turn key once held is let go only within this heading error, or half a physics step's turn of it (<see cref="TurnReleaseQuanta"/>), or when the error
    /// changes side, in degrees: the hysteresis that keeps the turn key from flickering round the tolerance (each flick is a MoveToState). Not retail: Lifestone's own.
    /// </summary>
    public const double TurnReleaseDegrees = 1.5;

    /// <summary>
    /// The least time from the last change of the turn key to a press, in seconds (unless the error is past <see cref="TurnInPlaceDegrees"/>; a
    /// release is never held back, so the body never turns past its heading): round a corner the lead point moves on as fast as the body turns, and
    /// a key pressed and let go every frame would be 30 MoveToStates a second against Lifestone's own budget of 20 (ACE @95ef426d sets no limit:
    /// <c>Player_Tick.cs</c> 394-403 caps only the position broadcasts). P5a held releases too, so a 0.2 s press overshot the 5 degree tolerance
    /// into the other side's key, 9 to 11 key changes a second round a corner (nav-natural-steering). Not retail: Lifestone's own.
    /// </summary>
    public const double TurnKeyMinSeconds = 0.2;

    /// <summary>The heading error beyond which forward is let go and the body turns in place, in degrees. Not retail: Lifestone's own.</summary>
    public const double TurnInPlaceDegrees = 60.0;

    /// <summary>A turn press of the other side within this of the last turn key's release is a reversal (<see cref="TurnReversals"/>, report-only), in seconds. Not retail: Lifestone's own.</summary>
    public const double ReversalCountSeconds = 1.5;

    /// <summary>
    /// A turn press of the other side within this of the last turn key's release is a flip (<see cref="TurnFlips"/>, report-only), in seconds: under
    /// <see cref="TurnKeyMinSeconds"/> only an error past <see cref="TurnInPlaceDegrees"/> may press, so a flip is the aim jumping, not an overshoot. Not retail: Lifestone's own.
    /// </summary>
    public const double FlipCountSeconds = 0.2;

    /// <summary>A forward press within this of the forward key going up is a re-press (<see cref="ForwardRepresses"/>, report-only), in seconds. Not retail: Lifestone's own.</summary>
    public const double ForwardRepressCountSeconds = 0.3;

    /// <summary>A press that turned in place, then two reversals within this, is a swing (<see cref="Swings"/>, report-only, logged), in seconds. Not retail: Lifestone's own.</summary>
    public const double SwingWindowSeconds = 3.0;

    /// <summary>
    /// D134 (the owner's turn at a junction): an indoor route that turns by at least this within <see cref="CornerSpanMetres"/> is a corner, in degrees: the body
    /// runs to the corner point, lets forward go, turns in place to the next leg's heading, then runs on. Not retail: Lifestone's own.
    /// </summary>
    public const double CornerMinDegrees = 45.0;

    /// <summary>A route vertex turning less than this is no part of a corner (a door pair on a straight), in degrees. Not retail: Lifestone's own.</summary>
    public const double CornerVertexDegrees = 10.0;

    /// <summary>The longest stretch of route whose turns add up to one corner, in metres (the den's corner cells are cut by a 2.7 m diagonal). Not retail: Lifestone's own.</summary>
    public const float CornerSpanMetres = 3.5f;

    /// <summary>
    /// Within this of the corner point the body aims at it (the point where the leg before and the leg after meet, or the vertex itself), and the farthest the
    /// corner point may lie from the corner's first and last vertices, in metres. Not retail: Lifestone's own.
    /// </summary>
    public const float CornerApproachMetres = 2.5f;

    /// <summary>
    /// Within this of a corner's point the body aims at it when the straight run does not aim past the corner and the line to it is clear (else it would start
    /// its turn for the corner's diagonal early, then turn again), in metres. Not retail: Lifestone's own.
    /// </summary>
    public const float CornerLookMetres = 6f;

    /// <summary>How far ahead of the body's foot on the leg before a corner the body aims while it runs to the corner, in metres. Not retail: Lifestone's own.</summary>
    public const float CornerLeadMetres = 3f;

    /// <summary>The dead-band while the body runs to a corner, in degrees: within a few metres of a turn in place a small heading error is let ride. Not retail: Lifestone's own.</summary>
    public const double CornerApproachDeadBandDegrees = 15.0;

    /// <summary>Indoors, a route vertex the body is within this of is passed (the next vertex is the one after it), in metres. Not retail: Lifestone's own.</summary>
    public const float VertexPassMetres = 0.75f;

    /// <summary>A leg within this of a N/S/E/W axis is snapped to it for the corner (the den's corridors run on the axes), in degrees. Not retail: Lifestone's own.</summary>
    public const double CornerAxisSnapDegrees = 5.0;

    /// <summary>The body lets forward go this far before the corner point at the least, in metres. Not retail: Lifestone's own.</summary>
    public const float CornerStopMetres = 0.4f;

    /// <summary>The body lets forward go this many seconds of its speed before the corner point (it coasts as the run animation stops). Not retail: Lifestone's own.</summary>
    public const float CornerBrakeSeconds = 0.1f;

    /// <summary>The longest the body stands turning at a corner, in seconds (an about-face at the slowest turn rate is 2 s). Not retail: Lifestone's own.</summary>
    public const double CornerTurnSeconds = 2.5;

    /// <summary>An indoor point goal is reached only within this height of it too (a room above or below is not the room), in metres. Not retail: Lifestone's own.</summary>
    public const float ArrivalHeightMetres = 1.5f;

    /// <summary>Within this of a stop the runner walks, at the least (<see cref="WalkZoneMetres"/> widens it with her speed). Not retail: Lifestone's own.</summary>
    public const float WalkNearStopMetres = 2f;

    /// <summary>
    /// M6 14 (portal overshoot): the depth of the zone before a stop in which the runner walks, in metres: <see cref="WalkNearStopMetres"/>, or wider when her live speed needs it. The walk takes over at the step after the one that finds the body in the
    /// zone, and the run's coast (<see cref="CornerBrakeSeconds"/> of her speed) is spent slowing to it, so the zone holds the goal's tolerance plus her speed over one step and the coast: she reaches the goal at walking pace at any Run skill and
    /// any frame time (at the buffed climber's 7.9 m/s and the live log's 141 ms slow frame, 2.4 m).
    /// </summary>
    /// <param name="tolerance">The goal's stop tolerance.</param>
    /// <param name="liveSpeed">Her speed now, metres a second (the runner's measure).</param>
    /// <param name="frameSeconds">The last step's time.</param>
    public static float WalkZoneMetres(float tolerance, float liveSpeed, double frameSeconds) =>
        MathF.Max(WalkNearStopMetres, tolerance + (MathF.Max(0f, liveSpeed) * (float)(Math.Max(frameSeconds, 0.0) + CornerBrakeSeconds)));

    /// <summary>
    /// M6 14 (portal overshoot): how far past the goal's centre a body may still be when it comes to rest on a stop, in metres: the runner decides the arrival anywhere within the goal's tolerance, the body moves one physics step
    /// (<see cref="PhysicsGlobals.MinQuantum"/>) before the released keys act, then coasts as the motion stops (the runner's brake: <see cref="CornerStopMetres"/>, or <see cref="CornerBrakeSeconds"/> of the speed). The speed is the walk's
    /// (<see cref="MotionInterpreter.WalkAnimSpeed"/>, retail's walk, which no Run skill changes): <see cref="WalkZoneMetres"/> makes every stop a walked one. 1.0 m at the default 0.5 m tolerance.
    /// </summary>
    /// <param name="tolerance">The goal's stop tolerance.</param>
    public static float StopMetres(float tolerance) =>
        MathF.Max(0f, tolerance) + (MotionInterpreter.WalkAnimSpeed * (float)PhysicsGlobals.MinQuantum) + MathF.Max(CornerStopMetres, CornerBrakeSeconds * MotionInterpreter.WalkAnimSpeed);

    /// <summary>
    /// Within this of a doorway vertex the runner walks (the thin-doorway rule), except where it runs through the doorway (<see cref="RunsThroughDoorway"/>: indoors, M5 2 F6) and, in a dungeon,
    /// outside the shorter <see cref="WalkNearDungeonDoorwayMetres"/>.
    /// Not retail: Lifestone's own.
    /// </summary>
    public const float WalkNearDoorwayMetres = 3f;

    /// <summary>
    /// In a dungeon, the body walks within this of a doorway vertex it has not passed and does not run through (<see cref="WalksNearDoorway"/>, m6-dungeon-run-share). Measured on the dat legs
    /// of every dungeon profile at 0.5, 0.75, 1, 1.25, 1.5 and 2 m (<c>IndoorRunThroughDatTests</c>): no size added a rung, a wall frame or a replan, and the body's track over the route's length did not grow with a
    /// smaller zone (the walk that matters is the one through the turn, <see cref="_doorwayWalk"/>); only 0.5 m brings the Lodge's doorway chain, three sharp turns in 12 m, under the bound. Not retail: Lifestone's own.
    /// </summary>
    public const float WalkNearDungeonDoorwayMetres = 0.5f;

    /// <summary>
    /// In a dungeon, within <see cref="WalkNearDoorwayMetres"/> of a doorway the body does not run through, forward is held up to this much heading error instead of <see cref="TurnInPlaceDegrees"/>, in degrees: it no longer
    /// walks there, and a run begun 50 degrees off the aim swings metres wide (the synthetic room of <c>IndoorDoorwayWalkTests</c> left its cells and replanned). Measured on the dat legs at 15 (the Lodge's legs over the bound),
    /// 30 and at the open 60 (the wide run); walking until lined up instead, at 20, 30 and 45, leaves the Lodge over the bound. Not retail: Lifestone's own.
    /// </summary>
    public const double TurnInPlaceNearDoorwayDegrees = 30.0;

    /// <summary>
    /// The sharpest turn of the route at an indoor doorway vertex the body runs through, in degrees: beyond it the body turns in place at the doorway
    /// (<see cref="TurnInPlaceDegrees"/>), so it walks near it. Not retail: Lifestone's own.
    /// </summary>
    public const double WalkSharpTurnDegrees = TurnInPlaceDegrees;

    /// <summary>Off the route by more than this is a replan (<see cref="PathPlanner.OffRouteMetres"/>).</summary>
    public const float OffRouteMetres = PathPlanner.OffRouteMetres;

    /// <summary>Within this of a horizon route's end, a plan to the real goal once the map changed (<see cref="PathPlanner.HorizonReplanMetres"/>).</summary>
    public const float HorizonReplanMetres = PathPlanner.HorizonReplanMetres;

    /// <summary>The least arc-length progress that counts, in metres. Not retail: Lifestone's own.</summary>
    public const float StuckProgressMetres = 0.25f;

    /// <summary>
    /// How far ahead of its last progress along the route the body's nearest point is searched each step, in metres (D134: progress only moves forward, so the
    /// next vertex never steps back to the leg behind when the body swings wide at a corner or passes over a leg on another storey). Not retail: Lifestone's own.
    /// </summary>
    public const float ProgressWindowMetres = 8f;

    /// <summary>
    /// How far behind its last progress the body's nearest point is also searched, in metres: the leg the body is on stays a candidate when the body steps back
    /// or sideways (the progress then holds), so a later leg across a wall never wins by default. Not retail: Lifestone's own.
    /// </summary>
    public const float ProgressBehindMetres = 2f;

    /// <summary>
    /// When the nearest point in the progress window is this much farther than the route's nearest point anywhere, the progress is searched again over the
    /// whole route (a body moved by a station or a server's MoveTo), in metres. Not retail: Lifestone's own.
    /// </summary>
    public const float ProgressReanchorMetres = 1.5f;

    /// <summary>
    /// How long the body may stand at the end of a horizon route (or, M6 7 N, after a plan that failed with tiles missing, <see cref="TileWaits"/>) waiting for the next tiles to attach, in seconds; past it the run ends Blocked
    /// (<c>why=horizon:</c> naming the landblocks). Tiles build in 15-40 ms on a worker, so this is generous. Not retail: Lifestone's own.
    /// </summary>
    public const double HorizonWaitSeconds = 30.0;

    /// <summary>Without progress for this long with forward held (the clock starts when forward is pressed, so a turn in place before it is not stuck) on the ground is a rung. Not retail: Lifestone's own.</summary>
    public const double StuckSeconds = 1.5;

    /// <summary>Without progress for this long whatever the keys is a rung too (a body that turns and turns). Not retail: Lifestone's own.</summary>
    public const double NoProgressSeconds = 6.0;

    /// <summary>Rung 1's back-off, in metres. Not retail: Lifestone's own.</summary>
    public const float BackOffMetres = 1f;

    /// <summary>Rung 2's sidestep left, in metres. Not retail: Lifestone's own.</summary>
    public const float SidestepLeftMetres = 1f;

    /// <summary>Rung 2's sidestep right (from the left one's end), in metres. Not retail: Lifestone's own.</summary>
    public const float SidestepRightMetres = 2f;

    /// <summary>The longest a rung's move is held when the body does not get there, in seconds. Not retail: Lifestone's own.</summary>
    public const double RungMoveSeconds = 3.0;

    /// <summary>
    /// M5 7 P7i (D144): the farthest a rung's move may carry the body from where the move began, in metres; past it the move's keys go up and the rung ends. The jump rung held forward until it landed or
    /// <see cref="RungMoveSeconds"/> passed: on the rehearsal of 2026-10-30 one such jump carried the mage 15 m into a pocket. Not retail: Lifestone's own (the side step's 2 m and a metre more).
    /// </summary>
    public const float RungMoveMaxMetres = 3f;

    /// <summary>The scar round the stuck node, in metres (the node and a metre round it). Not retail: Lifestone's own.</summary>
    public const float StuckScarMetres = 1.5f;

    /// <summary>Plans in one stuck episode before its fourth rung is Blocked. Not retail: Lifestone's own.</summary>
    public const int MaxStuckPlans = 3;

    /// <summary>Progress past where the ladder began that ends it, in metres. Not retail: Lifestone's own.</summary>
    public const float LadderResetMetres = 2f;

    /// <summary>
    /// Refused confirmations in a row (no path confirmed between them) before the run is Blocked. Not retail: Lifestone's own. P5a counted them over the
    /// whole run, so a 2 km run that met three small statics the tile under-stamps far apart (TODO(nav-static-footprint)) ended Blocked; the forest run
    /// already met two at its start.
    /// </summary>
    public const int MaxConfirmFailures = 3;

    /// <summary>A jump of the body in one step longer than this is Failed (<see cref="RouteDriver.TeleportMetres"/>).</summary>
    public const float TeleportMetres = RouteDriver.TeleportMetres;

    /// <summary>The speed estimate's smoothing per step. Not retail: Lifestone's own.</summary>
    private const float SpeedSmoothing = 0.2f;

    /// <summary>The turn rate estimate's smoothing per step. Not retail: Lifestone's own.</summary>
    private const double RateSmoothing = 0.3;

    private const string LogCategory = "wayfinding";

    private readonly MoveIntents _move;
    private readonly INavBody _body;
    private readonly INavPathSource _paths;
    private readonly RouteConfirmer? _confirmer;
    private readonly MovementAudit? _audit;
    private readonly ILogSink _log;
    private readonly List<ActionHandle> _inFlight = [];
    private readonly Queue<double> _keyTimes = new();
    private readonly INavCreatureFeed? _creatureFeed;
    private readonly List<NavCreature> _planned = [];
    private NavScar[] _scarBuffer = new NavScar[8];
    private bool[] _scarIgnored = new bool[8];

    private NavGoal? _goal;
    private NavPath? _path;
    private NavRoute? _route;
    private int _mapVersion;
    private bool _confirming;
    private int _confirmFailures;
    private int _refusalsInARow;
    private double _startTime;
    private double _endTime = double.NaN;
    private double _lastTime;
    private Vector2 _lastGlobal;
    private float _lastZ;
    private bool _started;
    private float _speed;
    private double _frameSeconds = PhysicsGlobals.MinQuantum; // M6 14: the last step's time (the live walk zone)
    private PortalKeepOff? _keep; // M6 14: KeepOff with the own portal's reach set for this run's goal (ResolveKeepOff)
    private bool _zoneLogged;

    private float _bestProgress;
    private double _lastProgressTime;
    private double _yieldSince;
    private bool _ladderActive;
    private int _ladderLevel;
    private int _stuckPlans;
    private float _ladderOrigin;
    private Rung _rung;
    private double _rungStart;
    private Vector2 _rungFrom;
    private bool _rungLeftGround;
    private bool _rungFromPatch; // M5 7 P7i: the rung's move began inside an avoid region

    private bool _forward;
    private bool _curveHeld;
    private bool _reachHeld;
    private double _forwardSince;
    private bool _back;
    private int _turn;
    private double _turnChangedAt = double.NegativeInfinity;
    private double _forwardOffAt = double.NegativeInfinity;
    private int _releasedTurn;
    private double _turnReleasedAt = double.NegativeInfinity;
    private bool _pressInPlace;
    private double _swingFrom = double.NegativeInfinity;
    private int _swingReversals;
    private NavRoute? _nextRoute;
    private int _lastNext;
    private NavRoute? _progressRoute;
    private float _progressArc;
    private NavRoute? _cornerRoute;
    private int _cornerLast = -1;
    private Vector2 _cornerPoint;
    private Vector2 _cornerIn;
    private double _cornerHeading;
    private bool _cornerTurning;
    private bool _cornerActive;
    private int _cornerSerial;
    private int _steerCorner;
    private int _pressCorner;
    private int _releasedCorner;
    private sbyte[] _cornerChecked = new sbyte[16];
    private bool _cornerRouteIndoors;
    private float _steerProgress;
    private NavPath? _cornerCheckedPath;
    private double _cornerTurnFrom;
    private int _side;
    private bool _runKey;
    private bool _awaitingTiles;
    private double _awaitingSince;

    private float _lastHeading = float.NaN;
    private double _turnRate = DefaultTurnDegreesPerSecond;
    private int _turnHeldSteps;
    private double _nextCreatureRefresh;
    private double _lastCreatureReplan = double.NegativeInfinity;
    private double? _tilesWaitSince; // M6 7 N: when a failed plan began to wait for the corridor's tiles (WaitForTiles), or null
    private int _aimVertex;
    private bool _tracking;
    private bool _noLayerLogged;
    private List<DoorStep> _doorSteps = [];
    private readonly List<NavDoor> _doorBuffer = [];
    private readonly HashSet<uint> _lockAsked = []; // M6 8 C: the doors whose lock was asked of the server this run (once each)
    private int _doorFeedVersion;
    private int _doorMapVersion;
    private RoomGraph? _doorRooms;
    private int _doorIndex;
    private bool _doorOpening;
    private bool _doorSeenOpen;
    private bool _doorStalledUse;
    private int _doorUses;
    private int _doorReopens;
    private double _doorUsedAt;
    private double _doorFirstUse;
    private ActionHandle? _doorHandle;
    private RoomGraph? _rooms;
    private bool _keepOffInsideLogged;
    private (string Portal, CellFrame Body)? _keepOffNearest;
    private readonly NpcBodies _npcs = new();
    private double _npcLaneUntil = double.NegativeInfinity;
    private bool _npcLaneWaited;
    private bool _npcIndoorLogged;
    private int[] _sweptTo = new int[16];

    /// <summary>The plan's rooms have no way outside: a dungeon, not a building (<see cref="WalksNearDoorway"/>).</summary>
    private bool _dungeonRooms;

    /// <summary>The body walks for a doorway, or for the turn in place at one (<see cref="WalksNearDoorway"/>).</summary>
    private bool _doorwayWalk;
    private readonly Dictionary<uint, (EnvCellData Cell, CellBsp Bsp)> _cellShapes = [];
    private RoomGraph? _cellShapesOf;
    private int _sweptCount;

    /// <summary>A runner moving the player bound to <paramref name="move"/>, reading <paramref name="body"/>, planning with <paramref name="paths"/>.</summary>
    /// <param name="move">The session's <see cref="IActionApi.Move"/> (bound to the player's controller).</param>
    /// <param name="body">The player's body (<see cref="ControllerNavBody"/>).</param>
    /// <param name="paths">Where paths come from.</param>
    /// <param name="confirmer">The physics confirmation, or null to run unconfirmed paths (the scripted-body tests).</param>
    /// <param name="audit">The walk's audit, or null.</param>
    /// <param name="options">The tuning, or null for the defaults.</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="creatures">The hostile creatures the client sees (<see cref="HostileCreatureFeed"/>), or null for a run over terrain and statics alone.</param>
    public NavRunner(MoveIntents move, INavBody body, INavPathSource paths, RouteConfirmer? confirmer = null, MovementAudit? audit = null, NavRunnerOptions? options = null, ILogSink? log = null, INavCreatureFeed? creatures = null)
    {
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(paths);
        _move = move;
        _body = body;
        _paths = paths;
        _confirmer = confirmer;
        _audit = audit;
        Options = options ?? NavRunnerOptions.Default;
        Scars = new NavScars(Options.ScarClock);
        _log = log ?? NullLogSink.Instance;
        _creatureFeed = creatures;
        _move.HumanKey += OnHumanKey;
    }

    private enum Rung
    {
        None,
        BackOff,
        SidestepLeft,
        SidestepRight,
        JumpCharge,
        JumpAir,
    }

    /// <summary>The tuning.</summary>
    public NavRunnerOptions Options { get; }

    /// <summary>Where the run stands.</summary>
    public NavRunState State { get; private set; } = NavRunState.Idle;

    /// <summary>Why the run ended Blocked, Interrupted, Failed or ArrivedWithoutSight (shaped like <see cref="RouteDriver.FailureReason"/>); else null.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The goal.</summary>
    public NavGoal? Goal => _goal;

    /// <summary>The path being run (null before the first plan).</summary>
    public NavPath? Path => _path;

    /// <summary>The scars of this run (the planner's keep-out overlay; 30 s each).</summary>
    public NavScars Scars { get; }

    /// <summary>The creature layer of this run (nav-creature-avoid): the hostile creatures the feed gave at its last refresh, the goal's own left out; empty without a feed.</summary>
    public NavCreatures Creatures { get; } = new();

    /// <summary>
    /// The bodies of the NPCs and vendors the client sees (M6 4 W2, <c>m6-npc-in-door-lane</c>), or null for a run that knows none (its plans then run through an NPC's body, as
    /// before). With a feed and a lattice, each plan stamps the bodies near the way as blocked discs (<see cref="NpcBodies"/>, <see cref="NavScars.SetBodies"/>), the goal's own
    /// object left out, so the route goes round an NPC when there is room; a body the plan cannot go round (in a doorway lane, a gap narrower than the body and its disc) is
    /// waited for <see cref="NpcBodies.LaneWaitSeconds"/> with every key released, then planned once more (<c>npc-in-lane</c>), then the run ends Blocked
    /// <c>no path: npc-in-lane:0x&lt;guid&gt;</c>: it never pushes. Both journey hosts set it.
    /// </summary>
    public INavBodyFeed? BodyFeed { get; set; }

    /// <summary>The failure name of a walk that cannot keep clear of a portal (M6 7 AI), after <c>no path: </c>: <c>portal-keep-off:&lt;name&gt;: ...</c>.</summary>
    public const string PortalKeepOffFailure = "portal-keep-off";

    /// <summary>
    /// M6 7 AI: the portals this run's plans and its straight runs keep clear of (<see cref="PortalKeepOff"/>), or null for a run that knows none. Set before <see cref="Start"/>. Each plan gives the list to the room graph (<see cref="RoomGraph.KeepOff"/>) and the outdoor portals to the
    /// lattice (<see cref="NavScars.SetKeepOff"/>), the line of every plan is checked against it, and a plan that cannot keep clear ends the run Blocked <c>no path: portal-keep-off:&lt;name&gt;</c>.
    /// </summary>
    public PortalKeepOff? KeepOff { get; set; }

    /// <summary>The nearest the body came to a kept portal at its storey in this run, in metres (infinite while none stood near): report-only, the verdict's <c>keep_off_nearest_m=</c>.</summary>
    public float KeepOffNearestMetres { get; private set; } = float.PositiveInfinity;

    /// <summary>M6 14: the nearest the body came to the walk's own portal (<see cref="KeptPortal.Own"/>) at its storey in this run, in metres (infinite without one): report-only, the verdict's <c>own_nearest_m=</c>.</summary>
    public float OwnNearestMetres { get; private set; } = float.PositiveInfinity;

    /// <summary>Where <see cref="KeepOffNearestMetres"/> was reached: the portal and the body's place; null while none stood near.</summary>
    public string? KeepOffNearestWhere => _keepOffNearest is { } at ? string.Create(CultureInfo.InvariantCulture, $"{at.Portal} from {at.Body}") : null;

    /// <summary>The failure name of a plan that would cross a pressure plate's wall (M6 14 holding plates), after <c>no path: </c>: <c>plate-keep-off:&lt;name&gt;: ...</c>.</summary>
    public const string PlateKeepOffFailure = "plate-keep-off";

    /// <summary>
    /// M6 14 holding plates: the nearest the body came, standing on the ground, to a pressure plate of the room graph (<see cref="RoomGraph.PlateList"/>) at its storey in this run, planar metres from the plate's origin (infinite while none
    /// stood near): report-only, the verdict's <c>plate_nearest_m=</c>. The flight of a jump is left out (she is over the plates then).
    /// </summary>
    public float PlateNearestMetres { get; private set; } = float.PositiveInfinity;

    /// <summary>Where <see cref="PlateNearestMetres"/> was reached: the plate and the body's place; null while none stood near.</summary>
    public string? PlateNearestWhere { get; private set; }

    /// <summary>
    /// M6 14 holding plates: what tells, after a jump over plates, whether a plate's spells reached her anyway (the chat line or her enchantments; the session's <c>PlateSpellWitness</c>), or null for a run that cannot tell (the landing check
    /// is then the body's alone).
    /// </summary>
    public IJumpWitness? JumpWitness { get; set; }

    /// <summary>The NPC bodies of the last plan (empty without a <see cref="BodyFeed"/>).</summary>
    public NpcBodies Npcs => _npcs;

    /// <summary>The waits for an NPC in the way the plan could not go round in this run. Report-only.</summary>
    public int NpcLaneWaits { get; private set; }

    /// <summary>
    /// The doors the client knows and the way to Use one (M5 item 5, d1), or null for a run that knows no doors (its routes then go through any door, as before). With a feed, a
    /// leg on foot whose route crosses a closed door stops <see cref="NavRunnerOptions.DoorStandOffMetres"/> before the doorway, logs <c>purpose=door</c> (D47), Uses it and waits for
    /// its Ethereal state, then walks on (<see cref="NavRunnerOptions.DoorOpenSeconds"/>, else the leg fails <c>door-closed-timeout</c>); an open door is walked through without a Use. A body that stalls short of
    /// the stop within <see cref="NavRunnerOptions.DoorUseReachMetres"/> of the shut door Uses it from there (M5 7 P7l, D141), never a rung or a replan into the same shut door.
    /// </summary>
    public INavDoorFeed? Doors { get; set; }

    /// <summary>The doors the current route crosses, in route order (empty without a feed or when it crosses none).</summary>
    public IReadOnlyList<DoorStep> DoorSteps => _doorSteps;

    /// <summary>Uses sent to doors in this run. Report-only.</summary>
    public int DoorUses { get; private set; }

    /// <summary>
    /// The longest the runner yields to one server MoveTo, in seconds: <c>PlayerMovementController.ServerMoveToLimitSeconds</c> (ACE's <c>defaultMoveToTimeout</c>,
    /// <c>Player_Move.cs:16</c> @95ef426d); past it the run fails, named (<c>TODO(m5-moveto-stuck)</c>).
    /// </summary>
    public const double YieldLimitSeconds = PlayerMovementController.ServerMoveToLimitSeconds;

    /// <summary>The yields that ended on a stuck server MoveTo: the body's rule ended it, or the runner's backstop (<see cref="YieldLimitSeconds"/>) failed the run.</summary>
    public int YieldsStuck { get; private set; }

    /// <summary>Door steps Used from where the body stalled short of their stop (D141, <see cref="NavRunnerOptions.DoorUseReachMetres"/>). Report-only.</summary>
    public int StalledDoorUses { get; private set; }

    /// <summary>Doors seen open after this run's Use. Report-only.</summary>
    public int DoorsOpened { get; private set; }

    /// <summary>Doors the route crossed that were already open when the body reached them (no Use sent). Report-only.</summary>
    public int DoorsFoundOpen { get; private set; }

    /// <summary>The most creatures on the layer at one refresh. Report-only.</summary>
    public int CreaturesMax { get; private set; }

    /// <summary>Replans because a creature's disc met the route ahead. Report-only.</summary>
    public int CreatureReplans { get; private set; }

    /// <summary>The nearest a hostile creature's centre came to the body at a refresh, in metres (the goal's own left out); null before one was seen. Report-only.</summary>
    public float? CreatureMinMetres { get; private set; }

    /// <summary>Plans made (the first and every replan).</summary>
    public int Plans { get; private set; }

    /// <summary>Replans made.</summary>
    public int Replans { get; private set; }

    /// <summary>Rungs of the ladder climbed.</summary>
    public int Rungs { get; private set; }

    /// <summary>Times the run was stuck (each is a rung).</summary>
    public int Stuck => Rungs;

    /// <summary>
    /// M5 7 P7b (D139 g): the rungs climbed in the stuck episode under way, the replans round a scar included (the ladder's back-off, sidestep and jump, then the replan, then again); 0 when no episode is under way: a new run,
    /// or progress of <see cref="LadderResetMetres"/> past where the ladder began ended it. Report-only here: the fight reads it to give a walk up long before the runner's own <see cref="MaxStuckPlans"/> ends it Blocked.
    /// </summary>
    public int EpisodeRungs { get; private set; }

    /// <summary>Metres the body moved horizontally while the run was live.</summary>
    public float RouteMetres { get; private set; }

    /// <summary>Metres of <see cref="RouteMetres"/> the body moved at a walk (the run key said walk). Report-only: a body that walks a dungeon's leg is what the owner called aimless (M5 2 F6).</summary>
    public float WalkedMetres { get; private set; }

    /// <summary>Seconds from the start to the end (or to the last step).</summary>
    public double RunSeconds => (double.IsNaN(_endTime) ? _lastTime : _endTime) - _startTime;

    /// <summary>Movement key changes sent (each a MoveToState for a command; the run key sends none).</summary>
    public int KeyChanges { get; private set; }

    /// <summary>The most key changes in any one second.</summary>
    public int MaxKeysPerSecond { get; private set; }

    /// <summary>Turn keys pressed (a turn key going down, left or right; the owner's always-turning complaint, nav-natural-steering). Report-only.</summary>
    public int Turns { get; private set; }

    /// <summary>Turn presses of the other side within <see cref="ReversalCountSeconds"/> of the last turn key's release (D134). Report-only.</summary>
    public int TurnReversals { get; private set; }

    /// <summary>Turn presses of the other side within <see cref="FlipCountSeconds"/> of the last turn key's release: the aim jumping (D134). Report-only.</summary>
    public int TurnFlips { get; private set; }

    /// <summary>Turn presses during which forward was up (the body turned in place at some point of the press) (D134). Report-only.</summary>
    public int InPlacePresses { get; private set; }

    /// <summary>Forward presses within <see cref="ForwardRepressCountSeconds"/> of the forward key going up (D134). Report-only.</summary>
    public int ForwardRepresses { get; private set; }

    /// <summary>Steps whose next route vertex lay behind the last step's on the same route: the progress jumping backwards (D134). Report-only.</summary>
    public int ProgressBacks { get; private set; }

    /// <summary>
    /// M5 7 P7i: turn presses made because the body faced past <see cref="TurnInPlaceDegrees"/> from its aim and the steering would have pressed nothing (the
    /// corner approach's no-press zone or the indoor pass), each logged. Report-only.
    /// </summary>
    public int StandTurns { get; private set; }

    /// <summary>Turn presses that turned the body in place at a corner to the next leg's heading (D134, <see cref="CornerMinDegrees"/>). Report-only.</summary>
    public int CornerTurns { get; private set; }

    /// <summary>Starts of a run held back because the curve it would sweep met a static (<see cref="CurveClear"/>), each logged. Report-only.</summary>
    public int CurveTurns { get; private set; }

    /// <summary>
    /// Of <see cref="TurnReversals"/>, those between two corner turns at two corners (D134): a route that steps right then left round the den's grid turns in
    /// place at each corner (or, with <see cref="NavRunnerOptions.PredictiveSteering"/>, runs onto each next leg: <see cref="PredictCornerTurns"/>), a deliberate turn and no swing. Report-only.
    /// </summary>
    public int CornerReversals { get; private set; }

    /// <summary>Presses that turned in place followed by two reversals within <see cref="SwingWindowSeconds"/> (D134), each logged. Report-only.</summary>
    public int Swings { get; private set; }

    /// <summary>The steering's state for the offline walker's traces (D134): the route, the next vertex, the aimed vertex, the turn key (-1, 0, 1), forward held.</summary>
    /// <summary>Whether the plan's rooms are a dungeon's, no way outside (<see cref="WalksNearDoorway"/>: a building keeps the full walk zone).</summary>
    internal bool DungeonRooms => _dungeonRooms;

    internal (NavRoute? Route, int Next, int Aim, int Turn, bool Forward, bool Tracking, int Corner) SteeringState => (_route, _lastNext, _aimVertex, _turn, _forward, _tracking, _cornerActive ? (_cornerTurning ? 2 : 1) : 0);

    /// <summary>M5 7 P7i: true while a rung's recovery move is under way (the tests' bound on it).</summary>
    internal bool RungActive => _rung != Rung.None;

    /// <summary>True when the predictive steering held the keys at the last step (the walk bench's trace).</summary>
    internal bool PredictiveHeld => _predictiveHeld;

    /// <summary>The steering counters as fields: <c>turn_reversals= turn_flips= in_place= fwd_repress= progress_backs= swings=</c> (D134, report-only).</summary>
    public string SteeringFields() => string.Create(
        CultureInfo.InvariantCulture,
        $"turn_reversals={TurnReversals} turn_flips={TurnFlips} in_place={InPlacePresses} fwd_repress={ForwardRepresses} progress_backs={ProgressBacks} swings={Swings} corner_turns={CornerTurns} corner_reversals={CornerReversals} predict_keys={PredictKeyChanges} moved_m={MovedMetres:F0} straight10_m={LongStraightMetres:F0} short_taps={ShortTaps} wall_touches={WallTouches} object_touches={ObjectTouches} contact_s={ContactSeconds:F1}");

    /// <summary>How short a turn press counts as a tap (<see cref="ShortTaps"/>), in seconds: a person's quickest deliberate tap of a key is about a tenth of a second. Not retail: Lifestone's own.</summary>
    public const double ShortTapSeconds = 0.1;

    /// <summary>Turn presses let go within <see cref="ShortTapSeconds"/> (a frame or two of turn: no person taps a key so briefly). Report-only.</summary>
    public int ShortTaps { get; private set; }

    /// <summary>
    /// Touches of a wall while the run moved the body (<see cref="INavBody.Contact"/>: a cell's walls, a building, a static stopped a physics step), a touch being a contact after
    /// <see cref="ContactGapSeconds"/> without one; each is logged (the first <see cref="ContactLogLimit"/> of a run). Report-only.
    /// </summary>
    public int WallTouches { get; private set; }

    /// <summary>Touches of another object (a door, a body) while the run moved the body, as <see cref="WallTouches"/>. Report-only.</summary>
    public int ObjectTouches { get; private set; }

    /// <summary>Seconds of the run's steps a wall or an object stopped (the body pressed to or sliding along it). Report-only.</summary>
    public double ContactSeconds { get; private set; }

    /// <summary>How long without a contact before the next is a new touch, in seconds (a slide along a wall is one touch). Not retail: Lifestone's own.</summary>
    public const double ContactGapSeconds = 0.5;

    /// <summary>How many touches a run logs. Not retail: Lifestone's own.</summary>
    public const int ContactLogLimit = 12;

    /// <summary>Movement key changes a minute over the run (<see cref="KeyChanges"/> over <see cref="RunSeconds"/>); 0 before the run has a second. Report-only.</summary>
    public double KeyChangesPerMinute => RunSeconds >= 1.0 ? KeyChanges * 60.0 / RunSeconds : 0.0;

    /// <summary>Paths the confirmer refused.</summary>
    public int ConfirmFailures => _confirmFailures;

    /// <summary>The last refused confirmation, or null.</summary>
    public RouteConfirmFailure? LastConfirmFailure { get; private set; }

    /// <summary>M6 7 N: the waits for tiles a failed plan began (<see cref="WaitForTiles"/>; report-only).</summary>
    public int TileWaits { get; private set; }

    /// <summary>True once the run has ended (any state but Idle, Running, Waiting and Yielding).</summary>
    public bool IsDone => State is not (NavRunState.Idle or NavRunState.Running or NavRunState.Waiting or NavRunState.Yielding);

    /// <summary>Starts a run to <paramref name="goal"/> at <paramref name="now"/>; the first <see cref="Step"/> plans.</summary>
    public void Start(NavGoal goal, double now)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goal = goal;
        _path = null;
        _route = null;
        HandedToUse = false;
        HandOverRefusal = null;
        _handOverCheckedAt = new Vector2(float.NaN, float.NaN);
        _confirming = false;
        _confirmFailures = 0;
        _refusalsInARow = 0;
        _startTime = now;
        _lastTime = now;
        _endTime = double.NaN;
        _started = false;
        _doorwayWalk = false;
        _ladderActive = false;
        _ladderLevel = 0;
        EpisodeRungs = 0;
        _rung = Rung.None;
        Creatures.Clear();
        _planned.Clear();
        _npcs.Clear();
        Scars.ClearBodies();
        KeepOffNearestMetres = float.PositiveInfinity;
        OwnNearestMetres = float.PositiveInfinity;
        _keepOffNearest = null;
        _keepOffInsideLogged = false;
        PlateNearestMetres = float.PositiveInfinity;
        PlateNearestWhere = null;
        _plateInsideLogged = false;
        _npcLaneUntil = double.NegativeInfinity;
        _npcLaneWaited = false;
        _npcIndoorLogged = false;
        _tilesWaitSince = null;
        ResetDoorStep();
        _doorSteps = [];
        _lockAsked.Clear();
        _plannedJumps.Clear(); // M6 14 pits
        _jumpRun = null;
        _curveHeld = false;
        _reachHeld = false;
        _tracking = false;
        _nextCreatureRefresh = now;
        _lastCreatureReplan = double.NegativeInfinity;
        FailureReason = null;
        State = NavRunState.Running;
        _zoneLogged = false;
        _log.Info(LogCategory, "run started", new LogField("name", Options.Name), new LogField("goal", Describe(goal)));
        ResolveKeepOff();
        OwnPortalCheck(now);
    }

    /// <summary>M6 14: the list this run keeps off (<see cref="KeepOff"/> with the own portal's reach set from the goal's stop, <see cref="PortalKeepOff.WithOwnClearance"/>), read at the start and at each plan.</summary>
    private void ResolveKeepOff()
    {
        if (KeepOff is not { } keep || _goal is not { } goal)
        {
            _keep = KeepOff;
            _keepFrom = null;
            return;
        }

        var reach = PortalKeepOff.OwnReach(StopMetres(Options.Stop ? goal.StopTolerance : goal.PassTolerance));
        if (!ReferenceEquals(keep, _keepFrom) || _keep is null || MathF.Abs(_keep.OwnClearance - reach) > 1e-4f)
        {
            _keepFrom = keep;
            _keep = keep.WithOwnClearance(reach); // the same list object while nothing changes: the room graph keeps its detours
        }
    }

    private PortalKeepOff? _keepFrom;

    /// <summary>
    /// M6 14 (portal overshoot), the check before a walk to a goal beside its own portal (<see cref="KeptPortal.Own"/>): one line with the reach and its parts; a goal inside the reach (where her stop would carry her into the portal's touch)
    /// ends the run Blocked by name (<c>no path: portal-keep-off:&lt;name&gt;: the goal ...</c>), never a walk into it. The plan's steps keep the same reach (<see cref="Plan"/>).
    /// </summary>
    private void OwnPortalCheck(double now)
    {
        if (_keep?.Own is not { } own || _goal is not { } goal)
        {
            return;
        }

        var tolerance = Options.Stop ? goal.StopTolerance : goal.PassTolerance;
        var metres = Vector2.Distance(goal.Point.Global, own.Global);
        _log.Info(
            LogCategory,
            "the walk's goal stands beside its own portal: kept off by the touch and her stop",
            new LogField("name", Options.Name),
            new LogField("portal", own.Name),
            new LogField("goal_m", metres),
            new LogField("reach_m", _keep.OwnClearance),
            new LogField("touch_m", PortalKeepOff.TouchMetres),
            new LogField("stop_m", StopMetres(tolerance)),
            new LogField("tolerance_m", tolerance),
            new LogField("walk_mps", MotionInterpreter.WalkAnimSpeed));
        if (!goal.IsObject && metres < _keep.OwnClearance && MathF.Abs(goal.Point.Z - own.Z) <= PortalKeepOff.StoreyMetres)
        {
            End(NavRunState.Blocked, "no path: " + KeepOffName(_keep, own, string.Create(CultureInfo.InvariantCulture, $"the goal stands {metres:F2} m from the walk's own portal, inside its touch and her stop")), now);
        }
    }

    /// <summary>Pauses the run (a station's wait): every key released, the route kept.</summary>
    public void Pause()
    {
        if (State is NavRunState.Running or NavRunState.Yielding)
        {
            if (_jumpRun is { Phase: JumpPhase.Run or JumpPhase.Air } committed)
            {
                // M6 14 airborne and pit-hold: a caller should hold its pause until the landing check ran (JumpCommitted); one that does not is named, so the lost check, or a body left on the brink, is never silent.
                _log.Warn(
                    LogCategory,
                    committed.Phase == JumpPhase.Air
                        ? "jump: the run was paused with the jump in flight: its landing check does not run (the caller did not wait for JumpCommitted; m6-airborne)"
                        : "jump: the run was paused in the run for the brink with the jump key down: the body may stop on the brink, in the pit's cell (the caller did not wait for JumpCommitted; m6-pit-hold)",
                    new LogField("name", Options.Name),
                    new LogField("jump", committed.Jump.Name));
            }

            ReleaseAll();
            _rung = Rung.None;
            _jumpRun = null; // M6 14 pits: a paused jump is not resumed; the resumed run replans from where the body stands
            State = NavRunState.Waiting;
        }
    }

    /// <summary>Resumes a paused run; the next step replans when the body is off the route by <see cref="OffRouteMetres"/>.</summary>
    public void Resume(double now)
    {
        if (State == NavRunState.Waiting)
        {
            State = NavRunState.Running;
            _lastProgressTime = now;
            _started = false; // re-anchor: a station may have moved the player (a recall) without a Step in between; the off-route check replans
            _awaitingTiles = false;
            _tilesWaitSince = null;
        }
    }

    /// <summary>One frame: reads the body, sends the keys that change. Call after the physics step.</summary>
    /// <param name="now">The body's clock in seconds (<see cref="INavBody.Now"/>, or the host's monotonic clock).</param>
    public NavRunState Step(double now)
    {
        if (State is NavRunState.Idle || IsDone)
        {
            return State;
        }

        var position = _body.Position;
        var here = NavMap.FromCellFrame(position);
        var global = here.Global;
        if (!_started)
        {
            _started = true;
            _lastGlobal = global;
            _lastZ = here.Z;
            _lastTime = now;
        }

        var moved = Vector2.Distance(global, _lastGlobal);
        var jump = MathF.Sqrt((moved * moved) + ((here.Z - _lastZ) * (here.Z - _lastZ)));
        NoteKeepOff(global, here.Z, position);
        NotePlate(here, position);
        if (jump > TeleportMetres && State != NavRunState.Waiting) // a paused run's station may move the player (a recall); Resume replans
        {
            return End(NavRunState.Failed, string.Create(CultureInfo.InvariantCulture, $"the player was moved {jump:F1} m in one step (a teleport or a server correction)"), now);
        }

        var dt = now - _lastTime;
        var heading = (float)RetailFrames.GetHeading(position.Frame);
        if (dt > 0)
        {
            _frameSeconds = dt; // M6 14: the walk zone's frame (WalkZoneMetres)
            _speed += SpeedSmoothing * ((float)(moved / dt) - _speed);
            _turnHeldSteps = _turn != 0 ? _turnHeldSteps + 1 : 0;
            if (_turnHeldSteps >= 2 && !float.IsNaN(_lastHeading))
            {
                // The rate the held key turns the body (the first step after a press may not have turned yet).
                _turnRate += RateSmoothing * ((Math.Abs(Normalize(heading - _lastHeading)) / dt) - _turnRate);
            }
        }

        NotePredictMotion(now, global, heading); // the predictive steering's own measures, between the body's moves (NavRunnerPredictive)
        NoteContact(now, dt, position, heading);

        _lastHeading = heading;

        if (State is NavRunState.Running or NavRunState.Yielding)
        {
            RouteMetres += moved;
            if (_runKey == _move.RunByDefault)
            {
                WalkedMetres += moved; // the run key inverts the run-by-default option: down with it on, or up with it off, is a walk
            }
        }

        _lastGlobal = global;
        _lastZ = here.Z;
        _lastTime = now;

        if (CheckIntents(now) is { } ended)
        {
            return ended;
        }

        if (State == NavRunState.Waiting)
        {
            return State;
        }

        if (_body.ServerDriving)
        {
            if (State != NavRunState.Yielding)
            {
                ReleaseAll();
                _rung = Rung.None;
                State = NavRunState.Yielding;
                _yieldSince = now;
                _log.Info(LogCategory, "yielding to the server's MoveTo", new LogField("name", Options.Name));
            }
            else if (now - _yieldSince >= YieldLimitSeconds)
            {
                // The body's own rule (PlayerMovementController's TODO(m5-moveto-stuck), measured from the MoveTo's arrival, which is never later than the yield's start) ends a
                // stuck MoveTo first; this is the backstop for a body without it.
                YieldsStuck++;
                return End(NavRunState.Failed, string.Create(CultureInfo.InvariantCulture, $"the server's MoveTo held the player {now - _yieldSince:F1} s without ending (ACE's MoveTo timeout is {YieldLimitSeconds:F0} s; TODO(m5-moveto-stuck))"), now);
            }

            return State;
        }

        if (State == NavRunState.Yielding)
        {
            State = NavRunState.Running;
            _lastProgressTime = now;
            var end = _body.ServerMoveToEnd ?? "unknown";
            if (end == "stuck")
            {
                YieldsStuck++;
            }

            _log.Info(
                LogCategory,
                end == "stuck" ? "the server's MoveTo was ended as stuck (TODO(m5-moveto-stuck)): running on" : "the server's MoveTo ended: running on",
                new LogField("name", Options.Name),
                new LogField("end", end),
                new LogField("yielded_s", (now - _yieldSince).ToString("F1", CultureInfo.InvariantCulture)));
        }

        RunStep(now, position, here);
        return CheckIntents(now) ?? State;
    }

    /// <summary>
    /// The runner's verdict fields (report-only in their first gate): <c>route_m= run_s= speed_mps= plans= replans= rungs= blocked=
    /// corrections= where_max_m= turns= key_changes_per_min= creatures_max= creature_replans= creature_min_m=</c>; corrections and where_max_m from the audit,
    /// <c>none</c> without one (or without a where check); creature_min_m <c>none</c> before a creature was seen.
    /// </summary>
    public string VerdictFields()
    {
        var seconds = RunSeconds;
        var summary = _audit?.Summary;
        var corrections = summary is null ? "none" : summary.Corrections.ToString(CultureInfo.InvariantCulture);
        var where = summary?.WhereMaxMetres is { } w ? w.ToString("0.00", CultureInfo.InvariantCulture) : "none";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"route_m={RouteMetres:0.0} run_s={seconds:0.0} speed_mps={(seconds > 0 ? RouteMetres / seconds : 0):0.00} plans={Plans} replans={Replans} rungs={Rungs} blocked={(State == NavRunState.Blocked ? 1 : 0)} corrections={corrections} where_max_m={where} turns={Turns} key_changes_per_min={KeyChangesPerMinute:0} creatures_max={CreaturesMax} creature_replans={CreatureReplans} creature_min_m={(CreatureMinMetres is { } m ? m.ToString("0.0", CultureInfo.InvariantCulture) : "none")}")
            + (Doors is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" door_steps={_doorSteps.Count} door_uses={DoorUses} doors_opened={DoorsOpened} doors_found_open={DoorsFoundOpen}"))
            + (KeepOff is null ? string.Empty : float.IsInfinity(KeepOffNearestMetres) ? " keep_off_nearest_m=none" : string.Create(CultureInfo.InvariantCulture, $" keep_off_nearest_m={KeepOffNearestMetres:0.00}"))
            + (_keep?.Own is null || float.IsInfinity(OwnNearestMetres) ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" own_nearest_m={OwnNearestMetres:0.00}"))
            + (JumpsTaken == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" jumps={JumpsTaken} jumps_landed={JumpsLanded}"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _move.HumanKey -= OnHumanKey;
        ReleaseAll(); // every state: an Interrupted or Arrived (no Stop) run still holds what it pressed, and the controller's lists are stacks
    }

    private static string Describe(NavGoal goal) => goal.IsObject
        ? string.Create(CultureInfo.InvariantCulture, $"object 0x{goal.Guid:X8} within {goal.Range:F1} m of {goal.Point}{(goal.NeedSight ? " in sight" : string.Empty)}")
        : string.Create(CultureInfo.InvariantCulture, $"{goal.Point}{(goal.CellId is { } c ? $" in cell 0x{c:X8}" : string.Empty)}");

    private static double Normalize(double degrees)
    {
        degrees %= 360.0;
        return degrees > 180.0 ? degrees - 360.0 : degrees <= -180.0 ? degrees + 360.0 : degrees;
    }

    private void RunStep(double now, in CellFrame position, NavPoint here)
    {
        var goal = _goal!;

        // M6 14 pits: a jump drives the body from its run-up to its landing (NavRunner.Jumps.cs); nothing else steers meanwhile.
        if (StepJump(now, position, here))
        {
            return;
        }

        // Arrival comes first: anywhere along the route.
        if (CheckArrival(position, here, now))
        {
            return;
        }

        // The hub walk: a walk that ends by Using its target hands over to the server's walk-to-use once the straight line there is clear (NavRunner.UseHandOver.cs).
        if (TryHandOver(now, position, here))
        {
            return;
        }

        if (_rung != Rung.None)
        {
            StepRung(now, here.Global);
            return;
        }

        if (_npcLaneUntil > now)
        {
            // An NPC stands where the plan cannot go round (M6 4 W2): the body waits, keys released, then plans once more (npc-in-lane); it never pushes.
            ReleaseAll();
            _lastProgressTime = now;
            return;
        }

        if (_tilesWaitSince is { } waitingSince && _paths.Map is { } tiles && tiles.Version == _mapVersion && now - waitingSince < HorizonWaitSeconds)
        {
            // M6 7 N: a plan failed while the corridor's tiles were not attached: the body stands, keys released, until a tile changes the map or the bound (WaitForTiles).
            ReleaseAll();
            _lastProgressTime = now;
            return;
        }

        if (_route is null && !Plan(now, position, replan: _npcLaneWaited || _tilesWaitSince is not null, _npcLaneWaited ? "npc-in-lane" : _tilesWaitSince is not null ? "tiles" : "start"))
        {
            return;
        }

        if (_confirming && !StepConfirm(now, position))
        {
            return;
        }

        var route = _route!;
        if (route.DistanceFrom(here) > OffRouteMetres)
        {
            if (Plan(now, position, replan: true, "off-route") && (!_confirming || StepConfirm(now, position)))
            {
                route = _route!;
            }
            else
            {
                return;
            }
        }

        var s = Progress(route, here);
        var remaining = route.ArcLength - s;
        if (_path!.Status == NavPathStatus.Horizon && _paths.Map is { } map)
        {
            if (map.Version != _mapVersion && remaining <= HorizonReplanMetres)
            {
                _awaitingTiles = false;
                if (!Plan(now, position, replan: true, "horizon") || (_confirming && !StepConfirm(now, position)))
                {
                    return;
                }

                route = _route!;
                s = Progress(route, here);
                remaining = route.ArcLength - s;
            }
            else if (remaining < goal.StopTolerance + 0.5f)
            {
                if (!_awaitingTiles)
                {
                    _awaitingTiles = true;
                    _awaitingSince = now;
                    _log.Info(LogCategory, "at the horizon: waiting for the next tiles", new LogField("name", Options.Name));
                }

                if (now - _awaitingSince >= HorizonWaitSeconds)
                {
                    var end = route.End;
                    End(
                        NavRunState.Blocked,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"horizon:landblock 0x{end.LandblockId:X8} (goal 0x{goal.Point.LandblockId:X8}) not attached within {HorizonWaitSeconds:F0} s ({map.AttachedLandblocks.Count} attached)"),
                        now);
                    return;
                }

                ReleaseAll();
                _lastProgressTime = now;
                return;
            }
        }

        if (Doors is { } doorFeed && DoorSourcesChanged(doorFeed))
        {
            FindLateDoors(now, s);
            if (IsDone)
            {
                return;
            }
        }

        if (_doorSteps.Count > 0 && StepDoors(now, s))
        {
            return; // stopped at a closed door: the Use, the wait (the stuck ladder must not read the wait as a stall)
        }

        if (IsDone)
        {
            return;
        }

        var progress = remaining > WalkNearStopMetres ? s : route.ArcLength - here.HorizontalDistance(route.End);
        if (progress > _bestProgress + StuckProgressMetres)
        {
            _bestProgress = progress;
            _lastProgressTime = now;
            if (_ladderActive && progress > _ladderOrigin + LadderResetMetres)
            {
                _ladderActive = false;
                _ladderLevel = 0;
                EpisodeRungs = 0;
            }
        }
        else if ((_forward && _body.OnGround && now - Math.Max(_lastProgressTime, _forwardSince) >= StuckSeconds) || now - _lastProgressTime >= NoProgressSeconds)
        {
            ClimbRung(now, position, here, progress);
            return;
        }

        if (StepCreatures(now, position, here, route, s))
        {
            if (IsDone || _confirming || _route is null)
            {
                return;
            }

            route = _route;
            s = Progress(route, here);
        }

        _steerProgress = s;
        Steer(route, here, position, s);
    }

    /// <summary>
    /// The body's progress along <paramref name="route"/> in arc length (D134): forward only, the nearest point searched within <see cref="ProgressWindowMetres"/>
    /// ahead of the last progress and on the body's storey (<see cref="ArrivalHeightMetres"/>); a new route starts from its nearest point on the storey; a body
    /// farther than <see cref="ProgressReanchorMetres"/> beyond the route's nearest point anywhere is re-anchored there.
    /// </summary>
    private float Progress(NavRoute route, NavPoint here)
    {
        if (!ReferenceEquals(route, _progressRoute))
        {
            _progressRoute = route;
            _progressArc = route.NearestInWindow(here, 0f, 0f, route.ArcLength, ArrivalHeightMetres).Arc;
            return _progressArc;
        }

        var (arc, distance) = route.NearestInWindow(here, _progressArc, ProgressBehindMetres, ProgressWindowMetres, ArrivalHeightMetres);
        if (distance > route.DistanceFrom(here) + ProgressReanchorMetres)
        {
            arc = route.NearestInWindow(here, 0f, 0f, route.ArcLength, ArrivalHeightMetres).Arc;
            _progressArc = arc;
            return arc;
        }

        _progressArc = MathF.Max(_progressArc, arc);
        return _progressArc;
    }

    /// <summary>
    /// Refreshes the creature layer when it is due and replans when a creature the plan did not know there meets the route ahead (nav-creature-avoid);
    /// true when it replanned.
    /// </summary>
    private bool StepCreatures(double now, in CellFrame position, NavPoint here, NavRoute route, float s)
    {
        if (_creatureFeed is null || now < _nextCreatureRefresh)
        {
            return false;
        }

        _nextCreatureRefresh = now + CreatureRefreshSeconds;
        var me = here.Global;
        Creatures.Refresh(_creatureFeed, me, CreatureLookMetres, _goal!.Guid ?? 0);
        CreaturesMax = Math.Max(CreaturesMax, Creatures.Count);
        if (Creatures.Nearest(me, out _, out var nearest))
        {
            CreatureMinMetres = Math.Min(CreatureMinMetres ?? float.MaxValue, nearest);
        }

        if (now - _lastCreatureReplan < CreatureReplanSeconds || (_paths.Planner is null && _rooms is null))
        {
            return false;
        }

        var all = Creatures.All;
        for (var i = 0; i < all.Count; i++) // by index: a foreach over the interface boxes its enumerator
        {
            var c = all[i];
            if (Vector2.Distance(me, c.Centre) <= c.Radius || PlannedNear(c) || !RouteMeets(route, s, c))
            {
                continue; // it has noticed the body already, or the plan knew it there, or it is off the route ahead
            }

            _lastCreatureReplan = now;
            CreatureReplans++;
            _log.Info(
                LogCategory,
                "a creature on the route ahead: replan",
                new LogField("name", Options.Name),
                new LogField("creature", "0x" + c.Guid.ToString("X8", CultureInfo.InvariantCulture)),
                new LogField("metres", Vector2.Distance(me, c.Centre)),
                new LogField("radius", c.Radius));
            Plan(now, position, replan: true, "creature");
            return true;
        }

        return false;
    }

    /// <summary>Whether the last plan knew <paramref name="creature"/> within <see cref="CreatureMovedMetres"/> of where it is now.</summary>
    private bool PlannedNear(in NavCreature creature)
    {
        foreach (var p in _planned)
        {
            if (p.Guid == creature.Guid && Vector2.Distance(p.Centre, creature.Centre) <= CreatureMovedMetres)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="creature"/>'s disc meets the route from arc length <paramref name="s"/> over the next <see cref="CreatureLookAheadMetres"/>. A segment between two indoor waypoints (a room route's) is met only by a creature
    /// within <see cref="NavCreatures.StoreyBandMetres"/> of the segment's heights (<see cref="OnSegmentStorey"/>), so a creature on the storey above or below never forces a replan (M5 2 fix F2, review 2-R6); any other segment is met by the flat disc, as before.
    /// </summary>
    private bool RouteMeets(NavRoute route, float s, in NavCreature creature)
    {
        var end = s + CreatureLookAheadMetres;
        var waypoints = _path!.Waypoints;
        var from = route.PointAt(s);
        for (var i = 1; i < route.Vertices.Count; i++)
        {
            if (route.ArcLengthAt(i) <= s)
            {
                continue;
            }

            var to = route.ArcLengthAt(i) > end ? route.PointAt(end) : route.Vertices[i];
            var indoor = i < waypoints.Count && waypoints[i].Indoor && waypoints[i - 1].Indoor;
            if (SegmentDistance(creature.Centre, from.Global, to.Global) <= creature.Radius
                && (!indoor || OnSegmentStorey(creature.Z, from.Z, to.Z)))
            {
                return true;
            }

            if (route.ArcLengthAt(i) > end)
            {
                break;
            }

            from = to;
        }

        return false;
    }

    /// <summary>Whether a creature at height <paramref name="creatureZ"/> is within <see cref="NavCreatures.StoreyBandMetres"/> of the heights <paramref name="za"/> to <paramref name="zb"/> (a ramp's segment spans both).</summary>
    private static bool OnSegmentStorey(float creatureZ, float za, float zb) =>
        float.IsNaN(creatureZ) || (creatureZ >= Math.Min(za, zb) - NavCreatures.StoreyBandMetres && creatureZ <= Math.Max(za, zb) + NavCreatures.StoreyBandMetres);

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        var t = len2 <= 0f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + (ab * t));
    }

    private void ResetDoorStep()
    {
        _doorIndex = 0;
        ResetDoorState();
    }

    private void ResetDoorState()
    {
        _doorOpening = false;
        _doorSeenOpen = false;
        _doorStalledUse = false;
        _doorUses = 0;
        _doorReopens = 0;
        _doorHandle = null;
    }

    /// <summary>
    /// Marks the doors the new path crosses (M5 item 5, d1): each closed door in a doorway the route passes through is a door step the run opens on its way (<see cref="StepDoors"/>); a
    /// door the server says is locked is blocked outright, named <c>door-locked</c> (the planner does not route round it: <c>TODO(door-locked-replan)</c>). False when it ended the run.
    /// </summary>
    private bool PlanDoors(NavPath path, double now)
    {
        _doorSteps = [];
        _curveHeld = false;
        _reachHeld = false;
        ResetDoorStep();
        if (Doors is not { } feed)
        {
            return true;
        }

        NoteDoorSources(feed);
        feed.Fill(_doorBuffer);
        if (_doorBuffer.Count == 0)
        {
            return true;
        }

        _doorSteps = DoorCrossings.StepsOf(path, feed.Crossings(_doorBuffer, _paths), Options.DoorStandOffMetres);
        foreach (var step in _doorSteps)
        {
            if (!NoteDoorStep(step, "the route crosses a door", now))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Ends the run <c>door-locked</c> by name (M6 8 C, D117): one log line with the door's guid, name, cell and where the lock was read from, and a failure text that names them.</summary>
    private void EndLocked(DoorStep step, NavDoor door, string where, double now)
    {
        var guid = "0x" + door.Guid.ToString("X8", CultureInfo.InvariantCulture);
        var cell = "0x" + step.Crossing.CellId.ToString("X8", CultureInfo.InvariantCulture);
        var from = door.LockedFrom ?? "unknown";
        _log.Warn(LogCategory, "the closed door is locked: the walk stops", new LogField("name", Options.Name), new LogField("door", guid), new LogField("door_name", door.Name ?? "?"), new LogField("cell", cell), new LogField("locked_from", from));
        End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"door-locked: {where} the closed door {guid} ({door.Name ?? "?"}, cell {cell}) and the server says it is locked (read from {from}); no way round it is planned (TODO(door-locked-replan), M7)"), now);
    }

    /// <summary>Logs a door step and ends the run <c>door-locked</c> for a closed door the server says is locked; false when it ended the run.</summary>
    private bool NoteDoorStep(DoorStep step, string message, double now)
    {
        var door = step.Crossing.Door;
        _log.Info(
            LogCategory,
            message,
            new LogField("name", Options.Name),
            new LogField("door", "0x" + door.Guid.ToString("X8", CultureInfo.InvariantCulture)),
            new LogField("open", door.Open),
            new LogField("locked", door.Locked is { } l ? l.ToString() : "unknown"),
            new LogField("cell", "0x" + step.Crossing.CellId.ToString("X8", CultureInfo.InvariantCulture)),
            new LogField("cross_m", step.CrossArc),
            new LogField("stop_m", step.StopArc),
            new LogField("stop_straight_m", step.StopStraightMetres),
            new LogField("stop_rule", step.StopRule));
        if (!door.Open && door.Locked == true)
        {
            EndLocked(step, door, "the route crosses", now);
            return false;
        }

        if (!door.Open && !door.Unusable && door.Locked is null && _lockAsked.Add(door.Guid))
        {
            // Check before the act (M6 8 C, D117): the lock is read before the body walks up; the answer is the session's appraisal, and StepDoors stops the walk when it says locked.
            _log.Info(LogCategory, "the lock of the door is unknown: asking the server (appraisal)", new LogField("name", Options.Name), new LogField("door", "0x" + door.Guid.ToString("X8", CultureInfo.InvariantCulture)), new LogField("door_name", door.Name ?? "?"));
            Doors!.ReadLock(door.Guid);
        }

        if (!door.Open && door.Unusable)
        {
            End(NavRunState.Blocked, DoorLeverReason(door.Guid, "the route crosses"), now);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The named stop before a shut door the player cannot Use (<see cref="NavDoor.Unusable"/>): it opens only by a linked lever or switch, which the walk does not work
    /// (<c>TODO(door-lever)</c>: Fort Tethana's gate door 0x72581042 and its lever 0x72581044, landblock_instance_link row 298488 of the pinned world SQL v0.9.296).
    /// </summary>
    private static string DoorLeverReason(uint guid, string where) => string.Create(
        CultureInfo.InvariantCulture,
        $"door-lever: {where} the closed door 0x{guid:X8}, which cannot be used (its Usable is No: \"cannot be activated from here\"); it opens only by a linked lever or switch, TODO(door-lever)");

    private void NoteDoorSources(INavDoorFeed feed)
    {
        _doorFeedVersion = feed.Version;
        _doorMapVersion = _paths.Map?.Version ?? 0;
        _doorRooms = (_paths as INavRoomSource)?.Rooms;
    }

    /// <summary>True when the doors the runner knows, or the doorways it can place them in, changed since the doors of the path were last looked for. Allocates nothing.</summary>
    private bool DoorSourcesChanged(INavDoorFeed feed) =>
        feed.Version != _doorFeedVersion || (_paths.Map?.Version ?? 0) != _doorMapVersion || !ReferenceEquals((_paths as INavRoomSource)?.Rooms, _doorRooms);

    /// <summary>
    /// Looks for doors on the rest of the path that the plan did not know (M5 7 F2: a door still outside the object table, or a doorway still unloaded, when the path was planned, so the body walked to
    /// it and pressed on the shut door). Runs when the door feed's doors or the path source's doorways changed, and at a stuck rung. A closed door found ahead becomes a door step in route order, logged
    /// "the route crosses a door (found after the plan)"; the stand-off and the Use are <see cref="StepDoors"/>'s as for any door. True when it added a step or ended the run. The planner's route is not changed.
    /// </summary>
    private bool FindLateDoors(double now, float s)
    {
        if (Doors is not { } feed || _path is null)
        {
            return false;
        }

        NoteDoorSources(feed);
        feed.Fill(_doorBuffer);
        if (_doorBuffer.Count == 0)
        {
            return false;
        }

        var added = false;
        foreach (var step in DoorCrossings.StepsOf(_path, feed.Crossings(_doorBuffer, _paths), Options.DoorStandOffMetres))
        {
            if (s > step.CrossArc + DoorClearMetres)
            {
                continue; // behind the body
            }

            var known = false;
            var at = _doorSteps.Count;
            for (var i = _doorIndex; i < _doorSteps.Count; i++)
            {
                if (_doorSteps[i].Guid == step.Guid)
                {
                    known = true;
                    break;
                }

                if (at == _doorSteps.Count && _doorSteps[i].CrossArc > step.CrossArc)
                {
                    at = i;
                }
            }

            if (known)
            {
                continue;
            }

            _doorSteps.Insert(at, step);
            if (at == _doorIndex)
            {
                ResetDoorState(); // the state belonged to the step this one now comes before
            }

            added = true;
            if (!NoteDoorStep(step, "the route crosses a door (found after the plan)", now))
            {
                return true;
            }
        }

        return added;
    }

    /// <summary>
    /// The door step in front of the body (M5 item 5, d1). An open door is walked through (no Use). A closed one: the body walks to <see cref="DoorStep.StopArc"/>, stops, logs
    /// <c>purpose=door</c> (D47), Uses it and waits (keys released) for its Ethereal state; one more Use after <see cref="NavRunnerOptions.DoorRetrySeconds"/> (a door still animating ignores a
    /// Use: <c>Door.cs:90</c> @95ef426d); no open within <see cref="NavRunnerOptions.DoorOpenSeconds"/> ends the run Blocked <c>door-closed-timeout</c>. A door seen open that closes again
    /// before the body is through is Used once more, then the same failure. True when the step consumed this frame (the body stands).
    /// </summary>
    private bool StepDoors(double now, float s)
    {
        var feed = Doors!;
        while (_doorIndex < _doorSteps.Count && s > _doorSteps[_doorIndex].CrossArc + DoorClearMetres)
        {
            _doorIndex++;
            ResetDoorState();
        }

        if (_doorIndex >= _doorSteps.Count)
        {
            return false;
        }

        var step = _doorSteps[_doorIndex];
        if (!feed.TryGet(step.Guid, out var door))
        {
            _log.Warn(LogCategory, "the door left the object table: walking on", new LogField("name", Options.Name), new LogField("door", "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture)));
            _doorIndex++;
            ResetDoorState();
            return false;
        }

        if (_doorOpening)
        {
            ReleaseAll();
            _lastProgressTime = now;
            if (door.Open)
            {
                _doorOpening = false;
                _doorSeenOpen = true;
                DoorsOpened++;
                _log.Info(LogCategory, "the door opened: walking on", new LogField("name", Options.Name), new LogField("purpose", "door"), new LogField("door", "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture)), new LogField("waited_s", now - _doorFirstUse));
                return false;
            }

            if (_doorHandle is { IsCompleted: true, Outcome: { Kind: ActionOutcomeKind.Refused or ActionOutcomeKind.DryRun or ActionOutcomeKind.Cancelled or ActionOutcomeKind.NotImplemented } refused })
            {
                End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"door-use-refused: the Use of door 0x{step.Guid:X8} was {refused.Kind}: {refused.Detail}"), now);
                return true;
            }

            if (door.Locked == true)
            {
                // The Use was refused (sound 0x94, read by the feed): known locked at the first Use, no second Use and no open timeout.
                EndLocked(step, door, "stopped at", now);
                return true;
            }

            if (_doorUses < 2 && now - _doorUsedAt >= Options.DoorRetrySeconds)
            {
                SendDoorUse(now, step, retry: true);
                return true;
            }

            if (now - _doorFirstUse >= Options.DoorOpenSeconds)
            {
                End(
                    NavRunState.Blocked,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"door-closed-timeout: door 0x{step.Guid:X8} did not open within {Options.DoorOpenSeconds:F1} s of {_doorUses} Use(s) (locked: {(door.Locked is { } l ? l.ToString() : "unknown, TODO(door-locked-unknown)")})"),
                    now);
            }

            return true;
        }

        if (door.Open)
        {
            if (!_doorSeenOpen)
            {
                _doorSeenOpen = true;
                if (_doorUses == 0)
                {
                    DoorsFoundOpen++;
                    _log.Info(LogCategory, "the door is open: walking through", new LogField("name", Options.Name), new LogField("door", "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture)));
                }
            }

            return false;
        }

        if (door.Locked == true)
        {
            // Known locked, from the appraisal asked at the plan or the server's word since: stop here by name, not at the door (M6 8 C).
            EndLocked(step, door, "the route crosses", now);
            return true;
        }

        if (_doorSeenOpen)
        {
            // It was open and has closed before the body is through: one more Use, then the named failure.
            if (_doorReopens >= 1)
            {
                End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"door-closed-timeout: door 0x{step.Guid:X8} closed again before the body was through, twice"), now);
                return true;
            }

            _doorReopens++;
            _doorSeenOpen = false;
            _doorUses = 0;
            _log.Warn(LogCategory, "the door closed again before the body was through", new LogField("name", Options.Name), new LogField("door", "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture)));
        }

        if (s < step.StopArc && !_doorStalledUse)
        {
            return false; // still walking to the stop
        }

        if (door.Unusable)
        {
            // Check before the act: a Use the player cannot make is never sent (ACE would open the door on it, Door.cs:86-101 @95ef426d, which retail's player cannot do).
            End(NavRunState.Blocked, DoorLeverReason(step.Guid, "stopped at"), now);
            return true;
        }

        ReleaseAll();
        _lastProgressTime = now;
        _doorOpening = true;
        _doorFirstUse = now;
        SendDoorUse(now, step, retry: false);
        return true;
    }

    /// <summary>
    /// The owner's rule D141 (check, act, check) at a stall (M5 7 P7l): when the body has stalled before the door step in front of it reached its stop, the door is shut (and not said to be locked) and the body stands
    /// within <see cref="NavRunnerOptions.DoorUseReachMetres"/> of where the route crosses its doorway, the step Uses the door from here: <see cref="StepDoors"/> sends the Use, waits for the door's Ethereal state and
    /// walks on, or ends the run <c>door-closed-timeout</c> by name when it stays shut. Each decision is logged. True when the step was armed (the stall is the door's, not the ladder's).
    /// </summary>
    private bool UseDoorWhenStalled(double now, in CellFrame position, NavPoint here)
    {
        if (_doorIndex >= _doorSteps.Count || _doorOpening || _doorStalledUse || _route is null)
        {
            return false;
        }

        var step = _doorSteps[_doorIndex];
        if (!Doors!.TryGet(step.Guid, out var door) || door.Open)
        {
            return false;
        }

        var cross = _route.PointAt(step.CrossArc);
        var reach = Vector2.Distance(here.Global, cross.Global);
        var rise = MathF.Abs(here.Z - cross.Z); // a doorway a storey above or below is not in reach
        var guid = "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture);
        if (reach > Options.DoorUseReachMetres || rise > Options.DoorUseReachMetres || door.Locked == true)
        {
            _log.Info(
                LogCategory,
                door.Locked == true ? "stalled before a shut door the server says is locked: the ladder climbs (D141)" : "stalled before a shut door out of use reach: the ladder climbs (D141)",
                new LogField("name", Options.Name),
                new LogField("door", guid),
                new LogField("reach_m", reach),
                new LogField("rise_m", rise),
                new LogField("use_reach_m", Options.DoorUseReachMetres),
                new LogField("at", position.ToString()));
            return false;
        }

        _doorStalledUse = true;
        StalledDoorUses++;
        _log.Info(
            LogCategory,
            "stalled within use reach of a shut door short of its stop: using it from here (D141)",
            new LogField("name", Options.Name),
            new LogField("purpose", "door"),
            new LogField("door", guid),
            new LogField("reach_m", reach),
            new LogField("use_reach_m", Options.DoorUseReachMetres),
            new LogField("progress_m", _route.NearestArcLength(here)),
            new LogField("stop_m", step.StopArc),
            new LogField("cross_m", step.CrossArc),
            new LogField("at", position.ToString()));
        return true;
    }

    private void SendDoorUse(double now, DoorStep step, bool retry)
    {
        _doorHandle = Doors!.Use(step.Guid);
        _doorUses++;
        DoorUses++;
        _doorUsedAt = now;
        _log.Info(
            LogCategory,
            retry ? "the door is still shut: using it again" : "stopped at a closed door: using it",
            new LogField("name", Options.Name),
            new LogField("purpose", "door"),
            new LogField("door", "0x" + step.Guid.ToString("X8", CultureInfo.InvariantCulture)),
            new LogField("uses", _doorUses));
    }

    /// <summary>The id of a closed door whose doorway the line from <paramref name="from"/> to <paramref name="to"/> passes through, or null (also null for a run with no door feed).</summary>
    private uint? ClosedDoorBetween(in CellFrame from, in CellFrame to)
    {
        if (Doors is not { } feed)
        {
            return null;
        }

        feed.Fill(_doorBuffer);
        _doorBuffer.RemoveAll(d => d.Open);
        if (_doorBuffer.Count == 0)
        {
            return null;
        }

        var a = NavMap.FromCellFrame(from);
        var b = NavMap.FromCellFrame(to);
        foreach (var crossing in feed.Crossings(_doorBuffer, _paths))
        {
            if (DoorCrossings.Crosses(crossing, a.Global, a.Z, b.Global, b.Z, checkHeight: false, out _))
            {
                return crossing.Door.Guid;
            }
        }

        return null;
    }

    private bool CheckArrival(in CellFrame position, NavPoint here, double now)
    {
        var goal = _goal!;
        var distance = here.HorizontalDistance(goal.Point);
        if (goal.IsObject)
        {
            // An object goal that names an indoor cell is reached in that room (M4 P5b F1): the range alone is met through a wall from the street.
            if (distance > goal.Range || (goal.CellId is { } room && !LandDefs.IsOutdoorCell(room) && position.CellId != room))
            {
                return false;
            }

            ReleaseAll();
            if (!goal.NeedSight)
            {
                End(NavRunState.Arrived, null, now);
                return true;
            }

            if (_confirmer is null)
            {
                End(NavRunState.ArrivedWithoutSight, "TODO(nav-sight-no-physics): no physics world to test the sight line (no RouteConfirmer given)", now);
                return true;
            }

            var target = goal.CellId is { } cell
                ? new CellFrame(cell, new AcFrame(goal.Point.Local, Quaternion.Identity))
                : NavMap.ToCellFrame(goal.Point);
            if (_confirmer.InSight(position, target, out var tries))
            {
                if (ClosedDoorBetween(position, target) is { } shut)
                {
                    // nav-sight-no-physics, the door half (M5 item 5): the sweep has no physics object for a closed door, so the line the physics called clear is blocked by it.
                    End(NavRunState.ArrivedWithoutSight, string.Create(CultureInfo.InvariantCulture, $"within {distance:F1} m of 0x{goal.Guid:X8} but the closed door 0x{shut:X8} stands between"), now);
                }
                else
                {
                    End(NavRunState.Arrived, null, now);
                }
            }
            else
            {
                End(NavRunState.ArrivedWithoutSight, string.Create(CultureInfo.InvariantCulture, $"within {distance:F1} m of 0x{goal.Guid:X8} but every sight line ({tries}) is blocked"), now);
            }

            return true;
        }

        var tolerance = Options.Stop ? goal.StopTolerance : goal.PassTolerance;
        if (distance > tolerance || (goal.CellId is { } indoor && !LandDefs.IsOutdoorCell(indoor) && MathF.Abs(here.Z - goal.Point.Z) > ArrivalHeightMetres))
        {
            return false;
        }

        if (Options.Stop)
        {
            ReleaseAll();
        }

        End(NavRunState.Arrived, null, now);
        return true;
    }

    private bool Plan(double now, in CellFrame position, bool replan, string why)
    {
        var rooms = (_paths as INavRoomSource)?.Rooms;
        if (_creatureFeed is not null && (_paths.Planner is not null || rooms is not null))
        {
            Creatures.Refresh(_creatureFeed, NavMap.FromCellFrame(position).Global, CreatureLookMetres, _goal!.Guid ?? 0);
            _nextCreatureRefresh = now + CreatureRefreshSeconds;
        }

        // A planner or a room graph a source shares between runs takes this run's layer (none for a run without a feed: the plans are today's).
        if (_paths.Planner is { } planner)
        {
            planner.Creatures = _creatureFeed is null ? null : Creatures;
        }

        if (rooms is not null)
        {
            rooms.Creatures = _creatureFeed is null ? null : Creatures; // nav-creatures-indoor
        }
        else if (_paths.Planner is null && _creatureFeed is not null && !_noLayerLogged)
        {
            _noLayerLogged = true;
            _log.Warn(LogCategory, "the path source has neither a lattice nor a room graph, so this run plans without its creature layer", new LogField("name", Options.Name));
        }

        ResolveKeepOff();
        if (rooms is not null)
        {
            rooms.KeepOff = _keep;
            rooms.JumpReachMetres = _body.JumpReachMetres; // M6 14 pits: a jump the body cannot make is no edge of this run's plans (as the creature layer, the run's own)
            rooms.PlateSpeedMetresPerSecond = _body.RunSpeedMetresPerSecond; // M6 14 holding plates: the plates' walls take her stop from her live speed
            if (_plateWatch is { } watch && now <= watch.Until && JumpWitness?.Fired() is { } fired)
            {
                _plateWatch = null;
                PlateFired(watch.Jump, fired); // M6 14 holding plates: a plate's spell after the last jump, seen at this plan: that jump is out of this one
            }
        }

        Scars.SetKeepOff(_keep?.OutdoorDiscs ?? []);
        _planned.Clear();
        _planned.AddRange(Creatures.All);
        var path = PlanPath(position, out var inLane);
        Plans++;
        if (replan)
        {
            Replans++;
        }

        _audit?.RecordPlan(replan);
        _mapVersion = _paths.Map?.Version ?? 0;
        if (KeepOffFailure(path, position, rooms) is { } kept)
        {
            End(NavRunState.Blocked, "no path: " + kept, now);
            return false;
        }

        if (path.Status != NavPathStatus.Failed && rooms?.Plates is { } walls && walls.FirstMeeting(path.Waypoints, rooms.LandblockId) is { } crossed)
        {
            // M6 14 holding plates: a guard, never a silent brush: the room graph's plans keep out of every wall, so a line that meets one is a planner's fault, named
            End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"no path: {PlateKeepOffFailure}:{crossed.Plate.Name.Replace(' ', '_')}: leg {crossed.Leg} of the plan's {path.Waypoints.Count} waypoints passes within its wall ({walls.ReachOf(crossed.Plate):F2} m)"), now);
            return false;
        }

        if (path.Status == NavPathStatus.Failed && rooms?.Plates is { } closed)
        {
            _log.Info(LogCategory, "no plan with the plates' walls in force", new LogField("name", Options.Name), new LogField("reason", path.Reason ?? "no plan"), new LogField("plates", closed.Plates.Count), new LogField("stop_m", closed.StopMetres), new LogField("plate_jumps", rooms.AllPlateJumps().Count), new LogField("refused_jumps", rooms.RefusedPlateJumps.Count));
        }

        if (path.Status == NavPathStatus.Failed || path.Waypoints.Count == 0)
        {
            if (WaitForTiles(path, position, now, out var gaveUp))
            {
                return false;
            }

            // An empty path is a named failure, never an exception (m5-nav-one-waypoint: the sibling edge of the one-waypoint route).
            End(NavRunState.Blocked, "no path: " + (path.Reason ?? "empty-path (the path source returned no waypoints)") + (path.WorldRouteNote is { } note ? " (world route: " + note + ")" : string.Empty) + gaveUp, now);
            return false;
        }

        if (_tilesWaitSince is { } waited)
        {
            _log.Info(LogCategory, "check after: the plan found a way once the tiles came", new LogField("name", Options.Name), new LogField("why", why), new LogField("status", path.Status.ToString()), new LogField("waited_s", (now - waited).ToString("F1", CultureInfo.InvariantCulture)), new LogField("attached", _paths.Map?.AttachedLandblocks.Count ?? 0));
            _tilesWaitSince = null;
        }

        if (inLane is { } body)
        {
            return WaitForBody(now, body);
        }

        _npcLaneWaited = false;
        _path = path;
        _route = path.ToRoute();
        NoteJumps(path); // M6 14 pits
        _rooms = (_paths as INavRoomSource)?.Rooms; // read again after the plan (M6 4 W2, W1's note 1): a two-landblock source's Rooms follows the plan's start, so a plan from outdoors runs straight in the goal's rooms
        SweepRooms(path);
        if (!PlanDoors(path, now))
        {
            return false;
        }

        _bestProgress = float.MinValue;
        _lastProgressTime = now;
        _awaitingTiles = false;
        if (_ladderActive)
        {
            _ladderOrigin = 0f;
        }

        _log.Info(
            LogCategory,
            replan ? "replanned" : "planned",
            new LogField("name", Options.Name),
            new LogField("why", why),
            new LogField("status", path.Status.ToString()),
            new LogField("metres", _route.ArcLength),
            new LogField("waypoints", path.Waypoints.Count));
        if (_keep is { } keep && keep.NearestOnLine(path.Waypoints) is { } line)
        {
            _log.Info(LogCategory, "the plan keeps clear of the portals", new LogField("name", Options.Name), new LogField("portals", keep.Portals.Count), new LogField("clearance_m", keep.Clearance), new LogField("nearest_m", line.Metres), new LogField("nearest", line.Portal.Name));
            if (keep.Own is { } own && keep.NearestOnLine(path.Waypoints, own) is { } ownLine)
            {
                _log.Info(LogCategory, "the plan keeps clear of its own portal", new LogField("name", Options.Name), new LogField("portal", own.Name), new LogField("reach_m", keep.OwnClearance), new LogField("nearest_m", ownLine));
            }
        }

        if (_confirmer is not null)
        {
            var goal = _goal!;
            // The goal's arrival tolerance (P5a S5), or an object goal's range (M4 P5b F1): the body stops within it, so the end need not be standable.
            var stopShort = path.Status == NavPathStatus.Horizon ? 0f
                : goal.IsObject ? RouteConfirmer.ArcBeyondRange(path.Waypoints, goal.Point, goal.Range, goal.CellId)
                : Options.Stop ? goal.StopTolerance : goal.PassTolerance;
            _confirmer.Begin(path.Waypoints, stopShort);
            _confirming = true;
        }

        return true;
    }

    /// <summary>
    /// M6 7 N (the live round 1c: <c>no path: no-path</c> 168 ms after a hop's arrival): a plan that fails <c>no-path</c> or <c>no-horizon</c> while a landblock on the straight line from the body to the goal has no tile says nothing yet: the streamer keeps a dropped corridor
    /// landblock, so the goal's tile can stand attached and alone (the Burrow's 0xF981 beside the ring round the Refuge's 0xF682: no horizon is planned, the planner searches for a goal it cannot reach). Check before: the reason and the missing tiles, one line. Act: every key released,
    /// the route dropped, the body stands (the host's corridor feed goes on naming the landblocks ahead, in both hosts, <see cref="HeadlessStreamer.FollowRoute"/>). Check after: each change of the map plans again (<c>why=tiles</c>), a plan that finds a way logs it; every tile on the line
    /// attached and still no way is a genuine no-path, at once; the tiles not there within <see cref="HorizonWaitSeconds"/> is the bound, the failure then names them in <paramref name="gaveUp"/>. Any other reason (<c>start-not-attached</c>, <c>no-tile:</c>, <c>goal-blocked</c>, a source
    /// with no lattice) is not this wait's: true only while the body waits. Not retail: Lifestone's own.
    /// </summary>
    private bool WaitForTiles(NavPath path, in CellFrame position, double now, out string gaveUp)
    {
        gaveUp = string.Empty;
        if (path.Reason is not ("no-path" or "no-horizon") || _paths.Map is not { } map)
        {
            return false;
        }

        var missing = MissingTiles(map, NavMap.FromCellFrame(position).Global, _goal!.Point.Global);
        var names = string.Join(' ', missing.Select(id => "0x" + (id >> 16).ToString("X4", CultureInfo.InvariantCulture)));
        if (missing.Count == 0)
        {
            if (_tilesWaitSince is { } since)
            {
                _log.Info(LogCategory, "check after: every tile to the goal is attached and the plan still fails: the no-path stands", new LogField("name", Options.Name), new LogField("reason", path.Reason), new LogField("waited_s", (now - since).ToString("F1", CultureInfo.InvariantCulture)));
            }

            return false;
        }

        if (_tilesWaitSince is { } waiting)
        {
            if (now - waiting >= HorizonWaitSeconds)
            {
                gaveUp = string.Create(CultureInfo.InvariantCulture, $"; the tiles {names} between the body and the goal were not attached within {HorizonWaitSeconds:F0} s ({map.AttachedLandblocks.Count} attached)");
                return false;
            }

            _log.Info(LogCategory, "the map changed and the plan still fails: waiting on for the tiles", new LogField("name", Options.Name), new LogField("reason", path.Reason), new LogField("missing", names), new LogField("attached", map.AttachedLandblocks.Count));
        }
        else
        {
            _tilesWaitSince = now;
            TileWaits++;
            _log.Info(LogCategory, "check before: the plan failed with tiles missing between the body and the goal: the body waits for them, then plans again", new LogField("name", Options.Name), new LogField("reason", path.Reason), new LogField("missing", names), new LogField("attached", map.AttachedLandblocks.Count), new LogField("wait_s", HorizonWaitSeconds));
        }

        _path = null;
        _route = null;
        _confirming = false;
        _rung = Rung.None;
        ReleaseAll();
        _lastProgressTime = now;
        return true;
    }

    /// <summary>The landblocks (<c>0xXXYY0000</c>) with no attached tile on the straight line from <paramref name="from"/> to <paramref name="to"/>, in order, walked in land-cell steps.</summary>
    /// <param name="map">The lattice.</param>
    /// <param name="from">The body's global position in metres.</param>
    /// <param name="to">The goal's global position in metres.</param>
    internal static IReadOnlyList<uint> MissingTiles(NavMap map, Vector2 from, Vector2 to)
    {
        var missing = new List<uint>();
        var length = Vector2.Distance(from, to);
        for (var s = 0f; ; s += NavMap.LandCellNodes)
        {
            var id = NavPoint.FromGlobal(Vector2.Lerp(from, to, length <= 0f ? 1f : MathF.Min(s / length, 1f)), 0f).LandblockId & 0xFFFF0000u;
            if (!map.TryGetTile(id, out _) && !missing.Contains(id))
            {
                missing.Add(id);
            }

            if (s >= length)
            {
                return missing;
            }
        }
    }

    /// <summary>
    /// The path source's plan with the NPC bodies on it (M6 4 W2, <c>m6-npc-in-door-lane</c>): the bodies within <see cref="NpcBodies.RouteMetres"/> of the straight line to the goal
    /// are stamped first; a path that meets a body not stamped stamps the bodies within that of the path and plans again (at most <see cref="NpcBodies.MaxPlans"/> plans); a path that
    /// still meets a stamped body, or a plan the bodies closed whose plan without them meets one, names that body in <paramref name="inLane"/>. Without a feed or a lattice: the plan alone.
    /// </summary>
    private NavPath PlanPath(in CellFrame position, out NavBody? inLane)
    {
        inLane = null;
        var goal = _goal!;
        if (BodyFeed is not { } feed || _paths.Planner is null)
        {
            Scars.ClearBodies();
            return _paths.Plan(position, goal, Scars);
        }

        var start = NavMap.FromCellFrame(position).Global;
        var to = goal.Point.Global;
        _npcs.Refresh(feed, start, MathF.Max(CreatureLookMetres, Vector2.Distance(start, to) + NpcBodies.RouteMetres), goal.Guid ?? 0, to, _paths.Map);
        if (_npcs.Indoor > 0 && !_npcIndoorLogged)
        {
            _npcIndoorLogged = true;
            _log.Warn(LogCategory, "TODO(m6-npc-indoor-bodies): NPC bodies in rooms are not on the plan (the room graph knows no bodies)", new LogField("name", Options.Name), new LogField("bodies", _npcs.Indoor));
        }

        _npcs.SelectNear(start, to);
        for (var attempt = 1; ; attempt++)
        {
            _npcs.Stamp(Scars);
            var path = _paths.Plan(position, goal, Scars);
            if (path.Status == NavPathStatus.Failed || path.Waypoints.Count == 0)
            {
                if (_npcs.Stamped.Count == 0)
                {
                    return path;
                }

                // Did the bodies close the plan? The plan without them: a body it meets stands where no way goes round.
                Scars.ClearBodies();
                var bare = _paths.Plan(position, goal, Scars);
                if (bare.Status == NavPathStatus.Failed || bare.Waypoints.Count == 0)
                {
                    LogBodies("the plan failed with and without the NPC bodies", attempt + 1);
                    return path;
                }

                inLane = _npcs.FirstMeeting(bare.Waypoints, start);
                LogBodies(inLane is null ? "the NPC bodies closed the plan but none stands on the plan without them: running that plan" : "the NPC bodies closed the plan", attempt + 1);
                return bare;
            }

            var met = _npcs.FirstMeeting(path.Waypoints, start);
            if (met is null)
            {
                LogBodies("NPC bodies on the plan", attempt);
                return path;
            }

            if (_npcs.IsStamped(met.Value.Guid) || attempt >= NpcBodies.MaxPlans || _npcs.SelectNear(path.Waypoints) == 0)
            {
                inLane = met;
                LogBodies("an NPC body on the plan that the plan cannot go round", attempt);
                return path;
            }
        }
    }

    /// <summary>
    /// M6 7 AI: the named failure of a plan that does not keep clear of the run's portals, or null. A whole plan's line is checked, whichever source made it (a doorway lane or a ramp is no room graph step). A plan the list closed is made once without it: the portal that plan meets stands where no way
    /// goes round; a plan that fails without the list too is its own failure.
    /// </summary>
    private string? KeepOffFailure(NavPath path, in CellFrame position, RoomGraph? rooms)
    {
        if (_keep is not { } keep)
        {
            return null;
        }

        if (path.Status != NavPathStatus.Failed && path.Waypoints.Count > 0)
        {
            return keep.FirstMeeting(path.Waypoints) is { } met ? KeepOffName(keep, met.Portal, $"leg {met.Leg} of the plan's {path.Waypoints.Count} waypoints passes within the clearance") : null;
        }

        if (rooms is not null)
        {
            rooms.KeepOff = null;
        }

        Scars.SetKeepOff([]);
        var bare = PlanPath(position, out _);
        if (rooms is not null)
        {
            rooms.KeepOff = keep;
        }

        Scars.SetKeepOff(keep.OutdoorDiscs);
        if (bare.Status == NavPathStatus.Failed || bare.Waypoints.Count == 0)
        {
            return null;
        }

        return keep.FirstMeeting(bare.Waypoints) is { } closed
            ? KeepOffName(keep, closed.Portal, $"no way round it: {path.Reason ?? "no plan"} with the list, and the plan without the list passes within the clearance on leg {closed.Leg}")
            : string.Create(CultureInfo.InvariantCulture, $"{PortalKeepOffFailure}:none: the plan failed with the list ({path.Reason ?? "no plan"}) and the plan without it meets no portal");
    }

    private static string KeepOffName(PortalKeepOff keep, in KeptPortal portal, string how) =>
        string.Create(CultureInfo.InvariantCulture, $"{PortalKeepOffFailure}:{portal.Name.Replace(' ', '_')}: {how} ({keep.ClearanceOf(portal):F2} m)");

    /// <summary>
    /// M6 14 holding plates: the nearest the body, on the ground, has come to a plate of the room graph; the first time inside a plate's touch (its shape and her body: <see cref="PlateKeepOff.TouchOf"/>) is logged as a warning (report-only:
    /// the touch itself is the server's sweep, and a plate that fires is named by the jump's witness).
    /// </summary>
    private void NotePlate(NavPoint here, in CellFrame position)
    {
        if (_rooms?.Plates is not { } walls || here.LandblockId != _rooms.LandblockId || !_body.OnGround || JumpInFlight || walls.Nearest(here.Local) is not { } nearest || nearest.Metres >= PlateNearestMetres)
        {
            return;
        }

        PlateNearestMetres = nearest.Metres;
        PlateNearestWhere = string.Create(CultureInfo.InvariantCulture, $"{nearest.Plate.Name} from {position}");
        if (!_plateInsideLogged && nearest.Metres < PlateKeepOff.TouchOf(nearest.Plate))
        {
            _plateInsideLogged = true;
            _log.Warn(LogCategory, "the body came inside a pressure plate's touch (report-only)", new LogField("name", Options.Name), new LogField("plate", nearest.Plate.Name), new LogField("metres", nearest.Metres), new LogField("touch_m", PlateKeepOff.TouchOf(nearest.Plate)), new LogField("at", position.ToString()));
        }
    }

    private bool _plateInsideLogged;

    /// <summary>M6 7 AI: the nearest the body has come to a kept portal; the first time inside the clearance is logged (report-only: the touch itself is the server's, and the walk's own failure names it).</summary>
    private void NoteKeepOff(Vector2 global, float z, in CellFrame position)
    {
        if (_keep?.Own is { } own && MathF.Abs(z - own.Z) <= PortalKeepOff.StoreyMetres)
        {
            OwnNearestMetres = MathF.Min(OwnNearestMetres, Vector2.Distance(global, own.Global)); // M6 14: report-only, the verdict's own_nearest_m=
        }

        if (_keep is not { } keep || keep.Nearest(global, z) is not { } nearest || nearest.Metres >= KeepOffNearestMetres)
        {
            return;
        }

        KeepOffNearestMetres = nearest.Metres;
        _keepOffNearest = (nearest.Portal.Name, position);
        if (!_keepOffInsideLogged && nearest.Metres < keep.Clearance)
        {
            _keepOffInsideLogged = true;
            _log.Warn(LogCategory, "the body came inside a portal's clearance (report-only)", new LogField("name", Options.Name), new LogField("portal", nearest.Portal.Name), new LogField("metres", nearest.Metres), new LogField("clearance_m", keep.Clearance), new LogField("at", position.ToString()));
        }
    }

    private void LogBodies(string message, int plans)
    {
        if (_npcs.Seen.Count == 0 && _npcs.AtGoal == 0 && _npcs.OffLevel == 0)
        {
            return;
        }

        _log.Info(
            LogCategory,
            message,
            new LogField("name", Options.Name),
            new LogField("seen", _npcs.Seen.Count),
            new LogField("stamped", _npcs.Stamped.Count),
            new LogField("at_goal", _npcs.AtGoal),
            new LogField("off_level", _npcs.OffLevel),
            new LogField("indoor", _npcs.Indoor),
            new LogField("plans", plans));
    }

    /// <summary>
    /// An NPC stands where the plan cannot go round (M6 4 W2): the first time in a row the body waits <see cref="NpcBodies.LaneWaitSeconds"/>, keys released, then plans once more;
    /// the second ends the run Blocked <c>no path: npc-in-lane:0x&lt;guid&gt;</c>. Never a push. Returns false (no path to run now).
    /// </summary>
    private bool WaitForBody(double now, in NavBody body)
    {
        var at = NavPoint.FromGlobal(body.Centre, body.Z);
        var where = string.Create(CultureInfo.InvariantCulture, $"0x{body.Guid:X8} ({body.Name}) at 0x{at.LandblockId:X8} ({at.Local.X:F2}, {at.Local.Y:F2}, {body.Z:F2}), disc {NpcBodies.DiscRadius(body.Radius):F2} m");
        _path = null;
        _route = null;
        _confirming = false;
        ReleaseAll();
        if (_npcLaneWaited)
        {
            End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"no path: npc-in-lane:0x{body.Guid:X8}: {where} stands where the plan cannot go round (waited {NpcBodies.LaneWaitSeconds:F0} s and planned once more; the walker never pushes)"), now);
            return false;
        }

        _npcLaneWaited = true;
        NpcLaneWaits++;
        _npcLaneUntil = now + NpcBodies.LaneWaitSeconds;
        _lastProgressTime = now;
        _log.Warn(LogCategory, "an NPC stands where the plan cannot go round: waiting, then one more plan (npc-in-lane)", new LogField("name", Options.Name), new LogField("npc", where), new LogField("wait_s", NpcBodies.LaneWaitSeconds));
        return false;
    }

    /// <summary>Confirms on within the budget; true once the path is confirmed (and the run may steer this step).</summary>
    private bool StepConfirm(double now, in CellFrame position)
    {
        var confirmer = _confirmer!;
        var status = confirmer.Continue(Options.ConfirmBudgetMilliseconds);
        if (status == RouteConfirmStatus.Pending)
        {
            ReleaseAll(); // the body stands while the rest of the path is confirmed (the Godot host's slices)
            _lastProgressTime = now;
            return false;
        }

        _confirming = false;
        if (status == RouteConfirmStatus.Confirmed)
        {
            _lastProgressTime = now;
            _refusalsInARow = 0;
            return true;
        }

        var failure = confirmer.Failure!.Value;
        var refused = _path!.Waypoints;
        LastConfirmFailure = failure;
        _confirmFailures++;
        _refusalsInARow++;
        _log.Warn(LogCategory, "the physics refused the path", new LogField("name", Options.Name), new LogField("failure", failure.ToString()));
        if (AvoidCellOf(_path!.Waypoints, failure, _goal!) is { } cell && _paths is INavCellAvoider avoider && avoider.Avoid(cell))
        {
            // Indoors the scar is the cell: the physics refused the way into it, the room graph goes round it.
            _log.Warn(LogCategory, "TODO(room-steps): the room graph leaves out a cell the physics refused (the graph knows no steps)", new LogField("name", Options.Name), new LogField("cell", cell));
        }
        else if (_paths.Map is not null && LandDefs.IsOutdoorCell(failure.At.CellId))
        {
            Scars.Add(NavMap.FromCellFrame(failure.At), StuckScarMetres);
        }

        if (_refusalsInARow >= MaxConfirmFailures)
        {
            End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"the physics refused the path {_refusalsInARow} times in a row ({_confirmFailures} in the run); last: {failure}"), now);
            return false;
        }

        if (!Plan(now, position, replan: true, "refused"))
        {
            return false;
        }

        if (_confirming && SameWaypoints(refused, _path!.Waypoints))
        {
            // M6 7 Q: nothing was left out (a graph that cannot avoid, an outdoor scar the plan does not meet), so the physics would refuse the same samples again: the live round's three identical refusals, a stop with no change between tries.
            _confirming = false;
            End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"the physics refused the path and the replan reproduced it ({_refusalsInARow} in a row, {_confirmFailures} in the run; {refused.Count} waypoints): nothing to try; last: {failure}"), now);
            return false;
        }

        return StepConfirm(now, position);
    }

    /// <summary>True when two plans walk the same points in the same cells (to the centimetre).</summary>
    internal static bool SameWaypoints(IReadOnlyList<NavWaypoint> a, IReadOnlyList<NavWaypoint> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].At.CellId != b[i].At.CellId || Vector3.DistanceSquared(a[i].At.Origin, b[i].At.Origin) > 1e-4f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The indoor cell a refused sample was on its way into: the first waypoint's cell after the failed segment's start that differs from
    /// it (an EnvCell, not the goal's). When that cell is the goal's own, the room the refused sample stands in instead, if it is a room
    /// the path entered (not the path's first cell, where the body is): the way through it to the goal is the refused one (M6 4 w6a: Fort
    /// Tethana's Grand Master, whose east door's room 0x2581016D holds a step the ground probe refuses on the way into his room 0x25810170;
    /// without it the plan takes the west door, which confirms). A sample refused on a walk inside one room the path entered (M6 7 G, the Town Network's 0x00070144: the crossing either side of the ring of stones at its door stands, the walk across the
    /// room under its central dais does not) leaves out that room, not the door after it. Null when there is none (the failure was in the goal's own cell, or outdoors).
    /// </summary>
    internal static uint? AvoidCellOf(IReadOnlyList<NavWaypoint> waypoints, RouteConfirmFailure failure, NavGoal goal)
    {
        if (failure.Segment >= waypoints.Count)
        {
            return null;
        }

        var from = waypoints[failure.Segment].At.CellId;
        if (failure.Segment + 1 < waypoints.Count && waypoints[failure.Segment + 1].At.CellId == from && !LandDefs.IsOutdoorCell(from) && from != waypoints[0].At.CellId && from != goal.CellId)
        {
            return from; // refused inside a room the path entered: the way across it is the refused one, not the door after it
        }

        for (var i = failure.Segment + 1; i < waypoints.Count; i++)
        {
            var cell = waypoints[i].At.CellId;
            if (cell != from)
            {
                if (cell == goal.CellId)
                {
                    var at = failure.At.CellId;
                    return !LandDefs.IsOutdoorCell(at) && at != cell && at != waypoints[0].At.CellId ? at : null;
                }

                return !LandDefs.IsOutdoorCell(cell) ? cell : null;
            }
        }

        return null;
    }

    private void Steer(NavRoute route, NavPoint here, in CellFrame position, float s)
    {
        var goal = _goal!;
        var waypoints = _path!.Waypoints;
        if (waypoints.Count == 0)
        {
            // Plan() ends an empty path before it is stored, so this is unreachable today; kept so no later caller can index an empty list.
            End(NavRunState.Blocked, "no path: empty-path (Steer was given no waypoints)", _lastTime);
            return;
        }

        // m5-nav-one-waypoint (closed in M5 item 0 c2): a route of one waypoint (the start and the goal snapped to one lattice point) has no next vertex
        // past the first: steer at the goal itself (a horizon route's one waypoint is its edge) and let the arrival rule end the run.
        var next = Math.Min(1, waypoints.Count - 1);
        while (next < waypoints.Count - 1 && (route.ArcLengthAt(next) <= s + 0.01f || (waypoints[next].Indoor && here.HorizontalDistance(route.Vertices[next]) <= VertexPassMetres)))
        {
            next++; // D134: indoors a vertex the body is within VertexPassMetres of is passed (a body beside it would turn on the spot to touch it)
        }

        if (ReferenceEquals(route, _nextRoute) && next < _lastNext)
        {
            ProgressBacks++; // D134: the next vertex stepped back on the same route (report-only)
            SteeringTally.AddProgressBack();
        }

        _nextRoute = route;
        _lastNext = next;
        if (ReferenceEquals(route, _cornerRoute) && !_cornerActive && _cornerLast > next && _cornerLast < waypoints.Count)
        {
            next = _cornerLast; // D134: a corner turned is behind the body: the leg after it is the one ahead
        }

        // Outdoors over the lattice towards a plain vertex, the straight run (nav-natural-steering); indoors over the rooms towards a room vertex, the same
        // straight run (nav-indoor-steering); outdoors towards a doorway, P5a's pursuit.
        var outdoorStraight = _paths.Map is not null && LandDefs.IsOutdoorCell(position.CellId) && !waypoints[next].Doorway && !waypoints[next].Indoor;
        var indoorStraight = !outdoorStraight && _rooms is not null && !LandDefs.IsOutdoorCell(position.CellId) && waypoints[next].Indoor;
        var straight = outdoorStraight || indoorStraight;
        // The predictive steering (NavRunnerPredictive); the tracks are swept through the physics: without it a cell test cannot see a thin wall between two rooms of the route.
        if (((Options.PredictiveSteering && indoorStraight && _dungeonRooms) || (Options.PredictiveOutdoors && outdoorStraight)) && _confirmer is not null)
        {
            _pDriving = true; // the keys it sends count as its own (PredictKeyChanges)
            var driven = PredictiveSteer(route, here, position, next);
            _pDriving = false;
            _predictiveHeld = driven;
            if (driven)
            {
                return;
            }
        }
        else
        {
            _predictiveHeld = false;
        }

        var aim = outdoorStraight ? StraightAim(route, here, s, next)
            : indoorStraight ? IndoorStraightAim(route, here, position, s, next)
            : PursuitAim(route, here, position, s, next);

        // Aiming at the next vertex, a body that drifted off the route tracks back onto it (a line the physics confirmed), always and with the pursuit's short lead
        // near a static; a corner cut is checked from the body each step.
        var tight = false;
        if (straight && _aimVertex == next)
        {
            tight = outdoorStraight && (ClearanceAt(here) <= TightClearanceNodes || ClearanceAt(route.PointAt(s + TightLookAheadMetres)) <= TightClearanceNodes);
            var offRoute = route.DistanceFrom(here);
            _tracking = tight || offRoute > (_tracking ? TrackReleaseMetres : CrossTrackMetres);
        }
        else
        {
            _tracking = false;
        }

        if (_tracking)
        {
            var lead = tight ? MathF.Max(LeadMinMetres, LeadSpeedFactor * _speed) : MathF.Max(TrackLeadMinMetres, TrackLeadSeconds * _speed);
            aim = route.PointAt(MathF.Min(s + lead, route.ArcLengthAt(next)));
        }

        if (waypoints.Count == 1 && _path.Status != NavPathStatus.Horizon)
        {
            aim = goal.Point;
        }

        // D134: at an indoor corner the body runs to the corner point, lets forward go and turns in place to the next leg's heading, then runs on.
        var corner = indoorStraight ? StepCorner(route, here, position, s, next) : 0;
        _steerCorner = corner == 2 ? _cornerSerial : 0;
        if (corner == 1)
        {
            // Along the leg before the corner (its line, snapped to the axis): the point CornerLeadMetres past the body's foot on it, so the heading holds the
            // corridor's axis to the corner instead of swinging at a point a few centimetres to the side.
            var along = Vector2.Dot(here.Global - _cornerPoint, _cornerIn);
            var onLine = _cornerPoint + (_cornerIn * (along + CornerLeadMetres));
            aim = new NavPoint(here.LandblockId, new Vector3(onLine - NavPoint.BlockOrigin(here.LandblockId), here.Z));
            _tracking = false;
        }

        var offset = aim.Global - here.Global;
        var bearing = (Math.Atan2(offset.X, offset.Y) * (180.0 / Math.PI) + 360.0) % 360.0;
        var error = Normalize((corner == 2 ? _cornerHeading : bearing) - RetailFrames.GetHeading(position.Frame));
        var magnitude = Math.Abs(error);

        var nearDoorway = false;
        var turnsAtDoorway = false;
        for (var i = Math.Max(0, next - 1); i < waypoints.Count; i++)
        {
            if (waypoints[i].Doorway)
            {
                nearDoorway |= WalksNearDoorway(route, here, s, i, indoorStraight && corner == 0);
                turnsAtDoorway |= _dungeonRooms && here.HorizontalDistance(route.Vertices[i]) <= WalkNearDoorwayMetres && !RunsThroughDoorway(route, i);
            }

            if (route.ArcLengthAt(i) > s + WalkNearDoorwayMetres)
            {
                break;
            }
        }

        var zone = WalkZoneMetres(goal.StopTolerance, _speed, _frameSeconds); // M6 14: the walk zone grows with her live speed and the frame time
        var nearStop = Options.Stop && here.HorizontalDistance(goal.Point) <= zone;
        if (nearStop && !_zoneLogged && zone > WalkNearStopMetres)
        {
            _zoneLogged = true;
            _log.Info(LogCategory, "the walk zone before the stop is widened by her speed", new LogField("name", Options.Name), new LogField("zone_m", zone), new LogField("speed_mps", _speed), new LogField("frame_s", _frameSeconds));
        }
        var side = error > 0 ? 1 : -1;

        // A held key is let go a frame's turn before the heading arrives (or once it has passed), never held back, so it never overshoots into the
        // other side's key; a press needs the dead-band (the tolerance in the pursuit) and TurnKeyMinSeconds since the last change.
        // The corner's wider band and its approach's no-press zone let a running body's small error ride; a body standing still (no forward held) is not running at the corner, and
        // facing up to TurnInPlaceDegrees off it ran off the line beside a static before it turned (the world walk's rooms, W8: a leg begun beside a staircase 0x02000396 12 and 25 degrees
        // off its first leg, in a house layout repeated over a hundred landblocks, walked into the stairs' side and needed a recovery move). It takes the ordinary dead-band.
        var quantum = _turnRate * PhysicsGlobals.MinQuantum;
        var release = Math.Max(TurnReleaseDegrees, quantum * TurnReleaseQuanta);
        var deadBand = Math.Max(corner == 1 && _forward ? CornerApproachDeadBandDegrees : !straight ? HeadingToleranceDegrees : _tracking ? TrackDeadBandDegrees : HeadingDeadBandDegrees, quantum * DeadBandMinQuanta);
        // Indoors a press also waits while the heading's line passes the aimed vertex within its tolerance (nav-indoor-steering): near a vertex a few
        // centimetres off are many degrees, and the body passes the doorway (or stops at the goal) without the key.
        // D134: in the last CornerApproachMetres to a corner no new press (the turn in place sets the heading; a tap there is reversed by it).
        var holding = corner == 1 && _forward && Vector2.Dot(_cornerPoint - here.Global, _cornerIn) <= CornerApproachMetres;
        var passing = holding || (indoorStraight && !_tracking && corner == 0 && Passes(here, position, route.Vertices[_aimVertex], _aimVertex == waypoints.Count - 1 ? 0.5f * DoorwayPassMetres : DoorwayPassMetres));
        var turn = _turn != 0
            ? (side == _turn && magnitude > release ? _turn : 0)
            : (magnitude > deadBand && !passing ? side : 0);
        // In a dungeon a doorway is run at, so near one the body turns at it lines up first: a press at the open angle would run it wide of the next doorway (m6-dungeon-run-share).
        var forward = corner != 2 && magnitude <= (turnsAtDoorway ? TurnInPlaceNearDoorwayDegrees : TurnInPlaceDegrees);
        // A body standing and turning in place runs off at TurnInPlaceDegrees still turning, and the turn carries it round a curve that bulges off the straight line the plan
        // confirmed: at 7 m/s and 127 degrees a second, 60 degrees off bulges 1.6 m. In a dungeon (and in a building on a run that steers predictively outdoors, whose
        // runs end where the next walk out of the building starts: Dryreach's scrivener, 2026-11-16) it turns on in place while that curve meets a static or leaves the rooms (CurveClear).
        if (forward && !_forward && magnitude > deadBand && !RunStartReaches(route, here, position, next, aim, magnitude))
        {
            forward = false;
            if (!_reachHeld)
            {
                _reachHeld = true;
                ReachTurns++;
                _log.Info(
                    LogCategory,
                    "a turn in place: the way ahead is inside the circle a run started now would turn on",
                    new LogField("name", Options.Name),
                    new LogField("error_deg", magnitude.ToString("F0", CultureInfo.InvariantCulture)),
                    new LogField("at", position.ToString()));
            }
        }
        else if (forward && !_forward && magnitude > deadBand && !CurveClear(position, error))
        {
            forward = false;
            if (!_curveHeld)
            {
                _curveHeld = true;
                CurveTurns++;
                _log.Info(
                    LogCategory,
                    "a turn in place: the curve a run started now would sweep meets a static",
                    new LogField("name", Options.Name),
                    new LogField("error_deg", magnitude.ToString("F0", CultureInfo.InvariantCulture)),
                    new LogField("at", position.ToString()));
            }
        }
        else if (forward)
        {
            _curveHeld = false;
            _reachHeld = false;
        }

        if (turn == 0 && !forward && corner != 2 && magnitude > deadBand)
        {
            // M5 7 P7i: a body that may not run (its aim past TurnInPlaceDegrees) and holds no turn key would stand for good: the corner approach's no-press
            // zone and the indoor pass both assume a body already running at its aim (live 2026-10-30: a leg begun inside a corner's approach facing away
            // stood 6 s to each stuck rung, three legs in a row). It turns in place.
            turn = side;
            if (_turn == 0)
            {
                StandTurns++;
                _log.Info(
                    LogCategory,
                    "a turn in place: the aim is behind the body and no key would press (P7i)",
                    new LogField("name", Options.Name),
                    new LogField("error_deg", magnitude.ToString("F0", CultureInfo.InvariantCulture)),
                    new LogField("why", holding ? "corner-approach" : passing ? "passing" : "none"),
                    new LogField("corner", corner),
                    new LogField("at", position.ToString()));
            }
        }

        if (turn != _turn && (turn == 0 || _lastTime - _turnChangedAt >= TurnKeyMinSeconds || magnitude > TurnInPlaceDegrees))
        {
            _turnChangedAt = _lastTime;
            SetTurn(turn);
        }

        SetBack(false);
        SetSide(0);
        // A doorway's walk lasts through the turn in place it was for: forward let go from a walk stops the body in 0.3 m, and the run key pressed before the turn ends glides it on at a run with no forward key (the Lodge's 0x0283, m6-dungeon-run-share).
        _doorwayWalk = nearDoorway || (_doorwayWalk && _dungeonRooms && !forward);
        SetRun(!nearStop && !_doorwayWalk);
        SetForward(forward);
    }

    /// <summary>
    /// D134's corner (the owner's rule: turn in place to the corridor's axis, then run): 0 when no corner is near, 1 while the body runs to the corner point (the aim),
    /// 2 while it stands and turns to the next leg's heading. A corner is a stretch of the indoor route within <see cref="CornerSpanMetres"/> whose vertices turn
    /// one way by <see cref="CornerMinDegrees"/> or more; its point is where the leg before it and the leg after it meet (each snapped to a N/S/E/W axis within
    /// <see cref="CornerAxisSnapDegrees"/>), within <see cref="CornerApproachMetres"/> of both ends and inside the route's cells at the body's radius, else the
    /// corner is run as before. It is engaged within <see cref="CornerApproachMetres"/> of the point when the straight run does not aim past the corner; forward
    /// goes up at the point (less its coast); the corner ends when the heading is within the turn key's release of the next leg's.
    /// </summary>
    private int StepCorner(NavRoute route, NavPoint here, in CellFrame position, float s, int next)
    {
        if (!ReferenceEquals(route, _cornerRoute))
        {
            _cornerRoute = route;
            _cornerActive = false;
            _cornerLast = -1;
            _cornerRouteIndoors = true;
            foreach (var waypoint in _path!.Waypoints)
            {
                _cornerRouteIndoors &= waypoint.Indoor;
            }
        }

        if (!_cornerRouteIndoors)
        {
            return 0; // a building's route (a door to the outside): its rooms hold furniture the cells do not show, so its corners run as before
        }

        if (!_cornerActive)
        {
            if (!FindCorner(route, s, Math.Max(next, _cornerLast + 1), out var found) || _aimVertex > found.Last
                || Vector2.Distance(here.Global, found.Point) > CornerLookMetres || !ClearToCorner(position, found, next))
            {
                return 0;
            }

            _cornerActive = true;
            _cornerTurning = false;
            _cornerSerial++;
            _cornerLast = found.Last;
            _cornerPoint = found.Point;
            _cornerIn = found.In;
            _cornerHeading = found.Heading;
        }

        var toPoint = _cornerPoint - here.Global;
        if (!_cornerTurning)
        {
            if (toPoint.Length() > CornerLookMetres + 1f)
            {
                _cornerActive = false; // pushed off the corner (a creature, a slide): the straight run takes over
                _cornerLast = -1;
                return 0;
            }

            if (Vector2.Dot(toPoint, _cornerIn) > MathF.Max(CornerStopMetres, CornerBrakeSeconds * _speed))
            {
                return 1;
            }

            _cornerTurning = true;
            _cornerTurnFrom = _lastTime;
        }

        // Turned when within the straight run's dead-band (a key is let go half a physics step early and the step may carry it past, so a tighter test could
        // wait for a press the dead-band never makes), or after CornerTurnSeconds whatever the heading (never a stand that the stuck ladder reads as a stall).
        var quantum = _turnRate * PhysicsGlobals.MinQuantum;
        var aligned = Math.Max(HeadingDeadBandDegrees, quantum * DeadBandMinQuanta);
        if (Math.Abs(Normalize(_cornerHeading - RetailFrames.GetHeading(position.Frame))) <= aligned || _lastTime - _cornerTurnFrom >= CornerTurnSeconds)
        {
            _cornerActive = false; // turned: the leg after the corner is ahead (Steer's floor on the next vertex)
            return 0;
        }

        return 2;
    }

    /// <summary>Whether the body's line to the corner's point, swept at its radius, stays in its cell and the route's cells up to the corner.</summary>
    private bool ClearToCorner(in CellFrame position, RouteCorner corner, int next)
    {
        var local = corner.Point - NavPoint.BlockOrigin(position.CellId & 0xFFFF0000u);
        var to = Math.Min(_path!.Waypoints.Count - 1, corner.Last + 1);
        return SweepInRouteCells(position.Origin, new Vector3(local.X, local.Y, position.Origin.Z), Math.Max(0, next - 1), to, position.CellId);
    }

    /// <summary>A corner of the route (<see cref="StepCorner"/>): its last turning vertex, its point, the leg before's direction and the heading after it.</summary>
    private readonly record struct RouteCorner(int Last, Vector2 Point, Vector2 In, double Heading);

    /// <summary>The first corner of the indoor route from vertex <paramref name="from"/> within reach of the body's progress <paramref name="s"/>. Allocates nothing.</summary>
    private bool FindCorner(NavRoute route, float s, int from, out RouteCorner corner)
    {
        corner = default;
        var waypoints = _path!.Waypoints;
        var vertices = route.Vertices;
        for (var first = Math.Max(1, from); first < waypoints.Count - 1; first++)
        {
            if (route.ArcLengthAt(first) - s > CornerApproachMetres + CornerSpanMetres)
            {
                return false;
            }

            var turn = VertexTurn(vertices, first);
            if (Math.Abs(turn) < CornerVertexDegrees || !waypoints[first].Indoor)
            {
                continue;
            }

            var last = first;
            for (var k = first + 1; k < waypoints.Count - 1 && route.ArcLengthAt(k) - route.ArcLengthAt(first) <= CornerSpanMetres && waypoints[k].Indoor; k++)
            {
                var t = VertexTurn(vertices, k);
                if (Math.Abs(t) < CornerVertexDegrees)
                {
                    continue;
                }

                if (Math.Sign(t) != Math.Sign(turn))
                {
                    break;
                }

                turn += t;
                last = k;
            }

            if (Math.Abs(turn) < CornerMinDegrees)
            {
                first = last;
                continue;
            }

            var headingIn = Snap(Bearing(vertices[first - 1].Global, vertices[first].Global));
            var headingOut = Snap(Bearing(vertices[last].Global, vertices[last + 1].Global));
            var dirIn = Direction(headingIn);
            var dirOut = Direction(headingOut);
            var a = vertices[first].Global;
            var b = vertices[last].Global;
            Vector2 point;
            var cross = (dirIn.X * dirOut.Y) - (dirIn.Y * dirOut.X);
            if (first == last || MathF.Abs(cross) < 0.3f)
            {
                point = first == last ? a : (a + b) * 0.5f;
            }
            else
            {
                var ab = b - a;
                point = a + (dirIn * (((ab.X * dirOut.Y) - (ab.Y * dirOut.X)) / cross));
            }

            if (Vector2.Distance(point, a) > CornerApproachMetres || Vector2.Distance(point, b) > CornerApproachMetres || !CornerInsideRooms(first, last, point))
            {
                return false;
            }

            corner = new RouteCorner(last, point, dirIn, headingOut);
            return true;
        }

        return false;
    }

    /// <summary>Whether the lines from the corner's first vertex to its point and on to its last vertex, swept at the body's radius, stay in the route's cells.</summary>
    private bool CornerInsideRooms(int first, int last, Vector2 point)
    {
        var waypoints = _path!.Waypoints;
        if (_cornerCheckedPath != _path || _cornerChecked.Length < waypoints.Count)
        {
            _cornerCheckedPath = _path;
            if (_cornerChecked.Length < waypoints.Count)
            {
                _cornerChecked = new sbyte[Math.Max(waypoints.Count, _cornerChecked.Length * 2)];
            }

            Array.Clear(_cornerChecked);
        }

        if (_cornerChecked[first] != 0)
        {
            return _cornerChecked[first] > 0;
        }

        var ok = CornerClear(first, last, point);
        _cornerChecked[first] = (sbyte)(ok ? 1 : -1);
        return ok;
    }

    /// <summary>
    /// The corner's test, once a plan: the lines from its first vertex to its point and on to its last vertex swept at the body's radius inside the route's cells, and
    /// with a <see cref="RouteConfirmer"/> the physics' sweep of the body's sphere along both clear (a building's counter or table lies inside its room's cell).
    /// </summary>
    private bool CornerClear(int first, int last, Vector2 point)
    {
        var waypoints = _path!.Waypoints;
        var a = waypoints[first].At;
        var b = waypoints[last].At;
        if ((a.CellId >> 16) != (b.CellId >> 16))
        {
            return false;
        }

        // An EnvCell's origin is landblock-local but not inside the landblock's 192 m (a dungeon's cells lie anywhere): the point is taken back to the corner's landblock, never re-normalised.
        var local = point - NavPoint.BlockOrigin(a.CellId & 0xFFFF0000u);
        var p = new Vector3(local.X, local.Y, a.Origin.Z);
        var to = Math.Min(waypoints.Count - 1, last + 1);
        if (!SweepInRouteCells(a.Origin, p, first, to, a.CellId) || !SweepInRouteCells(p, b.Origin, first, to, a.CellId))
        {
            return false;
        }

        if (_confirmer is null)
        {
            return true;
        }

        var lift = new Vector3(0f, 0f, IndoorLineLiftMetres);
        var cell = a.CellId;
        if (!InRouteCell(p + lift, ref cell, first, to, a.CellId))
        {
            return false;
        }

        var world = _confirmer.World;
        var from = new CellFrame(a.CellId, new AcFrame(a.Origin + lift, Quaternion.Identity));
        var at = new CellFrame(cell, new AcFrame(p + lift, Quaternion.Identity));
        var onto = new CellFrame(b.CellId, new AcFrame(b.Origin + lift, Quaternion.Identity));
        return !world.SweepViewer(from, at, NavCreatures.PlayerRadiusMetres).Blocked && !world.SweepViewer(at, onto, NavCreatures.PlayerRadiusMetres).Blocked;
    }

    private static double VertexTurn(IReadOnlyList<NavPoint> vertices, int i) =>
        Normalize(Bearing(vertices[i].Global, vertices[i + 1].Global) - Bearing(vertices[i - 1].Global, vertices[i].Global));

    private static double Bearing(Vector2 from, Vector2 to) => (Math.Atan2(to.X - from.X, to.Y - from.Y) * (180.0 / Math.PI) + 360.0) % 360.0;

    private static Vector2 Direction(double heading)
    {
        var radians = heading * (Math.PI / 180.0);
        return new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
    }

    /// <summary>A heading within <see cref="CornerAxisSnapDegrees"/> of N, E, S or W snapped to it.</summary>
    private static double Snap(double heading)
    {
        var axis = Math.Round(heading / 90.0) * 90.0;
        return Math.Abs(heading - axis) <= CornerAxisSnapDegrees ? axis % 360.0 : heading;
    }

    /// <summary>
    /// Whether the body walks for doorway vertex <paramref name="i"/> where it stands: within <see cref="WalkNearDoorwayMetres"/> of a doorway it does not run through (<see cref="RunsThroughDoorway"/>).
    /// In a dungeon (m6-dungeon-run-share: rooms with no way outside, <see cref="SweepRooms"/>) that zone was most of a short leg through a doorway chain (the Burrow's 19 per cent of all legs, 75 per cent of a 12 m
    /// leg of the Lodge's) and more than the walk is for: the thin doorway cell is the land's (docs/physics.md section 15), and a sharp turn needs the walk only when forward goes up. So there the body walks only within
    /// <see cref="WalkNearDungeonDoorwayMetres"/> of the doorway, only before it, and not at a doorway its straight run aims past (<paramref name="aimed"/>, <see cref="IndoorStraightAim"/>: the line passes the vertex
    /// within <see cref="DoorwayPassMetres"/> and the cells and the sweep hold it, so the route's bend there is no turn of the body's; a D134 corner in progress does turn, so the caller passes false). A building's
    /// door and rooms and the land keep the full zone (the door stops and sills that passed live). Allocates nothing.
    /// </summary>
    private bool WalksNearDoorway(NavRoute route, NavPoint here, float s, int i, bool aimed)
    {
        var distance = here.HorizontalDistance(route.Vertices[i]);
        if (distance > WalkNearDoorwayMetres || RunsThroughDoorway(route, i))
        {
            return false;
        }

        if (!_dungeonRooms || i < 1 || i + 1 >= _path!.Waypoints.Count)
        {
            return true;
        }

        return distance <= WalkNearDungeonDoorwayMetres && route.ArcLengthAt(i) >= s && !(aimed && i < _aimVertex);
    }

    /// <summary>
    /// Whether the body runs through doorway vertex <paramref name="i"/> instead of walking near it (M5 2 F6, the owner's D47: a body in a dungeon always moves with purpose; on a room graph every
    /// cell-to-cell portal is a doorway vertex, so walking near each was walking the whole leg). The thin-doorway walk (<c>physics-thin-doorway</c>, docs/physics.md section 15) stays for the building's door, whose
    /// neighbour on one side is outdoors; between two rooms the body runs unless the route turns sharply there (more than <see cref="WalkSharpTurnDegrees"/>) or the physics' sweep from the vertex before
    /// it does not reach the vertex after it (<see cref="SweepRooms"/>). Allocates nothing.
    /// </summary>
    private bool RunsThroughDoorway(NavRoute route, int i)
    {
        var waypoints = _path!.Waypoints;
        if (_rooms is null || i < 1 || i + 1 >= waypoints.Count || !waypoints[i - 1].Indoor || !waypoints[i].Indoor || !waypoints[i + 1].Indoor)
        {
            return false;
        }

        if (i - 1 < _sweptCount && _sweptTo[i - 1] < i + 1)
        {
            return false; // the sweep stopped short of the vertex after the doorway
        }

        var before = route.Vertices[i].Global - route.Vertices[i - 1].Global;
        var after = route.Vertices[i + 1].Global - route.Vertices[i].Global;
        var lengths = before.Length() * after.Length();
        if (lengths < 1e-4f)
        {
            return true; // a vertex doubled: no direction to turn from
        }

        var turn = Math.Acos(Math.Clamp(Vector2.Dot(before, after) / lengths, -1f, 1f)) * (180.0 / Math.PI);
        return turn <= WalkSharpTurnDegrees;
    }

    /// <summary>
    /// The straight run's aim (nav-natural-steering): of the route's vertices from the next one up to the first past the look-ahead, the farthest the
    /// body may run to in a straight line (<see cref="CanRunStraightTo"/>), never past a doorway or an indoor vertex; the next vertex when none further.
    /// </summary>
    private NavPoint StraightAim(NavRoute route, NavPoint here, float s, int next)
    {
        var waypoints = _path!.Waypoints;
        var window = s + MathF.Max(StraightLeadMinMetres, StraightLeadSpeedFactor * _speed);
        var aim = next;
        var from = here.Global;
        var scars = CopyScars();
        for (var i = next + 1; i < waypoints.Count; i++)
        {
            if (waypoints[i].Doorway || waypoints[i].Indoor)
            {
                break;
            }

            if (CanRunStraightTo(route, from, next, i, scars))
            {
                aim = i;
            }

            if (route.ArcLengthAt(i) > window)
            {
                break; // one vertex past the look-ahead at most: the corner after the one ahead
            }
        }

        // At the corner: the vertex after it, when the body sees it.
        if (aim + 1 < waypoints.Count && !waypoints[aim + 1].Doorway && !waypoints[aim + 1].Indoor
            && here.HorizontalDistance(route.Vertices[aim]) <= MathF.Max(CornerSwitchMetres, CornerSwitchSeconds * _speed)
            && _paths.Map!.Sight(from, route.Vertices[aim + 1].Global, PathPlanner.StraightenMinClearance, NavFlags.Water | NavFlags.Slope | NavFlags.SteepEdge, _scarBuffer, _scarIgnored, scars))
        {
            aim++;
        }

        _aimVertex = aim;
        return route.Vertices[aim];
    }

    /// <summary>Whether the line ahead of the body along its heading passes within <paramref name="tolerance"/> of <paramref name="vertex"/> (ahead of it).</summary>
    private static bool Passes(NavPoint here, in CellFrame position, NavPoint vertex, float tolerance)
    {
        var radians = RetailFrames.GetHeading(position.Frame) * (Math.PI / 180.0);
        var ahead = new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
        var offset = vertex.Global - here.Global;
        var along = Vector2.Dot(offset, ahead);
        return along > 0f && MathF.Abs((ahead.X * offset.Y) - (ahead.Y * offset.X)) <= tolerance;
    }

    /// <summary>
    /// The indoor straight run's aim (nav-indoor-steering): of the route's room vertices from the next one up to the first past the look-ahead, and no further
    /// than the physics' sweep from the vertex behind the body reached (<see cref="SweepRooms"/>), the farthest the body may run to in a straight line
    /// (<see cref="CanRunStraightIndoors"/>); the next vertex when none further. Allocates nothing.
    /// </summary>
    private NavPoint IndoorStraightAim(NavRoute route, NavPoint here, in CellFrame position, float s, int next)
    {
        var waypoints = _path!.Waypoints;
        var window = s + MathF.Max(StraightLeadMinMetres, StraightLeadSpeedFactor * _speed);
        var behind = Math.Max(0, next - 1);
        var limit = behind < _sweptCount ? _sweptTo[behind] : waypoints.Count - 1;
        var aim = next;
        for (var j = next + 1; j <= limit && j < waypoints.Count; j++)
        {
            if (!waypoints[j].Indoor)
            {
                break;
            }

            if (CanRunStraightIndoors(route, here, position, behind, next, j))
            {
                aim = j;
            }

            if (route.ArcLengthAt(j) > window)
            {
                break; // one vertex past the look-ahead at most
            }
        }

        _aimVertex = aim;
        return route.Vertices[aim];
    }

    /// <summary>
    /// Whether the body may run straight from where it stands to route vertex <paramref name="to"/> past the vertices from <paramref name="next"/>: each
    /// skipped doorway vertex within <see cref="DoorwayPassMetres"/> of the line (a plain one within <see cref="SkipOffRouteMetres"/>); the line, swept at
    /// the body's radius (<see cref="NavCreatures.PlayerRadiusMetres"/>) <see cref="IndoorLineLiftMetres"/> above the floor, inside the body's cell and the
    /// cells of the route from <paramref name="behind"/> to <paramref name="to"/> (every <see cref="IndoorLineSampleMetres"/>, the centre and both sides:
    /// retail's <c>CEnvCell::point_in_cell</c> through <c>LandblockCellSet.Contains</c>); no more of the creature layer than the route between; and clear of the kept portals (<see cref="KeepOff"/>).
    /// </summary>
    private bool CanRunStraightIndoors(NavRoute route, NavPoint here, in CellFrame position, int behind, int next, int to)
    {
        var waypoints = _path!.Waypoints;
        var end = route.Vertices[to].Global;
        if (_keep?.FirstMeeting(here.Global, here.Z, end, route.Vertices[to].Z) is not null)
        {
            return false; // M6 7 AI: the plan went round this portal; the straight run does not cut the corner it turned
        }

        for (var k = next; k < to; k++)
        {
            var tolerance = waypoints[k].Doorway ? DoorwayPassMetres : SkipOffRouteMetres;
            if (SegmentDistance(route.Vertices[k].Global, here.Global, end) > tolerance)
            {
                return false;
            }
        }

        if (!InsideRouteCells(position, behind, to))
        {
            return false;
        }

        if (Creatures.Count == 0)
        {
            return true;
        }

        // Counted by height (M5 2 fix F2, review 2-R6): a creature a storey above or below the line costs it nothing, as it costs the plan nothing.
        var along = Creatures.ExposureAlong(here.Global, here.Z, route.Vertices[next].Global, route.Vertices[next].Z, 0);
        for (var k = next; k < to; k++)
        {
            along += Creatures.ExposureAlong(route.Vertices[k].Global, route.Vertices[k].Z, route.Vertices[k + 1].Global, route.Vertices[k + 1].Z, 0);
        }

        return Creatures.ExposureAlong(here.Global, here.Z, end, route.Vertices[to].Z, 0) <= along + PathPlanner.CreaturePullSlack;
    }

    /// <summary>The time step of <see cref="CurveClear"/>'s curve, in seconds. Not retail: Lifestone's own.</summary>
    public const double CurveStepSeconds = 0.02;

    /// <summary>
    /// Whether the curve a run started now would sweep stays clear: from the body, at its top run speed (<see cref="INavBody.RunSpeed"/>), the heading turning at the measured turn rate
    /// through <paramref name="error"/> degrees, every <see cref="CurveStepSeconds"/> a point inside a room (<c>LandblockCellSet.FindCell</c>) and out of its statics
    /// (<see cref="CellRoutePlanner.InStatic"/>), or no worse there than the straight run along the aim the same distance out (a body beside a static is not held for the line the plan confirmed). True outside a dungeon (a building's doorway leads out of its rooms onto the land, which the rooms do not hold) unless the run steers predictively outdoors (<see cref="NavRunnerOptions.PredictiveOutdoors"/>: a walk out of Dryreach's Master Scrivener of Creature Magic began where the run in had stopped, and the curve met the counter, two rungs; measured over Holtburg's walks out, no worse), without the rooms, or for a body that does not say its speed. The world walk's rooms: the cave of Alabree's
    /// tour started a leg 0x0162015C facing away from its aim, turned in place to 60 degrees off and ran, and the turn's curve carried the body between a pillar 0x02000209 and a pot
    /// 0x020000A5 beside the plan's line round the pillar (two rungs).
    /// </summary>
    private bool CurveClear(in CellFrame position, double error)
    {
        var speed = _body.RunSpeed;
        if ((!_dungeonRooms && !Options.PredictiveOutdoors) || _rooms is null || LandDefs.IsOutdoorCell(position.CellId) || !(speed > 0f) || _rooms.Cells.GetCell(position.CellId) is not { } cell)
        {
            return true;
        }

        var rate = Math.Max(_turnRate, 1.0);
        double heading = RetailFrames.GetHeading(position.Frame);
        var left = Math.Abs(error);
        var side = Math.Sign(error);
        var aim = (heading + error) * (Math.PI / 180.0);
        var ahead = new Vector3((float)Math.Sin(aim), (float)Math.Cos(aim), 0f);
        var at = position.Origin;
        var holder = cell;
        var straightHolder = cell;
        var step = 0;
        while (left > 0.0)
        {
            var turned = Math.Min(left, rate * CurveStepSeconds);
            heading += side * turned;
            left -= turned;
            step++;
            var radians = heading * (Math.PI / 180.0);
            at += new Vector3((float)Math.Sin(radians), (float)Math.Cos(radians), 0f) * (float)(speed * CurveStepSeconds);
            var straight = position.Origin + (ahead * (float)(speed * CurveStepSeconds * step));
            var straightCell = _rooms.Cells.FindCell(straight with { Z = straight.Z + CurveCellLiftMetres }, straightHolder.Id);
            var found = _rooms.Cells.FindCell(at with { Z = at.Z + CurveCellLiftMetres }, holder.Id);
            if ((found is null || _rooms.Planner.InStatic(found, at)) && straightCell is not null && !_rooms.Planner.InStatic(straightCell, straight))
            {
                return false; // only a curve worse than the straight run the plan confirmed, the same distance out along the aim
            }

            holder = found ?? holder;
            straightHolder = straightCell ?? straightHolder;
        }

        return true;
    }

    /// <summary>
    /// Whether a run started now, its turn held, reaches the way ahead (the aim and the route's next vertex), indoors: a body running at its run speed and turning at its turn rate
    /// runs round a circle of radius speed over rate, and a point inside that circle on the turning side is never reached (the body circles it: the Town Network's arrival,
    /// 2026-11-16, turned 240 degrees round the doorway 4 m ahead, stopped, and turned back). A point at distance d and error e off the heading lies outside the circle when
    /// d is at least 2 r sin e; the run waits, turning in place, until it is, with <see cref="ReachMarginMetres"/> to spare. Not retail: a person faces the way before running.
    /// </summary>
    private bool RunStartReaches(NavRoute route, NavPoint here, in CellFrame position, int next, NavPoint aim, double magnitude)
    {
        var speed = _body.RunSpeed;
        if (LandDefs.IsOutdoorCell(position.CellId) || !(speed > 0f) || magnitude <= 0.0)
        {
            return true;
        }

        var rate = Math.Max(_turnRate, PredictTurnDegreesPerSecond) * (Math.PI / 180.0); // the faster of the measured and the model's rate: the smaller circle, the kinder test
        var radius = speed / rate;
        var need = (2.0 * radius * Math.Sin(Math.Min(magnitude, 90.0) * (Math.PI / 180.0))) + ReachMarginMetres;
        var distance = Vector2.Distance(here.Global, aim.Global);
        var vertices = route.Vertices;
        for (var k = next; k < vertices.Count; k++)
        {
            var d = here.HorizontalDistance(vertices[k]);
            if (d >= ReachMinVertexMetres)
            {
                distance = Math.Min(distance, d); // the first vertex ahead the body has not reached: a doorway or a corner it must make
                break;
            }
        }

        return distance < ReachNearMetres || distance >= need; // a way a step or two ahead is the shuffle's (a run there is no run: the tailor's corner, Holtburg)
    }


    /// <summary>The margin <see cref="RunStartReaches"/> keeps outside the turning circle, in metres. Not retail: Lifestone's own.</summary>
    public const double ReachMarginMetres = 0.5;

    /// <summary>
    /// Nearer than this the way ahead is left to the old start (<see cref="RunStartReaches"/> holds no run back): in a cramped room the rule asked for a heading within a few degrees
    /// of a vertex a metre or two off and the body shuffled to a recovery move (Holtburg's Ecutha the Tailor, a start 3 m from her). In metres. Not retail: Lifestone's own.
    /// </summary>
    public const float ReachNearMetres = 2.5f;

    /// <summary>A route vertex nearer than this is the one being passed, and <see cref="RunStartReaches"/> measures to the next. Not retail: Lifestone's own.</summary>
    public const float ReachMinVertexMetres = 1f;

    /// <summary>Starts of a run held back because the way ahead lay inside the run's turning circle (<see cref="RunStartReaches"/>), each logged. Report-only.</summary>
    public int ReachTurns { get; private set; }

    /// <summary>How far above the floor <see cref="CurveClear"/> asks which room holds a point, in metres. Not retail: Lifestone's own.</summary>
    public const float CurveCellLiftMetres = 0.5f;

    /// <summary>Whether the line from the body to waypoint <paramref name="to"/>, swept at the body's radius, stays inside the cells of the route from <paramref name="behind"/> to it.</summary>
    private bool InsideRouteCells(in CellFrame position, int behind, int to)
    {
        var target = _path!.Waypoints[to].At;
        if ((target.CellId >> 16) != (position.CellId >> 16))
        {
            return false;
        }

        return SweepInRouteCells(position.Origin, target.Origin, behind, to, position.CellId);
    }

    /// <summary>Whether the line from <paramref name="a"/> to <paramref name="b"/> (landblock-local), swept at the body's radius, stays in the body's cell or the cells of the route from <paramref name="behind"/> to <paramref name="to"/>.</summary>
    private bool SweepInRouteCells(Vector3 a, Vector3 b, int behind, int to, uint bodyCell)
    {
        var flat = new Vector2(b.X - a.X, b.Y - a.Y);
        var length = flat.Length();
        if (length < 1e-3f)
        {
            return true;
        }

        var side = new Vector2(-flat.Y, flat.X) * (NavCreatures.PlayerRadiusMetres / length);
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

    /// <summary>
    /// Whether <paramref name="point"/> (landblock-local) lies in the body's cell or a cell of the route's waypoints from <paramref name="behind"/> to
    /// <paramref name="to"/>; the cell it lies in is the next point's first guess.
    /// </summary>
    private bool InRouteCell(Vector3 point, ref uint hint, int behind, int to, uint bodyCell)
    {
        if (InCell(hint, point))
        {
            return true;
        }

        var waypoints = _path!.Waypoints;
        for (var k = behind - 1; k <= to; k++)
        {
            var id = k < behind ? bodyCell : waypoints[k].At.CellId;
            if (id != hint && InCell(id, point))
            {
                hint = id;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail's <c>CEnvCell::point_in_cell</c> as <c>LandblockCellSet.Contains</c> tests it (the landblock-local point into the cell's frame, against the
    /// cell BSP), over the run's own cache of the cells' shapes (filled at each plan for the route's cells, so a warm step allocates nothing).
    /// </summary>
    private bool InCell(uint cellId, Vector3 point)
    {
        if (!_cellShapes.TryGetValue(cellId, out var shape))
        {
            if (_rooms!.Cells.GetCell(cellId) is not { } cell)
            {
                return false;
            }

            shape = (cell, _rooms.Cells.GetStructure(cell).Bsp);
            _cellShapes[cellId] = shape;
        }

        return shape.Bsp.Contains(shape.Cell.Frame.ParentToLocal(point));
    }

    /// <summary>
    /// With a <see cref="RouteConfirmer"/> (its physics world), the farthest room vertex each room vertex of <paramref name="path"/> sees, at plan time:
    /// a sphere of the body's radius (<see cref="NavCreatures.PlayerRadiusMetres"/>) swept <see cref="IndoorLineLiftMetres"/> above the floor from vertex i
    /// to each later room vertex in turn (<see cref="Lifestone.Data.Physics.World.PhysicsWorld.SweepViewer"/>, the transition the confirmer's wall check runs) until one is stopped. The
    /// next vertex is always allowed (the confirmer confirms the route itself). Without a confirmer the table is empty and the cell test alone decides.
    /// </summary>
    private void SweepRooms(NavPath path)
    {
        _sweptCount = 0;
        _dungeonRooms = _rooms is { ExitsToOutside.Count: 0 };
        var waypoints = path.Waypoints;
        if (_rooms is not null)
        {
            if (!ReferenceEquals(_cellShapesOf, _rooms))
            {
                _cellShapes.Clear(); // a new graph (a cell left out): its own cells
                _cellShapesOf = _rooms;
            }

            for (var i = 0; i < waypoints.Count; i++)
            {
                if (waypoints[i].Indoor)
                {
                    InCell(waypoints[i].At.CellId, waypoints[i].At.Origin); // the shapes of the route's cells, cached before the first step
                }
            }
        }

        if (_confirmer is null || _rooms is null || waypoints.Count < 3)
        {
            return;
        }

        if (_sweptTo.Length < waypoints.Count)
        {
            _sweptTo = new int[Math.Max(waypoints.Count, _sweptTo.Length * 2)];
        }

        var world = _confirmer.World;
        var lift = new Vector3(0f, 0f, IndoorLineLiftMetres);
        for (var i = 0; i < waypoints.Count; i++)
        {
            _sweptTo[i] = Math.Min(i + 1, waypoints.Count - 1);
            if (!waypoints[i].Indoor)
            {
                continue;
            }

            var from = new CellFrame(waypoints[i].At.CellId, new AcFrame(waypoints[i].At.Origin + lift, Quaternion.Identity));
            for (var j = i + 2; j < waypoints.Count && waypoints[j].Indoor; j++)
            {
                var to = new CellFrame(waypoints[j].At.CellId, new AcFrame(waypoints[j].At.Origin + lift, Quaternion.Identity));
                if (world.SweepViewer(from, to, NavCreatures.PlayerRadiusMetres).Blocked)
                {
                    break;
                }

                _sweptTo[i] = j;
            }
        }

        _sweptCount = waypoints.Count;
    }

    /// <summary>The lattice's clearance in nodes at <paramref name="point"/> (0 off the attached tiles or on a blocked node).</summary>
    private int ClearanceAt(NavPoint point) => _paths.Map is { } map && map.TryGetNode(point, out var node) ? node.Clearance : 0;

    /// <summary>The run's live scars into the pooled buffer the lattice's sight reads (none ignored: a line from the body never enters one); returns the count.</summary>
    private int CopyScars()
    {
        var n = Scars.CopyLive(ref _scarBuffer);
        if (_scarIgnored.Length < _scarBuffer.Length)
        {
            _scarIgnored = new bool[_scarBuffer.Length];
        }

        return n;
    }

    /// <summary>
    /// Whether the body at <paramref name="from"/> may run straight to route vertex <paramref name="to"/> past the vertices from <paramref name="next"/>:
    /// each skipped vertex within <see cref="SkipOffRouteMetres"/> of the line; the lattice's sight at the straightening's clearance
    /// (<see cref="PathPlanner.StraightenMinClearance"/>: the line is not confirmed by the physics) off water, slope and the run's scars; and no more of
    /// the creature layer than the route between.
    /// </summary>
    private bool CanRunStraightTo(NavRoute route, Vector2 from, int next, int to, int scars)
    {
        var end = route.Vertices[to].Global;
        for (var k = next; k < to; k++)
        {
            if (SegmentDistance(route.Vertices[k].Global, from, end) > SkipOffRouteMetres)
            {
                return false;
            }
        }

        if (!_paths.Map!.Sight(from, end, PathPlanner.StraightenMinClearance, NavFlags.Water | NavFlags.Slope | NavFlags.SteepEdge, _scarBuffer, _scarIgnored, scars))
        {
            return false;
        }

        if (Creatures.Count == 0)
        {
            return true;
        }

        var along = Creatures.ExposureAlong(from, route.Vertices[next].Global);
        for (var k = next; k < to; k++)
        {
            along += Creatures.ExposureAlong(route.Vertices[k].Global, route.Vertices[k + 1].Global);
        }

        return Creatures.ExposureAlong(from, end) <= along + PathPlanner.CreaturePullSlack;
    }

    /// <summary>P5a's pure pursuit (doorways and rooms): the lead point, never past the next doorway vertex, outdoors clamped to the farthest vertex the lattice sees.</summary>
    private NavPoint PursuitAim(NavRoute route, NavPoint here, in CellFrame position, float s, int next)
    {
        var lead = MathF.Max(LeadMinMetres, LeadSpeedFactor * _speed);
        var target = MathF.Min(s + lead, route.ArcLength);
        var waypoints = _path!.Waypoints;

        // Never past the next doorway vertex.
        for (var i = next; i < waypoints.Count; i++)
        {
            if (route.ArcLengthAt(i) > target)
            {
                break;
            }

            if (waypoints[i].Doorway)
            {
                target = route.ArcLengthAt(i);
                break;
            }
        }

        // Outdoors, the farthest vertex the lattice sees.
        var aim = route.PointAt(target);
        if (_paths.Map is { } map && LandDefs.IsOutdoorCell(position.CellId) && !map.LineOfSight(here, aim))
        {
            var seen = next;
            for (var i = next; i < waypoints.Count && route.ArcLengthAt(i) <= target; i++)
            {
                if (!waypoints[i].Indoor && map.LineOfSight(here, route.Vertices[i]))
                {
                    seen = i;
                }
            }

            aim = route.Vertices[Math.Min(seen, waypoints.Count - 1)];
        }

        return aim;
    }

    private void ClimbRung(double now, in CellFrame position, NavPoint here, float progress)
    {
        // A door the plan missed is the likeliest thing the body is pressed against: open it before any recovery move (M5 7 F2).
        if (Doors is not null && FindLateDoors(now, _route!.NearestArcLength(here)))
        {
            _lastProgressTime = now;
            return;
        }

        // M5 7 P7l (D141): stalled within use reach of the shut door ahead: Use it now (the door step's Use, its wait and its check that it opened), never a rung or a replan that walks into the same shut door.
        if (Doors is not null && UseDoorWhenStalled(now, position, here))
        {
            _lastProgressTime = now;
            return;
        }

        if (!_ladderActive)
        {
            _ladderActive = true;
            _ladderLevel = 0;
            _stuckPlans = 1;
            _ladderOrigin = progress;
            EpisodeRungs = 0;
        }

        _ladderLevel++;
        Rungs++;
        EpisodeRungs++;
        _audit?.RecordRung();
        _log.Info(LogCategory, "stuck: a rung", new LogField("name", Options.Name), new LogField("rung", _ladderLevel), new LogField("plan", _stuckPlans), new LogField("at", position.ToString()));
        _rungStart = now;
        _rungFrom = here.Global;
        _rungFromPatch = PatchHere() is not null;
        if (_ladderLevel <= 3 && RungMoveEntersPatch(position, here) is { } patch)
        {
            // M5 7 P7i (D144): the rung's move would carry the body into an avoid region: it moves nothing, and the ladder climbs on at the next stall.
            _log.Info(LogCategory, "stuck: the rung's move would enter an avoid region; it moves nothing (P7i)", new LogField("name", Options.Name), new LogField("rung", _ladderLevel), new LogField("region", patch));
            SetForward(false);
            SetTurn(0);
            EndRung(now);
            return;
        }

        switch (_ladderLevel)
        {
            case 1:
                _rung = Rung.BackOff;
                SetForward(false);
                SetTurn(0);
                SetBack(true);
                break;
            case 2:
                _rung = Rung.SidestepLeft;
                SetForward(false);
                SetTurn(0);
                SetSide(-1);
                break;
            case 3:
                _rung = Rung.JumpCharge;
                _rungLeftGround = false;
                SetTurn(0);
                SetForward(true);
                Send(_move.ChargeJump(ActionSource.Automation));
                break;
            default:
                if (_stuckPlans >= MaxStuckPlans)
                {
                    var nearest = _confirmer?.NearestStatic(position) ?? "no physics world to name the nearest static";
                    End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"stuck after {_stuckPlans} plans and {Rungs} rungs; nearest {nearest}"), now);
                    return;
                }

                // The stuck node: the route's node a metre past the furthest progress (where the body pushes), and a metre round it.
                var route = _route!;
                var stuck = route.PointAt(MathF.Min(route.ArcLength, MathF.Max(0f, route.NearestArcLength(here)) + 1f));
                if (_paths.Map is { } map && map.TryGetNode(stuck, out var node))
                {
                    stuck = node.Centre;
                }

                Scars.Add(stuck, StuckScarMetres);
                _stuckPlans++;
                _ladderLevel = 0;
                if (Plan(now, position, replan: true, "stuck"))
                {
                    _ladderOrigin = 0f;
                }

                break;
        }
    }

    private void StepRung(double now, Vector2 global)
    {
        var gone = Vector2.Distance(global, _rungFrom);
        var timedOut = now - _rungStart >= RungMoveSeconds;
        var patch = _rungFromPatch ? null : PatchHere(); // a move begun inside a region may cross it on the way out
        if (gone > RungMoveMaxMetres || patch is not null)
        {
            // M5 7 P7i (D144): a rung's move is bounded (RungMoveMaxMetres) and never carries the body into an avoid region: its keys go up and the rung ends.
            _log.Info(
                LogCategory,
                patch is null ? "stuck: the rung's move reached its bound; its keys go up (P7i)" : "stuck: the rung's move entered an avoid region; its keys go up (P7i)",
                new LogField("name", Options.Name),
                new LogField("rung", _rung.ToString()),
                new LogField("gone_m", gone.ToString("F1", CultureInfo.InvariantCulture)),
                new LogField("region", patch ?? "none"));
            SetForward(false);
            SetBack(false);
            SetSide(0);
            EndRung(now);
            return;
        }
        switch (_rung)
        {
            case Rung.BackOff:
                if (gone >= BackOffMetres || timedOut)
                {
                    SetBack(false);
                    EndRung(now);
                }

                break;
            case Rung.SidestepLeft:
                if (gone >= SidestepLeftMetres || timedOut)
                {
                    _rung = Rung.SidestepRight;
                    _rungStart = now;
                    _rungFrom = global;
                    SetSide(1);
                }

                break;
            case Rung.SidestepRight:
                if (gone >= SidestepRightMetres || timedOut)
                {
                    SetSide(0);
                    EndRung(now);
                }

                break;
            case Rung.JumpCharge:
                if (now - _rungStart >= JumpCharge.FillSeconds)
                {
                    Send(_move.Jump(ActionSource.Automation));
                    _rung = Rung.JumpAir;
                    _rungStart = now;
                }

                break;
            case Rung.JumpAir:
                _rungLeftGround |= !_body.OnGround;
                if ((_rungLeftGround && _body.OnGround) || timedOut)
                {
                    EndRung(now);
                }

                break;
        }
    }

    /// <summary>M5 7 P7i (D144): the avoid region a rung's move from <paramref name="here"/> would end in (<see cref="RungMoveMaxMetres"/> along the move: back, left, or ahead for the jump), or null.</summary>
    private string? RungMoveEntersPatch(in CellFrame position, NavPoint here)
    {
        if ((_rooms is not { } rooms || (rooms.Patches.Count == 0 && rooms.Plates is null) || here.LandblockId != rooms.LandblockId) && _keep is null)
        {
            return null;
        }

        var radians = RetailFrames.GetHeading(position.Frame) * (Math.PI / 180.0);
        var ahead = new Vector2((float)Math.Sin(radians), (float)Math.Cos(radians));
        var way = _ladderLevel switch
        {
            1 => -ahead,
            2 => new Vector2(-ahead.Y, ahead.X), // left of the heading (the side step left; the right one comes back across)
            _ => ahead,
        };
        if (_keep?.FirstMeeting(here.Global, here.Z, here.Global + (way * RungMoveMaxMetres), here.Z) is { } portal)
        {
            return portal.Name; // M6 7 AI: a kept portal is an avoid region to a rung's move, by the same outward rule
        }

        if (_rooms is { } walled && here.LandblockId == walled.LandblockId && walled.Plates?.FirstMeeting(here.Local, here.Local + new Vector3(way * RungMoveMaxMetres, 0f)) is { } plate)
        {
            return plate.Name; // M6 14 holding plates: no rung's move steps into a plate's wall (a body inside one moves out)
        }

        if (_rooms is not { } patched || patched.Patches.Count == 0 || here.LandblockId != patched.LandblockId)
        {
            return null;
        }

        // A body already in a region moves as it must to get out; one outside never moves in.
        return patched.Patches.Contains(here.Local) is null ? patched.Patches.Contains(here.Local + new Vector3(way * RungMoveMaxMetres, 0f)) : null;
    }

    /// <summary>M5 7 P7i (D144): the avoid region holding the body now, or null (no room graph, no patch, another landblock).</summary>
    private string? PatchHere()
    {
        if (_rooms is not { } rooms || rooms.Patches.Count == 0)
        {
            return null;
        }

        var at = NavMap.FromCellFrame(_body.Position);
        return at.LandblockId == rooms.LandblockId ? rooms.Patches.Contains(at.Local) : null;
    }

    private void EndRung(double now)
    {
        _rung = Rung.None;
        _lastProgressTime = now;
        _bestProgress = _route is { } route ? route.NearestArcLength(NavMap.FromCellFrame(_body.Position)) : _bestProgress;
    }

    private NavRunState? CheckIntents(double now)
    {
        for (var i = _inFlight.Count - 1; i >= 0; i--)
        {
            var handle = _inFlight[i];
            if (!handle.IsCompleted)
            {
                continue;
            }

            _inFlight.RemoveAt(i);
            if (handle.Outcome is { } outcome && outcome.Kind is ActionOutcomeKind.DryRun or ActionOutcomeKind.Refused or ActionOutcomeKind.Cancelled or ActionOutcomeKind.NotImplemented
                && !IsDone)
            {
                _inFlight.Clear();
                return End(NavRunState.Blocked, string.Create(CultureInfo.InvariantCulture, $"the movement intent '{handle.Summary}' ended {outcome.Kind}: {outcome.Detail}"), now);
            }
        }

        return null;
    }

    private void OnHumanKey(string key)
    {
        if (State is NavRunState.Idle || IsDone)
        {
            return;
        }

        // The human's key was applied before it was reported (MovementBindings.OnInputAction). The controller's command lists are stacks (CommandList.Add
        // never dedupes, Remove takes the newest entry), so the runner releases EVERY key it holds, the pressed one included: its own entry for that
        // key would otherwise outlive the person's press and resume as the head when the person lets go.
        ReleaseAll();

        _rung = Rung.None;
        State = NavRunState.Interrupted;
        FailureReason = Reason("a person pressed " + key + " (the human's key wins)");
        _endTime = _lastTime;
        _log.Info(LogCategory, "interrupted by the human's key", new LogField("name", Options.Name), new LogField("key", key));
    }

    private NavRunState End(NavRunState state, string? why, double now)
    {
        if (state is not NavRunState.Arrived || Options.Stop || _goal?.IsObject == true)
        {
            ReleaseAll();
        }

        _rung = Rung.None;
        State = state;
        _endTime = now;
        FailureReason = why is null ? null : Reason(why);
        _log.Write(
            state is NavRunState.Blocked or NavRunState.Failed ? LogSeverity.Warning : LogSeverity.Info,
            LogCategory,
            "run ended " + state + " " + VerdictFields() + " " + SteeringFields(),
            [new LogField("name", Options.Name), new LogField("why", FailureReason ?? string.Empty), new LogField("keys", KeyChanges), new LogField("keys_per_s_max", MaxKeysPerSecond)]);
        return state;
    }

    private string Reason(string why)
    {
        var p = _body.Position;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"run '{Options.Name}' to {(_goal is null ? "nothing" : Describe(_goal))} plan {Plans} rung {Rungs}: {why}; player in cell 0x{p.CellId:X8} at ({p.Origin.X:F2}, {p.Origin.Y:F2}, {p.Origin.Z:F2})");
    }

    private void Send(ActionHandle handle)
    {
        KeyChanges++;
        if (_pDriving)
        {
            PredictKeyChanges++;
        }

        _keyTimes.Enqueue(_lastTime);
        while (_keyTimes.Count > 0 && _lastTime - _keyTimes.Peek() >= 1.0)
        {
            _keyTimes.Dequeue();
        }

        MaxKeysPerSecond = Math.Max(MaxKeysPerSecond, _keyTimes.Count);
        if (!handle.IsCompleted || handle.Outcome?.Kind is not ActionOutcomeKind.Local)
        {
            _inFlight.Add(handle);
        }
    }

    private void SetForward(bool on)
    {
        if (on != _forward)
        {
            if (on && _lastTime - _forwardOffAt <= ForwardRepressCountSeconds)
            {
                ForwardRepresses++;
            }

            if (!on)
            {
                EndStraightRun();
                _forwardOffAt = _lastTime;
                if (_turn != 0)
                {
                    PressedInPlace();
                }
            }

            _forward = on;
            _forwardSince = _lastTime;
            Send(_move.Command(MotionCommands.WalkForward, on, ActionSource.Automation));
        }
    }

    private void SetBack(bool on)
    {
        if (on != _back)
        {
            _back = on;
            Send(_move.Command(MotionCommands.WalkBackwards, on, ActionSource.Automation));
        }
    }

    /// <summary>
    /// The wall touches (<see cref="WallTouches"/>, <see cref="ObjectTouches"/>, <see cref="ContactSeconds"/>): a step of a running run, the body moving under its keys, that a wall or
    /// an object stopped (<see cref="INavBody.Contact"/>). The log names the place, the heading, the keys and which steering held them.
    /// </summary>
    private void NoteContact(double now, double dt, in CellFrame position, float heading)
    {
        var contact = _body.Contact;
        if (State != NavRunState.Running || contact is not (NavBodyContact.Wall or NavBodyContact.Object) || (!_forward && _turn == 0 && _speed < 0.3f))
        {
            return;
        }

        if (dt > 0 && dt < 0.25)
        {
            ContactSeconds += dt;
        }

        var fresh = now - _contactAt > ContactGapSeconds;
        _contactAt = now;
        if (!fresh)
        {
            return;
        }

        if (contact == NavBodyContact.Wall)
        {
            WallTouches++;
        }
        else
        {
            ObjectTouches++;
        }

        if (WallTouches + ObjectTouches <= ContactLogLimit)
        {
            _log.Info(
                LogCategory,
                contact == NavBodyContact.Wall ? "the body touched a wall (report-only)" : "the body touched an object (report-only)",
                new LogField("name", Options.Name),
                new LogField("at", position.ToString()),
                new LogField("heading", heading.ToString("F0", CultureInfo.InvariantCulture)),
                new LogField("speed_mps", _speed.ToString("F2", CultureInfo.InvariantCulture)),
                new LogField("keys", string.Create(CultureInfo.InvariantCulture, $"turn={_turn} forward={_forward}")),
                new LogField("steering", _rung != Rung.None ? "rung" : _predictiveHeld ? "predictive" : "ordinary"),
                new LogField("rung", _rung.ToString()),
                new LogField("progress_m", _steerProgress.ToString("F1", CultureInfo.InvariantCulture)),
                new LogField("object", contact == NavBodyContact.Object ? $"0x{_body.ContactObject:X8}" : "none"),
                new LogField("normal", string.Create(CultureInfo.InvariantCulture, $"({_body.ContactNormal.X:F2},{_body.ContactNormal.Y:F2},{_body.ContactNormal.Z:F2})")));
        }
    }

    private double _contactAt = double.NegativeInfinity;
    private bool _predictiveHeld;
    private double _turnPressedAt;

    private void SetTurn(int turn)
    {
        if (turn == _turn)
        {
            return;
        }

        if (_turn != 0)
        {
            _releasedTurn = _turn;
            _releasedCorner = _pressCorner;
            _turnReleasedAt = _lastTime;
            if (_lastTime - _turnPressedAt < ShortTapSeconds)
            {
                ShortTaps++;
            }

            Send(_move.Command(_turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, false, ActionSource.Automation));
        }

        _turn = turn;
        if (turn != 0)
        {
            EndStraightRun();
            Turns++;
            _turnPressedAt = _lastTime;
            CountPress(turn);
            Send(_move.Command(turn > 0 ? MotionCommands.TurnRight : MotionCommands.TurnLeft, true, ActionSource.Automation));
        }
    }

    /// <summary>The steering counters of a turn press (D134, report-only): a reversal or a flip of the last release, a swing's reversals, an in-place press.</summary>
    private void CountPress(int turn)
    {
        _pressInPlace = false;
        _pressCorner = _steerCorner;
        if (_pressCorner != 0 && _steerCornerRunning)
        {
            PredictCornerTurns++; // the predictive steering's turn onto the next leg (NavRunnerPredictive), not D134's in place
        }
        else if (_pressCorner != 0)
        {
            CornerTurns++;
        }

        var gap = _lastTime - _turnReleasedAt;
        if (_releasedTurn != 0 && turn != _releasedTurn && gap <= ReversalCountSeconds)
        {
            TurnReversals++;
            SteeringTally.AddTurnReversal();
            if (_pressCorner != 0 && _releasedCorner != 0 && _pressCorner != _releasedCorner)
            {
                CornerReversals++;
            }

            if (gap < FlipCountSeconds)
            {
                TurnFlips++;
                SteeringTally.AddTurnFlip();
            }

            if (_lastTime - _swingFrom <= SwingWindowSeconds && ++_swingReversals == 2)
            {
                Swings++;
                SteeringTally.AddSwing();
                LogSwing();
                _swingFrom = double.NegativeInfinity;
            }
        }

        if (!_forward)
        {
            PressedInPlace();
        }
    }

    /// <summary>The held turn press turns in place (forward is up): counted once a press, and a swing's window opens.</summary>
    private void PressedInPlace()
    {
        if (_pressInPlace)
        {
            return;
        }

        _pressInPlace = true;
        InPlacePresses++;
        _swingFrom = _lastTime;
        _swingReversals = 0;
    }

    /// <summary>A swing's line (D134, report-only, step 4): filled in with the swing logging.</summary>
    private void LogSwing()
    {
        var p = _body.Position;
        var route = _route;
        var aimAt = _path is { } path && _aimVertex < path.Waypoints.Count && path.Waypoints[_aimVertex].At is var w
            ? string.Create(CultureInfo.InvariantCulture, $"0x{w.CellId:X8} [{w.Origin.X:F2} {w.Origin.Y:F2} {w.Origin.Z:F2}]")
            : "none";
        _log.Info(
            LogCategory,
            "swing: a turn in place, then two reversals within 3 s (D134, report-only)",
            new LogField("name", Options.Name),
            new LogField("at", string.Create(CultureInfo.InvariantCulture, $"0x{p.CellId:X8} [{p.Origin.X:F2} {p.Origin.Y:F2} {p.Origin.Z:F2}]")),
            new LogField("heading", RetailFrames.GetHeading(p.Frame).ToString("F0", CultureInfo.InvariantCulture)),
            new LogField("progress_m", _steerProgress.ToString("F1", CultureInfo.InvariantCulture)),
            new LogField("route_m", (route?.ArcLength ?? 0f).ToString("F1", CultureInfo.InvariantCulture)),
            new LogField("next", _lastNext),
            new LogField("aim", _aimVertex),
            new LogField("aim_at", aimAt),
            new LogField("corner", _cornerActive ? (_cornerTurning ? "turning" : "approach") : "none"),
            new LogField("reversals", TurnReversals),
            new LogField("flips", TurnFlips));
    }

    private void SetSide(int side)
    {
        if (side == _side)
        {
            return;
        }

        if (_side != 0)
        {
            Send(_move.Command(_side > 0 ? MotionCommands.SideStepRight : MotionCommands.SideStepLeft, false, ActionSource.Automation));
        }

        _side = side;
        if (side != 0)
        {
            Send(_move.Command(side > 0 ? MotionCommands.SideStepRight : MotionCommands.SideStepLeft, true, ActionSource.Automation));
        }
    }

    /// <summary>Runs (true) or walks (false) whatever the run-by-default option is (<see cref="MoveIntents.SetRun"/>).</summary>
    private void SetRun(bool run)
    {
        var down = run != _move.RunByDefault;
        if (down != _runKey)
        {
            _runKey = down;
            Send(_move.SetRun(run, ActionSource.Automation));
        }
    }

    private void SetRunKey(bool down)
    {
        if (down != _runKey)
        {
            _runKey = down;
            Send(_move.HoldRun(down, ActionSource.Automation));
        }
    }

    private void ReleaseAll()
    {
        SetForward(false);
        SetBack(false);
        SetTurn(0);
        SetSide(0);
        SetRunKey(false);
    }
}
