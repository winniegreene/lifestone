using Lifestone.Data.World;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>
/// What the runner (<c>NavRunner</c>, P5a design 3.2) reads of the player's body: a small read-only view over
/// <c>PlayerMovementController</c> that both hosts share (the Godot play host and the headless <c>OfflineWalker</c>), so the
/// runner has no engine and no controller type in its signature. Writes never go through it: the runner moves the
/// player only through <c>IActionApi.Move</c> (the human's keys, R2). Game thread only; every member is a plain read.
/// </summary>
public interface INavBody
{
    /// <summary>The player's cell and frame after the last physics step.</summary>
    CellFrame Position { get; }

    /// <summary>The player's cell id (<see cref="Position"/>'s <c>CellId</c>); 0 before the body is placed.</summary>
    uint CellId { get; }

    /// <summary>True while the player stands on walkable ground (retail Contact and OnWalkable): a jump or a fall is false.</summary>
    bool OnGround { get; }

    /// <summary>
    /// True while the server is driving the player: retail's <c>controlled_by_server</c> with a MoveTo or TurnTo under way
    /// (<c>CPhysicsObj::IsMovingTo</c>, 11.6096 0x0050F5E0), e.g. ACE's own melee or Use approach. While it is true a movement
    /// key would take control from the server (<c>TakeControlFromServer</c>), so the runner presses nothing (state Yielding).
    /// </summary>
    bool ServerDriving { get; }

    /// <summary>
    /// How the last server MoveTo or TurnTo ended (<c>PlayerMovementController.LastServerMoveToEnd</c>: <c>arrived</c>, <c>cancelled (0xNNNN Name)</c> or <c>stuck</c>, the
    /// controller's <c>TODO(m5-moveto-stuck)</c> rule); null while one is under way, before the first, or for a body that does not say (M5 item 5 F6).
    /// </summary>
    string? ServerMoveToEnd => null;

    /// <summary>
    /// M6 14 pits: the horizontal flight of the player's full-bar running jump on level ground, in metres (<see cref="Routes.JumpReach.FlightMetres(Lifestone.Data.Physics.Objects.IPhysicsWeenie)"/>
    /// over the body's own weenie: its Run and Jump skills, buffs counted); null for a body that cannot tell, which makes no jump.
    /// </summary>
    float? JumpReachMetres => null;

    /// <summary>
    /// M6 14 pits: the jump bar's level now, 0 to 1, while the jump key is held (<see cref="JumpCharge.GetPowerBarLevel"/>), 0 when no jump is charged; null for a body that cannot tell. The runner lets the key go only on a full bar.
    /// </summary>
    double? JumpBarLevel => null;

    /// <summary>
    /// M6 14 holding plates: the player's run speed, metres a second (retail's <c>get_max_speed</c>: the run animation's speed times the weenie's run rate, its Run skill with the buffs and its load); null for a body that cannot tell.
    /// The plates' walls take her stop from it (<see cref="PlateKeepOff.StopAt"/>).
    /// </summary>
    float? RunSpeedMetresPerSecond => null;

    /// <summary>
    /// M6 14 holding plates: true while the charged jump is a standing long jump (retail's <c>standing_longjump</c>: the jump key went down while she stood still, so held run forward does not move her until the release); false when
    /// it is not; null for a body that cannot tell. The runner's jump over plates presses forward only when it is true.
    /// </summary>
    bool? StandingLongJump => null;

    /// <summary>
    /// The body's top run speed in metres a second (retail <c>CMotionInterp::get_max_speed</c>, 11.6096 0x005288C0: 4 times the run rate), or NaN for a body that does not say: the runner's
    /// turn-curve check (<c>NavRunner.CurveClear</c>) predicts the curve a run started off its line sweeps.
    /// </summary>
    float RunSpeed => float.NaN;

    /// <summary>
    /// The body's turn rate in degrees a second while a turn key is held, or NaN for a body that does not say: the predictive steering (<c>NavRunner.PredictiveSteer</c>) starts its
    /// motion model from it until it has measured the body's own turns.
    /// </summary>
    float TurnRate => float.NaN;

    /// <summary>
    /// The controller's clock in seconds (the latest time it was stepped to): the time base of the player's physics, the one
    /// the runner's stuck timers use so a headless run at any tick rate and a 30 Hz play run agree.
    /// </summary>
    double Now { get; }

    /// <summary>
    /// What stopped the body's last physics step (<c>PhysicsObj.LastTransitionObstacle</c>): a wall (a cell's walls, a building, a static), another object (a door, a body), a refused
    /// step, or nothing; <see cref="NavBodyContact.None"/> for a body that does not say. The runner counts and logs the touches (<c>NavRunner.WallTouches</c>), report-only.
    /// </summary>
    NavBodyContact Contact => NavBodyContact.None;

    /// <summary>The object of an <see cref="NavBodyContact.Object"/> contact (its id), 0 otherwise.</summary>
    uint ContactObject => 0;

    /// <summary>The contact's normal (the sliding normal, else the collision normal), zero when there is none: a wall's is level, a floor's or a ramp's points up.</summary>
    System.Numerics.Vector3 ContactNormal => default;
}

/// <summary>What stopped a body's last physics step (<see cref="INavBody.Contact"/>).</summary>
public enum NavBodyContact : byte
{
    /// <summary>Nothing: the step went where the keys took it (or the body does not say).</summary>
    None,

    /// <summary>The environment: a cell's walls, a building, a static, the terrain's edge (a collision or sliding normal with no object).</summary>
    Wall,

    /// <summary>Another physics object: a door, a body, a server object.</summary>
    Object,

    /// <summary>Only a refused step-up or edge: a sill or a brink the body cannot cross.</summary>
    Step,
}
